using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Csv;
using BenchmarkDotNet.Running;
using ModernSpawner.Benchmarks;

// Parse command line args for specific benchmark selection
if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine("""
        ModernSpawner Property Access Benchmarks
        ========================================

        Usage:
          dotnet run -c Release [options]

        Options:
          --all         Run all benchmarks
          --simple      Run simple property access benchmarks
          --condition   Run condition evaluation benchmarks
          --spawner     Run 12k spawner simulation
          --compile     Run compilation cost benchmarks
          --set         Run property set benchmarks
          --expr        Run XmlSpawner vs ModernSpawner expression comparison
          --triggers    Run the D2 trigger dispatch benchmarks (set lookup, request/drain)
          --quick       Run with fewer iterations (for quick testing)
          --filter <p>  Hand the arguments to BenchmarkDotNet's own switcher, e.g. --filter *TriggerDispatch*
          --help, -h    Show this help

        Examples:
          dotnet run -c Release --all
          dotnet run -c Release --expr
          dotnet run -c Release --spawner --condition
          dotnet run -c Release --quick --simple
          dotnet run -c Release -- --filter *TriggerDispatch*
        """);
    return;
}

var config = DefaultConfig.Instance
    .AddExporter(MarkdownExporter.GitHub)
    .AddExporter(CsvExporter.Default);

// Quick mode for faster testing
if (args.Contains("--quick"))
{
    config = config.WithOptions(ConfigOptions.DisableOptimizationsValidator);
}

// A --filter run is handed straight to BenchmarkDotNet's switcher, which understands globs over the
// whole assembly. The curated flags below stay for the suites that predate it.
if (args.Contains("--filter"))
{
    BenchmarkSwitcher.FromAssembly(typeof(TriggerDispatchLookupBenchmarks).Assembly).Run(args, config);
    return;
}

// Determine which benchmarks to run
var runAll = args.Contains("--all") || args.Length == 0;
var benchmarkTypes = new List<Type>();

if (runAll || args.Contains("--simple"))
{
    benchmarkTypes.Add(typeof(PropertyAccessBenchmarks));
}

if (runAll || args.Contains("--set"))
{
    benchmarkTypes.Add(typeof(PropertySetBenchmarks));
}

if (runAll || args.Contains("--condition"))
{
    benchmarkTypes.Add(typeof(ConditionEvaluationBenchmarks));
}

if (runAll || args.Contains("--spawner"))
{
    benchmarkTypes.Add(typeof(SpawnerSimulationBenchmarks));
}

if (runAll || args.Contains("--compile"))
{
    benchmarkTypes.Add(typeof(CompilationCostBenchmarks));
}

if (runAll || args.Contains("--expr"))
{
    benchmarkTypes.Add(typeof(ExpressionEvaluationBenchmarks));
    benchmarkTypes.Add(typeof(MassConditionEvaluationBenchmarks));
    benchmarkTypes.Add(typeof(ComplexExpressionBenchmarks));
}

if (runAll || args.Contains("--triggers"))
{
    benchmarkTypes.Add(typeof(TriggerDispatchLookupBenchmarks));
    benchmarkTypes.Add(typeof(TriggerDispatchRequestDrainBenchmarks));
}

if (benchmarkTypes.Count == 0)
{
    Console.WriteLine("No benchmarks selected. Use --help for options.");
    return;
}

Console.WriteLine($"Running {benchmarkTypes.Count} benchmark suite(s)...\n");

foreach (var benchmarkType in benchmarkTypes)
{
    Console.WriteLine($"=== {benchmarkType.Name} ===\n");
    BenchmarkRunner.Run(benchmarkType, config);
    Console.WriteLine();
}

Console.WriteLine("\nBenchmark complete. Results saved to BenchmarkDotNet.Artifacts/");
