using System.Linq.Expressions;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;

namespace ModernSpawner.Benchmarks;

/// <summary>
/// Benchmarks comparing XmlSpawner-style reflection-based condition evaluation
/// vs ModernSpawner's compiled expression evaluation.
///
/// Tests the condition: trigMob.Karma less than 0 AND trigMob.Fame greater than 5000
///
/// This specifically tests the RUNTIME evaluation cost, not compilation overhead.
/// All caches/compiled expressions are pre-warmed.
/// </summary>
[RankColumn]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class ExpressionEvaluationBenchmarks
{
    private MockMobile _mobile = null!;

    // XmlSpawner-style: cached PropertyInfo (reflection)
    private static readonly PropertyInfo _karmaProperty = typeof(MockMobile).GetProperty("Karma")!;
    private static readonly PropertyInfo _fameProperty = typeof(MockMobile).GetProperty("Fame")!;

    // ModernSpawner-style: compiled typed delegates
    private static readonly Func<MockMobile, double> _getKarmaTyped;
    private static readonly Func<MockMobile, double> _getFameTyped;

    // ModernSpawner-style: compiled boxed delegates (generic cache path)
    private static readonly Func<object, object?> _getKarmaBoxed;
    private static readonly Func<object, object?> _getFameBoxed;

    static ExpressionEvaluationBenchmarks()
    {
        // Pre-compile typed getters (like PropertyAccessorCache.BuildNumericGetter)
        _getKarmaTyped = CompileTypedGetter<MockMobile, double>("Karma");
        _getFameTyped = CompileTypedGetter<MockMobile, double>("Fame");

        // Pre-compile boxed getters (like PropertyAccessorCache.BuildChainedGetter)
        _getKarmaBoxed = CompileBoxedGetter<MockMobile>("Karma");
        _getFameBoxed = CompileBoxedGetter<MockMobile>("Fame");
    }

    [GlobalSetup]
    public void Setup()
    {
        _mobile = new MockMobile
        {
            Karma = -500,  // Negative karma (condition true)
            Fame = 10000   // High fame (condition true)
        };
    }

    // ============================================
    // BASELINE: Direct C# code
    // ============================================

    [Benchmark(Description = "Direct C# (baseline)", Baseline = true)]
    public bool Direct_Condition()
    {
        return _mobile.Karma < 0 && _mobile.Fame > 5000;
    }

    // ============================================
    // XMLSPAWNER-STYLE: Reflection with string comparison
    // ============================================

    [Benchmark(Description = "XmlSpawner: Reflection + GetProperties()")]
    public bool XmlSpawner_ReflectionFull()
    {
        // This simulates the XmlSpawner path that calls GetProperties() each time
        var type = _mobile.GetType();
        var props = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);

        int karma = 0;
        int fame = 0;

        foreach (var p in props)
        {
            if (p.Name == "Karma")
            {
                karma = (int)p.GetValue(_mobile)!;
            }
            else if (p.Name == "Fame")
            {
                fame = (int)p.GetValue(_mobile)!;
            }
        }

        return karma < 0 && fame > 5000;
    }

    [Benchmark(Description = "XmlSpawner: Cached PropertyInfo.GetValue")]
    public bool XmlSpawner_CachedPropertyInfo()
    {
        // This simulates XmlSpawner with LookupPropertyInfo cache hit
        var karma = (int)_karmaProperty.GetValue(_mobile)!;
        var fame = (int)_fameProperty.GetValue(_mobile)!;

        return karma < 0 && fame > 5000;
    }

    [Benchmark(Description = "XmlSpawner: String-based comparison")]
    public bool XmlSpawner_StringComparison()
    {
        // This simulates XmlSpawner's CheckPropertyString which converts to strings
        var karmaStr = _karmaProperty.GetValue(_mobile)?.ToString();
        var fameStr = _fameProperty.GetValue(_mobile)?.ToString();

        // XmlSpawner parses strings back to numbers for comparison
        if (!int.TryParse(karmaStr, out var karma) || !int.TryParse(fameStr, out var fame))
        {
            return false;
        }

        return karma < 0 && fame > 5000;
    }

    // ============================================
    // MODERNSPAWNER-STYLE: Compiled expressions
    // ============================================

    [Benchmark(Description = "ModernSpawner: Compiled boxed getter")]
    public bool ModernSpawner_CompiledBoxed()
    {
        // Uses Func<object, object?> - requires boxing/unboxing
        var karma = (int)_getKarmaBoxed(_mobile)!;
        var fame = (int)_getFameBoxed(_mobile)!;

        return karma < 0 && fame > 5000;
    }

    [Benchmark(Description = "ModernSpawner: Compiled typed getter")]
    public bool ModernSpawner_CompiledTyped()
    {
        // Uses Func<T, double> - no boxing
        var karma = _getKarmaTyped(_mobile);
        var fame = _getFameTyped(_mobile);

        return karma < 0 && fame > 5000;
    }

    // ============================================
    // HELPER: Compile expression tree getters
    // ============================================

    private static Func<TTarget, TResult> CompileTypedGetter<TTarget, TResult>(string propertyName)
    {
        var param = Expression.Parameter(typeof(TTarget), "target");
        var prop = Expression.Property(param, propertyName);
        var converted = Expression.Convert(prop, typeof(TResult));
        return Expression.Lambda<Func<TTarget, TResult>>(converted, param).Compile();
    }

    private static Func<object, object?> CompileBoxedGetter<TTarget>(string propertyName)
    {
        var param = Expression.Parameter(typeof(object), "target");
        var castTarget = Expression.Convert(param, typeof(TTarget));
        var prop = Expression.Property(castTarget, propertyName);
        var boxed = Expression.Convert(prop, typeof(object));
        return Expression.Lambda<Func<object, object?>>(boxed, param).Compile();
    }
}

