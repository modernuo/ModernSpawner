using System;
using System.Collections.Generic;

namespace Server.Engines.ModernSpawner.Scripting.Expressions;

/// <summary>
/// Recursive descent parser for expressions.
/// Operator precedence (lowest to highest):
///   1. if-then-else
///   2. or
///   3. and
///   4. comparison (==, !=, <, >, <=, >=)
///   5. addition (+, -)
///   6. multiplication (*, /)
///   7. unary (not, -)
///   8. call/property access
///   9. primary (literals, identifiers, parentheses)
/// </summary>
public class ExpressionParser
{
    private readonly List<Token> _tokens;
    private int _current;

    public List<string> Errors { get; } = [];

    public ExpressionParser(List<Token> tokens)
    {
        _tokens = tokens;
    }

    public ExpressionNode Parse()
    {
        try
        {
            var expr = ParseExpression();

            if (!IsAtEnd())
            {
                Errors.Add($"Unexpected token at position {Current().Position}: '{Current().Lexeme}'");
            }

            return expr;
        }
        catch (ParseException ex)
        {
            Errors.Add(ex.Message);
            return null;
        }
    }

    private ExpressionNode ParseExpression()
    {
        return ParseConditional();
    }

    /// <summary>
    /// if condition then value else value
    /// </summary>
    private ExpressionNode ParseConditional()
    {
        if (Match(TokenType.If))
        {
            var condition = ParseOr();
            Consume(TokenType.Then, "Expected 'then' after if condition");
            var thenBranch = ParseOr();
            Consume(TokenType.Else, "Expected 'else' after then branch");
            var elseBranch = ParseConditional(); // Right-associative
            return new ConditionalExpression(condition, thenBranch, elseBranch);
        }

        return ParseOr();
    }

    /// <summary>
    /// expr or expr
    /// </summary>
    private ExpressionNode ParseOr()
    {
        var left = ParseAnd();

        while (Match(TokenType.Or))
        {
            var right = ParseAnd();
            left = new BinaryExpression(left, TokenType.Or, right);
        }

        return left;
    }

    /// <summary>
    /// expr and expr
    /// </summary>
    private ExpressionNode ParseAnd()
    {
        var left = ParseComparison();

        while (Match(TokenType.And))
        {
            var right = ParseComparison();
            left = new BinaryExpression(left, TokenType.And, right);
        }

        return left;
    }

    /// <summary>
    /// expr (==|!=|<|>|<=|>=) expr
    /// </summary>
    private ExpressionNode ParseComparison()
    {
        var left = ParseAddition();

        while (MatchAny(TokenType.Equal, TokenType.NotEqual,
                        TokenType.Less, TokenType.LessEqual,
                        TokenType.Greater, TokenType.GreaterEqual))
        {
            var op = Previous().Type;
            var right = ParseAddition();
            left = new BinaryExpression(left, op, right);
        }

        return left;
    }

    /// <summary>
    /// expr (+|-) expr
    /// </summary>
    private ExpressionNode ParseAddition()
    {
        var left = ParseMultiplication();

        while (MatchAny(TokenType.Plus, TokenType.Minus))
        {
            var op = Previous().Type;
            var right = ParseMultiplication();
            left = new BinaryExpression(left, op, right);
        }

        return left;
    }

    /// <summary>
    /// expr (*|/) expr
    /// </summary>
    private ExpressionNode ParseMultiplication()
    {
        var left = ParseUnary();

        while (MatchAny(TokenType.Star, TokenType.Slash))
        {
            var op = Previous().Type;
            var right = ParseUnary();
            left = new BinaryExpression(left, op, right);
        }

        return left;
    }

    /// <summary>
    /// (not|-) expr
    /// </summary>
    private ExpressionNode ParseUnary()
    {
        if (Match(TokenType.Not))
        {
            var operand = ParseUnary();
            return new UnaryExpression(TokenType.Not, operand);
        }

        if (Match(TokenType.Minus))
        {
            var operand = ParseUnary();
            return new UnaryExpression(TokenType.Minus, operand);
        }

        return ParseCall();
    }

