using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.IO;

namespace Imagibee {
    namespace Gigantor {
        //
        // Supports regex searching for very large files
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
        // A balance between memory footprint and performance can be achieved
        // by varying maxMatchCount, chunkKiBytes and maxWorkers parameters.
        //
        public class RegexSearcher : FileMapJoin<MapJoinData> {
            // The number of matches found so far
            public long MatchCount { get { return Interlocked.Read(ref matchCount); } }

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
            // maxMatchCount - places a limit on the number of matches, defaults to 1000
            // chunkKiBytes - the chunk size in KiBytes that each worker works on
            // maxWorkers - optional limit to the maximum number of simultaneous workers
            // overlap - the maximum size of a matched value, defaults to 1% of chunk size,
            // hoping to depricate this parameter in the future
            public RegexSearcher(
                string filePath,
                System.Text.RegularExpressions.Regex regex,
                AutoResetEvent progress,
                int maxMatchCount=1000,
                int chunkKiBytes=512,
                int maxWorkers=0,
                int overlap = -1) : base(
                    filePath,
                    progress,
                    JoinMode.None,
                    chunkKiBytes,
                    maxWorkers)
            {
                matches = new();
                matchQueue = new();
                this.regex = regex;
                this.maxMatchCount = maxMatchCount;
                if (overlap < 0) {
                    overlap = chunkKiBytes * 1024 / 100;
                }
                base.overlap = overlap;
            }

            // Start the background process
            public new void Start()
            {
                if (!Running) {
                    matches.Clear();
                    matchQueue.Clear();
                    matchCount = 0;
                    base.Start();
                }
            }

            // Finished, sort match data
            protected override void Finish()
            {
                while (matchQueue.TryDequeue(out MatchData result)) {
                    matches.Add(result);
                }
                matches = matches.OrderBy(x => x.StartFpos).ToList();
            }

            // Return the MatchData of current progress
            public IReadOnlyList<MatchData> GetMatchData()
            {
                if (Running) {
                    return new List<MatchData>().AsReadOnly();
                }
                else {
                    return matches.AsReadOnly();
                }
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
                    var newMatches = 0;
                    foreach (System.Text.RegularExpressions.Match match in partitionMatches) {
                        if (match != null && matchCount < maxMatchCount) {
                            matchQueue.Enqueue(new MatchData()
                            {
                                StartFpos = data.StartFpos + match.Index,
                                Name = match.Name,
                                Value = match.Value
                            });
                            newMatches++;
                        }
                    }
                    Interlocked.Add(ref matchCount, newMatches);
                }
                Interlocked.Add(ref byteCount, buf.Length);
                return result;
            }

            // private data
            ConcurrentQueue<MatchData> matchQueue;
            List<MatchData> matches;
            readonly System.Text.RegularExpressions.Regex regex;
            readonly int maxMatchCount;
            long matchCount;
        }
    }
}