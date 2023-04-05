using System;
using System.Threading;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Imagibee.Gigantor;
using System.IO.Compression;


namespace Testing {
    public class RegexSearchTests {
        readonly int maxMatchCount = 5000;
        readonly int overlap = 0;
        readonly int partitionSize = 1024 * 1024;
        readonly int maxWorkers = 1;
        string biblePath = "";
        string enwik9Gz = "";
        string enwik9 = "";

        [SetUp]
        public void Setup()
        {
            biblePath = Utilities.GetGutenbergBible();
            enwik9Gz = Utilities.GetEnwik9Gz();
            enwik9 = Utilities.GetEnwik9();
        }

        [Test]
        public void InitialStateTest()
        {
            AutoResetEvent progress = new(false);
            RegexSearcher searcher = new(
                "", new Regex(""), progress, maxMatchCount, partitionSize, maxWorkers, overlap);
            Assert.AreEqual(false, searcher.Running);
            Assert.AreEqual(0, searcher.MatchCount);
            Assert.AreEqual(true, searcher.Error == "");
        }

        [Test]
        public void EmptyPathTest()
        {
            AutoResetEvent progress = new(false);
            RegexSearcher searcher = new(
                "", new Regex(""), progress, maxMatchCount, partitionSize, maxWorkers, overlap);
            Background.StartAndWait(
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
                "A Missing File", new Regex(""), progress, maxMatchCount, partitionSize, maxWorkers, overlap);
            Background.StartAndWait(
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
            const string pattern = @"son\s*of\s*man";
            Regex regex = new(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            RegexSearcher searcher = new(
                biblePath, regex, progress, maxMatchCount, partitionSize, maxWorkers);
            Background.StartAndWait(
                searcher,
                progress,
                (_) => { },
                1000);
            Console.WriteLine(searcher.Error);
            Assert.AreEqual(true, searcher.Error == "");
            Assert.AreEqual(210, searcher.MatchCount);
            Assert.AreEqual(210, searcher.GetMatchData().Count);
            Assert.AreEqual(4457889, searcher.ByteCount);
            foreach (var matchData in searcher.GetMatchData()) {
                Logger.Log($"{matchData.Value} named '{matchData.Name}' " +
                    $"at {matchData.StartFpos}]");
            }
        }

        [Test]
        public void MultipleRegexTest()
        {
            AutoResetEvent progress = new(false);
            const string pattern = @"son\s*of\s*man";
            const string pattern1 = @"eye\s*of\s*a\s*needle";
            Regex regex1 = new(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            Regex regex2 = new(pattern1, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            RegexSearcher searcher = new(
                biblePath, new List<Regex>() { regex1, regex2 }, progress, maxMatchCount, partitionSize, maxWorkers);
            Background.StartAndWait(
                searcher,
                progress,
                (_) => { },
                1000);
            Console.WriteLine(searcher.Error);
            Assert.AreEqual(true, searcher.Error == "");
            Assert.AreEqual(212, searcher.MatchCount);
            var c1 = 0;
            var c2 = 0;
            foreach (var m in searcher.GetMatchData()) {
                if (m.RegexIndex == 0) {
                    c1++;
                }
                else if (m.RegexIndex == 1) {
                    c2++;
                }
            }
            Assert.AreEqual(210, c1);
            Assert.AreEqual(2, c2);
            Assert.AreEqual(4457889, searcher.ByteCount);
        }

        [Test]
        public void ChunkSizeTest()
        {
            AutoResetEvent progress = new(false);
            const string pattern = @"son\s*of\s*man";
            Regex regex = new(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            RegexSearcher searcher1 = new(
                biblePath, regex, progress, maxMatchCount, 1, maxWorkers);
            RegexSearcher searcher2 = new(
                biblePath, regex, progress, maxMatchCount, 2, maxWorkers);
            Background.StartAndWait(
                new List<IBackground>() { searcher1, searcher2 },
                progress,
                (_) => { },
                1000);
            Logger.Log($"{searcher1.Error}");
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
            const string pattern = @"son\s*of\s*man";
            Regex regex = new(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            RegexSearcher searcher = new(
                biblePath, regex, progress, 209, partitionSize, maxWorkers);
            Background.StartAndWait(
                searcher,
                progress,
                (_) => { },
                1000);
            Assert.AreEqual(true, searcher.Error == "");
            Assert.AreEqual(209, searcher.MatchCount);
            Assert.AreEqual(209, searcher.GetMatchData().Count);
        }

        [Test]
        public void DedupTest()
        {
            AutoResetEvent progress = new(false);
            const string pattern = @"son\s*of\s*man";
            Regex regex = new(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            RegexSearcher searcher1 = new(
                biblePath, regex, progress, maxMatchCount, partitionSize, maxWorkers);
            using var fileStream = new System.IO.FileStream(
                biblePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                partitionSize,
                FileOptions.Asynchronous);
            RegexSearcher searcher2 = new(
                fileStream, regex, progress, maxMatchCount, partitionSize, maxWorkers, overlap: partitionSize/2);
            Background.StartAndWait(
                new List<IBackground>() { searcher1, searcher2 },
                progress,
                (_) => { },
                1000);
            var m1 = searcher1.GetMatchData();
            var m2 = searcher2.GetMatchData();
            for (var i=0; i<m1.Count; i++) {
                if (m1[i].Value != m2[i].Value || m1[i].StartFpos != m2[i].StartFpos) {
                    Logger.Log($"1-> [{i}] {m1[i].Value} {m1[i].StartFpos}");
                    Logger.Log($"2-> [{i}] {m2[i].Value} {m2[i].StartFpos}");
                }
            }
            Assert.AreEqual(m1.Count, m2.Count);
            Assert.AreEqual(true, searcher1.Error == "");
            Assert.AreEqual(true, searcher2.Error == "");
            Assert.AreEqual(210, searcher1.MatchCount);
            Assert.AreEqual(searcher2.GetMatchData().Count, searcher2.MatchCount);
            Assert.AreEqual(210, searcher2.MatchCount);
        }

        [Test]
        public void GroupTest()
        {
            var maxMatches = 5;
            AutoResetEvent progress = new(false);
            const string pattern = @"(\w+)\s+is\s+(\w+)";
            Regex regex = new(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            RegexSearcher searcher = new(
                biblePath, regex, progress, maxMatchCount: maxMatches, partitionSize, maxWorkers);
            Background.StartAndWait(
                new List<IBackground>() { searcher },
                progress,
                (_) => { },
                1000);
            Logger.Log($"{searcher.Error}");
            var m = searcher.GetMatchData();
            for (var i = 0; i < maxMatches; i++) {
                Logger.Log($"1-> [{i}] {m[i].Value} {m[i].StartFpos}");
            }
            Assert.AreEqual(true, searcher.Error == "");
            Assert.AreEqual(maxMatches, searcher.MatchCount);
            var matches = searcher.GetMatchData();
            foreach (var match in matches) {
                foreach (var group in match.Groups) {
                    Logger.Log($"{group.Name} {group.Value} {group.StartFpos}");
                    foreach (var c in group.Captures) {
                        Logger.Log($"{c.Value} {c.StartFpos}");
                    }
                }
            }
        }

        [Test]
        public void StreamTest()
        {
            AutoResetEvent progress = new(false);
            const string pattern = @"comfort\s*food";
            Regex regex = new(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            using var fileStream = new System.IO.FileStream(
                enwik9,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                partitionSize,
                FileOptions.Asynchronous);
            RegexSearcher searcher = new(
                fileStream, regex, progress, maxMatchCount, partitionSize, maxWorkers: 16);
            Background.StartAndWait(
                searcher,
                progress,
                (_) => { },
                1000);
            Console.WriteLine(searcher.Error);
            Assert.AreEqual(true, searcher.Error == "");
            Assert.AreEqual(11, searcher.MatchCount);
            Assert.AreEqual(11, searcher.GetMatchData().Count);
            Assert.AreEqual(1000000000, searcher.ByteCount);
            foreach (var matchData in searcher.GetMatchData()) {
                Logger.Log($"{matchData.Value} named '{matchData.Name}' " +
                    $"at {matchData.StartFpos}]");
            }
        }

        [Test]
        public void MultipleRegexStreamTest()
        {
            AutoResetEvent progress = new(false);
            const string pattern = @"comfort\s*food";
            const string pattern1 = @"needle\s*in\s*a\s*haystack";
            Regex regex1 = new(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            Regex regex2 = new(pattern1, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            using var fileStream = new System.IO.FileStream(
                enwik9,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                partitionSize,
                FileOptions.Asynchronous);
            RegexSearcher searcher = new(
                fileStream, new List<Regex>() { regex1, regex2 }, progress, maxMatchCount, partitionSize, maxWorkers: 16);
            Background.StartAndWait(
                searcher,
                progress,
                (_) => { },
                1000);
            Console.WriteLine(searcher.Error);
            Assert.AreEqual(true, searcher.Error == "");
            Assert.AreEqual(15, searcher.MatchCount);
            Assert.AreEqual(15, searcher.GetMatchData().Count);
            var c1 = 0;
            var c2 = 0;
            foreach (var m in searcher.GetMatchData()) {
                if (m.RegexIndex == 0) {
                    c1++;
                }
                else if (m.RegexIndex == 1) {
                    c2++;
                }
            }
            Assert.AreEqual(11, c1);
            Assert.AreEqual(4, c2);
            Assert.AreEqual(1000000000, searcher.ByteCount);
        }

        [Test]
        public void GzStreamTest()
        {
            AutoResetEvent progress = new(false);
            const string pattern = @"comfort\s*food";
            Regex regex = new(
                pattern,
                RegexOptions.IgnoreCase |
                RegexOptions.Compiled);
            using var fs = new System.IO.FileStream(
                enwik9Gz, FileMode.Open);
            var stream = new GZipStream(
                fs, CompressionMode.Decompress, true);
            RegexSearcher searcher = new(
                stream,
                regex,
                progress,
                maxMatchCount,
                partitionSize,
                maxWorkers: 16);
            Background.StartAndWait(
                searcher,
                progress,
                (_) => { },
                1000);
            Console.WriteLine(searcher.Error);
            Assert.AreEqual(true, searcher.Error == "");
            Assert.AreEqual(11, searcher.MatchCount);
            Assert.AreEqual(11, searcher.GetMatchData().Count);
            Assert.AreEqual(1000000000, searcher.ByteCount);
            foreach (var matchData in searcher.GetMatchData()) {
                Logger.Log($"{matchData.Value} named '{matchData.Name}' " +
                    $"at {matchData.StartFpos}]");
            }
        }
    }
}

