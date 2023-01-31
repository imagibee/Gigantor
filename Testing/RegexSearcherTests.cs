using NUnit.Framework;
using System.Threading;
using System.IO;
using System.Text.RegularExpressions;
using Imagibee.Gigantor;

namespace Testing {
    public class RegexSearchTests {
        readonly int maxMatchCount = 5000;
        readonly int maxMatchSize = 0;
        readonly int chunkSize = 64 * 1024;
        readonly int maxWorkers = 1;

        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void InitialStateTest()
        {
            AutoResetEvent progress = new(false);
            RegexSearcher searcher = new(
                "", new Regex(""), progress, maxMatchCount, maxMatchSize, chunkSize, maxWorkers);
            Assert.AreEqual(false, searcher.Running);
            Assert.AreEqual(0, searcher.MatchCount);
            Assert.AreEqual(true, searcher.Error == "");
        }

        [Test]
        public void EmptyPathTest()
        {
            AutoResetEvent progress = new(false);
            RegexSearcher searcher = new(
                "", new Regex(""), progress, maxMatchCount, maxMatchSize, chunkSize, maxWorkers);
            searcher.Start();
            Utilities.Wait(
                searcher,
                progress,
                (_) => { },
                1000);
            Logger.Log($"error was {searcher.Error}");
            Assert.AreEqual(true, searcher.Error != "");
        }

        [Test]
        public void MissingPathTest()
        {
            AutoResetEvent progress = new(false);
            RegexSearcher searcher = new(
                "A Missing File", new Regex(""), progress, maxMatchCount, maxMatchSize, chunkSize, maxWorkers);
            searcher.Start();
            Utilities.Wait(
                searcher,
                progress,
                (_) => { },
                1000);
            Logger.Log($"error was {searcher.Error}");
            Assert.AreEqual(true, searcher.Error != "");
        }

        [Test]
        public void BibleTest()
        {
            AutoResetEvent progress = new(false);
            var path = Path.Combine("Assets", "BibleTest.txt");
            const string pattern = @"son\s*of\s*man";
            Regex regex = new(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            RegexSearcher searcher = new(
                path, regex, progress, maxMatchCount, pattern.Length, chunkSize, maxWorkers);
            searcher.Start();
            Utilities.Wait(
                searcher,
                progress,
                (_) => { },
                1000);
            Assert.AreEqual(true, searcher.Error == "");
            Assert.AreEqual(210, searcher.MatchCount);
            Assert.AreEqual(210, searcher.GetMatchData().Count);
            foreach (var matchData in searcher.GetMatchData()) {
                Logger.Log($"{matchData.Value} named '{matchData.Name}' " +
                    $"at {matchData.StartFpos}]");
            }
        }

        [Test]
        public void MaxMatchCountTest()
        {
            AutoResetEvent progress = new(false);
            var path = Path.Combine("Assets", "BibleTest.txt");
            const string pattern = @"son\s*of\s*man";
            Regex regex = new(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            RegexSearcher searcher = new(
                path, regex, progress, 209, pattern.Length, chunkSize, maxWorkers);
            searcher.Start();
            Utilities.Wait(
                searcher,
                progress,
                (_) => { },
                1000);
            Assert.AreEqual(true, searcher.Error == "");
            Assert.AreEqual(209, searcher.MatchCount);
            Assert.AreEqual(209, searcher.GetMatchData().Count);
        }
    }
}

