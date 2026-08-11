# Benchmark History

Baseline and regression log for AdaptiveSlop benchmarks. Run before and after any
performance-relevant change. Append a new dated section per run. Never edit old sections.

Run command:

```bash
cd benchmarks/AdaptiveSlop.Benchmarks && dotnet run -c Release -- --filter "*" --job short
```

Compare new runs against the most recent section. Treat these as regressions:

- Mean of an `AdaptiveSlop` row grows beyond the error margin of the baseline row.
- Allocated of an `AdaptiveSlop` row grows at all on a steady-state path.

Each table compares AdaptiveSlop against FSharp.Data.Adaptive (FDA) on the same workload.
Ratio 1.00 = the AdaptiveSlop row itself (baseline row per group).

---

## 2026-08-03 — Phase 0 baseline (pre-rebuild)

- Commit: ab69811 (+ uncommitted Phase 0 test additions; core code unchanged)
- Machine: macOS Tahoe 26.5.2, Intel Core i9-9980HK 2.40GHz, .NET 8.0.23, x64 RyuJIT
- Job: ShortRun (IterationCount=3, LaunchCount=1, WarmupCount=3)
- State of the code: pull-based with locks, ThreadStatics, and dead push machinery.
  This is the reference for all rebuild phases. ConcurrentBenchmarks measures the old
  multi-threaded model; the rebuild removes that model and this class stops being
  comparable until cross-thread posting exists again.

<!-- Generated from the Phase 0 baseline run. Edit only by appending new entries. -->

### BindBenchmarks

| Method             | Iterations | Mean     | Error      | StdDev   | Ratio | RatioSD | Gen0    | Allocated | Alloc Ratio |
|------------------- |----------- |---------:|-----------:|---------:|------:|--------:|--------:|----------:|------------:|
| AdaptiveSlop       | 1000       | 543.6 μs |   834.0 μs | 45.71 μs |  1.00 |    0.10 | 10.7422 | 349.75 KB |        1.00 |
| FSharpDataAdaptive | 1000       | 858.6 μs | 1,220.6 μs | 66.91 μs |  1.59 |    0.15 | 69.3359 | 570.31 KB |        1.63 |

### ConcurrentBenchmarks

| Method             | ThreadCount | IterationsPerThread | Mean     | Error    | StdDev   | Ratio | RatioSD | Gen0     | Allocated | Alloc Ratio |
|------------------- |------------ |-------------------- |---------:|---------:|---------:|------:|--------:|---------:|----------:|------------:|
| AdaptiveSlop       | 4           | 500                 | 697.7 μs | 494.7 μs | 27.12 μs |  1.00 |    0.05 |   6.8359 | 351.33 KB |        1.00 |
| FSharpDataAdaptive | 4           | 500                 | 936.4 μs | 726.2 μs | 39.81 μs |  1.34 |    0.07 | 112.3047 |  922.7 KB |        2.63 |

### DeepChainBenchmarks

| Method             | Depth | Iterations | Mean         | Error       | StdDev     | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------- |------ |----------- |-------------:|------------:|-----------:|------:|--------:|-------:|----------:|------------:|
| **AdaptiveSlop**       | **5**     | **100**        |     **63.61 μs** |    **62.67 μs** |   **3.435 μs** |  **1.00** |    **0.07** | **0.3662** |   **3.13 KB** |        **1.00** |
| FSharpDataAdaptive | 5     | 100        |    111.49 μs |    52.00 μs |   2.850 μs |  1.76 |    0.09 | 5.6152 |  46.09 KB |       14.75 |
|                    |       |            |              |             |            |       |         |        |           |             |
| **AdaptiveSlop**       | **10**    | **100**        |    **124.23 μs** |    **62.19 μs** |   **3.409 μs** |  **1.00** |    **0.03** | **0.3662** |   **3.13 KB** |        **1.00** |
| FSharpDataAdaptive | 10    | 100        |    186.19 μs |    12.94 μs |   0.709 μs |  1.50 |    0.04 | 5.6152 |  46.09 KB |       14.75 |
|                    |       |            |              |             |            |       |         |        |           |             |
| **AdaptiveSlop**       | **20**    | **100**        |    **271.97 μs** |    **91.42 μs** |   **5.011 μs** |  **1.00** |    **0.02** |      **-** |   **3.13 KB** |        **1.00** |
| FSharpDataAdaptive | 20    | 100        |    354.03 μs |    21.57 μs |   1.182 μs |  1.30 |    0.02 | 5.3711 |  46.09 KB |       14.75 |
|                    |       |            |              |             |            |       |         |        |           |             |
| **AdaptiveSlop**       | **100**   | **100**        |  **1,312.37 μs** | **1,346.92 μs** |  **73.829 μs** |  **1.00** |    **0.07** |      **-** |   **3.13 KB** |        **1.00** |
| FSharpDataAdaptive | 100   | 100        |  1,599.49 μs |   142.50 μs |   7.811 μs |  1.22 |    0.06 | 3.9063 |  46.09 KB |       14.75 |
|                    |       |            |              |             |            |       |         |        |           |             |
| **AdaptiveSlop**       | **1000**  | **100**        | **12,469.03 μs** |   **620.76 μs** |  **34.026 μs** |  **1.00** |    **0.00** |      **-** |  **35.13 KB** |        **1.00** |
| FSharpDataAdaptive | 1000  | 100        | 17,938.90 μs | 3,042.26 μs | 166.756 μs |  1.44 |    0.01 |      - |  46.09 KB |        1.31 |

### DeepWideBenchmarks

| Method             | Depth | BranchingFactor | Iterations | Mean         | Error        | StdDev     | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------- |------ |---------------- |----------- |-------------:|-------------:|-----------:|------:|--------:|-------:|----------:|------------:|
| **AdaptiveSlop**       | **3**     | **2**               | **50**         |     **31.78 μs** |    **42.400 μs** |   **2.324 μs** |  **1.00** |    **0.09** | **0.1831** |   **1.56 KB** |        **1.00** |
| FSharpDataAdaptive | 3     | 2               | 50         |     50.28 μs |     6.298 μs |   0.345 μs |  1.59 |    0.10 | 2.8076 |  23.05 KB |       14.75 |
|                    |       |                 |            |              |              |            |       |         |        |           |             |
| **AdaptiveSlop**       | **3**     | **3**               | **50**         |     **78.18 μs** |    **22.465 μs** |   **1.231 μs** |  **1.00** |    **0.02** | **0.1221** |   **1.56 KB** |        **1.00** |
| FSharpDataAdaptive | 3     | 3               | 50         |     86.57 μs |     6.856 μs |   0.376 μs |  1.11 |    0.02 | 2.8076 |  23.05 KB |       14.75 |
|                    |       |                 |            |              |              |            |       |         |        |           |             |
| **AdaptiveSlop**       | **3**     | **4**               | **50**         |    **140.98 μs** |    **38.880 μs** |   **2.131 μs** |  **1.00** |    **0.02** |      **-** |   **1.56 KB** |        **1.00** |
| FSharpDataAdaptive | 3     | 4               | 50         |    108.23 μs |     2.836 μs |   0.155 μs |  0.77 |    0.01 | 2.8076 |  23.05 KB |       14.75 |
|                    |       |                 |            |              |              |            |       |         |        |           |             |
| **AdaptiveSlop**       | **5**     | **2**               | **50**         |     **84.15 μs** |    **17.119 μs** |   **0.938 μs** |  **1.00** |    **0.01** | **0.1221** |   **1.56 KB** |        **1.00** |
| FSharpDataAdaptive | 5     | 2               | 50         |     72.72 μs |     6.729 μs |   0.369 μs |  0.86 |    0.01 | 2.8076 |  23.05 KB |       14.75 |
|                    |       |                 |            |              |              |            |       |         |        |           |             |
| **AdaptiveSlop**       | **5**     | **3**               | **50**         |    **438.31 μs** |    **64.160 μs** |   **3.517 μs** |  **1.00** |    **0.01** |      **-** |   **1.56 KB** |        **1.00** |
| FSharpDataAdaptive | 5     | 3               | 50         |    136.84 μs |     9.642 μs |   0.528 μs |  0.31 |    0.00 | 2.6855 |  23.05 KB |       14.75 |
|                    |       |                 |            |              |              |            |       |         |        |           |             |
| **AdaptiveSlop**       | **5**     | **4**               | **50**         |  **2,257.11 μs** |    **91.045 μs** |   **4.990 μs** |  **1.00** |    **0.00** |      **-** |   **1.56 KB** |        **1.00** |
| FSharpDataAdaptive | 5     | 4               | 50         |    169.13 μs |    55.287 μs |   3.030 μs |  0.07 |    0.00 | 2.6855 |  23.05 KB |       14.75 |
|                    |       |                 |            |              |              |            |       |         |        |           |             |
| **AdaptiveSlop**       | **7**     | **2**               | **50**         |    **325.16 μs** |    **18.016 μs** |   **0.988 μs** |  **1.00** |    **0.00** |      **-** |   **1.56 KB** |        **1.00** |
| FSharpDataAdaptive | 7     | 2               | 50         |     99.06 μs |    12.927 μs |   0.709 μs |  0.30 |    0.00 | 2.8076 |  23.05 KB |       14.75 |
|                    |       |                 |            |              |              |            |       |         |        |           |             |
| **AdaptiveSlop**       | **7**     | **3**               | **50**         |  **5,030.82 μs** |   **121.570 μs** |   **6.664 μs** |  **1.00** |    **0.00** |      **-** |   **1.56 KB** |        **1.00** |
| FSharpDataAdaptive | 7     | 3               | 50         |    190.18 μs |    28.463 μs |   1.560 μs |  0.04 |    0.00 | 2.6855 |  23.05 KB |       14.75 |
|                    |       |                 |            |              |              |            |       |         |        |           |             |
| **AdaptiveSlop**       | **7**     | **4**               | **50**         | **40,659.66 μs** | **8,545.159 μs** | **468.389 μs** | **1.000** |    **0.01** |      **-** |   **1.56 KB** |        **1.00** |
| FSharpDataAdaptive | 7     | 4               | 50         |    265.11 μs |    24.973 μs |   1.369 μs | 0.007 |    0.00 | 2.4414 |  23.05 KB |       14.75 |

### DiamondGraphBenchmarks

| Method             | Iterations | Mean       | Error     | StdDev  | Ratio | RatioSD | Gen0    | Allocated | Alloc Ratio |
|------------------- |----------- |-----------:|----------:|--------:|------:|--------:|--------:|----------:|------------:|
| AdaptiveSlop       | 1000       |   433.3 μs |  91.19 μs | 5.00 μs |  1.00 |    0.01 |  3.4180 | 287.25 KB |        1.00 |
| FSharpDataAdaptive | 1000       | 1,020.4 μs | 129.12 μs | 7.08 μs |  2.36 |    0.03 | 72.2656 | 601.56 KB |        2.09 |

### IncrementalChainBenchmarks

| Method             | InitialSize | Mutations | Mean     | Error     | StdDev  | Ratio | RatioSD | Gen0     | Allocated | Alloc Ratio |
|------------------- |------------ |---------- |---------:|----------:|--------:|------:|--------:|---------:|----------:|------------:|
| **AdaptiveSlop**       | **100**         | **200**       | **314.5 μs** |  **16.80 μs** | **0.92 μs** |  **1.00** |    **0.00** |  **25.3906** | **209.92 KB** |        **1.00** |
| FSharpDataAdaptive | 100         | 200       | 770.9 μs | 160.54 μs | 8.80 μs |  2.45 |    0.03 |  76.1719 | 623.73 KB |        2.97 |
|                    |             |           |          |           |         |       |         |          |           |             |
| **AdaptiveSlop**       | **1000**        | **200**       | **339.2 μs** |  **28.15 μs** | **1.54 μs** |  **1.00** |    **0.01** |  **34.1797** | **279.53 KB** |        **1.00** |
| FSharpDataAdaptive | 1000        | 200       | 767.0 μs | 154.26 μs | 8.46 μs |  2.26 |    0.02 |  78.1250 | 641.13 KB |        2.29 |
|                    |             |           |          |           |         |       |         |          |           |             |
| **AdaptiveSlop**       | **10000**       | **200**       | **397.1 μs** |  **70.77 μs** | **3.88 μs** |  **1.00** |    **0.01** |  **45.4102** | **372.34 KB** |        **1.00** |
| FSharpDataAdaptive | 10000       | 200       | 903.5 μs | 145.03 μs | 7.95 μs |  2.28 |    0.03 | 101.5625 | 832.75 KB |        2.24 |

### LargeCollectionBenchmarks

| Method             | InitialSize | Iterations | Mean      | Error     | StdDev   | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|------------------- |------------ |----------- |----------:|----------:|---------:|------:|--------:|--------:|-------:|----------:|------------:|
| AdaptiveSlop       | 10000       | 200        |  12.53 μs |  4.693 μs | 0.257 μs |  1.00 |    0.03 |  0.7629 |      - |   6.25 KB |        1.00 |
| FSharpDataAdaptive | 10000       | 200        | 124.49 μs | 94.920 μs | 5.203 μs |  9.94 |    0.40 | 28.0762 | 1.7090 | 229.94 KB |       36.79 |

### Map2Benchmarks

| Method             | Iterations | Mean     | Error     | StdDev  | Ratio | RatioSD | Gen0    | Allocated | Alloc Ratio |
|------------------- |----------- |---------:|----------:|--------:|------:|--------:|--------:|----------:|------------:|
| AdaptiveSlop       | 1000       | 336.0 μs |  11.31 μs | 0.62 μs |  1.00 |    0.00 |  7.3242 |  318.5 KB |        1.00 |
| FSharpDataAdaptive | 1000       | 664.4 μs | 109.38 μs | 6.00 μs |  1.98 |    0.02 | 62.5000 | 515.63 KB |        1.62 |

### MapBenchmarks

| Method             | Iterations | Mean     | Error     | StdDev   | Ratio | RatioSD | Gen0     | Allocated | Alloc Ratio |
|------------------- |----------- |---------:|----------:|---------:|------:|--------:|---------:|----------:|------------:|
| AdaptiveSlop       | 1000       | 236.5 μs |   5.81 μs |  0.32 μs |  1.00 |    0.00 |   7.3242 |   62.5 KB |        1.00 |
| FSharpDataAdaptive | 1000       | 456.3 μs | 643.46 μs | 35.27 μs |  1.93 |    0.13 | 104.9805 | 859.38 KB |       13.75 |

### MapTransformBenchmarks

| Method             | Iterations | Mean       | Error      | StdDev   | Ratio | RatioSD | Gen0     | Allocated  | Alloc Ratio |
|------------------- |----------- |-----------:|-----------:|---------:|------:|--------:|---------:|-----------:|------------:|
| AdaptiveSlop       | 500        |   857.4 μs |   357.8 μs | 19.61 μs |  1.00 |    0.03 |  88.8672 |  732.88 KB |        1.00 |
| FSharpDataAdaptive | 500        | 1,748.8 μs | 1,314.0 μs | 72.03 μs |  2.04 |    0.08 | 210.9375 | 1727.94 KB |        2.36 |

### OptimizedWideTreeBenchmarks

| Method                 | Width | Iterations | Mean        | Error         | StdDev     | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|----------------------- |------ |----------- |------------:|--------------:|-----------:|------:|--------:|-------:|----------:|------------:|
| **AdaptiveSlop_Map2Chain** | **10**    | **100**        |    **92.52 μs** |     **30.871 μs** |   **1.692 μs** |  **1.00** |    **0.02** | **0.3662** |   **3.13 KB** |        **1.00** |
| AdaptiveSlop_Reduce    | 10    | 100        |    49.71 μs |     39.826 μs |   2.183 μs |  0.54 |    0.02 | 0.3662 |   3.13 KB |        1.00 |
| FSharpDataAdaptive     | 10    | 100        |   146.54 μs |      4.553 μs |   0.250 μs |  1.58 |    0.02 | 5.6152 |  46.09 KB |       14.75 |
|                        |       |            |             |               |            |       |         |        |           |             |
| **AdaptiveSlop_Map2Chain** | **50**    | **100**        |   **517.82 μs** |    **152.761 μs** |   **8.373 μs** |  **1.00** |    **0.02** |      **-** |   **3.13 KB** |        **1.00** |
| AdaptiveSlop_Reduce    | 50    | 100        |   182.94 μs |     22.216 μs |   1.218 μs |  0.35 |    0.01 | 0.2441 |   3.13 KB |        1.00 |
| FSharpDataAdaptive     | 50    | 100        |   622.56 μs |    173.708 μs |   9.522 μs |  1.20 |    0.02 | 4.8828 |  46.09 KB |       14.75 |
|                        |       |            |             |               |            |       |         |        |           |             |
| **AdaptiveSlop_Map2Chain** | **100**   | **100**        | **1,079.14 μs** |    **955.741 μs** |  **52.387 μs** |  **1.00** |    **0.06** |      **-** |   **3.13 KB** |        **1.00** |
| AdaptiveSlop_Reduce    | 100   | 100        |   354.20 μs |     13.201 μs |   0.724 μs |  0.33 |    0.01 |      - |   3.13 KB |        1.00 |
| FSharpDataAdaptive     | 100   | 100        | 1,213.92 μs |    957.391 μs |  52.478 μs |  1.13 |    0.06 | 3.9063 |  46.09 KB |       14.75 |
|                        |       |            |             |               |            |       |         |        |           |             |
| **AdaptiveSlop_Map2Chain** | **500**   | **100**        | **5,619.55 μs** | **10,329.346 μs** | **566.186 μs** |  **1.01** |    **0.12** |      **-** |   **3.13 KB** |        **1.00** |
| AdaptiveSlop_Reduce    | 500   | 100        | 1,675.51 μs |     89.197 μs |   4.889 μs |  0.30 |    0.02 |      - |   3.13 KB |        1.00 |
| FSharpDataAdaptive     | 500   | 100        | 5,873.70 μs |  1,193.119 μs |  65.399 μs |  1.05 |    0.09 |      - |  46.09 KB |       14.75 |

### ReadHeavyBenchmarks

| Method             | WriteCount | ReadsPerWrite | Mean     | Error       | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------- |----------- |-------------- |---------:|------------:|----------:|------:|--------:|-------:|----------:|------------:|
| AdaptiveSlop       | 100        | 50            | 367.5 μs | 2,870.29 μs | 157.33 μs |  1.11 |    0.55 |      - |   3.13 KB |        1.00 |
| FSharpDataAdaptive | 100        | 50            | 189.8 μs |    41.02 μs |   2.25 μs |  0.57 |    0.18 | 5.6152 |  46.09 KB |       14.75 |

### SetBenchmarks

| Method             | Iterations | Mean     | Error    | StdDev  | Ratio | RatioSD | Gen0     | Allocated | Alloc Ratio |
|------------------- |----------- |---------:|---------:|--------:|------:|--------:|---------:|----------:|------------:|
| AdaptiveSlop       | 1000       | 311.9 μs | 72.10 μs | 3.95 μs |  1.00 |    0.02 |   7.3242 |   62.5 KB |        1.00 |
| FSharpDataAdaptive | 1000       | 499.4 μs | 55.85 μs | 3.06 μs |  1.60 |    0.02 | 105.4688 | 867.19 KB |       13.88 |

### SetTransformBenchmarks

| Method             | Iterations | Mean       | Error     | StdDev  | Ratio | Gen0     | Allocated  | Alloc Ratio |
|------------------- |----------- |-----------:|----------:|--------:|------:|---------:|-----------:|------------:|
| AdaptiveSlop       | 500        |   790.7 μs |  54.44 μs | 2.98 μs |  1.00 |  68.3594 |  558.43 KB |        1.00 |
| FSharpDataAdaptive | 500        | 1,878.6 μs | 156.46 μs | 8.58 μs |  2.38 | 185.5469 | 1524.86 KB |        2.73 |

### TransactionBenchmarks

| Method                     | ValueCount | Iterations | Mean     | Error     | StdDev    | Ratio | RatioSD | Gen0    | Allocated | Alloc Ratio |
|--------------------------- |----------- |----------- |---------:|----------:|----------:|------:|--------:|--------:|----------:|------------:|
| AdaptiveSlop_Batched       | 10         | 500        | 1.453 ms | 0.6780 ms | 0.0372 ms |  1.00 |    0.03 | 39.0625 | 456.13 KB |        1.00 |
| FSharpDataAdaptive_Batched | 10         | 500        | 2.643 ms | 0.0801 ms | 0.0044 ms |  1.82 |    0.04 | 85.9375 | 726.56 KB |        1.59 |

### UnbalancedTreeBenchmarks

