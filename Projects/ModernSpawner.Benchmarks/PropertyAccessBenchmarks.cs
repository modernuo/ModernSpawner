using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using ModernSpawner.Benchmarks.Accessors;

namespace ModernSpawner.Benchmarks;

/// <summary>
/// Benchmarks comparing different property access strategies.
/// Tests both simple property access and chained property access (e.g., Location.X).
/// </summary>
[RankColumn]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class PropertyAccessBenchmarks
{
    private MockMobile _mobile = null!;
    private MockScriptContext _context = null!;

    [GlobalSetup]
    public void Setup()
    {
        _mobile = new MockMobile
        {
            Karma = 1000,
            Fame = 500,
            Hits = 75,
            HitsMax = 100,
            Location = new MockLocation { X = 1234, Y = 5678, Z = 10 }
        };

        _context = new MockScriptContext
        {
            TriggeringMobile = _mobile,
            Spawner = new MockSpawner()
        };

        // Pre-warm all caches
        _ = CachedReflectionAccessor.GetValue(_mobile, "Karma");
        _ = ExpressionAccessor.GetValue(_mobile, "Karma");
        _ = EmitAccessor.GetValue(_mobile, "Karma");
        _ = ExpressionAccessor.GetValue(_mobile, "Location.X");
        _ = EmitAccessor.GetValue(_mobile, "Location.X");
    }

    // ============================================
    // SIMPLE PROPERTY ACCESS (e.g., mobile.Karma)
    // ============================================

    [Benchmark(Description = "Direct C# access", Baseline = true)]
    public int Direct_SimpleProperty()
    {
        return DirectAccessor.GetKarma(_mobile);
    }

    [Benchmark(Description = "Reflection (no cache)")]
    public object? Reflection_SimpleProperty()
    {
        return ReflectionAccessor.GetValue(_mobile, "Karma");
    }

    [Benchmark(Description = "Reflection (cached PropertyInfo)")]
    public object? CachedReflection_SimpleProperty()
    {
        return CachedReflectionAccessor.GetValue(_mobile, "Karma");
    }

    [Benchmark(Description = "Expression<T>.Compile() boxed")]
    public object? Expression_SimpleProperty()
    {
        return ExpressionAccessor.GetValue(_mobile, "Karma");
    }

    [Benchmark(Description = "Expression<T> typed (no boxing)")]
    public int TypedExpression_SimpleProperty()
    {
        return PrecompiledMobileAccessors.GetKarma(_mobile);
    }

    [Benchmark(Description = "IL Emit boxed")]
    public object? Emit_SimpleProperty()
    {
        return EmitAccessor.GetValue(_mobile, "Karma");
    }

    [Benchmark(Description = "IL Emit typed (no boxing)")]
    public int TypedEmit_SimpleProperty()
    {
        return PrecompiledEmitMobileAccessors.GetKarma(_mobile);
    }

    // ============================================
    // CHAINED PROPERTY ACCESS (e.g., mobile.Location.X)
    // ============================================

    [Benchmark(Description = "Direct C# chained")]
    public int Direct_ChainedProperty()
    {
        return DirectAccessor.GetLocationX(_mobile);
    }

    [Benchmark(Description = "Reflection chained (no cache)")]
    public object? Reflection_ChainedProperty()
    {
        return ReflectionAccessor.GetValue(_mobile, "Location.X");
    }

    [Benchmark(Description = "Reflection chained (cached)")]
    public object? CachedReflection_ChainedProperty()
    {
        return CachedReflectionAccessor.GetValue(_mobile, "Location.X");
    }

    [Benchmark(Description = "Expression chained boxed")]
    public object? Expression_ChainedProperty()
    {
        return ExpressionAccessor.GetValue(_mobile, "Location.X");
    }

    [Benchmark(Description = "Expression chained typed")]
    public int TypedExpression_ChainedProperty()
    {
        return PrecompiledMobileAccessors.GetLocationX(_mobile);
    }

    [Benchmark(Description = "IL Emit chained boxed")]
    public object? Emit_ChainedProperty()
    {
        return EmitAccessor.GetValue(_mobile, "Location.X");
    }

    [Benchmark(Description = "IL Emit chained typed")]
    public int TypedEmit_ChainedProperty()
    {
        return PrecompiledEmitMobileAccessors.GetLocationX(_mobile);
    }
}

