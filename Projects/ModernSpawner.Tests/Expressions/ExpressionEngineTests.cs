using Server.Engines.ModernSpawner.Scripting;
using Server.Engines.ModernSpawner.Scripting.Expressions;
using Xunit;

namespace Server.Engines.ModernSpawner.Tests.Expressions;

public class ExpressionEngineTests
{
    private readonly ExpressionEngine _engine = new();

    private static ScriptContext CreateEmptyContext()
    {
        return new ScriptContext(null);
    }

    #region Literal Evaluation

    [Theory]
    [InlineData("42", 42.0)]
    [InlineData("3.14", 3.14)]
    [InlineData("0", 0.0)]
    [InlineData("-5", -5.0)]
    public void Evaluate_NumberLiterals_ReturnsCorrectValue(string expression, double expected)
    {
        var context = CreateEmptyContext();
        var result = _engine.EvaluateNumber(expression, context);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("\"hello\"", "hello")]
    [InlineData("'world'", "world")]
    [InlineData("\"\"", "")]
    public void Evaluate_StringLiterals_ReturnsCorrectValue(string expression, string expected)
    {
        var context = CreateEmptyContext();
        var result = _engine.Evaluate(expression, context);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("TRUE", true)]
    [InlineData("False", false)]
    public void Evaluate_BooleanLiterals_ReturnsCorrectValue(string expression, bool expected)
    {
        var context = CreateEmptyContext();
        var result = _engine.EvaluateBoolean(expression, context);

        Assert.Equal(expected, result);
    }

    #endregion

    #region Arithmetic Operations

