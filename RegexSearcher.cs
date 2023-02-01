using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.IO;

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
        // of the file to search and a Regex instance.  All public methods
        // and properties are well behaved at any time.  Although, while
        // Running is true only partial results are available.
        //
        // After the process is finished, the results are stored until Start
        // is called again.  Although, calling Start while Running is true
        // will have no effect.
        //
        // Performance can be tailored to a particular system by varying
        // chunkKiBytes and maxWorkers parameters.
        //
        public class RegexSearcher : FileMapJoin<MapJoinData> {
            // The number of matches found so far
            public int MatchCount { get; private set; }

            // A structure for storing a single match value
            public struct MatchData {
                public long StartFpos;
                public string Name;
                public string Value;
                // TODO: add support for Groups
            }

            // Create a new instance
            //
            // filePath - the path to the file to search
            // regex - the regular expression to match against the file
            // progress - signaled each time MatchCount is updated
            // maxMatchSize - the maximum size of a matched value, defaults to 1% of chunk size
            // chunkKiBytes - the chunk size in KiBytes that each worker works on
            // maxWorkers - optional limit to the maximum number of simultaneous workers
            public RegexSearcher(
                string filePath,
                System.Text.RegularExpressions.Regex regex,
                AutoResetEvent progress,
                int maxMatchCount,
                int maxMatchSize,
                int chunkKiBytes=512,
                int maxWorkers=0) : base(
                    filePath,
                    progress,
                    JoinMode.None,
                    chunkKiBytes,
                    maxWorkers)
            {
                mutex = new();
                this.regex = regex;
                this.maxMatchCount = maxMatchCount;
                if (maxMatchSize < 1) {
                    maxMatchSize = chunkSize / 100;
                }
                overlap = maxMatchSize;
            }

            // Start the background process
            public override void Start()
            {
                if (!Running) {
                    matches = new();
                    base.Start();
                }
            }

            // Finished, sort match data
            protected override void Finish()
            {
                matches = matches.OrderBy(x => x.StartFpos).ToList();
            }

            // Return the MatchData of current progress
            public IReadOnlyList<MatchData> GetMatchData()
            {
                return matches.AsReadOnly();
            }

            protected override MapJoinData Join(MapJoinData a, MapJoinData b)
            {
                return a;
            }

            protected override MapJoinData Map(FileMapJoinData data)
            {
                MapJoinData result = new();
                //Logger.Log($"mapping chunk {data.Id} at {data.StartFpos}");
                using var fileStream = new FileStream(
                    Path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    chunkSize,
                    FileOptions.Asynchronous);
                fileStream.Seek(data.StartFpos, SeekOrigin.Begin);
                var buf = new BinaryReader(fileStream).ReadBytes(chunkSize);
                var str = Utilities.UnsafeByteToString(buf);
                if (buf.Length != str.Length) {
                    throw new System.Exception($"{data.Id} {buf.Length} != {str.Length}");
                }
                var partitionMatches = regex.Matches(str);
                if (partitionMatches.Count > 0) {
                    mutex.WaitOne();
                    try {
                        foreach (System.Text.RegularExpressions.Match match in partitionMatches) {
                            if (match != null &&  MatchCount < maxMatchCount) {
                                matches.Add(new MatchData()
                                {
                                    StartFpos = data.StartFpos + match.Index,
                                    Name = match.Name,
                                    Value = match.Value
                                });
                                MatchCount++;
                            }
                        }
                    }
                    finally {
                        mutex.ReleaseMutex();
                    }                   
                }
                Interlocked.Add(ref byteCount, buf.Length);
                return result;
            }

            // private data
            List<MatchData> matches;
            readonly System.Text.RegularExpressions.Regex regex;
            readonly int maxMatchCount;
            readonly Mutex mutex;

            ~RegexSearcher()
            {
                mutex.Dispose();
            }
        }
    }
}