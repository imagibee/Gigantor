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

            // A structure for storing capture data
            public struct CaptureData {
                public long StartFpos;
                public string Value;
            }
            // A structure for storing match group data
            public struct GroupData
            {
                public long StartFpos;
                public string Name;
                public string Value;
                public IReadOnlyList<CaptureData> Captures;
            }
            // A structure for storing a single match value
            public struct MatchData {
                public long StartFpos;
                public string Name;
                public string Value;
                public IReadOnlyList<GroupData> Groups;
            }

            // Create a new instance
            //
            // filePath - the path to the file to search
            // regex - the regular expression to match against the file
            // progress - signaled each time MatchCount is updated
            // maxMatchCount - places a limit on the number of matches, defaults to 1000
            // chunkKiBytes - the chunk size in KiBytes that each worker works on
            // maxWorkers - optional limit to the maximum number of simultaneous workers
            // overlap - size in bytes of partition overlap, used for finding matches that
            // span two partitions, when set greater than or equal to the maximum size
            // then all matches will be found, when set smaller there is a chance that a
            // match on a partition boundary will not be found, very large values
            // negatively impact performance, the optimal value is the exact size of the
            // maximum matched value (which may not be known), defaults to 1/128th of
            // chunk size, may not exceed half the chunk size
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
                base.Overlap = overlap;
            }

            public RegexSearcher(
                Stream stream,
                System.Text.RegularExpressions.Regex regex,
                AutoResetEvent progress,
                int maxMatchCount = 1000,
                int chunkKiBytes = 512,
                int maxWorkers = 0,
                int overlap = -1) : base(
                    "",
                    progress,
                    JoinMode.None,
                    chunkKiBytes,
                    maxWorkers)
            {
                matches = new();
                matchQueue = new();
                this.regex = regex;
                this.maxMatchCount = maxMatchCount;
                base.Overlap = overlap;
                base.Stream = stream;
            }

            // Start the background process
            public override void Start()
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
                HashSet<long> matchPositions = new();
                while (matchQueue.TryDequeue(out MatchData result)) {
                    // Ignore duplicates
                    if (!matchPositions.Contains(result.StartFpos)) {
                        matches.Add(result);
                        matchPositions.Add(result.StartFpos);
                    }
                }
                // Adjust matchCount after dedup
                matchCount = matches.Count;
                //matches = matches.OrderBy(x => x.StartFpos).ToList();
                matches.Sort((a, b) => a.StartFpos.CompareTo(b.StartFpos));
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
                if (data.Buf == null) {
                    using var fileStream = new FileStream(
                        Path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        chunkSize,
                        FileOptions.Asynchronous);
                    fileStream.Seek(data.StartFpos, SeekOrigin.Begin);
                    data.Buf = new BinaryReader(fileStream).ReadBytes(chunkSize);
                }
                var str = Utilities.UnsafeByteToString(data.Buf);
                if (data.Buf.Length != str.Length) {
                    throw new System.Exception($"{data.Id} {data.Buf.Length} != {str.Length}");
                }
                var partitionMatches = regex.Matches(str);
                if (partitionMatches.Count > 0) {
                    var newMatches = 0;
                    foreach (System.Text.RegularExpressions.Match match in partitionMatches) {
                        if (match != null && matchCount < maxMatchCount) {
                            var groups = new List<GroupData>();
                            foreach (System.Text.RegularExpressions.Group group in match.Groups) {
                                List<CaptureData> cd = new();
                                foreach (System.Text.RegularExpressions.Capture capture in group.Captures) {
                                    cd.Add(
                                        new CaptureData()
                                        {
                                            StartFpos = capture.Index + data.StartFpos,
                                            Value = capture.Value,
                                        });
                                }
                                groups.Add(
                                    new GroupData()
                                    {
                                        StartFpos = data.StartFpos + group.Index,
                                        Name = group.Name,
                                        Value = group.Value,
                                        Captures = cd.AsReadOnly(),
                                    });
                            }
                            matchQueue.Enqueue(
                                new MatchData()
                                {
                                    StartFpos = data.StartFpos + match.Index,
                                    Name = match.Name,
                                    Value = match.Value,
                                    Groups = groups.AsReadOnly(),
                                });
                            newMatches++;
                        }
                        Interlocked.Add(ref matchCount, 1);
                    }
                }
                Interlocked.Add(ref byteCount, data.Buf.Length - Overlap / 2);
                return result;
            }

            // private data
            readonly ConcurrentQueue<MatchData> matchQueue;
            readonly List<MatchData> matches;
            readonly System.Text.RegularExpressions.Regex regex;
            readonly int maxMatchCount;
            long matchCount;
        }
    }
}