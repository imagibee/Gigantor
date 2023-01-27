using System;
using System.Threading;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Diagnostics;
using Imagibee.Gigantor;

//
// A command line app that performs regex searches over 1 or more file
//
// The main purpose of this app is to assist with performance benchmarking.
//
// Usage - benchmarking
//   dotnet SearchApp/bin/Release/netcoreapp3.1/SearchApp.dll benchmark /tmp/enwik9.txt /tmp/enwik9-1.txt
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
        public long byteCount;
        public string pattern;
    }

    struct ResultData {
        public long matchCount;
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
        if (args[0] == "benchmark") {
            sessionType = SessionType.Benchmark;
            sessionData.pattern = "food";
            sessionData.iterations = 5;
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
        }
        sessionData.byteCount = Utilities.ByteCount(sessionData.paths);
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
        foreach (var maxWorkers in new List<int>() { 1, 2, 16, 0 }) {
            SessionData sessionData = new()
            {
                paths = sessionInfo.paths,
                chunkKiBytes = sessionInfo.chunkKiBytes,
                maxWorkers = maxWorkers,
                iterations = sessionInfo.iterations,
                byteCount = sessionInfo.byteCount,
                pattern = sessionInfo.pattern,
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
            byteCount = sessionInfo.byteCount,
            pattern = sessionInfo.pattern,
        };
        sessionDatas.Add(sessionData);
        return sessionDatas;
    }

    static void DoSessions(ICollection<SessionData> sessionDatas)
    {
        AutoResetEvent progress = new(false);
        foreach (var sessionData in sessionDatas) {
            var matchCount = 0;
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
                }
            }
            Console.Write('\n');
            ResultData resultData = new ResultData() { matchCount = matchCount, elapsedTime = stopwatch.Elapsed.TotalSeconds };
            DisplayResults(resultData, sessionData);
        }
    }

    static ICollection<RegexSearcher> StartSearching(AutoResetEvent progress, SessionData sessionData)
    {
        List<RegexSearcher> searchers = new();
        foreach (var path in sessionData.paths) {
            var searcher = new RegexSearcher(progress, sessionData.chunkKiBytes, sessionData.maxWorkers);
            searcher.Start(
                path,
                new Regex(sessionData.pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled),
                sessionData.pattern.Length);
            searchers.Add(searcher);
        }
        return searchers;
    }

    static void WaitForCompletion(AutoResetEvent progress, ICollection<RegexSearcher> searchers, Stopwatch stopwatch)
    {
        double lastTime = 0;
        stopwatch.Start();
        RegexSearcher.Wait(
            searchers,
            progress,
            (runningCount) =>
            {
                if (stopwatch.Elapsed.TotalSeconds - lastTime > 1) {
                    lastTime = stopwatch.Elapsed.TotalSeconds;
                    Console.Write('.');
                }
            },
            1000);
        stopwatch.Stop();
        foreach (var searcher in searchers) {
            if (Error.Length == 0 && searcher.LastError.Length != 0) {
                Error = searcher.LastError;
            }
        }
    }

    static void DisplayResults(ResultData resultData, SessionData sessionData)
    {
        long totalBytes = sessionData.iterations * sessionData.byteCount;
        ThreadPool.GetMaxThreads(out int maxThreads, out int _);
        Console.WriteLine($"maxWorkers={sessionData.maxWorkers}, chunkKiBytes={sessionData.chunkKiBytes}, maxThread={maxThreads}");
        Console.WriteLine($"   {resultData.matchCount} matches found");
        Console.WriteLine(value: $"   searched {totalBytes} bytes in {resultData.elapsedTime} seconds");
        Console.WriteLine(value: $"-> {totalBytes / resultData.elapsedTime / 1e6} MBytes/s");
    }

}
