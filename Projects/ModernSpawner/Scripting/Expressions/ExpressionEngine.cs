using System;
using System.Collections.Generic;
using Server.Logging;

namespace Server.Engines.ModernSpawner.Scripting.Expressions;

/// <summary>
/// The main expression engine that compiles and evaluates expressions.
/// </summary>
public class ExpressionEngine
{
    private static readonly ILogger Logger = LogFactory.GetLogger(typeof(ExpressionEngine));

    /// <summary>
    /// Singleton instance for convenience.
    /// </summary>
    public static ExpressionEngine Instance { get; } = new();

    /// <summary>
    /// Cache of compiled expressions.
    /// </summary>
    private readonly Dictionary<string, CompiledExpression> _cache = new();

    /// <summary>
    /// Compiles an expression string into a reusable compiled expression.
    /// </summary>
    public CompiledExpression Compile(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return CompiledExpression.Empty;
        }

        // Check cache
        if (_cache.TryGetValue(expression, out var cached))
        {
            return cached;
        }

        var compiled = CompileInternal(expression);
        _cache[expression] = compiled;
        return compiled;
    }

    /// <summary>
    /// Evaluates an expression and returns the result.
    /// </summary>
    public object Evaluate(string expression, ScriptContext context)
    {
        var compiled = Compile(expression);
        return Evaluate(compiled, context);
    }

    /// <summary>
    /// Evaluates a compiled expression and returns the result.
    /// </summary>
    public object Evaluate(CompiledExpression expression, ScriptContext context)
    {
        if (expression?.IsValid != true || context == null)
        {
            return null;
        }

        try
        {
            return expression.Root.Evaluate(context);
        }
        catch (Exception ex)
        {
            ReportEvaluationError(expression, ex);
            return null;
        }
    }

    /// <summary>
    /// Evaluates an expression as a boolean.
    /// </summary>
    public bool EvaluateBoolean(string expression, ScriptContext context)
    {
        var compiled = Compile(expression);
        return EvaluateBoolean(compiled, context);
    }

    /// <summary>
    /// Evaluates a compiled expression as a boolean.
    /// </summary>
    public bool EvaluateBoolean(CompiledExpression expression, ScriptContext context)
    {
        if (expression?.IsValid != true || context == null)
        {
            return false;
        }

        try
        {
            return expression.Root.EvaluateBoolean(context);
        }
        catch (Exception ex)
        {
            ReportEvaluationError(expression, ex);
            return false;
        }
    }

    /// <summary>
    /// Evaluates an expression as a number.
    /// </summary>
    public double EvaluateNumber(string expression, ScriptContext context)
    {
        var compiled = Compile(expression);
        return EvaluateNumber(compiled, context);
    }

    /// <summary>
    /// Evaluates a compiled expression as a number.
    /// </summary>
    public double EvaluateNumber(CompiledExpression expression, ScriptContext context)
    {
        if (expression?.IsValid != true || context == null)
        {
            return 0;
        }

        try
        {
            return expression.Root.EvaluateNumber(context);
        }
        catch (Exception ex)
        {
            ReportEvaluationError(expression, ex);
            return 0;
        }
    }

    /// <summary>
    /// Reports an expression that threw while being evaluated, once per compiled expression.
    /// </summary>
    /// <remarks>
    /// A <c>when:</c> condition is evaluated on the movement and speech paths, so an expression that
    /// throws - a property that does not exist on the mobile that happened to walk past, say - would
    /// otherwise log on every event. The compiled expression remembers that it has been reported, so
    /// the shard gets one line per broken expression rather than one per step.
    /// </remarks>
    /// <param name="expression">The expression that threw.</param>
    /// <param name="ex">What it threw.</param>
    private static void ReportEvaluationError(CompiledExpression expression, Exception ex)
    {
        if (expression == null || expression.HasReportedEvaluationError)
        {
            return;
        }

        expression.HasReportedEvaluationError = true;
        Logger.Warning(ex, "Expression {Expression} threw while being evaluated.", expression.Source);
    }

    /// <summary>
    /// Clears the expression cache.
    /// </summary>
    public void ClearCache()
    {
        _cache.Clear();
    }

    private static CompiledExpression CompileInternal(string expression)
    {
        var errors = new List<string>();

        // Tokenize
        var lexer = new ExpressionLexer(expression);
        var tokens = lexer.Tokenize();
        errors.AddRange(lexer.Errors);

        if (errors.Count > 0)
        {
            return CompiledExpression.Failed(expression, [.. errors]);
        }

        // Parse
        var parser = new ExpressionParser(tokens);
        var root = parser.Parse();
        errors.AddRange(parser.Errors);

        if (errors.Count > 0 || root == null)
        {
            return CompiledExpression.Failed(expression, [.. errors]);
        }

        return new CompiledExpression(expression, root);
    }
}

/// <summary>
/// A compiled expression that can be evaluated multiple times efficiently.
/// </summary>
public class CompiledExpression
{
    /// <summary>
    /// The original expression string.
    /// </summary>
    public string Source { get; }

    /// <summary>
    /// The root AST node of the compiled expression.
    /// </summary>
    public ExpressionNode Root { get; }

    /// <summary>
    /// Any errors that occurred during compilation.
    /// </summary>
    public string[] Errors { get; }

    /// <summary>
    /// Whether the expression was compiled successfully.
    /// </summary>
    public bool IsValid => Root != null && Errors.Length == 0;

    /// <summary>
    /// Whether a failed evaluation of this expression has already been logged. Compiled expressions
    /// are cached and shared, so this turns a per-event log into a per-expression one.
    /// </summary>
    internal bool HasReportedEvaluationError { get; set; }

    /// <summary>
    /// An empty expression that evaluates to null.
    /// </summary>
    public static CompiledExpression Empty { get; } = new(string.Empty, new LiteralExpression(null));

    public CompiledExpression(string source, ExpressionNode root)
    {
        Source = source;
        Root = root;
        Errors = [];
    }

    private CompiledExpression(string source, string[] errors)
    {
        Source = source;
        Root = null;
        Errors = errors;
    }

    public static CompiledExpression Failed(string source, string[] errors)
    {
        return new CompiledExpression(source, errors);
    }
}
