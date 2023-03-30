using System;
using System.Linq;
using System.Threading;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Net;
using NUnit.Framework;
using System.Text.RegularExpressions;
using Imagibee.Gigantor;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Mono.Unix.Native;


namespace BenchmarkTesting {
    internal class ReadThroughputTester : Partitioner<PartitionData>
    {
        PartitionData result = new();
        [ThreadStatic] static byte[]? buffer;

        // Constructs a file based overhead tester
        public ReadThroughputTester(
            string path,
            AutoResetEvent progress,
            int partitionSize,
            int maxWorkers,
            BufferMode bufferMode) :
            base(
                path,
                progress,
                JoinMode.Sequential,
                partitionSize: partitionSize,
                maxWorkers: maxWorkers,
                overlap: 0,
                bufferMode: bufferMode)
        {
        }

        // Constructs a stream based overhead tester
        public ReadThroughputTester(
            System.IO.FileStream stream,
            AutoResetEvent progress,
            int partitionSize,
            int maxWorkers) :
            base(
                "",
                progress,
                JoinMode.Sequential,
                partitionSize: partitionSize,
                maxWorkers: maxWorkers)
        {
            Stream = stream;
        }

        protected override PartitionData Join(PartitionData a, PartitionData b)
        {
            return a;
        }

        protected override PartitionData Map(PartitionerData data)
        {
            // File based, read and throw away results
            if (data.Buf == null) {
                using var fileStream = Imagibee.Gigantor.FileStream.Create(
                    Path, bufferSize: partitionSize, bufferMode: bufferMode);
                fileStream.Seek(data.StartFpos, SeekOrigin.Begin);
                long pos = 0;
                var bytesRead = 0;
                if (buffer == null) {
                    buffer = new byte[partitionSize];
                }
                do {
                    bytesRead = fileStream.Read(buffer, 0, partitionSize);
                    Interlocked.Add(ref byteCount, bytesRead);
                    pos += bytesRead;
                }
                while (bytesRead == partitionSize && pos < partitionSize);
            }
            // Stream based, reading has already been done in Partition<T> base class
            else {
                Interlocked.Add(ref byteCount, data.Buf.Length);
            }
            return result;
        }

        internal static void ReadAndThrowAway(System.IO.FileStream stream, byte[] buf)
        {
            int bytesRead;
            do {
                bytesRead = stream.Read(buf, 0, buf.Length);
            }
            while (bytesRead == buf.Length);
        }
    }

    // disposes of 1 or more IDisposable
    class Disposables : IDisposable {
        List<IDisposable> disposables = new();
        public void Add(IDisposable d)
        {
            disposables.Add(d);
        }

        public void Dispose()
        {
            for (var i = disposables.Count - 1; i >= 0; i--) {
                disposables[i].Dispose();
            }
        }
    }

    public class BenchmarkTests {
        const string pattern = @"/https?:\/\/(www\.)?[-a-zA-Z0-9@:%._\+~#=]{2,256}\.[a-z]{2,6}\b([-a-zA-Z0-9@:%_\+.~#()?&//=]*)/";
        string testPath = "";
        Regex regex = new(pattern, RegexOptions.Compiled);

        [SetUp]
        public void Setup()
        {
            testPath = $"{Utilities.GetEnwik9()}x32";
        }

        public void BaselineReadThroughputTest(BufferMode bufferMode)
        {
            var partitionSize = 128 * 1024 * 1024;
            Console.WriteLine($"{TestContext.CurrentContext.Test.Name} " +
                $"{Utilities.FileByteCount(testPath) / 1e6} MByte " +
                $"file with {partitionSize} byte buffer size");
            var totalBytes = Utilities.FileByteCount(testPath);
            Stopwatch stopwatch = new();
            AutoResetEvent progress = new(false);
            using var fileStream = Imagibee.Gigantor.FileStream.Create(
                testPath, bufferSize: partitionSize, bufferMode: bufferMode);
            var buf = new byte[partitionSize];
            fileStream.Seek(0, SeekOrigin.Begin);
            stopwatch.Start();
            ReadThroughputTester.ReadAndThrowAway(fileStream, buf);
            stopwatch.Stop();
            Console.WriteLine(
                $"main thread: " +
                $"{totalBytes / stopwatch.Elapsed.TotalSeconds / 1e6} MBps");
            stopwatch.Reset();
            fileStream.Close();
        }

