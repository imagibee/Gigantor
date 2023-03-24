using System.Threading;
using System.IO;

namespace Imagibee {
    namespace Gigantor {
        //
        // Supports duplicate testing for very large files
        //
        // Users should begin the process by calling Start.  All public
        // methods and properties are well-behaved while the background
        // process is running.
        //
        // After the process is finished, the results are kept until Start
        // is called again.  However, calls to Start while Running is true
        // are ignored.
        //
        // Exceptions during the background processing are caught and
        // stored in Error.  Exceptions during Start are not handled.
        //
        // A balance between memory footprint and performance can be achieved
        // by varying chunkKiBytes and maxWorkers parameters.
        //
        public class DuplicateChecker : Partitioner<PartitionData> {
            // True if the files are identical, otherwise false
            public bool Identical {
                get {
                    return Interlocked.Read(ref mismatches) == 0;
                }
                private set {
                    if (value == false) {
                        Interlocked.Exchange(ref mismatches, 1);
                    }
                    else {
                        Interlocked.Exchange(ref mismatches, 0);
                    }
                }
            }

            // Create a new instance
            //
            // path1, path2 - the paths of the files to compare
            // progress - signaled each time ByteCount is updated
            // chunkKiBytes - the chunk size in KiBytes that each worker works on
            // maxWorkers - optional limit to the maximum number of simultaneous workers
            public DuplicateChecker(
                string path1,
                string path2,
                AutoResetEvent progress,
                int chunkKiBytes = 1024,
                int maxWorkers = 0) : base(
                    path1,
                    progress,
                    JoinMode.None,
                    chunkKiBytes,
                    maxWorkers)
            {
                this.path2 = path2;
                byteCount = 0;
                mismatches = 1;
            }

            public override void Start()
            {
                if (!Running) {
                    mismatches = 0;
                    FileInfo fileInfo1 = new(Path);
                    FileInfo fileInfo2 = new(path2);
                    // save some time if file lengths don't match
                    if (fileInfo1.Length == fileInfo2.Length) {
                        //Logger.Log($"***full compare");
                        base.Start();
                    }
                    else {
                        Identical = false;
                        //Logger.Log($"***different size");
                    }
                }
            }

            protected override PartitionData Join(PartitionData a, PartitionData b)
            {
                return a;
            }

            protected override PartitionData Map(PartitionerData data)
            {
                PartitionData result = new(){};
                using var fileStream1 = new System.IO.FileStream(
                    Path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    chunkSize,
                    FileOptions.Asynchronous);
                fileStream1.Seek(data.StartFpos, SeekOrigin.Begin);
                var streamReader1 = new BinaryReader(fileStream1, System.Text.Encoding.UTF8, true);
                var buf1 = streamReader1.ReadBytes(chunkSize);
                using var fileStream2 = new System.IO.FileStream(
                    path2,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    chunkSize,
                    FileOptions.Asynchronous);
                fileStream2.Seek(data.StartFpos, SeekOrigin.Begin);
                var streamReader2 = new BinaryReader(fileStream2, System.Text.Encoding.UTF8, true);
                var buf2 = streamReader2.ReadBytes(chunkSize);
                if (!Utilities.UnsafeIsEqual(buf1, buf2)) {
                    //Logger.Log($"{data.Id}");
                    if (Identical) {
                        Identical = false;
                    }
                }
                Interlocked.Add(ref byteCount, buf1.Length);
                return result;
            }
            
            // private data
            readonly string path2;
            long mismatches;
        }
    }
}