﻿using System.IO;
using Raven.Client.Documents.Replication.Messages;
using Sparrow.Json;
using Voron;

namespace Raven.Server.Documents.Replication
{
    public class ReplicationBatchIndexItem
    {
        public string Name;
        public ChangeVectorEntry[] ChangeVector;
        public BlittableJsonReaderObject Definition;
        public long Etag;
        public int Type;
    }

    public class ReplicationBatchItem
    {
        public LazyStringValue Key;
        public long Etag;
        public short TransactionMarker;

        #region Document

        public ChangeVectorEntry[] ChangeVector;
        public BlittableJsonReaderObject Data;
        public LazyStringValue Collection;
        public DocumentFlags Flags;
        public long LastModifiedTicks;

        #endregion

        #region Attachment

        public ReplicationItemType Type;
        public LazyStringValue Name;
        public LazyStringValue ContentType;
        public Slice Base64Hash;
        public Stream Stream;
        
        #endregion

        public static ReplicationBatchItem From(Document doc)
        {
            return new ReplicationBatchItem
            {
                Type = ReplicationItemType.Document,
                Etag = doc.Etag,
                ChangeVector = doc.ChangeVector,
                Data = doc.Data,
                Key = doc.Key,
                Flags = doc.Flags,
                TransactionMarker = doc.TransactionMarker,
                LastModifiedTicks = doc.LastModified.Ticks,
            };
        }

        public static ReplicationBatchItem From(DocumentTombstone doc)
        {
            var item = new ReplicationBatchItem
            {
                Etag = doc.Etag,
                Key = doc.LoweredKey,
                TransactionMarker = doc.TransactionMarker,
            };

            if (doc.Type == DocumentTombstone.TombstoneType.Document)
            {
                item.Type = ReplicationItemType.Document;
                item.ChangeVector = doc.ChangeVector;
                item.Collection = doc.Collection;
                item.Flags = doc.Flags;
                item.LastModifiedTicks = doc.LastModified.Ticks;
            }
            else
            {
                item.Type = ReplicationItemType.AttachmentTombstone;
            }

            return item;
        }

        public static ReplicationBatchItem From(DocumentConflict doc)
        {
            return new ReplicationBatchItem
            {
                Type = ReplicationItemType.Document,
                Etag = doc.Etag,
                ChangeVector = doc.ChangeVector,
                Collection = doc.Collection,
                Data = doc.Doc,
                Key = doc.Key,
                LastModifiedTicks = doc.LastModified.Ticks,
                TransactionMarker = -1// not relevant
            };
        }

        public static ReplicationBatchItem From(Attachment attachment)
        {
            return new ReplicationBatchItem
            {
                Type = ReplicationItemType.Attachment,
                Key = attachment.LoweredKey,
                Etag = attachment.Etag,
                Name = attachment.Name,
                ContentType = attachment.ContentType,
                Base64Hash = attachment.Base64Hash,
                Stream = attachment.Stream,
                TransactionMarker = attachment.TransactionMarker,
            };
        }

        public enum ReplicationItemType : byte
        {
            Document = 1,
            Attachment = 2,
            AttachmentStream = 3,
            AttachmentTombstone = 4,
        }
    }
}