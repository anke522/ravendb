﻿using System;
using System.Diagnostics;
using FastTests.Client.Attachments;
using FastTests.Server.Replication;

namespace Tryouts
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine(Process.GetCurrentProcess().Id);
            Console.WriteLine();

            for (int i = 0; i < 100; i++)
            {
                Console.WriteLine(i);
                using (var a = new ReplicationConflictsTests())
                {
                    a.Conflict_insensitive_check();
                }
            }
            for (int i = 0; i < 100; i++)
            {
                Console.WriteLine(i);
                using (var a = new AttachmentsReplication())
                {
                    a.PutAttachments();
                }
            }

            for (int i = 0; i < 100; i++)
            {
                Console.WriteLine(i);
                using (var a = new ReplicationWithVersioning())
                {
                    a.CreateConflictAndResolveItIncreaseTheVersion().Wait();
                }
            }
        }
    }
}