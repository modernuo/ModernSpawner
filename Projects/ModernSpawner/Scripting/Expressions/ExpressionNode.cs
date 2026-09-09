using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Server.Engines.Spawners;

namespace Server.Engines.ModernSpawner.Scripting.Expressions;

/// <summary>
/// Base class for all expression AST nodes.
/// Unlike ScriptNode (which executes actions), ExpressionNode evaluates to a value.
/// </summary>
public abstract class ExpressionNode
{
    /// <summary>
    /// Evaluates this expression and returns the result.
    /// </summary>
    public abstract object Evaluate(ScriptContext context);

    /// <summary>
    /// Evaluates this expression as a boolean.
    /// Override in subclasses to avoid boxing when the result is known to be boolean.
    /// </summary>
    public virtual bool EvaluateBoolean(ScriptContext context)
    {
        var result = Evaluate(context);
        return result switch
        {
            bool b => b,
            int i => i != 0,
            double d => d != 0,
            string s => !string.IsNullOrEmpty(s),
            null => false,
            _ => true
        };
    }

    /// <summary>
    /// Evaluates this expression as a string.
    /// Override in subclasses to avoid boxing when the result is known to be string.
    /// </summary>
    public virtual string EvaluateString(ScriptContext context)
    {
        var result = Evaluate(context);
        return result?.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Evaluates this expression as a number without boxing.
    /// Override in subclasses to avoid boxing when the result is known to be numeric.
    /// </summary>
    public virtual double EvaluateNumber(ScriptContext context)
    {
        var result = Evaluate(context);
        return result switch
        {
            double d => d,
            int i => i,
            float f => f,
            long l => l,
            string s => double.TryParse(s, out var d) ? d : 0,
            bool b => b ? 1 : 0,
            _ => 0
        };
    }

    /// <summary>
    /// Returns true if this expression is known to produce a numeric result.
    /// Used for optimization to avoid boxing.
    /// </summary>
    public virtual bool IsNumeric => false;

    /// <summary>
    /// Returns true if this expression is known to produce a boolean result.
    /// Used for optimization to avoid boxing.
    /// </summary>
    public virtual bool IsBoolean => false;

    /// <summary>
    /// Returns true if this expression is known to produce a string result.
    /// Used for optimization to avoid boxing.
    /// </summary>
    public virtual bool IsString => false;
}

/// <summary>
/// A literal value (number, string, boolean, null).
/// Use the static Create method to get optimized typed instances.
/// </summary>
public class LiteralExpression : ExpressionNode
{
    public object Value { get; }

    public LiteralExpression(object value)
    {
        Value = value;
    }

    public override object Evaluate(ScriptContext context) => Value;

    /// <summary>
    /// Creates an optimized literal expression based on value type.
    /// Returns typed subclasses that avoid boxing for numeric, boolean, and string values.
    /// </summary>
    public static ExpressionNode Create(object value)
    {
        return value switch
        {
            double d => new DoubleLiteralExpression(d),
            int i => new DoubleLiteralExpression(i),
            float f => new DoubleLiteralExpression(f),
            long l => new DoubleLiteralExpression(l),
            bool b => new BooleanLiteralExpression(b),
            string s => new StringLiteralExpression(s),
            _ => new LiteralExpression(value)
        };
    }
}

/// <summary>
/// Optimized literal expression for numeric values. Avoids boxing.
/// </summary>
public sealed class DoubleLiteralExpression : ExpressionNode
{
    private readonly double _value;

    public DoubleLiteralExpression(double value) => _value = value;

    public override bool IsNumeric => true;

    public override object Evaluate(ScriptContext context) => _value;

    public override double EvaluateNumber(ScriptContext context) => _value;

    public override bool EvaluateBoolean(ScriptContext context) => _value != 0;
}

/// <summary>
/// Optimized literal expression for boolean values. Avoids boxing.
/// </summary>
public sealed class BooleanLiteralExpression : ExpressionNode
{
    private readonly bool _value;

    public BooleanLiteralExpression(bool value) => _value = value;

