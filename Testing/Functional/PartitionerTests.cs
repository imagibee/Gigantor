using System;
using System.Threading;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Imagibee.Gigantor;

namespace Testing {
    public class PartitionerTests {
        string biblePath = "";

        // Test class for throwing exceptions in the code path
        public class PartitionerErrorThrower : Partitioner<PartitionData> {

            private static readonly Random rand = new Random();
            [ThreadStatic] private static Random? tRand;

            public int Next(int a, int b)
            {
                if (tRand == null) {
                    int seed;
                    lock (rand) {
                        seed = rand.Next();
                    }
                    tRand = new Random(seed);
                }

                return tRand.Next(a, b);
            }

            public PartitionerErrorThrower(
                bool throwInMap,
                int startId,
                string path,
                AutoResetEvent progress,
                JoinMode joinMode,
                int partitionSize,
                int maxWorkers) :
                base(path, progress, joinMode, partitionSize, maxWorkers)
            {
                this.throwInMap = throwInMap;
                this.startId = startId;
            }

            protected override PartitionData Map(PartitionerData data)
            {
                //Logger.Log($"{data.Id}");
                Thread.Sleep(Next(1, 5));
                if (throwInMap && data.Id > startId) {
                    throw new Exception($"map error {data.Id}");
                }
                return new PartitionData() { Id = data.Id };
            }

            protected override PartitionData Join(PartitionData a, PartitionData b)
            {
                Thread.Sleep(Next(1,5));
                if (!throwInMap && a.Id > startId) {
                    throw new Exception($"join error {a.Id}");
                }
                var r = b;
                return r;
            }

            readonly bool throwInMap;
            readonly int startId;
        }

        [SetUp]
        public void Setup()
        {
            biblePath = Utilities.GetGutenbergBible();
        }

        [Test]
        public void MapExceptionTest()
        {
            const int iterations = 2;
            AutoResetEvent progress = new(false);
            for (var i = 0; i < iterations; i++) {
                PartitionerErrorThrower thrower = new(
                    true,
                    1,
                    biblePath,
                    progress,
                    JoinMode.Sequential,
                    1024 * 1024,
                    10);
                Background.StartAndWait(
                    thrower,
                    progress,
                    (_) => { });
                Assert.AreNotEqual(0, thrower.Error.Length);
                var error = Background.AnyError(new List<IBackground>() { thrower });
                Assert.AreEqual(true, error.Contains("map"));
            }
        }

        [Test]
        public void JoinExceptionTest()
        {
            const int iterations = 2;
            AutoResetEvent progress = new(false);
            for (var i = 0; i < iterations; i++) {
                PartitionerErrorThrower thrower = new(
                    false,
                    2,
                    biblePath,
                    progress,
                    JoinMode.Sequential,
                    1024 * 1024,
                    10);
                Background.StartAndWait(
                    thrower,
                    progress,
                    (_) => { });
                var error = Background.AnyError(new List<IBackground>() { thrower });
                Assert.AreEqual(true, error.Contains("join"));
            }
        }
    }
}

