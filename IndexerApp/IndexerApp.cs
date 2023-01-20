using System;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Diagnostics;
using Imagibee.TextFile;

//
// A command line app that computes the line count of 1 or more file
//
// The main purpose of this app is to assist with performance benchmarking.
//
class IndexerApp {
    static void Main(string[] paths)
    {
        var indexers = StartIndexing(paths);
        var stopwatch = WaitForCompletion(indexers);
        DisplayResults(paths, indexers, stopwatch);
    }

    static List<Indexer> StartIndexing(string[] paths)
    {
        List<Indexer> indexers = new();
        foreach (var arg in paths) {
            var indexer = new Indexer();
            indexer.Start(arg);
            indexers.Add(indexer);
        }
        return indexers;
    }

    static Stopwatch WaitForCompletion(List<Indexer> indexers)
    {
        Stopwatch stopwatch = new();
        stopwatch.Start();
        while (true) {
            var runningCount = 0;
            foreach (var indexer in indexers) {
                if (indexer.Running) {
                    runningCount++;
                }
            }
            if (runningCount == 0) {
                break;
            }
            Thread.Sleep(0);
        }
        stopwatch.Stop();
        return stopwatch;
    }

    static void DisplayResults(string[] paths, List<Indexer> indexers, Stopwatch stopwatch)
    {
        long totalBytes = 0;
        for (var i=0; i<paths.Length; i++) {
            if (indexers[i].LastError.Length != 0) {
                Console.WriteLine($"[{i}] ERROR:\n{indexers[i].LastError}");
            }
            else {
                FileInfo fileInfo = new(paths[i]);
                totalBytes += fileInfo.Length;
                Console.WriteLine($"[{i}] {indexers[i].LineCount} lines in {RemoveUsername(paths[i])}");
            }
        }
        Console.WriteLine(value: $"Indexed {totalBytes} bytes in {stopwatch.Elapsed.TotalSeconds} seconds");
        Console.WriteLine(value: $"Indexing rate {totalBytes/stopwatch.Elapsed.TotalSeconds/1e6} MBytes/s");
    }

    static string RemoveUsername(string path)
    {
        // mac format
        return Regex.Replace(path, @"/Users/([^/]*/)", "~/");
        // TODO: add more formats
    }
}
