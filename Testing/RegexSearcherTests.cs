using NUnit.Framework;
using System.Threading;
using System.IO;
using System.Text.RegularExpressions;
using System.Diagnostics;
using Imagibee.Gigantor;

namespace Testing {
    public class RegexSearchTests {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void InitialStateTest()
        {
            RegexSearcher searcher = new(new AutoResetEvent(false));
            Assert.AreEqual(false, searcher.Running);
            Assert.AreEqual(0, searcher.MatchCount);
            Assert.AreEqual(true, searcher.LastError == "");
        }

        [Test]
        public void EmptyPathTest()
        {
            RegexSearcher searcher = new(new AutoResetEvent(false));
            searcher.Start("", new Regex(""), 20);
            while (searcher.Running == true) {
                Thread.Sleep(0);
            }
            Logger.Log($"error was {searcher.LastError}");
            Assert.AreEqual(true, searcher.LastError != "");
        }

        [Test]
        public void MissingPathTest()
        {
            RegexSearcher searcher = new(new AutoResetEvent(false));
            searcher.Start("A Missing File", new Regex(""), 20);
            while (searcher.Running == true) {
                Thread.Sleep(0);
            }
            Logger.Log($"error was {searcher.LastError}");
            Assert.AreEqual(true, searcher.LastError != "");
        }

        [Test]
        public void BibleTest()
        {
            var path = Path.Combine("Assets", "BibleTest.txt");
            const string pattern = @"son\s*of\s*man";
            Regex regex = new(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            RegexSearcher searcher = new(new AutoResetEvent(false));
            searcher.Start(path, regex, pattern.Length);
            while (searcher.Running == true) {
                Thread.Sleep(0);
            }
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

