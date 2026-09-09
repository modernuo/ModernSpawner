using Server.Engines.Spawners;

namespace Server.Engines.ModernSpawner.Scripting.Ast;

/// <summary>
/// AST node that sets a property on the target entity.
/// </summary>
public class SetPropertyNode : ScriptNode
{
    /// <summary>
    /// The property path (e.g., "Hits", "Name", "Backpack.MaxItems").
    /// </summary>
    public string PropertyPath { get; }

    /// <summary>
    /// The value expression to set.
    /// </summary>
    public ValueExpression Value { get; }

    public SetPropertyNode(string propertyPath, ValueExpression value)
    {
        PropertyPath = propertyPath;
        Value = value;
    }

    public override void Execute(ScriptContext context)
    {
        if (context.Target == null)
        {
            return;
        }

        var value = Value.Evaluate(context);

        // Use compiled accessor cache for near-native performance
        PropertyAccessorCache.SetValue(context.Target, PropertyPath, value, '/');
    }
}

/// <summary>
/// Base class for value expressions that can be evaluated.
/// </summary>
public abstract class ValueExpression
{
    public abstract object Evaluate(ScriptContext context);
}

/// <summary>
/// A literal value (string, number, etc.).
/// </summary>
public class LiteralValue : ValueExpression
{
    public object Value { get; }

    public LiteralValue(object value)
    {
        Value = value;
    }

    public override object Evaluate(ScriptContext context) => Value;
}

/// <summary>
/// A reference to a context variable.
/// </summary>
public class VariableReference : ValueExpression
{
    public string VariableName { get; }

    public VariableReference(string variableName)
    {
        VariableName = variableName;
    }

    public override object Evaluate(ScriptContext context)
    {
        return context.GetVariable<object>(VariableName);
    }
}

/// <summary>
/// A property read from the target or spawner.
/// </summary>
public class PropertyReference : ValueExpression
{
    public string Source { get; } // "target", "spawner", "trigger"
    public string PropertyPath { get; }

    public PropertyReference(string source, string propertyPath)
    {
        Source = source;
        PropertyPath = propertyPath;
    }

    public override object Evaluate(ScriptContext context)
    {
        object target = Source.ToLower() switch
        {
            "target" => context.Target,
            "spawner" => context.Spawner,
            "trigger" => context.TriggeringMobile,
            "entry" => context.Entry,
            _ => context.Target
        };

        // Use compiled accessor cache for near-native performance
        return PropertyAccessorCache.GetValue(target, PropertyPath, '/');
    }
}

/// <summary>
/// A random value within a range.
/// </summary>
public class RandomValue : ValueExpression
{
    public int Min { get; }
    public int Max { get; }

    public RandomValue(int min, int max)
    {
        Min = min;
        Max = max;
    }

    public override object Evaluate(ScriptContext context)
    {
        return Utility.RandomMinMax(Min, Max);
    }
}

/// <summary>
/// A value computed by the new expression engine.
/// Supports syntax like: trigMob.Karma < 0 and trigMob.Fame > 5000
/// </summary>
public class ExpressionValue : ValueExpression
{
    public string Expression { get; }
    private Expressions.CompiledExpression _compiled;

    public ExpressionValue(string expression)
    {
        Expression = expression;
    }

    public override object Evaluate(ScriptContext context)
    {
        _compiled ??= Expressions.ExpressionEngine.Instance.Compile(Expression);
        return Expressions.ExpressionEngine.Instance.Evaluate(_compiled, context);
    }

    /// <summary>
    /// Evaluates the expression as a boolean.
    /// </summary>
    public bool EvaluateBoolean(ScriptContext context)
    {
        _compiled ??= Expressions.ExpressionEngine.Instance.Compile(Expression);
        return Expressions.ExpressionEngine.Instance.EvaluateBoolean(_compiled, context);
    }

    /// <summary>
    /// Evaluates the expression as a number.
    /// </summary>
    public double EvaluateNumber(ScriptContext context)
    {
        _compiled ??= Expressions.ExpressionEngine.Instance.Compile(Expression);
        return Expressions.ExpressionEngine.Instance.EvaluateNumber(_compiled, context);
    }
}
