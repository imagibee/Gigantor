using NUnit.Framework;
using System.Threading;
using System.IO;
using Imagibee.Gigantor;

namespace Testing {
    public class LineIndexerTests {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void InitialStateTest()
        {
            LineIndexer indexer = new(new AutoResetEvent(false));
            Assert.AreEqual(false, indexer.Running);
            Assert.AreEqual(0, indexer.LineCount);
            Assert.AreEqual(true, indexer.LastError == "");
        }

        [Test]
        public void EmptyPathTest()
        {
            LineIndexer indexer = new(new AutoResetEvent(false));
            indexer.Start("");
            while (indexer.Running == true) {
                Thread.Sleep(0);
            }
            Logger.Log($"error was {indexer.LastError}");
            Assert.AreEqual(true, indexer.LastError != "");
        }

        [Test]
        public void MissingPathTest()
        {
            LineIndexer indexer = new(new AutoResetEvent(false));
            indexer.Start("A Missing File");
            while (indexer.Running == true) {
                Thread.Sleep(0);
            }
            Logger.Log($"error was {indexer.LastError}");
            Assert.AreEqual(true, indexer.LastError != "");
        }

        [Test]
        public void SimpleTest()
        {
            var path = Path.Combine("Assets", "SimpleTest.txt");
            LineIndexer indexer = new(new AutoResetEvent(false));
            indexer.Start(path);
            while (indexer.Running == true) {
                Logger.Log($"{indexer.LineCount} lines indexed");
                Thread.Sleep(10);
            }
            Assert.AreEqual(true, indexer.LastError == "");
            Assert.AreEqual(6, indexer.LineCount);
            var index = indexer.GetIndex(0);
            Assert.AreEqual(false, index.HasValue);
            for (var i = 1; i <= indexer.LineCount; i++) {
                index = indexer.GetIndex(i);
                Assert.AreEqual(true, index.HasValue);
                Assert.AreEqual(path, index.Value.Path);
                Assert.AreEqual(1, index.Value.StartLine);
                Assert.AreEqual(6, index.Value.EndLine);
                Assert.AreEqual(0, index.Value.StartFpos);
            }
        }

        [Test]
        public void BibleTest()
        {
            const string LINE_0001 = "The Project Gutenberg eBook of The King James Bible";
            const string LINE_1516 = "that he said, Escape for thy life; look not behind thee, neither stay";
            var path = Path.Combine("Assets", "BibleTest.txt");
            LineIndexer indexer = new(new AutoResetEvent(false), chunkSize: 64 * 1024);
            indexer.Start(path);
            while (indexer.Running == true) {
                Logger.Log($"{indexer.LineCount} lines indexed");
                Thread.Sleep(1000);
            }
            Assert.AreEqual(true, indexer.LastError == "");
            Assert.AreEqual(100264, indexer.LineCount);
            using var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var index = indexer.GetIndex(1);
            Assert.AreEqual(true, index.HasValue);
            fileStream.Seek(index.Value.StartFpos, SeekOrigin.Begin);
            var line = new StreamReader(fileStream).ReadLine();
            Assert.AreEqual(LINE_0001, line);
            Assert.AreEqual(index.Value.StartFpos, indexer.GetIndex(1515).Value.StartFpos);
            index = indexer.GetIndex(1516);
            Assert.AreEqual(true, index.HasValue);
            fileStream.Seek(index.Value.StartFpos, SeekOrigin.Begin);
            line = new StreamReader(fileStream).ReadLine();
            Assert.AreEqual(LINE_1516, line);
            Assert.AreEqual(index.Value.StartFpos, indexer.GetIndex(2989).Value.StartFpos);
        }
    }
}

