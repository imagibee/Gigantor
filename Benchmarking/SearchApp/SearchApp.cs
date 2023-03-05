using System;
using System.Threading;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Diagnostics;
using Imagibee.Gigantor;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Mono.Unix.Native;

//
// A command line app that performs regex searches over 1 or more file
//
// The main purpose of this app is to assist with performance benchmarking.
//
// Usage - benchmarking
//   dotnet SearchApp/bin/Release/net6.0/SearchApp.dll benchmark ${TMPDIR}/enwik9.txt
//
class SearchApp {
    static string Error = "";

    enum SessionType {
        Normal,
        Benchmark
    }

    struct SessionData {
        public List<string> paths;
        public int chunkKiBytes;
        public int maxWorkers;
        public int iterations;
        public string pattern;
        public bool useStream;
        public bool useUnbuffered;
        public System.IO.Stream stream;
    }

    struct ResultData {
        public long matchCount;
        public long byteCount;
        public double elapsedTime;
    }

    static void Main(string[] args)
    {
        var session = ParseArgs(args);
        DoSessions(CreateSessionData(session.Item1, session.Item2));
    }

    static Tuple<SessionType, SessionData> ParseArgs(string[] args)
    {
        var startPathIndex = 1;
        var sessionType = SessionType.Normal;
        SessionData sessionData = new()
        {
            paths = new(),
            chunkKiBytes = 512,
            maxWorkers = 0
        };
        if (args[0].Contains("benchmark")) {
            sessionType = SessionType.Benchmark;
            sessionData.pattern = @"/https?:\/\/(www\.)?[-a-zA-Z0-9@:%._\+~#=]{2,256}\.[a-z]{2,6}\b([-a-zA-Z0-9@:%_\+.~#()?&//=]*)/";
            sessionData.iterations = 1;
            if (args[0].Contains("stream")) {
                sessionData.useStream = true;
            }
            else if (args[0].Contains("unbuffered")) {
                sessionData.useUnbuffered = true;
            }
            if (args[0].Contains("legacy")) {
                sessionData.pattern = "food";
            }
            else if (args[0].Contains("sparse")) {
                sessionData.pattern = "unicorn";
            }
            else if (args[0].Contains("zero")) {
                sessionData.pattern = "kerfuf";
            }
            if (args[0].Contains("64")) {
                sessionData.maxWorkers = 64;
            }
        }
        else {
            if (int.TryParse(args[0], out int workers)) {
                sessionData.maxWorkers = workers;
                sessionData.pattern = args[1];
                startPathIndex = 2;
            }
            else {
                sessionData.pattern = args[0];
            }
        }
        for (var i = startPathIndex; i < args.Length; i++) {
            sessionData.paths.Add(args[i]);
            if (args[i].Contains(".gz")) {
                sessionData.chunkKiBytes = 512;
            }
        }
        return new Tuple<SessionType, SessionData>(sessionType, sessionData);
    }

    static ICollection<SessionData> CreateSessionData(SessionType sessionType, SessionData sessionData)
    {
        if (sessionType == SessionType.Benchmark) {
            return CreateBenchmarkSession(sessionData);
        }
        else {
            return CreateNormalSession(sessionData);
        }
    }

    static ICollection<SessionData> CreateBenchmarkSession(SessionData sessionInfo)
    {
        List<SessionData> sessionDatas = new();
        List<int> maxWorkerPermutations;
        if (sessionInfo.maxWorkers == 0) {
            maxWorkerPermutations = new List<int>() { 1, 2, 4, 8, 16, 32, 64, 128 };
        }
        else {
            maxWorkerPermutations = new List<int>() { sessionInfo.maxWorkers };
        }
        if (sessionInfo.paths.Count > 1) {
            maxWorkerPermutations = new List<int>() { 16 };
        }
        foreach (var maxWorkers in maxWorkerPermutations) {
            SessionData sessionData = new()
            {
                paths = sessionInfo.paths,
                chunkKiBytes = sessionInfo.chunkKiBytes,
                maxWorkers = maxWorkers,
                iterations = sessionInfo.iterations,
                pattern = sessionInfo.pattern,
                useStream = sessionInfo.useStream,
                useUnbuffered = sessionInfo.useUnbuffered,
            };
            sessionDatas.Add(sessionData);
        }
        return sessionDatas;
    }

