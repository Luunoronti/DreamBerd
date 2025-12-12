// Parser.cs
using System;
using System.Collections.Generic;
using System.Globalization;

namespace DreamberdInterpreter
{
    internal readonly struct TerminatorInfo
    {
        public int ExclamationCount
        {
            get;
        }
        public int InvertedExclamationCount
        {
            get;
        }
        public bool HasQuestion
        {
            get;
        }

        public TerminatorInfo(int exclamationCount, int invertedExclamationCount, bool hasQuestion)
        {
            ExclamationCount = exclamationCount;
            InvertedExclamationCount = invertedExclamationCount;
            HasQuestion = hasQuestion;
        }

        public int Priority => ExclamationCount - InvertedExclamationCount;
    }

    public sealed class Parser
    {
        private readonly List<Token> _tokens;
        private int _position;

        public Parser(List<Token> tokens)
        {
            _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        }

        public List<Statement> Parse()
        {
            var statements = new List<Statement>();

            while (!IsAtEnd())
            {
                if (Check(TokenKind.EndOfFile))
                    break;

                statements.Add(ParseStatement());
            }

            return statements;
        }

        private Statement ParseStatement()
        {
            if (Check(TokenKind.Const) || Check(TokenKind.Var))
                return ParseVariableDeclaration();

            if (Match(TokenKind.Delete))
                return ParseDeleteStatement();

            if (Match(TokenKind.Reverse))
                return ParseReverseStatement();

            if (Match(TokenKind.Forward))
                return ParseForwardStatement();

            if (Match(TokenKind.When))
                return ParseWhenStatement();

            return ParseExpressionStatement();
        }

        private Statement ParseReverseStatement()
        {
            var terminators = ConsumeTerminators();
            return new ReverseStatement(terminators.Priority, terminators.HasQuestion);
        }

        private Statement ParseForwardStatement()
        {
            var terminators = ConsumeTerminators();
            return new ForwardStatement(terminators.Priority, terminators.HasQuestion);
        }

        private Statement ParseDeleteStatement()
        {
            var target = ParseExpression();
            var terminators = ConsumeTerminators();
            return new DeleteStatement(target, terminators.Priority, terminators.HasQuestion);
        }

        private Statement ParseVariableDeclaration()
        {
            Token firstToken = ConsumeConstOrVar("Expected 'const' or 'var' at start of declaration.");
            Token secondToken = ConsumeConstOrVar("Expected 'const' or 'var' after first modifier.");

            DeclarationKind declarationKind;
            Mutability mutability;

            if (firstToken.Kind == TokenKind.Const &&
                secondToken.Kind == TokenKind.Const &&
                Check(TokenKind.Const))
            {
                Advance(); // third const
                declarationKind = DeclarationKind.ConstConstConst;
                mutability = Mutability.ConstConst;
            }
            else
            {
                declarationKind = DeclarationKind.Normal;
                mutability = DetermineMutability(firstToken, secondToken);
            }

            Token nameToken = Consume(TokenKind.Identifier, "Expected variable name.");

            // Optional lifetime: <...>
            LifetimeSpecifier lifetime = LifetimeSpecifier.None;
            if (Match(TokenKind.Less))
            {
                lifetime = ParseLifetime();
            }

            Expression initializer;
            if (Match(TokenKind.Equal))
            {
                initializer = ParseExpression();
            }
            else
            {
                initializer = new LiteralExpression(Value.Null);
            }

            var terminators = ConsumeTerminators();
            int priority = terminators.Priority;

            return new VariableDeclarationStatement(
                declarationKind,
                mutability,
                nameToken.Lexeme,
                initializer,
                priority,
                lifetime);
        }

