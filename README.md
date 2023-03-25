# Gigantor
Boosts regular expression performance, and adds support for using gigantic files and streams

It solves the following problems:
* file exceeds the size of memory
* CPUs are under-utilized
* main thread is unresponsive
* searching streams
* searching compressed data

The approach is to partition the data into chunks which are processed in parallel using a [System.Threading.ThreadPool](https://learn.microsoft.com/en-us/dotnet/api/system.threading.threadpool?view=net-7.0) of background threads.  Since the threads are in the background they do not cause the main thread to become unresponsive.  Since the chunks are reasonably sized it does not matter if the whole file can fit into memory.

## [RegexSearcher](https://github.com/imagibee/Gigantor/blob/main/Gigantor/RegexSearcher.cs)
`RegexSearcher` is the class that boosts regular expression performance for gigantic files or streams.  Search was [benchmarked](https://github.com/imagibee/Gigantor/blob/main/Docs/Benchmarks.md#search-vs-buffer-size) at about 2.7 Gigabyte/s which was roughly 4x faster than the single threaded baseline.  It depends on a [System.Text.RegularExpressions.Regex](https://learn.microsoft.com/en-us/dotnet/api/system.text.regularexpressions.regex?view=net-7.0) to do the searching of the partitions.  It uses an overlap to handle matches that fall on partition boundaries.  De-duping of the overlap regions is performed automatically at the end of the search so that the final results are free of duplicates.  Performance can be further enhanced by simultaneously searching multiple regular expressions or files for use cases that have these dimensions.

```csharp
// Create a regular expression to match urls
System.Text.RegularExpressions.Regex regex = new(
    @"/https?:\/\/(www\.)?[-a-zA-Z0-9@:%._\+~#=]{2,256}\.[a-z]{2,6}\b([-a-zA-Z0-9@:%_\+.~#()?&//=]*)/",
    RegexOptions.Compiled);

// Create the searcher
Imagibee.Gigantor.RegexSearcher searcher = new("myfile", regex, progress);

// Do the search
Imagibee.Gigantor.Background.StartAndWait(
    searcher,
    progress,
    (_) => { Console.Write("."); },
    1000);

// Do something with the matches
foreach (var match in searcher.GetMatchData()) {
    ...
}

```

## [LineIndexer](https://github.com/imagibee/Gigantor/blob/main/Gigantor/LineIndexer.cs)
`LineIndexer` is the class that creates a mapping between line numbers and file positions for gigantic files.  Once the mapping has been created it can be used to quickly find the position at the start of a line or the line number that contains a position.  Index creation was [benchmarked](https://github.com/imagibee/Gigantor/blob/main/Docs/Benchmarks.md#indexing-vs-buffer-size) at about 2.5 Gigabyte/s which was roughly 4x faster than the single threaded baseline.

```csharp
// Create the indexer
LineIndexer indexer = new("myfile", progress);

// Do the indexing
Imagibee.Gigantor.Background.StartAndWait(
indexer,
    progress,
    (_) => { Console.Write("."); },
    1000);

// Use indexer to print the middle line
using System.IO.FileStream fs = new("myfile", FileMode.Open);
Imagibee.Gigantor.StreamReader reader = new(fs);
fs(indexer.PositionFromLine(indexer.LineCount / 2), SeekOrigin.Begin);
Console.WriteLine(reader.ReadLine());

```

## Input Data
The input data can either be uncompressed files, or streams.  Files should be used when possible because they were [benchmarked](https://github.com/imagibee/Gigantor/blob/main/Docs/Benchmarks.md#search-vs-buffer-size) to be faster than streams.  However, one notable use case for streams is searching compressed data without decompressing it to disk first.

## [Examples](https://github.com/imagibee/Gigantor/blob/main/Docs/Examples.md)

## [Benchmarks](https://github.com/imagibee/Gigantor/blob/main/Docs/Benchmarks.md)

## Testing
Prior to running the tests run `Scripts/setup` to prepare the test files.  This script creates some large files in the temporary folder which are deleted on reboot.  Once setup has been completed run `Scripts/test`.

## License
[MIT](https://raw.githubusercontent.com/imagibee/Gigantor/main/LICENSE)

## Versioning
This package uses [semantic versioning](https://en.wikipedia.org/wiki/Software_versioning#Semantic_versioning).  Tags on the main branch indicate versions.  It is recomended to use a tagged version.  The latest version on the main branch should be considered _under development_ when it is not tagged.

## Issues
Report and track issues [here](https://github.com/imagibee/Gigantor/issues).

## Contributing
Minor changes such as bug fixes are welcome.  Simply make a [pull request](https://opensource.com/article/19/7/create-pull-request-github).  Please discuss more significant changes prior to making the pull request by opening a new issue that describes the change.
