# Gigantor
Gigantor provides classes that support regular expression searches of gigantic files

The purpose of Gigantor is robust, easy, ready-made searching of gigantic files that avoids common pitfalls.  These goals include overcoming the problems of responsiveness, memory footprint, and processing time that are often encountered with this type of application.

In order to accomplish this goal, Gigantor provides `RegexSearcher` and `LineIndexer` classes that work together to search and read a file.  Both these classes use a similar approach.  They partition the file into chunks in the background, launch threads to work on each partition, update progress statistics, and finally join and sort the results.

Since many file processing applications fit into this parallel chunk processing paradigm, Gigantor also provides `FileMapJoin<T>` as a reusable base class for creating new file map/join classes.  This base class is customizable through its `Start`, `Map`, `Join`, `Finish` methods as well as its `chunkSize`, `maxWorkers`, and `joinMode` constructor parameters.

## Contents
- `RegexSearcher` - regex searching in the background
- `LineIndexer` - line counting in background, maps lines to fpos and fpos to lines
- `DuplicateChecker` - file duplicate detection in the background
- `FileMapJoin<T>` - base class for implementing custom file-based map/join operations
- `IBackground` - common interface for contolling a background job
- `Background` - functions for managing collections of IBackground


## Example
Here is an examples that illustrate searching a large file and reading several lines around a match.

```csharp
using Imagibee.Gigantor;

// Get enwik9 (this takes a moment)
var path = Utilities.GetEnwik9();

// The regular expression for the search
const string pattern = @"comfort\s*food";
Regex regex = new(
    pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

// A shared wait event to facilitate progress notifications
AutoResetEvent progress = new(false);

// Create the search and indexing workers
LineIndexer indexer = new(path, progress);
RegexSearcher searcher = new(path, regex, progress);

// Create a IBackground collection for convenient managment
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
if (searcher.MatchCount != 0) {
    Console.WriteLine($"Found {searcher.MatchCount} matches ...");
    var matchDatas = searcher.GetMatchData();
    for (var i = 0; i < matchDatas.Count; i++) {
        var matchData = matchDatas[i];
        Console.WriteLine(
            $"[{i}]({matchData.Value}) ({matchData.Name}) " +
            $"line {indexer.LineFromPosition(matchData.StartFpos)} " +
            $"fpos {matchData.StartFpos}");
    }

    // Get the line of the 1st match
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
}
```

Example console output
```console
 Searching ...
 ########################################
 Found 11 matches ...
 [0](Comfort food) (0) line 2115660 fpos 185913740
 [1](comfort food) (0) line 2115660 fpos 185913753
 [2](comfort food) (0) line 2405473 fpos 212784867
 [3](comfort food) (0) line 3254241 fpos 275813781
 [4](comfort food) (0) line 3254259 fpos 275817860
 [5](comfort food) (0) line 3993946 fpos 334916584
 [6](comfort food) (0) line 4029113 fpos 337507601
 [7](comfort food) (0) line 4194105 fpos 350053436
 [8](comfort food) (0) line 8614841 fpos 691616502
 [9](comfort food) (0) line 10190137 fpos 799397876
 [10](comfort food) (0) line 12488963 fpos 954837923
 [2115654](185912493)  
 [2115655](185912494)  Some [[fruit]]s were available in the area. [[Muscadine]]s, [[blackberry|blackberries]], [[raspberry|raspberries]], and many other wild berries were part of settlers&amp;#8217; diets when they were available.
 [2115656](185912703)  
 [2115657](185912704)  Early settlers also supplemented their diets with meats.  Most meat came from the hunting of native game.  [[Venison]] was an important meat staple due to the abundance of [[white-tailed deer]] in the area.  Settlers also hunted [[rabbit]]s, [[squirrel]]s, [[opossum]]s, and [[raccoon]]s, all of which were pests to the crops they raised.  [[Livestock]] in the form of [[hog]]s and [[cattle]] were kept.  When game or livestock was killed, the entire animal was used.  Aside from the meat, it was not uncommon for settlers to eat organ meats such as [[liver]], [[brain]]s and [[intestine]]s. This tradition remains today in hallmark dishes like [[chitterlings]] (commonly called ''chit&amp;#8217;lins'') which are fried large [[intestines]] of [[hog]]s, [[livermush]] (a common dish in the Carolinas made from hog liver), and pork [[brain]]s and eggs.  The fat of the animals, particularly hogs, was rendered and used for cooking and frying.
 [2115658](185913646)  
 [2115659](185913647)  ===Southern cuisine for the masses===
 [2115660](185913685)  A niche market for Southern food along with American [[Comfort food|comfort food]] has proven profitable for chains such as [[Cracker Barrel]], who have extended their market across the country, instead of staying solely in the South.
 [2115661](185913920)  
 [2115662](185913921)  Southern chains that are popular across the country include [[Stuckey's]] and [[Popeyes Chicken &amp; Biscuits|Popeye's]]. The former is known for being a &quot;pecan shoppe&quot; and the latter is known for its spicy fried chicken.
 [2115663](185914154)  
 [2115664](185914155)  Other Southern chains which specialize in this type of cuisine, but have decided mainly to stay in the South, are [[Po' Folks]] (also known as ''Folks'' in some markets) and Famous Amos. Another type of selection is [[Sonny's Real Pit Bar-B-Q]].
 [2115665](185914401)  
 [2115666](185914402)  ==Cajun and Creole cuisine==
```

