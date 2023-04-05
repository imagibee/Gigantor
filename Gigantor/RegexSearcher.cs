using System;
using System.Text;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.IO;
using System.Text.RegularExpressions;

namespace Imagibee {
    namespace Gigantor {
        //
        // Fast regex search/replace of gigantic files
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
        // by varying maxMatchCount, partitionSize and maxWorkers parameters.
        //
        public class RegexSearcher : Partitioner<PartitionData> {
            // The number of matches found so far
            public long MatchCount { get { return matchCount; } }

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
                public int RegexIndex;
            }

            // Create a new instance to search a file with single regex
            //
            // filePath - the path to the file to search
            // regex - the regular expression to match against the file
            // progress - signaled each time MatchCount is updated
            // maxMatchCount - places an approximate limit on the number of matches, defaults to 100000
            // partitionSize - the chunk size in bytes that each worker works on,
            // defaults to 256 KiB
            // maxWorkers - optional limit to the maximum number of simultaneous workers,
            // defaults to unlimited
            // overlap - size in bytes of partition overlap, used for finding matches,
            // that span two partitions, may not exceed half the chunk size, defaults to 1024
            public RegexSearcher(
                string filePath,
                System.Text.RegularExpressions.Regex regex,
                AutoResetEvent progress,
                int maxMatchCount = 100000,
                int partitionSize = 256 * 1024,
                int maxWorkers = 0,
                int overlap = 1024) : this(
                    filePath,
                    new List<System.Text.RegularExpressions.Regex>() { regex },
                    progress,
                    maxMatchCount,
                    partitionSize,
                    maxWorkers,
                    overlap)
            {
            }

            // Create a new instance to search a file with multiple regex
            //
            // filePath - the path to the file to search
            // regexs - the list of regular expression to match against the file
            // progress - signaled each time MatchCount is updated
            // maxMatchCount - places an approximate limit on the number of matches, defaults to 100000
            // partitionSize - the chunk size in bytes that each worker works on,
            // defaults to 256 KiB
            // maxWorkers - optional limit to the maximum number of simultaneous workers,
            // defaults to unlimited
            // overlap - size in bytes of partition overlap, used for finding matches,
            // that span two partitions, may not exceed half the chunk size, defaults to 1024
            public RegexSearcher(
                string filePath,
                List<System.Text.RegularExpressions.Regex> regexs,
                AutoResetEvent progress,
                int maxMatchCount = 100000,
                int partitionSize = 256 * 1024,
                int maxWorkers = 0,
                int overlap = 1024) : base(
                    filePath,
                    progress,
                    JoinMode.None,
                    partitionSize,
                    maxWorkers: maxWorkers,
                    overlap: overlap)
            {
                matches = new();
                matchQueue = new();
                this.regexs = regexs;
                this.maxMatchCount = maxMatchCount;
            }

            // Create a new instance to search a stream with single regex
            //
            // stream - the stream to search
            // regex - the regular expression to match against the stream
            // progress - signaled each time MatchCount is updated
            // maxMatchCount - places an approximate limit on the number of matches, defaults to 100000
            // partitionSize - the chunk size in bytes that each worker works on,
            // defaults to 4 MiB
            // maxWorkers - optional limit to the maximum number of simultaneous workers,
            // defaults to unlimited
            // overlap - size in bytes of partition overlap, used for finding matches,
            // that span two partitions, may not exceed half the chunk size, defaults to 1024
            public RegexSearcher(
                Stream stream,
                System.Text.RegularExpressions.Regex regex,
                AutoResetEvent progress,
                int maxMatchCount = 100000,
                int partitionSize = 4096 * 1024,
                int maxWorkers = 0,
                int overlap = 1024) : this(
                    stream,
                    new List<System.Text.RegularExpressions.Regex>() { regex },
                    progress,
                    maxMatchCount,
                    partitionSize,
                    maxWorkers,
                    overlap)
            {
            }

