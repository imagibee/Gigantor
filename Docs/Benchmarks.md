# Gigantor Benchmarks

One of the primary goals of Gigantor is performance boost.  Performance is benchmarked by processing a 32 GB prepared text file.  Each benchmark is timed, and throughput is computed by dividing the number of bytes processed by the time.

For the search benchmarks the following pattern is used to find all URLs in the file.

```csharp
var pattern = @"/https?:\/\/(www\.)?[-a-zA-Z0-9@:%._\+~#=]{2,256}\.[a-z]{2,6}\b([-a-zA-Z0-9@:%_\+.~#()?&//=]*)/"; 
```
The source code for the benchmarks is in [BenchmarkTests.cs](https://github.com/imagibee/Gigantor/blob/main/Benchmarking/Tests/BenchmarkTests.cs)

Prior to running the benchmarks run `Scripts/setup` to prepare the test files.  This script creates some large files in the temporary folder which are deleted on reboot.  Once setup has been completed run `Scripts/benchmark`.


## Read Throughput Baseline
This baseline establishes how fast a single thread can simply read through the test file and throw away the results.  After some experimentation a buffer size of 128 MiByte was determined to be optimal for the baseline and resulted in a throughput of 3271 MBytes/s.  

Here is the code for the baseline:
```csharp
var buf = new byte[128 * 1024 * 1024];
var bytesRead = 0;
do {
    bytesRead = stream.Read(buf, 0, buf.Length);
}
while (bytesRead == buf.Length);
```

Gigantor's [`Partitioner`](https://github.com/imagibee/Gigantor/blob/main/Gigantor/Partitioner.cs) class can be run in single threaded mode by setting `maxWorkers` to 1.  When this is done with a do-nothing implementation that just reads the file but doesn't do any processing it becomes a good way to compare `Partitioner`'s single threaded read efficiency with the baseline code shown above.  The following graph shows this comparison.

![Image](https://raw.githubusercontent.com/imagibee/Gigantor/main/Docs/Read-Baseline.png)

For file mode the differences between the performance of the `Partitioner` and the baseline are negligible which demonstrates that the `Partitioner` is efficient at file IO.  The efficiency for stream mode is not as good.

Now that we have established a single threaded baseline we can introduce multiple threads and compare the performance.  The next graph shows the results of changing `maxWorkers` from 1 to  1000 and re-running the do-nothing `Partitioner`.  This test is repeated for varying `partitionSize` and peaks between 128 KiBytes and 1024 KiBytes at 5541 MBytes/s which is significantly faster than the single threaded baseline.  Since the read throughput increases with more workers I assume the IO was not saturated by the single threaded baseline test.

![Image](https://raw.githubusercontent.com/imagibee/Gigantor/main/Docs/FileRead-vs-Buffer.png)

Below is the same test as above but using stream mode instead of file mode.

![Image](https://raw.githubusercontent.com/imagibee/Gigantor/main/Docs/StreamRead-vs-Buffer.png)



## Search vs Buffer Size
The following graph compares `RegexSearcher` throughput in various modes.  The peak using multiple threads is 2704 MBytes/s which is about 4x faster than the 691 MBytes/s peak for single threaded.

![Image](https://raw.githubusercontent.com/imagibee/Gigantor/main/Docs/Search-vs-Buffer.png)

## Indexing vs Buffer Size
The following graph compares `LineIndexer` throughput in various modes.  The peak using multiple threads is 2463 MBytes/s which is about 4x faster than the 677 MBytes/s peak for single threaded.

![Image](https://raw.githubusercontent.com/imagibee/Gigantor/main/Docs/Indexing-vs-Buffer.png)

## Compressed Search vs Buffer Size
The following graph compares `RegexSearcher` using a compressed stream as input.  The peak using mutlipe threads is 319 MBytes/s which is about 15% faster than the 278 MBytes/s peak for single threaded.

![Image](https://raw.githubusercontent.com/imagibee/Gigantor/main/Docs/CompressedSearch-vs-Buffer.png)

## Search vs Number of Regex
The following graph demonstrates how search throughput changes when using multiple regular expressions per partition pass.

![Image](https://raw.githubusercontent.com/imagibee/Gigantor/main/Docs/Search-vs-NumRegex.png)

## Compressed Search vs Number of Files
The following graph demonstrates how search throughput changes when searching multiple files simultaneously.

![Image](https://raw.githubusercontent.com/imagibee/Gigantor/main/Docs/Compressed-vs-NumFiles.png)


## Use Case Tuning
Gigantor provides several strategies and tuning parameters for boosting performance.  The defaults should provide some performance gain out-of-the box, but tuning is generally necessary to find the optimal settings.  Hopefully, these benchmarks have provided some insight into choosing a good starting point for these parameters.  I can also make the following suggestions about tuning.

1. Target net7.0 if possible because of [regular expression improvements released with .NET 7](https://devblogs.microsoft.com/dotnet/regular-expression-improvements-in-dotnet-7/).
1. Use files mode instead of stream mode when possible because it is faster.
1. Try various values of `partitionSize`.  For these benchmarks this was the most influential parameter.
1. Try both `Buffered` and `Unbuffered` modes.  In some cases this made a noticable difference.  If your not sure I recommend buffered because it is standard.
1. Leave `maxWorkers` alone unless you have a reason to change it.  The default is 0 which places no limits on the number of threads used.  In these benchmarks I found that limiting the threads did not improve performance. 

## Test System
The benchmarks were run on a 2019 16-inch Macbook Pro running macOS Ventura 13.2.1 with the following hardware:
- 8-Core Intel Core i9
- L2 Cache (per Core):	256 KB
- L3 Cache:	16 MB
- Memory:	16 GB
- Apple SSD AP1024N
