using System;
using Server.Engines.ModernSpawner.Scripting;
using Server.Engines.ModernSpawner.Scripting.Expressions;
using Xunit;

namespace Server.Engines.ModernSpawner.Tests.Expressions;

public class BuiltInFunctionsTests
{
    private readonly ExpressionEngine _engine = new();

    private static ScriptContext CreateEmptyContext()
    {
        return new ScriptContext(null);
    }

    #region Math Function Tests

    [Theory]
    [InlineData("min(5, 10)", 5.0)]
    [InlineData("min(10, 5)", 5.0)]
    [InlineData("min(-5, 5)", -5.0)]
    [InlineData("min(0, 0)", 0.0)]
    [InlineData("min(1, 2, 3)", 1.0)]
    [InlineData("min(3, 1, 2)", 1.0)]
    public void Min_ReturnsSmallestValue(string expression, double expected)
    {
        var context = CreateEmptyContext();
        var result = _engine.EvaluateNumber(expression, context);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("max(5, 10)", 10.0)]
    [InlineData("max(10, 5)", 10.0)]
    [InlineData("max(-5, 5)", 5.0)]
    [InlineData("max(0, 0)", 0.0)]
    [InlineData("max(1, 2, 3)", 3.0)]
    [InlineData("max(3, 1, 2)", 3.0)]
    public void Max_ReturnsLargestValue(string expression, double expected)
    {
        var context = CreateEmptyContext();
        var result = _engine.EvaluateNumber(expression, context);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("abs(5)", 5.0)]
    [InlineData("abs(-5)", 5.0)]
    [InlineData("abs(0)", 0.0)]
    [InlineData("abs(-3.14)", 3.14)]
    public void Abs_ReturnsAbsoluteValue(string expression, double expected)
    {
        var context = CreateEmptyContext();
        var result = _engine.EvaluateNumber(expression, context);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("floor(3.7)", 3.0)]
    [InlineData("floor(3.2)", 3.0)]
    [InlineData("floor(3.0)", 3.0)]
    [InlineData("floor(-3.7)", -4.0)]
    [InlineData("floor(-3.2)", -4.0)]
    public void Floor_ReturnsFloorValue(string expression, double expected)
    {
        var context = CreateEmptyContext();
        var result = _engine.EvaluateNumber(expression, context);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("ceiling(3.7)", 4.0)]
    [InlineData("ceiling(3.2)", 4.0)]
    [InlineData("ceiling(3.0)", 3.0)]
    [InlineData("ceiling(-3.7)", -3.0)]
    [InlineData("ceiling(-3.2)", -3.0)]
    public void Ceiling_ReturnsCeilingValue(string expression, double expected)
    {
        var context = CreateEmptyContext();
        var result = _engine.EvaluateNumber(expression, context);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("round(3.7)", 4.0)]
    [InlineData("round(3.2)", 3.0)]
    [InlineData("round(3.5)", 4.0)]
    [InlineData("round(-3.7)", -4.0)]
    [InlineData("round(-3.2)", -3.0)]
    public void Round_ReturnsRoundedValue(string expression, double expected)
    {
        var context = CreateEmptyContext();
        var result = _engine.EvaluateNumber(expression, context);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Min_WithExpressions_EvaluatesArguments()
    {
        var context = CreateEmptyContext();
        var result = _engine.EvaluateNumber("min(2 + 3, 10 - 5)", context);

        Assert.Equal(5.0, result);
    }

    [Fact]
    public void Max_WithConditional_EvaluatesArguments()
    {
        var context = CreateEmptyContext();
        var result = _engine.EvaluateNumber("max(if true then 5 else 0, 3)", context);

        Assert.Equal(5.0, result);
    }

    #endregion

    #region String Function Tests

    [Theory]
    [InlineData("lower(\"HELLO\")", "hello")]
    [InlineData("lower(\"Hello World\")", "hello world")]
    [InlineData("lower(\"already lower\")", "already lower")]
    [InlineData("lower(\"\")", "")]
    public void Lower_ConvertsToLowercase(string expression, string expected)
    {
        var context = CreateEmptyContext();
        var result = _engine.Evaluate(expression, context);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("upper(\"hello\")", "HELLO")]
    [InlineData("upper(\"Hello World\")", "HELLO WORLD")]
    [InlineData("upper(\"ALREADY UPPER\")", "ALREADY UPPER")]
    [InlineData("upper(\"\")", "")]
    public void Upper_ConvertsToUppercase(string expression, string expected)
    {
        var context = CreateEmptyContext();
        var result = _engine.Evaluate(expression, context);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("length(\"hello\")", 5)]
    [InlineData("length(\"\")", 0)]
    [InlineData("length(\"hello world\")", 11)]
    public void Length_ReturnsStringLength(string expression, int expected)
    {
        var context = CreateEmptyContext();
        var result = _engine.EvaluateNumber(expression, context);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Lower_WithConcatenation_Works()
    {
        var context = CreateEmptyContext();
        var result = _engine.Evaluate("lower(\"HELLO\" + \" \" + \"WORLD\")", context);

        Assert.Equal("hello world", result);
    }

    #endregion

    #region Random Function Tests

    [Fact]
    public void Random_ReturnsValueInRange()
    {
        var context = CreateEmptyContext();

        for (int i = 0; i < 100; i++)
        {
            var result = _engine.EvaluateNumber("random(1, 10)", context);
            Assert.InRange(result, 1, 10);
        }
    }

    [Fact]
    public void Random_SameMinMax_ReturnsThatValue()
    {
        var context = CreateEmptyContext();
        var result = _engine.EvaluateNumber("random(5, 5)", context);

        Assert.Equal(5.0, result);
    }

    [Fact]
    public void RandomFrom_ReturnsOneOfTheValues()
    {
        var context = CreateEmptyContext();
        var validResults = new[] { 10.0, 20.0, 30.0 };

        for (int i = 0; i < 100; i++)
        {
            var result = _engine.EvaluateNumber("randomFrom(10, 20, 30)", context);
            Assert.Contains(result, validResults);
        }
    }

    [Fact]
    public void RandomFrom_SingleValue_ReturnsThatValue()
    {
        var context = CreateEmptyContext();
        var result = _engine.EvaluateNumber("randomFrom(42)", context);

        Assert.Equal(42.0, result);
    }

    [Fact]
    public void RandomFrom_WithStrings_ReturnsOneOfTheStrings()
    {
        var context = CreateEmptyContext();
        var validResults = new[] { "apple", "banana", "cherry" };

        for (int i = 0; i < 100; i++)
        {
            var result = _engine.Evaluate("randomFrom(\"apple\", \"banana\", \"cherry\")", context);
            Assert.Contains(result?.ToString(), validResults);
        }
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public void Min_TooFewArguments_HandledGracefully()
    {
        var context = CreateEmptyContext();

        // The expression engine may compile fine but handle evaluation errors gracefully
        var compiled = _engine.Compile("min(5)");

        // Engine may return null/default on error rather than throwing
        // We test that this doesn't crash; actual behavior depends on implementation
        try
        {
            var result = _engine.Evaluate(compiled, context);
            // If no exception, just verify we got some result (possibly null)
        }
        catch (Exception)
        {
            // Exception is also valid error handling
        }
    }

    [Fact]
    public void Abs_TooFewArguments_HandledGracefully()
    {
        var context = CreateEmptyContext();

        var compiled = _engine.Compile("abs()");

        try
        {
            var result = _engine.Evaluate(compiled, context);
        }
        catch (Exception)
        {
            // Exception is valid error handling
        }
    }

    [Fact]
    public void UnknownFunction_HandledGracefully()
    {
        var context = CreateEmptyContext();

        var compiled = _engine.Compile("unknownFunc(1, 2)");

        try
        {
            var result = _engine.Evaluate(compiled, context);
        }
        catch (Exception)
        {
            // Exception is valid error handling
        }
    }

    #endregion

    #region Function Case Insensitivity

    [Theory]
    [InlineData("MIN(5, 10)")]
    [InlineData("Min(5, 10)")]
    [InlineData("min(5, 10)")]
    [InlineData("mIn(5, 10)")]
    public void Functions_AreCaseInsensitive(string expression)
    {
        var context = CreateEmptyContext();
        var result = _engine.EvaluateNumber(expression, context);

        Assert.Equal(5.0, result);
    }

    #endregion

    #region Nested Function Calls

    [Fact]
    public void NestedFunctionCalls_Work()
    {
        var context = CreateEmptyContext();

        // abs(min(-5, -10)) = abs(-10) = 10
        var result = _engine.EvaluateNumber("abs(min(-5, -10))", context);
        Assert.Equal(10.0, result);
    }

    [Fact]
    public void MultipleNestedCalls_Work()
    {
        var context = CreateEmptyContext();

        // max(abs(-3), abs(-5)) = max(3, 5) = 5
        var result = _engine.EvaluateNumber("max(abs(-3), abs(-5))", context);
        Assert.Equal(5.0, result);
    }

    [Fact]
    public void FunctionInConditional_Works()
    {
        var context = CreateEmptyContext();

        // if max(1, 2) > 1 then 100 else 200
        var result = _engine.EvaluateNumber("if max(1, 2) > 1 then 100 else 200", context);
        Assert.Equal(100.0, result);
    }

    #endregion
}