    [Theory]
    [InlineData("10 + 5", 15.0)]
    [InlineData("10 - 5", 5.0)]
    [InlineData("10 * 5", 50.0)]
    [InlineData("10 / 5", 2.0)]
    [InlineData("10 / 0", 0.0)] // Division by zero returns 0
    public void Evaluate_Arithmetic_ReturnsCorrectValue(string expression, double expected)
    {
        var context = CreateEmptyContext();
        var result = _engine.EvaluateNumber(expression, context);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("1 + 2 * 3", 7.0)]      // 1 + (2 * 3) = 7
    [InlineData("(1 + 2) * 3", 9.0)]    // (1 + 2) * 3 = 9
    [InlineData("10 - 2 - 3", 5.0)]     // (10 - 2) - 3 = 5
    [InlineData("10 / 2 / 2", 2.5)]     // (10 / 2) / 2 = 2.5
    public void Evaluate_ArithmeticPrecedence_ReturnsCorrectValue(string expression, double expected)
    {
        var context = CreateEmptyContext();
        var result = _engine.EvaluateNumber(expression, context);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Evaluate_StringConcatenation_ConcatenatesStrings()
    {
        var context = CreateEmptyContext();
        var result = _engine.Evaluate("\"hello\" + \" \" + \"world\"", context);

        Assert.Equal("hello world", result);
    }

    #endregion

    #region Comparison Operations

    [Theory]
    [InlineData("5 < 10", true)]
    [InlineData("10 < 5", false)]
    [InlineData("5 < 5", false)]
    [InlineData("5 <= 10", true)]
    [InlineData("5 <= 5", true)]
    [InlineData("10 <= 5", false)]
    [InlineData("10 > 5", true)]
    [InlineData("5 > 10", false)]
    [InlineData("5 > 5", false)]
    [InlineData("10 >= 5", true)]
    [InlineData("5 >= 5", true)]
    [InlineData("5 >= 10", false)]
    [InlineData("5 == 5", true)]
    [InlineData("5 == 10", false)]
    [InlineData("5 != 10", true)]
    [InlineData("5 != 5", false)]
    public void Evaluate_Comparisons_ReturnsCorrectValue(string expression, bool expected)
    {
        var context = CreateEmptyContext();
        var result = _engine.EvaluateBoolean(expression, context);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("\"abc\" == \"abc\"", true)]
    [InlineData("\"abc\" == \"ABC\"", true)] // Case-insensitive
    [InlineData("\"abc\" != \"xyz\"", true)]
    public void Evaluate_StringComparisons_ReturnsCorrectValue(string expression, bool expected)
    {
        var context = CreateEmptyContext();
        var result = _engine.EvaluateBoolean(expression, context);

        Assert.Equal(expected, result);
    }

    #endregion

    #region Boolean Operations

    [Theory]
    [InlineData("true and true", true)]
    [InlineData("true and false", false)]
    [InlineData("false and true", false)]
    [InlineData("false and false", false)]
    [InlineData("true or true", true)]
    [InlineData("true or false", true)]
    [InlineData("false or true", true)]
    [InlineData("false or false", false)]
    [InlineData("not true", false)]
    [InlineData("not false", true)]
    public void Evaluate_BooleanOperations_ReturnsCorrectValue(string expression, bool expected)
    {
        var context = CreateEmptyContext();
        var result = _engine.EvaluateBoolean(expression, context);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("true or false and false", true)]    // true or (false and false) = true
    [InlineData("false and true or true", true)]     // (false and true) or true = true
    [InlineData("not true and false", false)]        // (not true) and false = false
    public void Evaluate_BooleanPrecedence_ReturnsCorrectValue(string expression, bool expected)
    {
        var context = CreateEmptyContext();
        var result = _engine.EvaluateBoolean(expression, context);

        Assert.Equal(expected, result);
    }

    #endregion

    #region Conditional Expressions

    [Theory]
    [InlineData("if true then 1 else 0", 1.0)]
    [InlineData("if false then 1 else 0", 0.0)]
    [InlineData("if 5 > 3 then 100 else 200", 100.0)]
    [InlineData("if 5 < 3 then 100 else 200", 200.0)]
    public void Evaluate_Conditional_ReturnsCorrectBranch(string expression, double expected)
    {
        var context = CreateEmptyContext();
        var result = _engine.EvaluateNumber(expression, context);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Evaluate_NestedConditional_ReturnsCorrectValue()
    {
        var context = CreateEmptyContext();
        // if false then 1 else if true then 2 else 3
        var result = _engine.EvaluateNumber("if false then 1 else if true then 2 else 3", context);

        Assert.Equal(2.0, result);
    }

    #endregion

    #region Complex Expressions

    [Fact]
    public void Evaluate_ComplexBooleanExpression_ReturnsCorrectValue()
    {
        var context = CreateEmptyContext();
        // (10 > 5 and 3 < 7) or false
        var result = _engine.EvaluateBoolean("(10 > 5 and 3 < 7) or false", context);

        Assert.True(result);
    }

    [Fact]
    public void Evaluate_ComplexArithmeticExpression_ReturnsCorrectValue()
    {
        var context = CreateEmptyContext();
        // (10 + 5) * 2 - 3
        var result = _engine.EvaluateNumber("(10 + 5) * 2 - 3", context);

        Assert.Equal(27.0, result);
    }

    #endregion

    #region Caching

    [Fact]
    public void Compile_SameExpression_ReturnsCachedResult()
    {
        var compiled1 = _engine.Compile("10 + 5");
        var compiled2 = _engine.Compile("10 + 5");

        Assert.Same(compiled1, compiled2);
    }

    [Fact]
    public void ClearCache_RemovesCachedExpressions()
    {
        var compiled1 = _engine.Compile("10 + 5");
        _engine.ClearCache();
        var compiled2 = _engine.Compile("10 + 5");

        Assert.NotSame(compiled1, compiled2);
    }

    #endregion

    #region Error Handling

    [Fact]
    public void Compile_InvalidSyntax_ReturnsInvalidExpression()
    {
        var compiled = _engine.Compile("10 + +");

        Assert.False(compiled.IsValid);
        Assert.NotEmpty(compiled.Errors);
    }

    [Fact]
    public void Compile_EmptyExpression_ReturnsEmptyExpression()
    {
        var compiled = _engine.Compile("");

        Assert.Same(CompiledExpression.Empty, compiled);
    }

    [Fact]
    public void Compile_NullExpression_ReturnsEmptyExpression()
    {
        var compiled = _engine.Compile(null);

        Assert.Same(CompiledExpression.Empty, compiled);
    }

    [Fact]
    public void Evaluate_InvalidExpression_ReturnsNull()
    {
        var context = CreateEmptyContext();
        var compiled = _engine.Compile("invalid syntax +++");
        var result = _engine.Evaluate(compiled, context);

        Assert.Null(result);
    }

    #endregion

    #region Truthiness

    [Theory]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("-1", true)]
    [InlineData("\"hello\"", true)]
    [InlineData("\"\"", false)]
    public void EvaluateBoolean_NonBooleanValues_ReturnsTruthiness(string expression, bool expected)
    {
        var context = CreateEmptyContext();
        var result = _engine.EvaluateBoolean(expression, context);

        Assert.Equal(expected, result);
    }

    #endregion
}
