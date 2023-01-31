using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.IO;

namespace Imagibee {
    namespace Gigantor {

        // FileMapJoin job data
        public struct FileMapJoinData : IMapJoinData {
            public int Id { get; set; }
            public int Cycle { get; set; }
            public long StartFpos { get; set; }
        };

        //
        // Abstract base class for processing very large files
        //
        // The class dispatches threads that run in the background to
        // partition the file into chunks.  These chunks are mapped
        // to a result T by an implementation of the abstract Map
        // method.  Results are then joined by an implementation of
        // the abstract Join method according to the options defined
        // in MapJoinOptions.
        //
        // Begin the process by calling Start with the path of the
        // file to process.  All public methods and properties are
        // well behaved at any time.  Although, while Running is true
        // only partial results are available.
        //
        public abstract class FileMapJoin<T> : IBackground where T : IMapJoinData
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
            // options - defines the map/join options
            // chunkKiBytes - the chunk size in KiBytes that each worker works on
            // maxWorkers - optional limit to the maximum number of simultaneous workers
            public FileMapJoin(
                AutoResetEvent progress,
                MapJoinOption options,
                int chunkKiBytes,
                int maxWorkers)
            {
                if (chunkKiBytes < 1) {
                    chunkKiBytes = 1;
                }
                chunkSize = chunkKiBytes * 1024;
                this.maxWorkers = maxWorkers;
                this.progress = progress;
                this.options = options;
                synchronizer = new AutoResetEvent(false);
            }

            // Begin the processing in the background
            //
            // filePath - the path to the file to process
            public void Start(string filePath)
            {
                if (!Running) {
                    Started();
                    Error = "";
                    Path = filePath;
                    scheduledChunks = 0;
                    resultQueue = new ConcurrentQueue<T>();
                    jobQueue = new ConcurrentQueue<FileMapJoinData>();
                    ThreadPool.QueueUserWorkItem((_) => ManageJobs(filePath));
                    Running = true;
                }
            }


            //
            // PROTECTED INTERFACE
            //

            // Called each time Start is called prior to doing anything else
            protected abstract void Started();

            // Called by a background thread to map partition data to a T result
            protected abstract T Map(FileMapJoinData data);

            // Called by a background thread to join two results a and b
            //
            // The interpretation of a, b, and T depend on MapJoinOption
            protected abstract T Join(T a, T b);

            // When used from Join, this value contains the T result returned
            // from the prior call of Join
            protected T priorResult;


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
                    if ((options & MapJoinOption.ReducingJoins) != 0) {
                        for (var i = 0; i < resultBuf.Count - 1; i++) {
                            var result1 = resultBuf[i];
                            var result2 = resultBuf[i + 1];
                            if (result1.Id == result2.Id - 1 &&
                                result1.Cycle == result2.Cycle) {
                                resultQueue.Enqueue(Join(result1, result2));
                                resultBuf.RemoveAt(i);
                                resultBuf.RemoveAt(i+1);
                                progressMade += 1;
                            }
                        }
                    }
                    else {
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
            readonly MapJoinOption options;
            readonly int chunkSize;
            readonly int maxWorkers;
            ConcurrentQueue<FileMapJoinData> jobQueue;
            ConcurrentQueue<T> resultQueue;
            int scheduledChunks;
        }
    }
}