using System.Reflection;

namespace ModernSpawner.Benchmarks.Accessors;

/// <summary>
/// Baseline: Pure reflection on every access (current implementation pattern).
/// No caching of PropertyInfo - mirrors the existing AST behavior.
/// </summary>
public static class ReflectionAccessor
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

    /// <summary>
    /// Gets a property value using pure reflection (no caching).
    /// This is equivalent to the current PropertyAccessExpression.GetPropertyValue().
    /// </summary>
    public static object? GetValue(object target, string propertyPath)
    {
        var current = target;
        var parts = propertyPath.Split('.');

        foreach (var propName in parts)
        {
            if (current == null) return null;

            var prop = current.GetType().GetProperty(propName, Flags);
            if (prop == null) return null;

            current = prop.GetValue(current);
        }

        return current;
    }

    /// <summary>
    /// Sets a property value using pure reflection (no caching).
    /// </summary>
    public static void SetValue(object target, string propertyPath, object? value)
    {
        var parts = propertyPath.Split('.');
        var current = target;

        // Navigate to the parent object
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (current == null) return;

            var prop = current.GetType().GetProperty(parts[i], Flags);
            if (prop == null) return;

            current = prop.GetValue(current);
        }

        if (current == null) return;

        // Set the final property
        var finalProp = current.GetType().GetProperty(parts[^1], Flags);
        if (finalProp == null) return;

        // Convert value if needed
        var convertedValue = Convert.ChangeType(value, finalProp.PropertyType);
        finalProp.SetValue(current, convertedValue);
    }
}
