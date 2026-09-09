using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;

namespace ModernSpawner.Benchmarks.Accessors;

/// <summary>
/// Improvement 4: Direct MSIL emit for maximum performance.
/// This follows ModernUO's pattern from Emitter.cs and the command compilers.
/// </summary>
public static class EmitAccessor
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

    private static readonly ConcurrentDictionary<(Type, string), Func<object, object?>> _getterCache = new();
    private static readonly ConcurrentDictionary<(Type, string), Action<object, object?>> _setterCache = new();

    public static void ClearCache()
    {
        _getterCache.Clear();
        _setterCache.Clear();
    }

    /// <summary>
    /// Gets a property value using IL-emitted delegates.
    /// </summary>
    public static object? GetValue(object target, string propertyPath)
    {
        var current = target;
        var parts = propertyPath.Split('.');

        foreach (var propName in parts)
        {
            if (current == null) return null;

            var getter = GetOrCreateGetter(current.GetType(), propName);
            current = getter(current);
        }

        return current;
    }

    /// <summary>
    /// Sets a property value using IL-emitted delegates.
    /// </summary>
    public static void SetValue(object target, string propertyPath, object? value)
    {
        var parts = propertyPath.Split('.');
        var current = target;

        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (current == null) return;

            var getter = GetOrCreateGetter(current.GetType(), parts[i]);
            current = getter(current);
        }

        if (current == null) return;

        var setter = GetOrCreateSetter(current.GetType(), parts[^1]);
        setter(current, value);
    }

    private static Func<object, object?> GetOrCreateGetter(Type type, string propertyName)
    {
        return _getterCache.GetOrAdd((type, propertyName), key => EmitGetter(key.Item1, key.Item2));
    }

    private static Action<object, object?> GetOrCreateSetter(Type type, string propertyName)
    {
        return _setterCache.GetOrAdd((type, propertyName), key => EmitSetter(key.Item1, key.Item2));
    }

    private static Func<object, object?> EmitGetter(Type declaringType, string propertyName)
    {
        var prop = declaringType.GetProperty(propertyName, Flags);
        if (prop == null)
        {
            return _ => null;
        }

        var getMethod = prop.GetGetMethod();
        if (getMethod == null)
        {
            return _ => null;
        }

        // Create a DynamicMethod that takes object and returns object
        var dm = new DynamicMethod(
            $"Get_{declaringType.Name}_{propertyName}",
            typeof(object),
            [typeof(object)],
            typeof(EmitAccessor).Module,
            skipVisibility: true
        );

        var il = dm.GetILGenerator();

        // Load argument and cast to declaring type
        il.Emit(OpCodes.Ldarg_0);

        if (declaringType.IsValueType)
        {
            il.Emit(OpCodes.Unbox_Any, declaringType);
        }
        else
        {
            il.Emit(OpCodes.Castclass, declaringType);
        }

        // Call the getter
        if (getMethod.IsVirtual)
        {
            il.Emit(OpCodes.Callvirt, getMethod);
        }
        else
        {
            il.Emit(OpCodes.Call, getMethod);
        }

        // Box if value type
        if (prop.PropertyType.IsValueType)
        {
            il.Emit(OpCodes.Box, prop.PropertyType);
        }

        il.Emit(OpCodes.Ret);

        return dm.CreateDelegate<Func<object, object?>>();
    }

    private static Action<object, object?> EmitSetter(Type declaringType, string propertyName)
    {
        var prop = declaringType.GetProperty(propertyName, Flags);
        if (prop == null || !prop.CanWrite)
        {
            return (_, _) => { };
        }

        var setMethod = prop.GetSetMethod();
        if (setMethod == null)
        {
            return (_, _) => { };
        }

        // Create a DynamicMethod
        var dm = new DynamicMethod(
            $"Set_{declaringType.Name}_{propertyName}",
            typeof(void),
            [typeof(object), typeof(object)],
            typeof(EmitAccessor).Module,
            skipVisibility: true
        );

        var il = dm.GetILGenerator();

        // Load target and cast
        il.Emit(OpCodes.Ldarg_0);
        if (declaringType.IsValueType)
        {
            il.Emit(OpCodes.Unbox_Any, declaringType);
        }
        else
        {
            il.Emit(OpCodes.Castclass, declaringType);
        }

        // Load value and convert
        il.Emit(OpCodes.Ldarg_1);
        if (prop.PropertyType.IsValueType)
        {
            il.Emit(OpCodes.Unbox_Any, prop.PropertyType);
        }
        else
        {
            il.Emit(OpCodes.Castclass, prop.PropertyType);
        }

        // Call setter
        if (setMethod.IsVirtual)
        {
            il.Emit(OpCodes.Callvirt, setMethod);
        }
        else
        {
            il.Emit(OpCodes.Call, setMethod);
        }

        il.Emit(OpCodes.Ret);

        return dm.CreateDelegate<Action<object, object?>>();
    }
}

/// <summary>
/// Strongly-typed IL emit accessors that avoid boxing entirely.
/// This is the maximum performance pattern.
/// </summary>
public static class TypedEmitAccessor
{
    private static readonly ConcurrentDictionary<(Type, string), Delegate> _getterCache = new();
    private static readonly ConcurrentDictionary<(Type, string), Delegate> _setterCache = new();

