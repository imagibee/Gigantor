﻿using System;
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
class SearchApp {
    static string Error = "";

    enum SessionType {
        Normal,
        Benchmark
    }

    struct SessionData {
        public List<string> paths;
        public int chunkSize;
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
            chunkSize = 512 * 1024,
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

    static IEnumerable<SessionData> CreateSessionData(SessionType sessionType, SessionData sessionData)
    {
        if (sessionType == SessionType.Benchmark) {
            return CreateBenchmarkSession(sessionData);
        }
        else {
            return CreateNormalSession(sessionData);
        }
    }

    static IEnumerable<SessionData> CreateBenchmarkSession(SessionData sessionInfo)
    {
        List<SessionData> sessionDatas = new();
        foreach (var maxWorkers in new List<int>() { 1, 2, 16, 0 }) {
            SessionData sessionData = new()
            {
                paths = sessionInfo.paths,
                chunkSize = sessionInfo.chunkSize,
                maxWorkers = maxWorkers,
                iterations = sessionInfo.iterations,
                byteCount = sessionInfo.byteCount,
                pattern = sessionInfo.pattern,
            };
            sessionDatas.Add(sessionData);
        }
        return sessionDatas;
    }

    static IEnumerable<SessionData> CreateNormalSession(SessionData sessionInfo)
    {
        List<SessionData> sessionDatas = new();
        SessionData sessionData = new()
        {
            paths = sessionInfo.paths,
            chunkSize = sessionInfo.chunkSize,
            maxWorkers = sessionInfo.maxWorkers,
            iterations = 1,
            byteCount = sessionInfo.byteCount,
            pattern = sessionInfo.pattern,
        };
        sessionDatas.Add(sessionData);
        return sessionDatas;
    }

    static void DoSessions(IEnumerable<SessionData> sessionDatas)
    {
        AutoResetEvent progress = new(false);
        foreach (var sessionData in sessionDatas) {
            var matchCount = 0;
            Stopwatch stopwatch = new();
            for (var i = 0; i < sessionData.iterations; i++) {
                var searchers = StartSearching(progress, sessionData);
                WaitForCompletion(progress, searchers, stopwatch);
                if (Error.Length != 0) {
                    throw new Exception(Error);
                }
                foreach (var searcher in searchers) {
                    matchCount += searcher.MatchCount;
                }
            }
            ResultData resultData = new ResultData() { matchCount = matchCount, elapsedTime = stopwatch.Elapsed.TotalSeconds };
            DisplayResults(resultData, sessionData);
        }
    }

    static IEnumerable<RegexSearcher> StartSearching(AutoResetEvent progress, SessionData sessionData)
    {
        List<RegexSearcher> searchers = new();
        foreach (var path in sessionData.paths) {
            var searcher = new RegexSearcher(progress, sessionData.chunkSize, sessionData.maxWorkers);
            searcher.Start(
                path,
                new Regex(sessionData.pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled),
                sessionData.pattern.Length);
            searchers.Add(searcher);
        }
        return searchers;
    }

    static void WaitForCompletion(AutoResetEvent progress, IEnumerable<RegexSearcher> searchers, Stopwatch stopwatch)
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
        Console.Write('\n');
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
        Console.WriteLine($"maxWorkers={sessionData.maxWorkers}, chunkSize={sessionData.chunkSize}, maxThread={maxThreads}");
        Console.WriteLine($"   {resultData.matchCount} matches found");
        Console.WriteLine(value: $"   searched {totalBytes} bytes in {resultData.elapsedTime} seconds");
        Console.WriteLine(value: $"-> {totalBytes / resultData.elapsedTime / 1e6} MBytes/s");
    }

}