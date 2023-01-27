using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.IO;
using System.Collections.ObjectModel;

namespace Imagibee {
    namespace Gigantor {
        //
        // Helper for optimizing regex searching against very large files
        //
        // To achieve this goal the search process runs in the background,
        // seperates the file into chunks, and counts the lines in each
        // chunk.  These chunks are ultimately joined into a continuous
        // result.
        //
        // Users should begin the process by calling Start with the path
        // of the file to search and a Regex instance.  The MatchCount
        // property, Running property, LastError property, and GetMatchData
        // method may be used at any time.
        //
        // After the process is finished, the results are stored until Start
        // is called again.  Although, calling Start while Running is true
        // will have no effect.
        //
        // Performance can be tailored to a particular system by varying
        // the chunkSize and maxWorkers parameters.
        //
        public class RegexSearcher {
            // Path of the last successfully started search operation
            public string Path { get; private set; } = "";

            // True while serach process is running
            public bool Running { get; private set; } = false;

            // The number of matches found so far
            public int MatchCount { get; private set; }

            // The error that caused the search process to end prematurely (if any)
            public string LastError { get; private set; } = "";

            // A structure for storing the values of a chunk
            public struct ChunkData {
                public string Path;
                public long StartFpos;
                public List<MatchData> Matches;
            }

            // A structure for storing a single match value
            public struct MatchData {
                public long StartFpos;
                public string Name;
                public string Value;
                // TODO: add support for Groups
            }

            // Create a new instance
            //
            // progress - signaled each time MatchCount is updated
            // chunkKiBytes - the chunk size in KiBytes that each worker works on
            // maxWorkers - optional limit to the maximum number of simultaneous workers
            public RegexSearcher(AutoResetEvent progress, int chunkKiBytes=512, int maxWorkers=0)
            {
                chunkSize = chunkKiBytes * 1024;
                this.maxWorkers = maxWorkers;
                this.progress = progress;
                synchronizer = new AutoResetEvent(false);
            }

            // Begin the search process in the background
            //
            // filePath - the path of the file to search
            // regex - the regular expression to match against the file
            // maxMatchSize - the maximum size of a matched value
            public void Start(string filePath, System.Text.RegularExpressions.Regex regex, int maxMatchSize)
            {
                if (!Running) {
                    LastError = "";
                    Path = filePath;
                    overlap = maxMatchSize;
                    Running = true;
                    scheduledChunks = 0;
                    chunkResults = new ConcurrentQueue<ChunkData>();
                    chunkJobs = new ConcurrentQueue<ChunkJob>();
                    chunks = new List<ChunkData>();
                    ThreadPool.QueueUserWorkItem((_) => ManageJobs(filePath, regex));
                }
            }

            // Efficiently block until background work completes
            public void Wait()
            {
                while (Running) {
                    progress.WaitOne(1000);
                }
            }

            // Wait for multiple line searchers to all complete their background work
            //
            // searchers - a enumerable set of started searchers to wait for
            // OnProgressOrTimeout - called each time LineCount is updated, or at frequency determined by the timeout parameter
            // timeoutMilliSeconds - the time in milliseconds between callbacks
            public static void Wait(ICollection<RegexSearcher> searchers, AutoResetEvent progress, Action<int> OnProgressOrTimeout, int timeoutMilliSeconds)
            {
                while (true) {
                    var runningCount = 0;
                    foreach (var searcher in searchers) {
                        if (searcher.Running) {
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


            // Return the MatchData of current progress
            public ReadOnlyCollection<ChunkData> GetMatchData()
            {
                return chunks.AsReadOnly();
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
                    // for debugging, and abort the search process
                    LastError = e.ToString();
                    Logger.Log($"ERROR: manager : {LastError}");
                }
                Running = false;
                progress.Set();
                //Logger.Log($"search is all done!");
            }

            void ScheduleChunks(System.Text.RegularExpressions.Regex regex)
            {
                while (scheduledChunks < maxWorkers || maxWorkers < 1) {
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
                while (chunkResults.TryDequeue(out ChunkData chunkResult)) {
                    chunks.Add(chunkResult);
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
                            new ChunkData()
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
                    // for debugging, and abort the searching process
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
            ConcurrentQueue<ChunkData> chunkResults;
            ConcurrentQueue<ChunkJob> chunkJobs;
            List<ChunkData> chunks;
            readonly AutoResetEvent synchronizer;
            readonly AutoResetEvent progress;
            readonly int chunkSize;
            readonly int maxWorkers;
            int overlap;
            int scheduledChunks;
        }
    }
}