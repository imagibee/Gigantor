# LargeTextFile
A dotnet application library for working with large text files

## Contents
### Imagibee.TextFile.Indexer
A csharp class that indexes text files in the background.  The main problem being solved is optimizing the performance and memory footprint for very large text files concerning i) the determination of the number of lines, and ii) reading the text of a particular line.

### IndexerApp
A command line application for performance benchmarking.

### Unit tests
Functional testing.

## Performance
The performance benchmark consists of using a release build of IndexerApp to index multiple copies of enwik9 (1e9 bytes, not included). For each case of files three trials were run and the fastest trial selected.  The Indexer class parameters were set to a chunkSize of 524,288 bytes with maxWorkers set to 1 for each case.

| Files | Bytes Read | Time Elapsed [s] | Throughput [MBytes/s]
|:------|:-----------|:-----------------|:---------------------
| 1     | 1e9        | 2.002009         | 499
| 2     | 2e9        | 2.001029         | 999
| 3     | 3e9        | 2.008185         | 1494
| 4     | 4e9        | 3.003124         | 1332
| 5     | 5e9        | 3.003508         | **1664**
| 6     | 6e9        | 4.003641         | 1499
| 7     | 7e9        | 5.007622         | 1398
| 8     | 8e9        | 5.006550         | 1598
| 9     | 9e9        | 6.011037         | 1497
| 10    | 10e9       | 7.005548         | 1427 
| 11    | 11e9       | 8.007783         | 1373 
| 12    | 12e9       | 9.008069         | 1332
| 13    | 13e9       | 11.01649         | 1180
| 14    | 14e9       | 11.01100         | 1271
| 15    | 15e9       | 13.02632         | 1152
| 16    | 16e9       | 29.03443         | 551


## NOTES
1. No attempt was made to flush the file controller cache between trials or cases.
2. The test system was a MacBook Pro, 2.3 GHz 8-Core Intel Core i9, 16 GB 2667 MHz DDR4, 256 KB L2 Cache (per core). 
