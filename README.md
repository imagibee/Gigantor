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
const string pattern = @"my\s*yoke\s*is\s*easy";
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

// Get the line of the 1s5 match
var matchLine = indexer.LineFromPosition(
    searcher.GetMatchData()[0].StartFpos);

// Open the searched file for reading
using FileStream fileStream = new(path, FileMode.Open);
Imagibee.Gigantor.StreamReader gigantorReader = new(fileStream);

// Seek to the first line we want to read
var contextLines = 6;
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
 Found 1 matches ...
 [0](my yoke is easy) (0) line 77689 fpos 3460426
 [77683](3460202)  11:28 Come unto me, all ye that labour and are heavy laden, and I will
 [77684](3460274)  give you rest.
 [77685](3460290)  
 [77686](3460292)  11:29 Take my yoke upon you, and learn of me; for I am meek and lowly
 [77687](3460363)  in heart: and ye shall find rest unto your souls.
 [77688](3460414)  
 [77689](3460416)  11:30 For my yoke is easy, and my burden is light.
 [77690](3460468)  
 [77691](3460470)  12:1 At that time Jesus went on the sabbath day through the corn; and
 [77692](3460541)  his disciples were an hungred, and began to pluck the ears of corn and
 [77693](3460613)  to eat.
 [77694](3460622)  
 [77695](3460624)  12:2 But when the Pharisees saw it, they said unto him, Behold, thy
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