/// <summary>
/// Benchmarks for property setting operations.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class PropertySetBenchmarks
{
    private MockMobile _mobile = null!;

    [GlobalSetup]
    public void Setup()
    {
        _mobile = new MockMobile();

        // Pre-warm caches
        ExpressionAccessor.SetValue(_mobile, "Karma", 100);
        EmitAccessor.SetValue(_mobile, "Karma", 100);
    }

    [Benchmark(Description = "Direct C# set", Baseline = true)]
    public void Direct_SetProperty()
    {
        DirectAccessor.SetKarma(_mobile, 1000);
    }

    [Benchmark(Description = "Reflection set (no cache)")]
    public void Reflection_SetProperty()
    {
        ReflectionAccessor.SetValue(_mobile, "Karma", 1000);
    }

    [Benchmark(Description = "Reflection set (cached)")]
    public void CachedReflection_SetProperty()
    {
        CachedReflectionAccessor.SetValue(_mobile, "Karma", 1000);
    }

    [Benchmark(Description = "Expression set boxed")]
    public void Expression_SetProperty()
    {
        ExpressionAccessor.SetValue(_mobile, "Karma", 1000);
    }

    [Benchmark(Description = "Expression set typed")]
    public void TypedExpression_SetProperty()
    {
        PrecompiledMobileAccessors.SetKarma(_mobile, 1000);
    }

    [Benchmark(Description = "IL Emit set boxed")]
    public void Emit_SetProperty()
    {
        EmitAccessor.SetValue(_mobile, "Karma", 1000);
    }

    [Benchmark(Description = "IL Emit set typed")]
    public void TypedEmit_SetProperty()
    {
        PrecompiledEmitMobileAccessors.SetKarma(_mobile, 1000);
    }
}

/// <summary>
/// Simulates a realistic spawner condition evaluation scenario.
/// Expression: trigMob.Karma < 0 and trigMob.Fame > 5000
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class ConditionEvaluationBenchmarks
{
    private MockMobile _mobile = null!;
    private MockScriptContext _context = null!;

    // Pre-compiled typed accessors
    private static readonly Func<MockMobile, int> _getKarma = PrecompiledEmitMobileAccessors.GetKarma;
    private static readonly Func<MockMobile, int> _getFame = PrecompiledEmitMobileAccessors.GetFame;

    [GlobalSetup]
    public void Setup()
    {
        _mobile = new MockMobile
        {
            Karma = -500,  // Negative karma
            Fame = 10000   // High fame
        };

        _context = new MockScriptContext
        {
            TriggeringMobile = _mobile
        };

        // Pre-warm caches
        _ = CachedReflectionAccessor.GetValue(_mobile, "Karma");
        _ = CachedReflectionAccessor.GetValue(_mobile, "Fame");
        _ = ExpressionAccessor.GetValue(_mobile, "Karma");
        _ = ExpressionAccessor.GetValue(_mobile, "Fame");
        _ = EmitAccessor.GetValue(_mobile, "Karma");
        _ = EmitAccessor.GetValue(_mobile, "Fame");
    }

    [Benchmark(Description = "Direct C# condition", Baseline = true)]
    public bool Direct_Condition()
    {
        // trigMob.Karma < 0 and trigMob.Fame > 5000
        return _mobile.Karma < 0 && _mobile.Fame > 5000;
    }

    [Benchmark(Description = "Reflection condition (no cache)")]
    public bool Reflection_Condition()
    {
        var karma = (int)ReflectionAccessor.GetValue(_mobile, "Karma")!;
        var fame = (int)ReflectionAccessor.GetValue(_mobile, "Fame")!;
        return karma < 0 && fame > 5000;
    }

    [Benchmark(Description = "Reflection condition (cached)")]
    public bool CachedReflection_Condition()
    {
        var karma = (int)CachedReflectionAccessor.GetValue(_mobile, "Karma")!;
        var fame = (int)CachedReflectionAccessor.GetValue(_mobile, "Fame")!;
        return karma < 0 && fame > 5000;
    }

    [Benchmark(Description = "Expression condition boxed")]
    public bool Expression_Condition()
    {
        var karma = (int)ExpressionAccessor.GetValue(_mobile, "Karma")!;
        var fame = (int)ExpressionAccessor.GetValue(_mobile, "Fame")!;
        return karma < 0 && fame > 5000;
    }

    [Benchmark(Description = "Expression condition typed")]
    public bool TypedExpression_Condition()
    {
        var karma = PrecompiledMobileAccessors.GetKarma(_mobile);
        var fame = PrecompiledMobileAccessors.GetFame(_mobile);
        return karma < 0 && fame > 5000;
    }

    [Benchmark(Description = "IL Emit condition boxed")]
    public bool Emit_Condition()
    {
        var karma = (int)EmitAccessor.GetValue(_mobile, "Karma")!;
        var fame = (int)EmitAccessor.GetValue(_mobile, "Fame")!;
        return karma < 0 && fame > 5000;
    }

    [Benchmark(Description = "IL Emit condition typed")]
    public bool TypedEmit_Condition()
    {
        var karma = _getKarma(_mobile);
        var fame = _getFame(_mobile);
        return karma < 0 && fame > 5000;
    }
}

/// <summary>
/// Benchmarks the compilation cost (one-time overhead).
/// This measures how long it takes to compile an accessor for the first time.
/// </summary>
[MemoryDiagnoser]
public class CompilationCostBenchmarks
{
    private MockMobile _mobile = null!;
    private int _counter;

    [GlobalSetup]
    public void Setup()
    {
        _mobile = new MockMobile();
        _counter = 0;
    }

