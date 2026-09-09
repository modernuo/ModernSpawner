using Server.Engines.ModernSpawner.Scripting;
using Server.Engines.ModernSpawner.Scripting.Expressions;
using Xunit;

namespace Server.Engines.ModernSpawner.Tests.Expressions;

public class ExpressionParserTests
{
    private static ExpressionNode Parse(string input)
    {
        var lexer = new ExpressionLexer(input);
        var tokens = lexer.Tokenize();
        Assert.Empty(lexer.Errors);

        var parser = new ExpressionParser(tokens);
        var node = parser.Parse();
        Assert.Empty(parser.Errors);

        return node;
    }

    [Fact]
    public void Parse_NumberLiteral_ReturnsDoubleLiteralExpression()
    {
        var node = Parse("42");

        // Numbers are now optimized to DoubleLiteralExpression to avoid boxing
        Assert.IsType<DoubleLiteralExpression>(node);
        var context = new ScriptContext(null);
        Assert.Equal(42.0, node.EvaluateNumber(context));
    }

    [Fact]
    public void Parse_StringLiteral_ReturnsStringLiteralExpression()
    {
        var node = Parse("\"hello world\"");

        // Strings are now optimized to StringLiteralExpression for consistency
        Assert.IsType<StringLiteralExpression>(node);
        var context = new ScriptContext(null);
        Assert.Equal("hello world", node.EvaluateString(context));
    }

    [Fact]
    public void Parse_BooleanLiteral_ReturnsBooleanLiteralExpression()
    {
        var nodeTrue = Parse("true");
        var nodeFalse = Parse("false");

        // Booleans are now optimized to BooleanLiteralExpression to avoid boxing
        Assert.IsType<BooleanLiteralExpression>(nodeTrue);
        Assert.IsType<BooleanLiteralExpression>(nodeFalse);
        var context = new ScriptContext(null);
        Assert.True(nodeTrue.EvaluateBoolean(context));
        Assert.False(nodeFalse.EvaluateBoolean(context));
    }

    [Fact]
    public void Parse_PropertyAccess_ReturnsPropertyAccessExpression()
    {
        var node = Parse("trigMob.Karma");

        Assert.IsType<PropertyAccessExpression>(node);
        var prop = (PropertyAccessExpression)node;
        Assert.Equal("trigMob", prop.ObjectName);
        Assert.Single(prop.PropertyPath);
        Assert.Equal("Karma", prop.PropertyPath[0]);
    }

    [Fact]
    public void Parse_NestedPropertyAccess_ReturnsPropertyAccessExpression()
    {
        var node = Parse("target.Backpack.MaxItems");

        Assert.IsType<PropertyAccessExpression>(node);
        var prop = (PropertyAccessExpression)node;
        Assert.Equal("target", prop.ObjectName);
        Assert.Equal(2, prop.PropertyPath.Count);
        Assert.Equal("Backpack", prop.PropertyPath[0]);
        Assert.Equal("MaxItems", prop.PropertyPath[1]);
    }

    [Fact]
    public void Parse_BinaryAddition_ReturnsBinaryExpression()
    {
        var node = Parse("10 + 5");

        Assert.IsType<BinaryExpression>(node);
        var binary = (BinaryExpression)node;
        Assert.Equal(TokenType.Plus, binary.Operator);
        // Numbers are now optimized to DoubleLiteralExpression
        Assert.IsType<DoubleLiteralExpression>(binary.Left);
        Assert.IsType<DoubleLiteralExpression>(binary.Right);
    }

    [Fact]
    public void Parse_BinaryComparison_ReturnsBinaryExpression()
    {
        var node = Parse("x < 100");

        Assert.IsType<BinaryExpression>(node);
        var binary = (BinaryExpression)node;
        Assert.Equal(TokenType.Less, binary.Operator);
    }

    [Fact]
    public void Parse_BooleanAnd_ReturnsBinaryExpression()
    {
        var node = Parse("a and b");

        Assert.IsType<BinaryExpression>(node);
        var binary = (BinaryExpression)node;
        Assert.Equal(TokenType.And, binary.Operator);
    }

    [Fact]
    public void Parse_BooleanOr_ReturnsBinaryExpression()
    {
        var node = Parse("a or b");

        Assert.IsType<BinaryExpression>(node);
        var binary = (BinaryExpression)node;
        Assert.Equal(TokenType.Or, binary.Operator);
    }

    [Fact]
    public void Parse_UnaryNot_ReturnsUnaryExpression()
    {
        var node = Parse("not active");

        Assert.IsType<UnaryExpression>(node);
        var unary = (UnaryExpression)node;
        Assert.Equal(TokenType.Not, unary.Operator);
    }

