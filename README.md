# Gigantor
A dotnet application library for working with very large files

## Contents
Gigantor includes classes that can be safely and effectively used with very large files.  These classes are designed to operate within a reasonable memory footprint and to thoroughly and efficiently utilize CPU and IO.

- `LineIndexer` - line counting, map line to fpos, map fpos to line
- `RegexSearcher` - regex searching in the background
- `DuplicateChecker` - detects if two files are duplicates
- `FileMapJoin<T>` - base class for creating new file-based map/join operations

## Example
Here is an examples that illustrate usage. Refer to the tests and console apps for additional examples.

### Code
```csharp
using System;
using System.Linq;
using System.Threading;
using System.IO;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using Imagibee.Gigantor;

// The path to be searched and indexed
var path = Path.Combine("Assets", "BibleTest.txt");

// The regular expression for the search
const string pattern = @"love\s*thy\s*neighbour";
Regex regex = new(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

// A shared wait event to facilitate progress notifications
AutoResetEvent progress = new(false);

// Create the search and index workers
LineIndexer indexer = new(path, progress);
RegexSearcher searcher = new(path, regex, progress, 5000, pattern.Length);

// A progress bar
Utilities.ByteProgress progressBar = new(40, Utilities.FileByteCount(path));

// Start search and index in parallel and wait for completion
Console.WriteLine($"Searching ...");
Utilities.StartAndWait(
    new List<IBackground>() { indexer, searcher },
    progress,
    (processes) =>
    {
        progressBar.Update(
            processes.Select((p) => p.ByteCount).Sum());
    },
    1000);
Console.Write('\n');

// All done, check for errors
if (searcher.Error.Length != 0) {
    throw new Exception(searcher.Error);
}

// Display search results
Console.WriteLine($"Found {searcher.MatchCount} matches ...");
var matchDatas = searcher.GetMatchData();
for (var i=0; i<matchDatas.Count; i++) {
    var matchData = matchDatas[i];
    Console.WriteLine(
        $"[{i}]({matchData.Value}) ({matchData.Name}) " +
        $"at {indexer.LineFromPosition(matchData.StartFpos)} " +
        $"({matchData.StartFpos})");
}

// Display the lines before and after the 1st search result
var contextSize = 2;
Console.WriteLine($"{2* contextSize + 1} line context ...");
var match = searcher.GetMatchData()[2];
var matchLine = indexer.LineFromPosition(match.StartFpos);
using FileStream fileStream = new(path, FileMode.Open);
Imagibee.Gigantor.StreamReader gigantorReader = new(fileStream);
fileStream.Seek(indexer.PositionFromLine(matchLine - contextSize), SeekOrigin.Begin);
for (var line = matchLine - contextSize; line <= matchLine + contextSize; line++) {
    Console.WriteLine(
        $"[{line}]({indexer.PositionFromLine(line)})  " +
        gigantorReader.ReadLine());
}
```

### Console output
```console
 Searching ...
 ################################################################################
 Found 8 matches ...
 [0](love thy neighbour) (0) at 10773 (485642)
 [1](love thy
 neighbour) (0) at 77079 (3433850)
 [2](love thy neighbour) (0) at 78541 (3498270)
 [3](love thy neighbour) (0) at 78914 (3514744)
 [4](love thy
 neighbour) (0) at 81186 (3613142)
 [5](love thy neighbour) (0) at 91645 (4070425)
 [6](love thy neighbour) (0) at 94224 (4182790)
 [7](love thy
 neighbour) (0) at 97269 (4319613)
 5 line context ...
 [78539](3498123)  Thou shalt not commit adultery, Thou shalt not steal, Thou shalt not
 [78540](3498193)  bear false witness, 19:19 Honour thy father and thy mother: and, Thou
 [78541](3498264)  shalt love thy neighbour as thyself.
 [78542](3498302)  
 [78543](3498304)  19:20 The young man saith unto him, All these things have I kept from

```


## Performance
The performance benchmark consists of running the included benchmarking apps over multiple copies of enwik9.  Enwik9 is a 1e9 byte file that is not included.

![DuplicateChecker Throughput Graph](https://github.com/imagibee/Gigantor/blob/main/Images/DuplicateCheckerThroughput.png?raw=true)

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