    /// <summary>
    /// primary ((...) | .property)*
    /// </summary>
    private ExpressionNode ParseCall()
    {
        var expr = ParsePrimary();

        while (true)
        {
            if (Match(TokenType.LeftParen))
            {
                // Function call
                if (expr is not PropertyAccessExpression propExpr)
                {
                    throw Error("Can only call functions");
                }

                var funcName = propExpr.ObjectName;
                if (propExpr.PropertyPath.Count > 0)
                {
                    funcName = string.Join(".", propExpr.PropertyPath);
                }

                var args = ParseArguments();
                Consume(TokenType.RightParen, "Expected ')' after function arguments");
                expr = new FunctionCallExpression(funcName, args);
            }
            else if (Match(TokenType.Dot))
            {
                // Property access continuation
                var name = Consume(TokenType.Identifier, "Expected property name after '.'");
                if (expr is PropertyAccessExpression propAccess)
                {
                    propAccess.PropertyPath.Add(name.Lexeme);
                }
                else
                {
                    // Can't access properties on non-property expressions
                    throw Error($"Cannot access property '{name.Lexeme}' on expression");
                }
            }
            else
            {
                break;
            }
        }

        return expr;
    }

    private List<ExpressionNode> ParseArguments()
    {
        var args = new List<ExpressionNode>();

        if (!Check(TokenType.RightParen))
        {
            do
            {
                args.Add(ParseExpression());
            } while (Match(TokenType.Comma));
        }

        return args;
    }

    /// <summary>
    /// Parses primary expressions: literals, identifiers, grouped expressions.
    /// </summary>
    private ExpressionNode ParsePrimary()
    {
        // Boolean literals - use optimized typed expression
        if (Match(TokenType.True))
        {
            return new BooleanLiteralExpression(true);
        }

        if (Match(TokenType.False))
        {
            return new BooleanLiteralExpression(false);
        }

        // Number literal - use factory to get optimized typed expression
        if (Match(TokenType.Number))
        {
            return LiteralExpression.Create(Previous().Value);
        }

        // String literal - use optimized typed expression for consistency
        if (Match(TokenType.String))
        {
            return new StringLiteralExpression(Previous().Value as string);
        }

        // Identifier (variable, property access, or function name)
        if (Match(TokenType.Identifier))
        {
            var name = Previous().Lexeme;
            var path = new List<string>();

            // Check for property path (identifier.property.property...)
            while (Match(TokenType.Dot))
            {
                var prop = Consume(TokenType.Identifier, "Expected property name after '.'");
                path.Add(prop.Lexeme);
            }

            // Check if this is a function call
            if (Check(TokenType.LeftParen) && path.Count == 0)
            {
                // It's a function call, return as property access and let ParseCall handle it
                return new PropertyAccessExpression(name, path);
            }

            return new PropertyAccessExpression(name, path);
        }

        // Grouped expression
        if (Match(TokenType.LeftParen))
        {
            var expr = ParseExpression();
            Consume(TokenType.RightParen, "Expected ')' after expression");
            return new GroupExpression(expr);
        }

        throw Error($"Expected expression, got '{Current().Lexeme}'");
    }

    // Helper methods

    private bool Match(TokenType type)
    {
        if (Check(type))
        {
            Advance();
            return true;
        }
        return false;
    }

    private bool MatchAny(params TokenType[] types)
    {
        foreach (var type in types)
        {
            if (Check(type))
            {
                Advance();
                return true;
            }
        }
        return false;
    }

    private bool Check(TokenType type)
    {
        if (IsAtEnd()) return false;
        return Current().Type == type;
    }

    private Token Advance()
    {
        if (!IsAtEnd()) _current++;
        return Previous();
    }

    private Token Consume(TokenType type, string message)
    {
        if (Check(type)) return Advance();
        throw Error(message);
    }

    private Token Current() => _tokens[_current];
    private Token Previous() => _tokens[_current - 1];
    private bool IsAtEnd() => Current().Type == TokenType.EndOfInput;

    private ParseException Error(string message)
    {
        var token = Current();
        var fullMessage = $"[Position {token.Position}] {message}";
        Errors.Add(fullMessage);
        return new ParseException(fullMessage);
    }

    private class ParseException : Exception
    {
        public ParseException(string message) : base(message) { }
    }
}