Refer to the tests and console apps for additional examples.

## Performance
The performance benchmark consists of running the included benchmarking apps over enwik9 and measuring the throughput.  Enwik9 is a 1e9 byte file that is not included.

![Throughput Graph](https://raw.githubusercontent.com/imagibee/Gigantor/main/Images/Throughput.png)

Here is the search benchmark console output for a 5 GiByte search.  On the test system, performance peaked around 16 worker threads, and the peak is roughly eight times faster (8x) than the single threaded baseline.

```console
$ dotnet SearchApp/bin/Release/net6.0/SearchApp.dll benchmark ${TMPDIR}/enwik9
........................
maxWorkers=1, chunkKiBytes=512, maxThread=32767
   105160 matches found
   searched 5000000000 bytes in 24.0289207 seconds
-> 208.0825877460239 MBytes/s
..............
maxWorkers=2, chunkKiBytes=512, maxThread=32767
   105160 matches found
   searched 5000000000 bytes in 12.692795 seconds
-> 393.92426963485974 MBytes/s
.........
maxWorkers=4, chunkKiBytes=512, maxThread=32767
   105160 matches found
   searched 5000000000 bytes in 6.8668367 seconds
-> 728.1373095707955 MBytes/s
....
maxWorkers=8, chunkKiBytes=512, maxThread=32767
   105160 matches found
   searched 5000000000 bytes in 3.7174496 seconds
-> 1345.0081475213544 MBytes/s
....
maxWorkers=16, chunkKiBytes=512, maxThread=32767
   105160 matches found
   searched 5000000000 bytes in 3.0211296 seconds
-> 1655.0100995336313 MBytes/s
....
maxWorkers=32, chunkKiBytes=512, maxThread=32767
   105160 matches found
   searched 5000000000 bytes in 3.191699 seconds
-> 1566.5637643148682 MBytes/s
....
maxWorkers=64, chunkKiBytes=512, maxThread=32767
   105160 matches found
   searched 5000000000 bytes in 3.2240221 seconds
-> 1550.8578554718963 MBytes/s
....
maxWorkers=128, chunkKiBytes=512, maxThread=32767
   105160 matches found
   searched 5000000000 bytes in 3.3693127 seconds
-> 1483.982178323787 MBytes/s
```


The hardware used to measure performance was a Macbook Pro
- 8-Core Intel Core i9
- L2 Cache (per Core):	256 KB
- L3 Cache:	16 MB
- Memory:	16 GB

## License
[MIT](https://raw.githubusercontent.com/imagibee/Gigantor/main/LICENSE)

## Versioning
This package uses [semantic versioning](https://en.wikipedia.org/wiki/Software_versioning#Semantic_versioning).  Tags on the main branch indicate versions.  It is recomended to use a tagged version.  The latest version on the main branch should be considered _under development_ when it is not tagged.

## Issues
Report and track issues [here](https://github.com/imagibee/Gigantor/issues).

## Contributing
Minor changes such as bug fixes are welcome.  Simply make a [pull request](https://opensource.com/article/19/7/create-pull-request-github).  Please discuss more significant changes prior to making the pull request by opening a new issue that describes the change.
