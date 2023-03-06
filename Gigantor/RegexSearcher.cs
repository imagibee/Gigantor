using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.IO;
using System;
using System.IO.Pipes;

namespace Imagibee {
    namespace Gigantor {
        //
        // Fast regex searching of gigantic files
        //
        // The search process runs in the background, seperating the file into
        // chunks, and searching the chunks in parallel.  The search results
        // of each chunk are ultimately joined into a continuous result which
        // is accessible through the GetMatchData method after the search
        // completes.
        //
        // Users should instantiate an instance with the file or stream to
        // search and a Regex instance to perform the search.  Use the helper
        // functions from the Background class to control the process including
        // start, wait, cancel, and error detection.
        //
        // After the process is finished, the results are stored until Start
        // is called again.  Although, calling Start while Running is true
        // will have no effect.
        //
        // Exceptions during the background processing are caught and
        // stored in Error.
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

            // Create a new instance to search a file
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
            // bufferMode - choose whether or not files are buffered, for gigantic files
            // unbuffered tends to be faster
            public RegexSearcher(
                string filePath,
                System.Text.RegularExpressions.Regex regex,
                AutoResetEvent progress,
                int maxMatchCount=1000,
                int chunkKiBytes=512,
                int maxWorkers=64,
                int overlap = 512,
                BufferMode bufferMode = BufferMode.Unbuffered) : base(
                    filePath,
                    progress,
                    JoinMode.None,
                    chunkKiBytes,
                    maxWorkers: maxWorkers,
                    overlap: overlap,
                    bufferMode: bufferMode)
            {
                matches = new();
                matchQueue = new();
                this.regex = regex;
                this.maxMatchCount = maxMatchCount;
                this.overlap = overlap;
            }

            // Create a new instance to search a stream
            //
            // stream - the stream to search
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
                Stream stream,
                System.Text.RegularExpressions.Regex regex,
                AutoResetEvent progress,
                int maxMatchCount = 1000,
                int chunkKiBytes = 512,
                int maxWorkers = 64,
                int overlap = 512) : base(
                    "",
                    progress,
                    JoinMode.None,
                    chunkKiBytes,
                    maxWorkers: maxWorkers,
                    overlap: overlap)
            {
                matches = new();
                matchQueue = new();
                this.regex = regex;
                this.maxMatchCount = maxMatchCount;
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
                // Adjust byte count for overlap
                Interlocked.Add(ref byteCount, overlap);
            }

            // Return the MatchData of the completed search
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
                    using var fileStream = FileStream.Create(
                        Path, bufferSize: chunkSize, bufferMode: bufferMode);
                    fileStream.Seek(data.StartFpos, SeekOrigin.Begin);
                    data.Buf = new byte[chunkSize];
                    var bytesRead = fileStream.Read(data.Buf, 0, chunkSize);
                    if (bytesRead != chunkSize) {
                        var buf = new byte[bytesRead];
                        Array.Copy(data.Buf, buf, bytesRead);
                        data.Buf = buf;
                    }
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
                Interlocked.Add(ref byteCount, data.Buf.Length - overlap);
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