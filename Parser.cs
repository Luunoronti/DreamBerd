using System;
using System.Collections.Generic;

namespace DreamberdInterpreter
{
    public sealed class Parser
    {
        private readonly IReadOnlyList<Token> _tokens;
        private int _current;

        public Parser(IReadOnlyList<Token> tokens)
        {
            _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        }

        private bool IsAtEnd() => Peek().Type == TokenType.EndOfFile;

        private Token Peek() => _tokens[_current];

        private Token Previous() => _tokens[_current - 1];

        private Token Advance()
        {
            if (!IsAtEnd()) _current++;
            return Previous();
        }

        private bool Check(TokenType type)
        {
            if (IsAtEnd()) return false;
            return Peek().Type == type;
        }

        private bool Match(params TokenType[] types)
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

        private Token Consume(TokenType type, string message)
        {
            if (Check(type)) return Advance();
            var t = Peek();
            throw new InterpreterException(message + $" Found '{t.Lexeme}'.", t.Position);
        }

        public List<Statement> ParseProgram()
        {
            var statements = new List<Statement>();
            while (!IsAtEnd())
            {
                if (Check(TokenType.EndOfFile))
                    break;
                statements.Add(ParseStatement());
            }
            return statements;
        }

        private bool IsFunctionKeyword()
        {
            if (!Check(TokenType.Identifier))
                return false;

            string lex = Peek().Lexeme;
            return lex == "function"
                || lex == "func"
                || lex == "fun"
                || lex == "fn"
                || lex == "functi"
                || lex == "f";
        }

        private Statement ParseStatement()
        {
            // Blok { ... }
            if (Match(TokenType.LeftBrace))
            {
                return ParseBlockStatementAfterOpeningBrace();
            }

            // *** TU MUSI BYĆ OBSŁUGA IF ***
            if (Match(TokenType.If))
            {
                return ParseIfStatement();
            }

            if (Match(TokenType.While))
            {
                return ParseWhileStatement();
            }

            if (Match(TokenType.Break))
            {
                // break!
                Consume(TokenType.Bang, "Expected '!' after 'break'.");
                return new BreakStatement();
            }

            if (Match(TokenType.Continue))
            {
                // continue!
                Consume(TokenType.Bang, "Expected '!' after 'continue'.");
                return new ContinueStatement();
            }

            if (Match(TokenType.Return))
            {
                return ParseReturnStatementAfterKeyword();
            }

            if (IsFunctionKeyword())
            {
                return ParseFunctionDeclaration();
            }

            if (Check(TokenType.Const) || Check(TokenType.Var))
            {
                return ParseVariableDeclaration();
            }

            if (Match(TokenType.Reverse))
            {
                bool isDebug = ParseTerminatorIsDebug();
                return new ReverseStatement(isDebug);
            }

            if (Match(TokenType.Forward))
            {
                bool isDebug = ParseTerminatorIsDebug();
                return new ForwardStatement(isDebug);
            }

            if (Match(TokenType.Delete))
            {
                Expression target = ParseExpression();
                bool isDebug = ParseTerminatorIsDebug();
                return new DeleteStatement(target, isDebug);
            }

            if (Match(TokenType.When))
            {
                return ParseWhenStatement();
            }

            // domyślnie: wyrażenie jako statement
            Expression expr = ParseExpression();
            bool debug = ParseTerminatorIsDebug();
            return new ExpressionStatement(expr, debug);
        }

        private Statement ParseBlockStatementAfterOpeningBrace()
        {
            var statements = new List<Statement>();

            while (!Check(TokenType.RightBrace) && !IsAtEnd())
            {
                statements.Add(ParseStatement());
            }

            Consume(TokenType.RightBrace, "Expected '}' to close block.");
            return new BlockStatement(statements);
        }

        private bool ParseTerminatorIsDebug()
        {
            if (Match(TokenType.Bang))
                return false;
            if (Match(TokenType.Question))
                return true;
            throw new InterpreterException("Expected '!' or '?' at end of statement.", Peek().Position);
        }