    public override bool IsBoolean => true;

    public override object Evaluate(ScriptContext context) => _value;

    public override double EvaluateNumber(ScriptContext context) => _value ? 1 : 0;

    public override bool EvaluateBoolean(ScriptContext context) => _value;
}

/// <summary>
/// Optimized literal expression for string values. Avoids allocation on evaluation.
/// </summary>
public sealed class StringLiteralExpression : ExpressionNode
{
    private readonly string _value;

    public StringLiteralExpression(string value) => _value = value ?? string.Empty;

    public override bool IsString => true;

    public override object Evaluate(ScriptContext context) => _value;

    public override string EvaluateString(ScriptContext context) => _value;

    public override bool EvaluateBoolean(ScriptContext context) => !string.IsNullOrEmpty(_value);
}

/// <summary>
/// A property access expression (e.g., trigMob.Karma, target.Hits).
/// Caches the property path at construction to avoid allocations during evaluation.
/// </summary>
public class PropertyAccessExpression : ExpressionNode
{
    private readonly ObjectNameType _objectType;
    private readonly string _cachedPropertyPath;
    private bool _typeInfoResolved;
    private bool _isNumeric;
    private bool _isBoolean;

    public string ObjectName { get; }
    public List<string> PropertyPath { get; }

    public override bool IsNumeric => ResolveTypeInfo()._isNumeric;
    public override bool IsBoolean => ResolveTypeInfo()._isBoolean;

    public PropertyAccessExpression(string objectName, List<string> propertyPath)
    {
        ObjectName = objectName;
        PropertyPath = propertyPath;

        // Cache property path at construction (one-time string allocation)
        _cachedPropertyPath = string.Join(".", propertyPath);

        // Parse object name at construction to avoid per-evaluation string operations
        _objectType = ParseObjectName(objectName);
    }

    public override object Evaluate(ScriptContext context)
    {
        var target = ResolveObject(context);
        if (target == null)
        {
            return null;
        }

        return PropertyAccessorCache.GetValue(target, _cachedPropertyPath, '.');
    }

    /// <summary>
    /// Optimized numeric evaluation - avoids boxing for numeric properties.
    /// </summary>
    public override double EvaluateNumber(ScriptContext context)
    {
        var target = ResolveObject(context);
        if (target == null)
        {
            return 0;
        }

        return PropertyAccessorCache.GetNumericValue(target, _cachedPropertyPath, '.');
    }

