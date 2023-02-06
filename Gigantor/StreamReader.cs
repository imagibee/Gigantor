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
        // To achieve this goal StreamReader is derived from BinaryReader
        // with an added ReadLine method.
        //
        public class StreamReader : System.IO.BinaryReader
        {
            // User specifies the encoding
            public StreamReader(
                System.IO.Stream stream,
                System.Text.Encoding encoding,
                bool leaveOpen = true)
                : base(
                      stream,
                      encoding,
                      leaveOpen) { }

            // Default encoding is UTF8
            public StreamReader(
                System.IO.Stream stream,
                bool leaveOpen = true)
                : base(
                      stream,
                      System.Text.Encoding.UTF8,
                      leaveOpen) { }

            // Return the next line starting from the underlying BaseStream.Position
            public string ReadLine()
            {
                char c;
                var value = "";
                var startPosition = BaseStream.Position;
                try {
                    while ((c = ReadChar()) != '\n') {
                        if (c != '\r') {
                            value += c;
                            //Logger.Log($"{(byte)c}");
                        }
                    }
                }
                catch (System.IO.EndOfStreamException) { }
                // strip byte order mark (aka BOM)
                if (startPosition == 0) {
                    value = value.Trim(new char[] { '\uFEFF' });
                }
                return value;
            }
        }
    }

}