using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.IO;

namespace Imagibee {
    namespace Gigantor {
        //
        // Base class for partition processing very large files
        //
        // Creates a background manager thread that dispatches
        // additional background worker threads to partition the
        // file into partitions.  These partitions are mapped to a T result
        // by the implementation of the Map method.  Results are
        // then joined by the implementation of the Join method
        // according to the JoinMode.
        //
        // Begin the process by calling Start with the path of the
        // file to process.  All public methods and properties are
        // well behaved at any time.
        //
        // Exceptions during the background processing are caught
        // and stored in Error.  If Error is not empty or Cancelled
        // is true results are undefined.
        //
        public abstract class Partitioner<T> : Partition<PartitionerData, T>, IBackground where T : struct, IPartitionData
        {
            // IBackground methods
            public bool Running { get; private set; }
            public bool Cancelled { get; private set; }
            public string Error { get; private set; } = "";
            public long ByteCount { get { return Interlocked.Read(ref byteCount); } }

            // Path of the last successfully started indexing operation
            public string Path { get; private set; } = "";

            // Create a new instance
            //
            // filePath - the path to the file to process
            // progress - signaled each time a thread completes
            // joinMode - defines the partition join mode
            // partitionSize - the partition size in bytes that each worker works on, must
            // be at least 2048
            // maxWorkers - optional limit to the maximum number of simultaneous workers,
            // defaults to unlimited
            // overlap - size in bytes of partition overlap, defaults to 0, if non-zero
            // must be even and not exceed half the partitionSize
            public Partitioner(
                string filePath,
                AutoResetEvent progress,
                JoinMode joinMode,
                int partitionSize,
                int maxWorkers = 0,
                int overlap = 0)
            {
                Path = filePath;
                this.progress = progress;
                this.joinMode = joinMode;
                this.partitionSize = partitionSize;
                this.overlap = overlap;
                this.maxWorkers = maxWorkers;
                EnforceValidations();
                synchronize = new AutoResetEvent(false);
                cancel = new ManualResetEvent(false);
                resultQueue = new ConcurrentQueue<T>();
                jobQueue = new ConcurrentQueue<PartitionerData>();
                priorResult = new();
            }

            public virtual void Start()
            {
                if (!Running) {
                    byteCount = 0;
                    queueDone = false;
                    cancel.Reset();
                    Running = true;
                    Error = "";
                    joins = 0;
                    scheduledPartitions = 0;
                    resultQueue.Clear();
                    jobQueue.Clear();
                    ThreadPool.QueueUserWorkItem((_) => ManageJobs(Path));
                }
            }

            public virtual void Cancel()
            {
                if (Running) {
                    cancel.Set();
                }
            }


            //
            // PROTECTED INTERFACE
            //

            protected long byteCount;

            // Normally false, this canel event can by set by the user calling Cancel,
            // or by the implementation Map or Join method calling cancel.Set.  The
            // base class manager thread periodically polls the event and, if it is
            // ever set, cancels the entire job and sets Cancelled property to true.
            // If necessary the implementation Map and Join methods can also check for
            // this event being set and exit early.
            protected readonly ManualResetEvent cancel;

            // When used from Join, this value contains the T result returned
            // from the prior call of Join
            protected T priorResult;

            // Partitioning size in bytes
            protected int partitionSize;

            // Partition overlap in bytes
            protected int overlap;

            // Optional streaming mode
            protected Stream? Stream { get; set; }


            //
            // PRIVATE IMPLEMENTATION
            //

            void QueueFileJobs(string filePath)
            {
                // Create all the partition jobs (in order)
                var partitionNum = 0;
                FileInfo fileInfo = new(filePath);
                //Logger.Log($"{filePath} is {fileInfo.Length} bytes");
                for (long pos = 0; pos < fileInfo.Length; pos += partitionSize - overlap) {
                    //Logger.Log($"queueing file partition {partitionNum}, {partitionSize} bytes");
                    jobQueue.Enqueue(
                        new PartitionerData()
                        {
                            Id = partitionNum++,
                            StartFpos = pos,
                            Buf = null,
                        });
                }
                queueDone = true;
            }