    /// <summary>
    /// Optimized boolean evaluation - avoids boxing for boolean properties.
    /// </summary>
    public override bool EvaluateBoolean(ScriptContext context)
    {
        var target = ResolveObject(context);
        if (target == null)
        {
            return false;
        }

        // If property is boolean, use direct getter
        if (IsBoolean)
        {
            return PropertyAccessorCache.GetBooleanValue(target, _cachedPropertyPath, '.');
        }

        // For numeric properties, return true if non-zero
        if (IsNumeric)
        {
            return PropertyAccessorCache.GetNumericValue(target, _cachedPropertyPath, '.') != 0;
        }

        // Fallback for other types
        var result = PropertyAccessorCache.GetValue(target, _cachedPropertyPath, '.');
        return result switch
        {
            bool b => b,
            int i => i != 0,
            double d => d != 0,
            string s => !string.IsNullOrEmpty(s),
            null => false,
            _ => true
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private object ResolveObject(ScriptContext context)
    {
        return _objectType switch
        {
            ObjectNameType.TriggeringMobile => context.TriggeringMobile,
            ObjectNameType.Target => context.Target,
            ObjectNameType.Spawner => context.Spawner,
            ObjectNameType.Entry => context.Entry,
            _ => null
        };
    }

    /// <summary>
    /// Lazily resolves type information on first access.
    /// This is done lazily because we need a concrete type to check property types,
    /// which we may not have at parse time.
    /// </summary>
    private PropertyAccessExpression ResolveTypeInfo()
    {
        if (_typeInfoResolved)
        {
            return this;
        }

        // Get the base type for this object reference
        var baseType = _objectType switch
        {
            ObjectNameType.TriggeringMobile => typeof(Mobile),
            ObjectNameType.Target => typeof(IEntity), // Could be Mobile or Item
            ObjectNameType.Spawner => typeof(BaseSpawner),
            ObjectNameType.Entry => typeof(SpawnerEntry),
            _ => null
        };

        if (baseType != null)
        {
            _isNumeric = PropertyAccessorCache.IsNumericProperty(baseType, _cachedPropertyPath, '.');
            _isBoolean = PropertyAccessorCache.IsBooleanProperty(baseType, _cachedPropertyPath, '.');
        }

        _typeInfoResolved = true;
        return this;
    }

    private static ObjectNameType ParseObjectName(string name)
    {
        // Use span-based comparison to avoid ToLowerInvariant allocation
        var span = name.AsSpan();

        if (span.Equals("trigmob", StringComparison.OrdinalIgnoreCase) ||
            span.Equals("triggermob", StringComparison.OrdinalIgnoreCase) ||
            span.Equals("trigger", StringComparison.OrdinalIgnoreCase))
        {
            return ObjectNameType.TriggeringMobile;
        }

        if (span.Equals("target", StringComparison.OrdinalIgnoreCase) ||
            span.Equals("spawned", StringComparison.OrdinalIgnoreCase))
        {
            return ObjectNameType.Target;
        }

        if (span.Equals("spawner", StringComparison.OrdinalIgnoreCase))
        {
            return ObjectNameType.Spawner;
        }

        if (span.Equals("entry", StringComparison.OrdinalIgnoreCase))
        {
            return ObjectNameType.Entry;
        }

        return ObjectNameType.Unknown;
    }

    private enum ObjectNameType : byte
    {
        Unknown,
        TriggeringMobile,
        Target,
        Spawner,
        Entry
    }
}

/// <summary>
/// A variable reference expression (e.g., $myVar).
/// </summary>
public class VariableExpression : ExpressionNode
{
    public string VariableName { get; }

    public VariableExpression(string variableName)
    {
        VariableName = variableName;
    }

    public override double EvaluateNumber(ScriptContext context)
    {
        return context.GetNumericVariable(VariableName);
    }

    public override bool EvaluateBoolean(ScriptContext context)
    {
        return context.GetBooleanVariable(VariableName);
    }

    public override string EvaluateString(ScriptContext context)
    {
        return context.GetStringVariable(VariableName);
    }

    public override object Evaluate(ScriptContext context)
    {
        return context.GetVariable<object>(VariableName);
    }
}

/// <summary>
/// A binary expression (e.g., a + b, x < y, p and q).
/// </summary>
public class BinaryExpression : ExpressionNode
{
    public ExpressionNode Left { get; }
    public TokenType Operator { get; }
    public ExpressionNode Right { get; }

    public BinaryExpression(ExpressionNode left, TokenType op, ExpressionNode right)
    {
        Left = left;
        Operator = op;
        Right = right;
    }

    /// <summary>
    /// Arithmetic operators produce numeric results.
    /// </summary>
    public override bool IsNumeric => Operator is TokenType.Plus or TokenType.Minus
        or TokenType.Star or TokenType.Slash;

    /// <summary>
    /// Comparison and logical operators produce boolean results.
    /// </summary>
    public override bool IsBoolean => Operator is TokenType.Less or TokenType.LessEqual
        or TokenType.Greater or TokenType.GreaterEqual or TokenType.Equal or TokenType.NotEqual
        or TokenType.And or TokenType.Or;

    /// <summary>
    /// Optimized numeric evaluation - avoids boxing for arithmetic operations.
    /// </summary>
    public override double EvaluateNumber(ScriptContext context)
    {
        return Operator switch
        {
            TokenType.Plus => Left.EvaluateNumber(context) + Right.EvaluateNumber(context),
            TokenType.Minus => Left.EvaluateNumber(context) - Right.EvaluateNumber(context),
            TokenType.Star => Left.EvaluateNumber(context) * Right.EvaluateNumber(context),
            TokenType.Slash => DivideNumbers(Left.EvaluateNumber(context), Right.EvaluateNumber(context)),
            _ => base.EvaluateNumber(context)
        };
    }

    /// <summary>
    /// Optimized boolean evaluation - avoids boxing for comparisons.
    /// </summary>
    public override bool EvaluateBoolean(ScriptContext context)
    {
        return Operator switch
        {
            // Logical operators with short-circuit evaluation
            TokenType.And => Left.EvaluateBoolean(context) && Right.EvaluateBoolean(context),
            TokenType.Or => Left.EvaluateBoolean(context) || Right.EvaluateBoolean(context),

            // Numeric comparisons - use unboxed path
            TokenType.Less => Left.EvaluateNumber(context) < Right.EvaluateNumber(context),
            TokenType.LessEqual => Left.EvaluateNumber(context) <= Right.EvaluateNumber(context),
            TokenType.Greater => Left.EvaluateNumber(context) > Right.EvaluateNumber(context),
            TokenType.GreaterEqual => Left.EvaluateNumber(context) >= Right.EvaluateNumber(context),

            // Equality - need to handle mixed types
            TokenType.Equal => AreEqual(Left, Right, context),
            TokenType.NotEqual => !AreEqual(Left, Right, context),

            _ => base.EvaluateBoolean(context)
        };
    }

    public override object Evaluate(ScriptContext context)
    {
        // Use optimized paths when possible
        if (IsNumeric && Operator != TokenType.Plus)
        {
            return EvaluateNumber(context);
        }

        if (IsBoolean)
        {
            return EvaluateBoolean(context);
        }

        // Fall back to object evaluation for string concatenation
        var leftVal = Left.Evaluate(context);
        var rightVal = Right.Evaluate(context);

        return Operator switch
        {
            TokenType.Plus => Add(leftVal, rightVal),
            _ => throw new InvalidOperationException($"Unknown binary operator: {Operator}")
        };
    }

    private static double DivideNumbers(double left, double right)
    {
        return right == 0 ? 0.0 : left / right;
    }

    private static object Add(object left, object right)
    {
        // String concatenation
        if (left is string || right is string)
        {
            return $"{left}{right}";
        }

        return ToDouble(left) + ToDouble(right);
    }

    private static bool AreEqual(ExpressionNode left, ExpressionNode right, ScriptContext context)
    {
        // Optimize for numeric comparisons
        if (left.IsNumeric && right.IsNumeric)
        {
            return Math.Abs(left.EvaluateNumber(context) - right.EvaluateNumber(context)) < 0.0001;
        }

        // Optimize for boolean comparisons
        if (left.IsBoolean && right.IsBoolean)
        {
            return left.EvaluateBoolean(context) == right.EvaluateBoolean(context);
        }

        // Fall back to object comparison
        var leftVal = left.Evaluate(context);
        var rightVal = right.Evaluate(context);

        if (leftVal == null && rightVal == null) return true;
        if (leftVal == null || rightVal == null) return false;

        // String comparison
        if (leftVal is string ls && rightVal is string rs)
        {
            return string.Equals(ls, rs, StringComparison.OrdinalIgnoreCase);
        }

        // Boolean comparison
        if (leftVal is bool lb && rightVal is bool rb)
        {
            return lb == rb;
        }

        // Numeric comparison
        return Math.Abs(ToDouble(leftVal) - ToDouble(rightVal)) < 0.0001;
    }

    private static double ToDouble(object value)
    {
        return value switch
        {
            double d => d,
            int i => i,
            float f => f,
            long l => l,
            string s => double.TryParse(s, out var d) ? d : 0,
            bool b => b ? 1 : 0,
            _ => 0
        };
    }
}

/// <summary>
/// A unary expression (e.g., not x, -5).
/// </summary>
public class UnaryExpression : ExpressionNode
{
    public TokenType Operator { get; }
    public ExpressionNode Operand { get; }

    public UnaryExpression(TokenType op, ExpressionNode operand)
    {
        Operator = op;
        Operand = operand;
    }

    public override bool IsNumeric => Operator == TokenType.Minus;
    public override bool IsBoolean => Operator == TokenType.Not;

    public override double EvaluateNumber(ScriptContext context)
    {
        return Operator == TokenType.Minus
            ? -Operand.EvaluateNumber(context)
            : base.EvaluateNumber(context);
    }

    public override bool EvaluateBoolean(ScriptContext context)
    {
        return Operator == TokenType.Not
            ? !Operand.EvaluateBoolean(context)
            : base.EvaluateBoolean(context);
    }

    public override object Evaluate(ScriptContext context)
    {
        return Operator switch
        {
            TokenType.Not => EvaluateBoolean(context),
            TokenType.Minus => EvaluateNumber(context),
            _ => throw new InvalidOperationException($"Unknown unary operator: {Operator}")
        };
    }
}

/// <summary>
/// A function call expression (e.g., random(50, 100), isNight()).
/// </summary>
public class FunctionCallExpression : ExpressionNode
{
    public string FunctionName { get; }
    public List<ExpressionNode> Arguments { get; }
    private readonly bool _isNumeric;
    private readonly bool _isBoolean;
    private readonly bool _isString;

    public FunctionCallExpression(string functionName, List<ExpressionNode> arguments)
    {
        FunctionName = functionName;
        Arguments = arguments;
        _isNumeric = BuiltInFunctions.IsNumericFunction(functionName);
        _isBoolean = BuiltInFunctions.IsBooleanFunction(functionName);
        _isString = BuiltInFunctions.IsStringFunction(functionName);
    }

    public override bool IsString => _isString;
    public override bool IsNumeric => _isNumeric;
    public override bool IsBoolean => _isBoolean;

    public override double EvaluateNumber(ScriptContext context)
    {
        return _isNumeric
            ? BuiltInFunctions.InvokeNumeric(FunctionName, Arguments, context)
            : base.EvaluateNumber(context);
    }

    public override bool EvaluateBoolean(ScriptContext context)
    {
        return _isBoolean
            ? BuiltInFunctions.InvokeBoolean(FunctionName, Arguments, context)
            : base.EvaluateBoolean(context);
    }

    public override string EvaluateString(ScriptContext context)
    {
        return _isString
            ? BuiltInFunctions.InvokeString(FunctionName, Arguments, context)
            : base.EvaluateString(context);
    }

    public override object Evaluate(ScriptContext context)
    {
        // Use optimized paths when possible to avoid boxing
        if (_isNumeric)
        {
            return BuiltInFunctions.InvokeNumeric(FunctionName, Arguments, context);
        }

        if (_isBoolean)
        {
            return BuiltInFunctions.InvokeBoolean(FunctionName, Arguments, context);
        }

        if (_isString)
        {
            return BuiltInFunctions.InvokeString(FunctionName, Arguments, context);
        }

        return BuiltInFunctions.Invoke(FunctionName, Arguments, context);
    }
}

/// <summary>
/// A conditional expression (if ... then ... else ...).
/// </summary>
public class ConditionalExpression : ExpressionNode
{
    public ExpressionNode Condition { get; }
    public ExpressionNode ThenBranch { get; }
    public ExpressionNode ElseBranch { get; }

    public ConditionalExpression(ExpressionNode condition, ExpressionNode thenBranch, ExpressionNode elseBranch)
    {
        Condition = condition;
        ThenBranch = thenBranch;
        ElseBranch = elseBranch;
    }

    public override object Evaluate(ScriptContext context)
    {
        return Condition.EvaluateBoolean(context)
            ? ThenBranch.Evaluate(context)
            : ElseBranch.Evaluate(context);
    }
}

/// <summary>
/// A grouped expression (parentheses).
/// </summary>
public class GroupExpression : ExpressionNode
{
    public ExpressionNode Inner { get; }

    public GroupExpression(ExpressionNode inner)
    {
        Inner = inner;
    }

    public override object Evaluate(ScriptContext context) => Inner.Evaluate(context);
}
