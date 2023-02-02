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
        // to a T result by the implementation of the Map method.
        // Results are then joined by the implementation of the Join
        // method according to the modes defined in MapJoinMode.
        //
        // Begin the process by calling Start with the path of the
        // file to process.  All public methods and properties are
        // well behaved at any time.
        //
        // Exceptions during the background processing are caught and
        // stored in Error.  If Error is not empty results are undefined.
        // Exceptions during Start are not handled.
        //
        public abstract class FileMapJoin<T> : IBackground where T : IMapJoinData
        {
            // Path of the last successfully started indexing operation
            public string Path { get; private set; } = "";

            // True while background process is running
            public bool Running { get; private set; }

            //public bool Running {
            //    get {
            //        return Interlocked.Read(ref running) != 0;
            //    }
            //    private set {
            //        if (value == true) {
            //            Interlocked.Exchange(ref running, 1);
            //        }
            //        else {
            //            Interlocked.Exchange(ref running, 0);
            //        }
            //    }
            //}

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
                JoinMode joinMode,
                int chunkKiBytes,
                int maxWorkers=0,
                int overlap=0)
            {
                if (chunkKiBytes < 1) {
                    chunkKiBytes = 1;
                }
                Path = filePath;
                this.progress = progress;
                this.joinMode = joinMode;
                chunkSize = chunkKiBytes * 1024;
                this.maxWorkers = maxWorkers; //(maxWorkers == 1) ? 1:0;
                this.overlap = overlap;
                synchronizer = new AutoResetEvent(false);

            }

            // Begin the processing in the background
            public virtual void Start()
            {
                if (!Running) {
                    Running = true;
                    Error = "";
                    joins = 0;
                    scheduledChunks = 0;
                    resultQueue = new ConcurrentQueue<T>();
                    jobQueue = new ConcurrentQueue<FileMapJoinData>();
                    ThreadPool.QueueUserWorkItem((_) => ManageJobs(Path));
                }
            }

            // Called after all Join complete, override to perform final actions
            protected virtual void Finish()
            {
            }

            // FileMapJoin job data
            public struct FileMapJoinData : IMapJoinData {
                public int Id { get; set; }
                public int Cycle { get; set; }
                public long StartFpos { get; set; }
            };


            //
            // PROTECTED INTERFACE
            //
            // The quantity of bytes that have been completed
            public long ByteCount { get { return Interlocked.Read(ref byteCount); } }
            protected long byteCount;

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

            // Bytes of overlap between buffers
            protected int overlap;

            //
            // PRIVATE INTERFACE
            //

            void ManageJobs(string filePath)
            {
                try {
                    // Create all the chunk jobs (in order)
                    var chunkNum = 0;
                    FileInfo fileInfo = new(filePath);
                    //Logger.Log($"{filePath} is {fileInfo.Length} bytes");
                    for (long pos = 0; pos < fileInfo.Length; pos += chunkSize - overlap) {
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
                        //Logger.Log($"manager {jobQueue.Count} {resultQueue.Count} {scheduledChunks}");
                        ScheduleChunks();
                        synchronizer.WaitOne(1000);
                        JoinResults();
                    }
                    Finish();
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
                //var minWorkers = maxWorkers < 1 ? 0 : maxWorkers / 2;
                while (resultQueue.Count != 0) {
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
                    if (joinMode == JoinMode.Reduce) {
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
                    else if (joinMode == JoinMode.Sequential) {
                        var currentResult = resultBuf[0];
                        if (currentResult.Id == 0) {
                            priorResult = Join(currentResult, currentResult);
                            resultBuf.RemoveAt(0);
                            progressMade = 1;
                            joins++;
                        }
                        else if (joins != 0 && currentResult.Id == priorResult.Id + 1) {
                            priorResult = Join(priorResult, currentResult);
                            resultBuf.RemoveAt(0);
                            progressMade = 1;
                            joins++;
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
                }
                progress.Set();
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
            readonly JoinMode joinMode;
            readonly int maxWorkers;
            ConcurrentQueue<FileMapJoinData> jobQueue;
            ConcurrentQueue<T> resultQueue;
            int scheduledChunks;
            int joins;
            //long running;
        }
    }
}