            void QueueStreamJobs(Stream stream)
            {
                try {
                    if (ovBuf == null || ovBuf.Length != partitionSize) {
                        ovBuf = new byte[overlap];
                    }
                    var readSize = partitionSize - overlap;
                    var partitionNum = 0;
                    long pos = 0;
                    var bytesRead = Utilities.ReadChunk(stream, ovBuf, 0, overlap);
                    do {
                        if (jobQueue.Count < maxWorkers) {
                            var buf = new byte[partitionSize];
                            bytesRead = Utilities.ReadChunk(stream, buf, overlap, readSize);
                            Array.Copy(ovBuf, 0, buf, 0, overlap);
                            if (partitionSize != bytesRead + overlap) {
                                Array.Resize(ref buf, bytesRead + overlap);
                            }
                            var job = new PartitionerData()
                            {
                                Id = partitionNum++,
                                StartFpos = pos,
                                Buf = buf,
                            };
                            jobQueue.Enqueue(job);
                            if (bytesRead < readSize || cancel.WaitOne(0)) {
                                break;
                            }
                            pos += readSize;
                            Array.Copy(buf, buf.Length - overlap, ovBuf, 0, overlap);
                            synchronize.Set();
                        }
                        else {
                            synchronize.WaitOne(0);
                        }
                    }
                    while (bytesRead != 0);
                }
                catch (Exception e) {
                    // In the background catch all exceptions, record the text
                    // for debugging, and abort the indexing process
                    Error = e.ToString();
                    cancel.Set();
                }
                queueDone = true;
            }

            void ManageJobs(string filePath)
            {
                try {
                    if (Stream == null) {
                        QueueFileJobs(filePath);
                    }
                    else {
                        ThreadPool.QueueUserWorkItem((_) => QueueStreamJobs(Stream));
                        synchronize.WaitOne(1000);
                    }
                    // Work until the queues are empty
                    while (queueDone == false ||
                           jobQueue.Count != 0 ||
                           resultQueue.Count != 0 ||
                           scheduledPartitions != 0) {
                        //Logger.Log($"manager {jobQueue.Count} {resultQueue.Count} {scheduledpartitions}");
                        Schedulepartitions();
                        synchronize.WaitOne(1000);
                        JoinResults();
                        if (cancel.WaitOne(0)) {
                            Cancelled = true;
                            break;
                        }
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

            void Schedulepartitions()
            {
                while (scheduledPartitions < maxWorkers || maxWorkers < 1) {
                    if (jobQueue.TryDequeue(out PartitionerData job)) {
                        Interlocked.Add(ref scheduledPartitions, 1);
                        ThreadPool.QueueUserWorkItem((_) => MapJob(job));
                        //Logger.Log($"scheduled partition {job.Id}, currently {scheduledpartitions} scheduled partitions");
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
                    else { // JoinMode.None
                        progressMade = resultBuf.Count;
                        resultBuf.Clear();
                    }
                }
                foreach (var result in resultBuf) {
                    resultQueue.Enqueue(result);
                }
                if (progressMade != 0) {
                    Interlocked.Add(ref scheduledPartitions, -progressMade);
                }
                progress.Set();
            }

            void MapJob(PartitionerData data)
            {
                try {
                    if (ovBuf == null || ovBuf.Length != partitionSize) {
                        ovBuf = new byte[partitionSize];
                    }
                    if (data.Buf == null) {
                        using var fileStream = Imagibee.Gigantor.FileStream.Create(
                            Path, bufferSize: partitionSize);
                        fileStream.Seek(data.StartFpos, SeekOrigin.Begin);
                        data.Buf = ovBuf;
                        var bytesRead = fileStream.Read(data.Buf, 0, partitionSize);
                        // If this is the final read the buffer may be smaller than the partition
                        // and needs to be resized
                        if (bytesRead != partitionSize) {
                            var buf = new byte[bytesRead];
                            Array.Copy(data.Buf, buf, bytesRead);
                            data.Buf = buf;
                        }
                    }
                    resultQueue.Enqueue(Map(data));
                }
                catch (Exception e) {
                    // In the background catch all exceptions, record the text
                    // for debugging, and abort the indexing process
                    Error = e.ToString();
                    cancel.Set();
                }
                synchronize.Set();
            }

            void EnforceValidations()
            {
                if (partitionSize < 2048) {
                    // partition size must be at least 2048 bytes
                    partitionSize = 2048;
                }
                if (overlap < 0) {
                    // overlap cannot be negative
                    overlap = 0;
                }
                if (overlap > partitionSize / 2) {
                    // overlap cannot exceed 1/2 the partition size
                    overlap = partitionSize / 2;
                }
                if ((overlap & 1) != 0) {
                    // overlap must be even
                    overlap += 1;
                }
            }

            // private data
            readonly AutoResetEvent synchronize;
            readonly AutoResetEvent progress;
            readonly int maxWorkers;
            readonly ConcurrentQueue<PartitionerData> jobQueue;
            readonly ConcurrentQueue<T> resultQueue;
            int scheduledPartitions;
            int joins;
            bool queueDone;
            [ThreadStatic] static byte[]? ovBuf;
        }

        // Partition job data
        public struct PartitionerData : IPartitionData {
            public int Id { get; set; }
            public int Cycle { get; set; }
            public long StartFpos { get; set; }
            public byte[]? Buf { get; set; }
        };
    }
}
