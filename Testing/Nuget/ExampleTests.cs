using NUnit.Framework;
using System.Text.RegularExpressions;
using Imagibee.Gigantor;
using System.Diagnostics;
using System;

#pragma warning disable CS0436

namespace NugetTesting {

    public class ExampleTests {
        string enwik9Path;
        string biblePath;

        [SetUp]
        public void Setup()
        {
            enwik9Path = Path.Combine(
                Path.Combine(Path.GetTempPath(), "gigantor"),
                "enwik9");
            biblePath = Path.Combine(
                Path.Combine(Path.GetTempPath(), "gigantor"),
                "10.txt.utf-8");
        }

        [Test]
        public void MixedExampleTest()
        {
            // The regular expression for the search
            const string pattern = @"comfort\s*food";
            Regex regex = new(
                pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

            // A shared wait event to facilitate progress notifications
            AutoResetEvent progress = new(false);

            // Create the search and indexing workers
            LineIndexer indexer = new(enwik9Path, progress);
            RegexSearcher searcher = new(enwik9Path, regex, progress);

            // Create a IBackground collection for convenient managment
            var processes = new List<IBackground>()
            {
                indexer,
                searcher
            };

            // Start search and indexing in parallel and wait for completion
            Console.WriteLine($"Searching ...");
            Background.StartAndWait(
                processes,
                progress,
                (_) => {},
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
            if (searcher.MatchCount != 0) {
                Console.WriteLine($"Found {searcher.MatchCount} matches ...");
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
                using var fileStream = Imagibee.Gigantor.FileStream.Create(
                    enwik9Path, 4096 * 1024);
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
            }
        }

        [Test]
        public void MixedCancelTest()
        {
            const string pattern = @"love\s*thy\s*neighbour";
            Regex regex = new(
                pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            AutoResetEvent progress = new(false);
            LineIndexer indexer = new(enwik9Path, progress);
            RegexSearcher searcher = new(
                biblePath, regex, progress);
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
            Assert.Multiple(() =>
            {
                Assert.That(Background.AnyError(processes), Is.EqualTo(""));
                Assert.That(Background.AnyCancelled(processes), Is.EqualTo(true));
            });
        }
    }
}

