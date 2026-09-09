using Server.Engines.ModernSpawner.Scripting.Expressions;
using Xunit;

namespace Server.Engines.ModernSpawner.Tests.Expressions;

public class ExpressionLexerTests
{
    [Theory]
    [InlineData("42", TokenType.Number, 42.0)]
    [InlineData("3.14", TokenType.Number, 3.14)]
    [InlineData("0x8000", TokenType.Number, 32768.0)]
    [InlineData("0xFF", TokenType.Number, 255.0)]
    public void Tokenize_Numbers_ReturnsCorrectTokens(string input, TokenType expectedType, double expectedValue)
    {
        var lexer = new ExpressionLexer(input);
        var tokens = lexer.Tokenize();

        Assert.Empty(lexer.Errors);
        Assert.Equal(2, tokens.Count); // Token + EndOfInput
        Assert.Equal(expectedType, tokens[0].Type);
        Assert.Equal(expectedValue, tokens[0].Value);
    }

    [Theory]
    [InlineData("\"hello\"", TokenType.String, "hello")]
    [InlineData("'world'", TokenType.String, "world")]
    [InlineData("\"test\\nline\"", TokenType.String, "test\nline")]
    public void Tokenize_Strings_ReturnsCorrectTokens(string input, TokenType expectedType, string expectedValue)
    {
        var lexer = new ExpressionLexer(input);
        var tokens = lexer.Tokenize();

        Assert.Empty(lexer.Errors);
        Assert.Equal(2, tokens.Count);
        Assert.Equal(expectedType, tokens[0].Type);
        Assert.Equal(expectedValue, tokens[0].Value);
    }

    [Theory]
    [InlineData("true", TokenType.True, true)]
    [InlineData("false", TokenType.False, false)]
    [InlineData("TRUE", TokenType.True, true)]
    [InlineData("False", TokenType.False, false)]
    public void Tokenize_Booleans_ReturnsCorrectTokens(string input, TokenType expectedType, bool expectedValue)
    {
        var lexer = new ExpressionLexer(input);
        var tokens = lexer.Tokenize();

        Assert.Empty(lexer.Errors);
        Assert.Equal(2, tokens.Count);
        Assert.Equal(expectedType, tokens[0].Type);
        Assert.Equal(expectedValue, tokens[0].Value);
    }

    [Theory]
    [InlineData("and", TokenType.And)]
    [InlineData("or", TokenType.Or)]
    [InlineData("not", TokenType.Not)]
    [InlineData("if", TokenType.If)]
    [InlineData("then", TokenType.Then)]
    [InlineData("else", TokenType.Else)]
    [InlineData("AND", TokenType.And)]
    [InlineData("OR", TokenType.Or)]
    public void Tokenize_Keywords_ReturnsCorrectTokens(string input, TokenType expectedType)
    {
        var lexer = new ExpressionLexer(input);
        var tokens = lexer.Tokenize();

        Assert.Empty(lexer.Errors);
        Assert.Equal(2, tokens.Count);
        Assert.Equal(expectedType, tokens[0].Type);
    }

    [Theory]
    [InlineData("identifier", TokenType.Identifier)]
    [InlineData("trigMob", TokenType.Identifier)]
    [InlineData("_private", TokenType.Identifier)]
    [InlineData("my_var123", TokenType.Identifier)]
    public void Tokenize_Identifiers_ReturnsCorrectTokens(string input, TokenType expectedType)
    {
        var lexer = new ExpressionLexer(input);
        var tokens = lexer.Tokenize();

        Assert.Empty(lexer.Errors);
        Assert.Equal(2, tokens.Count);
        Assert.Equal(expectedType, tokens[0].Type);
        Assert.Equal(input, tokens[0].Lexeme);
    }

    [Theory]
    [InlineData("+", TokenType.Plus)]
    [InlineData("-", TokenType.Minus)]
    [InlineData("*", TokenType.Star)]
    [InlineData("/", TokenType.Slash)]
    [InlineData("<", TokenType.Less)]
    [InlineData("<=", TokenType.LessEqual)]
    [InlineData(">", TokenType.Greater)]
    [InlineData(">=", TokenType.GreaterEqual)]
    [InlineData("==", TokenType.Equal)]
    [InlineData("!=", TokenType.NotEqual)]
    public void Tokenize_Operators_ReturnsCorrectTokens(string input, TokenType expectedType)
    {
        var lexer = new ExpressionLexer(input);
        var tokens = lexer.Tokenize();

        Assert.Empty(lexer.Errors);
        Assert.Equal(2, tokens.Count);
        Assert.Equal(expectedType, tokens[0].Type);
    }

    [Theory]
    [InlineData("(", TokenType.LeftParen)]
    [InlineData(")", TokenType.RightParen)]
    [InlineData(",", TokenType.Comma)]
    [InlineData(".", TokenType.Dot)]
    public void Tokenize_Delimiters_ReturnsCorrectTokens(string input, TokenType expectedType)
    {
        var lexer = new ExpressionLexer(input);
        var tokens = lexer.Tokenize();

        Assert.Empty(lexer.Errors);
        Assert.Equal(2, tokens.Count);
        Assert.Equal(expectedType, tokens[0].Type);
    }

    [Fact]
    public void Tokenize_ComplexExpression_ReturnsCorrectTokens()
    {
        var input = "trigMob.Karma < 0 and trigMob.Fame > 5000";
        var lexer = new ExpressionLexer(input);
        var tokens = lexer.Tokenize();

        Assert.Empty(lexer.Errors);

        var expectedTypes = new[]
        {
            TokenType.Identifier, // trigMob
            TokenType.Dot,        // .
            TokenType.Identifier, // Karma
            TokenType.Less,       // <
            TokenType.Number,     // 0
            TokenType.And,        // and
            TokenType.Identifier, // trigMob
            TokenType.Dot,        // .
            TokenType.Identifier, // Fame
            TokenType.Greater,    // >
            TokenType.Number,     // 5000
            TokenType.EndOfInput
        };

        Assert.Equal(expectedTypes.Length, tokens.Count);
        for (var i = 0; i < expectedTypes.Length; i++)
        {
            Assert.Equal(expectedTypes[i], tokens[i].Type);
        }
    }

    [Fact]
    public void Tokenize_FunctionCall_ReturnsCorrectTokens()
    {
        var input = "random(50, 100)";
        var lexer = new ExpressionLexer(input);
        var tokens = lexer.Tokenize();

        Assert.Empty(lexer.Errors);

        var expectedTypes = new[]
        {
            TokenType.Identifier,  // random
            TokenType.LeftParen,   // (
            TokenType.Number,      // 50
            TokenType.Comma,       // ,
            TokenType.Number,      // 100
            TokenType.RightParen,  // )
            TokenType.EndOfInput
        };

        Assert.Equal(expectedTypes.Length, tokens.Count);
        for (var i = 0; i < expectedTypes.Length; i++)
        {
            Assert.Equal(expectedTypes[i], tokens[i].Type);
        }
    }

    [Fact]
    public void Tokenize_SkipsWhitespace()
    {
        var input = "   42   +   3   ";
        var lexer = new ExpressionLexer(input);
        var tokens = lexer.Tokenize();

        Assert.Empty(lexer.Errors);
        Assert.Equal(4, tokens.Count); // 42, +, 3, EndOfInput
    }

    [Fact]
    public void Tokenize_UnterminatedString_ReturnsError()
    {
        var input = "\"unterminated";
        var lexer = new ExpressionLexer(input);
        lexer.Tokenize();

        Assert.NotEmpty(lexer.Errors);
        Assert.Contains("Unterminated string", lexer.Errors[0]);
    }
}
