using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.IO;

namespace Imagibee {
    namespace Gigantor {
        //
        // Helper for optimizing regex search for very large text files
        //
        public class RegexSearcher {
            // True while index process is running
            public bool Running { get; private set; } = false;

            // The number of matches found so far
            public int MatchCount { get; private set; }

            // The error that caused the index process to end prematurely (if any)
            public string LastError { get; private set; } = "";

            // A structure for storing the values of match index
            public struct IndexData {
                public string Path;
                public long StartFpos;
                public List<MatchData> Matches;
                //public System.Text.RegularExpressions.MatchCollection Matches;
            }

            // A structure for storing a single match value
            public struct MatchData {
                public long StartFpos;
                public string Name;
                public string Value;
                // TODO: add support for Groups
            }

            // Create a new instance 
            public RegexSearcher(AutoResetEvent progress, int chunkSize = 512 * 1024, int maxWorkers = 128)
            {
                this.chunkSize = chunkSize;
                this.maxWorkers = maxWorkers;
                this.progress = progress;
                synchronizer = new AutoResetEvent(false);
            }

            // Begin the search process in the background
            public void Start(string filePath, System.Text.RegularExpressions.Regex regex, int maxMatchSize)
            {
                LastError = "";
                if (!Running) {
                    overlap = maxMatchSize;
                    Running = true;
                    scheduledChunks = 0;
                    chunkResults = new ConcurrentQueue<IndexData>();
                    chunkJobs = new ConcurrentQueue<ChunkJob>();
                    indexes = new List<IndexData>();
                    ThreadPool.QueueUserWorkItem((_) => ManageJobs(filePath, regex));
                }
            }

            // Return the MatchData of current progress
            public List<IndexData> GetMatchData()
            {
                return indexes;
            }

            void ManageJobs(string filePath, System.Text.RegularExpressions.Regex regex)
            {
                try {
                    // Create all the chunk jobs (in order)
                    var chunkNum = 0;
                    FileInfo fileInfo = new(filePath);
                    //Logger.Log($"{filePath} is {fileInfo.Length} bytes");
                    for (long pos = 0; pos < fileInfo.Length; pos += chunkSize - overlap) {
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
                        //Logger.Log($"{chunkJobs.Count} {chunkResults.Count} {scheduledChunks}");
                        ScheduleChunks(regex);
                        synchronizer.WaitOne(1000);
                        JoinResults();
                    }
                }
                catch (Exception e) {
                    // In the background catch all exceptions, record the text
                    // for debugging, and abort the indexing process
                    LastError = e.ToString();
                    Logger.Log($"ERROR: manager : {LastError}");
                }
                Running = false;
                progress.Set();
                //Logger.Log($"search is all done!");
            }

            void ScheduleChunks(System.Text.RegularExpressions.Regex regex)
            {
                while (scheduledChunks < maxWorkers) {
                    if (chunkJobs.TryDequeue(out ChunkJob chunkJob)) {
                        Interlocked.Add(ref scheduledChunks, 1);
                        ThreadPool.QueueUserWorkItem((_) => MapChunk(chunkJob, regex));
                        //Logger.Log($"scheduled chunk {chunkJob.Id}, currently {scheduledChunks} scheduled chunks");
                    }
                    else {
                        break;
                    }
                }
            }

            void JoinResults()
            {
                // TODO: add de-duplication here since duplicates can occurr in the overlap region
                while (chunkResults.TryDequeue(out IndexData chunkResult)) {
                    indexes.Add(chunkResult);
                    MatchCount += chunkResult.Matches.Count;
                    progress.Set();
                }
            }

            void MapChunk(ChunkJob chunk, System.Text.RegularExpressions.Regex regex)
            {
                try {
                    //Logger.Log($"mapping chunk {chunk.Id} from {chunk.Path} at {chunk.StartFpos}");
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
                    var matches = regex.Matches(System.Text.Encoding.UTF8.GetString(buf));
                    if (matches.Count > 0) {
                        List<MatchData> chunkMatches = new();
                        foreach (System.Text.RegularExpressions.Match match in matches) {
                            if (match != null) {
                                chunkMatches.Add(new MatchData()
                                {
                                    StartFpos = chunk.StartFpos + match.Index,
                                    Name = match.Name,
                                    Value = match.Value
                                });
                            }
                        }
                        chunkResults.Enqueue(
                            new IndexData()
                            {
                                Path = chunk.Path,
                                StartFpos = chunk.StartFpos,
                                Matches = chunkMatches
                                //Matches = matches
                            });
                    }
                }
                catch (Exception e) {
                    // In the background catch all exceptions, record the text
                    // for debugging, and abort the indexing process
                    LastError = e.ToString();
                    Logger.Log($"ERROR: chunk {chunk.Id}: {LastError}");
                }
                Interlocked.Add(ref scheduledChunks, -1);
                synchronizer.Set();
                //Logger.Log($"finished chunk {chunk.Id}");
            }

            // private types
            struct ChunkJob {
                public int Id;
                public string Path;
                public long StartFpos;
            };

            // private data
            ConcurrentQueue<IndexData> chunkResults;
            ConcurrentQueue<ChunkJob> chunkJobs;
            List<IndexData> indexes;
            AutoResetEvent synchronizer;
            AutoResetEvent progress;
            readonly int chunkSize;
            readonly int maxWorkers;
            int overlap;
            int scheduledChunks;
        }
    }
}