# Gigantor
A dotnet application library for working with gigantic files


## Introduction
Have you ever had the need to work on a very large file?  Searching it?  Perhaps mapping file positions to line numbers? Or maybe something else entirely?  If so you may have learned that working with large files presents unique challenges.  The goal of Gigantor is to provide robust, easy, ready-made solutions for working with gigantic files.  These solutions are intended to be safe and effective by having a reasonable memory footprint and thoroughly and efficiently utilizing CPU and IO.

In order to accomplish this goal, Gigantor uses parallel chunk processing.  For those unfamiliar with this terminology.  It encompasses partitioning the file into chunks, using a seperate thread to process each chunk into a result, and finally joining the results.

Not all file processing problems will fit cleanly into this paradigm, but for those that do there is some opportunity for reuse.  Gigantor provides `FileMapJoin<T>` as a base class for reuse when working with files that are already uncompressed on disk.

## Contents
- `RegexSearcher` - regex searching in the background
- `LineIndexer` - line counting, map line to fpos, map fpos to line
- `DuplicateChecker` - detects if two files are duplicates
- `FileMapJoin<T>` - base class for implementing custom file-based map/join operations


## Example
Here is an examples that illustrate searching a large file and displaying several lines around a match.

```csharp
using Imagibee.Gigantor;

// The path to be searched and indexed
var path = Path.Combine("Assets", "BibleTest.txt");

// The regular expression for the search
const string pattern = @"love\s*thy\s*neighbour";
Regex regex = new(
    pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

// A shared wait event to facilitate progress notifications
AutoResetEvent progress = new(false);

// Create the search and indexing workers
LineIndexer indexer = new(path, progress);
RegexSearcher searcher = new(path, regex, progress);


// Create a IBackground collection for convenient monitoring
var processes = new List<IBackground>()
{
    indexer,
    searcher
};

// Create a progress bar to illustrate progress updates
Utilities.ByteProgress progressBar = new(
    40, processes.Count * Utilities.FileByteCount(path));

// Start search and indexing in parallel and wait for completion
Console.WriteLine($"Searching ...");
Background.StartAndWait(
    processes,
    progress,
    (_) =>
    {
        progressBar.Update(
            processes.Select((p) => p.ByteCount).Sum());
    },
    1000);
Console.Write('\n');

// All done, check for errors
var error = Background.AnyError(processes);
if (error.Length != 0) {
    throw new Exception(error);
}

// Check for cancellation
if (Background.AnyCancelled(processes)) {
    throw new Exception("search cancelled");
}

// Display search results
Console.WriteLine($"Found {searcher.MatchCount} matches ...");
var matchDatas = searcher.GetMatchData();
for (var i=0; i<matchDatas.Count; i++) {
    var matchData = matchDatas[i];
    Console.WriteLine(
        $"[{i}]({matchData.Value}) ({matchData.Name}) " +
        $"line {indexer.LineFromPosition(matchData.StartFpos)} " +
        $"fpos {matchData.StartFpos}");
}

// Get the line of the 3rd match
var matchLine = indexer.LineFromPosition(
    searcher.GetMatchData()[2].StartFpos);

// Open the searched file for reading
using FileStream fileStream = new(path, FileMode.Open);
Imagibee.Gigantor.StreamReader gigantorReader = new(fileStream);

// Seek to the first line we want to read
var contextLines = 2;
fileStream.Seek(indexer.PositionFromLine(
    matchLine - contextLines), SeekOrigin.Begin);

// Read and display a few lines around the match
for (var line = matchLine - contextLines;
    line <= matchLine + contextLines;
    line++) {
    Console.WriteLine(
        $"[{line}]({indexer.PositionFromLine(line)})  " +
        gigantorReader.ReadLine());
}
```

Console output
```console
 Searching ...
 ########################################
 Found 8 matches ...
 [0](love thy neighbour) (0) line 10773 fpos 485642
 [1](love thy
 neighbour) (0) line 77079 fpos 3433850
 [2](love thy neighbour) (0) line 78541 fpos 3498270
 [3](love thy neighbour) (0) line 78914 fpos 3514744
 [4](love thy
 neighbour) (0) line 81186 fpos 3613142
 [5](love thy neighbour) (0) line 91645 fpos 4070425
 [6](love thy neighbour) (0) line 94224 fpos 4182790
 [7](love thy
 neighbour) (0) line 97269 fpos 4319613
 [78539](3498123)  Thou shalt not commit adultery, Thou shalt not steal, Thou shalt not
 [78540](3498193)  bear false witness, 19:19 Honour thy father and thy mother: and, Thou
 [78541](3498264)  shalt love thy neighbour as thyself.
 [78542](3498302)  
 [78543](3498304)  19:20 The young man saith unto him, All these things have I kept from
```
Refer to the tests and console apps for additional examples.

## Performance
The performance benchmark consists of running the included benchmarking apps over enwik9 and measuring the throughput.  Enwik9 is a 1e9 byte file that is not included.

![Throughput Graph](https://github.com/imagibee/Gigantor/blob/main/Images/Throughput.png?raw=true)


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
