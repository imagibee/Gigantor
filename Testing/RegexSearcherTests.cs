using System;
using System.Threading;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Imagibee.Gigantor;

namespace Testing {
    public class RegexSearchTests {
        readonly int maxMatchCount = 5000;
        readonly int maxMatchSize = 0;
        readonly int chunkSize = 64;
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
            Console.WriteLine(searcher.Error);
            Assert.AreEqual(true, searcher.Error == "");
            Assert.AreEqual(210, searcher.MatchCount);
            Assert.AreEqual(210, searcher.GetMatchData().Count);
            foreach (var matchData in searcher.GetMatchData()) {
                Logger.Log($"{matchData.Value} named '{matchData.Name}' " +
                    $"at {matchData.StartFpos}]");
            }
        }

        [Test]
        public void ChunkSizeTest()
        {
            AutoResetEvent progress = new(false);
            var path = Path.Combine("Assets", "BibleTest.txt");
            const string pattern = @"son\s*of\s*man";
            Regex regex = new(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            RegexSearcher searcher1 = new(
                path, regex, progress, maxMatchCount, pattern.Length, 64, maxWorkers);
            RegexSearcher searcher2 = new(
                path, regex, progress, maxMatchCount, pattern.Length, 65, maxWorkers);
            Utilities.StartAndWait(
                new List<IBackground>() { searcher1, searcher2 },
                progress,
                (_) => { },
                1000);
            Assert.AreEqual(true, searcher1.Error == "");
            Assert.AreEqual(true, searcher2.Error == "");
            Assert.AreEqual(searcher1.MatchCount, searcher2.MatchCount);
            var md1 = searcher1.GetMatchData();
            var md2 = searcher2.GetMatchData();
            for (var i = 0; i < md1.Count; i++) {
                Assert.AreEqual(md1[i].Name, md2[i].Name);
                Assert.AreEqual(md1[i].Value, md2[i].Value);
                Assert.AreEqual(md1[i].StartFpos, md2[i].StartFpos);
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

