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
using Microsoft.VisualStudio.TestPlatform.Utilities;

namespace Testing {
    public class ExampleTests {
        string enwik9Path = "";
        string biblePath = "";
        string teaPath = "";

        [SetUp]
        public void Setup()
        {
            enwik9Path = Utilities.GetEnwik9();
            biblePath = Utilities.GetGutenbergBible();
            teaPath = Path.Combine(Utilities.GetBenchmarkPath(), "enwik9tea");
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
            Stopwatch stopwatch = new();
            stopwatch.Start();
            Background.StartAndWait(
                processes,
                progress,
                (_) => {
                    if (stopwatch.Elapsed.TotalSeconds > 1) {
                        Console.Write('.');
                        stopwatch.Reset();
                    }
                },
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
            Console.WriteLine($"Found {searcher.GetMatchData().Count} matches for regex ...");
            if (searcher.GetMatchData().Count != 0) {
                var matchDatas = searcher.GetMatchData();
                for (var i = 0; i < matchDatas.Count; i++) {
                    var matchData = matchDatas[i];
                    Console.WriteLine(
                        $"[{i}]({matchData.Value}) ({matchData.Name}) " +
                        $"line {indexer.LineFromPosition(matchData.StartFpos)} " +
                        $"fpos {matchData.StartFpos}");
                }

                // Get the line of the 1st match
                var matchLine = indexer.LineFromPosition(
                    searcher.GetMatchData()[0].StartFpos);

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

            // Replace matches that contain "coffee" with "tea"
            searcher.Replace(
                File.Create(teaPath),
                (match) => {
                    if (match.Value.Contains("coffee")) {
                        return "tea";
                    }
                    return match.Value;
                });
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

        // Don't run, just compile
        public void IndexerExample()
        {
            AutoResetEvent progress = new(false);

            // Create the indexer
            LineIndexer indexer = new("myfile", progress);

            // Do the indexing
            Imagibee.Gigantor.Background.StartAndWait(indexer, progress, (_) => {});

            // Use indexer to print the middle line
            using System.IO.FileStream fs = new("myfile", FileMode.Open);
            Imagibee.Gigantor.StreamReader reader = new(fs);
            fs.Seek(indexer.PositionFromLine(indexer.LineCount / 2), SeekOrigin.Begin);
            Console.WriteLine(reader.ReadLine());
        }

        // Don't run, just compile
        public void SearchReplaceExample()
        {
            AutoResetEvent progress = new(false);

            // Create a regular expression to match urls
            System.Text.RegularExpressions.Regex regex = new(
                @"/https?:\/\/(www\.)?[-a-zA-Z0-9@:%._\+~#=]{2,256}\.[a-z]{2,6}\b([-a-zA-Z0-9@:%_\+.~#()?&//=]*)/",
                RegexOptions.Compiled);

            // Create the searcher
            Imagibee.Gigantor.RegexSearcher searcher = new("myfile", regex, progress);

            // Do the search
            Imagibee.Gigantor.Background.StartAndWait(searcher, progress, (_) => { });

            foreach (var match in searcher.GetMatchData()) {
                // Do something with the matches
            }

            // Replace all the urls with stackoverflow.com in a new file
            using System.IO.FileStream output = File.Create("myfile2");
            searcher.Replace(output, (match) => { return "https://www.stackoverflow.com"; }); 
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
