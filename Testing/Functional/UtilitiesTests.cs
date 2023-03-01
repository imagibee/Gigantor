using System;
using System.IO;
using System.Diagnostics;
using NUnit.Framework;
using Imagibee.Gigantor;

#pragma warning disable CS8618

namespace Testing {
    public class UtilitiesTests {
        string biblePath;

        [SetUp]
        public void Setup()
        {
            biblePath = Utilities.GetGutenbergBible();
        }

        [Test]
        public void IsEqualTest()
        {
            var buf1 = new byte[] {
                101, 101, 101, 101, 101, 101, 101, 101, 101, 101, 101, 101, 101, 101, 101, 101
            };
            var buf2 = new byte[buf1.Length];
            Array.Copy(buf1, buf2, buf1.Length);
            Assert.AreEqual(true, Utilities.UnsafeIsEqual(buf1, buf2));
            for (var i = 0; i < buf1.Length; i++) {
                buf2[i] = 102;
                Console.Write($"{i} ");
                Assert.AreEqual(false, Utilities.UnsafeIsEqual(buf1, buf2));
                Assert.AreEqual(false, Utilities.UnsafeIsEqual(buf2, buf1));
                buf2[i] = 101;
            }
        }

        [Test]
        public void IsEqualPerformance()
        {
            const int iterations = 100;
            Stopwatch stopwatch = new();
            var buf1 = new byte[512 * 1024];
            var buf2 = new byte[buf1.Length];
            Array.Copy(buf1, buf2, buf1.Length);
            stopwatch.Start();
            for (var i=0; i<iterations; i++) {
                IsEqual(buf1, buf2);
            }
            Console.WriteLine($"IsEqual: {stopwatch.Elapsed.TotalSeconds}");
            stopwatch.Reset();
            stopwatch.Start();
            for (var i = 0; i < iterations; i++) {
                IsEqual1(buf1, buf2);
            }
            Console.WriteLine($"IsEqual1: {stopwatch.Elapsed.TotalSeconds}");
            stopwatch.Reset();
            stopwatch.Start();
            for (var i = 0; i < iterations; i++) {
                Utilities.UnsafeIsEqual(buf1, buf2);
            }
            Console.WriteLine($"UnsafeIsEqual: {stopwatch.Elapsed.TotalSeconds}");
            stopwatch.Reset();
            stopwatch.Start();
            for (var i = 0; i < iterations; i++) {
                UnsafeIsEqual1(buf1, buf2);
            }
            Console.WriteLine($"UnsafeIsEqual1: {stopwatch.Elapsed.TotalSeconds}");
            //Assert.AreEqual(false, true);
        }

        // For performance comparisions
        protected static bool IsEqual(byte[] value1, byte[] value2)
        {
            int length = value1.Length;
            if (length != value2.Length) {
                return false;
            }
            for (var i = 0; i < length; i += sizeof(long)) {

                if (BitConverter.ToInt64(value1, i) != BitConverter.ToInt64(value2, i)) {
                    return false;
                }
            }
            return true;
        }

        // For performance comparisions
        protected static bool IsEqual1(byte[] value1, byte[] value2)
        {
            int length = value1.Length;
            if (length != value2.Length) {
                return false;
            }
            for (var i = 0; i < length; i++) {
                if (value1[i] != value2[i]) {
                    return false;
                }
            }
            return true;
        }

        // For performance comparisions
        protected static unsafe bool UnsafeIsEqual1(byte[] value1, byte[] value2)
        {
            int length = value1.Length;
            if (length != value2.Length) {
                return false;
            }
            fixed (byte* p1 = value1, p2 = value2) {
                for (var i = 0; i < length; i++) {
                    if (p1[i] != p2[i]) {
                        return false;
                    }
                }
            }
            return true;
        }
    }
}