| Method                     | DeepBranchDepth | ShallowBranchCount | Iterations | Mean        | Error        | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------------------------- |---------------- |------------------- |----------- |------------:|-------------:|----------:|------:|--------:|-------:|----------:|------------:|
| **AdaptiveSlop_DeepChange**    | **10**              | **5**                  | **50**         |   **120.45 μs** |    **31.820 μs** |  **1.744 μs** |  **1.00** |    **0.02** | **0.1221** |   **1.56 KB** |        **1.00** |
| FDA_DeepChange             | 10              | 5                  | 50         |   207.57 μs |     3.733 μs |  0.205 μs |  1.72 |    0.02 | 2.6855 |  23.05 KB |       14.75 |
| AdaptiveSlop_ShallowChange | 10              | 5                  | 50         |    80.89 μs |    33.422 μs |  1.832 μs |  0.67 |    0.02 | 0.1221 |   1.56 KB |        1.00 |
| FDA_ShallowChange          | 10              | 5                  | 50         |   104.44 μs |    73.888 μs |  4.050 μs |  0.87 |    0.03 | 2.8076 |  23.05 KB |       14.75 |
|                            |                 |                    |            |             |              |           |       |         |        |           |             |
| **AdaptiveSlop_DeepChange**    | **10**              | **20**                 | **50**         |   **286.46 μs** |    **30.854 μs** |  **1.691 μs** |  **1.00** |    **0.01** |      **-** |   **1.56 KB** |        **1.00** |
| FDA_DeepChange             | 10              | 20                 | 50         |   413.18 μs |    35.402 μs |  1.941 μs |  1.44 |    0.01 | 2.4414 |  23.05 KB |       14.75 |
| AdaptiveSlop_ShallowChange | 10              | 20                 | 50         |   228.77 μs |    15.449 μs |  0.847 μs |  0.80 |    0.00 |      - |   1.56 KB |        1.00 |
| FDA_ShallowChange          | 10              | 20                 | 50         |   301.37 μs |   101.712 μs |  5.575 μs |  1.05 |    0.02 | 2.4414 |  23.05 KB |       14.75 |
|                            |                 |                    |            |             |              |           |       |         |        |           |             |
| **AdaptiveSlop_DeepChange**    | **10**              | **50**                 | **50**         |   **596.07 μs** |   **532.232 μs** | **29.173 μs** |  **1.00** |    **0.06** |      **-** |   **1.56 KB** |        **1.00** |
| FDA_DeepChange             | 10              | 50                 | 50         |   828.29 μs |    19.570 μs |  1.073 μs |  1.39 |    0.06 | 1.9531 |  23.05 KB |       14.75 |
| AdaptiveSlop_ShallowChange | 10              | 50                 | 50         |   547.03 μs |   134.649 μs |  7.381 μs |  0.92 |    0.04 |      - |   1.56 KB |        1.00 |
| FDA_ShallowChange          | 10              | 50                 | 50         |   751.48 μs |    15.956 μs |  0.875 μs |  1.26 |    0.05 | 1.9531 |  23.05 KB |       14.75 |
|                            |                 |                    |            |             |              |           |       |         |        |           |             |
| **AdaptiveSlop_DeepChange**    | **50**              | **5**                  | **50**         |   **472.11 μs** |   **373.143 μs** | **20.453 μs** |  **1.00** |    **0.05** |      **-** |   **1.56 KB** |        **1.00** |
| FDA_DeepChange             | 50              | 5                  | 50         |   690.97 μs |   237.653 μs | 13.027 μs |  1.47 |    0.06 | 1.9531 |  23.05 KB |       14.75 |
| AdaptiveSlop_ShallowChange | 50              | 5                  | 50         |   198.65 μs |    32.172 μs |  1.763 μs |  0.42 |    0.02 |      - |   1.56 KB |        1.00 |
| FDA_ShallowChange          | 50              | 5                  | 50         |   112.00 μs |     1.851 μs |  0.101 μs |  0.24 |    0.01 | 2.8076 |  23.05 KB |       14.75 |
|                            |                 |                    |            |             |              |           |       |         |        |           |             |
| **AdaptiveSlop_DeepChange**    | **50**              | **20**                 | **50**         |   **691.93 μs** |    **42.700 μs** |  **2.341 μs** |  **1.00** |    **0.00** |      **-** |   **1.56 KB** |        **1.00** |
| FDA_DeepChange             | 50              | 20                 | 50         |   980.60 μs |    16.813 μs |  0.922 μs |  1.42 |    0.00 | 1.9531 |  23.05 KB |       14.75 |
| AdaptiveSlop_ShallowChange | 50              | 20                 | 50         |   382.98 μs |   237.906 μs | 13.040 μs |  0.55 |    0.02 |      - |   1.56 KB |        1.00 |
| FDA_ShallowChange          | 50              | 20                 | 50         |   368.63 μs |   242.305 μs | 13.282 μs |  0.53 |    0.02 | 2.4414 |  23.05 KB |       14.75 |
|                            |                 |                    |            |             |              |           |       |         |        |           |             |
| **AdaptiveSlop_DeepChange**    | **50**              | **50**                 | **50**         | **1,096.84 μs** | **1,000.787 μs** | **54.857 μs** |  **1.00** |    **0.06** |      **-** |   **1.56 KB** |        **1.00** |
| FDA_DeepChange             | 50              | 50                 | 50         | 1,495.43 μs |    38.535 μs |  2.112 μs |  1.37 |    0.06 | 1.9531 |  23.05 KB |       14.75 |
| AdaptiveSlop_ShallowChange | 50              | 50                 | 50         |   718.54 μs |    40.149 μs |  2.201 μs |  0.66 |    0.03 |      - |   1.56 KB |        1.00 |
| FDA_ShallowChange          | 50              | 50                 | 50         |   847.71 μs |   471.440 μs | 25.841 μs |  0.77 |    0.04 | 1.9531 |  23.05 KB |       14.75 |
|                            |                 |                    |            |             |              |           |       |         |        |           |             |
| **AdaptiveSlop_DeepChange**    | **100**             | **5**                  | **50**         |   **965.59 μs** |    **40.776 μs** |  **2.235 μs** |  **1.00** |    **0.00** |      **-** |   **1.56 KB** |        **1.00** |
| FDA_DeepChange             | 100             | 5                  | 50         | 1,252.11 μs |    33.282 μs |  1.824 μs |  1.30 |    0.00 | 1.9531 |  23.05 KB |       14.75 |
| AdaptiveSlop_ShallowChange | 100             | 5                  | 50         |   337.78 μs |    32.145 μs |  1.762 μs |  0.35 |    0.00 |      - |   1.56 KB |        1.00 |
| FDA_ShallowChange          | 100             | 5                  | 50         |   112.02 μs |     1.792 μs |  0.098 μs |  0.12 |    0.00 | 2.8076 |  23.05 KB |       14.75 |
|                            |                 |                    |            |             |              |           |       |         |        |           |             |
| **AdaptiveSlop_DeepChange**    | **100**             | **20**                 | **50**         | **1,152.35 μs** |    **34.459 μs** |  **1.889 μs** |  **1.00** |    **0.00** |      **-** |   **1.56 KB** |        **1.00** |
| FDA_DeepChange             | 100             | 20                 | 50         | 1,558.95 μs |   906.669 μs | 49.698 μs |  1.35 |    0.04 | 1.9531 |  23.05 KB |       14.75 |
| AdaptiveSlop_ShallowChange | 100             | 20                 | 50         |   511.39 μs |    33.860 μs |  1.856 μs |  0.44 |    0.00 |      - |   1.56 KB |        1.00 |
| FDA_ShallowChange          | 100             | 20                 | 50         |   356.59 μs |     3.027 μs |  0.166 μs |  0.31 |    0.00 | 2.4414 |  23.05 KB |       14.75 |
|                            |                 |                    |            |             |              |           |       |         |        |           |             |
| **AdaptiveSlop_DeepChange**    | **100**             | **50**                 | **50**         | **1,508.22 μs** |    **76.897 μs** |  **4.215 μs** |  **1.00** |    **0.00** |      **-** |   **1.56 KB** |        **1.00** |
| FDA_DeepChange             | 100             | 50                 | 50         | 2,074.41 μs |    46.807 μs |  2.566 μs |  1.38 |    0.00 |      - |  23.05 KB |       14.75 |
| AdaptiveSlop_ShallowChange | 100             | 50                 | 50         |   867.45 μs |    22.558 μs |  1.236 μs |  0.58 |    0.00 |      - |   1.56 KB |        1.00 |
| FDA_ShallowChange          | 100             | 50                 | 50         |   816.16 μs |     5.074 μs |  0.278 μs |  0.54 |    0.00 | 1.9531 |  23.05 KB |       14.75 |

### ValueBenchmarks

| Method             | Iterations | Mean     | Error    | StdDev   | Ratio | RatioSD | Gen0    | Allocated | Alloc Ratio |
|------------------- |----------- |---------:|---------:|---------:|------:|--------:|--------:|----------:|------------:|
| AdaptiveSlop       | 1000       | 291.5 μs | 223.8 μs | 12.27 μs |  1.00 |    0.05 |  3.4180 | 287.25 KB |        1.00 |
| FSharpDataAdaptive | 1000       | 621.6 μs | 477.6 μs | 26.18 μs |  2.13 |    0.11 | 55.6641 | 460.94 KB |        1.60 |

### WideTreeBenchmarks

| Method             | Width | Iterations | Mean       | Error       | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------- |------ |----------- |-----------:|------------:|----------:|------:|--------:|-------:|----------:|------------:|
| **AdaptiveSlop**       | **10**    | **100**        |   **143.4 μs** |   **215.72 μs** |  **11.82 μs** |  **1.00** |    **0.10** | **0.2441** |   **3.13 KB** |        **1.00** |
| FSharpDataAdaptive | 10    | 100        |   224.5 μs |     3.31 μs |   0.18 μs |  1.57 |    0.11 | 5.6152 |  46.09 KB |       14.75 |
|                    |       |            |            |             |           |       |         |        |           |             |
| **AdaptiveSlop**       | **50**    | **100**        |   **786.0 μs** |    **68.45 μs** |   **3.75 μs** |  **1.00** |    **0.01** |      **-** |   **3.13 KB** |        **1.00** |
| FSharpDataAdaptive | 50    | 100        |   937.1 μs |    16.37 μs |   0.90 μs |  1.19 |    0.01 | 4.8828 |  46.09 KB |       14.75 |
|                    |       |            |            |             |           |       |         |        |           |             |
| **AdaptiveSlop**       | **100**   | **100**        | **1,626.9 μs** | **1,075.66 μs** |  **58.96 μs** |  **1.00** |    **0.04** |      **-** |   **3.13 KB** |        **1.00** |
| FSharpDataAdaptive | 100   | 100        | 1,836.3 μs |   111.05 μs |   6.09 μs |  1.13 |    0.03 | 3.9063 |  46.09 KB |       14.75 |
|                    |       |            |            |             |           |       |         |        |           |             |
| **AdaptiveSlop**       | **500**   | **100**        | **8,571.6 μs** |   **596.20 μs** |  **32.68 μs** |  **1.00** |    **0.00** |      **-** |  **35.13 KB** |        **1.00** |
| FSharpDataAdaptive | 500   | 100        | 9,739.8 μs | 8,151.94 μs | 446.84 μs |  1.14 |    0.05 |      - |  46.09 KB |        1.31 |


---

## 2026-08-03 — Post-Phase-4 (push-mark, pull-evaluate)

- Branch: core-redesign. Core rebuilt per docs/PLAN.md Phases 0-4:
  no locks, owner-thread confinement, edge protocol, push-marking, transaction
  coalescing, observation.
- Machine and job: same as the Phase 0 baseline.
- Note: ConcurrentBenchmarks still exercises the old multi-thread model. The core
  no longer supports that model; treat its numbers as void until Phase 5 (Post/Pump).

### BindBenchmarks

| Method             | Iterations | Mean      | Error      | StdDev    | Ratio | RatioSD | Gen0    | Allocated | Alloc Ratio |
|------------------- |----------- |----------:|-----------:|----------:|------:|--------:|--------:|----------:|------------:|
| AdaptiveSlop       | 1000       |  82.51 μs |   8.658 μs |  0.475 μs |  1.00 |    0.01 |       - |         - |          NA |
| FSharpDataAdaptive | 1000       | 739.68 μs | 709.837 μs | 38.909 μs |  8.97 |    0.41 | 69.3359 |  584000 B |          NA |

### ConcurrentBenchmarks

| Method             | ThreadCount | IterationsPerThread | Mean     | Error    | StdDev  | Ratio | RatioSD | Gen0     | Allocated | Alloc Ratio |
|------------------- |------------ |-------------------- |---------:|---------:|--------:|------:|--------:|---------:|----------:|------------:|
| AdaptiveSlop       | 4           | 500                 |       NA |       NA |      NA |     ? |       ? |       NA |        NA |           ? |
| FSharpDataAdaptive | 4           | 500                 | 763.5 μs | 39.05 μs | 2.14 μs |     ? |       ? | 112.3047 |  922.7 KB |           ? |

### DeepChainBenchmarks

| Method             | Depth | Iterations | Mean         | Error        | StdDev     | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------- |------ |----------- |-------------:|-------------:|-----------:|------:|--------:|-------:|----------:|------------:|
| **AdaptiveSlop**       | **5**     | **100**        |     **35.11 μs** |     **2.102 μs** |   **0.115 μs** |  **1.00** |    **0.00** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 5     | 100        |    107.65 μs |    95.469 μs |   5.233 μs |  3.07 |    0.13 | 5.6152 |   47200 B |          NA |
|                    |       |            |              |              |            |       |         |        |           |             |
| **AdaptiveSlop**       | **10**    | **100**        |     **74.26 μs** |    **33.087 μs** |   **1.814 μs** |  **1.00** |    **0.03** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 10    | 100        |    182.88 μs |    11.311 μs |   0.620 μs |  2.46 |    0.05 | 5.6152 |   47200 B |          NA |
|                    |       |            |              |              |            |       |         |        |           |             |
| **AdaptiveSlop**       | **20**    | **100**        |    **161.41 μs** |     **2.355 μs** |   **0.129 μs** |  **1.00** |    **0.00** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 20    | 100        |    339.47 μs |   208.456 μs |  11.426 μs |  2.10 |    0.06 | 5.3711 |   47200 B |          NA |
|                    |       |            |              |              |            |       |         |        |           |             |
| **AdaptiveSlop**       | **100**   | **100**        |  **1,043.44 μs** |   **583.141 μs** |  **31.964 μs** |  **1.00** |    **0.04** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 100   | 100        |  1,768.78 μs |   846.425 μs |  46.395 μs |  1.70 |    0.06 | 3.9063 |   47200 B |          NA |
|                    |       |            |              |              |            |       |         |        |           |             |
| **AdaptiveSlop**       | **1000**  | **100**        |  **9,923.58 μs** | **1,415.095 μs** |  **77.566 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 1000  | 100        | 16,053.04 μs | 1,855.103 μs | 101.684 μs |  1.62 |    0.01 |      - |   47200 B |          NA |

### DeepWideBenchmarks

| Method             | Depth | BranchingFactor | Iterations | Mean         | Error        | StdDev     | Ratio | RatioSD | Gen0   | Gen1   | Gen2   | Allocated | Alloc Ratio |
|------------------- |------ |---------------- |----------- |-------------:|-------------:|-----------:|------:|--------:|-------:|-------:|-------:|----------:|------------:|
| **AdaptiveSlop**       | **3**     | **2**               | **50**         |     **13.17 μs** |     **1.313 μs** |   **0.072 μs** |  **1.00** |    **0.01** |      **-** |      **-** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 3     | 2               | 50         |     45.00 μs |     3.749 μs |   0.206 μs |  3.42 |    0.02 | 2.8076 |      - |      - |   23600 B |          NA |
|                    |       |                 |            |              |              |            |       |         |        |        |        |           |             |
| **AdaptiveSlop**       | **3**     | **3**               | **50**         |     **36.44 μs** |     **2.364 μs** |   **0.130 μs** |  **1.00** |    **0.00** |      **-** |      **-** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 3     | 3               | 50         |     75.84 μs |     7.816 μs |   0.428 μs |  2.08 |    0.01 | 2.8076 |      - |      - |   23600 B |          NA |
|                    |       |                 |            |              |              |            |       |         |        |        |        |           |             |
| **AdaptiveSlop**       | **3**     | **4**               | **50**         |     **68.50 μs** |     **3.626 μs** |   **0.199 μs** |  **1.00** |    **0.00** |      **-** |      **-** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 3     | 4               | 50         |    105.41 μs |    29.186 μs |   1.600 μs |  1.54 |    0.02 | 2.8076 |      - |      - |   23600 B |          NA |
|                    |       |                 |            |              |              |            |       |         |        |        |        |           |             |
| **AdaptiveSlop**       | **5**     | **2**               | **50**         |     **34.79 μs** |    **35.261 μs** |   **1.933 μs** |  **1.00** |    **0.07** |      **-** |      **-** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 5     | 2               | 50         |     67.82 μs |     9.119 μs |   0.500 μs |  1.95 |    0.09 | 2.8076 |      - |      - |   23600 B |          NA |
|                    |       |                 |            |              |              |            |       |         |        |        |        |           |             |
| **AdaptiveSlop**       | **5**     | **3**               | **50**         |    **215.03 μs** |   **175.837 μs** |   **9.638 μs** |  **1.00** |    **0.05** |      **-** |      **-** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 5     | 3               | 50         |    122.02 μs |    61.471 μs |   3.369 μs |  0.57 |    0.03 | 2.6855 |      - |      - |   23600 B |          NA |
|                    |       |                 |            |              |              |            |       |         |        |        |        |           |             |
| **AdaptiveSlop**       | **5**     | **4**               | **50**         |  **1,151.20 μs** |   **211.314 μs** |  **11.583 μs** |  **1.00** |    **0.01** |      **-** |      **-** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 5     | 4               | 50         |    161.20 μs |    59.068 μs |   3.238 μs |  0.14 |    0.00 | 2.6855 |      - |      - |   23600 B |          NA |
|                    |       |                 |            |              |              |            |       |         |        |        |        |           |             |
| **AdaptiveSlop**       | **7**     | **2**               | **50**         |    **145.71 μs** |   **146.155 μs** |   **8.011 μs** |  **1.00** |    **0.07** |      **-** |      **-** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 7     | 2               | 50         |     87.05 μs |    14.344 μs |   0.786 μs |  0.60 |    0.03 | 2.8076 |      - |      - |   23600 B |          NA |
|                    |       |                 |            |              |              |            |       |         |        |        |        |           |             |
| **AdaptiveSlop**       | **7**     | **3**               | **50**         |  **2,468.37 μs** | **4,670.618 μs** | **256.012 μs** |  **1.01** |    **0.12** |      **-** |      **-** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 7     | 3               | 50         |    168.16 μs |    30.716 μs |   1.684 μs |  0.07 |    0.01 | 2.6855 |      - |      - |   23600 B |          NA |
|                    |       |                 |            |              |              |            |       |         |        |        |        |           |             |
| **AdaptiveSlop**       | **7**     | **4**               | **50**         | **23,156.76 μs** | **8,445.975 μs** | **462.952 μs** | **1.000** |    **0.02** |      **-** |      **-** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 7     | 4               | 50         |    225.52 μs |    10.491 μs |   0.575 μs | 0.010 |    0.00 | 2.9297 | 0.7324 | 0.2441 |   23600 B |          NA |

### DiamondGraphBenchmarks

| Method             | Iterations | Mean     | Error    | StdDev  | Ratio | RatioSD | Gen0    | Allocated | Alloc Ratio |
|------------------- |----------- |---------:|---------:|--------:|------:|--------:|--------:|----------:|------------:|
| AdaptiveSlop       | 1000       | 167.0 μs |  9.27 μs | 0.51 μs |  1.00 |    0.00 |       - |         - |          NA |
| FSharpDataAdaptive | 1000       | 920.6 μs | 62.47 μs | 3.42 μs |  5.51 |    0.02 | 73.2422 |  616000 B |          NA |

### IncrementalChainBenchmarks

| Method             | InitialSize | Mutations | Mean     | Error     | StdDev   | Ratio | RatioSD | Gen0     | Allocated | Alloc Ratio |
|------------------- |------------ |---------- |---------:|----------:|---------:|------:|--------:|---------:|----------:|------------:|
| **AdaptiveSlop**       | **100**         | **200**       | **242.9 μs** |  **53.25 μs** |  **2.92 μs** |  **1.00** |    **0.01** |  **23.9258** | **197.42 KB** |        **1.00** |
| FSharpDataAdaptive | 100         | 200       | 682.2 μs | 326.45 μs | 17.89 μs |  2.81 |    0.07 |  76.1719 | 623.73 KB |        3.16 |
|                    |             |           |          |           |          |       |         |          |           |             |
| **AdaptiveSlop**       | **1000**        | **200**       | **290.8 μs** | **453.07 μs** | **24.83 μs** |  **1.00** |    **0.10** |  **32.2266** | **267.03 KB** |        **1.00** |
| FSharpDataAdaptive | 1000        | 200       | 678.4 μs | 169.46 μs |  9.29 μs |  2.34 |    0.17 |  78.1250 | 641.13 KB |        2.40 |
|                    |             |           |          |           |          |       |         |          |           |             |
| **AdaptiveSlop**       | **10000**       | **200**       | **329.9 μs** |  **58.87 μs** |  **3.23 μs** |  **1.00** |    **0.01** |  **43.9453** | **359.84 KB** |        **1.00** |
| FSharpDataAdaptive | 10000       | 200       | 802.4 μs | 429.07 μs | 23.52 μs |  2.43 |    0.07 | 101.5625 | 832.75 KB |        2.31 |

### LargeCollectionBenchmarks

| Method             | InitialSize | Iterations | Mean       | Error      | StdDev    | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|------------------- |------------ |----------- |-----------:|-----------:|----------:|------:|--------:|--------:|-------:|----------:|------------:|
| AdaptiveSlop       | 10000       | 200        |   2.219 μs |  0.6778 μs | 0.0372 μs |  1.00 |    0.02 |       - |      - |         - |          NA |
| FSharpDataAdaptive | 10000       | 200        | 111.465 μs | 42.6052 μs | 2.3353 μs | 50.23 |    1.17 | 28.0762 | 1.7090 |  235456 B |          NA |

### Map2Benchmarks

| Method             | Iterations | Mean      | Error      | StdDev    | Ratio | RatioSD | Gen0    | Allocated | Alloc Ratio |
|------------------- |----------- |----------:|-----------:|----------:|------:|--------:|--------:|----------:|------------:|
| AdaptiveSlop       | 1000       |  71.11 μs |   8.527 μs |  0.467 μs |  1.00 |    0.01 |       - |         - |          NA |
| FSharpDataAdaptive | 1000       | 659.07 μs | 459.833 μs | 25.205 μs |  9.27 |    0.31 | 62.5000 |  528000 B |          NA |

### MapBenchmarks

| Method             | Iterations | Mean     | Error     | StdDev   | Ratio | RatioSD | Gen0     | Allocated | Alloc Ratio |
|------------------- |----------- |---------:|----------:|---------:|------:|--------:|---------:|----------:|------------:|
| AdaptiveSlop       | 1000       | 116.8 μs |  19.42 μs |  1.06 μs |  1.00 |    0.01 |        - |         - |          NA |
| FSharpDataAdaptive | 1000       | 469.4 μs | 302.62 μs | 16.59 μs |  4.02 |    0.13 | 104.9805 |  880000 B |          NA |

### MapTransformBenchmarks

| Method             | Iterations | Mean       | Error    | StdDev   | Ratio | RatioSD | Gen0     | Allocated  | Alloc Ratio |
|------------------- |----------- |-----------:|---------:|---------:|------:|--------:|---------:|-----------:|------------:|
| AdaptiveSlop       | 500        |   653.6 μs | 252.9 μs | 13.86 μs |  1.00 |    0.03 |  84.9609 |  701.63 KB |        1.00 |
| FSharpDataAdaptive | 500        | 1,584.7 μs | 135.8 μs |  7.45 μs |  2.43 |    0.05 | 210.9375 | 1727.94 KB |        2.46 |

### OptimizedWideTreeBenchmarks

| Method                 | Width | Iterations | Mean        | Error        | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|----------------------- |------ |----------- |------------:|-------------:|----------:|------:|--------:|-------:|----------:|------------:|
| **AdaptiveSlop_Map2Chain** | **10**    | **100**        |    **45.33 μs** |     **8.012 μs** |  **0.439 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| AdaptiveSlop_Reduce    | 10    | 100        |    13.59 μs |    17.176 μs |  0.941 μs |  0.30 |    0.02 |      - |         - |          NA |
| FSharpDataAdaptive     | 10    | 100        |   139.19 μs |    84.708 μs |  4.643 μs |  3.07 |    0.09 | 5.6152 |   47200 B |          NA |
|                        |       |            |             |              |           |       |         |        |           |             |
| **AdaptiveSlop_Map2Chain** | **50**    | **100**        |   **313.64 μs** |    **39.006 μs** |  **2.138 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| AdaptiveSlop_Reduce    | 50    | 100        |    52.34 μs |    38.738 μs |  2.123 μs |  0.17 |    0.01 |      - |         - |          NA |
| FSharpDataAdaptive     | 50    | 100        |   628.02 μs |   396.881 μs | 21.754 μs |  2.00 |    0.06 | 4.8828 |   47200 B |          NA |
|                        |       |            |             |              |           |       |         |        |           |             |
| **AdaptiveSlop_Map2Chain** | **100**   | **100**        |   **718.38 μs** |   **197.649 μs** | **10.834 μs** |  **1.00** |    **0.02** |      **-** |         **-** |          **NA** |
| AdaptiveSlop_Reduce    | 100   | 100        |   113.80 μs |    72.554 μs |  3.977 μs |  0.16 |    0.01 |      - |         - |          NA |
| FSharpDataAdaptive     | 100   | 100        | 1,218.66 μs |   114.816 μs |  6.293 μs |  1.70 |    0.02 | 3.9063 |   47200 B |          NA |
|                        |       |            |             |              |           |       |         |        |           |             |
| **AdaptiveSlop_Map2Chain** | **500**   | **100**        | **4,814.18 μs** | **1,037.049 μs** | **56.844 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| AdaptiveSlop_Reduce    | 500   | 100        |   460.85 μs |   102.543 μs |  5.621 μs |  0.10 |    0.00 |      - |         - |          NA |
| FSharpDataAdaptive     | 500   | 100        | 5,413.10 μs |   791.776 μs | 43.400 μs |  1.12 |    0.01 |      - |   47200 B |          NA |

### ReadHeavyBenchmarks

| Method             | WriteCount | ReadsPerWrite | Mean      | Error      | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------- |----------- |-------------- |----------:|-----------:|---------:|------:|--------:|-------:|----------:|------------:|
| AdaptiveSlop       | 100        | 50            |  71.00 μs |   2.616 μs | 0.143 μs |  1.00 |    0.00 |      - |         - |          NA |
| FSharpDataAdaptive | 100        | 50            | 162.77 μs | 162.112 μs | 8.886 μs |  2.29 |    0.11 | 5.6152 |   47200 B |          NA |

### SetBenchmarks

| Method             | Iterations | Mean     | Error     | StdDev   | Ratio | RatioSD | Gen0     | Allocated | Alloc Ratio |
|------------------- |----------- |---------:|----------:|---------:|------:|--------:|---------:|----------:|------------:|
| AdaptiveSlop       | 1000       | 159.6 μs |  33.52 μs |  1.84 μs |  1.00 |    0.01 |        - |         - |          NA |
| FSharpDataAdaptive | 1000       | 452.1 μs | 479.23 μs | 26.27 μs |  2.83 |    0.15 | 105.9570 |  888000 B |          NA |

### SetTransformBenchmarks

| Method             | Iterations | Mean       | Error    | StdDev   | Ratio | RatioSD | Gen0     | Allocated  | Alloc Ratio |
|------------------- |----------- |-----------:|---------:|---------:|------:|--------:|---------:|-----------:|------------:|
| AdaptiveSlop       | 500        |   582.6 μs | 279.9 μs | 15.34 μs |  1.00 |    0.03 |  64.4531 |  527.18 KB |        1.00 |
| FSharpDataAdaptive | 500        | 1,532.2 μs | 122.4 μs |  6.71 μs |  2.63 |    0.06 | 185.5469 | 1524.86 KB |        2.89 |

### TransactionBenchmarks

