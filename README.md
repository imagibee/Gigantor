# Gigantor
Gigantor provides fast regular expression search and line indexing of gigantic files.

It solves the following problems:
* file exceeds the size of memory
* CPUs are under-utilized
* main thread is unresponsive
* search of compressed files

The approach is to partition the data into chunks which are processed in parallel using a [System.Threading.ThreadPool](https://learn.microsoft.com/en-us/dotnet/api/system.threading.threadpool?view=net-7.0) of background threads.  The input data can either be uncompressed files, or streams.  Streams are intended to support use cases that have additional processing needs by allowing the user to provide a class derived from [System.IO.Stream](https://learn.microsoft.com/en-us/dotnet/api/system.io.stream?view=net-7.0) as the input.  One notable use case for streams is searching compressed data.

Search depends on [System.Text.RegularExpressions.Regex](https://learn.microsoft.com/en-us/dotnet/api/system.text.regularexpressions.regex?view=net-7.0) as the underlying regular expression library.  While line indexing uses its own implementation.

## Contents
- `RegexSearcher` - multi-threaded regular expression matching in the background
- `LineIndexer` - multi-threaded line counting in background, maps lines to fpos and fpos to lines
- `Background` - functions for managing multiple `RegexSearcher` or `LineIndexer` instances

## Performance
Performance is benchmarked by processing a 32 GB prepared text file.  Each benchmark is timed, and throughput is computed by dividing the number of bytes processed by the time.

For the search benchmarks the following pattern is used to search for all URLs in the file.

```csharp
var pattern = @"/https?:\/\/(www\.)?[-a-zA-Z0-9@:%._\+~#=]{2,256}\.[a-z]{2,6}\b([-a-zA-Z0-9@:%_\+.~#()?&//=]*)/"; 
```

### Uncompressed Search
The following graph demonstrates that unbuffered IO is about 35% faster for the uncompressed search benchmark.  It also shows about a 5x gain due to parallelization.  Unbuffered IO can be used via the `bufferMode` parameter, or using the approach taken by `Imagibee.Gigantor.FileStream.Create` when opening the file. 

![UncompressedSearch](https://raw.githubusercontent.com/imagibee/Gigantor/main/Images/UncompressedSearch.png)

### Example 1 - using RegexSearcher to search an uncompressed file

```csharp
// Create a regular expression
System.Text.RegularExpressions.Regex regex = new(pattern, RegexOptions.Compiled);

// Create the searcher using the file path as input
Imagibee.Gigantor.RegexSearcher searcher = new(path, regex, progress);

// Do the search
Imagibee.Gigantor.Background.StartAndWait(
    searcher,
    progress,
    (_) => { Console.Write("."); },
    1000);
```

### Compressed Search
Using multiple threads to search a single compressed file does not yield performance gains since decompression dominates the processing time.  However, if multiple files are searched in parallel there is some gain.  The following graph shows compressed search throughput for varying numbers of files.  For this benchmark multiple copies of the 32 GB data file are gzip compressed to about 10 GB each.  These copies are searched in parallel without decompressing them to disk.  Searching multiple compressed files in parallel is about 6x faster than searching them one at a time.  The throughput is measured in terms of the *uncompressed* bytes of data that are searched.  Searching multiple files in parallel can also benefit uncompressed use cases.

![CompressedSearch](https://raw.githubusercontent.com/imagibee/Gigantor/main/Images/CompressedSearch.png)

### Example 2 - using RegexSearcher to search multiple compressed files

```csharp
// Open compressed files with buffering disabled
using var fs1 = Imagibee.Gigantor.FileStream.Create("myfile1.gz");
using var fs2 = Imagibee.Gigantor.FileStream.Create("myfile2.gz");

// Create the decompressed streams
var stream1 = new System.IO.Compression.GZipStream(fs1, CompressionMode.Decompress, true);
var stream2 = new System.IO.Compression.GZipStream(fs2, CompressionMode.Decompress, true);

// Create a seperate searcher for each stream
Imagibee.Gigantor.RegexSearcher searcher1 = new(stream1, regex, progress);
Imagibee.Gigantor.RegexSearcher searcher2 = new(stream2, regex, progress);

// Start both searchers in parallel and wait for completion
Imagibee.Gigantor.Background.StartAndWait(
    new List<Imagibee.Gigantor.IBackground>() { searcher1, searcher2 },
    progress,
    (_) => { Console.Write("."); },
    1000);
```



### Uncompressed Indexing
The following graph shows about a 10x gain for uncompressed line indexing due to parallelization using unbuffered IO.

![UncompressedLine](https://raw.githubusercontent.com/imagibee/Gigantor/main/Images/UncompressedLine.png)

### Example 3 - using RegexSearcher and LineIndexer together to search a uncompressed file and then read several lines around a match

```csharp
var path = "enwik9";

// The regular expressions for the search
List<Regex> regexs = new() {
    new(@"comfort\s*food", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    new(@"strong\s*coffee", RegexOptions.IgnoreCase | RegexOptions.Compiled)
};

// A shared wait event to facilitate progress notifications
AutoResetEvent progress = new(false);

// Create the search and indexing workers
LineIndexer indexer = new(enwik9Path, progress);
RegexSearcher searcher = new(enwik9Path, regexs, progress);

// Create a IBackground collection for convenient managment
var processes = new List<IBackground>()
{
    indexer,
    searcher
};

// Start search and indexing in parallel and wait for completion
Console.WriteLine($"Working ...");
Background.StartAndWait(
    processes,
    progress,
    (_) => { Console.Write('.'); },
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

// Display results
for (var j = 0; j < regexs.Count; j++) {
    Console.WriteLine($"Found {searcher.GetMatchData(j).Count} matches for regex {j} ...");
    if (searcher.GetMatchData(j).Count != 0) {
        var matchDatas = searcher.GetMatchData(j);
        for (var i = 0; i < matchDatas.Count; i++) {
            var matchData = matchDatas[i];
            Console.WriteLine(
                $"[{i}]({matchData.Value}) ({matchData.Name}) " +
                $"line {indexer.LineFromPosition(matchData.StartFpos)} " +
                $"fpos {matchData.StartFpos}");
        }

        // Get the line of the 1st match
        var matchLine = indexer.LineFromPosition(
            searcher.GetMatchData(j)[0].StartFpos);

        // Open the searched file for reading
        using System.IO.FileStream fileStream = new(enwik9Path, FileMode.Open);
        Imagibee.Gigantor.StreamReader gigantorReader = new(fileStream);

        // Seek to the first line we want to read
        var contextLines = 3;
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
}
```

### Example 3 console output
```console
Found 11 matches for regex 0 ...
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
[2115657](185912704)  Early settlers also supplemented their diets with meats.  Most meat came from the hunting of native game.  [[Venison]] was an important meat staple due to the abundance of [[white-tailed deer]] in the area.  Settlers also hunted [[rabbit]]s, [[squirrel]]s, [[opossum]]s, and [[raccoon]]s, all of which were pests to the crops they raised.  [[Livestock]] in the form of [[hog]]s and [[cattle]] were kept.  When game or livestock was killed, the entire animal was used.  Aside from the meat, it was not uncommon for settlers to eat organ meats such as [[liver]], [[brain]]s and [[intestine]]s. This tradition remains today in hallmark dishes like [[chitterlings]] (commonly called ''chit&amp;#8217;lins'') which are fried large [[intestines]] of [[hog]]s, [[livermush]] (a common dish in the Carolinas made from hog liver), and pork [[brain]]s and eggs.  The fat of the animals, particularly hogs, was rendered and used for cooking and frying.
[2115658](185913646)  
[2115659](185913647)  ===Southern cuisine for the masses===
[2115660](185913685)  A niche market for Southern food along with American [[Comfort food|comfort food]] has proven profitable for chains such as [[Cracker Barrel]], who have extended their market across the country, instead of staying solely in the South.
[2115661](185913920)  
[2115662](185913921)  Southern chains that are popular across the country include [[Stuckey's]] and [[Popeyes Chicken &amp; Biscuits|Popeye's]]. The former is known for being a &quot;pecan shoppe&quot; and the latter is known for its spicy fried chicken.
[2115663](185914154)  
Found 2 matches for regex 1 ...
[0](Strong Coffee) (0) line 9158481 fpos 729217370
[1](strong coffee) (0) line 11045381 fpos 856562911
[9158478](729217176)  *&quot;Love Song For A Savior&quot;
[9158479](729217212)  *&quot;Portrait of an Apology&quot;
[9158480](729217248)  *&quot;Sad Clown&quot;
[9158481](729217271)  *&quot;The Coffee Song&quot; (This song is often referred to as being subtitled &quot;Good Goffee, Strong Coffee&quot;, but has been officially titled &quot;The Coffee Song&quot;
[9158482](729217450)  
[9158483](729217451)  === [[Ja Rule]] ===
[9158484](729217471)  *&quot;Mesmerize&quot; (featuring [[Ashanti (singer)|Ashanti]])
```

### Notes
1. Target net7.0 if possible because of [regular expression improvements released with .NET 7](https://devblogs.microsoft.com/dotnet/regular-expression-improvements-in-dotnet-7/).
1. Disable file cacheing for gigantic files using `bufferMode`, `Imagibee.Gigantor.FileStream.Create`, or equivalent
1. Use case tuning is supported by the following parameters: `maxMatchCount`, `chunkKiBytes`, `maxWorkers`, `overlap`, `bufferMode`
1. See [NOTES](https://github.com/imagibee/Gigantor/blob/main/NOTES) and [benchmarking apps](https://github.com/imagibee/Gigantor/tree/main/Benchmarking) for hints about running the benchmarks



## Hardware
The benchmarks were run on a Macbook Pro with the following hardware.
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
