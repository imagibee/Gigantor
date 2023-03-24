using System.IO;
using Mono.Unix.Native;
using System.Runtime.InteropServices;

namespace Imagibee {
    namespace Gigantor {

        // Define the file buffering mode
        public enum BufferMode {
            Buffered,
            Unbuffered
        };

        // Creates unbuffered or buffered file stream
        //
        // Unbuffered is generally faster for gigantic files, but buffered may be faster if
        // the files are smaller and if the use case causes them to already be cached in memory.
        //
        // path - path to the file
        // bufferSize - buffer size in bytes, for optimal results this should match
        // the partitionSize parameter used by the Gigantor class consuming this stream
        // fileMode - defaults to System.IO.FileMode.Open
        // fileAccess - defaults to System.IO.FileAccess.Read
        // fileShare - defaults to System.IO.FileShare.Read
        // fileOptions - defaults to System.IO.FileOptions.None
        // bufferMode - choose whether or not files are buffered, defaults to buffered,
        // unbuffered is experimental
        //
        // Thanks to http://saplin.blogspot.com/2018/07/non-cachedunbuffered-file-operations.html
        public class FileStream {
            public static System.IO.FileStream Create(
                string path,
                int bufferSize,
                BufferMode bufferMode = BufferMode.Unbuffered,
                FileMode fileMode = FileMode.Open,
                FileAccess fileAccess = FileAccess.Read,
                FileShare fileShare = FileShare.Read,
                FileOptions fileOptions = FileOptions.None                )
            {
                if (bufferMode == BufferMode.Buffered) {
                    return new System.IO.FileStream(
                        path,
                        fileMode,
                        fileAccess,
                        fileShare,
                        bufferSize,
                        fileOptions);
                }
                else if (RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)) {
                    return new System.IO.FileStream(
                        path,
                        fileMode,
                        fileAccess,
                        fileShare,
                        bufferSize,
                        fileOptions | (FileOptions) 0x20000000);
                }
                return new PosixUnbufferedFileStream(
                    path,
                    fileMode,
                    fileAccess,
                    fileShare,
                    bufferSize,
                    fileOptions);
            }

            internal class PosixUnbufferedFileStream : System.IO.FileStream {
                public PosixUnbufferedFileStream(
                    string path,
                    FileMode mode,
                    FileAccess access,
                    FileShare share,
                    int bufferSize,
                    FileOptions options
                    ) : base(
                        path,
                        mode,
                        access,
                        share,
                        bufferSize,
                        options)
                {
                    Syscall.fcntl(
                      (int)SafeFileHandle.DangerousGetHandle(),
                      FcntlCommand.F_NOCACHE);
                }
            }
        }
    }
}
