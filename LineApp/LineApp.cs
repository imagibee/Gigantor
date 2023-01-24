using System;
using System.Threading;
using System.Collections.Generic;
using System.Diagnostics;
using Imagibee.Gigantor;

//
// A command line app that computes the line count of 1 or more file
//
// The main purpose of this app is to assist with performance benchmarking.
//
class LineApp {
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
    }

    struct ResultData {
        public long lineCount;
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
            sessionData.iterations = 5;
        }
        else {
            if (int.TryParse(args[0], out int workers)) {
                sessionData.maxWorkers = workers;
            }
            else {
                startPathIndex = 0;
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
        };
        sessionDatas.Add(sessionData);
        return sessionDatas;
    }

    static void DoSessions(IEnumerable<SessionData> sessionDatas)
    {
        AutoResetEvent progress = new(false);
        foreach (var sessionData in sessionDatas) {
            var lineCount = 0;
            Stopwatch stopwatch = new();
            for (var i = 0; i < sessionData.iterations; i++) {
                var indexers = StartIndexing(progress, sessionData);
                WaitForCompletion(progress, indexers, stopwatch);
                if (Error.Length != 0) {
                    throw new Exception(Error);
                }
                foreach (var indexer in indexers) {
                    lineCount += indexer.LineCount;
                }
            }
            ResultData resultData = new ResultData() { lineCount = lineCount, elapsedTime = stopwatch.Elapsed.TotalSeconds };
            DisplayResults(resultData, sessionData);
        }
    }

    static IEnumerable<LineIndexer> StartIndexing(AutoResetEvent progress, SessionData sessionData)
    {
        List<LineIndexer> indexers = new();
        foreach (var path in sessionData.paths) {
            var indexer = new LineIndexer(progress, sessionData.chunkSize, sessionData.maxWorkers);
            indexer.Start(path);
            indexers.Add(indexer);
        }
        return indexers;
    }

    static void WaitForCompletion(AutoResetEvent progress, IEnumerable<LineIndexer> indexers, Stopwatch stopwatch)
    {
        double lastTime = 0;
        stopwatch.Start();
        LineIndexer.Wait(
            indexers,
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
        foreach (var indexer in indexers) {
            if (Error.Length == 0 && indexer.LastError.Length != 0) {
                Error = indexer.LastError;
            }
        }
    }

    static void DisplayResults(ResultData resultData, SessionData sessionData)
    {
        long totalBytes = sessionData.iterations * sessionData.byteCount;
        ThreadPool.GetMaxThreads(out int maxThreads, out int _);
        Console.WriteLine($"maxWorkers={sessionData.maxWorkers}, chunkSize={sessionData.chunkSize}, maxThread={maxThreads}");
        Console.WriteLine(       $"   {resultData.lineCount} lines indexed");
        Console.WriteLine(value: $"   indexed {totalBytes} bytes in {resultData.elapsedTime} seconds");
        Console.WriteLine(value: $"-> {totalBytes/resultData.elapsedTime/1e6} MBytes/s");
    }
}
