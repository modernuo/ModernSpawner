using System.Collections.Concurrent;
using System.Reflection;

namespace ModernSpawner.Benchmarks.Accessors;

/// <summary>
/// Improvement 1: Cache PropertyInfo lookups but still use reflection for GetValue/SetValue.
/// This eliminates the GetProperty() cost but keeps the reflection invoke overhead.
/// </summary>
public static class CachedReflectionAccessor
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

    private static readonly ConcurrentDictionary<(Type, string), PropertyInfo?> _propertyCache = new();

    public static void ClearCache() => _propertyCache.Clear();

    private static PropertyInfo? GetCachedProperty(Type type, string name)
    {
        return _propertyCache.GetOrAdd((type, name), key => key.Item1.GetProperty(key.Item2, Flags));
    }

    /// <summary>
    /// Gets a property value with cached PropertyInfo lookup.
    /// </summary>
    public static object? GetValue(object target, string propertyPath)
    {
        var current = target;
        var parts = propertyPath.Split('.');

        foreach (var propName in parts)
        {
            if (current == null) return null;

            var prop = GetCachedProperty(current.GetType(), propName);
            if (prop == null) return null;

            current = prop.GetValue(current);
        }

        return current;
    }

    /// <summary>
    /// Sets a property value with cached PropertyInfo lookup.
    /// </summary>
    public static void SetValue(object target, string propertyPath, object? value)
    {
        var parts = propertyPath.Split('.');
        var current = target;

        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (current == null) return;

            var prop = GetCachedProperty(current.GetType(), parts[i]);
            if (prop == null) return;

            current = prop.GetValue(current);
        }

        if (current == null) return;

        var finalProp = GetCachedProperty(current.GetType(), parts[^1]);
        if (finalProp == null) return;

        var convertedValue = Convert.ChangeType(value, finalProp.PropertyType);
        finalProp.SetValue(current, convertedValue);
    }
}
