// Lexer.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DreamberdInterpreter
{
    public sealed class Lexer
    {
        private readonly string _source;
        private readonly int _length;
        private int _position;
        private int _line;
        private int _column;

        public Lexer(string source)
        {
            _source = source ?? string.Empty;
            _length = _source.Length;
            _position = 0;
            _line = 1;
            _column = 1;
        }

        public List<Token> Tokenize()
        {
            var tokens = new List<Token>();

            while (!IsAtEnd())
            {
                SkipWhitespaceAndComments();

                if (IsAtEnd())
                    break;

                int tokenLine = _line;
                int tokenColumn = _column;
                char c = Peek();

                switch (c)
                {
                    case '(':
                        Advance();
                        tokens.Add(new Token(TokenKind.LeftParen, "(", tokenLine, tokenColumn));
                        break;
                    case ')':
                        Advance();
                        tokens.Add(new Token(TokenKind.RightParen, ")", tokenLine, tokenColumn));
                        break;
                    case '[':
                        Advance();
                        tokens.Add(new Token(TokenKind.LeftBracket, "[", tokenLine, tokenColumn));
                        break;
                    case ']':
                        Advance();
                        tokens.Add(new Token(TokenKind.RightBracket, "]", tokenLine, tokenColumn));
                        break;
                    case '{':
                        Advance();
                        tokens.Add(new Token(TokenKind.LeftBrace, "{", tokenLine, tokenColumn));
                        break;
                    case '}':
                        Advance();
                        tokens.Add(new Token(TokenKind.RightBrace, "}", tokenLine, tokenColumn));
                        break;
                    case ',':
                        Advance();
                        tokens.Add(new Token(TokenKind.Comma, ",", tokenLine, tokenColumn));
                        break;
                    case '+':
                        Advance();
                        tokens.Add(new Token(TokenKind.Plus, "+", tokenLine, tokenColumn));
                        break;
                    case '-':
                        Advance();
                        tokens.Add(new Token(TokenKind.Minus, "-", tokenLine, tokenColumn));
                        break;
                    case '*':
                        Advance();
                        tokens.Add(new Token(TokenKind.Star, "*", tokenLine, tokenColumn));
                        break;
                    case '/':
                        Advance();
                        tokens.Add(new Token(TokenKind.Slash, "/", tokenLine, tokenColumn));
                        break;
                    case '<':
                        Advance();
                        if (Match('='))
                            tokens.Add(new Token(TokenKind.LessEqual, "<=", tokenLine, tokenColumn));
                        else
                            tokens.Add(new Token(TokenKind.Less, "<", tokenLine, tokenColumn));
                        break;
                    case '>':
                        Advance();
                        if (Match('='))
                            tokens.Add(new Token(TokenKind.GreaterEqual, ">=", tokenLine, tokenColumn));
                        else
                            tokens.Add(new Token(TokenKind.Greater, ">", tokenLine, tokenColumn));
                        break;
                    case '=':
                        Advance();
                        if (Match('='))
                        {
                            if (Match('='))
                            {
                                if (Match('='))
                                    tokens.Add(new Token(TokenKind.QuadEqual, "====", tokenLine, tokenColumn));
                                else
                                    tokens.Add(new Token(TokenKind.TripleEqual, "===", tokenLine, tokenColumn));
                            }
                            else
                            {
                                tokens.Add(new Token(TokenKind.DoubleEqual, "==", tokenLine, tokenColumn));
                            }
                        }
                        else
                        {
                            tokens.Add(new Token(TokenKind.Equal, "=", tokenLine, tokenColumn));
                        }
                        break;
                    case '!':
                        Advance();
                        tokens.Add(new Token(TokenKind.Exclamation, "!", tokenLine, tokenColumn));
                        break;
                    case '¡':
                        Advance();
                        tokens.Add(new Token(TokenKind.InvertedExclamation, "¡", tokenLine, tokenColumn));
                        break;
                    case '?':
                        Advance();
                        tokens.Add(new Token(TokenKind.Question, "?", tokenLine, tokenColumn));
                        break;
                    case '"':
                        tokens.Add(ReadString());
                        break;
                    default:
                        if (char.IsDigit(c))
                        {
                            tokens.Add(ReadNumber());
                        }
                        else if (char.IsLetter(c) || c == '_' || c == '@' || char.GetUnicodeCategory(c) == UnicodeCategory.OtherLetter)
                        {
                            tokens.Add(ReadIdentifierOrKeyword());
                        }
                        else
                        {
                            throw new InterpreterException($"Unexpected character '{c}' at line {_line}, column {_column}.");
                        }
                        break;
                }
            }

            tokens.Add(new Token(TokenKind.EndOfFile, string.Empty, _line, _column));
            return tokens;
        }

        private void SkipWhitespaceAndComments()
        {
            while (!IsAtEnd())
            {
                char c = Peek();
                if (char.IsWhiteSpace(c))
                {
                    Advance();
                    continue;
                }

                // single-line comments: //
                if (c == '/' && PeekNext() == '/')
                {
                    Advance();
                    Advance();
                    while (!IsAtEnd() && Peek() != '\n')
                        Advance();
                    continue;
                }

                break;
            }
        }

        private Token ReadNumber()
        {
            int startLine = _line;
            int startColumn = _column;
            int start = _position;
            bool hasDot = false;

            while (!IsAtEnd())
            {
                char c = Peek();
                if (char.IsDigit(c))
                {
                    Advance();
                }
                else if (c == '.' && !hasDot)
                {
                    hasDot = true;
                    Advance();
                }
                else
                {
                    break;
                }
            }

            string text = _source.Substring(start, _position - start);
            return new Token(TokenKind.Number, text, startLine, startColumn);
        }

        private Token ReadString()
        {
            int startLine = _line;
            int startColumn = _column;
            Advance(); // opening "

            var sb = new StringBuilder();
            while (!IsAtEnd())
            {
                char c = Peek();
                if (c == '"')
                {
                    Advance();
                    break;
                }

                if (c == '\\')
                {
                    Advance();
                    if (IsAtEnd())
                        break;
                    char e = Peek();
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        default: sb.Append(e); break;
                    }
                    Advance();
                }
                else
                {
                    sb.Append(c);
                    Advance();
                }
            }

            return new Token(TokenKind.String, sb.ToString(), startLine, startColumn);
        }

        private Token ReadIdentifierOrKeyword()
        {
            int startLine = _line;
            int startColumn = _column;
            int start = _position;

            while (!IsAtEnd())
            {
                char c = Peek();
                if (char.IsLetterOrDigit(c) || c == '_' || c == '@' || char.GetUnicodeCategory(c) == UnicodeCategory.OtherLetter)
                {
                    Advance();
                }
                else
                {
                    break;
                }
            }

            string text = _source.Substring(start, _position - start);

            TokenKind kind = text switch
            {
                "const" => TokenKind.Const,
                "var" => TokenKind.Var,
                "true" => TokenKind.True,
                "false" => TokenKind.False,
                "maybe" => TokenKind.Maybe,
                "undefined" => TokenKind.Undefined,
                "delete" => TokenKind.Delete,
                "reverse" => TokenKind.Reverse,
                "forward" => TokenKind.Forward,
                "when" => TokenKind.When,
                _ => TokenKind.Identifier
            };

            return new Token(kind, text, startLine, startColumn);
        }

        private bool IsAtEnd() => _position >= _length;

        private char Peek() => _position >= _length ? '\0' : _source[_position];

        private char PeekNext() => _position + 1 >= _length ? '\0' : _source[_position + 1];

        private void Advance()
        {
            if (IsAtEnd())
                return;

            char c = _source[_position++];
            if (c == '\n')
            {
                _line++;
                _column = 1;
            }
            else
            {
                _column++;
            }
        }

        private bool Match(char expected)
        {
            if (IsAtEnd())
                return false;
            if (_source[_position] != expected)
                return false;
            Advance();
            return true;
        }
    }
}
