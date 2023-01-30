using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.IO;

namespace Imagibee {
    namespace Gigantor {
        public class FileMapJoin<T> where T : IMapJoinData
        {
            // Path of the last successfully started indexing operation
            public string Path { get; private set; } = "";

            // True while index process is running
            public bool Running { get; private set; } = false;

            // The error that caused the index process to end prematurely (if any)
            public string Error { get; private set; } = "";

            // Create a new instance
            //
            // progress - signaled each time MatchCount is updated
            // chunkKiBytes - the chunk size in KiBytes that each worker works on
            // maxWorkers - optional limit to the maximum number of simultaneous workers
            public FileMapJoin(
                AutoResetEvent progress,
                Func<FileJob, T> DoMap,
                Func<T, T, T> DoJoin,
                MapJoinOption options = MapJoinOption.None,
                int chunkKiBytes = 512,
                int maxWorkers = 0)
            {
                if (chunkKiBytes < 1) {
                    chunkKiBytes = 1;
                }
                chunkSize = chunkKiBytes * 1024;
                this.maxWorkers = maxWorkers;
                this.progress = progress;
                this.DoMap = DoMap;
                this.DoJoin = DoJoin;
                this.options = options;
                synchronizer = new AutoResetEvent(false);
            }

            // Begin the indexing process in the background
            public void Start(string filePath)
            {
                if (!Running) {
                    Error = "";
                    Path = filePath;
                    Running = true;
                    priorIndex = -1;
                    scheduledChunks = 0;
                    resultQueue = new ConcurrentQueue<T>();
                    jobQueue = new ConcurrentQueue<FileJob>();
                    ThreadPool.QueueUserWorkItem((_) => ManageJobs(filePath));
                }
            }

            // Efficiently block until background work completes
            public void Wait()
            {
                while (Running) {
                    progress.WaitOne(1000);
                }
            }

            // Wait for multiple line indexers to all complete their background work
            //
            // indexers - a enumerable set of started indexers to wait for
            // OnProgressOrTimeout - called each time LineCount is updated, or at frequency determined by the timeout parameter
            // timeoutMilliSeconds - the time in milliseconds between callbacks
            public static void Wait(
                ICollection<FileMapJoin<T>> mapJoins,
                AutoResetEvent progress,
                Action<int> OnProgressOrTimeout,
                int timeoutMilliSeconds)
            {
                while (true) {
                    var runningCount = 0;
                    foreach (var mr in mapJoins) {
                        if (mr.Running) {
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

            void ManageJobs(string filePath)
            {
                try {
                    // Create all the chunk jobs (in order)
                    var chunkNum = 0;
                    FileInfo fileInfo = new(filePath);
                    //Logger.Log($"{filePath} is {fileInfo.Length} bytes");
                    for (long pos = 0; pos < fileInfo.Length; pos += chunkSize) {
                        jobQueue.Enqueue(
                            new FileJob()
                            {
                                Id = chunkNum++,
                                StartFpos = pos,
                            });
                    }
                    //Logger.Log($"{chunkJobs.Count} chunks");
                    // Work until the queues are empty
                    while (jobQueue.Count != 0 || resultQueue.Count != 0 || scheduledChunks != 0) {
                        ScheduleChunks();
                        synchronizer.WaitOne(1000);
                        JoinResults();
                    }
                }
                catch (Exception e) {
                    // In the background catch all exceptions, record the text
                    // for debugging, and abort the indexing process
                    Error = e.ToString();
                }
                Running = false;
                progress.Set();
            }

            void ScheduleChunks()
            {
                while (scheduledChunks < maxWorkers || maxWorkers < 1) {
                    if (jobQueue.TryDequeue(out FileJob job)) {
                        Interlocked.Add(ref scheduledChunks, 1);
                        ThreadPool.QueueUserWorkItem((_) => Map(job));
                        //Logger.Log($"scheduled chunk {chunkJob.Id}, currently {scheduledChunks} scheduled chunks");
                    }
                    else {
                        break;
                    }
                }
            }

            void JoinResults()
            {
                while (resultQueue.Count > 0) {
                    JoinNextResult();
                }
            }

            void JoinNextResult()
            {
                List<T> resultBuf = new();
                while (resultQueue.TryDequeue(out T result)) {
                    resultBuf.Add(result);
                }
                if (resultBuf.Count > 0) {
                    resultBuf.Sort((a, b) => a.Id.CompareTo(b.Id));
                    if ((options & MapJoinOption.ReducingJoins) != 0) {
                        for (var i = 0; i < resultBuf.Count - 1; i++) {
                            var result1 = resultBuf[i];
                            var result2 = resultBuf[i + 1];
                            if (result1.Id == result2.Id - 1 &&
                                result1.Cycle == result2.Cycle) {
                                resultQueue.Enqueue(DoJoin(result1, result2));
                                resultBuf.RemoveAt(i);
                                resultBuf.RemoveAt(i+1);
                            }
                        }
                    }
                    else {
                        var currentResult = resultBuf[0];
                        if (currentResult.Id == priorIndex + 1) {
                            DoJoin(currentResult, currentResult);
                            resultBuf.RemoveAt(0);
                        }
                    }
                    progress.Set();
                }
                foreach (var chunk in resultBuf) {
                    resultQueue.Enqueue(chunk);
                }
            }

            void Map(FileJob data)
            {
                try {
                    resultQueue.Enqueue(DoMap(data));
                }
                catch (Exception e) {
                    // In the background catch all exceptions, record the text
                    // for debugging, and abort the indexing process
                    Error = e.ToString();
                }
                Interlocked.Add(ref scheduledChunks, -1);
                synchronizer.Set();
            }

            // private types
            public struct FileJob : IMapJoinData {
                public int Id { get; set; }
                public int Cycle { get; set; }
                public long StartFpos { get; set; }
            };

            // private data
            ConcurrentQueue<T> resultQueue;
            ConcurrentQueue<FileJob> jobQueue;
            readonly AutoResetEvent synchronizer;
            readonly AutoResetEvent progress;
            Func<FileJob, T> DoMap;
            Func<T, T, T> DoJoin;
            readonly MapJoinOption options;
            readonly int chunkSize;
            readonly int maxWorkers;
            int priorIndex;
            int scheduledChunks;
        }
    }
}