        private Statement ParseFunctionDeclaration()
        {
            // keyword: function / func / fun / fn / functi / f
            Advance(); // zjadamy keyword (Identifier)

            Token nameTok = Consume(TokenType.Identifier, "Expected function name.");
            string name = nameTok.Lexeme;

            Consume(TokenType.LeftParen, "Expected '(' after function name.");

            var parameters = new List<string>();
            if (!Check(TokenType.RightParen))
            {
                do
                {
                    Token paramTok = Consume(TokenType.Identifier, "Expected parameter name.");
                    parameters.Add(paramTok.Lexeme);
                }
                while (Match(TokenType.Comma));
            }

            Consume(TokenType.RightParen, "Expected ')' after function parameters.");
            Consume(TokenType.Arrow, "Expected '=>' after function parameter list.");

            // body: albo expression (stary styl), albo blok { ... }
            Statement bodyStmt;
            if (Match(TokenType.LeftBrace))
            {
                bodyStmt = ParseBlockStatementAfterOpeningBrace();
            }
            else
            {
                // Dla kompatybilności: function f(x) => expr!
                // zachowuje się jak "return expr".
                Expression bodyExpr = ParseExpression();
                bodyStmt = new ReturnStatement(bodyExpr);
            }

            bool isDebug = ParseTerminatorIsDebug(); // na razie ignorujemy isDebug

            return new FunctionDeclarationStatement(name, parameters, bodyStmt);
        }

        private Statement ParseReturnStatementAfterKeyword()
        {
            // return!
            // return expr!
            Expression? expr = null;

            if (!Check(TokenType.Bang) && !Check(TokenType.Question))
            {
                expr = ParseExpression();
            }

            bool isDebug = ParseTerminatorIsDebug(); // ignorujemy – return nie ma debug-print
            _ = isDebug;

            return new ReturnStatement(expr);
        }

        private Statement ParseVariableDeclaration()
        {
            TokenType firstKw;
            if (Match(TokenType.Const))
                firstKw = TokenType.Const;
            else if (Match(TokenType.Var))
                firstKw = TokenType.Var;
            else
            throw new InterpreterException("Expected 'const' or 'var' at variable declaration.", Peek().Position);

            TokenType secondKw;
            if (Match(TokenType.Const))
                secondKw = TokenType.Const;
            else if (Match(TokenType.Var))
                secondKw = TokenType.Var;
            else
            throw new InterpreterException("Variable declaration must use two keywords (e.g. 'const const', 'var var').", Peek().Position);

            DeclarationKind declKind = DeclarationKind.Normal;
            Mutability mutability;

            if (firstKw == TokenType.Const && secondKw == TokenType.Const && Match(TokenType.Const))
            {
                declKind = DeclarationKind.ConstConstConst;
                mutability = Mutability.ConstConst;
            }
            else
            {
                bool aConst = firstKw == TokenType.Const;
                bool bConst = secondKw == TokenType.Const;

                if (aConst && bConst) mutability = Mutability.ConstConst;
                else if (aConst && !bConst) mutability = Mutability.ConstVar;
                else if (!aConst && bConst) mutability = Mutability.VarConst;
                else mutability = Mutability.VarVar;
            }

            Token nameTok = Consume(TokenType.Identifier, "Expected variable name.");
            string name = nameTok.Lexeme;

            LifetimeSpecifier lifetime = LifetimeSpecifier.None;
            if (Match(TokenType.Less))
            {
                lifetime = ParseLifetimeSpecifier();
            }

            Consume(TokenType.Assign, "Expected '=' after variable name.");
            Expression initializer = ParseExpression();
            bool isDebug = ParseTerminatorIsDebug(); // ignored

            int priority = 0;

            return new VariableDeclarationStatement(declKind, mutability, name, lifetime, priority, initializer);
        }

        private LifetimeSpecifier ParseLifetimeSpecifier()
        {
            if (Match(TokenType.Identifier) &&
                string.Equals(Previous().Lexeme, "Infinity", StringComparison.Ordinal))
            {
                Consume(TokenType.Greater, "Expected '>' after Infinity in lifetime specifier.");
                return LifetimeSpecifier.Infinity;
            }

            Token numberTok = Consume(TokenType.Number, "Expected number in lifetime specifier.");
            double value = (double)(numberTok.Literal ?? 0.0);

            bool seconds = false;
            if (Match(TokenType.Identifier) &&
                string.Equals(Previous().Lexeme, "s", StringComparison.OrdinalIgnoreCase))
            {
                seconds = true;
            }

            Consume(TokenType.Greater, "Expected '>' to close lifetime specifier.");

            if (seconds)
                return LifetimeSpecifier.Seconds(value);
            else
                return LifetimeSpecifier.Lines(value);
        }

