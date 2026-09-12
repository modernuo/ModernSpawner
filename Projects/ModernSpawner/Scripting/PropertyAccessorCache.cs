using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Server.Engines.Spawners;

/// <summary>
/// High-performance property accessor cache using compiled Expression trees.
/// Benchmarks show this achieves near-native C# performance (6% overhead vs 18x for reflection).
/// Note: All caches use Dictionary (not ConcurrentDictionary) since game logic is single-threaded.
/// </summary>
public static class PropertyAccessorCache
{
    private const BindingFlags PropertyFlags =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

    // Cache for single property accessors: (Type, propertyName) -> getter/setter
    private static readonly Dictionary<(Type, string), CompiledAccessor> _accessorCache = new();

    // Cache for chained property getters: (Type, "Prop1.Prop2.Prop3") -> getter
    private static readonly Dictionary<(Type, string), Func<object, object?>> _chainedGetterCache = new();

    // Typed getter caches to avoid boxing for numeric and boolean properties
    // Value is null if property type doesn't match (e.g., not numeric)
    private static readonly Dictionary<(Type, string), Func<object, double>?> _numericGetterCache = new();
    private static readonly Dictionary<(Type, string), Func<object, bool>?> _booleanGetterCache = new();

    // Property type cache for quick type lookup without reflection
    private static readonly Dictionary<(Type, string), Type?> _propertyTypeCache = new();

    /// <summary>
    /// Gets a property value using a compiled accessor. Handles chained paths like "Location.X".
    /// Returns boxed value - use GetNumericValue/GetBooleanValue for unboxed access.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static object? GetValue(object target, string propertyPath, char separator = '.')
    {
        if (target == null || string.IsNullOrEmpty(propertyPath))
        {
            return null;
        }

        var targetType = target.GetType();
        var cacheKey = (targetType, propertyPath);

        // Check for cached chained getter
        if (_chainedGetterCache.TryGetValue(cacheKey, out var cachedGetter))
        {
            return cachedGetter(target);
        }

        // Build and cache the chained getter
        var getter = BuildChainedGetter(targetType, propertyPath, separator);
        if (getter != null)
        {
            _chainedGetterCache[cacheKey] = getter;
            return getter(target);
        }

        return null;
    }

    /// <summary>
    /// Gets a numeric property value without boxing. Returns 0 if property doesn't exist or isn't numeric.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double GetNumericValue(object target, string propertyPath, char separator = '.')
    {
        if (target == null || string.IsNullOrEmpty(propertyPath))
        {
            return 0;
        }

        var targetType = target.GetType();
        var cacheKey = (targetType, propertyPath);

        // Check for cached numeric getter
        if (_numericGetterCache.TryGetValue(cacheKey, out var cachedGetter))
        {
            return cachedGetter?.Invoke(target) ?? 0;
        }

        // Build and cache the numeric getter
        var getter = BuildNumericGetter(targetType, propertyPath, separator);
        _numericGetterCache[cacheKey] = getter;
        return getter?.Invoke(target) ?? 0;
    }

    /// <summary>
    /// Gets a boolean property value without boxing. Returns false if property doesn't exist or isn't boolean.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool GetBooleanValue(object target, string propertyPath, char separator = '.')
    {
        if (target == null || string.IsNullOrEmpty(propertyPath))
        {
            return false;
        }

        var targetType = target.GetType();
        var cacheKey = (targetType, propertyPath);

        // Check for cached boolean getter
        if (_booleanGetterCache.TryGetValue(cacheKey, out var cachedGetter))
        {
            return cachedGetter?.Invoke(target) ?? false;
        }

        // Build and cache the boolean getter
        var getter = BuildBooleanGetter(targetType, propertyPath, separator);
        _booleanGetterCache[cacheKey] = getter;
        return getter?.Invoke(target) ?? false;
    }

    /// <summary>
    /// Gets the type of a property at the given path. Returns null if not found.
    /// Used to determine which typed getter to use.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Type? GetPropertyType(Type rootType, string propertyPath, char separator = '.')
    {
        var cacheKey = (rootType, propertyPath);

        if (_propertyTypeCache.TryGetValue(cacheKey, out var cachedType))
        {
            return cachedType;
        }

        var type = ResolvePropertyType(rootType, propertyPath, separator);
        _propertyTypeCache[cacheKey] = type;
        return type;
    }

    /// <summary>
    /// Returns true if the property type is numeric (can use GetNumericValue without boxing).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNumericProperty(Type rootType, string propertyPath, char separator = '.')
    {
        var type = GetPropertyType(rootType, propertyPath, separator);
        return type != null && IsNumericType(type);
    }

