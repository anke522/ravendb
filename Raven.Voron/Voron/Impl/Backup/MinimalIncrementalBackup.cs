﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Voron.Impl.Journal;
using Voron.Impl.Paging;
using Voron.Util;

namespace Voron.Impl.Backup
{
	public unsafe class MinimalIncrementalBackup
	{
		public void ToFile(StorageEnvironment env, string backupPath, CompressionLevel compression = CompressionLevel.Optimal, Action<string> infoNotify = null,
			Action backupStarted = null)
		{
			if (env.Options.IncrementalBackupEnabled == false)
				throw new InvalidOperationException("Incremental backup is disabled for this storage");

			var pageNumberToPageInScratch = new Dictionary<long, long>();
			if (infoNotify == null)
				infoNotify = str => { };
			var toDispose = new List<IDisposable>();
			try
			{
				IncrementalBackupInfo backupInfo;
				long lastWrittenLogPage = -1;
				long lastWrittenLogFile = -1;

				using (var txw = env.NewTransaction(TransactionFlags.ReadWrite))
				{
					backupInfo = env.HeaderAccessor.Get(ptr => ptr->IncrementalBackup);

					if (env.Journal.CurrentFile != null)
					{
						lastWrittenLogFile = env.Journal.CurrentFile.Number;
						lastWrittenLogPage = env.Journal.CurrentFile.WritePagePosition;
					}

					//txw.Commit(); - intentionally not committing
				}

				if (backupStarted != null)
					backupStarted();

				infoNotify("Voron - reading storage journals for snapshot pages");

				var lastBackedUpFile = backupInfo.LastBackedUpJournal;
				var lastBackedUpPage = backupInfo.LastBackedUpJournalPage;
				var firstJournalToBackup = backupInfo.LastBackedUpJournal;

				if (firstJournalToBackup == -1)
					firstJournalToBackup = 0; // first time that we do incremental backup

				var lastTransaction = new TransactionHeader { TransactionId = -1 };

				var recoveryPager = env.Options.CreateScratchPager("min-inc-backup.scratch");
				toDispose.Add(recoveryPager);
				int recoveryPage = 0;
				for (var journalNum = firstJournalToBackup; journalNum <= backupInfo.LastCreatedJournal; journalNum++)
				{
					lastBackedUpFile = journalNum;
					var journalFile = IncrementalBackup.GetJournalFile(env, journalNum, backupInfo);
					try
					{
						using (var filePager = env.Options.OpenJournalPager(journalNum))
						{
							var reader = new JournalReader(filePager, recoveryPager, 0, null, recoveryPage);
							reader.MaxPageToRead = lastBackedUpPage = journalFile.JournalWriter.NumberOfAllocatedPages;
							if (journalNum == lastWrittenLogFile) // set the last part of the log file we'll be reading
								reader.MaxPageToRead = lastBackedUpPage = lastWrittenLogPage;

							if (lastBackedUpPage == journalFile.JournalWriter.NumberOfAllocatedPages) // past the file size
							{
								// move to the next
								lastBackedUpPage = -1;
								lastBackedUpFile++;
							}

							if (journalNum == backupInfo.LastBackedUpJournal) // continue from last backup
								reader.SetStartPage(backupInfo.LastBackedUpJournalPage + 1);
							TransactionHeader* lastJournalTxHeader = null;
							while (reader.ReadOneTransaction(env.Options))
							{
								// read all transactions here 
								lastJournalTxHeader = reader.LastTransactionHeader;
							}

							if (lastJournalTxHeader != null)
								lastTransaction = *lastJournalTxHeader;

							recoveryPage = reader.RecoveryPage;

							foreach (var pagePosition in reader.TransactionPageTranslation)
							{
								var pageInJournal = pagePosition.Value.JournalPos;
								var page = recoveryPager.Read(pageInJournal);
								pageNumberToPageInScratch[pagePosition.Key] = pageInJournal;
								if (page.IsOverflow)
								{
									var numberOfOverflowPages = recoveryPager.GetNumberOfOverflowPages(page.OverflowSize);
									for (int i = 1; i < numberOfOverflowPages; i++)
										pageNumberToPageInScratch.Remove(page.PageNumber + i);
								}
							}
						}
					}
					finally
					{
						journalFile.Release();	
					}
				}

				if (pageNumberToPageInScratch.Count == 0)
				{
					infoNotify("Voron - no changes since last backup, nothing to do");
					return;
				}

				infoNotify("Voron - started writing snapshot file.");

				if (lastTransaction.TransactionId == -1)
					throw new InvalidOperationException("Could not find any transactions in the journals, but found pages to write? That ain't right.");


				var finalPager = env.Options.CreateScratchPager("min-inc-backup-final.scratch");
				toDispose.Add(finalPager);
				finalPager.EnsureContinuous(null, 0,1);//txHeader
				int totalNumberOfPages = 0;
				int overflowPages = 0;
				int start = 1;
				foreach (var pageNum in pageNumberToPageInScratch.Values)
				{
					var p = recoveryPager.Read(pageNum);
					var size = 1;
					if (p.IsOverflow)
					{
						size = recoveryPager.GetNumberOfOverflowPages(p.OverflowSize);
						overflowPages += (size - 1);
					}
					totalNumberOfPages += size;
					finalPager.EnsureContinuous(null, start, size); //maybe increase size

					StdLib.memcpy(finalPager.AcquirePagePointer(start), p.Base, size * AbstractPager.PageSize);

					start += size;
				}

				//TODO: what happens when we have enough transactions here that handle more than 4GB? 
				//TODO: in this case, we need to split this into multiple merged transactions, of up to 2GB 
				//TODO: each

				var uncompressedSize = totalNumberOfPages * AbstractPager.PageSize;
				var outputBufferSize = LZ4.MaximumOutputLength(uncompressedSize);

				finalPager.EnsureContinuous(null, start,
					finalPager.GetNumberOfOverflowPages(outputBufferSize));

				var txPage = finalPager.AcquirePagePointer(0);
				StdLib.memset(txPage, 0, AbstractPager.PageSize);
				var txHeader = (TransactionHeader*)txPage;
				txHeader->HeaderMarker = Constants.TransactionHeaderMarker;
				txHeader->FreeSpace = lastTransaction.FreeSpace;
				txHeader->Root = lastTransaction.Root;
				txHeader->OverflowPageCount = overflowPages;
				txHeader->PageCount = totalNumberOfPages - overflowPages;
				txHeader->PreviousTransactionCrc = lastTransaction.PreviousTransactionCrc;
				txHeader->TransactionId = lastTransaction.TransactionId;
				txHeader->NextPageNumber = lastTransaction.NextPageNumber;
				txHeader->LastPageNumber = lastTransaction.LastPageNumber;
				txHeader->TxMarker = TransactionMarker.Commit | TransactionMarker.Merged;
				txHeader->Compressed = false;
				txHeader->UncompressedSize = txHeader->CompressedSize = uncompressedSize;

				txHeader->Crc = Crc.Value(finalPager.AcquirePagePointer(1), 0, totalNumberOfPages * AbstractPager.PageSize);

				using (var file = new FileStream(backupPath, FileMode.Create))
				{
					using (var package = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true))
					{
						var entry = package.CreateEntry(string.Format("{0:D19}.journal", lastBackedUpFile), compression);
						using (var stream = entry.Open())
						{
							var copier = new DataCopier(AbstractPager.PageSize * 16);
							copier.ToStream(finalPager.AcquirePagePointer(0), (totalNumberOfPages + 1) * AbstractPager.PageSize, stream);
						}
					}
					file.Flush(true);// make sure we hit the disk and stay there
				}

				env.HeaderAccessor.Modify(header =>
				{
					header->IncrementalBackup.LastBackedUpJournal = lastBackedUpFile;
					header->IncrementalBackup.LastBackedUpJournalPage = lastBackedUpPage;
				});
			}
			finally
			{
				foreach (var disposable in toDispose)
					disposable.Dispose();
			}
		}
	}
}
