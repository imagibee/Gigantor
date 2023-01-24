using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Imagibee {
    namespace Gigantor {
        public class Utilities
        {
            // Avoid showing username for privacy concerns
            public static string RemoveUsername(string path)
            {
                // mac format
                return Regex.Replace(path, @"/Users/([^/]*/)", "~/");
                // TODO: add more formats
            }

            public static long ByteCount(IEnumerable<string> paths)
            {
                long byteCount = 0;
                foreach (var path in paths)
                {
                    byteCount += ByteCount(path);
                }
                return byteCount;
            }

            public static long ByteCount(string path)
            {
                FileInfo fileInfo = new(path);
                return fileInfo.Length;
            }
        }
    }
}