using System;
using System.Linq;
using System.Threading;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Net;
using System.Diagnostics;
using NUnit.Framework;
using Imagibee.Gigantor;

#pragma warning disable CS8618

namespace Testing {
    public class ExampleTests {
        string enwik9Path;
        string biblePath;

        [SetUp]
        public void Setup()
        {
            enwik9Path = Utilities.GetEnwik9();
            biblePath = Utilities.GetGutenbergBible();
        }

        [Test]
        public void MixedExampleTest()
        {
            // The regular expressions for the search
            List<Regex> regexs = new() {
                new(@"comfort\s*food", RegexOptions.IgnoreCase | RegexOptions.Compiled),
                new(@"strong\s*coffee", RegexOptions.IgnoreCase | RegexOptions.Compiled)
            };

            // A shared wait event to facilitate progress notifications
            AutoResetEvent progress = new(false);

            // Create the search and indexing workers
            LineIndexer indexer = new(enwik9Path, progress);
            RegexSearcher searcher = new(enwik9Path, regexs, progress);

            // Create a IBackground collection for convenient managment
            var processes = new List<IBackground>()
            {
                indexer,
                searcher
            };

            // Start search and indexing in parallel and wait for completion
            Console.WriteLine($"Working ...");
            Background.StartAndWait(
                processes,
                progress,
                (_) => { Console.Write('.'); },
                1000);
            Console.Write('\n');

            // All done, check for errors
            var error = Background.AnyError(processes);
            if (error.Length != 0) {
                throw new Exception(error);
            }

            // Check for cancellation
            if (Background.AnyCancelled(processes)) {
                throw new Exception("search cancelled");
            }

            // Display results
            for (var j = 0; j < regexs.Count; j++) {
                Console.WriteLine($"Found {searcher.GetMatchData(j).Count} matches for regex {j} ...");
                if (searcher.GetMatchData(j).Count != 0) {
                    var matchDatas = searcher.GetMatchData(j);
                    for (var i = 0; i < matchDatas.Count; i++) {
                        var matchData = matchDatas[i];
                        Console.WriteLine(
                            $"[{i}]({matchData.Value}) ({matchData.Name}) " +
                            $"line {indexer.LineFromPosition(matchData.StartFpos)} " +
                            $"fpos {matchData.StartFpos}");
                    }

                    // Get the line of the 1st match
                    var matchLine = indexer.LineFromPosition(
                        searcher.GetMatchData(j)[0].StartFpos);

                    // Open the searched file for reading
                    using System.IO.FileStream fileStream = new(enwik9Path, FileMode.Open);
                    Imagibee.Gigantor.StreamReader gigantorReader = new(fileStream);

                    // Seek to the first line we want to read
                    var contextLines = 3;
                    fileStream.Seek(indexer.PositionFromLine(
                        matchLine - contextLines), SeekOrigin.Begin);

                    // Read and display a few lines around the match
                    for (var line = matchLine - contextLines;
                        line <= matchLine + contextLines;
                        line++) {
                        Console.WriteLine(
                            $"[{line}]({indexer.PositionFromLine(line)})  " +
                            gigantorReader.ReadLine());
                    }
                }
            }
        }

        [Test]
        public void MixedCancelTest()
        {
            const string pattern = @"love\s*thy\s*neighbour";
            Regex regex = new(
                pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            AutoResetEvent progress = new(false);
            LineIndexer indexer = new(biblePath, progress);
            RegexSearcher searcher = new(
                biblePath, regex, progress);
            Console.WriteLine($"Searching ...");
            var processes = new List<IBackground>()
            {
                indexer,
                searcher
            };
            foreach (var process in processes) {
                process.Start();
            }
            Background.CancelAll(processes);
            Background.Wait(
                processes,
                progress,
                (_) => {},
                1000);
            Assert.AreEqual("", Background.AnyError(processes));
            Assert.AreEqual(true, Background.AnyCancelled(processes));
        }

        //// https://stackoverflow.com/questions/60707118/fast-search-in-a-large-text-file
        //public List<string> Search(string path, string searchKey)
        //{
        //    // Create regex to search for the searchKey
        //    System.Text.RegularExpressions.Regex regex = new(searchKey);
        //    List<string> results = new List<string>();

        //    // Create Gigantor stuff
        //    System.Threading.AutoResetEvent progress = new(false);
        //    Imagibee.Gigantor.RegexSearcher searcher = new(path, regex, progress, maxMatchCount: 10000);

        //    // Start the search and wait for completion
        //    Imagibee.Gigantor.Background.StartAndWait(
        //        searcher,
        //        progress,
        //        (_) => { },
        //        1000);

        //    // Check for errors
        //    if (searcher.Error.Length != 0) {
        //        throw new Exception(searcher.Error);
        //    }

        //    // Open the searched file for reading
        //    using System.IO.FileStream fileStream = new(path, FileMode.Open);
        //    Imagibee.Gigantor.StreamReader reader = new(fileStream);

        //    // Capture the line of each match
        //    foreach (var match in searcher.GetMatchData()) {
        //        fileStream.Seek(match.StartFpos, SeekOrigin.Begin);
        //        results.Add(reader.ReadLine());
        //    }
        //    return results;
        //}

        //[Test]
        //public void SearchTest()
        //{
        //    var path = Path.Combine(Path.GetTempPath(), "enwik9x32");
        //    Stopwatch stopwatch = new();
        //    stopwatch.Start();
        //    var results = Search(path, "unicorn");
        //    stopwatch.Stop();
        //    Console.WriteLine($"found {results.Count} results in {stopwatch.Elapsed.TotalSeconds} seconds");
        //}
    }
}
