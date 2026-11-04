```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.5 LTS (Noble Numbat)
Intel Xeon Processor 2.10GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 8.0.133
  [Host]   : .NET 8.0.33 (8.0.3326.52219), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  ShortRun : .NET 8.0.33 (8.0.3326.52219), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                          | Mean       | Error      | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|-------------------------------- |-----------:|-----------:|----------:|------:|--------:|-------:|----------:|------------:|
| &#39;einzeln (linear)&#39;              | 3,873.4 μs | 2,648.0 μs | 145.15 μs |  1.00 |    0.05 |      - | 595.69 KB |        1.00 |
| &#39;SettingsLookup (inkl. Aufbau)&#39; |   543.0 μs |   547.1 μs |  29.99 μs |  0.14 |    0.01 | 1.9531 | 304.99 KB |        0.51 |