    static ICollection<SessionData> CreateNormalSession(SessionData sessionInfo)
    {
        List<SessionData> sessionDatas = new();
        SessionData sessionData = new()
        {
            paths = sessionInfo.paths,
            chunkKiBytes = sessionInfo.chunkKiBytes,
            maxWorkers = sessionInfo.maxWorkers,
            iterations = 1,
            pattern = sessionInfo.pattern,
        };
        sessionDatas.Add(sessionData);
        return sessionDatas;
    }

    static void DoSessions(ICollection<SessionData> sessionDatas)
    {
        AutoResetEvent progress = new(false);
        foreach (var sessionData in sessionDatas) {
            long matchCount = 0;
            long byteCount = 0;
            Stopwatch stopwatch = new();
            for (var i = 0; i < sessionData.iterations; i++) {
                var searchers = StartSearching(progress, sessionData);
                WaitForCompletion(progress, searchers, stopwatch);
                if (Error.Length != 0) {
                    Console.Write('\n');
                    throw new Exception(Error);
                }
                foreach (var searcher in searchers) {
                    matchCount += searcher.MatchCount;
                    byteCount += searcher.ByteCount;
                }
            }
            Console.Write('\n');
            ResultData resultData = new ResultData()
            {
                matchCount = matchCount,
                byteCount = byteCount,
                elapsedTime = stopwatch.Elapsed.TotalSeconds
            };
            DisplayResults(resultData, sessionData);
        }
    }

    static ICollection<RegexSearcher> StartSearching(AutoResetEvent progress, SessionData sessionData)
    {
        List<RegexSearcher> searchers = new();
        foreach (var path in sessionData.paths) {
            RegexSearcher searcher;
            if (sessionData.useStream || sessionData.useUnbuffered) {
                FileStream fs;
                if (sessionData.useStream) {
                    fs = new FileStream(path, FileMode.Open);
                }
                else {
                    fs = Utilities.UnbufferedFileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        sessionData.chunkKiBytes,
                        FileOptions.Asynchronous);
                }
                if (path.Contains(".gz")) {
                    sessionData.maxWorkers = 2;
                    sessionData.stream = new GZipStream(
                    fs, CompressionMode.Decompress, true);
                }
                else {
                    sessionData.stream = fs;
                }
                searcher = new RegexSearcher(
                    sessionData.stream,
                    new Regex(sessionData.pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled),
                    progress,
                    maxMatchCount: 50000,
                    chunkKiBytes: sessionData.chunkKiBytes,
                    maxWorkers: sessionData.maxWorkers,
                    overlap: sessionData.pattern.Length);
            }
            else {
                searcher = new RegexSearcher(
                    path,
                    new Regex(sessionData.pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled),
                    progress,
                    maxMatchCount: 50000,
                    chunkKiBytes: sessionData.chunkKiBytes,
                    maxWorkers: sessionData.maxWorkers,
                    overlap: sessionData.pattern.Length);
            }
            searcher.Start();
            searchers.Add(searcher);
        }
        return searchers;
    }

    static void WaitForCompletion(AutoResetEvent progress, ICollection<RegexSearcher> searchers, Stopwatch stopwatch)
    {
        double lastTime = 0;
        stopwatch.Start();
        Background.Wait(
            new List<IBackground>(searchers),
            progress,
            (_) =>
            {
                if (stopwatch.Elapsed.TotalSeconds - lastTime > 1) {
                    lastTime = stopwatch.Elapsed.TotalSeconds;
                    Console.Write('.');
                }
            },
            1000);
        stopwatch.Stop();
        foreach (var searcher in searchers) {
            if (Error.Length == 0 && searcher.Error.Length != 0) {
                Error = searcher.Error;
            }
        }
    }

    static void DisplayResults(ResultData resultData, SessionData sessionData)
    {
        long totalBytes = resultData.byteCount;
        ThreadPool.GetMaxThreads(out int maxThreads, out int _);
        Console.WriteLine($"files={sessionData.paths.Count}, maxWorkers={sessionData.maxWorkers}, chunkKiBytes={sessionData.chunkKiBytes}");
        Console.WriteLine($"   {resultData.matchCount} matches found");
        Console.WriteLine(value: $"   searched {totalBytes} bytes in {resultData.elapsedTime} seconds");
        Console.WriteLine(value: $"-> {totalBytes / resultData.elapsedTime / 1e6} MBytes/s");
    }
}
