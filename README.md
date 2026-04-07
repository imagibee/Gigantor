# Gigantor
Boosting performance of `System.Text.RegularExpressions.Regex`

It helps with the following problems:
* searching gigantic files that exceed RAM
* replacing matches with new text
* poor performance due to CPU under utilization
* main thread is unresponsive
* searching streams
* searching compressed data

The approach is to partition the data into chunks which are simultaneously processed on multiple threads.  Since processing takes place on worker threads, the main thread remains responsive.  Since the chunks are reasonably sized and the whole file does not need to fit into memory, files that exceed RAM can be processed.

## API
Here is a brief overview of the API.  For details refer to the [source code](https://github.com/imagibee/Gigantor/tree/main/Gigantor) or the [unit tests](https://github.com/imagibee/Gigantor/tree/main/Testing/Functional).

### [RegexSearcher](https://github.com/imagibee/Gigantor/blob/main/Gigantor/RegexSearcher.cs)
This class uses multi-threading to boost the performance of searches with [System.Text.RegularExpressions.Regex](https://learn.microsoft.com/en-us/dotnet/api/system.text.regularexpressions.regex?view=net-7.0) compared to to the single-threaded approach.  It also supports searching gigantic files that exceed RAM, and directly searching streams.  Roughly a 4x improvement was measured on the test system.

It uses an overlap to handle matches that fall on partition boundaries.  De-duping of the overlap regions is performed automatically at the end of the search so that the final results are free of duplicates.

```csharp
// Create a regular expression to match urls
System.Text.RegularExpressions.Regex regex = new(
    @"[\w]+://[^/\s?#]+[^\s?#]+(?:\?[^\s#]*)?(?:#[^\s]*)?",
    RegexOptions.Compiled);

// Create the searcher
Imagibee.Gigantor.RegexSearcher searcher = new("myfile", regex, progress);

// Do the search
Imagibee.Gigantor.Background.StartAndWait(searcher, progress, (_) => {});

foreach (var match in searcher.GetMatchData()) {
    // Do something with the matches
}

// Replace all the urls with stackoverflow.com in a new file
using System.IO.FileStream output = File.Create("myfile2");
searcher.Replace(output, (match) => { return "https://www.stackoverflow.com"; }); 
```

### [LineIndexer](https://github.com/imagibee/Gigantor/blob/main/Gigantor/LineIndexer.cs)
This class creates a mapping between line numbers and file positions for gigantic files.  Once the mapping has been created it can be used to quickly find positions at the start of a line or the line number that contains a position.

```csharp
// Create the indexer
LineIndexer indexer = new("myfile", progress);

// Do the indexing
Imagibee.Gigantor.Background.StartAndWait(indexer, progress, (_) => {});

// Use indexer to print the middle line
using System.IO.FileStream fs = new("myfile", FileMode.Open);
Imagibee.Gigantor.StreamReader reader = new(fs);
fs.Seek(indexer.PositionFromLine(indexer.LineCount / 2), SeekOrigin.Begin);
Console.WriteLine(reader.ReadLine());
```

## Input Data
The input data can either be uncompressed files, or streams.  Files offer better performance than streams, but streams allow searching compressed data without decompressing it to disk first.

## Examples
Here are some more detailed [examples](https://github.com/imagibee/Gigantor/blob/main/Docs/Examples.md).

## Benchmarks
Here is a document that discusses the [benchmarks](https://github.com/imagibee/Gigantor/blob/main/Docs/Benchmarks.md) in greater detail.

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
