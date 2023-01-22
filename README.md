# Gigantor
A dotnet application library for working with large text files

## Contents

- `Imagibee.Gigantor.LineIndexer` - A csharp class that indexes text files in the background.  The main problem being solved is optimizing the performance and memory footprint for very large text files concerning i) the determination of the number of lines, and ii) reading the text of a particular line.

- `IndexerApp` - A command line application for indexing multiple files that is intended for performance benchmarking.

- Unit tests

## Performance
The performance benchmark consists of using a release build of IndexerApp to index multiple copies of enwik9 (1e9 bytes, not included). For each case three trials were run and the fastest trial selected, and the Indexer class parameters were set to a chunkSize of 524,288 bytes with maxWorkers set to 1.

![Throughput Graph](https://github.com/imagibee/LargeTextFile/blob/main/Images/throughput.png?raw=true)

| Files | Bytes Read | Time Elapsed [s] | Throughput [MBytes/s]
|:------|:-----------|:-----------------|:---------------------
| 1     | 1e9        | 1.722169         | 581
| 2     | 2e9        | 1.543044         | 1296
| 3     | 3e9        | 1.813089         | 1655
| 4     | 4e9        | 2.324547         | 1721
| 5     | 5e9        | 3.102832         | 1611
| 6     | 6e9        | 3.928399         | 1527
| 7     | 7e9        | 4.940135         | 1416
| 8     | 8e9        | 4.565616         | 1752
| 9     | 9e9        | 5.440283         | 1654
| 10    | 10e9       | 6.221020         | 1607 
| 11    | 11e9       | 6.977484         | 1576 
| 12    | 12e9       | 8.517666         | 1409
| 13    | 13e9       | 7.743337         | 1679
| 14    | 14e9       | 9.821616         | 1425
| 15    | 15e9       | 11.99997         | 1250
| 16    | 16e9       | 27.86353         | 574


## NOTES
1. No attempt was made to flush the file controller cache between trials or cases.
2. The test system was a MacBook Pro, 2.3 GHz 8-Core Intel Core i9, 16 GB 2667 MHz DDR4, 256 KB L2 Cache (per core). 
