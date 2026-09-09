using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace ModernSpawner.Benchmarks.Accessors;

/// <summary>
/// Improvement 2: Use Expression Trees to compile property accessors to delegates.
/// This eliminates both GetProperty() and reflection invoke overhead.
/// </summary>
public static class ExpressionAccessor
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

    private static readonly ConcurrentDictionary<(Type, string), CompiledAccessor> _accessorCache = new();

    public static void ClearCache() => _accessorCache.Clear();

    private static CompiledAccessor GetOrCreateAccessor(Type type, string propertyName)
    {
        return _accessorCache.GetOrAdd((type, propertyName), key =>
        {
            var prop = key.Item1.GetProperty(key.Item2, Flags);
            return new CompiledAccessor(key.Item1, prop);
        });
    }

    /// <summary>
    /// Gets a property value using compiled expression delegates.
    /// </summary>
    public static object? GetValue(object target, string propertyPath)
    {
        var current = target;
        var parts = propertyPath.Split('.');

        foreach (var propName in parts)
        {
            if (current == null) return null;

            var accessor = GetOrCreateAccessor(current.GetType(), propName);
            current = accessor.Get(current);
        }

        return current;
    }

    /// <summary>
    /// Sets a property value using compiled expression delegates.
    /// </summary>
    public static void SetValue(object target, string propertyPath, object? value)
    {
        var parts = propertyPath.Split('.');
        var current = target;

        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (current == null) return;

            var accessor = GetOrCreateAccessor(current.GetType(), parts[i]);
            current = accessor.Get(current);
        }

        if (current == null) return;

        var finalAccessor = GetOrCreateAccessor(current.GetType(), parts[^1]);
        finalAccessor.Set(current, value);
    }

    /// <summary>
    /// Holds compiled getter and setter delegates for a property.
    /// </summary>
    public class CompiledAccessor
    {
        private readonly Func<object, object?> _getter;
        private readonly Action<object, object?>? _setter;

        public CompiledAccessor(Type declaringType, PropertyInfo? prop)
        {
            if (prop == null)
            {
                _getter = _ => null;
                _setter = null;
                return;
            }

            // Compile getter: (object target) => (object)((T)target).Property
            var targetParam = Expression.Parameter(typeof(object), "target");
            var castTarget = Expression.Convert(targetParam, declaringType);
            var propertyAccess = Expression.Property(castTarget, prop);
            var boxedResult = Expression.Convert(propertyAccess, typeof(object));
            _getter = Expression.Lambda<Func<object, object?>>(boxedResult, targetParam).Compile();

            // Compile setter if writable: (object target, object value) => ((T)target).Property = (TProp)value
            if (prop.CanWrite)
            {
                var valueParam = Expression.Parameter(typeof(object), "value");
                var castValue = Expression.Convert(valueParam, prop.PropertyType);
                var assignExpr = Expression.Assign(
                    Expression.Property(castTarget, prop),
                    castValue
                );
                _setter = Expression.Lambda<Action<object, object?>>(assignExpr, targetParam, valueParam).Compile();
            }
        }

        public object? Get(object target) => _getter(target);

        public void Set(object target, object? value) => _setter?.Invoke(target, value);
    }
}
