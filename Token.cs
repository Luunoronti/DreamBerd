// Token.cs
using System;

namespace DreamberdInterpreter
{
    public enum TokenKind
    {
        EndOfFile,

        Identifier,
        Number,
        String,

        Const,
        Var,
        True,
        False,
        Maybe,
        Undefined,
        Delete,
        Reverse,
        Forward,
        When,

        LeftParen,
        RightParen,
        LeftBracket,
        RightBracket,
        Comma,

        Plus,
        Minus,
        Star,
        Slash,

        Equal,
        DoubleEqual,
        TripleEqual,
        QuadEqual,

        Less,
        Greater,
        LessEqual,
        GreaterEqual,

        Exclamation,
        InvertedExclamation,
        Question,

        LeftBrace,
        RightBrace
    }

    public readonly struct Token
    {
        public TokenKind Kind
        {
            get;
        }
        public string Lexeme
        {
            get;
        }
        public int Line
        {
            get;
        }
        public int Column
        {
            get;
        }

        public Token(TokenKind kind, string lexeme, int line, int column)
        {
            Kind = kind;
            Lexeme = lexeme;
            Line = line;
            Column = column;
        }

        public override string ToString() => $"{Kind} '{Lexeme}' ({Line}:{Column})";
    }

}
