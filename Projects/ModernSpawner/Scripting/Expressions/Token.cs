namespace Server.Engines.ModernSpawner.Scripting.Expressions;

/// <summary>
/// Token types for the expression lexer.
/// </summary>
public enum TokenType
{
    // Literals
    Number,
    String,
    Boolean,
    Identifier,

    // Operators
    Plus,           // +
    Minus,          // -
    Star,           // *
    Slash,          // /
    Less,           // <
    LessEqual,      // <=
    Greater,        // >
    GreaterEqual,   // >=
    Equal,          // ==
    NotEqual,       // !=

    // Keywords (word-based boolean operators)
    And,
    Or,
    Not,
    If,
    Then,
    Else,
    True,
    False,

    // Delimiters
    LeftParen,      // (
    RightParen,     // )
    Comma,          // ,
    Dot,            // .

    // End of input
    EndOfInput,

    // Error
    Error
}

/// <summary>
/// Represents a token from the expression lexer.
/// </summary>
public readonly struct Token
{
    public TokenType Type { get; }
    public string Lexeme { get; }
    public object Value { get; }
    public int Position { get; }

    public Token(TokenType type, string lexeme, object value, int position)
    {
        Type = type;
        Lexeme = lexeme;
        Value = value;
        Position = position;
    }

    public override string ToString() => $"{Type}({Lexeme})";
}
