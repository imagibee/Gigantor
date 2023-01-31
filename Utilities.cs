using System;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Imagibee {
    namespace Gigantor {
        public class Utilities
        {
            // Efficiently wait until background process completes
            //
            // processes - a set of started indexers to wait for
            // OnProgressOrTimeout - called each time LineCount is updated, or
            // at frequency determined by the timeout parameter
            // timeoutMilliSeconds - the time in milliseconds between callbacks
            static public void Wait(
                IBackground process,
                AutoResetEvent progress,
                Action<int> OnProgressOrTimeout = null,
                int timeoutMilliSeconds = 1000)
            {
                Wait(
                    new List<IBackground>() { process },
                    progress,
                    OnProgressOrTimeout,
                    timeoutMilliSeconds);
            }


            // Efficiently wait for multiple background processes to all complete
            //
            // processes - a set of started indexers to wait for
            // OnProgressOrTimeout - called each time LineCount is updated, or at
            // frequency determined by the timeout parameter
            // timeoutMilliSeconds - the time in milliseconds between callbacks
            public static void Wait(
                IEnumerable<IBackground> processes,
                AutoResetEvent progress,
                Action<int> OnProgressOrTimeout = null,
                int timeoutMilliSeconds = 1000)
            {
                while (true) {
                    var runningCount = 0;
                    foreach (var mr in processes) {
                        if (mr.Running) {
                            runningCount++;
                        }
                    }
                    if (runningCount == 0) {
                        break;
                    }
                    progress.WaitOne(timeoutMilliSeconds);
                    if (OnProgressOrTimeout != null) {
                        OnProgressOrTimeout(runningCount);
                    }
                }
            }

            internal static string RemoveUsername(string path)
            {
                // mac format
                return Regex.Replace(path, @"/Users/([^/]*/)", "~/");
                // TODO: add more formats
            }

            internal static long FileByteCount(IEnumerable<string> paths)
            {
                long byteCount = 0;
                foreach (var path in paths)
                {
                    byteCount += FileByteCount(path);
                }
                return byteCount;
            }

            internal static long FileByteCount(string path)
            {
                FileInfo fileInfo = new(path);
                return fileInfo.Length;
            }

            internal static bool UnsafeIsEqual(byte[] value1, byte[] value2)
            {
                if (IntPtr.Size == 4) {
                    return UnsafeIsEqual32(value1, value2);
                }
                else if (IntPtr.Size == 8) {
                    return UnsafeIsEqual64(value1, value2);
                }
                else {
                    throw new NotImplementedException();
                }
            }

            internal static unsafe bool UnsafeIsEqual32(byte[] value1, byte[] value2)
            {
                var length = value1.Length;
                if (length != value2.Length) {
                    return false;
                }
                fixed (byte* p1 = value1, p2 = value2) {
                    int* lp1 = (int*)p1;
                    int* lp2 = (int*)p2;
                    for (var i = 0; i < length / sizeof(int); i += 1) {
                        if (lp1[i] != lp2[i]) {
                            return false;
                        }
                    }
                }
                return true;
            }

            internal static unsafe bool UnsafeIsEqual64(byte[] value1, byte[] value2)
            {
                var length = value1.Length;
                if (length != value2.Length) {
                    return false;
                }
                fixed (byte* p1 = value1, p2 = value2) {
                    long* lp1 = (long*)p1;
                    long* lp2 = (long*)p2;
                    for (var i = 0; i < length / sizeof(long); i++) {
                        if (lp1[i] != lp2[i]) {
                            return false;
                        }
                    }
                }
                return true;
            }
        }
    }
}