        private Statement ParseWhenStatement()
        {
            Consume(TokenType.LeftParen, "Expected '(' after 'when'.");
            Expression condition = ParseExpression();
            Consume(TokenType.RightParen, "Expected ')' after when condition.");

            // Na razie wspieramy:
            // when (cond) expr!
            // oraz (bonus) when (cond) { ... }
            Statement bodyStmt;
            if (Match(TokenType.LeftBrace))
            {
                bodyStmt = ParseBlockStatementAfterOpeningBrace();
            }
            else
            {
                Expression bodyExpr = ParseExpression();
                bool isDebug = ParseTerminatorIsDebug();
                bodyStmt = new ExpressionStatement(bodyExpr, isDebug);
            }

            return new WhenStatement(condition, bodyStmt);
        }

        private Statement ParseIfStatement()
        {
            Consume(TokenType.LeftParen, "Expected '(' after 'if'.");
            Expression condition = ParseExpression();
            Consume(TokenType.RightParen, "Expected ')' after if condition.");

            // Dopuszczamy zarówno pojedynczy statement (np. expr!), jak i blok { ... }
            Statement thenStmt = ParseStatement();

            Statement? elseStmt = null;
            if (Match(TokenType.Else))
            {
                elseStmt = ParseStatement();
            }

            return new IfStatement(condition, thenStmt, elseStmt);
        }

        private Statement ParseWhileStatement()
        {
            Consume(TokenType.LeftParen, "Expected '(' after 'while'.");
            Expression condition = ParseExpression();
            Consume(TokenType.RightParen, "Expected ')' after while condition.");

            // podobnie jak w if: body może być pojedynczym statementem albo blokiem
            Statement body = ParseStatement();
            return new WhileStatement(condition, body);
        }

        private Expression ParseExpression() => ParseAssignment();

        private Expression ParseAssignment()
        {
            Expression expr = ParseEquality();

            if (Match(TokenType.Assign))
            {
                Expression value = ParseAssignment();

                if (expr is IdentifierExpression ident)
                {
                    return new AssignmentExpression(ident.Name, value);
                }

                if (expr is IndexExpression idx)
                {
                    return new IndexAssignmentExpression(idx.Target, idx.Index, value);
                }

                // Błąd najlepiej przypiąć do tokenu '=' (Previous()), bo to on zaczyna assignment.
                throw new InterpreterException("Invalid assignment target.", Previous().Position);
            }

            // cztero-gałęziowy operator warunkowy:
            // cond ? t : f :: m ::: u
            if (Match(TokenType.Question))
            {
                Expression whenTrue = ParseAssignment();

                Consume(TokenType.Colon, "Expected ':' after true branch of conditional expression.");
                Expression whenFalse = ParseAssignment();

                // '::'
                Consume(TokenType.Colon, "Expected '::' before maybe-branch of conditional expression.");
                Consume(TokenType.Colon, "Expected '::' before maybe-branch of conditional expression.");
                Expression whenMaybe = ParseAssignment();

                // ':::'
                Consume(TokenType.Colon, "Expected ':::' before undefined-branch of conditional expression.");
                Consume(TokenType.Colon, "Expected ':::' before undefined-branch of conditional expression.");
                Consume(TokenType.Colon, "Expected ':::' before undefined-branch of conditional expression.");
                Expression whenUndefined = ParseAssignment();

                return new ConditionalExpression(expr, whenTrue, whenFalse, whenMaybe, whenUndefined);
            }

            return expr;
        }

