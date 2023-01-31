using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.IO;

namespace Imagibee {
    namespace Gigantor {
        //
        // Abstract base class for chunk processing very large files
        //
        // The class dispatches threads that run in the background to
        // partition the file into chunks.  These chunks are mapped
        // to a T result by the implementation of the abstract Map
        // method.  Results are then joined by the implementation of
        // the abstract Join method according to the modes defined
        // in MapJoinMode.
        //
        // Begin the process by calling Start with the path of the
        // file to process.  All public methods and properties are
        // well behaved at any time.  Although, while Running is true
        // only partial results are available.
        //
        // Exceptions during the background processing are caught and
        // stored in Error.  Exceptions during Start are not handled.
        //
        public abstract class FileMapJoin<T> : IBackground where T : IMapJoinData
        {
            // Path of the last successfully started indexing operation
            public string Path { get; private set; } = "";

            // True while index process is running
            public bool Running { get; private set; } = false;

            // The error that caused the background process to end prematurely (if any)
            public string Error { get; private set; } = "";

            // Create a new instance
            //
            // filePath - the path to the file to process
            // progress - signaled each time MatchCount is updated
            // mapJoinMode - defines the map/join mode
            // chunkKiBytes - the chunk size in KiBytes that each worker works on
            // maxWorkers - optional limit to the maximum number of simultaneous workers
            public FileMapJoin(
                string filePath,
                AutoResetEvent progress,
                JoinMode mapJoinMode,
                int chunkKiBytes,
                int maxWorkers)
            {
                if (chunkKiBytes < 1) {
                    chunkKiBytes = 1;
                }
                Path = filePath;
                this.progress = progress;
                this.mapJoinMode = mapJoinMode;
                chunkSize = chunkKiBytes * 1024;
                this.maxWorkers = maxWorkers;
                synchronizer = new AutoResetEvent(false);
            }

            // Begin the processing in the background
            public virtual void Start()
            {
                if (!Running) {
                    Error = "";
                    scheduledChunks = 0;
                    resultQueue = new ConcurrentQueue<T>();
                    jobQueue = new ConcurrentQueue<FileMapJoinData>();
                    ThreadPool.QueueUserWorkItem((_) => ManageJobs(Path));
                    Running = true;
                }
            }


            //
            // PROTECTED INTERFACE
            //

            // Called by a background thread to map partition data to a T result
            protected abstract T Map(FileMapJoinData data);

            // Called by a background thread to join two results a and b
            //
            // The interpretation of a, b, and T depend on MapJoinOption
            protected abstract T Join(T a, T b);

            // When used from Join, this value contains the T result returned
            // from the prior call of Join
            protected T priorResult;

            // Partitioning size in bytes
            protected readonly int chunkSize;

            //
            // END OF PUBLIC AND PROTECTED INTERFACE
            //

            void ManageJobs(string filePath)
            {
                try {
                    // Create all the chunk jobs (in order)
                    var chunkNum = 0;
                    FileInfo fileInfo = new(filePath);
                    //Logger.Log($"{filePath} is {fileInfo.Length} bytes");
                    for (long pos = 0; pos < fileInfo.Length; pos += chunkSize) {
                        jobQueue.Enqueue(
                            new FileMapJoinData()
                            {
                                Id = chunkNum++,
                                StartFpos = pos,
                            });
                    }
                    //Logger.Log($"{jobQueue.Count} chunks");
                    // Work until the queues are empty
                    while (jobQueue.Count != 0 ||
                           resultQueue.Count != 0 ||
                           scheduledChunks != 0) {
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
                    if (jobQueue.TryDequeue(out FileMapJoinData job)) {
                        Interlocked.Add(ref scheduledChunks, 1);
                        ThreadPool.QueueUserWorkItem((_) => MapJob(job));
                        //Logger.Log($"scheduled chunk {job.Id}, currently {scheduledChunks} scheduled chunks");
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
                var progressMade = 0;
                List<T> resultBuf = new();
                while (resultQueue.TryDequeue(out T result)) {
                    resultBuf.Add(result);
                }
                if (resultBuf.Count > 0) {
                    resultBuf.Sort((a, b) => a.Id.CompareTo(b.Id));
                    if ((mapJoinMode & JoinMode.Exponential) != 0) {
                        throw new NotImplementedException();
                        //for (var i = 0; i < resultBuf.Count - 1; i++) {
                        //    var result1 = resultBuf[i];
                        //    var result2 = resultBuf[i + 1];
                        //    if (result1.Id == result2.Id - 1 &&
                        //        result1.Cycle == result2.Cycle) {
                        //        resultQueue.Enqueue(Join(result1, result2));
                        //        resultBuf.RemoveAt(i);
                        //        resultBuf.RemoveAt(i+1);
                        //        progressMade += 1;
                        //    }
                        //}
                    }
                    else if ((mapJoinMode & JoinMode.Linear) != 0) {
                        var currentResult = resultBuf[0];
                        if (currentResult.Id == 0) {
                            priorResult = Join(currentResult, currentResult);
                            resultBuf.RemoveAt(0);
                            progressMade = 1;
                        }
                        else if (currentResult.Id == priorResult.Id + 1) {
                            priorResult = Join(priorResult, currentResult);
                            resultBuf.RemoveAt(0);
                            progressMade = 1;
                        }
                    }
                    else { // MapJoinMode.None
                        progressMade = resultBuf.Count;
                        resultBuf.Clear();
                    }
                }
                foreach (var result in resultBuf) {
                    resultQueue.Enqueue(result);
                }
                if (progressMade != 0) {
                    Interlocked.Add(ref scheduledChunks, -progressMade);
                    progress.Set();
                }
            }

            void MapJob(FileMapJoinData data)
            {
                try {
                    resultQueue.Enqueue(Map(data));
                }
                catch (Exception e) {
                    // In the background catch all exceptions, record the text
                    // for debugging, and abort the indexing process
                    Error = e.ToString();
                }
                synchronizer.Set();
            }

            // private data
            readonly AutoResetEvent synchronizer;
            readonly AutoResetEvent progress;
            readonly JoinMode mapJoinMode;
            readonly int maxWorkers;
            ConcurrentQueue<FileMapJoinData> jobQueue;
            ConcurrentQueue<T> resultQueue;
            int scheduledChunks;
        }

        // FileMapJoin job data
        public struct FileMapJoinData : IMapJoinData {
            public int Id { get; set; }
            public int Cycle { get; set; }
            public long StartFpos { get; set; }
        };
    }
}