    /// <summary>
    /// Returns true if the property type is boolean (can use GetBooleanValue without boxing).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsBooleanProperty(Type rootType, string propertyPath, char separator = '.')
    {
        var type = GetPropertyType(rootType, propertyPath, separator);
        return type == typeof(bool);
    }

    /// <summary>
    /// Sets a property value using a compiled accessor. Handles chained paths like "Location/X".
    /// </summary>
    public static bool SetValue(object target, string propertyPath, object? value, char separator = '/')
    {
        if (target == null || string.IsNullOrEmpty(propertyPath))
        {
            return false;
        }

        var parts = propertyPath.Split(separator);

        // Navigate to the parent object
        var current = target;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var accessor = GetOrCreateAccessor(current.GetType(), parts[i]);
            if (accessor == null)
            {
                return false;
            }

            current = accessor.Get(current);
            if (current == null)
            {
                return false;
            }
        }

        // Set the final property
        var finalAccessor = GetOrCreateAccessor(current.GetType(), parts[^1]);
        if (finalAccessor?.CanWrite != true)
        {
            return false;
        }

        var convertedValue = ConvertValue(value, finalAccessor.PropertyType);
        finalAccessor.Set(current, convertedValue);
        return true;
    }

    /// <summary>
    /// Gets or creates a compiled accessor for a single property.
    /// </summary>
    public static CompiledAccessor? GetOrCreateAccessor(Type type, string propertyName)
    {
        var key = (type, propertyName.ToLowerInvariant());

        if (_accessorCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var prop = FindProperty(type, propertyName);
        var accessor = prop != null ? new CompiledAccessor(type, prop) : null;
        _accessorCache[key] = accessor!;
        return accessor;
    }

    /// <summary>
    /// Resolves a named property on <paramref name="type"/> that the accessor cache can actually
    /// compile, or null.
    /// </summary>
    private static PropertyInfo? FindProperty(Type type, string propertyName)
    {
        var prop = FindPropertyCore(type, propertyName);

        // A ref / ref readonly property (Mobile.DamageEntries returns ref ValueLinkList<DamageEntry>),
        // a pointer property, or one returning a ref struct cannot be boxed into
        // Func<object, object?>, so the expression tree would throw at compile time. Scripts have no
        // way to address them either, so report them as "not found" the way indexers are skipped.
        return prop != null && IsAccessorFriendly(prop.PropertyType) ? prop : null;
    }

    private static bool IsAccessorFriendly(Type propertyType) =>
        !propertyType.IsByRef && !propertyType.IsPointer && !propertyType.IsByRefLike;

    /// <summary>
    /// Resolves a named property on <paramref name="type"/> while avoiding
    /// <see cref="AmbiguousMatchException"/>:
    /// 1. Asks for a non-indexed property explicitly (types = <see cref="Type.EmptyTypes"/>),
    ///    which filters out indexer overloads like <c>Skills.this[SkillName]</c> that would
    ///    otherwise all share the reflection name "Item".
    /// 2. If that still trips on a <c>new</c>-shadowed property in the inheritance chain,
    ///    walks up the chain with <see cref="BindingFlags.DeclaredOnly"/> and returns the
    ///    most-derived match. Returns null if nothing resolves.
    /// </summary>
    private static PropertyInfo? FindPropertyCore(Type type, string propertyName)
    {
        try
        {
            return type.GetProperty(
                propertyName,
                PropertyFlags,
                binder: null,
                returnType: null,
                types: Type.EmptyTypes,
                modifiers: null);
        }
        catch (AmbiguousMatchException)
        {
            for (var t = type; t != null; t = t.BaseType)
            {
                try
                {
                    var prop = t.GetProperty(
                        propertyName,
                        PropertyFlags | BindingFlags.DeclaredOnly,
                        binder: null,
                        returnType: null,
                        types: Type.EmptyTypes,
                        modifiers: null);
                    if (prop != null)
                    {
                        return prop;
                    }
                }
                catch (AmbiguousMatchException)
                {
                    // Even declared-only was ambiguous — give up on this level.
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Pre-warms the cache for common types. Call during Configure().
    /// </summary>
    public static void PrewarmCache(params Type[] types)
    {
        foreach (var type in types)
        {
            foreach (var prop in type.GetProperties(PropertyFlags))
            {
                // Indexers (e.g. Skills.this[SkillName]) all share the name "Item" at
                // the reflection level, so they can't be looked up by name without
                // disambiguating on index parameters. The accessor cache is keyed
                // by type + name and doesn't model indexed access, so skip them.
                if (prop.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                GetOrCreateAccessor(type, prop.Name);
            }
        }
    }

    /// <summary>
    /// Clears all accessor caches. Primarily for testing.
    /// </summary>
    public static void ClearCache()
    {
        _accessorCache.Clear();
        _chainedGetterCache.Clear();
        _numericGetterCache.Clear();
        _booleanGetterCache.Clear();
        _propertyTypeCache.Clear();
    }

    private static Func<object, object?>? BuildChainedGetter(Type rootType, string propertyPath, char separator)
    {
        var parts = propertyPath.Split(separator);
        var param = Expression.Parameter(typeof(object), "target");

        try
        {
            Expression current = Expression.Convert(param, rootType);
            var currentType = rootType;
            var returnTarget = Expression.Label(typeof(object), "return");

            var expressions = new List<Expression>();
            var variables = new List<ParameterExpression>();

            foreach (var part in parts)
            {
                var prop = FindProperty(currentType, part);
                if (prop == null)
                {
                    return null;
                }

                // For reference types, add null check
                if (!currentType.IsValueType)
                {
                    var nullCheck = Expression.IfThen(
                        Expression.Equal(current, Expression.Constant(null, currentType)),
                        Expression.Return(returnTarget, Expression.Constant(null, typeof(object)))
                    );
                    expressions.Add(nullCheck);
                }

                current = Expression.Property(current, prop);
                currentType = prop.PropertyType;

                // Store intermediate result in a variable for null checking
                if (part != parts[^1] && !currentType.IsValueType)
                {
                    var temp = Expression.Variable(currentType, $"temp_{part}");
                    variables.Add(temp);
                    expressions.Add(Expression.Assign(temp, current));
                    current = temp;
                }
            }

            // Box the final result
            Expression result = currentType.IsValueType
                ? Expression.Convert(current, typeof(object))
                : current;

            expressions.Add(Expression.Return(returnTarget, result));
            expressions.Add(Expression.Label(returnTarget, Expression.Constant(null, typeof(object))));

            var body = Expression.Block(typeof(object), variables, expressions);
            return Expression.Lambda<Func<object, object?>>(body, param).Compile();
        }
        catch
        {
            return null;
        }
    }

    private static object? ConvertValue(object? value, Type targetType)
    {
        if (value == null)
        {
            return targetType.IsValueType ? targetType.CreateInstance<object>() : null;
        }

        var valueType = value.GetType();
        if (targetType.IsAssignableFrom(valueType))
        {
            return value;
        }

        // String conversions
        if (value is string str)
        {
            if (targetType == typeof(int) && int.TryParse(str, out var intVal))
            {
                return intVal;
            }

            if (targetType == typeof(double) && double.TryParse(str, out var doubleVal))
            {
                return doubleVal;
            }

            if (targetType == typeof(bool) && bool.TryParse(str, out var boolVal))
            {
                return boolVal;
            }

            if (targetType == typeof(string))
            {
                return str;
            }

            if (targetType.IsEnum && Enum.TryParse(targetType, str, true, out var enumVal))
            {
                return enumVal;
            }
        }

        // Numeric conversions
        if (IsNumericType(valueType) && IsNumericType(targetType))
        {
            return Convert.ChangeType(value, targetType);
        }

        try
        {
            return Convert.ChangeType(value, targetType);
        }
        catch
        {
            return targetType.IsValueType ? targetType.CreateInstance<object>() : null;
        }
    }

    private static bool IsNumericType(Type type)
    {
        return type == typeof(int) || type == typeof(double) || type == typeof(float) ||
               type == typeof(long) || type == typeof(short) || type == typeof(byte) ||
               type == typeof(decimal) || type == typeof(uint) || type == typeof(ulong) ||
               type == typeof(ushort) || type == typeof(sbyte);
    }

    /// <summary>
    /// Resolves the final property type for a chained property path.
    /// </summary>
    private static Type? ResolvePropertyType(Type rootType, string propertyPath, char separator)
    {
        var parts = propertyPath.Split(separator);
        var currentType = rootType;

        foreach (var part in parts)
        {
            var prop = FindProperty(currentType, part);
            if (prop == null)
            {
                return null;
            }
            currentType = prop.PropertyType;
        }

        return currentType;
    }

    /// <summary>
    /// Builds a compiled getter that returns double directly (no boxing).
    /// Returns null if property doesn't exist or isn't numeric.
    /// </summary>
    private static Func<object, double>? BuildNumericGetter(Type rootType, string propertyPath, char separator)
    {
        var parts = propertyPath.Split(separator);
        var param = Expression.Parameter(typeof(object), "target");

        try
        {
            Expression current = Expression.Convert(param, rootType);
            var currentType = rootType;
            var returnTarget = Expression.Label(typeof(double), "return");

            var expressions = new List<Expression>();
            var variables = new List<ParameterExpression>();

            foreach (var part in parts)
            {
                var prop = FindProperty(currentType, part);
                if (prop == null)
                {
                    return null;
                }

                // For reference types, add null check
                if (!currentType.IsValueType)
                {
                    var nullCheck = Expression.IfThen(
                        Expression.Equal(current, Expression.Constant(null, currentType)),
                        Expression.Return(returnTarget, Expression.Constant(0.0))
                    );
                    expressions.Add(nullCheck);
                }

                current = Expression.Property(current, prop);
                currentType = prop.PropertyType;

                // Store intermediate result in a variable for null checking
                if (part != parts[^1] && !currentType.IsValueType)
                {
                    var temp = Expression.Variable(currentType, $"temp_{part}");
                    variables.Add(temp);
                    expressions.Add(Expression.Assign(temp, current));
                    current = temp;
                }
            }

            // Final type must be numeric
            if (!IsNumericType(currentType))
            {
                return null;
            }

            // Convert to double
            Expression result = currentType == typeof(double)
                ? current
                : Expression.Convert(current, typeof(double));

            expressions.Add(Expression.Return(returnTarget, result));
            expressions.Add(Expression.Label(returnTarget, Expression.Constant(0.0)));

            var body = Expression.Block(typeof(double), variables, expressions);
            return Expression.Lambda<Func<object, double>>(body, param).Compile();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Builds a compiled getter that returns bool directly (no boxing).
    /// Returns null if property doesn't exist or isn't boolean.
    /// </summary>
    private static Func<object, bool>? BuildBooleanGetter(Type rootType, string propertyPath, char separator)
    {
        var parts = propertyPath.Split(separator);
        var param = Expression.Parameter(typeof(object), "target");

        try
        {
            Expression current = Expression.Convert(param, rootType);
            var currentType = rootType;
            var returnTarget = Expression.Label(typeof(bool), "return");

            var expressions = new List<Expression>();
            var variables = new List<ParameterExpression>();

            foreach (var part in parts)
            {
                var prop = FindProperty(currentType, part);
                if (prop == null)
                {
                    return null;
                }

                // For reference types, add null check
                if (!currentType.IsValueType)
                {
                    var nullCheck = Expression.IfThen(
                        Expression.Equal(current, Expression.Constant(null, currentType)),
                        Expression.Return(returnTarget, Expression.Constant(false))
                    );
                    expressions.Add(nullCheck);
                }

                current = Expression.Property(current, prop);
                currentType = prop.PropertyType;

                // Store intermediate result in a variable for null checking
                if (part != parts[^1] && !currentType.IsValueType)
                {
                    var temp = Expression.Variable(currentType, $"temp_{part}");
                    variables.Add(temp);
                    expressions.Add(Expression.Assign(temp, current));
                    current = temp;
                }
            }

            // Final type must be boolean
            if (currentType != typeof(bool))
            {
                return null;
            }

            expressions.Add(Expression.Return(returnTarget, current));
            expressions.Add(Expression.Label(returnTarget, Expression.Constant(false)));

            var body = Expression.Block(typeof(bool), variables, expressions);
            return Expression.Lambda<Func<object, bool>>(body, param).Compile();
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Compiled getter and setter delegates for a single property.
/// </summary>
public class CompiledAccessor
{
    private readonly Func<object, object?> _getter;
    private readonly Action<object, object?>? _setter;

    public Type PropertyType { get; }
    public bool CanWrite { get; }

    public CompiledAccessor(Type declaringType, PropertyInfo property)
    {
        PropertyType = property.PropertyType;
        CanWrite = property.CanWrite;

        _getter = CompileGetter(declaringType, property);

        if (property.CanWrite)
        {
            _setter = CompileSetter(declaringType, property);
        }
    }

    public object? Get(object target) => _getter(target);

    public void Set(object target, object? value) => _setter?.Invoke(target, value);

    private static Func<object, object?> CompileGetter(Type declaringType, PropertyInfo property)
    {
        var param = Expression.Parameter(typeof(object), "target");
        var castTarget = Expression.Convert(param, declaringType);
        var propertyAccess = Expression.Property(castTarget, property);

        Expression body = property.PropertyType.IsValueType
            ? Expression.Convert(propertyAccess, typeof(object))
            : propertyAccess;

        return Expression.Lambda<Func<object, object?>>(body, param).Compile();
    }

    private static Action<object, object?> CompileSetter(Type declaringType, PropertyInfo property)
    {
        var targetParam = Expression.Parameter(typeof(object), "target");
        var valueParam = Expression.Parameter(typeof(object), "value");

        var castTarget = Expression.Convert(targetParam, declaringType);
        var castValue = Expression.Convert(valueParam, property.PropertyType);
        var propertyAccess = Expression.Property(castTarget, property);
        var assign = Expression.Assign(propertyAccess, castValue);

        return Expression.Lambda<Action<object, object?>>(assign, targetParam, valueParam).Compile();
    }
}