/// <summary>
/// Simulates evaluating conditions on 12,000 spawners/mobiles.
/// This represents the real-world scenario of many spawners checking triggers.
/// </summary>
[RankColumn]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class MassConditionEvaluationBenchmarks
{
    private MockMobile[] _mobiles = null!;
    private const int Count = 12000;

    // XmlSpawner-style: cached PropertyInfo
    private static readonly PropertyInfo _karmaProperty = typeof(MockMobile).GetProperty("Karma")!;
    private static readonly PropertyInfo _fameProperty = typeof(MockMobile).GetProperty("Fame")!;

    // ModernSpawner-style: compiled typed delegates
    private static readonly Func<MockMobile, double> _getKarmaTyped;
    private static readonly Func<MockMobile, double> _getFameTyped;

    static MassConditionEvaluationBenchmarks()
    {
        _getKarmaTyped = CompileTypedGetter<MockMobile, double>("Karma");
        _getFameTyped = CompileTypedGetter<MockMobile, double>("Fame");
    }

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(42);
        _mobiles = new MockMobile[Count];

        for (var i = 0; i < Count; i++)
        {
            _mobiles[i] = new MockMobile
            {
                Karma = random.Next(-10000, 10000),
                Fame = random.Next(0, 20000)
            };
        }
    }

    [Benchmark(Description = "Direct C# (12k)", Baseline = true)]
    public int Direct_12k()
    {
        var count = 0;
        for (var i = 0; i < _mobiles.Length; i++)
        {
            var m = _mobiles[i];
            if (m.Karma < 0 && m.Fame > 5000)
            {
                count++;
            }
        }

        return count;
    }

    [Benchmark(Description = "XmlSpawner: PropertyInfo.GetValue (12k)")]
    public int XmlSpawner_CachedPropertyInfo_12k()
    {
        var count = 0;
        for (var i = 0; i < _mobiles.Length; i++)
        {
            var m = _mobiles[i];
            var karma = (int)_karmaProperty.GetValue(m)!;
            var fame = (int)_fameProperty.GetValue(m)!;
            if (karma < 0 && fame > 5000)
            {
                count++;
            }
        }

        return count;
    }

    [Benchmark(Description = "XmlSpawner: String comparison (12k)")]
    public int XmlSpawner_StringComparison_12k()
    {
        var count = 0;
        for (var i = 0; i < _mobiles.Length; i++)
        {
            var m = _mobiles[i];
            var karmaStr = _karmaProperty.GetValue(m)?.ToString();
            var fameStr = _fameProperty.GetValue(m)?.ToString();

            if (int.TryParse(karmaStr, out var karma) &&
                int.TryParse(fameStr, out var fame) &&
                karma < 0 && fame > 5000)
            {
                count++;
            }
        }

        return count;
    }

    [Benchmark(Description = "ModernSpawner: Compiled typed (12k)")]
    public int ModernSpawner_CompiledTyped_12k()
    {
        var count = 0;
        for (var i = 0; i < _mobiles.Length; i++)
        {
            var m = _mobiles[i];
            if (_getKarmaTyped(m) < 0 && _getFameTyped(m) > 5000)
            {
                count++;
            }
        }

        return count;
    }

    private static Func<TTarget, TResult> CompileTypedGetter<TTarget, TResult>(string propertyName)
    {
        var param = Expression.Parameter(typeof(TTarget), "target");
        var prop = Expression.Property(param, propertyName);
        var converted = Expression.Convert(prop, typeof(TResult));
        return Expression.Lambda<Func<TTarget, TResult>>(converted, param).Compile();
    }
}

