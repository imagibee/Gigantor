using NUnit.Framework;
using System.Threading;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Imagibee.Gigantor;

namespace Testing {
    public class RegexSearchTests {
        readonly int chunkSize = 64 * 1024;
        readonly int maxWorkers = 1;

        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void InitialStateTest()
        {
            RegexSearcher searcher = new(new AutoResetEvent(false), chunkSize, maxWorkers);
            Assert.AreEqual(false, searcher.Running);
            Assert.AreEqual(0, searcher.MatchCount);
            Assert.AreEqual(true, searcher.LastError == "");
        }

        [Test]
        public void EmptyPathTest()
        {
            RegexSearcher searcher = new(new AutoResetEvent(false), chunkSize, maxWorkers);
            searcher.Start("", new Regex(""), 20);
            searcher.Wait();
            Logger.Log($"error was {searcher.LastError}");
            Assert.AreEqual(true, searcher.LastError != "");
        }

        [Test]
        public void MissingPathTest()
        {
            RegexSearcher searcher = new(new AutoResetEvent(false), chunkSize, maxWorkers);
            searcher.Start("A Missing File", new Regex(""), 20);
            searcher.Wait();
            Logger.Log($"error was {searcher.LastError}");
            Assert.AreEqual(true, searcher.LastError != "");
        }

        [Test]
        public void BibleTest()
        {
            AutoResetEvent progress = new(false);
            var path = Path.Combine("Assets", "BibleTest.txt");
            const string pattern = @"son\s*of\s*man";
            Regex regex = new(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            RegexSearcher searcher = new(progress, chunkSize, maxWorkers);
            searcher.Start(path, regex, pattern.Length);
            RegexSearcher.Wait(
                new List<RegexSearcher>() { searcher },
                progress,
                (_) => { },
                1000);
            Assert.AreEqual(true, searcher.LastError == "");
            Assert.AreEqual(210, searcher.MatchCount);
            foreach (var matchData in searcher.GetMatchData()) {
                Logger.Log($"[{matchData.StartFpos}] {matchData.Path}");
                foreach (var match in matchData.Matches) {
                    Logger.Log($"{match}");
                }
            }
        }
    }
}

