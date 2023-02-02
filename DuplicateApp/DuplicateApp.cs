using System;
using System.Threading;
using System.Collections.Generic;
using System.Diagnostics;
using Imagibee.Gigantor;

//
// A command line app that detects duplicates between files
//
// The main purpose of this app is to assist with performance benchmarking.
//
// Usage - benchmarking
//   dotnet DuplicateApp/bin/Release/netcoreapp3.1/DuplicateApp.dll benchmark "/tmp/enwik9.txt;/tmp/enwik9-1.txt"
//
class DuplicateApp {
    static string Error = "";

    enum SessionType {
        Normal,
        Benchmark
    }

    struct SessionData {
        public List<string> paths1;
        public List<string> paths2;
        public int chunkKiBytes;
        public int maxWorkers;
        public int iterations;
        public long byteCount;
    }

    struct ResultData {
        public double elapsedTime;
        public bool identical;
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
            paths1 = new(),
            paths2 = new(),
            chunkKiBytes = 512,
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
            var paths = args[i].Split(';');
            sessionData.paths1.Add(paths[0]);
            sessionData.paths2.Add(paths[1]);
        }
        sessionData.byteCount = Utilities.FileByteCount(sessionData.paths1);
        return new Tuple<SessionType, SessionData>(sessionType, sessionData);
    }

    static IList<SessionData> CreateSessionData(SessionType sessionType, SessionData sessionData)
    {
        if (sessionType == SessionType.Benchmark) {
            return CreateBenchmarkSession(sessionData);
        }
        else {
            return CreateNormalSession(sessionData);
        }
    }

    static IList<SessionData> CreateBenchmarkSession(SessionData sessionInfo)
    {
        List<SessionData> sessionDatas = new();
        foreach (var maxWorkers in new List<int>() { 1, 2, 4, 8, 16, 32, 64, 128 }) {
            SessionData sessionData = new()
            {
                paths1 = sessionInfo.paths1,
                paths2 = sessionInfo.paths2,
                chunkKiBytes = sessionInfo.chunkKiBytes,
                maxWorkers = maxWorkers,
                iterations = sessionInfo.iterations,
                byteCount = sessionInfo.byteCount,
            };
            sessionDatas.Add(sessionData);
        }
        return sessionDatas;
    }

    static IList<SessionData> CreateNormalSession(SessionData sessionInfo)
    {
        List<SessionData> sessionDatas = new();
        SessionData sessionData = new()
        {
            paths1 = sessionInfo.paths1,
            paths2 = sessionInfo.paths2,
            chunkKiBytes = sessionInfo.chunkKiBytes,
            maxWorkers = sessionInfo.maxWorkers,
            iterations = 1,
            byteCount = sessionInfo.byteCount,
        };
        sessionDatas.Add(sessionData);
        return sessionDatas;
    }

    static void DoSessions(IList<SessionData> sessionDatas)
    {
        AutoResetEvent progress = new(false);
        foreach (var sessionData in sessionDatas) {
            var identical = true;
            Stopwatch stopwatch = new();
            for (var i = 0; i < sessionData.iterations; i++) {
                var checkers = StartChecking(progress, sessionData);
                WaitForCompletion(progress, checkers, stopwatch);
                if (Error.Length != 0) {
                    Console.Write('\n');
                    throw new Exception(Error);
                }
                foreach (var checker in checkers) {
                    if (!checker.Identical) {
                        identical = false;
                    }
                }
            }
            Console.Write('\n');
            ResultData resultData = new ResultData()
            {
                identical = identical,
                elapsedTime = stopwatch.Elapsed.TotalSeconds,
            };
            DisplayResults(resultData, sessionData);
        }
    }

    static IList<DuplicateChecker> StartChecking(AutoResetEvent progress, SessionData sessionData)
    {
        List<DuplicateChecker> checkers = new();
        for (var i=0; i<sessionData.paths1.Count; i++) {
            var checker = new DuplicateChecker(
                sessionData.paths1[i],
                sessionData.paths2[i],
                progress,
                sessionData.chunkKiBytes,
                sessionData.maxWorkers);
            checker.Start();
            checkers.Add(checker);
        }
        return checkers;
    }

    static void WaitForCompletion(AutoResetEvent progress, IList<DuplicateChecker> checkers, Stopwatch stopwatch)
    {
        double lastTime = 0;
        stopwatch.Start();
        Utilities.Wait(
            new List<IBackground>(checkers),
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
        foreach (var indexer in checkers) {
            if (Error.Length == 0 && indexer.Error.Length != 0) {
                Error = indexer.Error;
            }
        }
    }

    static void DisplayResults(ResultData resultData, SessionData sessionData)
    {
        long totalBytes = sessionData.iterations * sessionData.byteCount;
        var result = resultData.identical ? "identical" : "different";
        ThreadPool.GetMaxThreads(out int maxThreads, out int _);
        Console.WriteLine($"maxWorkers={sessionData.maxWorkers}, chunkKiBytes={sessionData.chunkKiBytes}, maxThread={maxThreads}");
        Console.WriteLine(value: $"   files are {result}");
        Console.WriteLine(value: $"   checked {totalBytes} bytes in {resultData.elapsedTime} seconds");
        Console.WriteLine(value: $"-> {totalBytes / resultData.elapsedTime / 1e6} MBytes/s");
    }
}
