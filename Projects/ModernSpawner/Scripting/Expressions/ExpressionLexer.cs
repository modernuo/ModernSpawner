using System.Collections.Generic;
using System.Globalization;

namespace Server.Engines.ModernSpawner.Scripting.Expressions;

/// <summary>
/// Tokenizes expression strings into a sequence of tokens.
/// </summary>
public class ExpressionLexer
{
    private readonly string _source;
    private int _position;
    private int _start;

    public List<string> Errors { get; } = [];

    public ExpressionLexer(string source)
    {
        _source = source ?? string.Empty;
    }

    public List<Token> Tokenize()
    {
        var tokens = new List<Token>();
        _position = 0;

        while (!IsAtEnd())
        {
            _start = _position;
            var token = ScanToken();

            // Don't add duplicate EndOfInput tokens
            if (token.Type == TokenType.EndOfInput)
            {
                break;
            }

            if (token.Type != TokenType.Error || Errors.Count == 0)
            {
                tokens.Add(token);
            }
        }

        tokens.Add(new Token(TokenType.EndOfInput, string.Empty, null, _position));
        return tokens;
    }

    private Token ScanToken()
    {
        SkipWhitespace();

        if (IsAtEnd())
        {
            return new Token(TokenType.EndOfInput, string.Empty, null, _position);
        }

        _start = _position;
        var c = Advance();

        // Single-character tokens
        switch (c)
        {
            case '(':
                {
                    return MakeToken(TokenType.LeftParen);
                }
            case ')':
                {
                    return MakeToken(TokenType.RightParen);
                }
            case ',':
                {
                    return MakeToken(TokenType.Comma);
                }
            case '.':
                {
                    return MakeToken(TokenType.Dot);
                }
            case '+':
                {
                    return MakeToken(TokenType.Plus);
                }
            case '-':
                {
                    return MakeToken(TokenType.Minus);
                }
            case '*':
                {
                    return MakeToken(TokenType.Star);
                }
            case '/':
                {
                    return MakeToken(TokenType.Slash);
                }
        }

        // Two-character operators
        if (c == '<')
        {
            return Match('=')
                ? MakeToken(TokenType.LessEqual)
                : MakeToken(TokenType.Less);
        }

        if (c == '>')
        {
            return Match('=')
                ? MakeToken(TokenType.GreaterEqual)
                : MakeToken(TokenType.Greater);
        }

        if (c == '=')
        {
            if (Match('='))
            {
                return MakeToken(TokenType.Equal);
            }
            return ErrorToken("Expected '==' for equality comparison");
        }

        if (c == '!')
        {
            if (Match('='))
            {
                return MakeToken(TokenType.NotEqual);
            }
            return ErrorToken("Expected '!=' for inequality comparison");
        }

        // String literals
        if (c is '"' or '\'')
        {
            return ScanString(c);
        }

        // Numbers (including hex 0x...)
        if (char.IsDigit(c))
        {
            // Check for hex number (0x...)
            if (c == '0' && (Peek() == 'x' || Peek() == 'X'))
            {
                Advance(); // consume 'x'
                return ScanHexNumber();
            }
            return ScanNumber();
        }

        // Identifiers and keywords
        if (IsIdentifierStart(c))
        {
            return ScanIdentifier();
        }

        return ErrorToken($"Unexpected character: '{c}'");
    }

    private Token ScanString(char quote)
    {
        while (!IsAtEnd() && Peek() != quote)
        {
            if (Peek() == '\\')
            {
                Advance(); // skip escape character
            }
            Advance();
        }

        if (IsAtEnd())
        {
            return ErrorToken("Unterminated string");
        }

        Advance(); // closing quote

        // Extract string value without quotes
        var value = _source[(_start + 1)..(_position - 1)];
        // Handle escape sequences
        value = value.Replace("\\n", "\n")
                     .Replace("\\t", "\t")
                     .Replace("\\\"", "\"")
                     .Replace("\\'", "'")
                     .Replace("\\\\", "\\");

        return new Token(TokenType.String, _source[_start.._position], value, _start);
    }

    private Token ScanNumber()
    {
        while (char.IsDigit(Peek()))
        {
            Advance();
        }

        // Check for decimal
        if (Peek() == '.' && char.IsDigit(PeekNext()))
        {
            Advance(); // consume '.'
            while (char.IsDigit(Peek()))
            {
                Advance();
            }
        }

        var lexeme = _source[_start.._position];
        if (double.TryParse(lexeme, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return new Token(TokenType.Number, lexeme, value, _start);
        }

        return ErrorToken($"Invalid number: {lexeme}");
    }

    private Token ScanHexNumber()
    {
        var hexStart = _position;
        while (IsHexDigit(Peek()))
        {
            Advance();
        }

        if (_position == hexStart)
        {
            return ErrorToken("Expected hex digits after '0x'");
        }

        var lexeme = _source[_start.._position];
        var hexPart = _source[hexStart.._position];

        if (int.TryParse(hexPart, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            return new Token(TokenType.Number, lexeme, (double)value, _start);
        }

        return ErrorToken($"Invalid hex number: {lexeme}");
    }

    private Token ScanIdentifier()
    {
        while (IsIdentifierChar(Peek()))
        {
            Advance();
        }

        var lexeme = _source[_start.._position];
        var type = GetKeywordType(lexeme);
        object value = null;

        if (type == TokenType.True)
        {
            value = true;
        }
        else if (type == TokenType.False)
        {
            value = false;
        }

        return new Token(type, lexeme, value, _start);
    }

    private static TokenType GetKeywordType(string lexeme)
    {
        return lexeme.ToLowerInvariant() switch
        {
            "and" => TokenType.And,
            "or" => TokenType.Or,
            "not" => TokenType.Not,
            "if" => TokenType.If,
            "then" => TokenType.Then,
            "else" => TokenType.Else,
            "true" => TokenType.True,
            "false" => TokenType.False,
            _ => TokenType.Identifier
        };
    }

    private void SkipWhitespace()
    {
        while (!IsAtEnd() && char.IsWhiteSpace(Peek()))
        {
            Advance();
        }
    }

    private char Advance() => _source[_position++];
    private char Peek() => IsAtEnd() ? '\0' : _source[_position];
    private char PeekNext() => _position + 1 >= _source.Length ? '\0' : _source[_position + 1];
    private bool IsAtEnd() => _position >= _source.Length;

    private bool Match(char expected)
    {
        if (IsAtEnd() || _source[_position] != expected)
        {
            return false;
        }
        _position++;
        return true;
    }

    private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';
    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';
    private static bool IsHexDigit(char c) =>
        char.IsDigit(c) || c is >= 'a' and <= 'f' || c is >= 'A' and <= 'F';

    private Token MakeToken(TokenType type)
    {
        return new Token(type, _source[_start.._position], null, _start);
    }

    private Token ErrorToken(string message)
    {
        Errors.Add($"[Position {_start}] {message}");
        return new Token(TokenType.Error, _source[_start.._position], null, _start);
    }
}