        private LifetimeSpecifier ParseLifetime()
        {
            // After '<'
            if (Match(TokenKind.Identifier))
            {
                var id = Previous();
                if (string.Equals(id.Lexeme, "Infinity", StringComparison.OrdinalIgnoreCase))
                {
                    Consume(TokenKind.Greater, "Expected '>' after Infinity lifetime.");
                    return new LifetimeSpecifier(LifetimeKind.Infinity, double.PositiveInfinity);
                }

                // Unknown identifier inside <...>: treat as no-op, but still require '>'
                Consume(TokenKind.Greater, "Expected '>' after lifetime.");
                return LifetimeSpecifier.None;
            }

            bool isNegative = Match(TokenKind.Minus);

            Token numberToken = Consume(TokenKind.Number, "Expected lifetime number or Infinity.");
            if (!double.TryParse(numberToken.Lexeme, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                throw Error(numberToken, "Invalid lifetime number.");

            if (isNegative)
                value = -value;

            LifetimeKind kind = LifetimeKind.Lines;

            if (Match(TokenKind.Identifier))
            {
                var unitToken = Previous();
                if (string.Equals(unitToken.Lexeme, "s", StringComparison.OrdinalIgnoreCase))
                    kind = LifetimeKind.Seconds;
                else
                    kind = LifetimeKind.Lines; // unknown unit -> treat as lines
            }

            Consume(TokenKind.Greater, "Expected '>' after lifetime.");
            return new LifetimeSpecifier(kind, value);
        }

        private Statement ParseWhenStatement()
        {
            // we already consumed 'when'
            Consume(TokenKind.LeftParen, "Expected '(' after 'when'.");
            Expression condition = ParseWhenCondition();
            Consume(TokenKind.RightParen, "Expected ')' after when condition.");

            // Single-statement body, e.g.:
            // when (x > 5) print("x > 5")!
            // Body statement will consume its own terminators.
            Statement body = ParseStatement();

            return new WhenStatement(condition, body);
        }

        private Expression ParseWhenCondition()
        {
            // Like expression, but '=' is always treated as equality, not assignment.
            return ParseEqualityForWhen();
        }

        private Expression ParseEqualityForWhen()
        {
            Expression expr = ParseComparison();

            while (true)
            {
                if (Match(TokenKind.Equal))
                {
                    Expression right = ParseComparison();
                    expr = new BinaryExpression(expr, right, BinaryOperator.Equal);
                }
                else if (Match(TokenKind.DoubleEqual))
                {
                    Expression right = ParseComparison();
                    expr = new BinaryExpression(expr, right, BinaryOperator.DoubleEqual);
                }
                else if (Match(TokenKind.TripleEqual))
                {
                    Expression right = ParseComparison();
                    expr = new BinaryExpression(expr, right, BinaryOperator.TripleEqual);
                }
                else if (Match(TokenKind.QuadEqual))
                {
                    Expression right = ParseComparison();
                    expr = new BinaryExpression(expr, right, BinaryOperator.QuadEqual);
                }
                else
                {
                    break;
                }
            }

            return expr;
        }

        private Statement ParseExpressionStatement()
        {
            Expression expr = ParseExpression();
            var terminators = ConsumeTerminators();
            return new ExpressionStatement(expr, terminators.Priority, terminators.HasQuestion);
        }

        private TerminatorInfo ConsumeTerminators()
        {
            int exclamations = 0;
            int inverted = 0;
            bool hasQuestion = false;
            bool any = false;

            while (true)
            {
                if (Match(TokenKind.Exclamation))
                {
                    exclamations++;
                    any = true;
                    continue;
                }

                if (Match(TokenKind.InvertedExclamation))
                {
                    inverted++;
                    any = true;
                    continue;
                }

                if (Match(TokenKind.Question))
                {
                    hasQuestion = true;
                    any = true;
                    continue;
                }

                break;
            }

            if (!any)
                throw Error(Peek(), "Expected '!', '¡' or '?' at end of statement.");

            return new TerminatorInfo(exclamations, inverted, hasQuestion);
        }

        private Expression ParseExpression()
        {
            return ParseAssignment();
        }

        private Expression ParseAssignment()
        {
            Expression expr = ParseEquality();

            if (Match(TokenKind.Equal))
            {
                if (expr is IdentifierExpression identifier)
                {
                    Expression valueExpr = ParseAssignment();
                    return new AssignmentExpression(identifier.Name, valueExpr);
                }

                if (expr is IndexExpression indexExpr)
                {
                    Expression valueExpr = ParseAssignment();
                    return new IndexAssignmentExpression(indexExpr.Target, indexExpr.Index, valueExpr);
                }

                throw Error(Previous(), "Invalid assignment target.");
            }

            return expr;
        }

        private Expression ParseEquality()
        {
            Expression expr = ParseComparison();

            while (true)
            {
                if (Match(TokenKind.DoubleEqual))
                {
                    Expression right = ParseComparison();
                    expr = new BinaryExpression(expr, right, BinaryOperator.DoubleEqual);
                }
                else if (Match(TokenKind.TripleEqual))
                {
                    Expression right = ParseComparison();
                    expr = new BinaryExpression(expr, right, BinaryOperator.TripleEqual);
                }
                else if (Match(TokenKind.QuadEqual))
                {
                    Expression right = ParseComparison();
                    expr = new BinaryExpression(expr, right, BinaryOperator.QuadEqual);
                }
                else
                {
                    break;
                }
            }

            return expr;
        }

        private Expression ParseComparison()
        {
            Expression expr = ParseAddition();

            while (true)
            {
                if (Match(TokenKind.Less))
                {
                    Expression right = ParseAddition();
                    expr = new BinaryExpression(expr, right, BinaryOperator.Less);
                }
                else if (Match(TokenKind.Greater))
                {
                    Expression right = ParseAddition();
                    expr = new BinaryExpression(expr, right, BinaryOperator.Greater);
                }
                else if (Match(TokenKind.LessEqual))
                {
                    Expression right = ParseAddition();
                    expr = new BinaryExpression(expr, right, BinaryOperator.LessOrEqual);
                }
                else if (Match(TokenKind.GreaterEqual))
                {
                    Expression right = ParseAddition();
                    expr = new BinaryExpression(expr, right, BinaryOperator.GreaterOrEqual);
                }
                else
                {
                    break;
                }
            }

            return expr;
        }

        private Expression ParseAddition()
        {
            Expression expr = ParseMultiplication();

            while (true)
            {
                if (Match(TokenKind.Plus))
                {
                    Expression right = ParseMultiplication();
                    expr = new BinaryExpression(expr, right, BinaryOperator.Add);
                }
                else if (Match(TokenKind.Minus))
                {
                    Expression right = ParseMultiplication();
                    expr = new BinaryExpression(expr, right, BinaryOperator.Subtract);
                }
                else
                {
                    break;
                }
            }

            return expr;
        }

        private Expression ParseMultiplication()
        {
            Expression expr = ParseUnary();

            while (true)
            {
                if (Match(TokenKind.Star))
                {
                    Expression right = ParseUnary();
                    expr = new BinaryExpression(expr, right, BinaryOperator.Multiply);
                }
                else if (Match(TokenKind.Slash))
                {
                    Expression right = ParseUnary();
                    expr = new BinaryExpression(expr, right, BinaryOperator.Divide);
                }
                else
                {
                    break;
                }
            }

            return expr;
        }

        private Expression ParseUnary()
        {
            if (Match(TokenKind.Minus))
            {
                Expression operand = ParseUnary();
                return new UnaryExpression(UnaryOperator.Negate, operand);
            }

            return ParsePrimary();
        }

        private Expression ParsePrimary()
        {
            Expression expr;

            if (Match(TokenKind.Number))
            {
                string text = Previous().Lexeme;
                double value = double.Parse(text, CultureInfo.InvariantCulture);
                expr = new LiteralExpression(Value.FromNumber(value));
            }
            else if (Match(TokenKind.String))
            {
                string s = Previous().Lexeme;
                expr = new LiteralExpression(Value.FromString(s));
            }
            else if (Match(TokenKind.True))
            {
                expr = new LiteralExpression(Value.FromBoolean(true));
            }
            else if (Match(TokenKind.False))
            {
                expr = new LiteralExpression(Value.FromBoolean(false));
            }
            else if (Match(TokenKind.Maybe))
            {
                expr = new LiteralExpression(Value.FromString("maybe"));
            }
            else if (Match(TokenKind.Undefined))
            {
                expr = new LiteralExpression(Value.Undefined);
            }
            else if (Match(TokenKind.Identifier))
            {
                string name = Previous().Lexeme;
                expr = new IdentifierExpression(name);
            }
            else if (Match(TokenKind.LeftParen))
            {
                Expression inner = ParseExpression();
                Consume(TokenKind.RightParen, "Expected ')' after expression.");
                expr = inner;
            }
            else if (Match(TokenKind.LeftBracket))
            {
                expr = ParseArrayLiteral();
            }
            else
            {
                throw Error(Peek(), "Unexpected token.");
            }

            return ParsePostfix(expr);
        }

        private Expression ParseArrayLiteral()
        {
            var elements = new List<Expression>();

            if (!Check(TokenKind.RightBracket))
            {
                do
                {
                    elements.Add(ParseExpression());
                } while (Match(TokenKind.Comma));
            }

            Consume(TokenKind.RightBracket, "Expected ']' after array literal.");
            return new ArrayLiteralExpression(elements);
        }

        private Expression ParsePostfix(Expression expr)
        {
            while (true)
            {
                if (Match(TokenKind.LeftParen))
                {
                    var args = new List<Expression>();
                    if (!Check(TokenKind.RightParen))
                    {
                        do
                        {
                            args.Add(ParseExpression());
                        } while (Match(TokenKind.Comma));
                    }

                    Consume(TokenKind.RightParen, "Expected ')' after function arguments.");
                    expr = new CallExpression(expr, args);
                    continue;
                }

                if (Match(TokenKind.LeftBracket))
                {
                    var indexExpr = ParseExpression();
                    Consume(TokenKind.RightBracket, "Expected ']' after index expression.");
                    expr = new IndexExpression(expr, indexExpr);
                    continue;
                }

                break;
            }

            return expr;
        }

        // Helpers

        private bool Match(TokenKind kind)
        {
            if (Check(kind))
            {
                Advance();
                return true;
            }

            return false;
        }

        private bool Check(TokenKind kind)
        {
            if (IsAtEnd())
                return kind == TokenKind.EndOfFile;

            return Peek().Kind == kind;
        }

        private Token Advance()
        {
            if (!IsAtEnd())
                _position++;

            return Previous();
        }

        private bool IsAtEnd()
        {
            return _position >= _tokens.Count || _tokens[_position].Kind == TokenKind.EndOfFile;
        }

        private Token Peek()
        {
            if (_position >= _tokens.Count)
                return _tokens[_tokens.Count - 1];

            return _tokens[_position];
        }

        private Token Previous()
        {
            return _tokens[_position - 1];
        }

        private Token Consume(TokenKind kind, string message)
        {
            if (Check(kind))
                return Advance();

            throw Error(Peek(), message);
        }

        private Token ConsumeConstOrVar(string message)
        {
            if (Check(TokenKind.Const) || Check(TokenKind.Var))
                return Advance();

            throw Error(Peek(), message);
        }

        private Mutability DetermineMutability(Token first, Token second)
        {
            if (first.Kind == TokenKind.Const && second.Kind == TokenKind.Const)
                return Mutability.ConstConst;
            if (first.Kind == TokenKind.Const && second.Kind == TokenKind.Var)
                return Mutability.ConstVar;
            if (first.Kind == TokenKind.Var && second.Kind == TokenKind.Const)
                return Mutability.VarConst;
            if (first.Kind == TokenKind.Var && second.Kind == TokenKind.Var)
                return Mutability.VarVar;

            throw new InterpreterException("Invalid mutability combination.");
        }

        private InterpreterException Error(Token token, string message)
        {
            string where = token.Kind == TokenKind.EndOfFile
                ? "at end of input"
                : $"at line {token.Line}, column {token.Column}";

            return new InterpreterException($"{message} ({where}).");
        }
    }
}
