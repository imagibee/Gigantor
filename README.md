# Gigantor
Gigantor provides classes that support regular expression searches of gigantic files

The purpose of Gigantor is robust, easy, ready-made searching of gigantic files that avoids common pitfalls including unresponsiveness, excessive memory usage, and processing time that are often encountered with this type of application.

In order to accomplish this goal, Gigantor provides `RegexSearcher` and `LineIndexer` classes that work together to search and read a file.  Both these classes use a similar approach.  They partition the file into chunks in the background, launch threads to work on each partition, update progress statistics, and finally join and sort the results.

Since many file processing applications fit into this parallel chunk processing paradigm, Gigantor also provides `FileMapJoin<T>` as a reusable base class for creating new file map/join classes.  This base class is customizable through its `Start`, `Map`, `Join`, `Finish` methods as well as its `chunkSize`, `maxWorkers`, and `joinMode` constructor parameters.

## Contents
- `RegexSearcher` - regex searching in the background
- `LineIndexer` - line counting in background, maps lines to fpos and fpos to lines
- `DuplicateChecker` - file duplicate detection in the background
- `FileMapJoin<T>` - base class for implementing custom file-based map/join operations
- `IBackground` - common interface for contolling a background job
- `Background` - functions for managing collections of IBackground

## File vs Stream Mode
RegexSearcher supports file and stream modes.  For file mode the user specifies the path to a file on disk as the input.  For stream mode the user specifies an open `Stream` as the input.  File mode is generally more performant and it is recomended to use file mode in situations where the file to be searched is already stored on disk.  The main use case for stream mode is searching a compressed file without decompressing it to disk, but it is considerably slower.

## Example - stream mode search
Here is an example that illustrates constructing RegexSearcher to search a gzipped file without decompressing it to disk.

```csharp
// Open a compressed file
using var fs = new FileStream(
    "myfile.gz", FileMode.Open);

// Create a decompressed stream
var stream = new GZipStream(
    fs, CompressionMode.Decompress, true);

// Create the searcher passing it the decompressed stream
RegexSearcher searcher = new(
    stream, regex, progress);
```

## Example - file mode search and index
Here is a more extensive examples that illustrate using RegexSearcher and LineIndexer to search a large uncompressed file and then read several lines around a match.

```csharp
using Imagibee.Gigantor;

// Get enwik9 (this takes a moment)
var path = Utilities.GetEnwik9();

// The regular expression for the search
const string pattern = @"comfort\s*food";
Regex regex = new(
    pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

// A shared wait event for progress notifications
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

File mode search and index console output
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
It is recomended to target `net7.0` if possible because of [regular expression improvements released with .NET 7](https://devblogs.microsoft.com/dotnet/regular-expression-improvements-in-dotnet-7/).

### Per Thread Performance
The first performance graph consists of running the [benchmarking apps](https://github.com/imagibee/Gigantor/tree/main/Benchmarking) over 5GBytes of data and measuring the throughput at different values of `maxWorkers`.  For RegexSearcher several distinct modes are benchmarked including file, stream, and gzipped stream.

![Throughput Graph](https://raw.githubusercontent.com/imagibee/Gigantor/main/Images/Throughput.png)


### Per File Performance
For the gzip stream mode, performance is not much improved by multi-threading.  Another option for getting better performance in this mode which may be possible depending on the use case is to search multiple files in parallel.  Searching multiple files in parallel is a good optimization strategy for uncompressed use cases as well.  The following graph compares searching multiple copies of the same file in gzip stream mode.

![Gzip Throughput Graph](https://raw.githubusercontent.com/imagibee/Gigantor/main/Images/GzipThroughput.png)


## Example - search of multiple compressed streams in parallel
Here is example code that demonstrates searching multiple gzipped streams in parallel without decompressing them to disk.

```csharp
// Create the decompressed streams
var stream1 = new GZipStream(
    new FileStream("myfile1.gz", FileMode.Open),
    CompressionMode.Decompress,
    true);

var stream2 = new GZipStream(
    new FileStream("myfile2.gz", FileMode.Open),
    CompressionMode.Decompress,
    true);

// Create a seperate searcher for each stream
RegexSearcher searcher1 = new(stream1, regex, progress);
RegexSearcher searcher2 = new(stream2, regex, progress);

// Start and wait form completion
Background.StartAndWait(
    new List<IBackground>() { searcher1, searcher2 },
    progress,
    (_) => { Console.Write("."); },
    1000);
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
