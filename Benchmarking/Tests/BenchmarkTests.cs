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

#pragma warning disable CS8618

namespace BenchmarkTesting {
    internal class OverheadTester : FileMapJoin<MapJoinData>
    {
        MapJoinData result = new();

        public OverheadTester(
            string path,
            AutoResetEvent progress,
            int chunkSize,
            int maxWorkers) :
            base(
                path,
                progress,
                JoinMode.Sequential,
                chunkKiBytes: chunkSize,
                maxWorkers: maxWorkers)
        {
            this.chunkSize = chunkSize;
        }

        public OverheadTester(
            FileStream stream,
            AutoResetEvent progress,
            int chunkSize,
            int maxWorkers) :
            base(
                "",
                progress,
                JoinMode.Sequential,
                chunkKiBytes: chunkSize,
                maxWorkers: maxWorkers)
        {
            this.chunkSize = chunkSize;
            Stream = stream;
        }

        protected override MapJoinData Join(MapJoinData a, MapJoinData b)
        {
            return a;
        }

        protected override MapJoinData Map(FileMapJoinData data)
        {
            //Console.WriteLine($"chunk {data.Id} at {data.StartFpos}");
            var bufferSize = 500 * 1000;
            if (data.Buf == null) {
                using var fileStream = new FileStream(
                    Path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    chunkSize,
                    FileOptions.Asynchronous);
                fileStream.Seek(data.StartFpos, SeekOrigin.Begin);
                long pos = 0;
                var bytesRead = 0;
                byte[] buffer = new byte[bufferSize];
                do {
                    bytesRead = fileStream.Read(buffer, 0, bufferSize);
                    Interlocked.Add(ref byteCount, bytesRead);
                    pos += bytesRead;
                }
                while (bytesRead == bufferSize && pos < chunkSize);
            }
            Interlocked.Add(ref byteCount, bufferSize);
            return result;
        }
    }

    public class BenchmarkTests {
        string enwik9Path;

        [SetUp]
        public void Setup()
        {
            enwik9Path = Utilities.GetEnwik9();
        }


        // Example code
        public void Benchmark(string path, string word)
        {
            // Create the dependencies we will need
            Regex regex = new(word, RegexOptions.Compiled);
            AutoResetEvent progress = new(false);
            Stopwatch stopwatch = new();

            // Benchmark results with differing number of threads
            foreach (var numWorkers in new List<int>() { 1, 128 }) {
                Console.WriteLine($"Starting search with {numWorkers} thread(s)");
                stopwatch.Start();

                // Create the searcher
                RegexSearcher searcher = new(
                    path,
                    regex,
                    progress,
                    maxMatchCount: 1000,
                    maxWorkers: numWorkers);

                // Start and wait for completion
                Background.StartAndWait(
                    new List<IBackground>() { searcher },
                    progress,
                    (_) => { Console.Write("."); },
                    1000);
                Console.Write('\n');

                // Display results
                var runTime = stopwatch.Elapsed.TotalSeconds;
                Console.WriteLine($"Completed in {runTime} seconds");
                stopwatch.Reset();
            }
        }

        [Test]
        public void FileOverheadTest()
        {
            var totalBytes = Utilities.FileByteCount(enwik9Path);
            Stopwatch stopwatch = new();
            AutoResetEvent progress = new(false);
            Console.WriteLine("File overhead");
            foreach (var numWorkers in new List<int>() { 1, 2, 4, 8, 16, 32, 64, 128 }) {
                OverheadTester tester = new(
                    enwik9Path,
                    progress,
                    (int)(totalBytes / numWorkers),
                    numWorkers);
                stopwatch.Start();
                Background.StartAndWait(
                    tester,
                    progress,
                    (_) => { },
                    1000);
                stopwatch.Stop();
                Console.WriteLine(
                    $"{numWorkers} threads " +
                    $"{tester.ByteCount / stopwatch.Elapsed.TotalSeconds / 1e6} MBps, " +
                    $"{tester.ByteCount / 1e6} MBytes, {stopwatch.Elapsed.TotalSeconds} seconds ({numWorkers} threads)");
                stopwatch.Reset();
            }
        }

        [Test]
        public void StreamOverheadTest()
        {
            var bufferSize = 500 * 1000;
            var totalBytes = Utilities.FileByteCount(enwik9Path);
            Stopwatch stopwatch = new();
            AutoResetEvent progress = new(false);
            Console.WriteLine("Stream overhead");
            using var fileStream = new FileStream(
                enwik9Path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize,
                FileOptions.Asynchronous);
            foreach (var numWorkers in new List<int>() { 1, 2, 4, 8, 16, 32, 64, 128 }) {
                fileStream.Seek(0, SeekOrigin.Begin);
                OverheadTester tester = new(
                    fileStream,
                    progress,
                    bufferSize,
                    numWorkers);
                stopwatch.Start();
                Background.StartAndWait(
                    tester,
                    progress,
                    (_) => { },
                    1000);
                stopwatch.Stop();
                Console.WriteLine(
                    $"{numWorkers} threads " +
                    $"{tester.ByteCount / stopwatch.Elapsed.TotalSeconds / 1e6} MBps, " +
                    $"{tester.ByteCount / 1e6} MBytes, {stopwatch.Elapsed.TotalSeconds} seconds ({numWorkers} threads)");
                stopwatch.Reset();
            }
            fileStream.Close();
        }

        [Test]
        public void UnbufferedOverheadTest()
        {
            var bufferSize = 500 * 1000;
            var totalBytes = Utilities.FileByteCount(enwik9Path);
            Stopwatch stopwatch = new();
            AutoResetEvent progress = new(false);
            Console.WriteLine("Unbuffered overhead");
            using var fileStream = UncachedReadStream(
                enwik9Path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize,
                FileOptions.Asynchronous);
            foreach (var numWorkers in new List<int>() { 1, 2, 4, 8, 16, 32, 64, 128 }) {
                fileStream.Seek(0, SeekOrigin.Begin);
                OverheadTester tester = new(
                    fileStream,
                    progress,
                    bufferSize,
                    numWorkers);
                stopwatch.Start();
                Background.StartAndWait(
                    tester,
                    progress,
                    (_) => { },
                    1000);
                stopwatch.Stop();
                Console.WriteLine(
                    $"{numWorkers} threads " +
                    $"{tester.ByteCount / stopwatch.Elapsed.TotalSeconds / 1e6} MBps, " +
                    $"{tester.ByteCount / 1e6} MBytes, {stopwatch.Elapsed.TotalSeconds} seconds ({numWorkers} threads)");
                stopwatch.Reset();
            }
            fileStream.Close();
        }

        // See http://saplin.blogspot.com/2018/07/non-cachedunbuffered-file-operations.html
        internal static FileStream UncachedReadStream(
            string path,
            FileMode fileMode,
            FileAccess fileAccess,
            FileShare fileShare,
            int bufferSize,
            FileOptions fileOptions)
        {
            if (RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)) {
                throw new NotImplementedException();
            }
            return new PosixUncachedFileStream(
                path,
                fileMode,
                fileAccess,
                fileShare,
                bufferSize,
                fileOptions);
        }

        internal class PosixUncachedFileStream : FileStream {
            public PosixUncachedFileStream(
                string path,
                FileMode mode,
                FileAccess access,
                FileShare share,
                int bufferSize,
                FileOptions options
                ) : base(
                    path,
                    mode,
                    access,
                    share,
                    bufferSize,
                    options)
            {
                Syscall.fcntl(
                  (int)SafeFileHandle.DangerousGetHandle(),
                  FcntlCommand.F_NOCACHE);
            }
        }
    }
}

