using System.IO;

namespace Imagibee {
    namespace Gigantor {
        public class StreamReader
        {
            public StreamReader(Stream stream, System.Text.Encoding encoding, bool leaveOpen=true)
            {
                reader = new BinaryReader(stream, encoding, leaveOpen);
            }
            public StreamReader(Stream stream, bool leaveOpen=true)
            {
                reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen);
            }

            public string ReadLine()
            {
                char c;
                var value = "";
                var startPosition = reader.BaseStream.Position;
                while ((c = reader.ReadChar()) != '\n') {
                    if (c != '\r') {
                        value += c;
                        Logger.Log($"{(byte)c}");
                    }
                }
                // strip byte order mark
                if (startPosition == 0) {
                    value = value.Trim(new char[] { '\uFEFF' });
                }
                return value;
            }

            // private data
            readonly BinaryReader reader;
        }
    }

}