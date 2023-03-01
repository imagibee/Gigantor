using System;
using System.Threading;
using System.IO;
using NUnit.Framework;
using Imagibee.Gigantor;

#pragma warning disable CS8618

namespace Testing {
    public class DuplicateCheckerTests {
        string biblePath;
        string simplePath;
        string simplePath2;

        [SetUp]
        public void Setup()
        {
            biblePath = Utilities.GetGutenbergBible();
            simplePath = Utilities.GetSimpleFile();
            simplePath2 = Utilities.GetSimpleFile2();
        }

        [Test]
        public void InitialStateTest()
        {
            DuplicateChecker checker = new("", "", new AutoResetEvent(false));
            Assert.AreEqual(false, checker.Running);
            Assert.AreEqual(false, checker.Identical);
            Assert.AreEqual(0, checker.ByteCount);
            Assert.AreEqual(true, checker.Error == "");
        }

        [Test]
        public void EmptyPathTest()
        {
            AutoResetEvent progress = new(false);
            DuplicateChecker checker = new("", "", progress);
            Assert.Throws<ArgumentException>(() => checker.Start());
        }

        [Test]
        public void MissingPathTest()
        {
            AutoResetEvent progress = new(false);
            DuplicateChecker checker = new(
                "A Missing File1", "A Missing File2", progress);
            Assert.Throws<FileNotFoundException>(() => checker.Start());
        }

        [Test]
        public void MatchingTest()
        {
            AutoResetEvent progress = new(false);
            DuplicateChecker checker = new(biblePath, biblePath, progress);
            Background.StartAndWait(
                checker,
                progress,
                (_) => { },
                1000);
            Assert.AreEqual(true, checker.Error == "");
            Assert.AreEqual(true, checker.Identical);
        }

        [Test]
        public void SizeMismatchTest()
        {
            AutoResetEvent progress = new(false);
            DuplicateChecker checker = new(biblePath, simplePath, progress);
            Background.StartAndWait(
                checker,
                progress,
                (_) => { },
                1000);
            Assert.AreEqual(true, checker.Error == "");
            Assert.AreEqual(false, checker.Identical);
        }

        [Test]
        public void ValueMismatchTest()
        {
            AutoResetEvent progress = new(false);
            DuplicateChecker checker = new(simplePath, simplePath2, progress);
            Background.StartAndWait(
                checker,
                progress,
                (_) => { },
                1000);
            Assert.AreEqual(true, checker.Error == "");
            Assert.AreEqual(false, checker.Identical);
        }
    }
}

