using System;
using System.IO;
using Mono.Unix.Native;
using System.Runtime.InteropServices;

namespace Benchmark
{
    internal class Utilities
    {
        // See http://saplin.blogspot.com/2018/07/non-cachedunbuffered-file-operations.html
        public static FileStream UnbufferedFileStream(
            string path,
            FileMode fileMode,
            FileAccess fileAccess,
            FileShare fileShare,
            int bufferSize,
            FileOptions fileOptions)
        {
            if (RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)) {
                throw new NotImplementedException();
            }
            return new PosixUncachedFileStream(
                path,
                fileMode,
                fileAccess,
                fileShare,
                bufferSize,
                fileOptions);
        }

        public class PosixUncachedFileStream : FileStream
        {
            public PosixUncachedFileStream(
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

