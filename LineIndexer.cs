using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.IO;

namespace Imagibee {
    namespace Gigantor {
        //
        // Helper for optimizing reading lines for very large files
        //
        // To achieve this goal the index process runs in the background,
        // seperates the file into chunks, and counts the lines in each
        // chunk.  These chunks are ultimately joined into a continuous
        // result.
        //
        // Users should begin the process by calling Start with the path
        // of the file to index.  All public methods and properties are
        // well behaved at any time.  Although, while Running is true only
        // partial results are available.
        //
        // After the process is finished, the results are stored until Start
        // is called again.  Although, calling Start while Running is true
        // will have no effect.
        //
        // Performance can be tailored to a particular system by varying
        // chunkKiBytes and maxWorkers parameters.
        //
        public class LineIndexer {
            // Path of the last successfully started indexing operation
            public string Path { get; private set; } = "";

            // True while index process is running
            public bool Running { get; private set; } = false;

            // The number of lines that have been indexed so far
            public long LineCount { get; private set; } = 0;

            // The number of bytes that have been indexed so far
            public long ByteCount { get; private set; } = 0;

            // The error that caused the index process to end prematurely (if any)
            public string LastError { get; private set; } = "";

            // Create a new instance
            //
            // progress - signaled each time MatchCount is updated
            // chunkKiBytes - the chunk size in KiBytes that each worker works on
            // maxWorkers - optional limit to the maximum number of simultaneous workers
            public LineIndexer(AutoResetEvent progress, int chunkKiBytes=512, int maxWorkers=0)
            {
                chunkSize = chunkKiBytes * 1024;
                this.maxWorkers = maxWorkers;
                this.progress = progress;
                synchronizer = new AutoResetEvent(false);
            }