| Method                     | ValueCount | Iterations | Mean       | Error      | StdDev    | Ratio | RatioSD | Gen0    | Allocated | Alloc Ratio |
|--------------------------- |----------- |----------- |-----------:|-----------:|----------:|------:|--------:|--------:|----------:|------------:|
| AdaptiveSlop_Batched       | 10         | 500        |   496.1 μs |   762.3 μs |  41.78 μs |  1.00 |    0.10 |  0.9766 |  15.63 KB |        1.00 |
| FSharpDataAdaptive_Batched | 10         | 500        | 2,624.3 μs | 2,157.3 μs | 118.25 μs |  5.31 |    0.42 | 85.9375 | 726.56 KB |       46.50 |

### UnbalancedTreeBenchmarks

| Method                     | DeepBranchDepth | ShallowBranchCount | Iterations | Mean        | Error      | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------------------------- |---------------- |------------------- |----------- |------------:|-----------:|----------:|------:|--------:|-------:|----------:|------------:|
| **AdaptiveSlop_DeepChange**    | **10**              | **5**                  | **50**         |    **72.69 μs** |  **50.284 μs** |  **2.756 μs** |  **1.00** |    **0.05** |      **-** |         **-** |          **NA** |
| FDA_DeepChange             | 10              | 5                  | 50         |   151.99 μs |  34.196 μs |  1.874 μs |  2.09 |    0.07 | 2.6855 |   23600 B |          NA |
| AdaptiveSlop_ShallowChange | 10              | 5                  | 50         |    33.65 μs |  20.860 μs |  1.143 μs |  0.46 |    0.02 |      - |         - |          NA |
| FDA_ShallowChange          | 10              | 5                  | 50         |    87.97 μs |  51.199 μs |  2.806 μs |  1.21 |    0.05 | 2.8076 |   23600 B |          NA |
|                            |                 |                    |            |             |            |           |       |         |        |           |             |
| **AdaptiveSlop_DeepChange**    | **10**              | **20**                 | **50**         |   **179.97 μs** |  **29.717 μs** |  **1.629 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FDA_DeepChange             | 10              | 20                 | 50         |   313.06 μs |  15.649 μs |  0.858 μs |  1.74 |    0.01 | 2.4414 |   23600 B |          NA |
| AdaptiveSlop_ShallowChange | 10              | 20                 | 50         |   122.30 μs |  20.980 μs |  1.150 μs |  0.68 |    0.01 |      - |         - |          NA |
| FDA_ShallowChange          | 10              | 20                 | 50         |   236.57 μs | 352.966 μs | 19.347 μs |  1.31 |    0.09 | 2.4414 |   23600 B |          NA |
|                            |                 |                    |            |             |            |           |       |         |        |           |             |
| **AdaptiveSlop_DeepChange**    | **10**              | **50**                 | **50**         |   **372.58 μs** |  **89.450 μs** |  **4.903 μs** |  **1.00** |    **0.02** |      **-** |         **-** |          **NA** |
| FDA_DeepChange             | 10              | 50                 | 50         |   731.75 μs | 530.663 μs | 29.087 μs |  1.96 |    0.07 | 1.9531 |   23600 B |          NA |
| AdaptiveSlop_ShallowChange | 10              | 50                 | 50         |   352.05 μs |  90.208 μs |  4.945 μs |  0.95 |    0.02 |      - |         - |          NA |
| FDA_ShallowChange          | 10              | 50                 | 50         |   621.50 μs |  40.431 μs |  2.216 μs |  1.67 |    0.02 | 1.9531 |   23600 B |          NA |
|                            |                 |                    |            |             |            |           |       |         |        |           |             |
| **AdaptiveSlop_DeepChange**    | **50**              | **5**                  | **50**         |   **281.10 μs** |  **13.947 μs** |  **0.765 μs** |  **1.00** |    **0.00** |      **-** |         **-** |          **NA** |
| FDA_DeepChange             | 50              | 5                  | 50         |   512.41 μs |  10.853 μs |  0.595 μs |  1.82 |    0.00 | 1.9531 |   23600 B |          NA |
| AdaptiveSlop_ShallowChange | 50              | 5                  | 50         |    85.27 μs |   1.441 μs |  0.079 μs |  0.30 |    0.00 |      - |         - |          NA |
| FDA_ShallowChange          | 50              | 5                  | 50         |    70.58 μs |   7.248 μs |  0.397 μs |  0.25 |    0.00 | 2.8076 |   23600 B |          NA |
|                            |                 |                    |            |             |            |           |       |         |        |           |             |
| **AdaptiveSlop_DeepChange**    | **50**              | **20**                 | **50**         |   **325.79 μs** |   **8.259 μs** |  **0.453 μs** |  **1.00** |    **0.00** |      **-** |         **-** |          **NA** |
| FDA_DeepChange             | 50              | 20                 | 50         |   622.39 μs | 204.429 μs | 11.205 μs |  1.91 |    0.03 | 1.9531 |   23600 B |          NA |
| AdaptiveSlop_ShallowChange | 50              | 20                 | 50         |   153.03 μs | 112.626 μs |  6.173 μs |  0.47 |    0.02 |      - |         - |          NA |
| FDA_ShallowChange          | 50              | 20                 | 50         |   262.79 μs |  57.690 μs |  3.162 μs |  0.81 |    0.01 | 2.4414 |   23600 B |          NA |
|                            |                 |                    |            |             |            |           |       |         |        |           |             |
| **AdaptiveSlop_DeepChange**    | **50**              | **50**                 | **50**         |   **508.51 μs** |  **54.045 μs** |  **2.962 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FDA_DeepChange             | 50              | 50                 | 50         |   988.96 μs | 655.927 μs | 35.954 μs |  1.94 |    0.06 | 1.9531 |   23600 B |          NA |
| AdaptiveSlop_ShallowChange | 50              | 50                 | 50         |   387.62 μs | 146.200 μs |  8.014 μs |  0.76 |    0.01 |      - |         - |          NA |
| FDA_ShallowChange          | 50              | 50                 | 50         |   588.95 μs | 717.199 μs | 39.312 μs |  1.16 |    0.07 | 1.9531 |   23600 B |          NA |
|                            |                 |                    |            |             |            |           |       |         |        |           |             |
| **AdaptiveSlop_DeepChange**    | **100**             | **5**                  | **50**         |   **483.05 μs** | **567.041 μs** | **31.081 μs** |  **1.00** |    **0.08** |      **-** |         **-** |          **NA** |
| FDA_DeepChange             | 100             | 5                  | 50         |   818.80 μs |  97.138 μs |  5.324 μs |  1.70 |    0.10 | 1.9531 |   23600 B |          NA |
| AdaptiveSlop_ShallowChange | 100             | 5                  | 50         |   148.15 μs |  21.580 μs |  1.183 μs |  0.31 |    0.02 |      - |         - |          NA |
| FDA_ShallowChange          | 100             | 5                  | 50         |    74.19 μs |  28.196 μs |  1.546 μs |  0.15 |    0.01 | 2.8076 |   23600 B |          NA |
|                            |                 |                    |            |             |            |           |       |         |        |           |             |
| **AdaptiveSlop_DeepChange**    | **100**             | **20**                 | **50**         |   **653.27 μs** | **152.272 μs** |  **8.347 μs** |  **1.00** |    **0.02** |      **-** |         **-** |          **NA** |
| FDA_DeepChange             | 100             | 20                 | 50         | 1,035.64 μs |  57.529 μs |  3.153 μs |  1.59 |    0.02 | 1.9531 |   23600 B |          NA |
| AdaptiveSlop_ShallowChange | 100             | 20                 | 50         |   224.23 μs | 457.735 μs | 25.090 μs |  0.34 |    0.03 |      - |         - |          NA |
| FDA_ShallowChange          | 100             | 20                 | 50         |   251.97 μs | 275.707 μs | 15.112 μs |  0.39 |    0.02 | 2.6855 |   23600 B |          NA |
|                            |                 |                    |            |             |            |           |       |         |        |           |             |
| **AdaptiveSlop_DeepChange**    | **100**             | **50**                 | **50**         |   **743.56 μs** | **183.105 μs** | **10.037 μs** |  **1.00** |    **0.02** |      **-** |         **-** |          **NA** |
| FDA_DeepChange             | 100             | 50                 | 50         | 1,383.19 μs | 111.821 μs |  6.129 μs |  1.86 |    0.02 | 1.9531 |   23600 B |          NA |
| AdaptiveSlop_ShallowChange | 100             | 50                 | 50         |   395.19 μs | 783.617 μs | 42.953 μs |  0.53 |    0.05 |      - |         - |          NA |
| FDA_ShallowChange          | 100             | 50                 | 50         |   540.73 μs | 172.336 μs |  9.446 μs |  0.73 |    0.01 | 1.9531 |   23600 B |          NA |

### ValueBenchmarks

| Method             | Iterations | Mean      | Error     | StdDev   | Ratio | RatioSD | Gen0    | Allocated | Alloc Ratio |
|------------------- |----------- |----------:|----------:|---------:|------:|--------:|--------:|----------:|------------:|
| AdaptiveSlop       | 1000       |  62.25 μs |  3.924 μs | 0.215 μs |  1.00 |    0.00 |       - |         - |          NA |
| FSharpDataAdaptive | 1000       | 440.11 μs | 69.113 μs | 3.788 μs |  7.07 |    0.06 | 56.1523 |  472000 B |          NA |

### WideTreeBenchmarks

| Method             | Width | Iterations | Mean        | Error        | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------- |------ |----------- |------------:|-------------:|----------:|------:|--------:|-------:|----------:|------------:|
| **AdaptiveSlop**       | **10**    | **100**        |    **45.04 μs** |    **47.347 μs** |  **2.595 μs** |  **1.00** |    **0.07** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 10    | 100        |   148.37 μs |     8.271 μs |  0.453 μs |  3.30 |    0.16 | 5.6152 |   47200 B |          NA |
|                    |       |            |             |              |           |       |         |        |           |             |
| **AdaptiveSlop**       | **50**    | **100**        |   **331.82 μs** |    **15.481 μs** |  **0.849 μs** |  **1.00** |    **0.00** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 50    | 100        |   508.30 μs |    25.145 μs |  1.378 μs |  1.53 |    0.00 | 4.8828 |   47200 B |          NA |
|                    |       |            |             |              |           |       |         |        |           |             |
| **AdaptiveSlop**       | **100**   | **100**        |   **684.82 μs** |    **21.483 μs** |  **1.178 μs** |  **1.00** |    **0.00** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 100   | 100        | 1,028.99 μs |   188.324 μs | 10.323 μs |  1.50 |    0.01 | 3.9063 |   47200 B |          NA |
|                    |       |            |             |              |           |       |         |        |           |             |
| **AdaptiveSlop**       | **500**   | **100**        | **3,898.88 μs** | **1,063.281 μs** | **58.282 μs** |  **1.00** |    **0.02** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 500   | 100        | 5,721.17 μs |   506.619 μs | 27.770 μs |  1.47 |    0.02 |      - |   47200 B |          NA |


---

## 2026-08-04 — WriteGen dirty cache + Kipo physics shape

- Commit: (pending — writeGen cache + Polling removed + KipoPhysicsBenchmarks added)
- Machine: WSL2 (Ubuntu), .NET 8.0, x64 RyuJIT
- Job: ShortRun (IterationCount=3, LaunchCount=1, WarmupCount=3)

### What changed

- Scalar nodes (AdaptiveNode, MapNNode, ReduceNode) cache the dirty verdict keyed by
  the global write generation: repeated reads at the same generation are O(1) per node.
  The per-evaluation (evalId) cache was removed; the generation key subsumes it.
- Recompute keys the cache to the generation at which it started; a write from user
  code mid-compute keeps the node Dirty (fixes a latent mid-compute-mark staleness hole).
- `PollingBenchmarks` (map2 aggregation tree, 1 write + N reads) was removed: the shape
  follows FDA's synthetic stress pattern, not real usage. `DeepWideBenchmarks` stays as
  the documented unobserved-tree corner (unchanged: write+read per iteration still walks
  the subtree — the generation key does not help a fresh-generation first read).
- `KipoPhysicsBenchmarks` added: a faithful clone of Pomo.Core `Projections.fs`
  `PhysicsCache` — per frame the sim advances every entity position, the render side
  materializes (variant 1: force per frame — the current Kipo shape) or reads the graph
  directly (variant 2: derived positions/rotations nodes + transient views + the spatial
  grid rebuild — the graph-as-cache extension).

### KipoPhysicsBenchmarks (50 frames per op)

Variant 1 — force per frame (current Kipo shape):

| Method | Entities | Mean | Ratio vs FDA | Allocated | Alloc ratio |
|---|---|---|---|---|---|
| AdaptiveSlop | 250 | 2.758 ms | 1.00 | 5,501.97 KB | 1.000 |
| FSharpDataAdaptive | 250 | 9.257 ms | 3.36 | 14,115.15 KB | 2.565 |
| AdaptiveSlop | 1000 | 11.109 ms | 1.00 | 23,783.71 KB | 1.000 |
| FSharpDataAdaptive | 1000 | 41.402 ms | 3.73 | 62,581.01 KB | 2.631 |

Variant 2 — graph direct (derived nodes + transient views):

| Method | Entities | Mean | Ratio vs FDA | Allocated | Alloc ratio |
|---|---|---|---|---|---|
| AdaptiveSlop_GraphDirect | 250 | 1.066 ms | 1.00 | 22.66 KB* | 0.004 |
| FSharpDataAdaptive_GraphDirect | 250 | 80.963 ms | 76.0 | 97,674.31 KB | 17.753 |
| AdaptiveSlop_GraphDirect | 1000 | 4.016 ms | 1.00 | 22.66 KB* | 0.001 |
| FSharpDataAdaptive_GraphDirect | 1000 | 592.519 ms | 147.5 | 433,005.78 KB | 18.206 |

* The BD `Allocated` column for the graph-direct variant is a short-job artifact
(contradicts its own frame-1 cost). Direct counter
(`GC.GetAllocatedBytesForCurrentThread`) per steady-state frame: library = 24 B
(writes 0 B + derived drain constant), user-code snapshot dictionaries ≈ 62 KB at 250
entities (identical in both libraries — the graph does not allocate it).

### Notes

- The 1.22 MB/frame FDA allocation at 1000 entities (variant 1) is the burden that made
  Kipo abandon adaptive maps for positions. AdaptiveSlop cuts it to ~38% and runs the
  frame 3.7× faster; the graph-direct variant removes the library allocation entirely
  (24 B/frame) and is 147× faster than FDA's per-element adaptive blocks.
- The spatial grid stays a per-frame user-code rebuild in both variants; a
  delta-maintained grouped node is Phase 7 work.

## 2026-08-07 — 7.4/7.5 hardening (collect/bind, initial-load ordering)

- Commit: 191bdeb + uncommitted 7.5 changes (snapshot/register-between initial loads, reentrant-write test)
- Machine: WSL2 (Linux Fedora Remix), AMD Ryzen 9 6900HX 3.29GHz, 8 cores, .NET 8.0.29, x64 RyuJIT (x86-64-v3)
- Job: ShortRun (IterationCount=3, LaunchCount=1, WarmupCount=3)

### What changed

- 7.4 dynamic dependencies: CollectSetNode (ASet.collect), BindSetNode/BindMapNode (ASet.bind/AMap.bind over aval).
- Initial-load ordering fix: every node now snapshots the source view, registers its sink, then runs the user mapping (a dirty source at first read no longer double-applies its delta; a reentrant write from the mapping lands in the journal exactly once).
- F# KeyValue dictionary iteration replaced by explicit struct enumerators in hot loops (88 B/entry measured).
- Means comparison: the 2026-08-04 section (same machine shape) is the valid baseline; earlier sections ran on other machines.

### ValueBenchmarks

| Method             | Iterations | Mean      | Error     | StdDev   | Ratio | RatioSD | Gen0    | Allocated | Alloc Ratio |
|------------------- |----------- |----------:|----------:|---------:|------:|--------:|--------:|----------:|------------:|
| AdaptiveSlop       | 1000       |  49.59 μs |  3.400 μs | 0.186 μs |  1.00 |    0.00 |       - |         - |          NA |
| FSharpDataAdaptive | 1000       | 453.41 μs | 35.438 μs | 1.942 μs |  9.14 |    0.05 | 56.1523 |  472000 B |          NA |

### DeepChainBenchmarks

| Method             | Depth | Iterations | Mean         | Error      | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------- |------ |----------- |-------------:|-----------:|----------:|------:|--------:|-------:|----------:|------------:|
| **AdaptiveSlop**       | **5**     | **100**        |     **24.56 μs** |   **1.698 μs** |  **0.093 μs** |  **1.00** |    **0.00** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 5     | 100        |     78.33 μs |  14.751 μs |  0.809 μs |  3.19 |    0.03 | 5.6152 |   47200 B |          NA |
|                    |       |            |              |            |           |       |         |        |           |             |
| **AdaptiveSlop**       | **10**    | **100**        |     **52.94 μs** |   **0.276 μs** |  **0.015 μs** |  **1.00** |    **0.00** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 10    | 100        |    133.05 μs |  18.724 μs |  1.026 μs |  2.51 |    0.02 | 5.6152 |   47200 B |          NA |
|                    |       |            |              |            |           |       |         |        |           |             |
| **AdaptiveSlop**       | **20**    | **100**        |    **116.74 μs** |  **10.416 μs** |  **0.571 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 20    | 100        |    267.64 μs |  55.752 μs |  3.056 μs |  2.29 |    0.02 | 5.3711 |   47200 B |          NA |
|                    |       |            |              |            |           |       |         |        |           |             |
| **AdaptiveSlop**       | **100**   | **100**        |    **723.90 μs** | **119.462 μs** |  **6.548 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 100   | 100        |  1,237.01 μs |  95.391 μs |  5.229 μs |  1.71 |    0.01 | 3.9063 |   47200 B |          NA |
|                    |       |            |              |            |           |       |         |        |           |             |
| **AdaptiveSlop**       | **1000**  | **100**        |  **7,898.13 μs** | **429.251 μs** | **23.529 μs** |  **1.00** |    **0.00** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 1000  | 100        | 12,100.83 μs | 657.173 μs | 36.022 μs |  1.53 |    0.01 |      - |   47200 B |          NA |

### Map2Benchmarks

| Method             | Iterations | Mean      | Error     | StdDev   | Ratio | RatioSD | Gen0    | Allocated | Alloc Ratio |
|------------------- |----------- |----------:|----------:|---------:|------:|--------:|--------:|----------:|------------:|
| AdaptiveSlop       | 1000       |  64.79 μs |  2.823 μs | 0.155 μs |  1.00 |    0.00 |       - |         - |          NA |
| FSharpDataAdaptive | 1000       | 449.92 μs | 63.215 μs | 3.465 μs |  6.94 |    0.05 | 62.9883 |  528000 B |          NA |

### BindBenchmarks

| Method             | Iterations | Mean      | Error     | StdDev   | Ratio | RatioSD | Gen0    | Allocated | Alloc Ratio |
|------------------- |----------- |----------:|----------:|---------:|------:|--------:|--------:|----------:|------------:|
| AdaptiveSlop       | 1000       |  76.26 μs |  26.64 μs | 1.460 μs |  1.00 |    0.02 |       - |         - |          NA |
| FSharpDataAdaptive | 1000       | 608.53 μs | 172.09 μs | 9.433 μs |  7.98 |    0.17 | 69.3359 |  584000 B |          NA |

### TransactionBenchmarks

| Method                     | ValueCount | Iterations | Mean       | Error     | StdDev   | Ratio | RatioSD | Gen0    | Allocated | Alloc Ratio |
|--------------------------- |----------- |----------- |-----------:|----------:|---------:|------:|--------:|--------:|----------:|------------:|
| AdaptiveSlop_Batched       | 10         | 500        |   369.4 μs |  76.09 μs |  4.17 μs |  1.00 |    0.01 |  1.4648 |  15.63 KB |        1.00 |
| FSharpDataAdaptive_Batched | 10         | 500        | 1,515.3 μs | 391.78 μs | 21.47 μs |  4.10 |    0.06 | 87.8906 | 726.56 KB |       46.50 |

### SetBenchmarks

| Method             | Iterations | Mean      | Error      | StdDev   | Ratio | RatioSD | Gen0     | Allocated | Alloc Ratio |
|------------------- |----------- |----------:|-----------:|---------:|------:|--------:|---------:|----------:|------------:|
| AdaptiveSlop       | 1000       |  42.48 μs |   1.558 μs | 0.085 μs |  1.00 |    0.00 |        - |         - |          NA |
| FSharpDataAdaptive | 1000       | 385.20 μs | 103.398 μs | 5.668 μs |  9.07 |    0.12 | 105.9570 |  888000 B |          NA |

### SetTransformBenchmarks

| Method             | Iterations | Mean       | Error     | StdDev   | Ratio | RatioSD | Gen0     | Allocated | Alloc Ratio |
|------------------- |----------- |-----------:|----------:|---------:|------:|--------:|---------:|----------:|------------:|
| AdaptiveSlop       | 500        |   118.0 μs |  25.85 μs |  1.42 μs |  1.00 |    0.01 |        - |         - |          NA |
| FSharpDataAdaptive | 500        | 1,484.9 μs | 237.94 μs | 13.04 μs | 12.59 |    0.16 | 185.5469 | 1561456 B |          NA |

### MapBenchmarks

| Method             | Iterations | Mean      | Error     | StdDev   | Ratio | RatioSD | Gen0     | Allocated | Alloc Ratio |
|------------------- |----------- |----------:|----------:|---------:|------:|--------:|---------:|----------:|------------:|
| AdaptiveSlop       | 1000       |  45.08 μs |  2.981 μs | 0.163 μs |  1.00 |    0.00 |        - |         - |          NA |
| FSharpDataAdaptive | 1000       | 354.00 μs | 38.976 μs | 2.136 μs |  7.85 |    0.05 | 104.9805 |  880000 B |          NA |

### MapTransformBenchmarks

| Method             | Iterations | Mean       | Error     | StdDev  | Ratio | RatioSD | Gen0     | Allocated | Alloc Ratio |
|------------------- |----------- |-----------:|----------:|--------:|------:|--------:|---------:|----------:|------------:|
| AdaptiveSlop       | 500        |   123.7 μs |  33.61 μs | 1.84 μs |  1.00 |    0.02 |        - |         - |          NA |
| FSharpDataAdaptive | 500        | 1,421.0 μs | 124.41 μs | 6.82 μs | 11.49 |    0.15 | 210.9375 | 1769408 B |          NA |

### LargeCollectionBenchmarks

| Method             | InitialSize | Iterations | Mean      | Error      | StdDev    | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|------------------- |------------ |----------- |----------:|-----------:|----------:|------:|--------:|--------:|-------:|----------:|------------:|
| AdaptiveSlop       | 10000       | 200        |  2.646 μs |  0.2014 μs | 0.0110 μs |  1.00 |    0.01 |       - |      - |         - |          NA |
| FSharpDataAdaptive | 10000       | 200        | 92.147 μs | 55.9439 μs | 3.0665 μs | 34.82 |    1.01 | 28.0762 | 1.7090 |  235456 B |          NA |

### ReadHeavyBenchmarks

| Method             | WriteCount | ReadsPerWrite | Mean      | Error     | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------- |----------- |-------------- |----------:|----------:|---------:|------:|--------:|-------:|----------:|------------:|
| AdaptiveSlop       | 100        | 50            |  50.34 μs |  1.081 μs | 0.059 μs |  1.00 |    0.00 |      - |         - |          NA |
| FSharpDataAdaptive | 100        | 50            | 127.16 μs | 67.097 μs | 3.678 μs |  2.53 |    0.06 | 5.6152 |   47200 B |          NA |

### DiamondGraphBenchmarks

| Method             | Iterations | Mean     | Error     | StdDev  | Ratio | RatioSD | Gen0    | Allocated | Alloc Ratio |
|------------------- |----------- |---------:|----------:|--------:|------:|--------:|--------:|----------:|------------:|
| AdaptiveSlop       | 1000       | 147.4 μs |   2.30 μs | 0.13 μs |  1.00 |    0.00 |       - |         - |          NA |
| FSharpDataAdaptive | 1000       | 657.4 μs | 143.23 μs | 7.85 μs |  4.46 |    0.05 | 73.2422 |  616000 B |          NA |

### WideTreeBenchmarks

