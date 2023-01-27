# Gigantor
A dotnet application library for working with very large files

## Contents
Gigantor includes C# classes that can be safely and effectively used with very large files.  These classes are designed to operate within a reasonable memory footprint and to thoroughly and efficiently utilize CPU and IO.

- LineIndexer - class for indexing text lines in the background
- StreamReader - class for reading consecutive lines
- RegexSearcher - class for efficient regex search in the background

## Usage

### LineIndexer
Here is a simple example that demonstrates using `LineIndexer`.  Refer to `LineApp` for a more detailed example that demonstrates efficiently using multiple `LineIndexer` simultaneously.

```cs
using Imagibee.Gigantor;

// A file to index
var path = "~/VeryLarge.txt";

// A wait event that signals the user whenever progress has been made
AutoResetEvent progress = new(false);

// Instantiate a indexer with a progress wait event
LineIndexer indexer = new(progress);

// Start indexing the requested path
indexer.Start(path);

// Wait for the indexer to complete (WaitOne is more efficient than Sleep)
while (indexer.Running) {
    progress.WaitOne(1000);
    Console.Write('.');
}
Console.Write('\n');

// At this point the index is done, partial results can be used prior to completion
if (indexer.LastError.Length == 0) {
    Console.WriteLine($"Found {indexer.LineCount} lines");
}

// Get the total number of lines
long totalLines = indexer.LineCount;

// Get the total number of bytes
long totalBytes = indexer.ByteCount;

// Get the file position at the start of the 1,000,000th line
long myFpos = indexer.PositionFromLine(1000000);

// Get the line that contains the file position value 747724
long myLine = indexer.LineFromPosition(747724);

// Read lines 1,000,000 and 1,000,001
using var fileStream = new FileStream(simplePath, FileMode.Open, FileAccess.Read, FileShare.Read);
Imagibee.Gigantor.StreamReader gigantorReader = new(fileStream);
fileStream.Seek(indexer.PositionFromLine(myFpos), SeekOrigin.Begin);
string myText = gigantorReader.ReadLine();
myText = gigantorReader.ReadLine();

```


### RegexSearcher
Here is a simple example that demonstrates using `RegexSearcher`.  Refer to `SearchApp` for a more detailed example that demonstrates efficiently using multiple `RegexSearcher` simultaneously.

```cs
using Imagibee.Gigantor;

// A file to search
var path = "~/VeryLarge.txt";

// The pattern to search for
var pattern = "Hello World!";

// A regular expression to search the file for
Regex regex = new(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

// A wait event that signals the user whenever progress has been made
AutoResetEvent progress = new(false);

// Instantiate a searcher with a progress wait event
RegexSearcher searcher = new(progress);

// Start searching the requested path, with our regex, and with the maximum
// expected length of a match 
searcher.Start(path, regex, pattern.Length);

// Wait for the searcher to complete (WaitOne is more efficient than Sleep)
while (searcher.Running) {
    progress.WaitOne(1000);
    Console.Write('.');
}
Console.Write('\n');

// At this point the search is done, partial results can be used prior to completion
if (searcher.LastError.Length == 0) {
    Console.WriteLine($"Found {searcher.MatchCount} matches");
    foreach (var matchData in searcher.GetMatchData()) {
        Console.WriteLine($"[{matchData.StartFpos}] {matchData.Path}");
        foreach (var match in matchData.Matches) {
            Console.WriteLine($"{match.Name}, {match.Value}");
        }
    }
}

```
## Performance
The performance benchmark consists of running LineApp and SearchApp over multiple copies of enwik9.  Enwik9 is a 1e9 byte file that is not included.  On the test system LineIndexer caps out at about 3400 MBytes/s and RegexSearcher at 1400 MBytes/s.

![LineIndexer Throughput Graph](https://github.com/imagibee/Gigantor/blob/main/Images/LindeIndexerThroughput.png?raw=true)

![RegexSearcher Throughput Graph](https://github.com/imagibee/Gigantor/blob/main/Images/RegexSearcherThroughput.png?raw=true)

The hardware used to measure performance was a Macbook Pro
- 8-Core Intel Core i9
- L2 Cache (per Core):	256 KB
- L3 Cache:	16 MB
- Memory:	16 GB

## License
[MIT](https://www.mit.edu/~amini/LICENSE.md)

## Versioning
This package uses [semantic versioning](https://en.wikipedia.org/wiki/Software_versioning#Semantic_versioning).  Tags on the main branch indicate versions.  It is recomended to use a tagged version.  The latest version on the main branch should be considered _under development_ when it is not tagged.

## Issues
Report and track issues [here](https://github.com/imagibee/Gigantor/issues).

## Contributing
Minor changes such as bug fixes are welcome.  Simply make a [pull request](https://opensource.com/article/19/7/create-pull-request-github).  Please discuss more significant changes prior to making the pull request by opening a new issue that describes the change.