        public void FileReadThroughputTest(BufferMode bufferMode)
        {
            Stopwatch stopwatch = new();
            AutoResetEvent progress = new(false);
            List<Tuple<int, int>> cases = new()
            {
                new Tuple<int, int>(128 * 1024 * 1024, 1),
                new Tuple<int, int>(2048 * 1024, 1000),
                new Tuple<int, int>(1024 * 1024, 1000),
                new Tuple<int, int>(512 * 1024, 1000),
                new Tuple<int, int>(256 * 1024, 1000),
                new Tuple<int, int>(128 * 1024, 1000),
                new Tuple<int, int>(64 * 1024, 1000),
            };
            foreach (var c in cases) {
                ReadThroughputTester tester = new(
                    testPath,
                    progress,
                    partitionSize: c.Item1,
                    maxWorkers: c.Item2,
                    bufferMode: bufferMode);
                stopwatch.Start();
                Background.StartAndWait(
                    tester,
                    progress,
                    (_) => { },
                    1000);
                stopwatch.Stop();
                Console.WriteLine($"{TestContext.CurrentContext.Test.Name} " +
                    $"{tester.ByteCount / 1e6} MByte " +
                    $"file with {c.Item1} byte buffer size");
                Console.WriteLine(
                    $"{c.Item2} threads: " +
                    $"{tester.ByteCount / stopwatch.Elapsed.TotalSeconds / 1e6} MBps");
                stopwatch.Reset();
            }
        }


        public void StreamReadThroughputTest(BufferMode bufferMode)
        {
            Stopwatch stopwatch = new();
            AutoResetEvent progress = new(false);
            List<Tuple<int, int>> cases = new()
            {
                new Tuple<int, int>(128 * 1024 * 1024, 1),
                new Tuple<int, int>(32768 * 1024, 1000),
                new Tuple<int, int>(16384 * 1024, 1000),
                new Tuple<int, int>(8192 * 1024, 1000),
                new Tuple<int, int>(4096 * 1024, 1000),
            };
            foreach (var c in cases) {
                using var fileStream = Imagibee.Gigantor.FileStream.Create(
                    testPath, bufferSize: c.Item1, bufferMode: bufferMode);
                fileStream.Seek(0, SeekOrigin.Begin);
                ReadThroughputTester tester = new(
                    fileStream,
                    progress,
                    partitionSize: c.Item1,
                    maxWorkers: c.Item2);
                stopwatch.Start();
                Background.StartAndWait(
                    tester,
                    progress,
                    (_) => { },
                    1000);
                stopwatch.Stop();
                Console.WriteLine($"{TestContext.CurrentContext.Test.Name} " +
                    $"{tester.ByteCount / 1e6} MByte " +
                    $"file with {c.Item1} byte buffer size");
                Console.WriteLine(
                    $"{c.Item2} threads: " +
                    $"{tester.ByteCount / stopwatch.Elapsed.TotalSeconds / 1e6} MBps");
                stopwatch.Reset();
                fileStream.Close();
            }
        }

        public void LineIndexingTest(BufferMode bufferMode)
        {
            Stopwatch stopwatch = new();
            AutoResetEvent progress = new(false);
            List<Tuple<int, int>> cases = new()
            {
                new Tuple<int, int>(128  * 1024 * 1024, 1),
                new Tuple<int, int>(32768 * 1024, 1000),
                new Tuple<int, int>(16384 * 1024, 1000),
                new Tuple<int, int>(8192 * 1024, 1000),
                new Tuple<int, int>(4096 * 1024, 1000),
                new Tuple<int, int>(2048 * 1024, 1000),
                new Tuple<int, int>(1024 * 1024, 1000),
                new Tuple<int, int>(512 * 1024, 1000),
                new Tuple<int, int>(256 * 1024, 1000),
                new Tuple<int, int>(128 * 1024, 1000),
                new Tuple<int, int>(64 * 1024, 1000),
            };
            foreach (var c in cases) {
                LineIndexer indexer = new(
                    testPath,
                    progress,
                    partitionSize: c.Item1,
                    maxWorkers: c.Item2,
                    bufferMode: bufferMode);
                stopwatch.Start();
                Background.StartAndWait(
                    indexer,
                    progress,
                    (_) => { },
                    1000);
                stopwatch.Stop();
                Console.WriteLine($"{TestContext.CurrentContext.Test.Name} " +
                    $"{indexer.ByteCount / 1e6} MByte " +
                    $"file with {c.Item1} byte buffer size");
                Console.WriteLine(
                    $"{c.Item2} threads: " +
                    $"{indexer.ByteCount / stopwatch.Elapsed.TotalSeconds / 1e6} MBps " +
                    $"with {indexer.LineCount} lines");
                stopwatch.Reset();
            }
        }

