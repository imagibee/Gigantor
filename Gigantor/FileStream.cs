using System.IO;

namespace Imagibee {
    namespace Gigantor {

        // Creates a file stream
        //
        // path - path to the file
        // bufferSize - buffer size in bytes, for optimal results this should match
        // the partitionSize parameter used by the Gigantor class consuming this stream
        // fileMode - defaults to System.IO.FileMode.Open
        // fileAccess - defaults to System.IO.FileAccess.Read
        // fileShare - defaults to System.IO.FileShare.Read
        // fileOptions - defaults to System.IO.FileOptions.None
        public class FileStream {
            public static System.IO.FileStream Create(
                string path,
                int bufferSize,
                FileMode fileMode = FileMode.Open,
                FileAccess fileAccess = FileAccess.Read,
                FileShare fileShare = FileShare.Read,
                FileOptions fileOptions = FileOptions.None)
            {
                return new System.IO.FileStream(
                    path,
                    fileMode,
                    fileAccess,
                    fileShare,
                    bufferSize,
                    fileOptions);
            }
        }
    }
}
