using System;
using System.Threading;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Imagibee.Gigantor;

namespace Testing {
    public class DuplicateCheckerTests {
        readonly string biblePath = Path.Combine("Assets", "BibleTest.txt");
        readonly string simplePath = Path.Combine("Assets", "SimpleTest.txt");
        readonly string simplePath2 = Path.Combine("Assets", "SimpleTest2.txt");

        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void InitialStateTest()
        {
            DuplicateChecker checker = new(new AutoResetEvent(false));
            Assert.AreEqual(false, checker.Running);
            Assert.AreEqual(false, checker.Identical);
            Assert.AreEqual(0, checker.ByteCount);
            Assert.AreEqual(true, checker.LastError == "");
        }

        [Test]
        public void EmptyPathTest()
        {
            DuplicateChecker checker = new(new AutoResetEvent(false));
            checker.Start("", "");
            checker.Wait();
            Assert.AreEqual(true, checker.LastError != "");
        }

        [Test]
        public void MissingPathTest()
        {
            DuplicateChecker checker = new(new AutoResetEvent(false));
            checker.Start("A Missing File1", "A Missing File2");
            checker.Wait();
            Assert.AreEqual(true, checker.LastError != "");
        }

        [Test]
        public void MatchingTest()
        {
            AutoResetEvent progress = new(false);
            DuplicateChecker checker = new(new AutoResetEvent(false));
            checker.Start(biblePath, biblePath);
            DuplicateChecker.Wait(
                new List<DuplicateChecker>() { checker },
                progress,
                (_) => { },
                1000);
            Assert.AreEqual(true, checker.LastError == "");
            Assert.AreEqual(true, checker.Identical);
        }

        public void SizeMismatchTest()
        {
            AutoResetEvent progress = new(false);
            DuplicateChecker checker = new(new AutoResetEvent(false));
            checker.Start(biblePath, simplePath);
            DuplicateChecker.Wait(
                new List<DuplicateChecker>() { checker },
                progress,
                (_) => { },
                1000);
            Assert.AreEqual(true, checker.LastError == "");
            Assert.AreEqual(false, checker.Identical);
        }

        public void ValueMismatchTest()
        {
            AutoResetEvent progress = new(false);
            DuplicateChecker checker = new(new AutoResetEvent(false));
            checker.Start(simplePath, simplePath2);
            DuplicateChecker.Wait(
                new List<DuplicateChecker>() { checker },
                progress,
                (_) => { },
                1000);
            Assert.AreEqual(true, checker.LastError == "");
            Assert.AreEqual(false, checker.Identical);
        }
    }
}