            // Create a new instance to search a stream with multiple regex
            //
            // stream - the stream to search
            // regexs - the list of regular expressions to match against the stream
            // progress - signaled each time MatchCount is updated
            // maxMatchCount - places an approximate limit on the number of matches, defaults to 100000
            // partitionSize - the chunk size in bytes that each worker works on,
            // defaults to 4 MiB
            // maxWorkers - optional limit to the maximum number of simultaneous workers,
            // defaults to unlimited
            // overlap - size in bytes of partition overlap, used for finding matches,
            // that span two partitions, may not exceed half the chunk size, defaults to 1024
            public RegexSearcher(
                Stream stream,
                List<System.Text.RegularExpressions.Regex> regexs,
                AutoResetEvent progress,
                int maxMatchCount = 100000,
                int partitionSize = 4096 * 1024,
                int maxWorkers = 0,
                int overlap = 1024) : base(
                    "",
                    progress,
                    JoinMode.None,
                    partitionSize,
                    maxWorkers: maxWorkers,
                    overlap: overlap)
            {
                matches = new();
                matchQueue = new();
                this.regexs = regexs;
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
                var dedupedMatchCount = 0;
                HashSet<long> matchPositions = new();
                var matchQueue = this.matchQueue;
                while (matchQueue.TryDequeue(out MatchData result)) {
                    // Ignore duplicates
                    if (!matchPositions.Contains(result.StartFpos)) {
                        matches.Add(result);
                        matchPositions.Add(result.StartFpos);
                    }
                }
                //matches = matches.OrderBy(x => x.StartFpos).ToList();
                matches.Sort((a, b) => a.StartFpos.CompareTo(b.StartFpos));
                dedupedMatchCount += matches.Count;
                // Adjust matchCount after dedup
                matchCount = dedupedMatchCount;
                // Adjust byte count for overlap
                Interlocked.Add(ref byteCount, overlap);
            }

            // Return the MatchData of the completed search sorted by fpos
            public IReadOnlyList<MatchData> GetMatchData()
            {
                if (Running) {
                    return new List<MatchData>().AsReadOnly();
                }
                else {
                    return matches.AsReadOnly();
                }
            }

            // Replace matches after a completed search
            //
            // Data in Path that is between matches is copied as-is to the output stream.
            // When a match is encountered matchEvaluator is called to determine how to
            // replace the match.  To erase the match the matchEvaluator should return an
            // empty string.  To overwrite the match with a new value the matchEvaluator
            // should return the new value.  To keep the existing match value the
            // matchEvaluator should return the existing match value.  Only works for
            // file mode searches.
            //
            // output - the open output stream to receive the data
            // matchEvaluator - callback to handle replacements
            // encoding - the encoding of the replacement strings, defaults to UTF8
            public void Replace(Stream output, Func<MatchData, string> matchEvaluator, Encoding? encoding = null)
            {
                encoding ??= Encoding.UTF8;
                const int bufSize = 128 * 1024 * 1024;
                if (!Running && Stream == null) {
                    var buf = new byte[bufSize];
                    using System.IO.FileStream input = Imagibee.Gigantor.FileStream.Create(Path, bufSize);
                    long endPos = input.Seek(0, SeekOrigin.End);
                    long readPos = input.Seek(0, SeekOrigin.Begin);
                    foreach (var match in matches) {
                        CopyBetweenMatches(buf, input, output, match.StartFpos);
                        CopyAtMatch(output, matchEvaluator(match), encoding);
                        input.Position += match.Value.Length;
                    }
                    CopyBetweenMatches(buf, input, output, endPos);
                }
            }

            protected override PartitionData Join(PartitionData a, PartitionData b)
            {
                return a;
            }

            protected override PartitionData Map(PartitionerData data)
            {
                var str = Utilities.UnsafeByteToString(data.Buf);
                for (var i = 0; i < regexs.Count; i++) {
                    DoMatch(data, str, i);
                }
                var bufLen = data.Buf == null ? 0:data.Buf.Length;
                Interlocked.Add(ref byteCount, bufLen - overlap);
                return new PartitionData();
            }

            void DoMatch(PartitionerData data, string partition, int regexIndex)
            {
                var partitionMatches = regexs[regexIndex].Matches(partition);
                if (partitionMatches.Count > 0) {
                    for (int i = 0; i < partitionMatches.Count; i++) {
                        System.Text.RegularExpressions.Match match = partitionMatches[i];
                        if (match != null && matchQueue.Count < maxMatchCount) {
                            var groups = new List<GroupData>();
                            for (var j=0; j<match.Groups.Count; j++)  {
                                var group = match.Groups[j];
                                List<CaptureData> cd = new();
                                for (var k=0; k<group.Captures.Count; k++) {
                                    var capture = group.Captures[k];
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
                                    RegexIndex = regexIndex,
                                });
                        }
                    }
                }
            }

            void CopyBetweenMatches(byte[] buf, Stream input, Stream output, long endPos)
            {
                while (input.Position < endPos) {
                    int copySize = (int)Math.Min(endPos - input.Position, buf.Length);
                    input.Read(buf, 0, copySize);
                    output.Write(buf, 0, copySize);
                }
            }

            void CopyAtMatch(Stream output, string replacement, Encoding encoding)
            {
                output.Write(encoding.GetBytes(replacement));
            }

            // private data
            readonly ConcurrentQueue<MatchData> matchQueue;
            readonly List<MatchData> matches;
            readonly List<System.Text.RegularExpressions.Regex> regexs;
            readonly int maxMatchCount;
            long matchCount;
        }
    }
}