    public static void ClearCache()
    {
        _getterCache.Clear();
        _setterCache.Clear();
    }

    /// <summary>
    /// Get a strongly-typed getter with no boxing.
    /// </summary>
    public static Func<TSource, TResult> GetGetter<TSource, TResult>(string propertyPath)
    {
        var key = (typeof(TSource), propertyPath);
        return (Func<TSource, TResult>)_getterCache.GetOrAdd(key, _ => EmitChainedGetter<TSource, TResult>(propertyPath));
    }

    /// <summary>
    /// Get a strongly-typed setter with no boxing.
    /// </summary>
    public static Action<TSource, TValue> GetSetter<TSource, TValue>(string propertyName)
    {
        var key = (typeof(TSource), propertyName);
        return (Action<TSource, TValue>)_setterCache.GetOrAdd(key, _ => EmitSetter<TSource, TValue>(propertyName));
    }

    private static Func<TSource, TResult> EmitChainedGetter<TSource, TResult>(string propertyPath)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

        var dm = new DynamicMethod(
            $"Get_{typeof(TSource).Name}_{propertyPath.Replace('.', '_')}",
            typeof(TResult),
            [typeof(TSource)],
            typeof(TypedEmitAccessor).Module,
            skipVisibility: true
        );

        var il = dm.GetILGenerator();
        var parts = propertyPath.Split('.');
        var currentType = typeof(TSource);

        // Load the source object
        il.Emit(OpCodes.Ldarg_0);

        foreach (var propName in parts)
        {
            var prop = currentType.GetProperty(propName, flags)
                ?? throw new InvalidOperationException($"Property '{propName}' not found on '{currentType.Name}'");

            var getMethod = prop.GetGetMethod()
                ?? throw new InvalidOperationException($"Property '{propName}' has no getter");

            // Call the getter (use Callvirt for virtual/interface methods)
            if (getMethod.IsVirtual)
            {
                il.Emit(OpCodes.Callvirt, getMethod);
            }
            else
            {
                il.Emit(OpCodes.Call, getMethod);
            }

            currentType = prop.PropertyType;
        }

        // Convert if necessary (e.g., int to object)
        if (currentType != typeof(TResult))
        {
            if (typeof(TResult) == typeof(object) && currentType.IsValueType)
            {
                il.Emit(OpCodes.Box, currentType);
            }
            else if (currentType == typeof(object) && typeof(TResult).IsValueType)
            {
                il.Emit(OpCodes.Unbox_Any, typeof(TResult));
            }
        }

        il.Emit(OpCodes.Ret);

        return dm.CreateDelegate<Func<TSource, TResult>>();
    }

    private static Action<TSource, TValue> EmitSetter<TSource, TValue>(string propertyName)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

        var prop = typeof(TSource).GetProperty(propertyName, flags)
            ?? throw new InvalidOperationException($"Property '{propertyName}' not found on '{typeof(TSource).Name}'");

        var setMethod = prop.GetSetMethod()
            ?? throw new InvalidOperationException($"Property '{propertyName}' has no setter");

        var dm = new DynamicMethod(
            $"Set_{typeof(TSource).Name}_{propertyName}",
            typeof(void),
            [typeof(TSource), typeof(TValue)],
            typeof(TypedEmitAccessor).Module,
            skipVisibility: true
        );

        var il = dm.GetILGenerator();

        // Load target
        il.Emit(OpCodes.Ldarg_0);

        // Load value
        il.Emit(OpCodes.Ldarg_1);

        // Convert if necessary
        if (typeof(TValue) != prop.PropertyType)
        {
            if (typeof(TValue) == typeof(object) && prop.PropertyType.IsValueType)
            {
                il.Emit(OpCodes.Unbox_Any, prop.PropertyType);
            }
        }

        // Call setter
        if (setMethod.IsVirtual)
        {
            il.Emit(OpCodes.Callvirt, setMethod);
        }
        else
        {
            il.Emit(OpCodes.Call, setMethod);
        }

        il.Emit(OpCodes.Ret);

        return dm.CreateDelegate<Action<TSource, TValue>>();
    }
}

/// <summary>
/// Pre-compiled IL emit accessors for common MockMobile properties.
/// </summary>
public static class PrecompiledEmitMobileAccessors
{
    public static readonly Func<MockMobile, int> GetKarma = TypedEmitAccessor.GetGetter<MockMobile, int>("Karma");
    public static readonly Func<MockMobile, int> GetFame = TypedEmitAccessor.GetGetter<MockMobile, int>("Fame");
    public static readonly Func<MockMobile, int> GetHits = TypedEmitAccessor.GetGetter<MockMobile, int>("Hits");
    public static readonly Func<MockMobile, int> GetLocationX = TypedEmitAccessor.GetGetter<MockMobile, int>("Location.X");
    public static readonly Func<MockMobile, int> GetLocationY = TypedEmitAccessor.GetGetter<MockMobile, int>("Location.Y");

    public static readonly Action<MockMobile, int> SetKarma = TypedEmitAccessor.GetSetter<MockMobile, int>("Karma");
    public static readonly Action<MockMobile, int> SetHits = TypedEmitAccessor.GetSetter<MockMobile, int>("Hits");
}
