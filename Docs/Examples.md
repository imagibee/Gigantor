# Gigantor Examples

## Example 1 - multiple searches in a single pass
If your use case has multiple regular expressions you can search them in a single pass to improve performance because once the partition has been loaded into memory it is more efficient to search it multiple times.

```csharp
// Create the searcher with multiple regular expressions
Imagibee.Gigantor.RegexSearcher searcher = new(
    path,
    new List<System.Text.RegularExpressions.Regex>()
    {
        new(pattern1, RegexOptions.Compiled),
        new(pattern2, RegexOptions.Compiled)
    },
    progress);

// Do the search
Imagibee.Gigantor.Background.StartAndWait(
    searcher,
    progress,
    (_) => { Console.Write("."); },
    1000);
```


### Example 2 - searching multiple compressed files
Another way to gain performacne is to search multiple files in parallel.  An easy way to do that is to use one RegexSearcher for each file and run them in parallel.

```csharp
// Open compressed files
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



## Example 3 - a more comprehensive example
Here is a more comprehensive example that shows using search and line indexing simultaneously to search a uncompressed file and then read several lines around a match and finally conditionally replace some of the matches.

```csharp
// The regular expressions for the search
List<Regex> regexs = new() {
    new(@"comfort\s*food", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    new(@"strong\s*coffee", RegexOptions.IgnoreCase | RegexOptions.Compiled)
};

// A shared wait event to facilitate progress notifications
AutoResetEvent progress = new(false);

// Create the search and indexing workers
LineIndexer indexer = new("enwik9", progress);
RegexSearcher searcher = new("enwik9", regexs, progress);

// Create a IBackground collection for convenient managment
var processes = new List<IBackground>()
{
    indexer,
    searcher
};

// Start search and indexing in parallel and wait for completion
Console.WriteLine($"Working ...");
Stopwatch stopwatch = new();
stopwatch.Start();
Background.StartAndWait(
    processes,
    progress,
    (_) => {
        if (stopwatch.Elapsed.TotalSeconds > 1) {
            Console.Write('.');
            stopwatch.Reset();
        }
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

// Replace matches that contain "coffee" with "tea"
searcher.Replace(
    File.Create(teaPath),
    (match) => {
        if (match.Value.Contains("coffee")) {
            return "tea";
        }
        return match.Value;
    });
```

## Example 3 console output
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
