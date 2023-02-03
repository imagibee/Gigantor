using System;
using System.Linq;
using System.Threading;
using System.IO;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using NUnit.Framework;
using Imagibee.Gigantor;

namespace Testing {
    public class ExampleTests {

        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void MixedExampleTest()
        {
            // The path to be searched and indexed
            var path = Path.Combine("Assets", "BibleTest.txt");

            // The regular expression for the search
            const string pattern = @"my\s*yoke\s*is\s*easy";
            Regex regex = new(
                pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

            // A shared wait event to facilitate progress notifications
            AutoResetEvent progress = new(false);

            // Create the search and indexing workers
            LineIndexer indexer = new(path, progress);
            RegexSearcher searcher = new(path, regex, progress);

            // Create a IBackground collection for convenient monitoring
            var processes = new List<IBackground>()
            {
                indexer,
                searcher
            };

            // Create a progress bar to illustrate progress updates
            Utilities.ByteProgress progressBar = new(
                40, processes.Count * Utilities.FileByteCount(path));

            // Start search and indexing in parallel and wait for completion
            Console.WriteLine($"Searching ...");
            Background.StartAndWait(
                processes,
                progress,
                (_) =>
                {
                    progressBar.Update(
                        processes.Select((p) => p.ByteCount).Sum());
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

            // Display search results
            Console.WriteLine($"Found {searcher.MatchCount} matches ...");
            var matchDatas = searcher.GetMatchData();
            for (var i=0; i<matchDatas.Count; i++) {
                var matchData = matchDatas[i];
                Console.WriteLine(
                    $"[{i}]({matchData.Value}) ({matchData.Name}) " +
                    $"line {indexer.LineFromPosition(matchData.StartFpos)} " +
                    $"fpos {matchData.StartFpos}");
            }

            // Get the line of the 1s5 match
            var matchLine = indexer.LineFromPosition(
                searcher.GetMatchData()[0].StartFpos);

            // Open the searched file for reading
            using FileStream fileStream = new(path, FileMode.Open);
            Imagibee.Gigantor.StreamReader gigantorReader = new(fileStream);

            // Seek to the first line we want to read
            var contextLines = 6;
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
            //Assert.AreEqual(true, false);
        }

        [Test]
        public void MixedCancelTest()
        {
            var path = Path.Combine("Assets", "BibleTest.txt");
            const string pattern = @"love\s*thy\s*neighbour";
            Regex regex = new(
                pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            AutoResetEvent progress = new(false);
            LineIndexer indexer = new(path, progress);
            RegexSearcher searcher = new(
                path, regex, progress);
            Console.WriteLine($"Searching ...");
            var processes = new List<IBackground>()
            {
                indexer,
                searcher
            };
            Background.StartAndWait(
                processes,
                progress,
                (_) =>
                {
                    Background.CancelAll(processes);
                },
                1000);
            Assert.AreEqual("", Background.AnyError(processes));
            Assert.AreEqual(true, Background.AnyCancelled(processes));
        }
    }
}