| Method             | Width | Iterations | Mean        | Error      | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------- |------ |----------- |------------:|-----------:|----------:|------:|--------:|-------:|----------:|------------:|
| **AdaptiveSlop**       | **10**    | **100**        |    **32.65 μs** |  **21.191 μs** |  **1.162 μs** |  **1.00** |    **0.04** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 10    | 100        |    97.41 μs |  15.575 μs |  0.854 μs |  2.99 |    0.09 | 5.6152 |   47200 B |          NA |
|                    |       |            |             |            |           |       |         |        |           |             |
| **AdaptiveSlop**       | **50**    | **100**        |   **229.88 μs** |   **6.477 μs** |  **0.355 μs** |  **1.00** |    **0.00** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 50    | 100        |   386.42 μs |  75.159 μs |  4.120 μs |  1.68 |    0.02 | 5.3711 |   47200 B |          NA |
|                    |       |            |             |            |           |       |         |        |           |             |
| **AdaptiveSlop**       | **100**   | **100**        |   **488.78 μs** |   **3.817 μs** |  **0.209 μs** |  **1.00** |    **0.00** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 100   | 100        |   746.44 μs | 133.724 μs |  7.330 μs |  1.53 |    0.01 | 4.8828 |   47200 B |          NA |
|                    |       |            |             |            |           |       |         |        |           |             |
| **AdaptiveSlop**       | **500**   | **100**        | **2,667.45 μs** |  **52.386 μs** |  **2.871 μs** |  **1.00** |    **0.00** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 500   | 100        | 3,669.49 μs | 801.220 μs | 43.918 μs |  1.38 |    0.01 | 3.9063 |   47200 B |          NA |

### OptimizedWideTreeBenchmarks

| Method                 | Width | Iterations | Mean        | Error        | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|----------------------- |------ |----------- |------------:|-------------:|----------:|------:|--------:|-------:|----------:|------------:|
| **AdaptiveSlop_Map2Chain** | **10**    | **100**        |    **35.53 μs** |     **3.264 μs** |  **0.179 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| AdaptiveSlop_Reduce    | 10    | 100        |    10.37 μs |     0.491 μs |  0.027 μs |  0.29 |    0.00 |      - |         - |          NA |
| FSharpDataAdaptive     | 10    | 100        |    94.94 μs |    10.710 μs |  0.587 μs |  2.67 |    0.02 | 5.6152 |   47200 B |          NA |
|                        |       |            |             |              |           |       |         |        |           |             |
| **AdaptiveSlop_Map2Chain** | **50**    | **100**        |   **236.95 μs** |    **10.358 μs** |  **0.568 μs** |  **1.00** |    **0.00** |      **-** |         **-** |          **NA** |
| AdaptiveSlop_Reduce    | 50    | 100        |    43.93 μs |     7.031 μs |  0.385 μs |  0.19 |    0.00 |      - |         - |          NA |
| FSharpDataAdaptive     | 50    | 100        |   394.20 μs |    84.288 μs |  4.620 μs |  1.66 |    0.02 | 5.3711 |   47200 B |          NA |
|                        |       |            |             |              |           |       |         |        |           |             |
| **AdaptiveSlop_Map2Chain** | **100**   | **100**        |   **474.24 μs** |    **49.237 μs** |  **2.699 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| AdaptiveSlop_Reduce    | 100   | 100        |    84.68 μs |     8.475 μs |  0.465 μs |  0.18 |    0.00 |      - |         - |          NA |
| FSharpDataAdaptive     | 100   | 100        |   763.61 μs |   249.936 μs | 13.700 μs |  1.61 |    0.03 | 4.8828 |   47200 B |          NA |
|                        |       |            |             |              |           |       |         |        |           |             |
| **AdaptiveSlop_Map2Chain** | **500**   | **100**        | **2,674.28 μs** |   **187.082 μs** | **10.255 μs** |  **1.00** |    **0.00** |      **-** |         **-** |          **NA** |
| AdaptiveSlop_Reduce    | 500   | 100        |   421.23 μs |    14.639 μs |  0.802 μs |  0.16 |    0.00 |      - |         - |          NA |
| FSharpDataAdaptive     | 500   | 100        | 3,934.42 μs | 1,480.487 μs | 81.151 μs |  1.47 |    0.03 |      - |   47200 B |          NA |

### DeepWideBenchmarks

| Method             | Depth | BranchingFactor | Iterations | Mean         | Error         | StdDev       | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------- |------ |---------------- |----------- |-------------:|--------------:|-------------:|------:|--------:|-------:|----------:|------------:|
| **AdaptiveSlop**       | **3**     | **2**               | **50**         |     **11.78 μs** |      **4.072 μs** |     **0.223 μs** |  **1.00** |    **0.02** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 3     | 2               | 50         |     35.58 μs |     25.415 μs |     1.393 μs |  3.02 |    0.11 | 2.8076 |   23600 B |          NA |
|                    |       |                 |            |              |               |              |       |         |        |           |             |
| **AdaptiveSlop**       | **3**     | **3**               | **50**         |     **32.36 μs** |     **14.026 μs** |     **0.769 μs** |  **1.00** |    **0.03** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 3     | 3               | 50         |     56.43 μs |     10.092 μs |     0.553 μs |  1.74 |    0.04 | 2.8076 |   23600 B |          NA |
|                    |       |                 |            |              |               |              |       |         |        |           |             |
| **AdaptiveSlop**       | **3**     | **4**               | **50**         |     **55.82 μs** |      **7.164 μs** |     **0.393 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 3     | 4               | 50         |     71.37 μs |     13.907 μs |     0.762 μs |  1.28 |    0.01 | 2.8076 |   23600 B |          NA |
|                    |       |                 |            |              |               |              |       |         |        |           |             |
| **AdaptiveSlop**       | **5**     | **2**               | **50**         |     **27.24 μs** |      **3.841 μs** |     **0.211 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 5     | 2               | 50         |     48.61 μs |      8.511 μs |     0.466 μs |  1.78 |    0.02 | 2.8076 |   23600 B |          NA |
|                    |       |                 |            |              |               |              |       |         |        |           |             |
| **AdaptiveSlop**       | **5**     | **3**               | **50**         |    **162.18 μs** |      **3.635 μs** |     **0.199 μs** |  **1.00** |    **0.00** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 5     | 3               | 50         |     90.76 μs |     16.791 μs |     0.920 μs |  0.56 |    0.00 | 2.8076 |   23600 B |          NA |
|                    |       |                 |            |              |               |              |       |         |        |           |             |
| **AdaptiveSlop**       | **5**     | **4**               | **50**         |    **982.09 μs** |     **34.246 μs** |     **1.877 μs** |  **1.00** |    **0.00** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 5     | 4               | 50         |    116.24 μs |      8.059 μs |     0.442 μs |  0.12 |    0.00 | 2.8076 |   23600 B |          NA |
|                    |       |                 |            |              |               |              |       |         |        |           |             |
| **AdaptiveSlop**       | **7**     | **2**               | **50**         |     **85.18 μs** |     **14.842 μs** |     **0.814 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 7     | 2               | 50         |     62.43 μs |      6.936 μs |     0.380 μs |  0.73 |    0.01 | 2.8076 |   23600 B |          NA |
|                    |       |                 |            |              |               |              |       |         |        |           |             |
| **AdaptiveSlop**       | **7**     | **3**               | **50**         |  **2,028.85 μs** |    **116.987 μs** |     **6.412 μs** |  **1.00** |    **0.00** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 7     | 3               | 50         |    120.28 μs |     24.418 μs |     1.338 μs |  0.06 |    0.00 | 2.8076 |   23600 B |          NA |
|                    |       |                 |            |              |               |              |       |         |        |           |             |
| **AdaptiveSlop**       | **7**     | **4**               | **50**         | **19,066.16 μs** | **22,235.971 μs** | **1,218.829 μs** | **1.003** |    **0.08** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 7     | 4               | 50         |    165.69 μs |     51.470 μs |     2.821 μs | 0.009 |    0.00 | 2.6855 |   23600 B |          NA |

### KipoPhysicsBenchmarks

| Method                         | EntityCount | Iterations | Mean         | Error         | StdDev       | Ratio | RatioSD | Gen0       | Gen1       | Gen2       | Allocated    | Alloc Ratio |
|------------------------------- |------------ |----------- |-------------:|--------------:|-------------:|------:|--------:|-----------:|-----------:|-----------:|-------------:|------------:|
| **AdaptiveSlop**                   | **250**         | **50**         |   **2,561.7 μs** |     **137.66 μs** |      **7.55 μs** |  **1.00** |    **0.00** |   **671.8750** |   **128.9063** |          **-** |   **5501.97 KB** |       **1.000** |
| FSharpDataAdaptive             | 250         | 50         |   8,248.0 μs |   1,215.68 μs |     66.64 μs |  3.22 |    0.02 |  1718.7500 |   250.0000 |          - |  14115.15 KB |       2.565 |
| AdaptiveSlop_GraphDirect       | 250         | 50         |     917.2 μs |      14.70 μs |      0.81 μs |  0.36 |    0.00 |     1.9531 |          - |          - |     21.48 KB |       0.004 |
| FSharpDataAdaptive_GraphDirect | 250         | 50         |  82,494.0 μs | 433,487.26 μs | 23,760.90 μs | 32.20 |    8.03 | 12000.0000 |  9600.0000 |  7600.0000 |  97674.31 KB |      17.753 |
|                                |             |            |              |               |              |       |         |            |            |            |              |             |
| **AdaptiveSlop**                   | **1000**        | **50**         |  **10,683.4 μs** |     **281.44 μs** |     **15.43 μs** |  **1.00** |    **0.00** |  **2906.2500** |   **937.5000** |          **-** |  **23783.71 KB** |       **1.000** |
| FSharpDataAdaptive             | 1000        | 50         |  41,602.9 μs |  11,021.34 μs |    604.12 μs |  3.89 |    0.05 |  7615.3846 |  2230.7692 |          - |  62581.01 KB |       2.631 |
| AdaptiveSlop_GraphDirect       | 1000        | 50         |   3,643.6 μs |     468.99 μs |     25.71 μs |  0.34 |    0.00 |          - |          - |          - |     21.48 KB |       0.001 |
| FSharpDataAdaptive_GraphDirect | 1000        | 50         | 568,077.1 μs | 214,499.38 μs | 11,757.43 μs | 53.17 |    0.96 | 51000.0000 | 47000.0000 | 33000.0000 | 433005.69 KB |      18.206 |

### UnbalancedTreeBenchmarks

| Method                     | DeepBranchDepth | ShallowBranchCount | Iterations | Mean        | Error      | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------------------------- |---------------- |------------------- |----------- |------------:|-----------:|----------:|------:|--------:|-------:|----------:|------------:|
| **AdaptiveSlop_DeepChange**    | **10**              | **5**                  | **50**         |    **51.13 μs** |  **22.804 μs** |  **1.250 μs** |  **1.00** |    **0.03** |      **-** |         **-** |          **NA** |
| FDA_DeepChange             | 10              | 5                  | 50         |   117.97 μs |  14.881 μs |  0.816 μs |  2.31 |    0.05 | 2.8076 |   23600 B |          NA |
| AdaptiveSlop_ShallowChange | 10              | 5                  | 50         |    24.57 μs |   7.054 μs |  0.387 μs |  0.48 |    0.01 |      - |         - |          NA |
| FDA_ShallowChange          | 10              | 5                  | 50         |    53.92 μs |   9.026 μs |  0.495 μs |  1.06 |    0.02 | 2.8076 |   23600 B |          NA |
|                            |                 |                    |            |             |            |           |       |         |        |           |             |
| **AdaptiveSlop_DeepChange**    | **10**              | **20**                 | **50**         |   **118.15 μs** |  **19.253 μs** |  **1.055 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FDA_DeepChange             | 10              | 20                 | 50         |   235.60 μs |  18.007 μs |  0.987 μs |  1.99 |    0.02 | 2.6855 |   23600 B |          NA |
| AdaptiveSlop_ShallowChange | 10              | 20                 | 50         |    84.87 μs |   7.893 μs |  0.433 μs |  0.72 |    0.01 |      - |         - |          NA |
| FDA_ShallowChange          | 10              | 20                 | 50         |   170.94 μs |   8.457 μs |  0.464 μs |  1.45 |    0.01 | 2.6855 |   23600 B |          NA |
|                            |                 |                    |            |             |            |           |       |         |        |           |             |
| **AdaptiveSlop_DeepChange**    | **10**              | **50**                 | **50**         |   **257.27 μs** |  **10.779 μs** |  **0.591 μs** |  **1.00** |    **0.00** |      **-** |         **-** |          **NA** |
| FDA_DeepChange             | 10              | 50                 | 50         |   487.16 μs | 100.252 μs |  5.495 μs |  1.89 |    0.02 | 2.4414 |   23600 B |          NA |
| AdaptiveSlop_ShallowChange | 10              | 50                 | 50         |   222.49 μs |  42.344 μs |  2.321 μs |  0.86 |    0.01 |      - |         - |          NA |
| FDA_ShallowChange          | 10              | 50                 | 50         |   415.22 μs |  51.272 μs |  2.810 μs |  1.61 |    0.01 | 2.4414 |   23600 B |          NA |
|                            |                 |                    |            |             |            |           |       |         |        |           |             |
| **AdaptiveSlop_DeepChange**    | **50**              | **5**                  | **50**         |   **196.05 μs** |  **56.765 μs** |  **3.111 μs** |  **1.00** |    **0.02** |      **-** |         **-** |          **NA** |
| FDA_DeepChange             | 50              | 5                  | 50         |   336.94 μs |  53.731 μs |  2.945 μs |  1.72 |    0.03 | 2.4414 |   23600 B |          NA |
| AdaptiveSlop_ShallowChange | 50              | 5                  | 50         |    51.70 μs |   0.679 μs |  0.037 μs |  0.26 |    0.00 |      - |         - |          NA |
| FDA_ShallowChange          | 50              | 5                  | 50         |    57.54 μs |   4.260 μs |  0.233 μs |  0.29 |    0.00 | 2.8076 |   23600 B |          NA |
|                            |                 |                    |            |             |            |           |       |         |        |           |             |
| **AdaptiveSlop_DeepChange**    | **50**              | **20**                 | **50**         |   **283.93 μs** | **147.356 μs** |  **8.077 μs** |  **1.00** |    **0.03** |      **-** |         **-** |          **NA** |
| FDA_DeepChange             | 50              | 20                 | 50         |   500.19 μs | 150.602 μs |  8.255 μs |  1.76 |    0.05 | 1.9531 |   23600 B |          NA |
| AdaptiveSlop_ShallowChange | 50              | 20                 | 50         |   133.95 μs |   5.595 μs |  0.307 μs |  0.47 |    0.01 |      - |         - |          NA |
| FDA_ShallowChange          | 50              | 20                 | 50         |   171.72 μs |  10.075 μs |  0.552 μs |  0.61 |    0.01 | 2.6855 |   23600 B |          NA |
|                            |                 |                    |            |             |            |           |       |         |        |           |             |
| **AdaptiveSlop_DeepChange**    | **50**              | **50**                 | **50**         |   **422.45 μs** |  **58.950 μs** |  **3.231 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FDA_DeepChange             | 50              | 50                 | 50         |   750.26 μs | 210.597 μs | 11.544 μs |  1.78 |    0.03 | 1.9531 |   23600 B |          NA |
| AdaptiveSlop_ShallowChange | 50              | 50                 | 50         |   271.87 μs |  11.802 μs |  0.647 μs |  0.64 |    0.00 |      - |         - |          NA |
| FDA_ShallowChange          | 50              | 50                 | 50         |   408.78 μs |  11.853 μs |  0.650 μs |  0.97 |    0.01 | 2.4414 |   23600 B |          NA |
|                            |                 |                    |            |             |            |           |       |         |        |           |             |
| **AdaptiveSlop_DeepChange**    | **100**             | **5**                  | **50**         |   **373.86 μs** |  **53.907 μs** |  **2.955 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FDA_DeepChange             | 100             | 5                  | 50         |   622.09 μs | 132.899 μs |  7.285 μs |  1.66 |    0.02 | 1.9531 |   23600 B |          NA |
| AdaptiveSlop_ShallowChange | 100             | 5                  | 50         |   113.33 μs |  15.983 μs |  0.876 μs |  0.30 |    0.00 |      - |         - |          NA |
| FDA_ShallowChange          | 100             | 5                  | 50         |    54.43 μs |   4.328 μs |  0.237 μs |  0.15 |    0.00 | 2.8076 |   23600 B |          NA |
|                            |                 |                    |            |             |            |           |       |         |        |           |             |
| **AdaptiveSlop_DeepChange**    | **100**             | **20**                 | **50**         |   **469.67 μs** |  **57.935 μs** |  **3.176 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FDA_DeepChange             | 100             | 20                 | 50         |   766.82 μs |  24.881 μs |  1.364 μs |  1.63 |    0.01 | 1.9531 |   23600 B |          NA |
| AdaptiveSlop_ShallowChange | 100             | 20                 | 50         |   181.52 μs |   7.279 μs |  0.399 μs |  0.39 |    0.00 |      - |         - |          NA |
| FDA_ShallowChange          | 100             | 20                 | 50         |   169.66 μs |  11.516 μs |  0.631 μs |  0.36 |    0.00 | 2.6855 |   23600 B |          NA |
|                            |                 |                    |            |             |            |           |       |         |        |           |             |
| **AdaptiveSlop_DeepChange**    | **100**             | **50**                 | **50**         |   **590.81 μs** |   **9.318 μs** |  **0.511 μs** |  **1.00** |    **0.00** |      **-** |         **-** |          **NA** |
| FDA_DeepChange             | 100             | 50                 | 50         | 1,038.37 μs |  58.052 μs |  3.182 μs |  1.76 |    0.00 | 1.9531 |   23600 B |          NA |
| AdaptiveSlop_ShallowChange | 100             | 50                 | 50         |   326.31 μs |  26.509 μs |  1.453 μs |  0.55 |    0.00 |      - |         - |          NA |
| FDA_ShallowChange          | 100             | 50                 | 50         |   410.85 μs |  62.947 μs |  3.450 μs |  0.70 |    0.01 | 2.4414 |   23600 B |          NA |

### IncrementalChainBenchmarks

| Method             | InitialSize | Mutations | Mean      | Error      | StdDev   | Ratio | RatioSD | Gen0     | Allocated | Alloc Ratio |
|------------------- |------------ |---------- |----------:|-----------:|---------:|------:|--------:|---------:|----------:|------------:|
| **AdaptiveSlop**       | **100**         | **200**       |  **49.95 μs** |   **7.847 μs** | **0.430 μs** |  **1.00** |    **0.01** |        **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 100         | 200       | 596.57 μs | 106.191 μs | 5.821 μs | 11.94 |    0.13 |  76.1719 |  638704 B |          NA |
|                    |             |           |           |            |          |       |         |          |           |             |
| **AdaptiveSlop**       | **1000**        | **200**       |  **49.77 μs** |   **2.525 μs** | **0.138 μs** |  **1.00** |    **0.00** |        **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 1000        | 200       | 617.11 μs |  22.354 μs | 1.225 μs | 12.40 |    0.04 |  78.1250 |  656512 B |          NA |
|                    |             |           |           |            |          |       |         |          |           |             |
| **AdaptiveSlop**       | **10000**       | **200**       |  **49.29 μs** |   **2.735 μs** | **0.150 μs** |  **1.00** |    **0.00** |        **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 10000       | 200       | 661.95 μs |  68.922 μs | 3.778 μs | 13.43 |    0.08 | 101.5625 |  852736 B |          NA |

### ConcurrentBenchmarks

| Method             | ThreadCount | IterationsPerThread | Mean      | Error      | StdDev   | Ratio | RatioSD | Gen0     | Gen1   | Allocated | Alloc Ratio |
|------------------- |------------ |-------------------- |----------:|-----------:|---------:|------:|--------:|---------:|-------:|----------:|------------:|
| AdaptiveSlop       | 4           | 500                 |  53.82 μs |   6.926 μs | 0.380 μs |  1.00 |    0.01 |   0.0610 |      - |     849 B |        1.00 |
| FSharpDataAdaptive | 4           | 500                 | 593.65 μs | 110.121 μs | 6.036 μs | 11.03 |    0.12 | 112.3047 | 0.9766 |  944848 B |    1,112.90 |

---

## 2026-08-05 — Hostile-review fixes + weak-reference sinks

- Commit: 77fa089 (feat/hostile-review-fixes: ea5c455 hostile-review fixes + c992e76 weak-reference sinks); docs/archive/2026-08-05-DESIGN-WEAK-SINK-REFERENCES.md
- Machine: WSL2 (Linux Fedora Remix), AMD Ryzen 9 6900HX 3.29GHz, 16 logical / 8 physical cores, .NET 8.0.29, x64 RyuJIT (x86-64-v3)
- Job: DefaultJob — full suite, 134 benchmarks (BenchmarkResults/BenchmarkRun-20260805-132907.log)

### What changed

- Correctness fixes from the hostile reviews (docs/archive/2026-08-05-GLM_REVIEW_FINDINGS.md,
  docs/archive/2026-08-05-KIMI_REVIEW_FINDINGS.md): ofAVal poll-on-version, net deltas at all
  producers, list replay validation, ReduceNode write-generation guard, exception-safe
  drain compaction, notification isolation, Set-supersedes-batch, real changeable
  Dispose, plus allocation fixes (de-boxed enumerators, pre-sized scratch sets).
- Weak-reference sinks (GLM 10): SinkList entries are now WeakReference; a derived
  collection node that was read and then dropped is collected (FDA precedent:
  WeakOutputSet). Delivery compacts dead entries at batch start; reentrant
  registrations during delivery are not delivered (bound captured).

### Regressions vs the 2026-08-07 section (same machine, doc rule: AdaptiveSlop mean beyond the baseline error margin)

- Map 45.08 → 54.57 μs (+21%), Set 42.48 → 50.42 μs (+19%), Map2 64.79 → 68.16 μs (+5%,
  marginal), DeepWide 5/3 162.18 → 176.70 μs (+9%), UnbalancedTree ShallowChange 50/5
  51.70 → 57.68 μs (+12%), KipoPhysics GraphDirect 250 917.2 → 954.6 μs (+4%, marginal).
- SetTransform (+7%) and KipoPhysics GraphDirect 1000 (+3%) also grew, but stay inside the
  baseline error margin (ShortRun baseline; this run is the default job) — not counted.
- Everything else is flat or faster. The largest wins: DeepWide 7/4 −41%, 5/4 −32%, 7/3 −22%;
  OptimizedWideTree Map2Chain w10 −16%, w50 −15%, w500 −14%; WideTree w500 −15%; DeepChain
  d1000 −14%, d100 −13%; UnbalancedTree DeepChange 50/20 −14%, 10/5 −13%, 100/20 −13%;
  LargeCollection −8%; DiamondGraph −7%; ReadHeavy −7%; Bind −5%; Transaction −5%;
  Concurrent −4%; Value −3%; MapTransform −3%.
- Allocated stays zero on every steady-state path (Gen0 "-"). The rows with allocation are
  the documented non-steady paths (ListWriteRead, KipoPhysics, Concurrent, Transaction).
- New benchmarks with no prior baseline: ListAppend 71.48 μs, ListTransform 158.8 μs,
  ListWriteRead 24.07 / 24.95 / 36.88 / 276.91 μs (Count 0 / 1000 / 10000 / 100000).
- FDA-relative: AdaptiveSlop is faster on every path except the losses already present in the
  2026-08-07 section (DeepWide wide/deep, UnbalancedTree ShallowChange at depth ≥ 50,
  ListWriteRead at Count 100000); those ratios are unchanged within noise.

### BindBenchmarks

| Method             | Iterations | Mean      | Error     | StdDev   | Ratio | RatioSD | Gen0    | Allocated | Alloc Ratio |
|------------------- |----------- |----------:|----------:|---------:|------:|--------:|--------:|----------:|------------:|
| AdaptiveSlop       | 1000       |  72.62 μs |  0.792 μs | 0.741 μs |  1.00 |    0.01 |       - |         - |          NA |
| FSharpDataAdaptive | 1000       | 539.77 μs | 10.190 μs | 9.532 μs |  7.43 |    0.15 | 69.3359 |  584000 B |          NA |

### ConcurrentBenchmarks

| Method             | ThreadCount | IterationsPerThread | Mean      | Error    | StdDev   | Ratio | RatioSD | Gen0     | Gen1   | Allocated | Alloc Ratio |
|------------------- |------------ |-------------------- |----------:|---------:|---------:|------:|--------:|---------:|-------:|----------:|------------:|
| AdaptiveSlop       | 4           | 500                 |  51.55 μs | 0.971 μs | 0.861 μs |  1.00 |    0.02 |   0.0610 |      - |     849 B |        1.00 |
| FSharpDataAdaptive | 4           | 500                 | 502.85 μs | 7.996 μs | 6.677 μs |  9.76 |    0.20 | 112.3047 | 0.9766 |  944848 B |    1,112.90 |

