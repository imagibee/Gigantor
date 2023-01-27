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
        readonly string simplePath = Path.Combine("Assets", "SimpleTest.txt");
        readonly string biblePath = Path.Combine("Assets", "BibleTest.txt");
        const string BIBLE_000001 = "The Project Gutenberg eBook of The King James Bible";
        const string BIBLE_001515 = "19:17 And it came to pass, when they had brought them forth abroad,";
        const string BIBLE_001516 = "that he said, Escape for thy life; look not behind thee, neither stay";
        const string BIBLE_002989 = "mother with the children.";
        const string BIBLE_002990 = "";
        const string BIBLE_002991 = "32:12 And thou saidst, I will surely do thee good, and make thy seed";
        const string BIBLE_100263 = "subscribe to our email newsletter to hear about new eBooks.";
        const string BIBLE_100264 = "";

        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void InitialStateTest()
        {
            LineIndexer indexer = new(new AutoResetEvent(false), chunkKiBytes, maxWorkers);
            Assert.AreEqual(false, indexer.Running);
            Assert.AreEqual(0, indexer.LineCount);
            Assert.AreEqual(true, indexer.LastError == "");
        }

        [Test]
        public void EmptyPathTest()
        {
            LineIndexer indexer = new(new AutoResetEvent(false), chunkKiBytes, maxWorkers);
            indexer.Start("");
            indexer.Wait();
            Assert.AreEqual(true, indexer.LastError != "");
        }

        [Test]
        public void MissingPathTest()
        {
            LineIndexer indexer = new(new AutoResetEvent(false), chunkKiBytes, maxWorkers);
            indexer.Start("A Missing File");
            indexer.Wait();
            Logger.Log($"error was {indexer.LastError}");
            Assert.AreEqual(true, indexer.LastError != "");
        }

        [Test]
        public void FilePositionTest()
        {
            AutoResetEvent progress = new(false);
            LineIndexer indexer = new(progress, chunkKiBytes, maxWorkers);
            indexer.Start(biblePath);
            LineIndexer.Wait(
                new List<LineIndexer>() { indexer },
                progress,
                (_) => { },
                1000);
            Assert.AreEqual(true, indexer.LastError == "");
            Assert.AreEqual(-1, indexer.PositionFromLine(0));
            Assert.AreEqual(0, indexer.PositionFromLine(1));
            using var fileStream = new FileStream(biblePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var streamReader = new Imagibee.Gigantor.StreamReader(fileStream);
            List<Tuple<int, string>> tests = new()
            {
                new Tuple<int, string>(1, BIBLE_000001),
                new Tuple<int, string>(1515, BIBLE_001515),
                new Tuple<int, string>(1516, BIBLE_001516),
                new Tuple<int, string>(2989, BIBLE_002989),
                new Tuple<int, string>(2990, BIBLE_002990),
                new Tuple<int, string>(2991, BIBLE_002991),
                new Tuple<int, string>(100263, BIBLE_100263),
                new Tuple<int, string>(100264, BIBLE_100264),
            };
            foreach (var t in tests) {
                fileStream.Seek(indexer.PositionFromLine(t.Item1), SeekOrigin.Begin);
                Assert.AreEqual(t.Item2, streamReader.ReadLine());
            }
        }

        [Test]
        public void LineNumberTest()
        {
            LineIndexer indexer = new(new AutoResetEvent(false), chunkKiBytes, maxWorkers);
            indexer.Start(biblePath);
            indexer.Wait();
            Assert.AreEqual(true, indexer.LastError == "");
            foreach (var line in new List<int>() { 1, 1515, 1516, 2989, 2990, 2991, 100263, 100264 }) {
                Assert.AreEqual(line, indexer.LineFromPosition(indexer.PositionFromLine(line)));
            }
        }

        [Test]
        public void SimpleTest()
        {
            LineIndexer indexer = new(new AutoResetEvent(false), chunkKiBytes, maxWorkers);
            indexer.Start(simplePath);
            indexer.Wait();
            Assert.AreEqual(true, indexer.LastError == "");
            Assert.AreEqual(6, indexer.LineCount);
            using var fileStream = new FileStream(simplePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            Imagibee.Gigantor.StreamReader reader = new(fileStream);
            fileStream.Seek(indexer.PositionFromLine(1), SeekOrigin.Begin);
            var lines = new List<string>() { "hello", "world", "", "", "foo", "bar" };
            for (var i=0; i<lines.Count; i++) {
                Assert.AreEqual(lines[i], reader.ReadLine());
            }
        }
    }
}