            // Begin the indexing process in the background
            public void Start(string filePath)
            {
                if (!Running) {
                    LastError = "";
                    Path = filePath;
                    LineCount = 0;
                    ByteCount = 0;
                    Running = true;
                    priorIndex = -1;
                    scheduledChunks = 0;
                    chunkResults = new ConcurrentQueue<ChunkResult>();
                    chunkJobs = new ConcurrentQueue<ChunkJob>();
                    chunks = new List<ChunkData>();
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
            public static void Wait(ICollection<LineIndexer> indexers, AutoResetEvent progress, Action<int> OnProgressOrTimeout, int timeoutMilliSeconds)
            {
                while (true) {
                    var runningCount = 0;
                    foreach (var indexer in indexers) {
                        if (indexer.Running) {
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

            // Return the fpos of the requested line or -1 if the line does not exist
            public long PositionFromLine(long line)
            {
                long fpos = -1;
                var chunk = GetChunk(line);
                if (chunk.HasValue) {
                    var linesToConsume = line - chunk.Value.StartLine;
                    if (linesToConsume == 0) {
                        return chunk.Value.StartFpos;
                    }
                    using var fileStream = new FileStream(
                        Path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        chunkSize,
                        FileOptions.Asynchronous);
                    fileStream.Seek(chunk.Value.StartFpos, SeekOrigin.Begin);
                    using var streamReader = new BinaryReader(fileStream);
                    var buf = streamReader.ReadBytes(chunkSize);
                    for (var i = 0; i < buf.Length; i++) {
                        if (buf[i] == '\n') {
                            linesToConsume--;
                        }
                        if (linesToConsume == 0) {
                            return chunk.Value.StartFpos + i + 1;
                        }
                    }
                }
                return fpos;
            }

            // Return the line number of the requested fpos or -1 if the line does not exist
            public long LineFromPosition(long fpos)
            {
                long line = -1;
                var chunkIndex = (int)(fpos/chunkSize);
                if (chunkIndex >= 0 && chunkIndex < chunks.Count) {
                    var chunk = chunks[chunkIndex];
                    var distance = fpos - chunk.StartFpos;
                    line = chunk.StartLine;
                    using var fileStream = new FileStream(
                        Path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        chunkSize,
                        FileOptions.Asynchronous);
                    fileStream.Seek(chunk.StartFpos, SeekOrigin.Begin);
                    using var streamReader = new BinaryReader(fileStream);
                    var buf = streamReader.ReadBytes(chunkSize);
                    for (var i = 0; i < buf.Length; i++) {
                        if (buf[i] == '\n') {
                            line++;
                        }
                        if (i >= distance) {
                            break;
                        }
                    }
                }
                return line;
            }

            // Return the ChunkData that contains the starting byte of the requested line
            ChunkData? GetChunk(long line)
            {
                if (line > 0 && line <= LineCount) {
                    // Make initial search start at the chunk where the average lines
                    // per chunk would suggest the line should be
                    var avgLinesPerChunk = LineCount / chunks.Count;
                    var chunkIndex = Math.Max(0, (int)(line / avgLinesPerChunk));
                    chunkIndex = Math.Min(chunkIndex, chunks.Count - 1);
                    while (chunkIndex >= 0 && chunkIndex < chunks.Count) {
                        int direction;
                        var chunk = chunks[chunkIndex];
                        if (line >= chunk.StartLine &&
                            line <= chunk.EndLine) {
                            return chunk;
                        }
                        else if (line > chunk.EndLine) {
                            direction = 1;
                        }
                        else {
                            direction = -1;
                        }
                        chunkIndex += direction;
                    }
                }
                return null;
            }

            void ManageJobs(string filePath)
            {
                try {
                    // Create all the chunk jobs (in order)
                    var chunkNum = 0;
                    FileInfo fileInfo = new(filePath);
                    //Logger.Log($"{filePath} is {fileInfo.Length} bytes");
                    for (long pos = 0; pos < fileInfo.Length; pos += chunkSize) {
                        chunkJobs.Enqueue(
                            new ChunkJob()
                            {
                                Id = chunkNum++,
                                StartFpos = pos,
                            });
                    }
                    //Logger.Log($"{chunkJobs.Count} chunks");
                    // Work until the queues are empty
                    while (chunkJobs.Count != 0 || chunkResults.Count != 0 || scheduledChunks != 0) {
                        ScheduleChunks();
                        synchronizer.WaitOne(1000);
                        JoinResults();
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
                        //Logger.Log($"scheduled chunk {chunkJob.Id}, currently {scheduledChunks} scheduled chunks");
                    }
                    else {
                        break;
                    }
                }
            }

            void JoinResults()
            {
                while (chunkResults.Count > 0) {
                    JoinNextResult();
                }
            }

            void JoinNextResult()
            {
                List<ChunkResult> chunkBuf = new();
                while (chunkResults.TryDequeue(out ChunkResult chunkResult)) {
                    chunkBuf.Add(chunkResult);
                }
                if (chunkBuf.Count > 0) {
                    chunkBuf.Sort((a, b) => a.Id.CompareTo(b.Id));
                    var currentResult = chunkBuf[0];
                    if (currentResult.Id == priorIndex + 1) {
                        var currentChunk = new ChunkData()
                        {
                            StartLine = 1,
                            EndLine = currentResult.LineCount,
                            StartFpos = currentResult.StartFpos,
                            EolEnding = currentResult.EolEnding,
                            FirstEolOffset = currentResult.FirstEolOffset,
                            ByteCount = currentResult.ByteCount,
                        };
                        if (priorIndex >= 0) {
                            var priorChunk = chunks[priorIndex];
                            currentChunk.StartLine = priorChunk.EndLine + 1;
                            currentChunk.EndLine = priorChunk.EndLine + currentResult.LineCount;
                            if (priorChunk.EolEnding == false) {
                                // adjustment since the first line was counted in the prior chunk
                                currentChunk.StartLine += 1;
                                priorChunk.EndLine += 1;
                                currentChunk.StartFpos += currentResult.FirstEolOffset + 1;
                                chunks[priorIndex] = priorChunk;
                            }
                            //Logger.Log($"chunk {priorIndex} between {priorChunk.StartLine} and {priorChunk.EndLine} at {priorChunk.StartFpos}");
                        }
                        if (currentResult.FinalChunk) {
                            if (currentResult.EolEnding == false) {
                                // adjustment for last chunk not ending on eol
                                currentChunk.EndLine += 1;
                            }
                            //Logger.Log($"chunk {priorIndex+1} between {currentChunk.StartLine} and {currentChunk.EndLine} at {currentChunk.StartFpos}");
                        }
                        chunks.Add(currentChunk);
                        LineCount += currentResult.LineCount;
                        ByteCount += currentResult.ByteCount;
                        chunkBuf.RemoveAt(0);
                        priorIndex++;
                        progress.Set();
                    }
                }
                foreach (var chunk in chunkBuf) {
                    chunkResults.Enqueue(chunk);
                }
            }

            void MapChunk(ChunkJob chunk)
            {
                try {
                    //Logger.Log($"mapping chunk {chunk.Id} from {Path} at {chunk.StartFpos}");
                    var result = new ChunkResult()
                    {
                        EolEnding = false,
                        Id = chunk.Id,
                        LineCount = 0,
                        ByteCount = 0,
                        StartFpos = chunk.StartFpos,
                        FirstEolOffset = -1,
                        FinalChunk = false,
                        
                    };
                    using var fileStream = new FileStream(
                        Path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        chunkSize,
                        FileOptions.Asynchronous);
                    fileStream.Seek(chunk.StartFpos, SeekOrigin.Begin);
                    using var streamReader = new BinaryReader(fileStream);
                    var buf = streamReader.ReadBytes(chunkSize);
                    result.ByteCount = buf.Length;
                    for (var i = 0; i < buf.Length; i++) {
                        if (buf[i] == '\n') {
                            result.LineCount++;
                            result.EolEnding = true;
                            if (result.FirstEolOffset == -1) {
                                // store the first eol position for later
                                result.FirstEolOffset = i;
                            }
                        }
                        else {
                            result.EolEnding = false;
                        }
                    }
                    var lastPosition = fileStream.Position;
                    fileStream.Seek(0, SeekOrigin.End);
                    if (lastPosition == fileStream.Position) {
                        result.FinalChunk = true;
                    };
                    result.LineCount = Math.Max(1, result.LineCount);
                    chunkResults.Enqueue(result);
                }
                catch (Exception e) {
                    // In the background catch all exceptions, record the text
                    // for debugging, and abort the indexing process
                    LastError = e.ToString();
                }
                Interlocked.Add(ref scheduledChunks, -1);
                synchronizer.Set();
            }

            // private types
            struct ChunkJob {
                public int Id;
                public long StartFpos;
            };
            struct ChunkResult {
                public int Id;
                public long StartFpos;
                public long LineCount;
                public bool EolEnding;
                public int FirstEolOffset;
                public int ByteCount;
                public bool FinalChunk;
            };
            struct ChunkData {
                public long StartLine;
                public long EndLine;
                public long StartFpos;
                public bool EolEnding;
                public int FirstEolOffset;
                public int ByteCount;
            }

            // private data
            ConcurrentQueue<ChunkResult> chunkResults;
            ConcurrentQueue<ChunkJob> chunkJobs;
            readonly AutoResetEvent synchronizer;
            readonly AutoResetEvent progress;
            List<ChunkData> chunks;
            readonly int chunkSize;
            readonly int maxWorkers;
            int priorIndex;
            int scheduledChunks;
        }
    }
}