        private Expression ParseEquality()
        {
            Expression expr = ParseComparison();

            while (true)
            {
                if (Match(TokenType.Equal))
                {
                    Expression right = ParseComparison();
                    expr = new BinaryExpression(expr, BinaryOperator.Equal, right);
                }
                else if (Match(TokenType.EqualEqual))
                {
                    Expression right = ParseComparison();
                    expr = new BinaryExpression(expr, BinaryOperator.DoubleEqual, right);
                }
                else if (Match(TokenType.EqualEqualEqual))
                {
                    Expression right = ParseComparison();
                    expr = new BinaryExpression(expr, BinaryOperator.TripleEqual, right);
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
            Expression expr = ParseTerm();

            while (true)
            {
                if (Match(TokenType.Less))
                {
                    Expression right = ParseTerm();
                    expr = new BinaryExpression(expr, BinaryOperator.Less, right);
                }
                else if (Match(TokenType.LessEqual))
                {
                    Expression right = ParseTerm();
                    expr = new BinaryExpression(expr, BinaryOperator.LessOrEqual, right);
                }
                else if (Match(TokenType.Greater))
                {
                    Expression right = ParseTerm();
                    expr = new BinaryExpression(expr, BinaryOperator.Greater, right);
                }
                else if (Match(TokenType.GreaterEqual))
                {
                    Expression right = ParseTerm();
                    expr = new BinaryExpression(expr, BinaryOperator.GreaterOrEqual, right);
                }
                else
                {
                    break;
                }
            }

            return expr;
        }

        private Expression ParseTerm()
        {
            Expression expr = ParseFactor();

            while (true)
            {
                if (Match(TokenType.Plus))
                {
                    Expression right = ParseFactor();
                    expr = new BinaryExpression(expr, BinaryOperator.Add, right);
                }
                else if (Match(TokenType.Minus))
                {
                    Expression right = ParseFactor();
                    expr = new BinaryExpression(expr, BinaryOperator.Subtract, right);
                }
                else
                {
                    break;
                }
            }

            return expr;
        }

        private Expression ParseFactor()
        {
            Expression expr = ParseUnary();

            while (true)
            {
                if (Match(TokenType.Star))
                {
                    Expression right = ParseUnary();
                    expr = new BinaryExpression(expr, BinaryOperator.Multiply, right);
                }
                else if (Match(TokenType.Slash))
                {
                    Expression right = ParseUnary();
                    expr = new BinaryExpression(expr, BinaryOperator.Divide, right);
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
            if (Match(TokenType.Minus))
            {
                Expression right = ParseUnary();
                return new UnaryExpression(UnaryOperator.Negate, right);
            }

            return ParsePostfix();
        }

        private Expression ParsePostfix()
        {
            Expression expr = ParsePrimary();

            while (true)
            {
                if (Match(TokenType.LeftParen))
                {
                    var args = new List<Expression>();
                    if (!Check(TokenType.RightParen))
                    {
                        do
                        {
                            args.Add(ParseExpression());
                        } while (Match(TokenType.Comma));
                    }
                    Consume(TokenType.RightParen, "Expected ')' after arguments.");
                    expr = new CallExpression(expr, args);
                }
                else if (Match(TokenType.LeftBracket))
                {
                    Expression index = ParseExpression();
                    Consume(TokenType.RightBracket, "Expected ']' after index.");
                    expr = new IndexExpression(expr, index);
                }
                else
                {
                    break;
                }
            }

            return expr;
        }

        private Expression ParsePrimary()
        {
            if (Match(TokenType.Number))
            {
                double value = (double)(Previous().Literal ?? 0.0);
                return new LiteralExpression(Value.FromNumber(value));
            }

            if (Match(TokenType.String))
            {
                string text = Previous().Literal as string ?? string.Empty;
                return new LiteralExpression(Value.FromString(text));
            }

            if (Match(TokenType.Identifier))
            {
                string name = Previous().Lexeme;

                // Booleany i undefined jako literały Dreamberda
                switch (name)
                {
                    case "true":
                        return new LiteralExpression(Value.FromBoolean(true));
                    case "false":
                        return new LiteralExpression(Value.FromBoolean(false));
                    case "maybe":
                        return new LiteralExpression(Value.Maybe);
                    case "undefined":
                        return new LiteralExpression(Value.Undefined);
                    default:
                        return new IdentifierExpression(name);
                }
            }

            if (Match(TokenType.LeftParen))
            {
                Expression expr = ParseExpression();
                Consume(TokenType.RightParen, "Expected ')' after expression.");
                return expr;
            }

            if (Match(TokenType.LeftBracket))
            {
                var elements = new List<Expression>();
                if (!Check(TokenType.RightBracket))
                {
                    do
                    {
                        elements.Add(ParseExpression());
                    } while (Match(TokenType.Comma));
                }
                Consume(TokenType.RightBracket, "Expected ']' after array literal.");
                return new ArrayLiteralExpression(elements);
            }

            throw new InterpreterException($"Unexpected token '{Peek().Lexeme}'.", Peek().Position);
        }
    }
}
