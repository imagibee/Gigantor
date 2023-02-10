using System;
using System.Threading;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Imagibee.Gigantor;

namespace Testing {
    public class LineIndexerTests {
        readonly int chunkKiBytes = 64;
        readonly int maxWorkers = 1;
        string enwik9Path;
        const string LINE_000001 = "<mediawiki xmlns=\"http://www.mediawiki.org/xml/export-0.3/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:schemaLocation=\"http://www.mediawiki.org/xml/export-0.3/ http://www.mediawiki.org/xml/export-0.3.xsd\" version=\"0.3\" xml:lang=\"en\">";
        const string LINE_001515 = "    <title>ArtificialLanguages</title>";
        const string LINE_001516 = "    <id>56</id>";
        const string LINE_002989 = "* [http://www.androphile.org/preview/Library/Mythology/Greek/ The Story of Achilles and Patroclus]";
        const string LINE_002990 = "* [http://www.historyguide.org/ancient/troy.html Trojan War Resources]";
        const string LINE_002991 = "";
        const string LINE_13147025 = "* Pressure changes can cause a 'squeeze' or [[barotrauma]] in the tissues surrounding trapped air inside the body, such as the [[lung]]s, behind the [[eardrum]], inside [[paranasal sinus]]es, or even trapped underneath [[dental]] fillings. ";
        const string LINE_13147026 = "* Breathing high-pressure oxygen for long periods can causes [[oxygen toxicity]]. One of the side effects ";

        [SetUp]
        public void Setup()
        {
            enwik9Path = Utilities.GetEnwik9();
        }

        [Test]
        public void InitialStateTest()
        {
            LineIndexer indexer = new("", new AutoResetEvent(false), chunkKiBytes, maxWorkers);
            Assert.AreEqual(false, indexer.Running);
            Assert.AreEqual(0, indexer.LineCount);
            Assert.AreEqual(true, indexer.Error == "");
            Assert.AreEqual(false, indexer.Cancelled);
        }

        [Test]
        public void EmptyPathTest()
        {
            AutoResetEvent progress = new(false);
            LineIndexer indexer = new("", progress, chunkKiBytes, maxWorkers);
            indexer.Start();
            Background.Wait(indexer, progress, (_) => { });
            Assert.AreEqual(true, indexer.Error != "");
            Assert.AreEqual(false, indexer.Cancelled);
        }

        [Test]
        public void MissingPathTest()
        {
            AutoResetEvent progress = new(false);
            LineIndexer indexer = new("A Missing File", progress, chunkKiBytes, maxWorkers);
            indexer.Start();
            Background.Wait(indexer, progress, (_) => { });
            Assert.AreEqual(true, indexer.Error != "");
            Assert.AreEqual(false, indexer.Cancelled);
        }

        [Test]
        public void FilePositionTest()
        {
            AutoResetEvent progress = new(false);
            LineIndexer indexer = new(enwik9Path, progress, chunkKiBytes: 512, maxWorkers: 0);
            indexer.Start();
            Background.Wait(
                indexer,
                progress,
                (_) => { },
                1000);
            Assert.AreEqual(true, indexer.Error == "");
            Assert.AreEqual(false, indexer.Cancelled);
            Assert.AreEqual(-1, indexer.PositionFromLine(0));
            Assert.AreEqual(0, indexer.PositionFromLine(1));
            Assert.AreEqual(13147026, indexer.LineCount);
            using var fileStream = new FileStream(enwik9Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var streamReader = new Imagibee.Gigantor.StreamReader(fileStream);
            List<Tuple<int, string>> tests = new()
            {
                new Tuple<int, string>(1, LINE_000001),
                new Tuple<int, string>(1515, LINE_001515),
                new Tuple<int, string>(1516, LINE_001516),
                new Tuple<int, string>(2989, LINE_002989),
                new Tuple<int, string>(2990, LINE_002990),
                new Tuple<int, string>(2991, LINE_002991),
                new Tuple<int, string>(13147025, LINE_13147025),
                new Tuple<int, string>(13147026, LINE_13147026),
            };
            foreach (var t in tests) {
                fileStream.Seek(indexer.PositionFromLine(t.Item1), SeekOrigin.Begin);
                Assert.AreEqual(t.Item2, streamReader.ReadLine());
            }
        }

        [Test]
        public void LineNumberTest()
        {
            AutoResetEvent progress = new(false);
            LineIndexer indexer = new(enwik9Path, progress, chunkKiBytes: 511, maxWorkers: 0);
            indexer.Start();
            Background.Wait(indexer, progress, (_) => { });
            Assert.AreEqual(true, indexer.Error == "");
            Assert.AreEqual(false, indexer.Cancelled);
            using var fileStream = new FileStream(enwik9Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var streamReader = new Imagibee.Gigantor.StreamReader(fileStream);
            foreach (var line in new List<int>() { 1, 1515, 1516, 2989, 2990, 2991, 13147025, 13147026 }) {
                var pos = indexer.PositionFromLine(line);
                fileStream.Seek(pos, SeekOrigin.Begin);
                Assert.AreEqual(line, indexer.LineFromPosition(pos));
            }
        }
    }
}

