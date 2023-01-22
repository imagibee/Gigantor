using System;
using System.IO;
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
class SearcherApp {
    static void Main(string[] paths)
    {
        AutoResetEvent progress = new(false);
        var searchers = StartSearching(progress, paths);
        var stopwatch = WaitForCompletion(progress, searchers);
        DisplayResults(paths, searchers, stopwatch);
    }

    static List<RegexSearcher> StartSearching(AutoResetEvent progress, string[] args)
    {
        List<RegexSearcher> searchers = new();
        for (var i=1; i<args.Length; i++) {
            var searcher = new RegexSearcher(progress);
            searcher.Start(
                args[i],
                new Regex(args[0], RegexOptions.IgnoreCase | RegexOptions.Compiled),
                args[0].Length);
            searchers.Add(searcher);
        }
        return searchers;
    }

    static Stopwatch WaitForCompletion(AutoResetEvent progress, List<RegexSearcher> searchers)
    {
        Stopwatch stopwatch = new();
        stopwatch.Start();
        double lastTime = 0;
        while (true) {
            var runningCount = 0;
            var error = false;
            foreach (var searcher in searchers) {
                if (searcher.Running) {
                    runningCount++;
                }
                if (searcher.LastError != "") {
                    error = true;
                }
            }
            if (runningCount == 0) {
                break;
            }
            progress.WaitOne(1000);
            if (stopwatch.Elapsed.TotalSeconds - lastTime > 1) {
                lastTime = stopwatch.Elapsed.TotalSeconds;
                if (error) {
                    Console.Write('E');
                }
                else {
                    Console.Write('.');
                }
            }
        }
        stopwatch.Stop();
        Console.Write('\n');
        return stopwatch;
    }

    static void DisplayResults(string[] paths, List<RegexSearcher> searchers, Stopwatch stopwatch)
    {
        long totalBytes = 0;
        for (var i = 0; i < searchers.Count; i++) {
            if (searchers[i].LastError.Length != 0) {
                Console.WriteLine($"[{i}] ERROR:\n{searchers[i].LastError}");
            }
            else {
                FileInfo fileInfo = new(paths[i+1]);
                totalBytes += fileInfo.Length;
                Console.WriteLine($"[{i}] {searchers[i].MatchCount} matches in {RemoveUsername(paths[i+1])}");
            }
        }
        Console.WriteLine(value: $"Searched {totalBytes} bytes in {stopwatch.Elapsed.TotalSeconds} seconds");
        Console.WriteLine(value: $"Searching rate {totalBytes / stopwatch.Elapsed.TotalSeconds / 1e6} MBytes/s");
    }

    static string RemoveUsername(string path)
    {
        // mac format
        return Regex.Replace(path, @"/Users/([^/]*/)", "~/");
        // TODO: add more formats
    }
}