    [IterationSetup]
    public void IterationSetup()
    {
        // Clear caches before each iteration to measure cold compilation
        CachedReflectionAccessor.ClearCache();
        ExpressionAccessor.ClearCache();
        EmitAccessor.ClearCache();
        TypedExpressionAccessor.ClearCache();
        TypedEmitAccessor.ClearCache();
        _counter++;
    }

    [Benchmark(Description = "Reflection PropertyInfo lookup")]
    public object? Reflection_Compile()
    {
        return ReflectionAccessor.GetValue(_mobile, $"Karma");
    }

    [Benchmark(Description = "Expression<T>.Compile()")]
    public object? Expression_Compile()
    {
        return ExpressionAccessor.GetValue(_mobile, $"Karma");
    }

    [Benchmark(Description = "IL Emit DynamicMethod")]
    public object? Emit_Compile()
    {
        return EmitAccessor.GetValue(_mobile, $"Karma");
    }
}

/// <summary>
/// Simulates 12k spawners each evaluating a condition.
/// This provides realistic aggregate timing for the use case.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class SpawnerSimulationBenchmarks
{
    private MockMobile[] _mobiles = null!;
    private const int SpawnerCount = 12000;

    // Pre-compiled typed accessors
    private static readonly Func<MockMobile, int> _getKarma = PrecompiledEmitMobileAccessors.GetKarma;
    private static readonly Func<MockMobile, int> _getFame = PrecompiledEmitMobileAccessors.GetFame;

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(42);
        _mobiles = new MockMobile[SpawnerCount];

        for (var i = 0; i < SpawnerCount; i++)
        {
            _mobiles[i] = new MockMobile
            {
                Karma = random.Next(-10000, 10000),
                Fame = random.Next(0, 20000)
            };
        }

        // Pre-warm all caches
        var warmupMobile = _mobiles[0];
        _ = CachedReflectionAccessor.GetValue(warmupMobile, "Karma");
        _ = ExpressionAccessor.GetValue(warmupMobile, "Karma");
        _ = EmitAccessor.GetValue(warmupMobile, "Karma");
    }

    [Benchmark(Description = "Direct C# (12k spawners)", Baseline = true)]
    public int Direct_12kSpawners()
    {
        var count = 0;
        foreach (var mobile in _mobiles)
        {
            if (mobile.Karma < 0 && mobile.Fame > 5000)
            {
                count++;
            }
        }
        return count;
    }

    [Benchmark(Description = "Reflection no cache (12k spawners)")]
    public int Reflection_12kSpawners()
    {
        var count = 0;
        foreach (var mobile in _mobiles)
        {
            var karma = (int)ReflectionAccessor.GetValue(mobile, "Karma")!;
            var fame = (int)ReflectionAccessor.GetValue(mobile, "Fame")!;
            if (karma < 0 && fame > 5000)
            {
                count++;
            }
        }
        return count;
    }

    [Benchmark(Description = "Reflection cached (12k spawners)")]
    public int CachedReflection_12kSpawners()
    {
        var count = 0;
        foreach (var mobile in _mobiles)
        {
            var karma = (int)CachedReflectionAccessor.GetValue(mobile, "Karma")!;
            var fame = (int)CachedReflectionAccessor.GetValue(mobile, "Fame")!;
            if (karma < 0 && fame > 5000)
            {
                count++;
            }
        }
        return count;
    }

    [Benchmark(Description = "Expression boxed (12k spawners)")]
    public int Expression_12kSpawners()
    {
        var count = 0;
        foreach (var mobile in _mobiles)
        {
            var karma = (int)ExpressionAccessor.GetValue(mobile, "Karma")!;
            var fame = (int)ExpressionAccessor.GetValue(mobile, "Fame")!;
            if (karma < 0 && fame > 5000)
            {
                count++;
            }
        }
        return count;
    }

    [Benchmark(Description = "Expression typed (12k spawners)")]
    public int TypedExpression_12kSpawners()
    {
        var count = 0;
        foreach (var mobile in _mobiles)
        {
            var karma = PrecompiledMobileAccessors.GetKarma(mobile);
            var fame = PrecompiledMobileAccessors.GetFame(mobile);
            if (karma < 0 && fame > 5000)
            {
                count++;
            }
        }
        return count;
    }

    [Benchmark(Description = "IL Emit boxed (12k spawners)")]
    public int Emit_12kSpawners()
    {
        var count = 0;
        foreach (var mobile in _mobiles)
        {
            var karma = (int)EmitAccessor.GetValue(mobile, "Karma")!;
            var fame = (int)EmitAccessor.GetValue(mobile, "Fame")!;
            if (karma < 0 && fame > 5000)
            {
                count++;
            }
        }
        return count;
    }

    [Benchmark(Description = "IL Emit typed (12k spawners)")]
    public int TypedEmit_12kSpawners()
    {
        var count = 0;
        foreach (var mobile in _mobiles)
        {
            var karma = _getKarma(mobile);
            var fame = _getFame(mobile);
            if (karma < 0 && fame > 5000)
            {
                count++;
            }
        }
        return count;
    }
}