        public void FileUnicornSearchTest(BufferMode bufferMode)
        {
            Stopwatch stopwatch = new();
            AutoResetEvent progress = new(false);
            var bufSize = 256 * 1024;
            var numTrials = 4;
            var totalThroughput = 0.0;
            for (var i=0; i<numTrials; i++) {
                RegexSearcher searcher = new(
                    testPath,
                    new Regex("unicorn", RegexOptions.Compiled),
                    progress,
                    partitionSize: bufSize,
                    maxWorkers: 1000,
                    bufferMode: bufferMode,
                    overlap: 8);
                stopwatch.Start();
                Background.StartAndWait(
                    searcher,
                    progress,
                    (_) => { },
                    1000);
                stopwatch.Stop();
                var throughput = searcher.ByteCount / stopwatch.Elapsed.TotalSeconds / 1e6;
                totalThroughput += throughput;
                Console.WriteLine(
                    $"{TestContext.CurrentContext.Test.Name} " +
                    $"{throughput} MBps " +
                    $"with {searcher.MatchCount} matches");
                stopwatch.Reset();
            }
            Console.WriteLine($"average throughput is {totalThroughput/numTrials} MBps");
        }

        public void FileURLSearchTest(BufferMode bufferMode)
        {
            Stopwatch stopwatch = new();
            AutoResetEvent progress = new(false);
            List<Tuple<int, int>> cases = new()
            {
                new Tuple<int, int>(128 * 1024 * 1024, 1),
                new Tuple<int, int>(4096 * 1024, 1000),
                new Tuple<int, int>(2048 * 1024, 1000),
                new Tuple<int, int>(1024 * 1024, 1000),
                new Tuple<int, int>(512 * 1024, 1000),
                new Tuple<int, int>(256 * 1024, 1000),
                new Tuple<int, int>(128 * 1024, 1000),
                new Tuple<int, int>(64 * 1024, 1000),
            };
            foreach (var c in cases) {
                RegexSearcher searcher = new(
                    testPath,
                    regex,
                    progress,
                    partitionSize: c.Item1,
                    maxWorkers: c.Item2,
                    maxMatchCount: 100000,
                    bufferMode: bufferMode);
                stopwatch.Start();
                Background.StartAndWait(
                    searcher,
                    progress,
                    (_) => { },
                    1000);
                stopwatch.Stop();
                Console.WriteLine($"{TestContext.CurrentContext.Test.Name} " +
                    $"{searcher.ByteCount / 1e6} MByte " +
                    $"file with {c.Item1} byte buffer size");
                Console.WriteLine(
                    $"{c.Item2} threads: " +
                    $"{searcher.ByteCount / stopwatch.Elapsed.TotalSeconds / 1e6} MBps " +
                    $"with {searcher.MatchCount} matches");
                stopwatch.Reset();
            }
        }

        public void StreamURLSearchTest(BufferMode bufferMode)
        {
            Stopwatch stopwatch = new();
            AutoResetEvent progress = new(false);
            List<Tuple<int, int>> cases = new()
            {
                new Tuple<int, int>(128  * 1024 * 1024, 1),
                new Tuple<int, int>(4096 * 1024, 1000),
                new Tuple<int, int>(2048 * 1024, 1000),
                new Tuple<int, int>(1024 * 1024, 1000),
                new Tuple<int, int>(512 * 1024, 1000),
                new Tuple<int, int>(256 * 1024, 1000),
                new Tuple<int, int>(128 * 1024, 1000),
                new Tuple<int, int>(64 * 1024, 1000),
            };
            foreach (var c in cases) {
                using var fileStream = Imagibee.Gigantor.FileStream.Create(
                    testPath, bufferSize: c.Item1, bufferMode: bufferMode);
                fileStream.Seek(0, SeekOrigin.Begin);
                RegexSearcher searcher = new(
                    fileStream,
                    regex,
                    progress,
                    partitionSize: c.Item1,
                    maxWorkers: c.Item2,
                    maxMatchCount: 100000);
                stopwatch.Start();
                Background.StartAndWait(
                    searcher,
                    progress,
                    (_) => { },
                    1000);
                stopwatch.Stop();
                Console.WriteLine($"{TestContext.CurrentContext.Test.Name} " +
                    $"{searcher.ByteCount / 1e6} MByte " +
                    $"file with {c.Item1} byte buffer size");
                Console.WriteLine(
                    $"{c.Item2} threads: " +
                    $"{searcher.ByteCount / stopwatch.Elapsed.TotalSeconds / 1e6} MBps " +
                    $"with {searcher.MatchCount} matches");
                stopwatch.Reset();
                fileStream.Close();
            }
        }

