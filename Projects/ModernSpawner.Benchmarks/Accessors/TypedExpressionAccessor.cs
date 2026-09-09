using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace ModernSpawner.Benchmarks.Accessors;

/// <summary>
/// Improvement 3: Strongly-typed Expression accessors that avoid boxing for value types.
/// This is the optimal pattern when you know the types at compile time.
/// </summary>
public static class TypedExpressionAccessor
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

    /// <summary>
    /// Cache for pre-compiled typed accessors.
    /// Key is (SourceType, PropertyPath) to handle chained properties.
    /// </summary>
    private static readonly ConcurrentDictionary<(Type, string), Delegate> _getterCache = new();
    private static readonly ConcurrentDictionary<(Type, string), Delegate> _setterCache = new();

    public static void ClearCache()
    {
        _getterCache.Clear();
        _setterCache.Clear();
    }

    /// <summary>
    /// Gets a strongly-typed getter for a property path.
    /// Example: GetGetter&lt;MockMobile, int&gt;("Karma") or GetGetter&lt;MockMobile, int&gt;("Location.X")
    /// </summary>
    public static Func<TSource, TResult> GetGetter<TSource, TResult>(string propertyPath)
    {
        var key = (typeof(TSource), propertyPath);
        return (Func<TSource, TResult>)_getterCache.GetOrAdd(key, _ => CompileGetter<TSource, TResult>(propertyPath));
    }

    /// <summary>
    /// Gets a strongly-typed setter for a simple property (not chained).
    /// </summary>
    public static Action<TSource, TValue> GetSetter<TSource, TValue>(string propertyName)
    {
        var key = (typeof(TSource), propertyName);
        return (Action<TSource, TValue>)_setterCache.GetOrAdd(key, _ => CompileSetter<TSource, TValue>(propertyName));
    }

    private static Func<TSource, TResult> CompileGetter<TSource, TResult>(string propertyPath)
    {
        var param = Expression.Parameter(typeof(TSource), "source");
        Expression current = param;

        var parts = propertyPath.Split('.');
        foreach (var propName in parts)
        {
            var prop = current.Type.GetProperty(propName, Flags)
                ?? throw new InvalidOperationException($"Property '{propName}' not found on type '{current.Type.Name}'");
            current = Expression.Property(current, prop);
        }

        // Convert to result type if necessary
        if (current.Type != typeof(TResult))
        {
            current = Expression.Convert(current, typeof(TResult));
        }

        return Expression.Lambda<Func<TSource, TResult>>(current, param).Compile();
    }

    private static Action<TSource, TValue> CompileSetter<TSource, TValue>(string propertyName)
    {
        var sourceParam = Expression.Parameter(typeof(TSource), "source");
        var valueParam = Expression.Parameter(typeof(TValue), "value");

        var prop = typeof(TSource).GetProperty(propertyName, Flags)
            ?? throw new InvalidOperationException($"Property '{propertyName}' not found on type '{typeof(TSource).Name}'");

        Expression valueExpr = valueParam;
        if (prop.PropertyType != typeof(TValue))
        {
            valueExpr = Expression.Convert(valueParam, prop.PropertyType);
        }

        var assignment = Expression.Assign(
            Expression.Property(sourceParam, prop),
            valueExpr
        );

        return Expression.Lambda<Action<TSource, TValue>>(assignment, sourceParam, valueParam).Compile();
    }
}

/// <summary>
/// Pre-compiled accessors for common MockMobile properties.
/// This simulates what we'd generate for known types.
/// </summary>
public static class PrecompiledMobileAccessors
{
    public static readonly Func<MockMobile, int> GetKarma = TypedExpressionAccessor.GetGetter<MockMobile, int>("Karma");
    public static readonly Func<MockMobile, int> GetFame = TypedExpressionAccessor.GetGetter<MockMobile, int>("Fame");
    public static readonly Func<MockMobile, int> GetHits = TypedExpressionAccessor.GetGetter<MockMobile, int>("Hits");
    public static readonly Func<MockMobile, int> GetLocationX = TypedExpressionAccessor.GetGetter<MockMobile, int>("Location.X");
    public static readonly Func<MockMobile, int> GetLocationY = TypedExpressionAccessor.GetGetter<MockMobile, int>("Location.Y");

    public static readonly Action<MockMobile, int> SetKarma = TypedExpressionAccessor.GetSetter<MockMobile, int>("Karma");
    public static readonly Action<MockMobile, int> SetHits = TypedExpressionAccessor.GetSetter<MockMobile, int>("Hits");
}