    [Fact]
    public void Parse_UnaryNegation_ReturnsUnaryExpression()
    {
        var node = Parse("-42");

        Assert.IsType<UnaryExpression>(node);
        var unary = (UnaryExpression)node;
        Assert.Equal(TokenType.Minus, unary.Operator);
    }

    [Fact]
    public void Parse_FunctionCall_ReturnsFunctionCallExpression()
    {
        var node = Parse("random(50, 100)");

        Assert.IsType<FunctionCallExpression>(node);
        var func = (FunctionCallExpression)node;
        Assert.Equal("random", func.FunctionName);
        Assert.Equal(2, func.Arguments.Count);
    }

    [Fact]
    public void Parse_FunctionCallNoArgs_ReturnsFunctionCallExpression()
    {
        var node = Parse("isNight()");

        Assert.IsType<FunctionCallExpression>(node);
        var func = (FunctionCallExpression)node;
        Assert.Equal("isNight", func.FunctionName);
        Assert.Empty(func.Arguments);
    }

    [Fact]
    public void Parse_Conditional_ReturnsConditionalExpression()
    {
        var node = Parse("if true then 1 else 0");

        Assert.IsType<ConditionalExpression>(node);
        var cond = (ConditionalExpression)node;
        // Booleans are now optimized to BooleanLiteralExpression
        Assert.IsType<BooleanLiteralExpression>(cond.Condition);
        // Numbers are now optimized to DoubleLiteralExpression
        Assert.IsType<DoubleLiteralExpression>(cond.ThenBranch);
        Assert.IsType<DoubleLiteralExpression>(cond.ElseBranch);
    }

    [Fact]
    public void Parse_GroupedExpression_ReturnsGroupExpression()
    {
        var node = Parse("(1 + 2) * 3");

        Assert.IsType<BinaryExpression>(node);
        var binary = (BinaryExpression)node;
        Assert.Equal(TokenType.Star, binary.Operator);
        Assert.IsType<GroupExpression>(binary.Left);
    }

    [Fact]
    public void Parse_ComplexExpression_ReturnsCorrectStructure()
    {
        var node = Parse("trigMob.Karma < 0 and trigMob.Fame > 5000");

        Assert.IsType<BinaryExpression>(node);
        var andExpr = (BinaryExpression)node;
        Assert.Equal(TokenType.And, andExpr.Operator);

        Assert.IsType<BinaryExpression>(andExpr.Left);
        var leftComparison = (BinaryExpression)andExpr.Left;
        Assert.Equal(TokenType.Less, leftComparison.Operator);

        Assert.IsType<BinaryExpression>(andExpr.Right);
        var rightComparison = (BinaryExpression)andExpr.Right;
        Assert.Equal(TokenType.Greater, rightComparison.Operator);
    }

    [Fact]
    public void Parse_OperatorPrecedence_MultiplicationBeforeAddition()
    {
        // 1 + 2 * 3 should be parsed as 1 + (2 * 3)
        var node = Parse("1 + 2 * 3");

        Assert.IsType<BinaryExpression>(node);
        var addExpr = (BinaryExpression)node;
        Assert.Equal(TokenType.Plus, addExpr.Operator);

        // Numbers are now optimized to DoubleLiteralExpression
        Assert.IsType<DoubleLiteralExpression>(addExpr.Left);
        Assert.IsType<BinaryExpression>(addExpr.Right);
        var multExpr = (BinaryExpression)addExpr.Right;
        Assert.Equal(TokenType.Star, multExpr.Operator);
    }

    [Fact]
    public void Parse_OperatorPrecedence_ComparisonBeforeAnd()
    {
        // a < b and c > d should be parsed as (a < b) and (c > d)
        var node = Parse("a < b and c > d");

        Assert.IsType<BinaryExpression>(node);
        var andExpr = (BinaryExpression)node;
        Assert.Equal(TokenType.And, andExpr.Operator);

        Assert.IsType<BinaryExpression>(andExpr.Left);
        Assert.IsType<BinaryExpression>(andExpr.Right);
    }

    [Fact]
    public void Parse_OperatorPrecedence_AndBeforeOr()
    {
        // a or b and c should be parsed as a or (b and c)
        var node = Parse("a or b and c");

        Assert.IsType<BinaryExpression>(node);
        var orExpr = (BinaryExpression)node;
        Assert.Equal(TokenType.Or, orExpr.Operator);

        Assert.IsType<PropertyAccessExpression>(orExpr.Left);
        Assert.IsType<BinaryExpression>(orExpr.Right);
        var andExpr = (BinaryExpression)orExpr.Right;
        Assert.Equal(TokenType.And, andExpr.Operator);
    }
}
