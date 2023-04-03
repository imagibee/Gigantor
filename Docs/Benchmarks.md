# Gigantor Benchmarks

One of the primary goals of Gigantor is performance boost.  Performance is benchmarked by processing a 32 GByte prepared text file.  Each benchmark is timed, and throughput is computed by dividing the number of bytes processed by the time.

The source code for the benchmarks is in [BenchmarkTests.cs](https://github.com/imagibee/Gigantor/blob/main/Benchmarking/Tests/BenchmarkTests.cs)

Prior to running the benchmarks run `Scripts/setup` to prepare the test files.  This script creates some large files in the temporary folder which are deleted on reboot.  Once setup has been completed run `Scripts/benchmark`.

## Read Throughput Baseline
This baseline establishes how fast a single-thread can simply read through the test file and throw away the results.  After some experimentation a buffer size of 128 MiByte was determined to be optimal for the baseline and resulted in a throughput of 3370 MBytes/s.  

Here is the code for the baseline:
```csharp
void ReadAndThrowAway(System.IO.FileStream stream, byte[] buf)
{
    int bytesRead;
    do {
        bytesRead = stream.Read(buf, 0, buf.Length);
    }
    while (bytesRead == buf.Length);
}
```

Gigantor's [`Partitioner`](https://github.com/imagibee/Gigantor/blob/main/Gigantor/Partitioner.cs) class can be configured like the baseline by setting `maxWorkers` to 1 and `partitionSize` to 128 MiBytes.  When this is done with a do-nothing implementation that just reads the file but doesn't do any processing it becomes a good way to compare `Partitioner`'s single-threaded read efficiency with the baseline code shown above.  The following graph shows this comparison.

![Image](https://raw.githubusercontent.com/imagibee/Gigantor/main/Docs/Read-Baseline.png)

The differences between the performance of the `Partitioner` and the baseline are negligible for file mode which demonstrates that the `Partitioner` is efficient at the part of performance that depends on reading files.  The efficiency for `Partitioner`'s stream mode is less efficient because of additional overhead needed by that mode.

Now that we have established that the `Partitioner` is efficient in single-threaded mode we can compare the `Partitioner`'s single and multiple threaded modes to see the impact of parallelization on do-nothing reads.

The next graph shows the results of changing `maxWorkers` from 1 to  1000 and re-running the do-nothing `Partitioner`.  This test is repeated for varying `partitionSize`.  The performance gain for the multi-threaded mode vs the single-threaded baseline is negligible.  Both tests max out around 3.4 GB/s.  Using AmorphousDiskMark's sequential read test on this SSD showed a similar result of 3.3 GB/s.  These results are all consistent with the AP1024N SSD used for this test which uses PCIe 3.0 x 4 lanes and has a maximum theoretical read throughput of about 3.9 GB/s.  So the best performance we can achieve on the test system is about 3.4 GB/s due to IO constraints.

![Image](https://raw.githubusercontent.com/imagibee/Gigantor/main/Docs/FileRead-vs-Buffer.png)

Below is the same test as above but using stream mode instead of file mode.  In multi-threaded stream mode the do-nothing `Partitioner` peaks at 2206 MBytes/s between 4096 and 8192 KiBytes.  This result is 2x better than the stream mode single-threaded baseline.

![Image](https://raw.githubusercontent.com/imagibee/Gigantor/main/Docs/StreamRead-vs-Buffer.png)

This concludes the do-nothing read throughput comparisons.  The main purpose of this section was to get an idea of how efficiently Gigantor is reading the input by comparing it to the simplest possible do-nothing code, and to establish an upper limit on the throughput that the do-something classes can aspire to achieve.  The remainder of the document will focus on benchmarking the actual `RegexSearcher` and `LineIndexer` classes.

## Search vs Buffer Size
For all the search benchmarks the following pattern is used to find all URLs in the 32 GByte test file.

```csharp
var pattern = @"/https?:\/\/(www\.)?[-a-zA-Z0-9@:%._\+~#=]{2,256}\.[a-z]{2,6}\b([-a-zA-Z0-9@:%_\+.~#()?&//=]*)/"; 
```
The following graph compares `RegexSearcher` throughput in various modes.  The peak using multiple threads is 2708 MBytes/s with a 256 KiByte partition size which is about 4x faster than the 614 MBytes/s peak for single-threaded using a 128 MiByte partition size.

![Image](https://raw.githubusercontent.com/imagibee/Gigantor/main/Docs/Search-vs-Buffer.png)

## Indexing vs Buffer Size
The following graph compares `LineIndexer` throughput in various modes.  The peak using multiple threads is 2489 MBytes/s with a 8192 KiByte partition size which is about 4x faster than the 680 MBytes/s peak for single-threaded using a 128 MiByte partition size.

![Image](https://raw.githubusercontent.com/imagibee/Gigantor/main/Docs/Indexing-vs-Buffer.png)

## Compressed Search vs Buffer Size
The following graph compares `RegexSearcher` using a compressed stream as input.  The peak using mutlipe threads is 288 MBytes/s with a 8192 KiByte partition size which is about 4% faster than the 278 MBytes/s peak for single-threaded using a 128 MiByte partition size.

![Image](https://raw.githubusercontent.com/imagibee/Gigantor/main/Docs/CompressedSearch-vs-Buffer.png)

## Search vs Number of Regex
The following graph compares `RegexSearcher` using from 1 to 10 regular expressions per partition pass.  A 10x increase in the number of searches yields about a 7x performance increase.

![Image](https://raw.githubusercontent.com/imagibee/Gigantor/main/Docs/Search-vs-NumRegex.png)

## Compressed Search vs Number of Files
The following graph compares `RegexSearcher` using from 1 to 5 compressed files in parallel.  A 5x increase in the number of files yields about a 3x performance increase.

![Image](https://raw.githubusercontent.com/imagibee/Gigantor/main/Docs/Compressed-vs-NumFiles.png)


## Use Case Tuning
Gigantor provides several strategies and tuning parameters for boosting performance.  The defaults should provide some performance gain out-of-the box, but tuning can help find more optimal settings.  Hopefully, these benchmarks have provided some insight into choosing a good starting point for these parameters.  I can also make the following suggestions about tuning.

1. Target net7.0 if possible because of [regular expression improvements released with .NET 7](https://devblogs.microsoft.com/dotnet/regular-expression-improvements-in-dotnet-7/).
1. Use files mode instead of stream mode when possible because it is faster.
1. Try various values of `partitionSize`.  For these benchmarks this was the most influential parameter.
1. Try various dotnet garbage collection modes.  These benchmarks were run using DOTNET_gcServer=1 and DOTNET_gcConcurrent=1.
If your not sure I recommend buffered because it is standard.
1. Probably just leave `maxWorkers` alone unless you have a reason to change it.  The default is 0 which places no limits on the number of threads used.  In these benchmarks I used a value of 1000 and found that furhter limiting the threads did not improve performance.

## Test System
The benchmarks were run on a 2019 16-inch Macbook Pro running macOS Ventura 13.2.1 with the following hardware:
- 8-Core Intel Core i9
- L2 Cache (per Core):	256 KB
- L3 Cache:	16 MB
- Memory:	16 GB
- Apple SSD AP1024N