/// <summary>
/// Complex expression benchmark with chained property access and multiple conditions.
/// Expression: trigMob.Hits less than trigMob.HitsMax * 0.5 AND trigMob.Location.X greater than 1000
/// </summary>
[RankColumn]
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
public class ComplexExpressionBenchmarks
{
    private MockMobile _mobile = null!;

    // XmlSpawner-style: cached PropertyInfo (including chained)
    private static readonly PropertyInfo _hitsProperty = typeof(MockMobile).GetProperty("Hits")!;
    private static readonly PropertyInfo _hitsMaxProperty = typeof(MockMobile).GetProperty("HitsMax")!;
    private static readonly PropertyInfo _locationProperty = typeof(MockMobile).GetProperty("Location")!;
    private static readonly PropertyInfo _locationXProperty = typeof(MockLocation).GetProperty("X")!;

    // ModernSpawner-style: compiled typed delegates
    private static readonly Func<MockMobile, double> _getHits;
    private static readonly Func<MockMobile, double> _getHitsMax;
    private static readonly Func<MockMobile, double> _getLocationX;

    static ComplexExpressionBenchmarks()
    {
        _getHits = CompileTypedGetter<MockMobile, double>("Hits");
        _getHitsMax = CompileTypedGetter<MockMobile, double>("HitsMax");
        _getLocationX = CompileChainedTypedGetter();
    }

    [GlobalSetup]
    public void Setup()
    {
        _mobile = new MockMobile
        {
            Hits = 30,
            HitsMax = 100,
            Location = new MockLocation { X = 1500, Y = 2000, Z = 0 }
        };
    }

    [Benchmark(Description = "Direct C# (complex)", Baseline = true)]
    public bool Direct_Complex()
    {
        return _mobile.Hits < _mobile.HitsMax * 0.5 && _mobile.Location.X > 1000;
    }

    [Benchmark(Description = "XmlSpawner: PropertyInfo (complex)")]
    public bool XmlSpawner_Complex()
    {
        var hits = (int)_hitsProperty.GetValue(_mobile)!;
        var hitsMax = (int)_hitsMaxProperty.GetValue(_mobile)!;
        var location = _locationProperty.GetValue(_mobile)!;
        var x = (int)_locationXProperty.GetValue(location)!;

        return hits < hitsMax * 0.5 && x > 1000;
    }

    [Benchmark(Description = "ModernSpawner: Compiled typed (complex)")]
    public bool ModernSpawner_Complex()
    {
        return _getHits(_mobile) < _getHitsMax(_mobile) * 0.5 && _getLocationX(_mobile) > 1000;
    }

    private static Func<TTarget, TResult> CompileTypedGetter<TTarget, TResult>(string propertyName)
    {
        var param = Expression.Parameter(typeof(TTarget), "target");
        var prop = Expression.Property(param, propertyName);
        var converted = Expression.Convert(prop, typeof(TResult));
        return Expression.Lambda<Func<TTarget, TResult>>(converted, param).Compile();
    }

    private static Func<MockMobile, double> CompileChainedTypedGetter()
    {
        // Compiles: (mobile) => (double)mobile.Location.X
        var param = Expression.Parameter(typeof(MockMobile), "mobile");
        var location = Expression.Property(param, "Location");
        var x = Expression.Property(location, "X");
        var converted = Expression.Convert(x, typeof(double));
        return Expression.Lambda<Func<MockMobile, double>>(converted, param).Compile();
    }
}