### DeepChainBenchmarks

| Method             | Depth | Iterations | Mean         | Error      | StdDev     | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------- |------ |----------- |-------------:|-----------:|-----------:|------:|--------:|-------:|----------:|------------:|
| **AdaptiveSlop**       | **5**     | **100**        |     **24.20 μs** |   **0.407 μs** |   **0.381 μs** |  **1.00** |    **0.02** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 5     | 100        |     71.28 μs |   0.679 μs |   0.567 μs |  2.95 |    0.05 | 5.6152 |   47200 B |          NA |
|                    |       |            |              |            |            |       |         |        |           |             |
| **AdaptiveSlop**       | **10**    | **100**        |     **49.82 μs** |   **0.507 μs** |   **0.424 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 10    | 100        |    120.21 μs |   0.695 μs |   0.650 μs |  2.41 |    0.02 | 5.6152 |   47200 B |          NA |
|                    |       |            |              |            |            |       |         |        |           |             |
| **AdaptiveSlop**       | **20**    | **100**        |    **116.96 μs** |   **2.328 μs** |   **3.108 μs** |  **1.00** |    **0.04** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 20    | 100        |    227.49 μs |   1.787 μs |   1.584 μs |  1.95 |    0.05 | 5.6152 |   47200 B |          NA |
|                    |       |            |              |            |            |       |         |        |           |             |
| **AdaptiveSlop**       | **100**   | **100**        |    **628.59 μs** |   **6.804 μs** |   **5.312 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 100   | 100        |  1,111.67 μs |   7.387 μs |   6.909 μs |  1.77 |    0.02 | 3.9063 |   47200 B |          NA |
|                    |       |            |              |            |            |       |         |        |           |             |
| **AdaptiveSlop**       | **1000**  | **100**        |  **6,767.47 μs** |  **85.838 μs** |  **80.292 μs** |  **1.00** |    **0.02** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 1000  | 100        | 11,144.61 μs | 178.116 μs | 148.735 μs |  1.65 |    0.03 |      - |   47200 B |          NA |

### DeepWideBenchmarks

| Method             | Depth | BranchingFactor | Iterations | Mean         | Error      | StdDev     | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------- |------ |---------------- |----------- |-------------:|-----------:|-----------:|------:|--------:|-------:|----------:|------------:|
| **AdaptiveSlop**       | **3**     | **2**               | **50**         |     **10.23 μs** |   **0.045 μs** |   **0.040 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 3     | 2               | 50         |     29.89 μs |   0.102 μs |   0.090 μs |  2.92 |    0.01 | 2.8076 |   23600 B |          NA |
|                    |       |                 |            |              |            |            |       |         |        |           |             |
| **AdaptiveSlop**       | **3**     | **3**               | **50**         |     **27.99 μs** |   **0.079 μs** |   **0.070 μs** |  **1.00** |    **0.00** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 3     | 3               | 50         |     49.88 μs |   0.150 μs |   0.141 μs |  1.78 |    0.01 | 2.8076 |   23600 B |          NA |
|                    |       |                 |            |              |            |            |       |         |        |           |             |
| **AdaptiveSlop**       | **3**     | **4**               | **50**         |     **48.41 μs** |   **0.190 μs** |   **0.178 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 3     | 4               | 50         |     62.15 μs |   0.174 μs |   0.163 μs |  1.28 |    0.01 | 2.8076 |   23600 B |          NA |
|                    |       |                 |            |              |            |            |       |         |        |           |             |
| **AdaptiveSlop**       | **5**     | **2**               | **50**         |     **24.81 μs** |   **0.083 μs** |   **0.069 μs** |  **1.00** |    **0.00** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 5     | 2               | 50         |     42.68 μs |   0.299 μs |   0.250 μs |  1.72 |    0.01 | 2.8076 |   23600 B |          NA |
|                    |       |                 |            |              |            |            |       |         |        |           |             |
| **AdaptiveSlop**       | **5**     | **3**               | **50**         |    **176.70 μs** |   **2.758 μs** |   **2.445 μs** |  **1.00** |    **0.02** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 5     | 3               | 50         |     78.95 μs |   0.153 μs |   0.128 μs |  0.45 |    0.01 | 2.8076 |   23600 B |          NA |
|                    |       |                 |            |              |            |            |       |         |        |           |             |
| **AdaptiveSlop**       | **5**     | **4**               | **50**         |    **667.06 μs** |   **2.366 μs** |   **1.976 μs** |  **1.00** |    **0.00** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 5     | 4               | 50         |    104.86 μs |   0.147 μs |   0.131 μs |  0.16 |    0.00 | 2.8076 |   23600 B |          NA |
|                    |       |                 |            |              |            |            |       |         |        |           |             |
| **AdaptiveSlop**       | **7**     | **2**               | **50**         |     **78.69 μs** |   **1.237 μs** |   **1.157 μs** |  **1.00** |    **0.02** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 7     | 2               | 50         |     56.01 μs |   0.200 μs |   0.187 μs |  0.71 |    0.01 | 2.8076 |   23600 B |          NA |
|                    |       |                 |            |              |            |            |       |         |        |           |             |
| **AdaptiveSlop**       | **7**     | **3**               | **50**         |  **1,581.63 μs** |  **30.746 μs** |  **35.407 μs** |  **1.00** |    **0.03** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 7     | 3               | 50         |    114.41 μs |   0.659 μs |   0.617 μs |  0.07 |    0.00 | 2.8076 |   23600 B |          NA |
|                    |       |                 |            |              |            |            |       |         |        |           |             |
| **AdaptiveSlop**       | **7**     | **4**               | **50**         | **11,262.63 μs** | **222.969 μs** | **319.776 μs** |  **1.00** |    **0.04** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 7     | 4               | 50         |    155.38 μs |   1.010 μs |   0.945 μs |  0.01 |    0.00 | 2.6855 |   23600 B |          NA |

### DiamondGraphBenchmarks

| Method             | Iterations | Mean     | Error   | StdDev  | Ratio | RatioSD | Gen0    | Allocated | Alloc Ratio |
|------------------- |----------- |---------:|--------:|--------:|------:|--------:|--------:|----------:|------------:|
| AdaptiveSlop       | 1000       | 137.5 μs | 0.37 μs | 0.35 μs |  1.00 |    0.00 |       - |         - |          NA |
| FSharpDataAdaptive | 1000       | 613.8 μs | 8.28 μs | 7.75 μs |  4.47 |    0.06 | 73.2422 |  616000 B |          NA |

### IncrementalChainBenchmarks

| Method             | InitialSize | Mutations | Mean      | Error    | StdDev   | Ratio | RatioSD | Gen0     | Allocated | Alloc Ratio |
|------------------- |------------ |---------- |----------:|---------:|---------:|------:|--------:|---------:|----------:|------------:|
| **AdaptiveSlop**       | **100**         | **200**       |  **50.84 μs** | **0.098 μs** | **0.092 μs** |  **1.00** |    **0.00** |        **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 100         | 200       | 521.15 μs | 4.359 μs | 4.077 μs | 10.25 |    0.08 |  76.1719 |  638704 B |          NA |
|                    |             |           |           |          |          |       |         |          |           |             |
| **AdaptiveSlop**       | **1000**        | **200**       |  **48.66 μs** | **0.266 μs** | **0.249 μs** |  **1.00** |    **0.01** |        **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 1000        | 200       | 527.97 μs | 7.101 μs | 6.295 μs | 10.85 |    0.14 |  78.1250 |  656512 B |          NA |
|                    |             |           |           |          |          |       |         |          |           |             |
| **AdaptiveSlop**       | **10000**       | **200**       |  **49.11 μs** | **0.153 μs** | **0.144 μs** |  **1.00** |    **0.00** |        **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 10000       | 200       | 579.03 μs | 3.715 μs | 3.102 μs | 11.79 |    0.07 | 101.5625 |  852736 B |          NA |

### KipoPhysicsBenchmarks

| Method                         | EntityCount | Iterations | Mean         | Error         | StdDev        | Median       | Ratio | RatioSD | Gen0       | Gen1       | Gen2       | Allocated    | Alloc Ratio |
|------------------------------- |------------ |----------- |-------------:|--------------:|--------------:|-------------:|------:|--------:|-----------:|-----------:|-----------:|-------------:|------------:|
| **AdaptiveSlop**                   | **250**         | **50**         |   **2,323.1 μs** |      **25.68 μs** |      **22.77 μs** |   **2,327.9 μs** |  **1.00** |    **0.01** |   **671.8750** |   **132.8125** |          **-** |   **5502.73 KB** |       **1.000** |
| FSharpDataAdaptive             | 250         | 50         |   7,216.3 μs |      48.62 μs |      45.48 μs |   7,232.3 μs |  3.11 |    0.04 |  1726.5625 |   250.0000 |          - |  14116.91 KB |       2.565 |
| AdaptiveSlop_GraphDirect       | 250         | 50         |     954.6 μs |       2.15 μs |       1.90 μs |     954.1 μs |  0.41 |    0.00 |     1.9531 |          - |          - |     21.48 KB |       0.004 |
| FSharpDataAdaptive_GraphDirect | 250         | 50         |  95,077.0 μs |   4,386.55 μs |  12,933.84 μs |  99,631.6 μs | 40.93 |    5.56 | 15600.0000 | 14600.0000 | 14000.0000 | 125574.53 KB |      22.820 |
|                                |             |            |              |               |               |              |       |         |            |            |            |              |             |
| **AdaptiveSlop**                   | **1000**        | **50**         |  **10,086.9 μs** |      **49.54 μs** |      **46.34 μs** |  **10,092.7 μs** |  **1.00** |    **0.01** |  **2906.2500** |  **1031.2500** |          **-** |  **23801.92 KB** |       **1.000** |
| FSharpDataAdaptive             | 1000        | 50         |  35,258.4 μs |     198.42 μs |     185.60 μs |  35,205.4 μs |  3.50 |    0.02 |  7666.6667 |  2333.3333 |          - |  62694.85 KB |       2.634 |
| AdaptiveSlop_GraphDirect       | 1000        | 50         |   3,763.0 μs |       7.81 μs |       7.31 μs |   3,762.4 μs |  0.37 |    0.00 |          - |          - |          - |     21.48 KB |       0.001 |
| FSharpDataAdaptive_GraphDirect | 1000        | 50         | 707,153.0 μs | 115,960.42 μs | 341,911.94 μs | 489,388.7 μs | 70.11 |   33.74 | 55000.0000 | 48000.0000 | 40000.0000 | 458548.35 KB |      19.265 |

### LargeCollectionBenchmarks

| Method             | InitialSize | Iterations | Mean      | Error     | StdDev    | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|------------------- |------------ |----------- |----------:|----------:|----------:|------:|--------:|--------:|-------:|----------:|------------:|
| AdaptiveSlop       | 10000       | 200        |  2.422 μs | 0.0067 μs | 0.0060 μs |  1.00 |    0.00 |       - |      - |         - |          NA |
| FSharpDataAdaptive | 10000       | 200        | 79.939 μs | 0.7938 μs | 0.7425 μs | 33.01 |    0.31 | 28.0762 | 1.7090 |  235456 B |          NA |

### ListAppendBenchmarks

| Method             | Iterations | Mean        | Error    | StdDev   | Ratio | RatioSD | Gen0     | Gen1     | Gen2   | Allocated | Alloc Ratio |
|------------------- |----------- |------------:|---------:|---------:|------:|--------:|---------:|---------:|-------:|----------:|------------:|
| AdaptiveSlop       | 500        |    71.48 μs | 0.220 μs | 0.195 μs |  1.00 |    0.00 |        - |        - |      - |         - |          NA |
| FSharpDataAdaptive | 500        | 2,530.62 μs | 9.717 μs | 9.089 μs | 35.41 |    0.15 | 289.0625 | 285.1563 | 7.8125 | 2434678 B |          NA |

### ListTransformBenchmarks

| Method             | Iterations | Mean       | Error    | StdDev   | Ratio | RatioSD | Gen0     | Gen1     | Gen2   | Allocated | Alloc Ratio |
|------------------- |----------- |-----------:|---------:|---------:|------:|--------:|---------:|---------:|-------:|----------:|------------:|
| AdaptiveSlop       | 500        |   158.8 μs |  0.53 μs |  0.50 μs |  1.00 |    0.00 |        - |        - |      - |         - |          NA |
| FSharpDataAdaptive | 500        | 2,840.7 μs | 18.81 μs | 15.71 μs | 17.88 |    0.11 | 343.7500 | 167.9688 | 7.8125 | 2899669 B |          NA |

### ListWriteReadBenchmarks

| Method             | Count  | Mean      | Error     | StdDev     | Median    | Ratio | RatioSD | Allocated  | Alloc Ratio |
|------------------- |------- |----------:|----------:|-----------:|----------:|------:|--------:|-----------:|------------:|
| **AdaptiveSlop**       | **0**      |  **24.07 μs** |  **0.480 μs** |   **0.801 μs** |  **24.08 μs** |  **1.00** |    **0.05** |   **17.29 KB** |        **1.00** |
| FSharpDataAdaptive | 0      | 251.06 μs |  4.992 μs |   3.897 μs | 250.46 μs | 10.44 |    0.37 |  141.05 KB |        8.16 |
|                    |        |           |           |            |           |       |         |            |             |
| **AdaptiveSlop**       | **1000**   |  **24.95 μs** |  **2.844 μs** |   **8.342 μs** |  **20.57 μs** |  **1.11** |    **0.51** |   **52.34 KB** |        **1.00** |
| FSharpDataAdaptive | 1000   | 229.28 μs | 36.093 μs | 105.285 μs | 171.77 μs | 10.16 |    5.71 |  180.94 KB |        3.46 |
|                    |        |           |           |            |           |       |         |            |             |
| **AdaptiveSlop**       | **10000**  |  **36.88 μs** |  **3.618 μs** |  **10.322 μs** |  **35.62 μs** |  **1.08** |    **0.44** |  **247.23 KB** |        **1.00** |
| FSharpDataAdaptive | 10000  | 192.41 μs |  9.387 μs |  26.166 μs | 184.87 μs |  5.65 |    1.81 |  202.94 KB |        0.82 |
|                    |        |           |           |            |           |       |         |            |             |
| **AdaptiveSlop**       | **100000** | **276.91 μs** |  **5.559 μs** |  **14.350 μs** | **273.68 μs** |  **1.00** |    **0.07** | **2356.61 KB** |        **1.00** |
| FSharpDataAdaptive | 100000 | 224.53 μs |  4.824 μs |  13.764 μs | 222.95 μs |  0.81 |    0.06 |  222.94 KB |        0.09 |

### Map2Benchmarks

| Method             | Iterations | Mean      | Error    | StdDev   | Ratio | RatioSD | Gen0    | Allocated | Alloc Ratio |
|------------------- |----------- |----------:|---------:|---------:|------:|--------:|--------:|----------:|------------:|
| AdaptiveSlop       | 1000       |  68.16 μs | 0.175 μs | 0.163 μs |  1.00 |    0.00 |       - |         - |          NA |
| FSharpDataAdaptive | 1000       | 400.48 μs | 1.626 μs | 1.521 μs |  5.88 |    0.03 | 62.9883 |  528000 B |          NA |

### MapBenchmarks

| Method             | Iterations | Mean      | Error    | StdDev   | Ratio | RatioSD | Gen0     | Allocated | Alloc Ratio |
|------------------- |----------- |----------:|---------:|---------:|------:|--------:|---------:|----------:|------------:|
| AdaptiveSlop       | 1000       |  54.57 μs | 0.109 μs | 0.096 μs |  1.00 |    0.00 |        - |         - |          NA |
| FSharpDataAdaptive | 1000       | 309.47 μs | 2.996 μs | 2.803 μs |  5.67 |    0.05 | 104.9805 |  880000 B |          NA |

### MapTransformBenchmarks

| Method             | Iterations | Mean       | Error   | StdDev  | Ratio | RatioSD | Gen0     | Allocated | Alloc Ratio |
|------------------- |----------- |-----------:|--------:|--------:|------:|--------:|---------:|----------:|------------:|
| AdaptiveSlop       | 500        |   119.6 μs | 0.20 μs | 0.18 μs |  1.00 |    0.00 |        - |         - |          NA |
| FSharpDataAdaptive | 500        | 1,137.3 μs | 9.25 μs | 8.65 μs |  9.51 |    0.07 | 210.9375 | 1769408 B |          NA |

### OptimizedWideTreeBenchmarks

| Method                 | Width | Iterations | Mean        | Error    | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|----------------------- |------ |----------- |------------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| **AdaptiveSlop_Map2Chain** | **10**    | **100**        |    **29.80 μs** | **0.065 μs** | **0.058 μs** |  **1.00** |    **0.00** |      **-** |         **-** |          **NA** |
| AdaptiveSlop_Reduce    | 10    | 100        |    10.06 μs | 0.030 μs | 0.028 μs |  0.34 |    0.00 |      - |         - |          NA |
| FSharpDataAdaptive     | 10    | 100        |    85.24 μs | 0.467 μs | 0.437 μs |  2.86 |    0.02 | 5.6152 |   47200 B |          NA |
|                        |       |            |             |          |          |       |         |        |           |             |
| **AdaptiveSlop_Map2Chain** | **50**    | **100**        |   **201.57 μs** | **0.530 μs** | **0.496 μs** |  **1.00** |    **0.00** |      **-** |         **-** |          **NA** |
| AdaptiveSlop_Reduce    | 50    | 100        |    41.76 μs | 0.120 μs | 0.112 μs |  0.21 |    0.00 |      - |         - |          NA |
| FSharpDataAdaptive     | 50    | 100        |   345.16 μs | 0.545 μs | 0.483 μs |  1.71 |    0.00 | 5.3711 |   47200 B |          NA |
|                        |       |            |             |          |          |       |         |        |           |             |
| **AdaptiveSlop_Map2Chain** | **100**   | **100**        |   **458.20 μs** | **2.884 μs** | **2.698 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| AdaptiveSlop_Reduce    | 100   | 100        |    82.18 μs | 0.182 μs | 0.170 μs |  0.18 |    0.00 |      - |         - |          NA |
| FSharpDataAdaptive     | 100   | 100        |   672.58 μs | 1.977 μs | 1.849 μs |  1.47 |    0.01 | 4.8828 |   47200 B |          NA |
|                        |       |            |             |          |          |       |         |        |           |             |
| **AdaptiveSlop_Map2Chain** | **500**   | **100**        | **2,300.94 μs** | **7.047 μs** | **6.592 μs** |  **1.00** |    **0.00** |      **-** |         **-** |          **NA** |
| AdaptiveSlop_Reduce    | 500   | 100        |   404.40 μs | 0.640 μs | 0.599 μs |  0.18 |    0.00 |      - |         - |          NA |
| FSharpDataAdaptive     | 500   | 100        | 3,395.71 μs | 7.459 μs | 6.977 μs |  1.48 |    0.01 | 3.9063 |   47200 B |          NA |

### ReadHeavyBenchmarks

| Method             | WriteCount | ReadsPerWrite | Mean      | Error    | StdDev   | Ratio | Gen0   | Allocated | Alloc Ratio |
|------------------- |----------- |-------------- |----------:|---------:|---------:|------:|-------:|----------:|------------:|
| AdaptiveSlop       | 100        | 50            |  46.93 μs | 0.073 μs | 0.068 μs |  1.00 |      - |         - |          NA |
| FSharpDataAdaptive | 100        | 50            | 113.82 μs | 0.268 μs | 0.251 μs |  2.43 | 5.6152 |   47200 B |          NA |

### SetBenchmarks

| Method             | Iterations | Mean      | Error    | StdDev   | Ratio | RatioSD | Gen0     | Allocated | Alloc Ratio |
|------------------- |----------- |----------:|---------:|---------:|------:|--------:|---------:|----------:|------------:|
| AdaptiveSlop       | 1000       |  50.42 μs | 0.093 μs | 0.087 μs |  1.00 |    0.00 |        - |         - |          NA |
| FSharpDataAdaptive | 1000       | 319.60 μs | 2.233 μs | 2.088 μs |  6.34 |    0.04 | 105.9570 |  888000 B |          NA |

### SetTransformBenchmarks

| Method             | Iterations | Mean       | Error   | StdDev  | Ratio | RatioSD | Gen0     | Allocated | Alloc Ratio |
|------------------- |----------- |-----------:|--------:|--------:|------:|--------:|---------:|----------:|------------:|
| AdaptiveSlop       | 500        |   125.8 μs | 0.33 μs | 0.31 μs |  1.00 |    0.00 |        - |         - |          NA |
| FSharpDataAdaptive | 500        | 1,286.3 μs | 9.10 μs | 8.51 μs | 10.23 |    0.07 | 185.5469 | 1561456 B |          NA |

### TransactionBenchmarks

| Method                     | ValueCount | Iterations | Mean       | Error   | StdDev  | Ratio | Gen0    | Allocated | Alloc Ratio |
|--------------------------- |----------- |----------- |-----------:|--------:|--------:|------:|--------:|----------:|------------:|
| AdaptiveSlop_Batched       | 10         | 500        |   351.4 μs | 0.68 μs | 0.56 μs |  1.00 |  1.4648 |  15.63 KB |        1.00 |
| FSharpDataAdaptive_Batched | 10         | 500        | 1,481.1 μs | 3.18 μs | 2.66 μs |  4.21 | 87.8906 | 726.56 KB |       46.50 |

### UnbalancedTreeBenchmarks

