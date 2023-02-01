namespace Imagibee {
    namespace Gigantor {

        public interface IBackground {
            // True if the background process is running
            bool Running { get; }

            // The quantity of bytes that have been completed
            long ByteCount { get; }

            // The error that caused the background process to end prematurely (if any)
            string Error { get; }

            // Start the background process
            void Start();
        }
    }
}