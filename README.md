# Gigantor
A dotnet application library for working with very large files

## Contents
Gigantor includes classes that can be safely and effectively used with very large files.  These classes are designed to operate within a reasonable memory footprint and to thoroughly and efficiently utilize CPU and IO.

- `DuplicateChecker` - detects if two files are duplicates
- `LineIndexer` - line counting, map line to fpos, map fpos to line
- `RegexSearcher` - regex searching in the background
- `FileMapJoin<T>` - abstract base class for partitioning operations

## Examples
Here are several examples that illustrate usage. Refer to the console apps for more thorough examples including how to use multiple instances simultaneously.

### 1. RegexSearcher Example
```csharp
using Imagibee.Gigantor;

// A regular expression to search the file for
Regex regex = new(
    "Hello World!", RegexOptions.IgnoreCase | RegexOptions.Compiled);

// Create a AutoResetEvent wait event to pass in
AutoResetEvent progress = new(false);

// Instantiate a searcher
RegexSearcher searcher = new("~/VeryLarge.txt", regex, progress, 5000, 12);

// Start and wait for completion 
searcher.Start();
Console.WriteLine($"searching {searcher.Path}");
while (searcher.Running) {
    progress.WaitOne(1000);
    Console.Write('.');
}
Console.Write('\n');

// All done
if (searcher.Error.Length != 0) {
    throw new Exception(searcher.Error);
}
Console.WriteLine($"Found {searcher.MatchCount} matches");
foreach (var matchData in searcher.GetMatchData()) {
    Logger.Log($"{matchData.Value} named '{matchData.Name}' " +
        $"at {matchData.StartFpos}]");
}
```

### 2. LineIndexer Example
```csharp
using Imagibee.Gigantor;

// Create a AutoResetEvent wait event to pass in
AutoResetEvent progress = new(false);

// Instantiate a indexer
LineIndexer indexer = new("~/VeryLarge.txt", progress);

// Start and wait for completion
indexer.Start();
Console.WriteLine($"indexing {indexer.Path}");
while (indexer.Running) {
    progress.WaitOne(1000);
    Console.Write('.');
}
Console.Write('\n');

// All done
if (indexer.Error.Length != 0) {
    throw new Exception(indexer.Error);
}
Console.WriteLine(
    $"Found {indexer.LineCount} lines " +
    $"in {indexer.ByteCount} bytes");

// Get the file position at the start of the 1,000,000th line
long myFpos = indexer.PositionFromLine(1000000);

// Get the line that contains the file position value 747724
long myLine = indexer.LineFromPosition(747724);

// Read lines 1,000,000 and 1,000,001
using FileStream fileStream = new(simplePath);
Imagibee.Gigantor.StreamReader gigantorReader = new(fileStream);
fileStream.Seek(indexer.PositionFromLine(myFpos), SeekOrigin.Begin);
string myText = gigantorReader.ReadLine();
myText = gigantorReader.ReadLine();

```

### 3. DuplicateChecker Example

```csharp
using Imagibee.Gigantor;

// Create a AutoResetEvent wait event to pass in
AutoResetEvent progress = new(false);

// Instantiate a checker
DuplicateChecker checker = new("~/VeryLarge1.txt", "~/VeryLarge2.txt", progress);

// Start and wait for completion
checker.Start();
Console.WriteLine($"comparing {checker.Path1} and {checker.Path2}");
while (checker.Running) {
    progress.WaitOne(1000);
    Console.Write('.');
}
Console.Write('\n');

// All done
if (checker.Error.Length != 0) {
    throw new Exception(checker.Error);
}

// Print results
var result = checker.Identical ? "identical":"different";
Console.WriteLine($"{checker.ByteCount} bytes checked");
Console.WriteLine($"files are {result}");
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
