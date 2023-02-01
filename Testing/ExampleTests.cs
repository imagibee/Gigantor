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
            const string pattern = @"love\s*thy\s*neighbour";
            Regex regex = new(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

            // A shared wait event to facilitate progress notifications
            AutoResetEvent progress = new(false);

            // Create the search and index workers
            LineIndexer indexer = new(path, progress);
            RegexSearcher searcher = new(path, regex, progress, 5000, pattern.Length);

            // A progress bar
            Utilities.ByteProgress progressBar = new(40, Utilities.FileByteCount(path));

            // Start search and index in parallel and wait for completion
            Console.WriteLine($"Searching ...");
            Utilities.StartAndWait(
                new List<IBackground>() { indexer, searcher },
                progress,
                (processes) =>
                {
                    progressBar.Update(
                        processes.Select((p) => p.ByteCount).Sum());
                },
                1000);
            Console.Write('\n');

            // All done, check for errors
            if (searcher.Error.Length != 0) {
                throw new Exception(searcher.Error);
            }

            // Display search results
            Console.WriteLine($"Found {searcher.MatchCount} matches ...");
            var matchDatas = searcher.GetMatchData();
            for (var i=0; i<matchDatas.Count; i++) {
                var matchData = matchDatas[i];
                Console.WriteLine(
                    $"[{i}]({matchData.Value}) ({matchData.Name}) " +
                    $"at {indexer.LineFromPosition(matchData.StartFpos)} " +
                    $"({matchData.StartFpos})");
            }

            // Display the lines before and after the 1st search result
            var contextSize = 2;
            Console.WriteLine($"{2* contextSize + 1} line context ...");
            var match = searcher.GetMatchData()[2];
            var matchLine = indexer.LineFromPosition(match.StartFpos);
            using FileStream fileStream = new(path, FileMode.Open);
            Imagibee.Gigantor.StreamReader gigantorReader = new(fileStream);
            fileStream.Seek(indexer.PositionFromLine(matchLine - contextSize), SeekOrigin.Begin);
            for (var line = matchLine - contextSize; line <= matchLine + contextSize; line++) {
                Console.WriteLine(
                    $"[{line}]({indexer.PositionFromLine(line)})  " +
                    gigantorReader.ReadLine());
            }
            Assert.AreEqual(true, false);
        }
    }
}

