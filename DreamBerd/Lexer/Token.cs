// Token.cs
namespace DreamberdInterpreter
{
    public enum TokenType
    {
        EndOfFile,
        Identifier,
        Number,
        String,

        Const,
        Var,
        Reverse,
        Forward,
        Delete,
        When,
        If,
        Else,
        Idk,
        Class,
        Is,
        A,

        Return,
        While,
        Break,
        Continue,

        LeftParen,
        RightParen,
        LeftBracket,
        RightBracket,
        LeftBrace,
        RightBrace,
        Comma,
        Semicolon,
        Colon,
        Percent,
        Ampersand,
        Pipe,
        Caret,
        DoublePipe,

        Bang,
        Question,          // statement debug terminator '?'
        QuestionOp,        // conditional operator '?'

        Plus,
        PlusPlus,
        Minus,
        MinusMinus,
        Star,
        StarRun,
        Slash,
        Tilde,

        Root,

        Less,
        LessEqual,
        MinOp,            // '<>'
        ShiftLeft,
        Greater,
        GreaterEqual,
        ShiftRight,
        MaxOp,            // '><'
        MinUnicode,       // ⌊⌋
        MaxUnicode,       // ⌈⌉
        ClampSymbol,      // ▷
        WrapSymbol,       // ↻
        ClampKeyword,
        WrapKeyword,
        RangeDots,
        At,

        Arrow,              // '=>'
        Assign,             // '='
        Equal,              // '=='
        EqualEqual,         // '==='
        EqualEqualEqual     // '===='
    }

    public sealed class Token
    {
        public TokenType Type
        {
            get;
        }
        public string Lexeme
        {
            get;
        }
        public object? Literal
        {
            get;
        }
        public int Position
        {
            get;
        }

        public Token(TokenType type, string lexeme, object? literal, int position)
        {
            Type = type;
            Lexeme = lexeme;
            Literal = literal;
            Position = position;
        }

        public override string ToString() => $"{Type} '{Lexeme}'";
    }
}
