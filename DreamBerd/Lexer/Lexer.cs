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
                case '(':
                    AddToken(TokenType.LeftParen);
                    break;
                case ')':
                    AddToken(TokenType.RightParen);
                    break;
                case ',':
                    AddToken(TokenType.Comma);
                    break;
                case ';':
                    AddToken(TokenType.Semicolon);
                    break;
                case ':':
                    AddToken(TokenType.Colon);
                    break;
                case '%':
                    AddToken(TokenType.Percent);
                    break;
                case '&':
                    AddToken(TokenType.Ampersand);
                    break;
                case '|':
                    if (Match('|'))
                        AddToken(TokenType.DoublePipe);
                    else
                        AddToken(TokenType.Pipe);
                    break;
                case '^':
                    AddToken(TokenType.Caret);
                    break;
                case '!':
                    AddToken(TokenType.Bang);
                    break;
                case '?':
                    // Disambiguation: '?' at end-of-line / before '}' / before '//' is a debug terminator.
                    // Otherwise it is the conditional operator token.
                    AddToken(IsQuestionTerminator() ? TokenType.Question : TokenType.QuestionOp);
                    break;
                case '@':
                    AddToken(TokenType.At);
                    break;
                case '~':
                    AddToken(TokenType.Tilde);
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
                    }
                case '/':
                    AddToken(TokenType.Slash);
                    break;

                case '\\':
                    if (Match('\\'))
                        AddToken(TokenType.Root);
                    else
                        throw new InterpreterException("Expected \\\\ (double backslash) for root operator.", _start);
                    break;

                case '<':
                    if (Match('>'))
                        AddToken(TokenType.MinOp);
                    else if (Match('='))
                        AddToken(TokenType.LessEqual);
                    else if (Match('<'))
                        AddToken(TokenType.ShiftLeft);
                    else
                        AddToken(TokenType.Less);
                    break;
                case '>':
                    if (Match('<'))
                        AddToken(TokenType.MaxOp);
                    else if (Match('='))
                        AddToken(TokenType.GreaterEqual);
                    else if (Match('>'))
                        AddToken(TokenType.ShiftRight);
                    else
                        AddToken(TokenType.Greater);
                    break;
                case '.':
                    if (Match('.'))
                    {
                        if (Match('.'))
                            AddToken(TokenType.Ellipsis);
                        else
                            AddToken(TokenType.RangeDots);
                    }
                    else
                        throw new InterpreterException("Unexpected '.'.", _current - 1);
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
                    int quoteCount = DetermineOpeningQuoteCount(c);
                    for (int i = 1; i < quoteCount; i++)
                        Advance();
                    ScanString(c, quoteCount);
                    break;
                case '▷':
                    AddToken(TokenType.ClampSymbol);
                    break;
                case '↻':
                    AddToken(TokenType.WrapSymbol);
                    break;
                case '⌊':
                    if (Match('⌋'))
                        AddToken(TokenType.MinUnicode);
                    else
                        throw new InterpreterException("Expected '⌋' after '⌊' for min operator.", _start);
                    break;
                case '⌈':
                    if (Match('⌉'))
                        AddToken(TokenType.MaxUnicode);
                    else
                        throw new InterpreterException("Expected '⌉' after '⌈' for max operator.", _start);
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

        private void ScanString(char quote, int quoteCount)
        {
            var sb = new StringBuilder();
            while (!IsAtEnd())
            {
                if (Peek() == quote)
                {
                    int run = 0;
                    while (!IsAtEnd() && Peek() == quote)
                    {
                        Advance();
                        run++;
                    }

                    if (run >= quoteCount)
                    {
                        AddToken(TokenType.String, sb.ToString());
                        return;
                    }

                    sb.Append(new string(quote, run));
                    continue;
                }

                char c = Advance();
                if (c == '\r')
                {
                    if (Peek() == '\n')
                        Advance();
                    sb.Append('\n');
                    continue;
                }

                if (c == '\\')
                {
                    if (IsAtEnd())
                        break;

                    char escaped = Advance();
                    switch (escaped)
                    {
                        case 'n':
                            sb.Append('\n');
                            break;
                        case 'r':
                            sb.Append('\r');
                            break;
                        case 't':
                            sb.Append('\t');
                            break;
                        case '0':
                            sb.Append('\0');
                            break;
                        case '\\':
                            sb.Append('\\');
                            break;
                        case '"':
                            sb.Append('"');
                            break;
                        case '\'':
                            sb.Append('\'');
                            break;
                        default:
                            sb.Append('\\');
                            sb.Append(escaped);
                            break;
                    }
                    continue;
                }

                sb.Append(c);
            }

            throw new InterpreterException("Unterminated string literal.", _start);
        }

        private int DetermineOpeningQuoteCount(char quote)
        {
            int run = 1;
            int i = _current;
            while (i < _source.Length && _source[i] == quote)
            {
                run++;
                i++;
            }

            if (run >= 3)
                return run;

            if (run == 2)
            {
                if (i >= _source.Length)
                    return 1;

                char next = _source[i];
                if (char.IsWhiteSpace(next) || IsOperatorChar(next) || next == '!' || next == '?' || next == '(' || next == ')')
                    return 1;

                return 2;
            }

            return 1;
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
                "class" => TokenType.Class,
                "is" => TokenType.Is,
                "a" => TokenType.A,
                "return" => TokenType.Return,
                "while" => TokenType.While,
                "break" => TokenType.Break,
                "continue" => TokenType.Continue,
                "clamp" => TokenType.ClampKeyword,
                "wrap" => TokenType.WrapKeyword,
                _ => TokenType.Identifier
            };

            AddToken(type, null);
        }

        private static bool IsIdentifierStart(char c)
        {
            if (IsOperatorChar(c))
                return false;

            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            return char.IsLetter(c)
                || cat == UnicodeCategory.OtherSymbol
                || cat == UnicodeCategory.CurrencySymbol
                || cat == UnicodeCategory.MathSymbol
                || cat == UnicodeCategory.ModifierSymbol
                || cat == UnicodeCategory.ModifierLetter
                || cat == UnicodeCategory.LetterNumber
                || cat == UnicodeCategory.OtherLetter
                || char.IsSurrogate(c)
                || c == '_' || c == '$';
        }

        private static bool IsIdentifierPart(char c)
        {
            if (IsOperatorChar(c))
                return false;

            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            return IsIdentifierStart(c)
                || char.IsDigit(c)
                || cat == UnicodeCategory.ConnectorPunctuation
                || cat == UnicodeCategory.NonSpacingMark
                || cat == UnicodeCategory.SpacingCombiningMark
                || char.IsSurrogate(c);
        }

        private static bool IsOperatorChar(char c) =>
            c is '+' or '-' or '*' or '/' or '\\' or '%' or '&' or '|' or '^' or '!' or '?' or ':' or '<' or '>' or '=' or '[' or ']' or '{' or '}' or ',' or ';' or '.' or '@' or '~'
                or '▷' or '↻' or '⌊' or '⌋' or '⌈' or '⌉';
    }
}
