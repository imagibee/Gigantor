using System.IO;

namespace Imagibee {
    namespace Gigantor {
        //
        // Helper for reading consecutive lines
        //
        // The System.IO.StreamReader has an issue that prevents changes
        // to the underlying Stream.Position between calls to ReadLine from
        // working properly.  The main purpose of this StreamReader is to
        // provide a workaround for that issue.
        //
        // To achieve this goal StreamReader is implemented as a BinaryReader
        // with some code to make ReadLine functional.
        //
        public class StreamReader
        {
            // User specifies the encoding
            public StreamReader(Stream stream, System.Text.Encoding encoding, bool leaveOpen=true)
            {
                reader = new BinaryReader(stream, encoding, leaveOpen);
            }

            // Default encoding is UTF8
            public StreamReader(Stream stream, bool leaveOpen=true)
            {
                reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen);
            }

            // Return the next line starting from the underlying Stream.Position
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
                // strip byte order mark (aka BOM)
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