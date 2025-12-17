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
        private readonly List<Token> _tokens = new();
        private int _start;
        private int _current;

        public Lexer(string source)
        {
            _source = source ?? string.Empty;
        }

        public IReadOnlyList<Token> Tokenize()
        {
            while (!IsAtEnd())
            {
                _start = _current;
                SkipWhitespaceAndComments();
                _start = _current;
                if (IsAtEnd())
                    break;
                ScanToken();
            }

            _tokens.Add(new Token(TokenType.EndOfFile, string.Empty, null, _current));
            return _tokens;
        }

        private bool IsAtEnd() => _current >= _source.Length;

        private char Advance() => _source[_current++];

        private char Peek() => IsAtEnd() ? '\0' : _source[_current];

        private char PeekNext() => (_current + 1 >= _source.Length) ? '\0' : _source[_current + 1];


        private bool IsQuestionTerminator()
        {
            // We're currently just after '?' (because ScanToken() already advanced).
            // If the next non-space char is newline, EOF, '}', or a line comment, treat '?' as a statement terminator.
            int i = _current;
            while (i < _source.Length)
            {
                char c = _source[i];
                if (c == ' ' || c == '\t')
                {
                    i++;
                    continue;
                }

                if (c == '\r' || c == '\n')
                    return true;

                if (c == '}')
                    return true;

                if (c == '/' && (i + 1) < _source.Length && _source[i + 1] == '/')
                    return true;

                return false;
            }

            // EOF
            return true;
        }

        private bool Match(char expected)
        {
            if (IsAtEnd()) return false;
            if (_source[_current] != expected) return false;
            _current++;
            return true;
        }

        private void AddToken(TokenType type, object? literal = null)
        {
            string text = _source.Substring(_start, _current - _start);
            _tokens.Add(new Token(type, text, literal, _start));
        }

        private void SkipWhitespaceAndComments()
        {
            while (!IsAtEnd())
            {
                char c = Peek();
                switch (c)
                {
                    case ' ':
                    case '\r':
                    case '\t':
                    case '\n':
                        _current++;
                        break;
                    case '/':
                        if (_current + 1 < _source.Length && _source[_current + 1] == '/')
                        {
                            _current += 2;
                            while (!IsAtEnd() && _source[_current] != '\n')
                                _current++;
                        }
                        else
                        {
                            return;
                        }
                        break;
                    default:
                        return;
                }
            }
        }

        private void ScanToken()
        {
            if (IsAtEnd())
                return;

            var c = Advance();
            switch (c)
            {
                case '(':
                    AddToken(TokenType.LeftParen);
                    break;
                case ')':
                    AddToken(TokenType.RightParen);
                    break;
                case '[':
                    AddToken(TokenType.LeftBracket);
                    break;
                case ']':
                    AddToken(TokenType.RightBracket);
                    break;
                case '{':
                    AddToken(TokenType.LeftBrace);
                    break;
                case '}':
                    AddToken(TokenType.RightBrace);
                    break;
                case ',':
                    AddToken(TokenType.Comma);
                    break;
                case ':':
                    AddToken(TokenType.Colon);
                    break;
                case '!':
                    AddToken(TokenType.Bang);
                    break;
                case '?':
                    // Disambiguation: '?' at end-of-line / before '}' / before '//' is a debug terminator.
                    // Otherwise it is the conditional operator token.
                    AddToken(IsQuestionTerminator() ? TokenType.Question : TokenType.QuestionOp);
                    break;
                case '+':
                    if (Match('+'))
                        AddToken(TokenType.PlusPlus);
                    else
                        AddToken(TokenType.Plus);
                    break;
                case '-':
                    if (Match('-'))
                        AddToken(TokenType.MinusMinus);
                    else
                        AddToken(TokenType.Minus);
                    break;
                
case '*':
{
    // '*' is normal multiply, but '**' and more is postfix power (StarRun)
    int count = 1;
    while (Match('*'))
        count++;

    if (count == 1)
        AddToken(TokenType.Star);
    else
        AddToken(TokenType.StarRun);

    break;
}case '/':
                    AddToken(TokenType.Slash);
                    break;
                
case '\\':
    if (Match('\\'))
        AddToken(TokenType.Root);
    else
        throw new InterpreterException("Expected \\\\ (double backslash) for root operator.", _start);
    break;

case '<':
                    if (Match('='))
                        AddToken(TokenType.LessEqual);
                    else
                        AddToken(TokenType.Less);
                    break;
                case '>':
                    if (Match('='))
                        AddToken(TokenType.GreaterEqual);
                    else
                        AddToken(TokenType.Greater);
                    break;
                case '=':
                    if (Match('>'))
                    {
                        AddToken(TokenType.Arrow); // '=>(arrow)'
                    }
                    else if (Match('='))
                    {
                        if (Match('='))
                        {
                            if (Match('='))
                            {
                                AddToken(TokenType.EqualEqualEqual); // '===='
                            }
                            else
                            {
                                AddToken(TokenType.EqualEqual); // '==='
                            }
                        }
                        else
                        {
                            AddToken(TokenType.Equal); // '=='
                        }
                    }
                    else
                    {
                        AddToken(TokenType.Assign); // '='
                    }
                    break;
                case '"':
                case '\'':
                    ScanString(c);
                    break;
                default:
                    if (char.IsDigit(c))
                    {
                        ScanNumber();
                    }
                    else if (IsIdentifierStart(c))
                    {
                        ScanIdentifier();
                    }
                    else
                    {
                        throw new InterpreterException($"Unexpected character '{c}'.", _current - 1);
                    }
                    break;
            }
        }

        private void ScanString(char quote)
        {
            var sb = new StringBuilder();
            while (!IsAtEnd() && Peek() != quote)
            {
                char c = Advance();
                sb.Append(c);
            }

            if (IsAtEnd())
                throw new InterpreterException("Unterminated string literal.", _start);

            Advance(); // closing quote
            AddToken(TokenType.String, sb.ToString());
        }

        private void ScanNumber()
        {
            while (char.IsDigit(Peek()))
                Advance();

            if (Peek() == '.' && char.IsDigit(PeekNext()))
            {
                Advance();
                while (char.IsDigit(Peek()))
                    Advance();
            }

            string text = _source.Substring(_start, _current - _start);
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                throw new InterpreterException($"Invalid number literal '{text}'.", _start);

            AddToken(TokenType.Number, value);
        }

        private void ScanIdentifier()
        {
            while (IsIdentifierPart(Peek()))
                Advance();

            string text = _source.Substring(_start, _current - _start);

            TokenType type = text switch
            {
                "const" => TokenType.Const,
                "var" => TokenType.Var,
                "reverse" => TokenType.Reverse,
                "forward" => TokenType.Forward,
                "delete" => TokenType.Delete,
                "when" => TokenType.When,
                "if" => TokenType.If,
                "else" => TokenType.Else,
                "idk" => TokenType.Idk,
                "return" => TokenType.Return,
                "while" => TokenType.While,
                "break" => TokenType.Break,
                "continue" => TokenType.Continue,
                _ => TokenType.Identifier
            };

            AddToken(type, null);
        }

        private static bool IsIdentifierStart(char c) =>
            char.IsLetter(c) || c == '_' || c == '$';

        private static bool IsIdentifierPart(char c) =>
            char.IsLetterOrDigit(c) || c == '_' || c == '$';
    }
}