| Method                     | DeepBranchDepth | ShallowBranchCount | Iterations | Mean      | Error    | StdDev   | Ratio | Gen0   | Allocated | Alloc Ratio |
|--------------------------- |---------------- |------------------- |----------- |----------:|---------:|---------:|------:|-------:|----------:|------------:|
| **AdaptiveSlop_DeepChange**    | **10**              | **5**                  | **50**         |  **44.60 μs** | **0.130 μs** | **0.122 μs** |  **1.00** |      **-** |         **-** |          **NA** |
| FDA_DeepChange             | 10              | 5                  | 50         | 104.25 μs | 0.294 μs | 0.275 μs |  2.34 | 2.8076 |   23600 B |          NA |
| AdaptiveSlop_ShallowChange | 10              | 5                  | 50         |  21.89 μs | 0.059 μs | 0.055 μs |  0.49 |      - |         - |          NA |
| FDA_ShallowChange          | 10              | 5                  | 50         |  50.08 μs | 0.107 μs | 0.094 μs |  1.12 | 2.8076 |   23600 B |          NA |
|                            |                 |                    |            |           |          |          |       |        |           |             |
| **AdaptiveSlop_DeepChange**    | **10**              | **20**                 | **50**         | **107.39 μs** | **0.212 μs** | **0.188 μs** |  **1.00** |      **-** |         **-** |          **NA** |
| FDA_DeepChange             | 10              | 20                 | 50         | 211.28 μs | 0.353 μs | 0.330 μs |  1.97 | 2.6855 |   23600 B |          NA |
| AdaptiveSlop_ShallowChange | 10              | 20                 | 50         |  78.43 μs | 0.138 μs | 0.129 μs |  0.73 |      - |         - |          NA |
| FDA_ShallowChange          | 10              | 20                 | 50         | 155.34 μs | 0.405 μs | 0.378 μs |  1.45 | 2.6855 |   23600 B |          NA |
|                            |                 |                    |            |           |          |          |       |        |           |             |
| **AdaptiveSlop_DeepChange**    | **10**              | **50**                 | **50**         | **235.37 μs** | **0.700 μs** | **0.655 μs** |  **1.00** |      **-** |         **-** |          **NA** |
| FDA_DeepChange             | 10              | 50                 | 50         | 426.31 μs | 0.659 μs | 0.584 μs |  1.81 | 2.4414 |   23600 B |          NA |
| AdaptiveSlop_ShallowChange | 10              | 50                 | 50         | 203.99 μs | 1.289 μs | 1.205 μs |  0.87 |      - |         - |          NA |
| FDA_ShallowChange          | 10              | 50                 | 50         | 365.08 μs | 1.016 μs | 0.950 μs |  1.55 | 2.4414 |   23600 B |          NA |
|                            |                 |                    |            |           |          |          |       |        |           |             |
| **AdaptiveSlop_DeepChange**    | **50**              | **5**                  | **50**         | **176.06 μs** | **0.368 μs** | **0.344 μs** |  **1.00** |      **-** |         **-** |          **NA** |
| FDA_DeepChange             | 50              | 5                  | 50         | 315.90 μs | 0.627 μs | 0.586 μs |  1.79 | 2.4414 |   23600 B |          NA |
| AdaptiveSlop_ShallowChange | 50              | 5                  | 50         |  57.68 μs | 0.276 μs | 0.258 μs |  0.33 |      - |         - |          NA |
| FDA_ShallowChange          | 50              | 5                  | 50         |  50.35 μs | 0.212 μs | 0.199 μs |  0.29 | 2.8076 |   23600 B |          NA |
|                            |                 |                    |            |           |          |          |       |        |           |             |
| **AdaptiveSlop_DeepChange**    | **50**              | **20**                 | **50**         | **243.17 μs** | **0.905 μs** | **0.847 μs** |  **1.00** |      **-** |         **-** |          **NA** |
| FDA_DeepChange             | 50              | 20                 | 50         | 448.20 μs | 1.511 μs | 1.413 μs |  1.84 | 2.4414 |   23600 B |          NA |
| AdaptiveSlop_ShallowChange | 50              | 20                 | 50         | 122.99 μs | 0.959 μs | 0.897 μs |  0.51 |      - |         - |          NA |
| FDA_ShallowChange          | 50              | 20                 | 50         | 159.55 μs | 0.437 μs | 0.387 μs |  0.66 | 2.6855 |   23600 B |          NA |
|                            |                 |                    |            |           |          |          |       |        |           |             |
| **AdaptiveSlop_DeepChange**    | **50**              | **50**                 | **50**         | **399.17 μs** | **1.257 μs** | **1.176 μs** |  **1.00** |      **-** |         **-** |          **NA** |
| FDA_DeepChange             | 50              | 50                 | 50         | 676.40 μs | 1.131 μs | 0.944 μs |  1.69 | 1.9531 |   23600 B |          NA |
| AdaptiveSlop_ShallowChange | 50              | 50                 | 50         | 249.06 μs | 0.667 μs | 0.592 μs |  0.62 |      - |         - |          NA |
| FDA_ShallowChange          | 50              | 50                 | 50         | 372.07 μs | 2.352 μs | 2.085 μs |  0.93 | 2.4414 |   23600 B |          NA |
|                            |                 |                    |            |           |          |          |       |        |           |             |
| **AdaptiveSlop_DeepChange**    | **100**             | **5**                  | **50**         | **346.39 μs** | **0.767 μs** | **0.717 μs** |  **1.00** |      **-** |         **-** |          **NA** |
| FDA_DeepChange             | 100             | 5                  | 50         | 575.74 μs | 1.648 μs | 1.542 μs |  1.66 | 1.9531 |   23600 B |          NA |
| AdaptiveSlop_ShallowChange | 100             | 5                  | 50         | 103.92 μs | 0.787 μs | 0.697 μs |  0.30 |      - |         - |          NA |
| FDA_ShallowChange          | 100             | 5                  | 50         |  49.22 μs | 0.135 μs | 0.126 μs |  0.14 | 2.8076 |   23600 B |          NA |
|                            |                 |                    |            |           |          |          |       |        |           |             |
| **AdaptiveSlop_DeepChange**    | **100**             | **20**                 | **50**         | **409.30 μs** | **1.335 μs** | **1.249 μs** |  **1.00** |      **-** |         **-** |          **NA** |
| FDA_DeepChange             | 100             | 20                 | 50         | 708.82 μs | 1.843 μs | 1.724 μs |  1.73 | 1.9531 |   23600 B |          NA |
| AdaptiveSlop_ShallowChange | 100             | 20                 | 50         | 164.55 μs | 2.515 μs | 2.352 μs |  0.40 |      - |         - |          NA |
| FDA_ShallowChange          | 100             | 20                 | 50         | 162.56 μs | 0.348 μs | 0.325 μs |  0.40 | 2.6855 |   23600 B |          NA |
|                            |                 |                    |            |           |          |          |       |        |           |             |
| **AdaptiveSlop_DeepChange**    | **100**             | **50**                 | **50**         | **539.44 μs** | **2.292 μs** | **2.144 μs** |  **1.00** |      **-** |         **-** |          **NA** |
| FDA_DeepChange             | 100             | 50                 | 50         | 975.04 μs | 2.897 μs | 2.710 μs |  1.81 | 1.9531 |   23600 B |          NA |
| AdaptiveSlop_ShallowChange | 100             | 50                 | 50         | 293.30 μs | 1.453 μs | 1.359 μs |  0.54 |      - |         - |          NA |
| FDA_ShallowChange          | 100             | 50                 | 50         | 367.57 μs | 0.905 μs | 0.756 μs |  0.68 | 2.4414 |   23600 B |          NA |

### ValueBenchmarks

| Method             | Iterations | Mean      | Error    | StdDev   | Ratio | RatioSD | Gen0    | Allocated | Alloc Ratio |
|------------------- |----------- |----------:|---------:|---------:|------:|--------:|--------:|----------:|------------:|
| AdaptiveSlop       | 1000       |  48.15 μs | 0.147 μs | 0.138 μs |  1.00 |    0.00 |       - |         - |          NA |
| FSharpDataAdaptive | 1000       | 301.90 μs | 1.640 μs | 1.454 μs |  6.27 |    0.03 | 56.1523 |  472000 B |          NA |

### WideTreeBenchmarks

| Method             | Width | Iterations | Mean        | Error     | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------- |------ |----------- |------------:|----------:|----------:|------:|--------:|-------:|----------:|------------:|
| **AdaptiveSlop**       | **10**    | **100**        |    **30.67 μs** |  **0.169 μs** |  **0.158 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 10    | 100        |    84.35 μs |  0.223 μs |  0.198 μs |  2.75 |    0.02 | 5.6152 |   47200 B |          NA |
|                    |       |            |             |           |           |       |         |        |           |             |
| **AdaptiveSlop**       | **50**    | **100**        |   **198.94 μs** |  **0.719 μs** |  **0.672 μs** |  **1.00** |    **0.00** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 50    | 100        |   338.01 μs |  0.891 μs |  0.790 μs |  1.70 |    0.01 | 5.3711 |   47200 B |          NA |
|                    |       |            |             |           |           |       |         |        |           |             |
| **AdaptiveSlop**       | **100**   | **100**        |   **452.23 μs** |  **1.980 μs** |  **1.654 μs** |  **1.00** |    **0.00** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 100   | 100        |   666.93 μs |  2.066 μs |  1.933 μs |  1.47 |    0.01 | 4.8828 |   47200 B |          NA |
|                    |       |            |             |           |           |       |         |        |           |             |
| **AdaptiveSlop**       | **500**   | **100**        | **2,264.01 μs** | **14.654 μs** | **13.707 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 500   | 100        | 3,421.83 μs | 14.785 μs | 13.830 μs |  1.51 |    0.01 | 3.9063 |   47200 B |          NA |


---

## 2026-08-05 — DefaultJob full run (post property-suite)

- Commit: cf16371 (+ uncommitted benchmark-results/)
- Machine: WSL2 Fedora Remix, .NET 8.0.29, X64 RyuJIT x86-64-v3
- Job: DefaultJob
- State of the code: post property-suite. Includes the element-node write-generation fix,
  the FrozenSet view-cast fixes, the fold mid-insert fix, and the journal off-by-one fix.
- Raw reports: `benchmark-results/` (CSV, Markdown, HTML per benchmark, plus the console log).

### BindBenchmarks

| Method             | Iterations | Mean      | Error     | StdDev    | Ratio | RatioSD | Gen0    | Allocated | Alloc Ratio |
|------------------- |----------- |----------:|----------:|----------:|------:|--------:|--------:|----------:|------------:|
| AdaptiveSlop       | 1000       |  72.80 μs |  0.635 μs |  0.594 μs |  1.00 |    0.01 |       - |         - |          NA |
| FSharpDataAdaptive | 1000       | 525.99 μs | 10.247 μs | 11.800 μs |  7.23 |    0.17 | 69.3359 |  584000 B |          NA |

### ConcurrentBenchmarks

| Method             | ThreadCount | IterationsPerThread | Mean      | Error    | StdDev    | Ratio | RatioSD | Gen0     | Gen1   | Allocated | Alloc Ratio |
|------------------- |------------ |-------------------- |----------:|---------:|----------:|------:|--------:|---------:|-------:|----------:|------------:|
| AdaptiveSlop       | 4           | 500                 |  56.45 μs | 1.060 μs |  1.134 μs |  1.00 |    0.03 |   0.0610 |      - |     849 B |        1.00 |
| FSharpDataAdaptive | 4           | 500                 | 518.39 μs | 9.847 μs | 10.112 μs |  9.19 |    0.25 | 112.3047 | 0.9766 |  944848 B |    1,112.90 |

### DeepChainBenchmarks

| Method             | Depth | Iterations | Mean         | Error     | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------- |------ |----------- |-------------:|----------:|----------:|------:|--------:|-------:|----------:|------------:|
| **AdaptiveSlop**       | **5**     | **100**        |     **22.78 μs** |  **0.117 μs** |  **0.097 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 5     | 100        |     75.01 μs |  1.067 μs |  0.998 μs |  3.29 |    0.04 | 5.6152 |   47200 B |          NA |
|                    |       |            |              |           |           |       |         |        |           |             |
| **AdaptiveSlop**       | **10**    | **100**        |     **50.74 μs** |  **0.387 μs** |  **0.343 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 10    | 100        |    125.94 μs |  0.734 μs |  0.686 μs |  2.48 |    0.02 | 5.6152 |   47200 B |          NA |
|                    |       |            |              |           |           |       |         |        |           |             |
| **AdaptiveSlop**       | **20**    | **100**        |    **116.88 μs** |  **0.374 μs** |  **0.350 μs** |  **1.00** |    **0.00** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 20    | 100        |    226.30 μs |  1.279 μs |  1.196 μs |  1.94 |    0.01 | 5.6152 |   47200 B |          NA |
|                    |       |            |              |           |           |       |         |        |           |             |
| **AdaptiveSlop**       | **100**   | **100**        |    **653.95 μs** |  **2.588 μs** |  **2.421 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 100   | 100        |  1,150.97 μs | 12.785 μs | 11.959 μs |  1.76 |    0.02 | 3.9063 |   47200 B |          NA |
|                    |       |            |              |           |           |       |         |        |           |             |
| **AdaptiveSlop**       | **1000**  | **100**        |  **6,845.20 μs** | **47.984 μs** | **44.884 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 1000  | 100        | 11,267.29 μs | 94.003 μs | 83.331 μs |  1.65 |    0.02 |      - |   47200 B |          NA |

### DeepWideBenchmarks

| Method             | Depth | BranchingFactor | Iterations | Mean         | Error      | StdDev     | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------- |------ |---------------- |----------- |-------------:|-----------:|-----------:|------:|--------:|-------:|----------:|------------:|
| **AdaptiveSlop**       | **3**     | **2**               | **50**         |     **10.78 μs** |   **0.061 μs** |   **0.057 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 3     | 2               | 50         |     32.49 μs |   0.372 μs |   0.330 μs |  3.01 |    0.03 | 2.8076 |   23600 B |          NA |
|                    |       |                 |            |              |            |            |       |         |        |           |             |
| **AdaptiveSlop**       | **3**     | **3**               | **50**         |     **26.18 μs** |   **0.213 μs** |   **0.199 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 3     | 3               | 50         |     53.48 μs |   0.174 μs |   0.163 μs |  2.04 |    0.02 | 2.8076 |   23600 B |          NA |
|                    |       |                 |            |              |            |            |       |         |        |           |             |
| **AdaptiveSlop**       | **3**     | **4**               | **50**         |     **53.32 μs** |   **0.375 μs** |   **0.351 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 3     | 4               | 50         |     68.08 μs |   0.396 μs |   0.351 μs |  1.28 |    0.01 | 2.8076 |   23600 B |          NA |
|                    |       |                 |            |              |            |            |       |         |        |           |             |
| **AdaptiveSlop**       | **5**     | **2**               | **50**         |     **26.35 μs** |   **0.220 μs** |   **0.195 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 5     | 2               | 50         |     45.62 μs |   0.303 μs |   0.283 μs |  1.73 |    0.02 | 2.8076 |   23600 B |          NA |
|                    |       |                 |            |              |            |            |       |         |        |           |             |
| **AdaptiveSlop**       | **5**     | **3**               | **50**         |    **183.74 μs** |   **1.345 μs** |   **1.258 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 5     | 3               | 50         |     84.30 μs |   0.375 μs |   0.333 μs |  0.46 |    0.00 | 2.8076 |   23600 B |          NA |
|                    |       |                 |            |              |            |            |       |         |        |           |             |
| **AdaptiveSlop**       | **5**     | **4**               | **50**         |    **610.44 μs** |   **4.594 μs** |   **4.072 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 5     | 4               | 50         |    114.61 μs |   0.899 μs |   0.797 μs |  0.19 |    0.00 | 2.8076 |   23600 B |          NA |
|                    |       |                 |            |              |            |            |       |         |        |           |             |
| **AdaptiveSlop**       | **7**     | **2**               | **50**         |     **80.34 μs** |   **0.588 μs** |   **0.550 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 7     | 2               | 50         |     60.48 μs |   0.305 μs |   0.285 μs |  0.75 |    0.01 | 2.8076 |   23600 B |          NA |
|                    |       |                 |            |              |            |            |       |         |        |           |             |
| **AdaptiveSlop**       | **7**     | **3**               | **50**         |  **1,431.17 μs** |   **7.205 μs** |   **6.387 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 7     | 3               | 50         |    115.82 μs |   0.413 μs |   0.387 μs |  0.08 |    0.00 | 2.8076 |   23600 B |          NA |
|                    |       |                 |            |              |            |            |       |         |        |           |             |
| **AdaptiveSlop**       | **7**     | **4**               | **50**         | **13,610.44 μs** | **163.434 μs** | **152.877 μs** |  **1.00** |    **0.02** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 7     | 4               | 50         |    161.30 μs |   0.465 μs |   0.412 μs |  0.01 |    0.00 | 2.6855 |   23600 B |          NA |

### DiamondGraphBenchmarks

| Method             | Iterations | Mean     | Error   | StdDev  | Ratio | RatioSD | Gen0    | Allocated | Alloc Ratio |
|------------------- |----------- |---------:|--------:|--------:|------:|--------:|--------:|----------:|------------:|
| AdaptiveSlop       | 1000       | 141.5 μs | 0.52 μs | 0.43 μs |  1.00 |    0.00 |       - |         - |          NA |
| FSharpDataAdaptive | 1000       | 685.2 μs | 3.99 μs | 3.73 μs |  4.84 |    0.03 | 73.2422 |  616000 B |          NA |

### IncrementalChainBenchmarks

| Method             | InitialSize | Mutations | Mean      | Error    | StdDev   | Ratio | RatioSD | Gen0     | Allocated | Alloc Ratio |
|------------------- |------------ |---------- |----------:|---------:|---------:|------:|--------:|---------:|----------:|------------:|
| **AdaptiveSlop**       | **100**         | **200**       |  **52.51 μs** | **0.287 μs** | **0.268 μs** |  **1.00** |    **0.01** |        **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 100         | 200       | 591.96 μs | 4.120 μs | 3.652 μs | 11.27 |    0.09 |  76.1719 |  638704 B |          NA |
|                    |             |           |           |          |          |       |         |          |           |             |
| **AdaptiveSlop**       | **1000**        | **200**       |  **52.98 μs** | **0.183 μs** | **0.153 μs** |  **1.00** |    **0.00** |        **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 1000        | 200       | 583.19 μs | 5.908 μs | 5.526 μs | 11.01 |    0.11 |  78.1250 |  656512 B |          NA |
|                    |             |           |           |          |          |       |         |          |           |             |
| **AdaptiveSlop**       | **10000**       | **200**       |  **53.74 μs** | **0.440 μs** | **0.367 μs** |  **1.00** |    **0.01** |        **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 10000       | 200       | 651.09 μs | 7.574 μs | 7.085 μs | 12.12 |    0.15 | 101.5625 |  852736 B |          NA |

### KipoPhysicsBenchmarks

| Method                         | EntityCount | Iterations | Mean       | Error       | StdDev      | Median     | Ratio | RatioSD | Gen0       | Gen1       | Gen2       | Allocated    | Alloc Ratio |
|------------------------------- |------------ |----------- |-----------:|------------:|------------:|-----------:|------:|--------:|-----------:|-----------:|-----------:|-------------:|------------:|
| **AdaptiveSlop**                   | **250**         | **50**         |   **2.637 ms** |   **0.0290 ms** |   **0.0272 ms** |   **2.639 ms** |  **1.00** |    **0.01** |   **671.8750** |   **132.8125** |          **-** |   **5502.73 KB** |       **1.000** |
| FSharpDataAdaptive             | 250         | 50         |   7.796 ms |   0.0683 ms |   0.0605 ms |   7.815 ms |  2.96 |    0.04 |  1718.7500 |   250.0000 |          - |  14114.82 KB |       2.565 |
| AdaptiveSlop_GraphDirect       | 250         | 50         |   1.002 ms |   0.0046 ms |   0.0039 ms |   1.003 ms |  0.38 |    0.00 |     1.9531 |          - |          - |     21.48 KB |       0.004 |
| FSharpDataAdaptive_GraphDirect | 250         | 50         | 103.979 ms |   4.7007 ms |  13.8602 ms | 108.664 ms | 39.44 |    5.25 | 15600.0000 | 14600.0000 | 14000.0000 | 125574.53 KB |      22.820 |
|                                |             |            |            |             |             |            |       |         |            |            |            |              |             |
| **AdaptiveSlop**                   | **1000**        | **50**         |  **10.846 ms** |   **0.0978 ms** |   **0.0914 ms** |  **10.854 ms** |  **1.00** |    **0.01** |  **2906.2500** |  **1046.8750** |          **-** |  **23793.67 KB** |       **1.000** |
| FSharpDataAdaptive             | 1000        | 50         |  40.868 ms |   0.2488 ms |   0.2206 ms |  40.851 ms |  3.77 |    0.04 |  7615.3846 |  2230.7692 |          - |  62674.11 KB |       2.634 |
| AdaptiveSlop_GraphDirect       | 1000        | 50         |   3.986 ms |   0.0281 ms |   0.0263 ms |   3.983 ms |  0.37 |    0.00 |          - |          - |          - |     21.48 KB |       0.001 |
| FSharpDataAdaptive_GraphDirect | 1000        | 50         | 892.906 ms | 146.6703 ms | 432.4606 ms | 662.639 ms | 82.33 |   39.69 | 58000.0000 | 56000.0000 | 49000.0000 | 490293.27 KB |      20.606 |

### LargeCollectionBenchmarks

| Method             | InitialSize | Iterations | Mean      | Error     | StdDev    | Ratio | RatioSD | Gen0    | Gen1   | Allocated | Alloc Ratio |
|------------------- |------------ |----------- |----------:|----------:|----------:|------:|--------:|--------:|-------:|----------:|------------:|
| AdaptiveSlop       | 10000       | 200        |  2.535 μs | 0.0185 μs | 0.0173 μs |  1.00 |    0.01 |       - |      - |         - |          NA |
| FSharpDataAdaptive | 10000       | 200        | 90.766 μs | 0.7126 μs | 0.6317 μs | 35.80 |    0.34 | 28.0762 | 1.7090 |  235456 B |          NA |

### ListAppendBenchmarks

| Method             | Iterations | Mean        | Error     | StdDev    | Ratio | RatioSD | Gen0     | Gen1     | Gen2   | Allocated | Alloc Ratio |
|------------------- |----------- |------------:|----------:|----------:|------:|--------:|---------:|---------:|-------:|----------:|------------:|
| AdaptiveSlop       | 500        |    72.88 μs |  0.607 μs |  0.538 μs |  1.00 |    0.01 |        - |        - |      - |         - |          NA |
| FSharpDataAdaptive | 500        | 2,813.81 μs | 16.426 μs | 14.561 μs | 38.61 |    0.34 | 289.0625 | 285.1563 | 7.8125 | 2435566 B |          NA |

### ListTransformBenchmarks

| Method             | Iterations | Mean       | Error   | StdDev  | Ratio | RatioSD | Gen0     | Gen1     | Gen2   | Allocated | Alloc Ratio |
|------------------- |----------- |-----------:|--------:|--------:|------:|--------:|---------:|---------:|-------:|----------:|------------:|
| AdaptiveSlop       | 500        |   163.6 μs | 1.07 μs | 0.95 μs |  1.00 |    0.01 |        - |        - |      - |         - |          NA |
| FSharpDataAdaptive | 500        | 3,124.8 μs | 7.60 μs | 7.11 μs | 19.10 |    0.12 | 343.7500 | 167.9688 | 7.8125 | 2900058 B |          NA |

### ListWriteReadBenchmarks

| Method             | Count  | Mean      | Error    | StdDev    | Median    | Ratio | RatioSD | Allocated  | Alloc Ratio |
|------------------- |------- |----------:|---------:|----------:|----------:|------:|--------:|-----------:|------------:|
| **AdaptiveSlop**       | **0**      |  **23.34 μs** | **0.467 μs** |  **0.933 μs** |  **23.35 μs** |  **1.00** |    **0.06** |   **17.29 KB** |        **1.00** |
| FSharpDataAdaptive | 0      | 257.82 μs | 4.921 μs |  4.109 μs | 255.93 μs | 11.06 |    0.49 |  141.05 KB |        8.16 |
|                    |        |           |          |           |           |       |         |            |             |
| **AdaptiveSlop**       | **1000**   |  **28.41 μs** | **1.907 μs** |  **5.156 μs** |  **29.99 μs** |  **1.04** |    **0.29** |   **52.34 KB** |        **1.00** |
| FSharpDataAdaptive | 1000   | 348.19 μs | 6.904 μs | 11.909 μs | 345.15 μs | 12.73 |    2.74 |  180.85 KB |        3.46 |
|                    |        |           |          |           |           |       |         |            |             |
| **AdaptiveSlop**       | **10000**  |  **35.39 μs** | **2.858 μs** |  **8.153 μs** |  **35.43 μs** |  **1.06** |    **0.36** |  **247.23 KB** |        **1.00** |
| FSharpDataAdaptive | 10000  | 192.82 μs | 7.108 μs | 19.695 μs | 189.47 μs |  5.76 |    1.53 |  203.11 KB |        0.82 |
|                    |        |           |          |           |           |       |         |            |             |
| **AdaptiveSlop**       | **100000** | **265.63 μs** | **7.572 μs** | **21.480 μs** | **260.03 μs** |  **1.01** |    **0.11** | **2356.61 KB** |        **1.00** |
| FSharpDataAdaptive | 100000 | 224.96 μs | 4.808 μs | 13.161 μs | 222.87 μs |  0.85 |    0.08 |  222.94 KB |        0.09 |

### Map2Benchmarks

| Method             | Iterations | Mean      | Error    | StdDev   | Ratio | RatioSD | Gen0    | Allocated | Alloc Ratio |
|------------------- |----------- |----------:|---------:|---------:|------:|--------:|--------:|----------:|------------:|
| AdaptiveSlop       | 1000       |  68.50 μs | 0.393 μs | 0.368 μs |  1.00 |    0.01 |       - |         - |          NA |
| FSharpDataAdaptive | 1000       | 472.54 μs | 2.234 μs | 1.980 μs |  6.90 |    0.05 | 62.9883 |  528000 B |          NA |

### MapABenchmarks

| Method                      | ElementCount | Mean      | Error    | StdDev    | Median    | Ratio | RatioSD | Allocated | Alloc Ratio |
|---------------------------- |------------- |----------:|---------:|----------:|----------:|------:|--------:|----------:|------------:|
| **AdaptiveSlopMapA**            | **100**          |  **16.31 μs** | **0.712 μs** |  **2.099 μs** |  **16.92 μs** |  **1.02** |    **0.19** |         **-** |          **NA** |
| NaiveMapForcesOnFullReplace | 100          |  33.46 μs | 0.668 μs |  0.868 μs |  33.32 μs |  2.09 |    0.29 |      80 B |          NA |
| FSharpDataAdaptive          | 100          |  19.29 μs | 0.342 μs |  0.303 μs |  19.23 μs |  1.20 |    0.16 |    1624 B |          NA |
|                             |              |           |          |           |           |       |         |           |             |
| **AdaptiveSlopMapA**            | **1000**         |  **94.21 μs** | **8.336 μs** | **23.099 μs** | **100.17 μs** |  **1.12** |    **0.60** |         **-** |          **NA** |
| NaiveMapForcesOnFullReplace | 1000         | 190.56 μs | 3.756 μs |  5.848 μs | 190.66 μs |  2.27 |    1.05 |      80 B |          NA |
| FSharpDataAdaptive          | 1000         |  23.89 μs | 1.330 μs |  3.728 μs |  22.55 μs |  0.29 |    0.14 |    1912 B |          NA |

