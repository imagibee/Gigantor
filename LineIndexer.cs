using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.IO;

namespace Imagibee {
    namespace Gigantor {
        //
        // Helper for optimizing reading lines for very large text files
        //
        // The main problem being solved is optimizing the performance and
        // memory footprint for very large text files concerning i) the
        // determination of the number of lines, and ii) reading the text
        // of a particular line.
        //
        // To achieve this goal the index process runs in the background,
        // seperates the file into chunks, and counts the lines in each
        // chunk.  These chunks are ultimately joined into a full index.
        //
        // Users should begin the process by calling Start with the path
        // of the file to index.  The LineCount property, Running property,
        // LastError property, and GetIndex method may be used at any time.
        //
        // After the process is finished, the results are stored until Start
        // is called again.  Although, calling Start while Running is true
        // will have no effect.
        //
        // Performance can be tailored to a particular system by varying
        // the chunkSize and maxWorkers parameters.
        //
        public class LineIndexer {
            // True while index process is running
            public bool Running { get; private set; } = false;

            // The number of lines that have been indexed so far
            public int LineCount { get; private set; } = 0;

            // The error that caused the index process to end prematurely (if any)
            public string LastError { get; private set; } = "";

            // A structure for storing the values of an index
            public struct IndexData {
                public string Path;
                public int StartLine;
                public int EndLine;
                public long StartFpos;
            }

            // Create a new instance 
            public LineIndexer(int chunkSize = 512 * 1024, int maxWorkers = 1)
            {
                this.chunkSize = chunkSize;
                this.maxWorkers = maxWorkers;
            }

            // Begin the indexing process in the background
            public void Start(string filePath)
            {
                LastError = "";
                if (!Running) {
                    Running = true;
                    currentChunk = -1;
                    scheduledChunks = 0;
                    chunkResults = new ConcurrentQueue<ChunkResult>();
                    chunkJobs = new ConcurrentQueue<ChunkJob>();
                    indexes = new List<IndexData>();
                    ThreadPool.QueueUserWorkItem((_) => ManageJobs(filePath));
                }
            }

            // Return the IndexData that contains the starting byte of the requested line
            public IndexData? GetIndex(int line)
            {
                if (line > 0 && line <= LineCount) {
                    foreach (var index in indexes) {
                        if (line >= index.StartLine &&
                            line <= index.EndLine) {
                            return index;
                        }
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
                                Path = filePath,
                                StartFpos = pos,
                            });
                    }
                    //Logger.Log($"{chunkJobs.Count} chunks");
                    // Work until the queues are empty
                    while (chunkJobs.Count != 0 || chunkResults.Count != 0 || scheduledChunks != 0) {
                        ScheduleChunks();
                        JoinResults();
                        Thread.Sleep(0);
                    }
                }
                catch (Exception e) {
                    // In the background catch all exceptions, record the text
                    // for debugging, and abort the indexing process
                    LastError = e.ToString();
                }
                Running = false;
            }

            void ScheduleChunks()
            {
                if (scheduledChunks < maxWorkers) {
                    if (chunkJobs.TryDequeue(out ChunkJob chunkJob)) {
                        scheduledChunks++;
                        ThreadPool.QueueUserWorkItem((_) => MapChunk(chunkJob));
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
                    var chunk = chunkBuf[0];
                    if (chunk.Id == currentChunk + 1) {
                        var index = new IndexData()
                        {
                            Path = chunk.Path,
                            StartLine = 1,
                            EndLine = chunk.LineCount,
                            StartFpos = chunk.StartFpos
                        };
                        if (currentChunk >= 0) {
                            var currentIndex = indexes[currentChunk];
                            index.StartLine = currentIndex.EndLine + 1;
                            index.EndLine = currentIndex.EndLine + chunk.LineCount;
                            if (currentIndex.Path == chunk.Path &&
                                chunk.EolEnding == false &&
                                chunk.FirstEolOffset != -1) {
                                // adjustment since the first line was counted in the prior chunk
                                currentIndex.EndLine += 1;
                                index.StartLine += 1;
                                index.EndLine += 1;
                                index.StartFpos += chunk.FirstEolOffset + 1;
                                indexes[currentChunk] = currentIndex;
                            }
                            //Logger.Log($"index {currentChunk} between {currentIndex.StartLine} and {currentIndex.EndLine} at {currentIndex.StartFpos}");
                        }
                        indexes.Add(index);
                        LineCount += chunk.LineCount;
                        chunkBuf.RemoveAt(0);
                        currentChunk++;
                    }
                }
                foreach (var chunk in chunkBuf) {
                    chunkResults.Enqueue(chunk);
                }
            }

            void MapChunk(ChunkJob chunk)
            {
                try {
                    //Logger.Log($"mapping chunk {chunk.Id} from {chunk.Path} at {chunk.StartFpos}");
                    var result = new ChunkResult()
                    {
                        EolEnding = false,
                        Path = chunk.Path,
                        Id = chunk.Id,
                        LineCount = 0,
                        StartFpos = chunk.StartFpos,
                        FirstEolOffset = -1
                    };
                    using var fileStream = new FileStream(
                        chunk.Path,
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
                    chunkResults.Enqueue(result);
                }
                catch (Exception e) {
                    // In the background catch all exceptions, record the text
                    // for debugging, and abort the indexing process
                    LastError = e.ToString();
                }
                scheduledChunks--;
            }

            // private types
            struct ChunkJob {
                public int Id;
                public string Path;
                public long StartFpos;
            };
            struct ChunkResult {
                public int Id;
                public string Path;
                public long StartFpos;
                public int LineCount;
                public bool EolEnding;
                public int FirstEolOffset;
            };

            // private data
            ConcurrentQueue<ChunkResult> chunkResults;
            ConcurrentQueue<ChunkJob> chunkJobs;
            List<IndexData> indexes;
            readonly int chunkSize;
            readonly int maxWorkers;
            int currentChunk;
            int scheduledChunks;
        }
    }
}