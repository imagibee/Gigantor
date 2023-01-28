using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.IO;

namespace Imagibee {
    namespace Gigantor {
        //
        // Determine if very large files are duplicates of one another
        //
        // To achieve this goal a background process partitions the files,
        // compares each partition, aborting as soon as any two partitions
        // don't match.
        //
        // Users should begin the process by calling Start with the path
        // of the files to check.
        //
        // After the process is finished the value of Identical indicates
        // if the files are identical.
        //
        // Performance can be tailored to a particular system by varying
        // the chunkSize and maxWorkers parameters.
        //
        public class DuplicateChecker {
            // true if the files are identical, otherwise false,
            // indeterminate while Running is true
            public bool Identical {
                get {
                    return Interlocked.Read(ref mismatches) == 0;
                }
                private set {
                    if (value == false) {
                        Interlocked.Exchange(ref mismatches, 1);
                    }
                    else {
                        Interlocked.Exchange(ref mismatches, 0);
                    }
                }
            }

            // True while index process is running
            public bool Running { get; private set; } = false;

            // The number of bytes that have been tested so far
            public long ByteCount { get { return Interlocked.Read(ref byteCount); } }

            // The error that caused the index process to end prematurely (if any)
            public string LastError { get; private set; } = "";

            // Create a new instance
            //
            // progress - signaled each time MatchCount is updated
            // chunkKiBytes - the chunk size in KiBytes that each worker works on
            // maxWorkers - optional limit to the maximum number of simultaneous workers
            public DuplicateChecker(AutoResetEvent progress, int chunkKiBytes=512, int maxWorkers=0)
            {
                if (chunkKiBytes < 1) {
                    chunkKiBytes = 1;
                }
                chunkSize = chunkKiBytes * 1024;
                this.maxWorkers = maxWorkers;
                this.progress = progress;
                synchronizer = new AutoResetEvent(false);
                byteCount = 0;
                mismatches = 1;
            }

            // Begin the indexing process in the background
            public void Start(string filePath1, string filePath2)
            {
                if (!Running) {
                    mismatches = 0;
                    LastError = "";
                    path1 = filePath1;
                    path2 = filePath2;
                    byteCount = 0;
                    Running = true;
                    scheduledChunks = 0;
                    chunkJobs = new ConcurrentQueue<ChunkJob>();
                    ThreadPool.QueueUserWorkItem((_) => ManageJobs());
                }
            }

            // Efficiently block until background work completes
            public void Wait()
            {
                while (Running) {
                    progress.WaitOne(1000);
                }
            }

            // Wait for multiple line checkers to all complete their background work
            //
            // checkers - a enumerable set of started checkers to wait for
            // OnProgressOrTimeout - called each time LineCount is updated, or at frequency determined by the timeout parameter
            // timeoutMilliSeconds - the time in milliseconds between callbacks
            public static void Wait(ICollection<DuplicateChecker> checkers, AutoResetEvent progress, Action<int> OnProgressOrTimeout, int timeoutMilliSeconds)
            {
                while (true) {
                    var runningCount = 0;
                    foreach (var checker in checkers) {
                        if (checker.Running) {
                            runningCount++;
                        }
                    }
                    if (runningCount == 0) {
                        break;
                    }
                    progress.WaitOne(timeoutMilliSeconds);
                    OnProgressOrTimeout(runningCount);
                }
            }

            void ManageJobs()
            {
                try {
                    FileInfo fileInfo1 = new(path1);
                    FileInfo fileInfo2 = new(path2);
                    // save some time if file lengths don't match
                    if (fileInfo1.Length == fileInfo2.Length) {
                        // Create all the chunk jobs (in order)
                        for (long pos = 0; pos < fileInfo1.Length; pos += chunkSize) {
                            chunkJobs.Enqueue(
                                new ChunkJob()
                                {
                                    StartFpos = pos,
                                });
                        }
                        // Work until the queues are empty
                        while (chunkJobs.Count != 0 || scheduledChunks != 0) {
                            ScheduleChunks();
                            synchronizer.WaitOne(1000);
                            if (Identical == false) {
                                chunkJobs.Clear();
                                break;
                            }
                        }
                    }
                    else {
                        Identical = false;
                    }
                }
                catch (Exception e) {
                    // In the background catch all exceptions, record the text
                    // for debugging, and abort the indexing process
                    LastError = e.ToString();
                }
                Running = false;
                progress.Set();
            }

            void ScheduleChunks()
            {
                while (scheduledChunks < maxWorkers || maxWorkers < 1) {
                    if (chunkJobs.TryDequeue(out ChunkJob chunkJob)) {
                        Interlocked.Add(ref scheduledChunks, 1);
                        ThreadPool.QueueUserWorkItem((_) => MapChunk(chunkJob));
                    }
                    else {
                        break;
                    }
                }
            }

            void MapChunk(ChunkJob chunk)
            {
                try {
                    using var fileStream1 = new FileStream(
                        path1,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        chunkSize,
                        FileOptions.Asynchronous);
                    fileStream1.Seek(chunk.StartFpos, SeekOrigin.Begin);
                    var streamReader1 = new BinaryReader(fileStream1, System.Text.Encoding.UTF8, true);
                    var buf1 = streamReader1.ReadBytes(chunkSize);
                    using var fileStream2 = new FileStream(
                        path2,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        chunkSize,
                        FileOptions.Asynchronous);
                    fileStream2.Seek(chunk.StartFpos, SeekOrigin.Begin);
                    var streamReader2 = new BinaryReader(fileStream2, System.Text.Encoding.UTF8, true);
                    var buf2 = streamReader2.ReadBytes(chunkSize);
                    if (!Utilities.UnsafeIsEqual(buf1, buf2)) {
                        if (Identical) {
                            Identical = false;
                        }
                    }
                    Interlocked.Add(ref byteCount, buf1.Length);
                }
                catch (Exception e) {
                    // In the background catch all exceptions, record the text
                    // for debugging, and abort the indexing process
                    LastError = e.ToString();
                }
                Interlocked.Add(ref scheduledChunks, -1);
                synchronizer.Set();
                progress.Set();
            }

            // private types
            struct ChunkJob {
                public long StartFpos;
            };

            // private data
            ConcurrentQueue<ChunkJob> chunkJobs;
            readonly AutoResetEvent synchronizer;
            readonly AutoResetEvent progress;
            // Paths of the files being compared
            string path1;
            string path2;
            readonly int chunkSize;
            readonly int maxWorkers;
            long mismatches;
            int scheduledChunks;
            long byteCount;
        }
    }
}