        public void CompressedStreamURLSearchTest(BufferMode bufferMode)
        {
            Stopwatch stopwatch = new();
            AutoResetEvent progress = new(false);
            List<Tuple<int, int>> cases = new()
            {
                new Tuple<int, int>(128 * 1024 * 1024, 1),
                new Tuple<int, int>(32768 * 1024, 1000),
                new Tuple<int, int>(16384 * 1024, 1000),
                new Tuple<int, int>(8192 * 1024, 1000),
                new Tuple<int, int>(4096 * 1024, 1000),
            };
            foreach (var c in cases) {
                using var fileStream = Imagibee.Gigantor.FileStream.Create(
                    $"{testPath}.gz", bufferSize: c.Item1, bufferMode: bufferMode);
                fileStream.Seek(0, SeekOrigin.Begin);
                using var gzStream = new GZipStream(
                    fileStream, CompressionMode.Decompress, true);
                RegexSearcher searcher = new(
                    gzStream,
                    regex,
                    progress,
                    partitionSize: c.Item1,
                    maxWorkers: c.Item2,
                    maxMatchCount: 100000);
                stopwatch.Start();
                Background.StartAndWait(
                    searcher,
                    progress,
                    (_) => { },
                    1000);
                stopwatch.Stop();
                Console.WriteLine($"{TestContext.CurrentContext.Test.Name} " +
                    $"{searcher.ByteCount / 1e6} MByte " +
                    $"file with {c.Item1} byte buffer size");
                Console.WriteLine(
                    $"{c.Item2} threads: " +
                    $"{searcher.ByteCount / stopwatch.Elapsed.TotalSeconds / 1e6} MBps " +
                    $"with {searcher.MatchCount} matches");
                stopwatch.Reset();
                gzStream.Close();
                fileStream.Close();
            }
        }

        public void FileMultipleURLSearchTest(BufferMode bufferMode)
        {
            var partitionSize = 256 * 1024;
            var maxWorkers = 1000;
            var maxRegexes = 10;
            Stopwatch stopwatch = new();
            AutoResetEvent progress = new(false);
            for (var numRegexes=2; numRegexes<=maxRegexes; numRegexes++) {
                List<Regex> regexs = new List<Regex>();
                for (var i = 0; i < numRegexes; i++) {
                    regexs.Add(new(pattern, RegexOptions.Compiled));
                }
                RegexSearcher searcher = new(
                    testPath,
                    regexs,
                    progress,
                    partitionSize: partitionSize,
                    maxWorkers: maxWorkers,
                    maxMatchCount: 1000000,
                    bufferMode: bufferMode);
                stopwatch.Start();
                Background.StartAndWait(
                    searcher,
                    progress,
                    (_) => { },
                    1000);
                stopwatch.Stop();
                var byteCount = searcher.ByteCount * numRegexes;
                Console.WriteLine($"{numRegexes} x {TestContext.CurrentContext.Test.Name} " +
                    $"{byteCount / 1e6} MByte " +
                    $"data with {partitionSize} byte buffer size");
                Console.WriteLine(
                    $"{maxWorkers} threads: " +
                    $"{byteCount / stopwatch.Elapsed.TotalSeconds / 1e6} MBps " +
                    $"with {searcher.MatchCount} matches");
                stopwatch.Reset();
            }
        }

