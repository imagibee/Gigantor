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

        // Creates an unbuffered file stream
        // See http://saplin.blogspot.com/2018/07/non-cachedunbuffered-file-operations.html
        public class FileStream {
            public static System.IO.FileStream Create(
                string path,
                FileMode fileMode = FileMode.Open,
                FileAccess fileAccess = FileAccess.Read,
                FileShare fileShare = FileShare.Read,
                int bufferSize = 512 * 1024,
                FileOptions fileOptions = FileOptions.None,
                BufferMode bufferMode = BufferMode.Unbuffered)
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
