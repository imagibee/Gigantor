using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Mono.Unix.Native;
using System.Runtime.InteropServices;
using System.IO.Pipes;
using System.Text;

namespace Imagibee {
    namespace Gigantor {
        // Internal common code
        public class Utilities {
            internal static string RemoveUsername(string path)
            {
                // mac format
                return Regex.Replace(path, @"/Users/([^/]*/)", "~/");
                // TODO: add more formats
            }

            internal static long FileByteCount(IEnumerable<string> paths)
            {
                long byteCount = 0;
                foreach (var path in paths) {
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
                return UnsafeIsEqual8(value1, value2);
                //if (IntPtr.Size == 4) {
                //    return UnsafeIsEqual32(value1, value2);
                //}
                //else if (IntPtr.Size == 8) {
                //    return UnsafeIsEqual64(value1, value2);
                //}
                //else {
                //    throw new NotImplementedException();
                //}
            }

            internal static unsafe bool UnsafeIsEqual8(byte[] value1, byte[] value2)
            {
                var length = value1.Length;
                if (length != value2.Length) {
                    return false;
                }
                fixed (byte* p1 = value1, p2 = value2) {
                    byte* lp1 = (byte*)p1;
                    byte* lp2 = (byte*)p2;
                    for (var i = 0; i < length / sizeof(byte); i += 1) {
                        if (lp1[i] != lp2[i]) {
                            return false;
                        }
                    }
                }
                return true;
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

            //internal static string ByteToString(byte[]? value, Encoding? encoding)
            //{
            //    if (value == null) {
            //        return "";
            //    }
            //    return (encoding ?? Encoding.UTF8).GetString(value);
            //}

            internal static unsafe string UnsafeByteToString(byte[]? value)
            {
                if (value == null) {
                    return "";
                }
                var length = value.Length;
                string str = new('\0', length);
                unsafe {
                    fixed (char* p1 = str) {
                        byte* p2 = (byte*)p1;
                        for (var i = 0; i < length / sizeof(byte); i++) {
                            p2[i << 1] = value[i];
                        }
                    }
                }
                return str;
            }

            internal static string GetBenchmarkPath()
            {
                var path = Environment.GetEnvironmentVariable("GIGANTOR_BENCHMARK_PATH");
                if (path == null) {
                    path = Path.Combine(Path.GetTempPath(), "gigantor");
                }
                return path;
            }

            internal static void ThrowBenchmarkSetupException()
            {
                throw new ApplicationException("must run Scripts/setup first");
            }

            internal static string GetEnwik9Gz()
            {
                var enwik9Path = GetEnwik9();
                var gzPath = $"{enwik9Path}.gz";
                if (!File.Exists(gzPath)) {
                    ThrowBenchmarkSetupException();
                }
                return gzPath;
            }

            internal static string GetEnwik9()
            {
                var enwik9Path = Path.Combine(GetBenchmarkPath(), "enwik9");
                if (!File.Exists(enwik9Path)) {
                    ThrowBenchmarkSetupException();
                }
                return enwik9Path;
            }

            internal static string GetGutenbergBible()
            {
                var path = Path.Combine(GetBenchmarkPath(), "10.txt.utf-8");
                if (!File.Exists(path)) {
                    ThrowBenchmarkSetupException();
                }
                return path;
            }

            internal static string GetSimpleFile()
            {
                var path = Path.Combine(GetBenchmarkPath(), "simple");
                if (!File.Exists(path)) {
                    using var fileStream = new System.IO.FileStream(path, FileMode.Create);
                    var writer = new StreamWriter(fileStream);
                    foreach (var line in new List<string>() { "hello", "world", "", "", "foo", "bar" }) {
                        writer.WriteLine(line);
                    }
                    writer.Close();
                }
                return path;
            }

            internal static string GetSimpleFile2()
            {
                var path = Path.Combine(GetBenchmarkPath(), "simple2");
                if (!File.Exists(path)) {
                    using var fileStream = new System.IO.FileStream(path, FileMode.Create);
                    var writer = new StreamWriter(fileStream);
                    foreach (var line in new List<string>() { "hello", "world", "", "", "foo", "bat"}) {
                        writer.WriteLine(line);
                    }
                    writer.Close();
                }
                return path;
            }

            internal static int ReadChunk(Stream stream, byte[] buf, int offset, int chunkSize)
            {
                var count = 0;
                var bytesRead = 0;
                do {
                    bytesRead = stream.Read(buf, count + offset, chunkSize - count);
                    count += bytesRead;
                }
                while (count < chunkSize && bytesRead > 0);
                return count;
            }

            // https://stackoverflow.com/questions/3825390/effective-way-to-find-any-files-encoding
            internal static bool TryGetEncoding(byte[] bom, ref Encoding encoding)
            {
                if (bom.Length < 4) {
                    return false;
                }
                if (bom[0] == 0x2b && bom[1] == 0x2f && bom[2] == 0x76) {
#pragma warning disable SYSLIB0001
                    encoding = Encoding.UTF7;
#pragma warning restore SYSLIB0001
                    return true;
                }
                if (bom[0] == 0xef && bom[1] == 0xbb && bom[2] == 0xbf) {
                    encoding = Encoding.UTF8;
                    return true;
                }
                if (bom[0] == 0xff && bom[1] == 0xfe && bom[2] == 0 && bom[3] == 0) {
                    encoding = Encoding.UTF32;
                    return true;
                }
                if (bom[0] == 0xff && bom[1] == 0xfe) {
                    encoding = Encoding.Unicode;
                    return true;
                }
                if (bom[0] == 0xfe && bom[1] == 0xff) {
                    encoding = Encoding.BigEndianUnicode;
                    return true;
                }
                if (bom[0] == 0 && bom[1] == 0 && bom[2] == 0xfe && bom[3] == 0xff) {
                    encoding = new UTF32Encoding(true, true);
                    return true;
                }
                return false;
            }
        }
    }
}