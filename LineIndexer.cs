using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.IO;

namespace Imagibee {
    namespace Gigantor {
        //
        // Optimized line counting and random access for very large files
        //
        // The file is processed in the background to get a total LineCount 
        // and to create an index.  The index is cached and used by subsequent
        // calls to optimize random access to the file by either line or
        // position.
        //
        // Users should begin the process by calling Start.  All public
        // methods and properties are well-behaved while the background
        // process is running.
        //
        // After the process is finished, the results are kept until Start
        // is called again.  However, calls to Start while Running is true
        // are ignored.
        //
        // Exceptions during the background processing are caught and
        // stored in Error.  Exceptions during Start are not handled.
        //
        // A balance between memory footprint and performance can be achieved
        // by varying chunkKiBytes and maxWorkers parameters.
        //
        public class LineIndexer : FileMapJoin<LineIndexerData> {
            // The number of lines that have been indexed so far
            public long LineCount { get { return Interlocked.Read(ref lineCount); } }

            // Create a new instance
            //
            // filePath - the path to the file to process
            // progress - signaled each time LineCount is updated
            // chunkKiBytes - the chunk size in KiBytes that each worker works on
            // maxWorkers - optional limit to the maximum number of simultaneous workers
            public LineIndexer(
                string filePath,
                AutoResetEvent progress,
                int chunkKiBytes=512,
                int maxWorkers=0) : base(
                    filePath,
                    progress,
                    JoinMode.Sequential,
                    chunkKiBytes,
                    maxWorkers)
            {
            }

            // Start the background process
            public override void Start()
            {
                if (!Running) {
                    Interlocked.Exchange(ref lineCount, 0);
                    chunkQueue = new();
                    chunks = new();
                    base.Start();
                }
            }

            protected override void Finish()
            {
                while (chunkQueue.TryDequeue(out LineIndexerData result)) {
                    chunks.Add(result);
                }
                chunks = chunks.OrderBy(x => x.Id).ToList();
            }

            // Get the fpos of a line
            //
            // line - the line number starting from 1
            //
            // Return the fpos at the beginning of line or -1 if none exists
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

            // Get the line of an fpos
            //
            // fpos - the byte offset from the beginning of the file
            //
            // Returns the line number that contains fpos or -1 if none exists
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

            //
            // END OF PUBLIC INTERFACE
            //

            LineIndexerData? GetChunk(long line)
            {
                if (line > 0 && line <= LineCount && chunks.Count > 0) {
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

            protected override LineIndexerData Join(LineIndexerData a, LineIndexerData b)
            {
                var currentChunk = new LineIndexerData()
                {
                    Id = b.Id,
                    StartLine = 1,
                    LineCount = b.LineCount,
                    EndLine = b.LineCount,
                    StartFpos = b.StartFpos,
                    EolEnding = b.EolEnding,
                    FirstEolOffset = b.FirstEolOffset,
                    ByteCount = b.ByteCount,
                };
                //Logger.Log($"chunk prior {a.Id} current {b.Id} line count {LineCount} ");
                if (a.Id != b.Id) {
                    var priorChunk = a;
                    currentChunk.StartLine = priorChunk.EndLine + 1;
                    currentChunk.EndLine = priorChunk.EndLine + b.LineCount;
                    if (priorChunk.EolEnding == false) {
                        // adjustment since the first line was counted in the prior chunk
                        currentChunk.StartLine += 1;
                        priorChunk.EndLine += 1;
                        currentChunk.StartFpos += b.FirstEolOffset + 1;
                    }
                    chunkQueue.Enqueue(priorChunk);
                    Interlocked.Add(ref lineCount, priorChunk.LineCount);
                    Interlocked.Add(ref byteCount, priorChunk.ByteCount);
                    //Logger.Log($"joined {priorChunk.Id} at {priorChunk.StartFpos} between {priorChunk.StartLine} and {priorChunk.EndLine}, {priorChunk.LineCount} lines, {priorChunk.ByteCount} bytes");
                }
                if (b.FinalChunk) {
                    if (b.EolEnding == false) {
                        // adjustment for last chunk not ending on eol
                        currentChunk.EndLine += 1;
                    }
                    chunkQueue.Enqueue(currentChunk);
                    Interlocked.Add(ref lineCount, currentChunk.LineCount);
                    Interlocked.Add(ref byteCount, currentChunk.ByteCount);
                    //Logger.Log($"*joined {currentChunk.Id} between {currentChunk.StartLine} and {currentChunk.EndLine} at {currentChunk.StartFpos}");
                }
                return currentChunk;
            }

            protected override LineIndexerData Map(FileMapJoinData data)
            {
                //Logger.Log($"mapping chunk {data.Id} from {Path} at {data.StartFpos}");
                var result = new LineIndexerData()
                {
                    Id = data.Id,
                    EolEnding = false,
                    LineCount = 0,
                    ByteCount = 0,
                    StartFpos = data.StartFpos,
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
                fileStream.Seek(data.StartFpos, SeekOrigin.Begin);
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
                //Logger.Log($"mapped {result.Id} at {result.StartFpos}, {result.LineCount} lines, {result.ByteCount} bytes");
                return result;
            }

            // private data
            ConcurrentQueue<LineIndexerData> chunkQueue;
            List<LineIndexerData> chunks;
            long lineCount;
        }

        // FileMapJoin job data used internally but it must be declared public
        public struct LineIndexerData : IMapJoinData {
            public int Id { get; set; }
            public int Cycle { get; set; }
            public long StartFpos;
            public long LineCount;
            public long StartLine;
            public long EndLine;
            public int ByteCount;
            public bool EolEnding;
            public int FirstEolOffset;
            public bool FinalChunk;
        }
    }
}