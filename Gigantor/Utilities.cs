using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

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
            internal class ByteProgress {
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
                            p2[i << 1] = value[i];
                        }
                    }
                }
                return str;
            }

            internal static string GetEnwik9()
            {
                var zipPath = Path.Combine(Path.GetTempPath(), "enwik9.zip");
                var enwik9Path = Path.Combine(Path.GetTempPath(), "enwik9");
                if (!File.Exists(enwik9Path)) {
                    if (!File.Exists(zipPath)) {
                        Console.WriteLine($"downloading enwik9 to {zipPath}...");
                        Wget("https://archive.org/download/enwik9/enwik9.zip", zipPath).Wait();
                    }
                    ZipFile.ExtractToDirectory(zipPath, Path.GetTempPath());
                }
                return enwik9Path;
            }

            internal static string GetGutenbergBible()
            {
                var path = Path.Combine(Path.GetTempPath(), "Gigantor-bible");
                if (!File.Exists(path)) {
                    Console.WriteLine($"downloading Gutenburg bible to {path}...");
                    Wget("https://www.gutenberg.org/ebooks/10.txt.utf-8", path).Wait();
                }
                return path;
            }

            internal static string GetSimpleFile()
            {
                var path = Path.Combine(Path.GetTempPath(), "Gigantor-simple");
                if (!File.Exists(path)) {
                    using var fileStream = new FileStream(path, FileMode.Create);
                    var writer = new StreamWriter(fileStream);
                    foreach (var line in new List<string>() { "hello", "world", "", "", "foo" }) {
                        writer.WriteLine(line);
                    }
                    writer.Write("bar");
                }
                return path;
            }

            internal static string GetSimpleFile2()
            {
                var path = Path.Combine(Path.GetTempPath(), "Gigantor-simple2");
                if (!File.Exists(path)) {
                    using var fileStream = new FileStream(path, FileMode.Create);
                    var writer = new StreamWriter(fileStream);
                    foreach (var line in new List<string>() { "hello", "world", "", "", "foo" }) {
                        writer.WriteLine(line);
                    }
                    writer.Write("bat");
                }
                return path;
            }

            internal static async Task Wget(string url, string destinationPath)
            {
                using (HttpClient httpClient = new()) {
                    using (var stream = await httpClient.GetStreamAsync(url)) {
                        using (var fileStream = new FileStream(destinationPath, FileMode.CreateNew)) {
                            await stream.CopyToAsync(fileStream);
                        }
                    }
                }
            }
        }
    }
}