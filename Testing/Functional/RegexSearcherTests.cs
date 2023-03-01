using System;
using System.Threading;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Imagibee.Gigantor;
using System.IO.Compression;
using Microsoft.VisualStudio.TestPlatform.Utilities;

#pragma warning disable CS8618

namespace Testing {
    public class RegexSearchTests {
        readonly int maxMatchCount = 5000;
        readonly int overlap = 0;
        readonly int chunkKiBytes = 64;
        readonly int maxWorkers = 1;
        string biblePath;
        string enwik9Gz;
        string enwik9;

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
                "", new Regex(""), progress, maxMatchCount, chunkKiBytes, maxWorkers, overlap);
            Assert.AreEqual(false, searcher.Running);
            Assert.AreEqual(0, searcher.MatchCount);
            Assert.AreEqual(true, searcher.Error == "");
        }

        [Test]
        public void EmptyPathTest()
        {
            AutoResetEvent progress = new(false);
            RegexSearcher searcher = new(
                "", new Regex(""), progress, maxMatchCount, chunkKiBytes, maxWorkers, overlap);
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
                "A Missing File", new Regex(""), progress, maxMatchCount, chunkKiBytes, maxWorkers, overlap);
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
                biblePath, regex, progress, maxMatchCount, chunkKiBytes, maxWorkers, pattern.Length);
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
        public void ChunkSizeTest()
        {
            AutoResetEvent progress = new(false);
            const string pattern = @"son\s*of\s*man";
            Regex regex = new(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            RegexSearcher searcher1 = new(
                biblePath, regex, progress, maxMatchCount, 64, maxWorkers, pattern.Length);
            RegexSearcher searcher2 = new(
                biblePath, regex, progress, maxMatchCount, 65, maxWorkers, pattern.Length);
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
                biblePath, regex, progress, 209, chunkKiBytes, maxWorkers, pattern.Length);
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
                biblePath, regex, progress, maxMatchCount, 64, maxWorkers, 32 * 1024);
            RegexSearcher searcher2 = new(
                biblePath, regex, progress, maxMatchCount, 64, maxWorkers, pattern.Length);
            Background.StartAndWait(
                new List<IBackground>() { searcher1, searcher2 },
                progress,
                (_) => { },
                1000);
            Logger.Log($"{searcher1.Error}");
            var m1 = searcher1.GetMatchData();
            var m2 = searcher1.GetMatchData();
            for (var i=0; i<10; i++) {
                Logger.Log($"1-> [{i}] {m1[i].Value} {m1[i].StartFpos}");
                Logger.Log($"2-> [{i}] {m2[i].Value} {m2[i].StartFpos}");
            }
            Assert.AreEqual(true, searcher1.Error == "");
            Assert.AreEqual(true, searcher2.Error == "");
            Assert.AreEqual(searcher1.MatchCount, searcher2.MatchCount);
        }

        [Test]
        public void GroupTest()
        {
            var maxMatches = 5;
            AutoResetEvent progress = new(false);
            const string pattern = @"(\w+)\s+is\s+(\w+)";
            Regex regex = new(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            RegexSearcher searcher = new(
                biblePath, regex, progress, maxMatchCount: maxMatches, 64, maxWorkers);
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
            using var fileStream = new FileStream(
                enwik9,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                chunkKiBytes,
                FileOptions.Asynchronous);
            RegexSearcher searcher = new(
                fileStream, regex, progress, maxMatchCount, chunkKiBytes: 512, maxWorkers: 16, overlap:pattern.Length);
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
        public void GzStreamTest()
        {
            AutoResetEvent progress = new(false);
            const string pattern = @"comfort\s*food";
            Regex regex = new(
                pattern,
                RegexOptions.IgnoreCase |
                RegexOptions.Compiled);
            using var fs = new FileStream(
                enwik9Gz, FileMode.Open);
            var stream = new GZipStream(
                fs, CompressionMode.Decompress, true);
            RegexSearcher searcher = new(
                stream,
                regex,
                progress,
                maxMatchCount,
                chunkKiBytes:512,
                maxWorkers: 16,
                overlap: pattern.Length);
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

