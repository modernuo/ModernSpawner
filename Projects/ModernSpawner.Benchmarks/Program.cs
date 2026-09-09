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
          --quick       Run with fewer iterations (for quick testing)
          --help, -h    Show this help

        Examples:
          dotnet run -c Release --all
          dotnet run -c Release --expr
          dotnet run -c Release --spawner --condition
          dotnet run -c Release --quick --simple
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