### MapBenchmarks

| Method             | Iterations | Mean      | Error    | StdDev   | Ratio | RatioSD | Gen0     | Allocated | Alloc Ratio |
|------------------- |----------- |----------:|---------:|---------:|------:|--------:|---------:|----------:|------------:|
| AdaptiveSlop       | 1000       |  55.65 μs | 0.114 μs | 0.106 μs |  1.00 |    0.00 |        - |         - |          NA |
| FSharpDataAdaptive | 1000       | 340.55 μs | 1.950 μs | 1.824 μs |  6.12 |    0.03 | 104.9805 |  880000 B |          NA |

### MapTransformBenchmarks

| Method             | Iterations | Mean       | Error    | StdDev   | Ratio | RatioSD | Gen0     | Allocated | Alloc Ratio |
|------------------- |----------- |-----------:|---------:|---------:|------:|--------:|---------:|----------:|------------:|
| AdaptiveSlop       | 500        |   132.5 μs |  0.76 μs |  0.67 μs |  1.00 |    0.01 |        - |         - |          NA |
| FSharpDataAdaptive | 500        | 1,267.4 μs | 15.86 μs | 14.06 μs |  9.57 |    0.11 | 210.9375 | 1769408 B |          NA |

### OptimizedWideTreeBenchmarks

| Method                 | Width | Iterations | Mean        | Error     | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|----------------------- |------ |----------- |------------:|----------:|----------:|------:|--------:|-------:|----------:|------------:|
| **AdaptiveSlop_Map2Chain** | **10**    | **100**        |    **35.16 μs** |  **0.383 μs** |  **0.358 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| AdaptiveSlop_Reduce    | 10    | 100        |    10.71 μs |  0.090 μs |  0.084 μs |  0.30 |    0.00 |      - |         - |          NA |
| FSharpDataAdaptive     | 10    | 100        |    91.54 μs |  0.459 μs |  0.429 μs |  2.60 |    0.03 | 5.6152 |   47200 B |          NA |
|                        |       |            |             |           |           |       |         |        |           |             |
| **AdaptiveSlop_Map2Chain** | **50**    | **100**        |   **215.93 μs** |  **0.361 μs** |  **0.282 μs** |  **1.00** |    **0.00** |      **-** |         **-** |          **NA** |
| AdaptiveSlop_Reduce    | 50    | 100        |    44.63 μs |  0.359 μs |  0.318 μs |  0.21 |    0.00 |      - |         - |          NA |
| FSharpDataAdaptive     | 50    | 100        |   364.14 μs |  3.519 μs |  3.291 μs |  1.69 |    0.01 | 5.3711 |   47200 B |          NA |
|                        |       |            |             |           |           |       |         |        |           |             |
| **AdaptiveSlop_Map2Chain** | **100**   | **100**        |   **461.95 μs** |  **4.297 μs** |  **4.019 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| AdaptiveSlop_Reduce    | 100   | 100        |    87.97 μs |  0.810 μs |  0.758 μs |  0.19 |    0.00 |      - |         - |          NA |
| FSharpDataAdaptive     | 100   | 100        |   727.61 μs |  6.495 μs |  5.758 μs |  1.58 |    0.02 | 4.8828 |   47200 B |          NA |
|                        |       |            |             |           |           |       |         |        |           |             |
| **AdaptiveSlop_Map2Chain** | **500**   | **100**        | **2,410.41 μs** | **25.845 μs** | **24.176 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| AdaptiveSlop_Reduce    | 500   | 100        |   437.97 μs |  3.585 μs |  3.354 μs |  0.18 |    0.00 |      - |         - |          NA |
| FSharpDataAdaptive     | 500   | 100        | 3,642.14 μs | 34.328 μs | 32.110 μs |  1.51 |    0.02 | 3.9063 |   47200 B |          NA |

### ReadHeavyBenchmarks

| Method             | WriteCount | ReadsPerWrite | Mean      | Error    | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------- |----------- |-------------- |----------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| AdaptiveSlop       | 100        | 50            |  49.26 μs | 0.492 μs | 0.461 μs |  1.00 |    0.01 |      - |         - |          NA |
| FSharpDataAdaptive | 100        | 50            | 130.05 μs | 1.560 μs | 1.460 μs |  2.64 |    0.04 | 5.6152 |   47200 B |          NA |

### SetBenchmarks

| Method             | Iterations | Mean      | Error    | StdDev   | Ratio | RatioSD | Gen0     | Allocated | Alloc Ratio |
|------------------- |----------- |----------:|---------:|---------:|------:|--------:|---------:|----------:|------------:|
| AdaptiveSlop       | 1000       |  54.20 μs | 0.443 μs | 0.392 μs |  1.00 |    0.01 |        - |         - |          NA |
| FSharpDataAdaptive | 1000       | 362.56 μs | 1.644 μs | 1.458 μs |  6.69 |    0.05 | 105.9570 |  888000 B |          NA |

### SetTransformBenchmarks

| Method             | Iterations | Mean       | Error    | StdDev   | Ratio | RatioSD | Gen0     | Allocated | Alloc Ratio |
|------------------- |----------- |-----------:|---------:|---------:|------:|--------:|---------:|----------:|------------:|
| AdaptiveSlop       | 500        |   135.1 μs |  1.00 μs |  0.94 μs |  1.00 |    0.01 |        - |         - |          NA |
| FSharpDataAdaptive | 500        | 1,393.5 μs | 12.05 μs | 11.27 μs | 10.31 |    0.11 | 185.5469 | 1561456 B |          NA |

### TransactionBenchmarks

| Method                     | ValueCount | Iterations | Mean       | Error    | StdDev   | Ratio | RatioSD | Gen0    | Allocated | Alloc Ratio |
|--------------------------- |----------- |----------- |-----------:|---------:|---------:|------:|--------:|--------:|----------:|------------:|
| AdaptiveSlop_Batched       | 10         | 500        |   384.7 μs |  3.74 μs |  3.50 μs |  1.00 |    0.01 |  1.4648 |  15.63 KB |        1.00 |
| FSharpDataAdaptive_Batched | 10         | 500        | 1,484.0 μs | 16.59 μs | 15.51 μs |  3.86 |    0.05 | 87.8906 | 726.56 KB |       46.50 |

### UnbalancedTreeBenchmarks

| Method                     | DeepBranchDepth | ShallowBranchCount | Iterations | Mean        | Error    | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------------------------- |---------------- |------------------- |----------- |------------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| **AdaptiveSlop_DeepChange**    | **10**              | **5**                  | **50**         |    **47.97 μs** | **0.311 μs** | **0.291 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FDA_DeepChange             | 10              | 5                  | 50         |   110.64 μs | 0.577 μs | 0.482 μs |  2.31 |    0.02 | 2.8076 |   23600 B |          NA |
| AdaptiveSlop_ShallowChange | 10              | 5                  | 50         |    23.94 μs | 0.170 μs | 0.159 μs |  0.50 |    0.00 |      - |         - |          NA |
| FDA_ShallowChange          | 10              | 5                  | 50         |    52.41 μs | 0.320 μs | 0.299 μs |  1.09 |    0.01 | 2.8076 |   23600 B |          NA |
|                            |                 |                    |            |             |          |          |       |         |        |           |             |
| **AdaptiveSlop_DeepChange**    | **10**              | **20**                 | **50**         |   **108.88 μs** | **0.491 μs** | **0.459 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FDA_DeepChange             | 10              | 20                 | 50         |   236.61 μs | 0.983 μs | 0.919 μs |  2.17 |    0.01 | 2.6855 |   23600 B |          NA |
| AdaptiveSlop_ShallowChange | 10              | 20                 | 50         |    83.84 μs | 0.405 μs | 0.379 μs |  0.77 |    0.00 |      - |         - |          NA |
| FDA_ShallowChange          | 10              | 20                 | 50         |   185.03 μs | 0.817 μs | 0.764 μs |  1.70 |    0.01 | 2.6855 |   23600 B |          NA |
|                            |                 |                    |            |             |          |          |       |         |        |           |             |
| **AdaptiveSlop_DeepChange**    | **10**              | **50**                 | **50**         |   **246.08 μs** | **1.098 μs** | **1.027 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FDA_DeepChange             | 10              | 50                 | 50         |   459.83 μs | 2.839 μs | 2.656 μs |  1.87 |    0.01 | 2.4414 |   23600 B |          NA |
| AdaptiveSlop_ShallowChange | 10              | 50                 | 50         |   213.04 μs | 1.417 μs | 1.325 μs |  0.87 |    0.01 |      - |         - |          NA |
| FDA_ShallowChange          | 10              | 50                 | 50         |   396.69 μs | 1.547 μs | 1.371 μs |  1.61 |    0.01 | 2.4414 |   23600 B |          NA |
|                            |                 |                    |            |             |          |          |       |         |        |           |             |
| **AdaptiveSlop_DeepChange**    | **50**              | **5**                  | **50**         |   **185.36 μs** | **0.850 μs** | **0.795 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FDA_DeepChange             | 50              | 5                  | 50         |   324.62 μs | 2.253 μs | 1.997 μs |  1.75 |    0.01 | 2.4414 |   23600 B |          NA |
| AdaptiveSlop_ShallowChange | 50              | 5                  | 50         |    61.61 μs | 0.180 μs | 0.150 μs |  0.33 |    0.00 |      - |         - |          NA |
| FDA_ShallowChange          | 50              | 5                  | 50         |    54.44 μs | 0.336 μs | 0.315 μs |  0.29 |    0.00 | 2.8076 |   23600 B |          NA |
|                            |                 |                    |            |             |          |          |       |         |        |           |             |
| **AdaptiveSlop_DeepChange**    | **50**              | **20**                 | **50**         |   **259.98 μs** | **0.845 μs** | **0.790 μs** |  **1.00** |    **0.00** |      **-** |         **-** |          **NA** |
| FDA_DeepChange             | 50              | 20                 | 50         |   471.38 μs | 2.053 μs | 1.920 μs |  1.81 |    0.01 | 2.4414 |   23600 B |          NA |
| AdaptiveSlop_ShallowChange | 50              | 20                 | 50         |   127.81 μs | 0.948 μs | 0.887 μs |  0.49 |    0.00 |      - |         - |          NA |
| FDA_ShallowChange          | 50              | 20                 | 50         |   166.21 μs | 1.300 μs | 1.216 μs |  0.64 |    0.00 | 2.6855 |   23600 B |          NA |
|                            |                 |                    |            |             |          |          |       |         |        |           |             |
| **AdaptiveSlop_DeepChange**    | **50**              | **50**                 | **50**         |   **412.84 μs** | **2.904 μs** | **2.716 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FDA_DeepChange             | 50              | 50                 | 50         |   790.28 μs | 5.995 μs | 5.608 μs |  1.91 |    0.02 | 1.9531 |   23600 B |          NA |
| AdaptiveSlop_ShallowChange | 50              | 50                 | 50         |   256.11 μs | 1.699 μs | 1.506 μs |  0.62 |    0.01 |      - |         - |          NA |
| FDA_ShallowChange          | 50              | 50                 | 50         |   391.02 μs | 2.960 μs | 2.624 μs |  0.95 |    0.01 | 2.4414 |   23600 B |          NA |
|                            |                 |                    |            |             |          |          |       |         |        |           |             |
| **AdaptiveSlop_DeepChange**    | **100**             | **5**                  | **50**         |   **355.61 μs** | **1.771 μs** | **1.570 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FDA_DeepChange             | 100             | 5                  | 50         |   595.78 μs | 5.839 μs | 5.462 μs |  1.68 |    0.02 | 1.9531 |   23600 B |          NA |
| AdaptiveSlop_ShallowChange | 100             | 5                  | 50         |   110.84 μs | 0.936 μs | 0.830 μs |  0.31 |    0.00 |      - |         - |          NA |
| FDA_ShallowChange          | 100             | 5                  | 50         |    51.31 μs | 0.310 μs | 0.242 μs |  0.14 |    0.00 | 2.8076 |   23600 B |          NA |
|                            |                 |                    |            |             |          |          |       |         |        |           |             |
| **AdaptiveSlop_DeepChange**    | **100**             | **20**                 | **50**         |   **429.75 μs** | **2.877 μs** | **2.692 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FDA_DeepChange             | 100             | 20                 | 50         |   727.63 μs | 3.665 μs | 3.428 μs |  1.69 |    0.01 | 1.9531 |   23600 B |          NA |
| AdaptiveSlop_ShallowChange | 100             | 20                 | 50         |   169.45 μs | 1.226 μs | 1.147 μs |  0.39 |    0.00 |      - |         - |          NA |
| FDA_ShallowChange          | 100             | 20                 | 50         |   163.33 μs | 0.459 μs | 0.429 μs |  0.38 |    0.00 | 2.6855 |   23600 B |          NA |
|                            |                 |                    |            |             |          |          |       |         |        |           |             |
| **AdaptiveSlop_DeepChange**    | **100**             | **50**                 | **50**         |   **570.55 μs** | **2.482 μs** | **2.200 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FDA_DeepChange             | 100             | 50                 | 50         | 1,009.06 μs | 7.301 μs | 6.830 μs |  1.77 |    0.01 | 1.9531 |   23600 B |          NA |
| AdaptiveSlop_ShallowChange | 100             | 50                 | 50         |   303.46 μs | 1.213 μs | 1.135 μs |  0.53 |    0.00 |      - |         - |          NA |
| FDA_ShallowChange          | 100             | 50                 | 50         |   402.77 μs | 1.801 μs | 1.684 μs |  0.71 |    0.00 | 2.4414 |   23600 B |          NA |

### ValueBenchmarks

| Method             | Iterations | Mean      | Error    | StdDev   | Ratio | RatioSD | Gen0    | Allocated | Alloc Ratio |
|------------------- |----------- |----------:|---------:|---------:|------:|--------:|--------:|----------:|------------:|
| AdaptiveSlop       | 1000       |  51.12 μs | 0.365 μs | 0.341 μs |  1.00 |    0.01 |       - |         - |          NA |
| FSharpDataAdaptive | 1000       | 317.81 μs | 2.647 μs | 2.476 μs |  6.22 |    0.06 | 56.1523 |  472000 B |          NA |

### WideTreeBenchmarks

| Method             | Width | Iterations | Mean        | Error     | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------- |------ |----------- |------------:|----------:|----------:|------:|--------:|-------:|----------:|------------:|
| **AdaptiveSlop**       | **10**    | **100**        |    **32.71 μs** |  **0.248 μs** |  **0.219 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 10    | 100        |    92.08 μs |  0.911 μs |  0.853 μs |  2.82 |    0.03 | 5.6152 |   47200 B |          NA |
|                    |       |            |             |           |           |       |         |        |           |             |
| **AdaptiveSlop**       | **50**    | **100**        |   **218.46 μs** |  **1.567 μs** |  **1.389 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 50    | 100        |   359.23 μs |  2.019 μs |  1.789 μs |  1.64 |    0.01 | 5.3711 |   47200 B |          NA |
|                    |       |            |             |           |           |       |         |        |           |             |
| **AdaptiveSlop**       | **100**   | **100**        |   **469.58 μs** |  **5.057 μs** |  **4.730 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 100   | 100        |   713.45 μs |  6.008 μs |  5.620 μs |  1.52 |    0.02 | 4.8828 |   47200 B |          NA |
|                    |       |            |             |           |           |       |         |        |           |             |
| **AdaptiveSlop**       | **500**   | **100**        | **2,472.69 μs** | **25.353 μs** | **22.475 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 500   | 100        | 3,852.09 μs | 23.986 μs | 22.436 μs |  1.56 |    0.02 | 3.9063 |   47200 B |          NA |

## 2026-08-09 — Per-key scalar escapes + DefaultJob full run (#16)

