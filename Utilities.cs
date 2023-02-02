using System;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Imagibee {
    namespace Gigantor {
        public class Utilities
        {
            // Start a background process and wait for it to complete
            //
            // process - the background process to start and wait for
            // progress - shared wait event to facilitate progress, initially false
            // OnProgressOrTimeout - called each time progress is updated, or at
            // frequency determined by the timeout parameter, callback argument
            // is a collection containing process
            // timeoutMilliSeconds - the time in milliseconds between callbacks
            public static void StartAndWait(
                IBackground process,
                AutoResetEvent progress,
                Action<IReadOnlyCollection<IBackground>> OnProgressOrTimeout = null,
                int timeoutMilliSeconds = 1000)
            {
                StartAndWait(
                    new List<IBackground>() { process },
                    progress,
                    OnProgressOrTimeout,
                    timeoutMilliSeconds);
            }

            // Start multiple background processes and wait for them all to complete
            //
            // processes - a collection of background processes to start and wait for
            // progress - shared wait event to facilitate progress, initially false
            // OnProgressOrTimeout - called each time progress is updated, or at
            // frequency determined by the timeout parameter, callback argument
            // is a collection containing processes
            // timeoutMilliSeconds - the time in milliseconds between callbacks
            public static void StartAndWait(
                ICollection<IBackground> processes,
                AutoResetEvent progress,
                Action<IReadOnlyCollection<IBackground>> OnProgressOrTimeout = null,
                int timeoutMilliSeconds = 1000)
            {
                foreach (var process in processes) {
                    process.Start();
                }
                Wait(processes, progress, OnProgressOrTimeout, timeoutMilliSeconds);
            }

            // Efficiently wait until background process completes
            //
            // process - the background process to wait for
            // progress - shared wait event to facilitate progress, initially false
            // OnProgressOrTimeout - called each time progress is updated, or at
            // frequency determined by the timeout parameter, callback argument
            // is a collection containing process
            // timeoutMilliSeconds - the time in milliseconds between callbacks
            static public void Wait(
                IBackground process,
                AutoResetEvent progress,
                Action<IReadOnlyCollection<IBackground>> OnProgressOrTimeout = null,
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
            // processes - a collection of backround processes to wait for
            // progress - shared wait event to facilitate progress, initially false
            // OnProgressOrTimeout - called each time progress is updated, or at
            // frequency determined by the timeout parameter, callback argument
            // is a collection containing processes
            // timeoutMilliSeconds - the time in milliseconds between callbacks
            public static void Wait(
                ICollection<IBackground> processes,
                AutoResetEvent progress,
                Action<IReadOnlyCollection<IBackground>> OnProgressOrTimeout = null,
                int timeoutMilliSeconds = 1000)
            {
                while (true) {
                    var runningCount = 0;
                    foreach (var process in processes) {
                        if (process.Running) {
                            runningCount++;
                        }
                        if (process.Error.Length != 0) {
                            runningCount = 0;
                            break;
                        }
                    }
                    if (runningCount == 0) {
                        break;
                    }
                    progress.WaitOne(timeoutMilliSeconds);
                    OnProgressOrTimeout?.Invoke(
                        (IReadOnlyCollection<IBackground>)processes);
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
            // Progress bar class for examples
            public class ByteProgress {
                int maxLength;
                int lastLength;
                long totalBytes;

                public ByteProgress(int maxLength, long totalBytes)
                {
                    this.maxLength = maxLength;
                    this.totalBytes = totalBytes;
                }

                public void Update(long byteCount)
                {
                    var progressLength = (int)(maxLength * byteCount / totalBytes);
                    for (var i = 0; i < progressLength - lastLength; i++) {
                        Console.Write('#');
                    }
                    lastLength = progressLength;
                }
            }

            internal static unsafe string UnsafeByteToString(byte[] value)
            {
                var length = value.Length;
                string str = new('\0', length);
                unsafe {
                    fixed (char* p1 = str) {
                        byte* p2 = (byte*)p1;
                        for (var i = 0; i < length / sizeof(byte); i++) {
                            p2[i<<1] = value[i];
                        }
                    }
                }
                return str;
            }
        }
    }
}