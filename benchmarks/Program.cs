using System.Text.Json;
using RtsDemo.Tests;

var benchmark = SimulationBenchmark.Run();
foreach (var result in benchmark.Cases)
{
    Console.WriteLine(
        $"RTS_BENCHMARK units={result.Units} " +
        $"avg={result.AverageTickMilliseconds:0.000}ms " +
        $"p95={result.P95TickMilliseconds:0.000}ms " +
        $"hash={result.AverageStateHashMilliseconds:0.000}ms " +
        $"max={result.MaximumTickMilliseconds:0.000}ms " +
        $"alloc={result.AverageAllocatedBytes / 1024.0:0.0}KB/tick");
}
foreach (var result in benchmark.CombatCases)
{
    Console.WriteLine(
        $"RTS_COMBAT_BENCHMARK units={result.Units} " +
        $"avg={result.AverageTickMilliseconds:0.000}ms " +
        $"p95={result.P95TickMilliseconds:0.000}ms " +
        $"hash={result.AverageStateHashMilliseconds:0.000}ms " +
        $"max={result.MaximumTickMilliseconds:0.000}ms " +
        $"alloc={result.AverageAllocatedBytes / 1024.0:0.0}KB/tick");
}

Console.WriteLine($"RTS_BENCHMARK_JSON {JsonSerializer.Serialize(benchmark)}");
return benchmark.Passed ? 0 : 1;