- Commit: e4eac80 (+ cb3ce17, the ScalarEscapeBenchmarks loop fix)
- Machine: Linux Fedora Remix for WSL, AMD Ryzen 9 6900HX 3.29GHz, .NET 10.0.10, x64 RyuJIT
- Job: DefaultJob
- State of the code: pull-only core with the per-key/per-position scalar escape
  nodes (#16): `AMap.tryFind/find/count/isEmpty`, `ASet.contains/count/isEmpty`,
  `AList.tryAt/tryGet/tryFirst/tryLast/count/isEmpty` are delta-sink nodes with
  per-key/per-position gates instead of whole-collection `AdaptiveNode`s. Also
  includes the set/map journal cross-kind coalescing (InDrain) fix.
- Raw reports: `benchmark-results/` (per-benchmark CSV/Markdown/HTML, console log
  `BenchmarkRun-20260809-080112.log`).
- ScalarEscapeBenchmarks note: the first full run exposed an artifact in
  `SlopCountAddWrite` and `SlopContainsUnrelatedWrite` — the loops wrote the
  same keys with the same values on every invocation, so after the first
  invocation every write was a no-op and the rows measured the no-op floor
  (~7 µs, zero alloc, regardless of the write). The loops were fixed
  (add+remove of a fresh key/element per iteration; bounded state) and the
  table below was re-measured with DefaultJob on Windows (AMD Ryzen 9 5900X
  class host, .NET 10.0.9, x64 RyuJIT). The other 23 classes are from the
  full run above, unmodified.

### BindBenchmarks

| AdaptiveSlop       | 1000       |  52.73 μs | 0.210 μs | 0.175 μs |  1.00 |    0.00 |       - |         - |          NA |
| FSharpDataAdaptive | 1000       | 505.33 μs | 2.190 μs | 1.941 μs |  9.58 |    0.05 | 69.3359 |  584000 B |          NA |

### ConcurrentBenchmarks

| AdaptiveSlop       | 4           | 500                 |  51.16 μs | 0.679 μs | 0.602 μs |  1.00 |    0.02 |   0.0610 |     849 B |        1.00 |
| FSharpDataAdaptive | 4           | 500                 | 532.01 μs | 6.013 μs | 5.624 μs | 10.40 |    0.16 | 113.2813 |  944848 B |    1,112.90 |

### DeepChainBenchmarks

| **AdaptiveSlop**       | **5**     | **100**        |    **19.22 μs** |  **0.249 μs** |  **0.221 μs** |  **1.00** |    **0.02** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 5     | 100        |    69.10 μs |  1.370 μs |  1.282 μs |  3.60 |    0.08 | 5.6152 |   47200 B |          NA |
|                    |       |            |             |           |           |       |         |        |           |             |
| **AdaptiveSlop**       | **10**    | **100**        |    **33.56 μs** |  **0.198 μs** |  **0.185 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 10    | 100        |   109.24 μs |  1.133 μs |  1.060 μs |  3.25 |    0.04 | 5.6152 |   47200 B |          NA |
|                    |       |            |             |           |           |       |         |        |           |             |
| **AdaptiveSlop**       | **20**    | **100**        |    **73.56 μs** |  **0.778 μs** |  **0.728 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 20    | 100        |   201.99 μs |  2.114 μs |  1.874 μs |  2.75 |    0.04 | 5.6152 |   47200 B |          NA |
|                    |       |            |             |           |           |       |         |        |           |             |
| **AdaptiveSlop**       | **100**   | **100**        |   **412.96 μs** |  **3.654 μs** |  **3.418 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 100   | 100        |   945.40 μs |  3.033 μs |  2.533 μs |  2.29 |    0.02 | 4.8828 |   47200 B |          NA |
|                    |       |            |             |           |           |       |         |        |           |             |
| **AdaptiveSlop**       | **1000**  | **100**        | **3,928.38 μs** | **15.816 μs** | **14.795 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 1000  | 100        | 9,658.53 μs | 21.794 μs | 20.386 μs |  2.46 |    0.01 |      - |   47200 B |          NA |

### DeepWideBenchmarks

| **AdaptiveSlop**       | **3**     | **2**               | **50**         |     **7.975 μs** |   **0.0278 μs** |   **0.0260 μs** |  **1.00** |    **0.00** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 3     | 2               | 50         |    28.024 μs |   0.1320 μs |   0.1170 μs |  3.51 |    0.02 | 2.8076 |   23600 B |          NA |
|                    |       |                 |            |              |             |             |       |         |        |           |             |
| **AdaptiveSlop**       | **3**     | **3**               | **50**         |    **20.168 μs** |   **0.0544 μs** |   **0.0482 μs** |  **1.00** |    **0.00** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 3     | 3               | 50         |    45.014 μs |   0.5731 μs |   0.5361 μs |  2.23 |    0.03 | 2.8076 |   23600 B |          NA |
|                    |       |                 |            |              |             |             |       |         |        |           |             |
| **AdaptiveSlop**       | **3**     | **4**               | **50**         |    **42.410 μs** |   **0.3078 μs** |   **0.2879 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 3     | 4               | 50         |    57.120 μs |   1.1281 μs |   1.2991 μs |  1.35 |    0.03 | 2.8076 |   23600 B |          NA |
|                    |       |                 |            |              |             |             |       |         |        |           |             |
| **AdaptiveSlop**       | **5**     | **2**               | **50**         |    **19.918 μs** |   **0.1069 μs** |   **0.0834 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 5     | 2               | 50         |    39.047 μs |   0.4980 μs |   0.4658 μs |  1.96 |    0.02 | 2.8076 |   23600 B |          NA |
|                    |       |                 |            |              |             |             |       |         |        |           |             |
| **AdaptiveSlop**       | **5**     | **3**               | **50**         |   **119.930 μs** |   **1.4327 μs** |   **1.3401 μs** |  **1.00** |    **0.02** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 5     | 3               | 50         |    70.200 μs |   1.0902 μs |   1.0198 μs |  0.59 |    0.01 | 2.8076 |   23600 B |          NA |
|                    |       |                 |            |              |             |             |       |         |        |           |             |
| **AdaptiveSlop**       | **5**     | **4**               | **50**         |   **454.120 μs** |   **2.4586 μs** |   **2.1795 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 5     | 4               | 50         |    93.795 μs |   0.6552 μs |   0.6129 μs |  0.21 |    0.00 | 2.8076 |   23600 B |          NA |
|                    |       |                 |            |              |             |             |       |         |        |           |             |
| **AdaptiveSlop**       | **7**     | **2**               | **50**         |    **59.239 μs** |   **0.7299 μs** |   **0.6095 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 7     | 2               | 50         |    51.751 μs |   0.2022 μs |   0.1689 μs |  0.87 |    0.01 | 2.8076 |   23600 B |          NA |
|                    |       |                 |            |              |             |             |       |         |        |           |             |
| **AdaptiveSlop**       | **7**     | **3**               | **50**         | **1,034.273 μs** |   **9.6456 μs** |   **8.0546 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 7     | 3               | 50         |    96.820 μs |   0.5755 μs |   0.5383 μs |  0.09 |    0.00 | 2.8076 |   23600 B |          NA |
|                    |       |                 |            |              |             |             |       |         |        |           |             |
| **AdaptiveSlop**       | **7**     | **4**               | **50**         | **9,424.179 μs** | **179.6559 μs** | **199.6871 μs** |  **1.00** |    **0.03** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 7     | 4               | 50         |   138.888 μs |   2.2415 μs |   1.9870 μs |  0.01 |    0.00 | 2.6855 |   23600 B |          NA |

### DiamondGraphBenchmarks

| AdaptiveSlop       | 1000       | 104.8 μs | 0.30 μs | 0.28 μs |  1.00 |    0.00 |       - |         - |          NA |
| FSharpDataAdaptive | 1000       | 623.9 μs | 2.78 μs | 2.60 μs |  5.95 |    0.03 | 73.2422 |  616000 B |          NA |

### IncrementalChainBenchmarks

| **AdaptiveSlop**       | **100**         | **200**       |  **38.44 μs** | **0.112 μs** |  **0.099 μs** |  **1.00** |    **0.00** |        **-** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 100         | 200       | 475.20 μs | 4.262 μs |  3.987 μs | 12.36 |    0.11 |  76.1719 | 0.4883 |  638704 B |          NA |
|                    |             |           |           |          |           |       |         |          |        |           |             |
| **AdaptiveSlop**       | **1000**        | **200**       |  **38.68 μs** | **0.323 μs** |  **0.302 μs** |  **1.00** |    **0.01** |        **-** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 1000        | 200       | 475.40 μs | 9.456 μs | 11.959 μs | 12.29 |    0.32 |  78.1250 | 0.4883 |  656512 B |          NA |
|                    |             |           |           |          |           |       |         |          |        |           |             |
| **AdaptiveSlop**       | **10000**       | **200**       |  **40.02 μs** | **0.753 μs** |  **0.773 μs** |  **1.00** |    **0.03** |        **-** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 10000       | 200       | 545.30 μs | 8.118 μs |  7.196 μs | 13.63 |    0.31 | 101.5625 |      - |  852736 B |          NA |

### KipoPhysicsBenchmarks

| **AdaptiveSlop**                   | **250**         | **50**         |   **1.638 ms** |   **0.0326 ms** |   **0.0619 ms** |   **1.612 ms** |  **1.00** |    **0.05** |   **585.9375** |    **54.6875** |          **-** |   **4.68 MB** |        **1.00** |
| FSharpDataAdaptive             | 250         | 50         |   6.995 ms |   0.1386 ms |   0.1650 ms |   6.993 ms |  4.28 |    0.18 |  1726.5625 |   242.1875 |          - |  13.79 MB |        2.95 |
| AdaptiveSlop_GraphDirect       | 250         | 50         |   2.280 ms |   0.0452 ms |   0.1048 ms |   2.256 ms |  1.39 |    0.08 |   554.6875 |   109.3750 |          - |   4.43 MB |        0.95 |
| FSharpDataAdaptive_GraphDirect | 250         | 50         |  98.997 ms |   4.4383 ms |  12.8764 ms | 101.360 ms | 60.51 |    8.14 | 16250.0000 | 15625.0000 | 15375.0000 | 127.05 MB |       27.17 |
|                                |             |            |            |             |             |            |       |         |            |            |            |           |             |
| **AdaptiveSlop**                   | **1000**        | **50**         |   **7.712 ms** |   **0.1496 ms** |   **0.1723 ms** |   **7.637 ms** |  **1.00** |    **0.03** |  **2539.0625** |   **843.7500** |          **-** |  **20.31 MB** |        **1.00** |
| FSharpDataAdaptive             | 1000        | 50         |  32.754 ms |   0.5745 ms |   0.5093 ms |  32.651 ms |  4.25 |    0.11 |  7625.0000 |  2312.5000 |          - |  61.24 MB |        3.01 |
| AdaptiveSlop_GraphDirect       | 1000        | 50         |   9.561 ms |   0.1212 ms |   0.1074 ms |   9.590 ms |  1.24 |    0.03 |  2406.2500 |   796.8750 |          - |  19.35 MB |        0.95 |
| FSharpDataAdaptive_GraphDirect | 1000        | 50         | 720.596 ms | 132.4413 ms | 390.5061 ms | 510.871 ms | 93.48 |   50.47 | 56000.0000 | 54000.0000 | 45000.0000 | 464.06 MB |       22.84 |

### LargeCollectionBenchmarks

| AdaptiveSlop       | 10000       | 200        |  1.318 μs | 0.0085 μs | 0.0079 μs |  1.00 |    0.01 |       - |      - |         - |          NA |
| FSharpDataAdaptive | 10000       | 200        | 73.527 μs | 1.4562 μs | 2.5118 μs | 55.80 |    1.91 | 28.0762 | 1.7090 |  235456 B |          NA |

### ListAppendBenchmarks

| AdaptiveSlop       | 500        |    68.39 μs |  0.684 μs |  0.640 μs |  1.00 |    0.01 |        - |        - |      - |         - |          NA |
| FSharpDataAdaptive | 500        | 2,446.46 μs | 47.944 μs | 53.289 μs | 35.78 |    0.83 | 281.2500 | 277.3438 | 7.8125 | 2365155 B |          NA |

### ListTransformBenchmarks

| AdaptiveSlop       | 500        |   166.8 μs |  1.36 μs |  1.27 μs |  1.00 |    0.01 |        - |        - |      - |         - |          NA |
| FSharpDataAdaptive | 500        | 2,690.6 μs | 47.14 μs | 71.99 μs | 16.13 |    0.44 | 343.7500 | 167.9688 | 7.8125 | 2887678 B |          NA |

### ListWriteReadBenchmarks

|------------------- |------- |----------:|----------:|-----------:|----------:|------:|--------:|-----------:|------------:|
| **AdaptiveSlop**       | **0**      |  **21.36 μs** |  **0.425 μs** |   **0.661 μs** |  **21.49 μs** |  **1.00** |    **0.04** |   **17.29 KB** |        **1.00** |
| FSharpDataAdaptive | 0      | 253.74 μs |  3.320 μs |   2.592 μs | 254.35 μs | 11.89 |    0.40 |  141.05 KB |        8.16 |
|                    |        |           |           |            |           |       |         |            |             |
| **AdaptiveSlop**       | **1000**   |  **24.03 μs** |  **2.959 μs** |   **8.678 μs** |  **19.24 μs** |  **1.12** |    **0.55** |   **52.34 KB** |        **1.00** |
| FSharpDataAdaptive | 1000   | 244.44 μs | 39.001 μs | 114.995 μs | 185.61 μs | 11.42 |    6.65 |  177.72 KB |        3.40 |
|                    |        |           |           |            |           |       |         |            |             |
| **AdaptiveSlop**       | **10000**  |  **31.13 μs** |  **2.268 μs** |   **6.652 μs** |  **30.84 μs** |  **1.05** |    **0.33** |  **247.23 KB** |        **1.00** |
| FSharpDataAdaptive | 10000  | 162.75 μs |  5.177 μs |  13.820 μs | 160.23 μs |  5.48 |    1.31 |  200.15 KB |        0.81 |
|                    |        |           |           |            |           |       |         |            |             |
| **AdaptiveSlop**       | **100000** | **268.94 μs** |  **7.547 μs** |  **21.775 μs** | **263.17 μs** |  **1.01** |    **0.11** | **2356.61 KB** |        **1.00** |
| FSharpDataAdaptive | 100000 | 209.87 μs |  5.375 μs |  15.249 μs | 204.13 μs |  0.79 |    0.08 |  219.46 KB |        0.09 |

### Map2Benchmarks

| AdaptiveSlop       | 1000       |  42.43 μs | 0.329 μs | 0.307 μs |  1.00 |    0.01 |       - |         - |          NA |
| FSharpDataAdaptive | 1000       | 373.86 μs | 6.161 μs | 5.763 μs |  8.81 |    0.15 | 62.9883 |  528000 B |          NA |

### MapABenchmarks

|---------------------------- |------------- |----------:|----------:|----------:|----------:|------:|--------:|----------:|------------:|
| **AdaptiveSlopMapA**            | **100**          |  **11.97 μs** |  **0.234 μs** |  **0.207 μs** |  **11.93 μs** |  **1.00** |    **0.02** |         **-** |          **NA** |
| NaiveMapForcesOnFullReplace | 100          |  51.20 μs |  3.516 μs | 10.256 μs |  46.16 μs |  4.28 |    0.86 |   24080 B |          NA |
| FSharpDataAdaptive          | 100          |  16.15 μs |  0.157 μs |  0.174 μs |  16.19 μs |  1.35 |    0.03 |    1624 B |          NA |
|                             |              |           |           |           |           |       |         |           |             |
| **AdaptiveSlopMapA**            | **1000**         |  **95.84 μs** |  **1.866 μs** |  **4.851 μs** |  **94.35 μs** |  **1.00** |    **0.07** |         **-** |          **NA** |
| NaiveMapForcesOnFullReplace | 1000         | 294.81 μs | 22.990 μs | 67.426 μs | 320.17 μs |  3.08 |    0.72 |  234736 B |          NA |
| FSharpDataAdaptive          | 1000         |  17.31 μs |  0.345 μs |  0.576 μs |  17.15 μs |  0.18 |    0.01 |    1864 B |          NA |

### MapBenchmarks

| AdaptiveSlop       | 1000       |  33.57 μs | 0.614 μs | 0.513 μs |  1.00 |    0.02 |        - |         - |          NA |
| FSharpDataAdaptive | 1000       | 300.84 μs | 5.114 μs | 6.467 μs |  8.96 |    0.23 | 100.0977 |  840000 B |          NA |

### MapTransformBenchmarks

| AdaptiveSlop       | 500        |   112.4 μs |  0.47 μs |  0.42 μs |  1.00 |    0.01 |        - |         - |          NA |
| FSharpDataAdaptive | 500        | 1,199.1 μs | 23.88 μs | 42.45 μs | 10.67 |    0.38 | 207.0313 | 1745456 B |          NA |

### OptimizedWideTreeBenchmarks

| **AdaptiveSlop_Map2Chain** | **10**    | **100**        |    **25.253 μs** |  **0.4909 μs** |  **0.4592 μs** |  **1.00** |    **0.02** |      **-** |         **-** |          **NA** |
| AdaptiveSlop_Reduce    | 10    | 100        |     9.724 μs |  0.0998 μs |  0.0779 μs |  0.39 |    0.01 |      - |         - |          NA |
| FSharpDataAdaptive     | 10    | 100        |    81.902 μs |  0.9251 μs |  0.8653 μs |  3.24 |    0.07 | 5.6152 |   47200 B |          NA |
|                        |       |            |              |            |            |       |         |        |           |             |
| **AdaptiveSlop_Map2Chain** | **50**    | **100**        |   **157.814 μs** |  **2.3498 μs** |  **2.1980 μs** |  **1.00** |    **0.02** |      **-** |         **-** |          **NA** |
| AdaptiveSlop_Reduce    | 50    | 100        |    43.657 μs |  0.4242 μs |  0.3968 μs |  0.28 |    0.00 |      - |         - |          NA |
| FSharpDataAdaptive     | 50    | 100        |   323.763 μs |  1.9644 μs |  1.7414 μs |  2.05 |    0.03 | 5.3711 |   47200 B |          NA |
|                        |       |            |              |            |            |       |         |        |           |             |
| **AdaptiveSlop_Map2Chain** | **100**   | **100**        |   **320.516 μs** |  **3.7779 μs** |  **3.1547 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| AdaptiveSlop_Reduce    | 100   | 100        |    84.890 μs |  0.8183 μs |  0.7254 μs |  0.26 |    0.00 |      - |         - |          NA |
| FSharpDataAdaptive     | 100   | 100        |   655.350 μs | 10.1160 μs |  9.4625 μs |  2.04 |    0.03 | 4.8828 |   47200 B |          NA |
|                        |       |            |              |            |            |       |         |        |           |             |
| **AdaptiveSlop_Map2Chain** | **500**   | **100**        | **1,645.984 μs** | **31.6805 μs** | **31.1145 μs** |  **1.00** |    **0.03** |      **-** |         **-** |          **NA** |
| AdaptiveSlop_Reduce    | 500   | 100        |   399.519 μs |  3.5929 μs |  3.1850 μs |  0.24 |    0.00 |      - |         - |          NA |
| FSharpDataAdaptive     | 500   | 100        | 3,030.910 μs | 10.6494 μs |  9.9615 μs |  1.84 |    0.04 | 3.9063 |   47200 B |          NA |

### ReadHeavyBenchmarks

| AdaptiveSlop       | 100        | 50            |  22.22 μs | 0.200 μs | 0.187 μs |  1.00 |    0.01 |      - |         - |          NA |
| FSharpDataAdaptive | 100        | 50            | 111.69 μs | 1.102 μs | 1.031 μs |  5.03 |    0.06 | 5.6152 |   47200 B |          NA |

### ScalarEscapeBenchmarks

| Method                     | Iterations | Mean      | Error    | StdDev   | Ratio | RatioSD | Gen0     | Allocated | Alloc Ratio |
|--------------------------- |----------- |----------:|---------:|---------:|------:|--------:|---------:|----------:|------------:|
| SlopTryFindUnrelatedWrite  | 1000       |  44.94 μs | 0.872 μs | 1.004 μs |  1.00 |    0.03 |        - |         - |          NA |
| SlopTryFindWatchedWrite    | 1000       | 133.16 μs | 0.366 μs | 0.306 μs |  2.96 |    0.06 |        - |         - |          NA |
| FdaTryFindUnrelatedWrite   | 1000       | 787.14 μs | 9.683 μs | 9.058 μs | 17.52 |    0.42 | 130.8594 | 1096000 B |          NA |
| SlopCountUpdateWrite       | 1000       |  43.98 μs | 0.050 μs | 0.044 μs |  0.98 |    0.02 |        - |         - |          NA |
| SlopCountAddWrite          | 1000       | 165.72 μs | 0.228 μs | 0.191 μs |  3.69 |    0.08 |        - |         - |          NA |
| SlopContainsUnrelatedWrite | 1000       |  62.72 μs | 0.166 μs | 0.148 μs |  1.40 |    0.03 |        - |         - |          NA |
| SlopContainsWatchedWrite   | 1000       | 125.43 μs | 2.424 μs | 2.381 μs |  2.79 |    0.08 |        - |         - |          NA |


### SetBenchmarks

| AdaptiveSlop       | 1000       |  31.25 μs | 0.113 μs | 0.106 μs |  1.00 |    0.00 |        - |         - |          NA |
| FSharpDataAdaptive | 1000       | 310.91 μs | 3.413 μs | 3.192 μs |  9.95 |    0.10 | 102.0508 |  856000 B |          NA |

### SetTransformBenchmarks

| AdaptiveSlop       | 500        |   107.2 μs |  0.26 μs |  0.24 μs |  1.00 |    0.00 |        - |         - |          NA |
| FSharpDataAdaptive | 500        | 1,117.0 μs | 14.31 μs | 12.68 μs | 10.42 |    0.12 | 185.5469 | 1561456 B |          NA |

### TransactionBenchmarks

| AdaptiveSlop_Batched       | 10         | 500        |   287.7 μs | 1.13 μs | 1.01 μs |  1.00 |    0.00 |  1.4648 |  15.63 KB |        1.00 |
| FSharpDataAdaptive_Batched | 10         | 500        | 1,257.3 μs | 4.51 μs | 4.21 μs |  4.37 |    0.02 | 87.8906 | 726.56 KB |       46.50 |

### UnbalancedTreeBenchmarks

| **AdaptiveSlop_DeepChange**    | **10**              | **5**                  | **50**         |  **30.27 μs** | **0.111 μs** | **0.104 μs** |  **1.00** |    **0.00** |      **-** |         **-** |          **NA** |
| FDA_DeepChange             | 10              | 5                  | 50         |  95.80 μs | 0.165 μs | 0.154 μs |  3.16 |    0.01 | 2.8076 |   23600 B |          NA |
| AdaptiveSlop_ShallowChange | 10              | 5                  | 50         |  15.99 μs | 0.214 μs | 0.189 μs |  0.53 |    0.01 |      - |         - |          NA |
| FDA_ShallowChange          | 10              | 5                  | 50         |  46.89 μs | 0.761 μs | 0.712 μs |  1.55 |    0.02 | 2.8076 |   23600 B |          NA |
|                            |                 |                    |            |           |          |          |       |         |        |           |             |
| **AdaptiveSlop_DeepChange**    | **10**              | **20**                 | **50**         |  **75.67 μs** | **0.384 μs** | **0.359 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FDA_DeepChange             | 10              | 20                 | 50         | 201.21 μs | 1.736 μs | 1.539 μs |  2.66 |    0.02 | 2.6855 |   23600 B |          NA |
| AdaptiveSlop_ShallowChange | 10              | 20                 | 50         |  57.52 μs | 0.631 μs | 0.527 μs |  0.76 |    0.01 |      - |         - |          NA |
| FDA_ShallowChange          | 10              | 20                 | 50         | 144.90 μs | 0.914 μs | 0.855 μs |  1.91 |    0.01 | 2.6855 |   23600 B |          NA |
|                            |                 |                    |            |           |          |          |       |         |        |           |             |
| **AdaptiveSlop_DeepChange**    | **10**              | **50**                 | **50**         | **165.40 μs** | **0.635 μs** | **0.563 μs** |  **1.00** |    **0.00** |      **-** |         **-** |          **NA** |
| FDA_DeepChange             | 10              | 50                 | 50         | 408.11 μs | 1.212 μs | 1.133 μs |  2.47 |    0.01 | 2.4414 |   23600 B |          NA |
| AdaptiveSlop_ShallowChange | 10              | 50                 | 50         | 136.05 μs | 0.637 μs | 0.596 μs |  0.82 |    0.00 |      - |         - |          NA |
| FDA_ShallowChange          | 10              | 50                 | 50         | 344.45 μs | 1.148 μs | 1.074 μs |  2.08 |    0.01 | 2.4414 |   23600 B |          NA |
|                            |                 |                    |            |           |          |          |       |         |        |           |             |
| **AdaptiveSlop_DeepChange**    | **50**              | **5**                  | **50**         | **117.75 μs** | **0.464 μs** | **0.412 μs** |  **1.00** |    **0.00** |      **-** |         **-** |          **NA** |
| FDA_DeepChange             | 50              | 5                  | 50         | 317.65 μs | 1.396 μs | 1.306 μs |  2.70 |    0.01 | 2.4414 |   23600 B |          NA |
| AdaptiveSlop_ShallowChange | 50              | 5                  | 50         |  26.60 μs | 0.052 μs | 0.046 μs |  0.23 |    0.00 |      - |         - |          NA |
| FDA_ShallowChange          | 50              | 5                  | 50         |  45.00 μs | 0.329 μs | 0.275 μs |  0.38 |    0.00 | 2.8076 |   23600 B |          NA |
|                            |                 |                    |            |           |          |          |       |         |        |           |             |
| **AdaptiveSlop_DeepChange**    | **50**              | **20**                 | **50**         | **162.37 μs** | **0.516 μs** | **0.431 μs** |  **1.00** |    **0.00** |      **-** |         **-** |          **NA** |
| FDA_DeepChange             | 50              | 20                 | 50         | 395.71 μs | 0.933 μs | 0.873 μs |  2.44 |    0.01 | 2.4414 |   23600 B |          NA |
| AdaptiveSlop_ShallowChange | 50              | 20                 | 50         |  62.48 μs | 0.112 μs | 0.105 μs |  0.38 |    0.00 |      - |         - |          NA |
| FDA_ShallowChange          | 50              | 20                 | 50         | 144.97 μs | 0.873 μs | 0.774 μs |  0.89 |    0.01 | 2.6855 |   23600 B |          NA |
|                            |                 |                    |            |           |          |          |       |         |        |           |             |
| **AdaptiveSlop_DeepChange**    | **50**              | **50**                 | **50**         | **255.01 μs** | **1.451 μs** | **1.357 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FDA_DeepChange             | 50              | 50                 | 50         | 662.41 μs | 1.281 μs | 1.136 μs |  2.60 |    0.01 | 1.9531 |   23600 B |          NA |
| AdaptiveSlop_ShallowChange | 50              | 50                 | 50         | 170.45 μs | 2.394 μs | 2.122 μs |  0.67 |    0.01 |      - |         - |          NA |
| FDA_ShallowChange          | 50              | 50                 | 50         | 358.79 μs | 2.666 μs | 2.494 μs |  1.41 |    0.01 | 2.4414 |   23600 B |          NA |
|                            |                 |                    |            |           |          |          |       |         |        |           |             |
| **AdaptiveSlop_DeepChange**    | **100**             | **5**                  | **50**         | **203.78 μs** | **1.576 μs** | **1.474 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FDA_DeepChange             | 100             | 5                  | 50         | 524.17 μs | 2.167 μs | 1.921 μs |  2.57 |    0.02 | 1.9531 |   23600 B |          NA |
| AdaptiveSlop_ShallowChange | 100             | 5                  | 50         |  56.27 μs | 0.652 μs | 0.610 μs |  0.28 |    0.00 |      - |         - |          NA |
| FDA_ShallowChange          | 100             | 5                  | 50         |  45.76 μs | 0.399 μs | 0.373 μs |  0.22 |    0.00 | 2.8076 |   23600 B |          NA |
|                            |                 |                    |            |           |          |          |       |         |        |           |             |
| **AdaptiveSlop_DeepChange**    | **100**             | **20**                 | **50**         | **234.39 μs** | **0.540 μs** | **0.451 μs** |  **1.00** |    **0.00** |      **-** |         **-** |          **NA** |
| FDA_DeepChange             | 100             | 20                 | 50         | 666.03 μs | 4.552 μs | 4.035 μs |  2.84 |    0.02 | 1.9531 |   23600 B |          NA |
| AdaptiveSlop_ShallowChange | 100             | 20                 | 50         |  95.46 μs | 0.269 μs | 0.252 μs |  0.41 |    0.00 |      - |         - |          NA |
| FDA_ShallowChange          | 100             | 20                 | 50         | 152.70 μs | 2.596 μs | 2.302 μs |  0.65 |    0.01 | 2.6855 |   23600 B |          NA |
|                            |                 |                    |            |           |          |          |       |         |        |           |             |
| **AdaptiveSlop_DeepChange**    | **100**             | **50**                 | **50**         | **364.08 μs** | **5.557 μs** | **5.198 μs** |  **1.00** |    **0.02** |      **-** |         **-** |          **NA** |
| FDA_DeepChange             | 100             | 50                 | 50         | 903.42 μs | 7.909 μs | 6.604 μs |  2.48 |    0.04 | 1.9531 |   23600 B |          NA |
| AdaptiveSlop_ShallowChange | 100             | 50                 | 50         | 172.69 μs | 1.080 μs | 1.010 μs |  0.47 |    0.01 |      - |         - |          NA |
| FDA_ShallowChange          | 100             | 50                 | 50         | 369.64 μs | 6.347 μs | 5.627 μs |  1.02 |    0.02 | 2.4414 |   23600 B |          NA |

### ValueBenchmarks

| AdaptiveSlop       | 1000       |  32.84 μs | 0.252 μs | 0.235 μs |  1.00 |    0.01 |       - |         - |          NA |
| FSharpDataAdaptive | 1000       | 288.62 μs | 5.750 μs | 5.905 μs |  8.79 |    0.19 | 56.1523 |  472000 B |          NA |

### WideTreeBenchmarks

| **AdaptiveSlop**       | **10**    | **100**        |    **25.29 μs** |  **0.388 μs** |  **0.363 μs** |  **1.00** |    **0.02** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 10    | 100        |    79.51 μs |  1.060 μs |  0.940 μs |  3.14 |    0.06 | 5.6152 |   47200 B |          NA |
|                    |       |            |             |           |           |       |         |        |           |             |
| **AdaptiveSlop**       | **50**    | **100**        |   **150.37 μs** |  **1.336 μs** |  **1.250 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 50    | 100        |   331.83 μs |  3.929 μs |  5.378 μs |  2.21 |    0.04 | 5.3711 |   47200 B |          NA |
|                    |       |            |             |           |           |       |         |        |           |             |
| **AdaptiveSlop**       | **100**   | **100**        |   **308.01 μs** |  **2.610 μs** |  **2.442 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 100   | 100        |   630.45 μs |  5.270 μs |  4.930 μs |  2.05 |    0.02 | 4.8828 |   47200 B |          NA |
|                    |       |            |             |           |           |       |         |        |           |             |
| **AdaptiveSlop**       | **500**   | **100**        | **1,606.50 μs** | **16.447 μs** | **14.579 μs** |  **1.00** |    **0.01** |      **-** |         **-** |          **NA** |
| FSharpDataAdaptive | 500   | 100        | 3,048.02 μs | 21.423 μs | 18.991 μs |  1.90 |    0.02 | 3.9063 |   47200 B |          NA |


## 2026-08-10 — joinOn vs mapA+tryFind (the per-update join churn shape)

- Branch: feat/joinon-groupby-reductions (AMap.joinOn, per-key swappable inputs)
- Machine: Windows 11, .NET 10.0, x64 RyuJIT
- Job: DefaultJob
- Workload: 200 right entries, 100 left entries. Every iteration every left
  entry updates (left-map churn); the join key is stable per key. This is
  the measured join shape, where the mapA idiom rebuilt every per-key
  subgraph per update (~5% of busy time as AdaptiveNode ZeroCreate).
- The left map is the churn source: the mapA journal re-runs the mapping per
  key per iteration (fresh lookup + wrapper nodes); joinOn swaps a value
  cell in place (no subgraph rebuild).

| Benchmark           | Mean    | Error   | Allocated |
| ------------------- | ------- | ------- | --------- |
| JoinOnUpdateAll     | 752.7 µs| 8.87 µs | 0 B       |
| MapATryFindUpdateAll| 1,185.0 µs | 22.91 µs | 2,120,000 B |

Result: joinOn is ~36% faster and allocates nothing on the update path; the
mapA+tryFind idiom allocates ~2.1 MB per operation on the same workload. The
allocation is exactly the rebuild the swap removes.