        public void MultipleCompressedStreamURLSearchTest(BufferMode bufferMode)
        {
            var partitionSize = 8192 * 1024;
            var maxWorkers = 1000;
            var maxFiles = 5;
            Stopwatch stopwatch = new();
            AutoResetEvent progress = new(false);
            List<System.IO.FileStream> files = new();
            List<GZipStream> gZips = new();
            Disposables disposables = new();
            for (var i=0; i<maxFiles; i++) {
                var path = $"{testPath}-{i}.gz";
                if (i==0) {
                    path = $"{testPath}.gz";
                }
                var f = Imagibee.Gigantor.FileStream.Create(
                    path, bufferSize: partitionSize, bufferMode: bufferMode);
                var g = new GZipStream(f, CompressionMode.Decompress, true);
                files.Add(f);
                gZips.Add(g);
                disposables.Add(f);
                disposables.Add(g);
            }
            using (disposables) {
                for (var numFiles = 2; numFiles <= maxFiles; numFiles++) {
                    List<RegexSearcher> searchers = new();
                    List<IBackground> processes = new();
                    for (var j = 0; j < numFiles; j++) {
                        files[j].Seek(0, SeekOrigin.Begin);
                        RegexSearcher s = new(
                            gZips[j],
                            new Regex(pattern, RegexOptions.Compiled),
                            progress,
                            partitionSize: partitionSize,
                            maxWorkers: maxWorkers,
                            maxMatchCount: 100000);
                        searchers.Add(s);
                        processes.Add(s);
                    }
                    stopwatch.Start();
                    Background.StartAndWait(
                        processes,
                        progress,
                        (_) => { },
                        1000);
                    stopwatch.Stop();
                    long byteCount = 0;
                    long matchCount = 0;
                    foreach (var s in searchers) {
                        byteCount += s.ByteCount;
                        matchCount += s.MatchCount;
                    }
                    Console.WriteLine($"{numFiles} x {TestContext.CurrentContext.Test.Name} " +
                        $"{byteCount / 1e6} MByte " +
                        $"data with {partitionSize} byte buffer size");
                    Console.WriteLine(
                        $"{maxWorkers} threads: " +
                        $"{byteCount / stopwatch.Elapsed.TotalSeconds / 1e6} MBps " +
                        $"with {matchCount} matches");
                    stopwatch.Reset();
                }
            }
        }

        [Test, Order(1)]
        public void BufferedBaselineReadThroughputTest()
        {
            BaselineReadThroughputTest(BufferMode.Buffered);
        }

        [Test, Order(2)]
        public void UnbufferedBaselineReadThroughputTest()
        {
            BaselineReadThroughputTest(BufferMode.Unbuffered);
        }

        [Test, Order(3)]
        public void BufferedFileReadThroughputTest()
        {
            FileReadThroughputTest(BufferMode.Buffered);
        }

        [Test, Order(4)]
        public void UnbufferedFileReadThroughputTest()
        {
            FileReadThroughputTest(BufferMode.Unbuffered);
        }

        [Test, Order(5)]
        public void BufferedStreamReadThroughputTest()
        {
            StreamReadThroughputTest(BufferMode.Buffered);
        }

        [Test, Order(6)]
        public void UnbufferedStreamReadThroughputTest()
        {
            StreamReadThroughputTest(BufferMode.Unbuffered);
        }

        [Test, Order(7)]
        public void BufferedLineIndexingTest()
        {
            LineIndexingTest(BufferMode.Buffered);
        }

        [Test, Order(8)]
        public void UnbufferedLineIndexingTest()
        {
            LineIndexingTest(BufferMode.Unbuffered);
        }

        [Test, Order(9)]
        public void BufferedFileURLSearchTest()
        {
            FileURLSearchTest(BufferMode.Buffered);
        }

        [Test, Order(10)]
        public void UnbfferedFileURLSearchTest()
        {
            FileURLSearchTest(BufferMode.Unbuffered);
        }

        [Test, Order(11)]
        public void BufferedStreamURLSearchTest()
        {
            StreamURLSearchTest(BufferMode.Buffered);
        }

        [Test, Order(12)]
        public void UnbufferedStreamURLSearchTest()
        {
            StreamURLSearchTest(BufferMode.Unbuffered);
        }

        [Test, Order(13)]
        public void BufferedCompressedStreamURLSearchTest()
        {
            CompressedStreamURLSearchTest(BufferMode.Buffered);
        }

        [Test, Order(14)]
        public void UnbufferedCompressedStreamURLSearchTest()
        {
            CompressedStreamURLSearchTest(BufferMode.Unbuffered);
        }

        [Test, Order(15)]
        public void BufferedFileMultipleURLSearchTest()
        {
            FileMultipleURLSearchTest(BufferMode.Buffered);
        }

        [Test, Order(16)]
        public void UnbfferedFileMultipleURLSearchTest()
        {
            FileMultipleURLSearchTest(BufferMode.Unbuffered);
        }

        [Test, Order(17)]
        public void BufferedMultipleCompressedStreamURLSearchTest()
        {
            MultipleCompressedStreamURLSearchTest(BufferMode.Buffered);
        }

        [Test, Order(18)]
        public void UnbufferedMultipleCompressedStreamURLSearchTest()
        {
            MultipleCompressedStreamURLSearchTest(BufferMode.Unbuffered);
        }

        [Test, Order(19)]
        public void UnbufferedFileUnicornSearchTest()
        {
            FileUnicornSearchTest(BufferMode.Unbuffered);
        }
    }
}

