# Gigantor
Gigantor provides fast regular expression search and line indexing of gigantic files that won't fit into RAM.


Search uses System.Text.RegularExpressions.Regex as the regex library and works with either uncompressed files or  streams for compressed input.  While line indexing only works with uncompressed files as input.

## Contents
- `RegexSearcher` - parallel searching in the background using [System.Text.RegularExpression.Regex](https://learn.microsoft.com/en-us/dotnet/api/system.text.regularexpressions.regex?view=net-7.0)
- `LineIndexer` - parallel line counting in background, maps lines to fpos and fpos to lines
- `Background` - functions for managing multiple `RegexSearcher` or `LineIndexer` instances

## Search Performance
Search performance is benchmarked by using the pattern below to search for all URLs in 32 GB of data [taken from Wikipedia](https://archive.org/details/enwik9).  

```csharp
var pattern = @"/https?:\/\/(www\.)?[-a-zA-Z0-9@:%._\+~#=]{2,256}\.[a-z]{2,6}\b([-a-zA-Z0-9@:%_\+.~#()?&//=]*)/"; 
```

### Uncompressed Search
The graph below shows uncompressed search throughput per thread.  Performance starts at 389 MBytes/s for a single thread and climbs up to a maximum of 1906 MBytes/s as more and more workers are utilized which equates to a peak improvement of about 5x. 

![UncompressedSearch](https://raw.githubusercontent.com/imagibee/Gigantor/main/Images/UncompressedSearch.png)

Here is an example that illustrates using RegexSearcher to search an uncompressed file.

```csharp
// Create a regular expression
System.Text.RegularExpressions.Regex regex = new(pattern, RegexOptions.Compiled);

// Create the searcher passing it the decompressed stream
Imagibee.Gigantor.RegexSearcher searcher = new(path, regex, progress);

// Do the search
Imagibee.Gigantor.Background.StartAndWait(
    searcher,
    progress,
    (_) => { Console.Write("."); },
    1000);
```

### Compressed Search
The graph below shows compressed search throughput per file.  For this benchmark multiple copies of the 32 GB data file are gzip compressed to about 10 GB each.  These copies are searched in parallel without decompressing them to disk.  Performance starts at 138 MBytes/s for a single file and climbs up to a maximum of 902 MBytes/s as more and more files are searched which equates to a peak improvement of about 6x.  Note, searching multiple files in parallel can also benefit uncompressed use cases, but using more threads does little to improve the compressed use case.

![CompressedSearch](https://raw.githubusercontent.com/imagibee/Gigantor/main/Images/CompressedSearch.png)

Here is an example that illustrates using RegexSearcher to search a gzipped file without decompressing it to disk.

```csharp
// Open a compressed file with buffering disabled
using var fs = new Imagibee.Gigantor.Utilities.FileStream(
    "myfile.gz",
    FileMode.Open,
    FileAccess.Read,
    FileShare.Read,
    512 * 1024,
    FileOptions.Asynchronous,
    Imagibee.Gigantor.BufferMode.Unbuffered);

// Create a decompressed stream
var stream = new System.IO.Compression.GZipStream(fs, CompressionMode.Decompress, true);

// Create a regular expression
System.Text.RegularExpressions.Regex regex = new(pattern, RegexOptions.Compiled);

// Create the searcher passing it the decompressed stream
Imagibee.Gigantor.RegexSearcher searcher = new(stream, regex, progress);

// Do the search
Imagibee.Gigantor.Background.StartAndWait(
    searcher,
    progress,
    (_) => { Console.Write("."); },
    1000);
```



### Uncompressed Indexing
The graph below shows uncompressed line indexing throughput per thread.  Performance starts at 334 MBytes/s for a single thread and climbs up to a maximum of 3151 MBytes/s as more and more workers are used which equates to a peak improvement of about 10x. 

![UncompressedLine](https://raw.githubusercontent.com/imagibee/Gigantor/main/Images/UncompressedLine.png)



### Notes
1. It is recomended to target `net7.0` if possible because of [regular expression improvements released with .NET 7](https://devblogs.microsoft.com/dotnet/regular-expression-improvements-in-dotnet-7/).
1. It is recomended to [disable file buffering](http://saplin.blogspot.com/2018/07/non-cachedunbuffered-file-operations.html) when using gigantic files  
1. See [NOTES](https://github.com/imagibee/Gigantor/blob/main/NOTES) and [benchmarking apps](https://github.com/imagibee/Gigantor/tree/main/Benchmarking) to run the benchmarks


## More Examples
Below is an example that demonstrates searching multiple gzipped streams in parallel without decompressing them to disk.

```csharp
// Open compressed files with buffering disabled
using var fs1 = new Imagibee.Gigantor.Utilities.FileStream(
    "myfile1.gz",
    FileMode.Open,
    FileAccess.Read,
    FileShare.Read,
    512 * 1024,
    FileOptions.Asynchronous,
    Imagibee.Gigantor.BufferMode.Unbuffered);

using var fs2 = new Imagibee.Gigantor.Utilities.FileStream(
    "myfile2.gz",
    FileMode.Open,
    FileAccess.Read,
    FileShare.Read,
    512 * 1024,
    FileOptions.Asynchronous,
    Imagibee.Gigantor.BufferMode.Unbuffered);

// Create the decompressed streams
var stream1 = new System.IO.Compression.GZipStream(fs1, CompressionMode.Decompress, true);
var stream2 = new System.IO.Compression.GZipStream(fs2, CompressionMode.Decompress, true);

// Create a seperate searcher for each stream
Imagibee.Gigantor.RegexSearcher searcher1 = new(stream1, regex, progress);
Imagibee.Gigantor.RegexSearcher searcher2 = new(stream2, regex, progress);

// Start and wait form completion
Imagibee.Gigantor.Background.StartAndWait(
    new List<Imagibee.Gigantor.IBackground>() { searcher1, searcher2 },
    progress,
    (_) => { Console.Write("."); },
    1000);
```


Below is a more extensive examples that illustrate using RegexSearcher and LineIndexer togehter to search a uncompressed file and then read several lines around a match.

```csharp
// Get enwik9 (this takes a moment)
var path = "myfile.txt";

// The regular expression for the search
const string pattern = @"comfort\s*food";
System.Text.RegularExpressions.Regex regex = new(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

// A shared wait event for progress notifications
AutoResetEvent progress = new(false);

// Create the search and indexing workers
Imagibee.Gigantor.LineIndexer indexer = new(path, progress);
Imagibee.Gigantor.RegexSearcher searcher = new(path, regex, progress);

// Create a IBackground collection for convenient managment
var processes = new List<Imagibee.Gigantor.IBackground>()
{
    indexer,
    searcher
};

// Start search and indexing in parallel and wait for completion
Console.WriteLine($"Searching ...");
Imagibee.Gigantor.Background.StartAndWait(
    processes,
    progress,
    (_) => { Console.Write("."); },
    1000);
Console.Write('\n');

// All done, check for errors
var error = Imagibee.Gigantor.Background.AnyError(processes);
if (error.Length != 0) {
    throw new Exception(error);
}

// Check for cancellation
if (Imagibee.Gigantor.Background.AnyCancelled(processes)) {
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
