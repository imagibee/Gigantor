using System;
using System.Threading;
using System.Collections.Generic;

namespace Imagibee {
    namespace Gigantor {

        public interface IBackground {
            // True if the background process is running
            bool Running { get; }

            // True if the background process was canceled
            bool Cancelled { get; }

            // The quantity of bytes that have been completed
            long ByteCount { get; }

            // The error that caused the background process to end prematurely (if any)
            string Error { get; }

            // Start the background process
            virtual void Start() { }

            // Cancel the background process
            virtual void Cancel() { }
        }

        //
        // Functions for working with collecitons of IBackground
        //
        public class Background
        {
            // Start a background process and wait for it to complete
            //
            // process - the background process to start and wait for
            // progress - shared wait event to facilitate progress, initially false
            // OnProgressOrTimeout - called each time progress is updated, or at
            // frequency determined by the timeout parameter, callback argument
            // is a collection containing process
            // timeoutMilliSeconds - the time in milliseconds between callbacks
            public static void StartAndWait(
                IBackground process,
                AutoResetEvent progress,
                Action<IReadOnlyCollection<IBackground>> OnProgressOrTimeout,
                int timeoutMilliSeconds = 1000)
            {
                StartAndWait(
                    new List<IBackground>() { process },
                    progress,
                    OnProgressOrTimeout,
                    timeoutMilliSeconds);
            }

            // Start multiple background processes and wait for them all to complete
            //
            // processes - a collection of background processes to start and wait for
            // progress - shared wait event to facilitate progress, initially false
            // OnProgressOrTimeout - called each time progress is updated, or at
            // frequency determined by the timeout parameter, callback argument
            // is a collection containing processes
            // timeoutMilliSeconds - the time in milliseconds between callbacks
            public static void StartAndWait(
                ICollection<IBackground> processes,
                AutoResetEvent progress,
                Action<IReadOnlyCollection<IBackground>> OnProgressOrTimeout,
                int timeoutMilliSeconds = 1000)
            {
                foreach (var process in processes) {
                    process.Start();
                }
                Wait(processes, progress, OnProgressOrTimeout, timeoutMilliSeconds);
            }

            // Efficiently wait until background process completes
            //
            // process - the background process to wait for
            // progress - shared wait event to facilitate progress, initially false
            // OnProgressOrTimeout - called each time progress is updated, or at
            // frequency determined by the timeout parameter, callback argument
            // is a collection containing process
            // timeoutMilliSeconds - the time in milliseconds between callbacks
            static public void Wait(
                IBackground process,
                AutoResetEvent progress,
                Action<IReadOnlyCollection<IBackground>> OnProgressOrTimeout,
                int timeoutMilliSeconds = 1000)
            {
                Wait(
                    new List<IBackground>() { process },
                    progress,
                    OnProgressOrTimeout,
                    timeoutMilliSeconds);
            }


            // Efficiently wait for multiple background processes to all complete
            //
            // processes - a collection of backround processes to wait for
            // progress - shared wait event to facilitate progress, initially false
            // OnProgressOrTimeout - called each time progress is updated, or at
            // frequency determined by the timeout parameter, callback argument
            // is a collection containing processes
            // timeoutMilliSeconds - the time in milliseconds between callbacks
            public static void Wait(
                ICollection<IBackground> processes,
                AutoResetEvent progress,
                Action<IReadOnlyCollection<IBackground>> OnProgressOrTimeout,
                int timeoutMilliSeconds = 1000)
            {
                while (true) {
                    var runningCount = 0;
                    progress.WaitOne(timeoutMilliSeconds);
                    foreach (var process in processes) {
                        if (process.Running) {
                            runningCount++;
                        }
                    }
                    if (runningCount == 0) {
                        break;
                    }
                    OnProgressOrTimeout?.Invoke(
                        (IReadOnlyCollection<IBackground>)processes);
                }
            }

            // Cancel multiple background processes
            public static void CancelAll(ICollection<IBackground> processes)
            {
                foreach (var process in processes) {
                    process.Cancel();
                }
            }

            // Return true if any process was canceled
            public static bool AnyCancelled(ICollection<IBackground> processes)
            {
                foreach (var process in processes) {
                    if (process.Cancelled) {
                        return true;
                    }
                }
                return false;
            }

            // Return Error or there are any or empty string if there are none
            public static string AnyError(ICollection<IBackground> processes)
            {
                foreach (var process in processes) {
                    if (process.Error.Length != 0) {
                        return process.Error;
                    }
                }
                return "";
            }
        }
    }
}