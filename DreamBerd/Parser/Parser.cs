
namespace DreamberdInterpreter
{
    public sealed partial class Parser
    {
        private readonly IReadOnlyList<Token> _tokens;
        private int _current;

        public Parser(IReadOnlyList<Token> tokens) => _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));

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

        private void Fatal(Token token, string message) => throw new InterpreterException(message + $" Found '{token.Lexeme}'.", token.Position);

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
                int pos = Previous().Position;
                return ParseBlockStatementAfterOpeningBrace(pos);
            }

            if (Match(TokenType.If))
            {
                int pos = Previous().Position;
                return ParseIfStatement(pos);
            }

            if (Match(TokenType.While))
            {
                int pos = Previous().Position;
                return ParseWhileStatement(pos);
            }

            if (Match(TokenType.Break))
            {
                int pos = Previous().Position;
                if (Check(TokenType.Bang))
                    Consume(TokenType.Bang, "Expected '!' after 'break'.");
                return new BreakStatement(pos);
            }

            if (Match(TokenType.Continue))
            {
                int pos = Previous().Position;
                if (Check(TokenType.Bang))
                    Consume(TokenType.Bang, "Expected '!' after 'continue'.");
                return new ContinueStatement(pos);
            }


            // try again!
            // Statement-only: dozwolone tylko wewnątrz if/else/idk.
            // Implementujemy to jako dwa identyfikatory: 'try' 'again' + terminator (! lub ?).
            if (Check(TokenType.Identifier)
                && string.Equals(Peek().Lexeme, "try", StringComparison.Ordinal)
                && _current + 1 < _tokens.Count
                && _tokens[_current + 1].Type == TokenType.Identifier
                && string.Equals(_tokens[_current + 1].Lexeme, "again", StringComparison.Ordinal))
            {
                int pos = Peek().Position;
                Advance(); // 'try'
                Advance(); // 'again'
                ParseTerminator(); // consume optional ! / ?? etc.
                return new TryAgainStatement(pos);
            }

            if (Match(TokenType.Return))
            {
                int pos = Previous().Position;
                return ParseReturnStatementAfterKeyword(pos);
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
                int pos = Previous().Position;
                bool isDebug = ParseTerminatorIsDebug();
                return new ReverseStatement(isDebug, pos);
            }

            if (Match(TokenType.Forward))
            {
                int pos = Previous().Position;
                bool isDebug = ParseTerminatorIsDebug();
                return new ForwardStatement(isDebug, pos);
            }

            if (Match(TokenType.Delete))
            {
                int pos = Previous().Position;
                Expression target = ParseExpression();
                bool isDebug = ParseTerminatorIsDebug();
                return new DeleteStatement(target, isDebug, pos);
            }

            if (Match(TokenType.When))
            {
                int pos = Previous().Position;
                return ParseWhenStatement(pos);
            }

            // domyślnie: wyrażenie jako statement
            Expression expr = ParseExpression();
            bool debug = ParseTerminatorIsDebug();
            return new ExpressionStatement(expr, debug, expr.Position);
        }

        private Statement ParseBlockStatementAfterOpeningBrace(int position)
        {
            var statements = new List<Statement>();

            while (!Check(TokenType.RightBrace) && !IsAtEnd())
            {
                statements.Add(ParseStatement());
            }

            Consume(TokenType.RightBrace, "Expected '}' to close block.");
            return new BlockStatement(statements, position);
        }

        private TerminatorInfo ParseTerminator()
        {
            // Gulf of Mexico / DreamBerd: można dać wiele '!' na końcu statementu.
            // Dla większości statementów to jest tylko "extra"; dla deklaracji zmiennych
            // ta liczba jest używana jako priorytet deklaracji (overloading).
            if (Match(TokenType.Bang))
            {
                int count = 1;
                while (Match(TokenType.Bang))
                    count++;

                return new TerminatorInfo(isDebug: false, exclamationCount: count);
            }

            if (Match(TokenType.Question))
            {
                // Pozwalamy na wielokrotne '??' jako debug-terminator.
                while (Match(TokenType.Question))
                {
                    // noop
                }

                return new TerminatorInfo(isDebug: true, exclamationCount: 0);
            }

            // we do allow no ! or ? at the end
            // throw new InterpreterException("Expected '!' or '?' at end of statement.", Peek().Position);
            return new TerminatorInfo(isDebug: false, exclamationCount: 0);
        }

        private bool ParseTerminatorIsDebug() => ParseTerminator().IsDebug;


        private Statement ParseFunctionDeclaration()
        {
            // keyword: function / func / fun / fn / functi / f
            int funcPos = Peek().Position;
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
                int blockPos = Previous().Position;
                bodyStmt = ParseBlockStatementAfterOpeningBrace(blockPos);
            }
            else
            {
                // Dla kompatybilności: function f(x) => expr!
                // zachowuje się jak "return expr".
                Expression bodyExpr = ParseExpression();
                bodyStmt = new ReturnStatement(bodyExpr, bodyExpr.Position);
            }

            bool isDebug = ParseTerminatorIsDebug(); // na razie ignorujemy isDebug
            _ = isDebug;

            return new FunctionDeclarationStatement(name, parameters, bodyStmt, funcPos);
        }

        private Statement ParseReturnStatementAfterKeyword(int position)
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

            return new ReturnStatement(expr, position);
        }

        private Statement ParseVariableDeclaration()
        {
            int position = Peek().Position;

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

            // Liczba wykrzykników determinuje priorytet deklaracji (overloading).
            // '?' (debug) traktujemy jak zwykły terminator z domyślnym priorytetem 1.
            var term = ParseTerminator();
            int priority = term.IsDebug ? 1 : term.ExclamationCount;

            return new VariableDeclarationStatement(declKind, mutability, name, lifetime, priority, initializer, position);
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

        private Statement ParseWhenStatement(int position)
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
                int blockPos = Previous().Position;
                bodyStmt = ParseBlockStatementAfterOpeningBrace(blockPos);
            }
            else
            {
                Expression bodyExpr = ParseExpression();
                bool isDebug = ParseTerminatorIsDebug();
                bodyStmt = new ExpressionStatement(bodyExpr, isDebug, bodyExpr.Position);
            }

            return new WhenStatement(condition, bodyStmt, position);
        }

        private Statement ParseIfStatement(int position)
        {
            Consume(TokenType.LeftParen, "Expected '(' after 'if'.");
            var condition = ParseExpression();
            Consume(TokenType.RightParen, "Expected ')' after if condition.");

            // Dopuszczamy zarówno pojedynczy statement (np. expr!), jak i blok { ... }
            var thenStmt = ParseStatement();

            Statement? idkStmt = null;
            Statement? elseStmt = null;

            for (var i = 0; i < 2; i++)
            {
                if (Match(TokenType.Idk))
                {
                    idkStmt = ParseStatement();
                }
                if (Match(TokenType.Else))
                {
                    elseStmt = ParseStatement();
                }
            }

            return new IfStatement(condition, thenStmt, elseStmt, idkStmt, position);
        }

        private Statement ParseWhileStatement(int position)
        {
            Consume(TokenType.LeftParen, "Expected '(' after 'while'.");
            Expression condition = ParseExpression();
            Consume(TokenType.RightParen, "Expected ')' after while condition.");

            // podobnie jak w if: body może być pojedynczym statementem albo blokiem
            Statement body = ParseStatement();
            return new WhileStatement(condition, body, position);
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
                    return new AssignmentExpression(ident.Name, value, ident.Position);
                }

                if (expr is IndexExpression idx)
                {
                    return new IndexAssignmentExpression(idx.Target, idx.Index, value, idx.Position);
                }

                // Błąd najlepiej przypiąć do tokenu '=' (Previous()), bo to on zaczyna assignment.
                throw new InterpreterException("Invalid assignment target.", Previous().Position);
            }

            // cztero-gałęziowy operator warunkowy:
            // cond ? t : f :: m ::: u
            if (Match(TokenType.QuestionOp))
            {
                int qPos = Previous().Position;

                Expression whenTrue = ParseAssignment();

                Expression? whenFalse = null;
                Expression? whenMaybe = null;
                Expression? whenUndefined = null;

                int ConsumeColonRunUpTo3()
                {
                    var t = Peek();
                    int run = 0;
                    while (Match(TokenType.Colon))
                    {
                        run++;
                        if (run > 3)
                            throw new InterpreterException($"\"Too many ':' in conditional expression (max is ':::').\" '{t.Lexeme}'.", t.Position);
                    }
                    return run;
                }

                // Optional branches after true:
                // ':'   -> false
                // '::'  -> maybe
                // ':::' -> undefined

                while (Check(TokenType.Colon))
                {
                    int run = ConsumeColonRunUpTo3();

                    switch (run)
                    {
                        case 1:
                            {
                                // ':' (false) is only allowed if we haven't already parsed any other branch.
                                //if (whenFalse != null || whenMaybe != null || whenUndefined != null)
                                //    Fatal(Peek(), "Unexpected ':' here. False-branch (':') can only appear once and must be first.");

                                whenFalse = ParseAssignment();
                                break;
                            }

                        case 2:
                            {
                                // '::' (maybe) can't appear twice and can't appear after undefined.
                                //if (whenMaybe != null)
                                //    Fatal(Peek(), "Duplicate '::' (maybe) branch in conditional expression.");
                                //if (whenUndefined != null)
                                //    Fatal(Peek(), "Unexpected '::' after ':::' (undefined). Branches must be in order.");

                                whenMaybe = ParseAssignment();
                                break;
                            }

                        case 3:
                            {
                                // ':::' (undefined) can't appear twice and must be last.
                                //if (whenUndefined != null)
                                //    Fatal(Peek(), "Duplicate ':::' (undefined) branch in conditional expression.");

                                whenUndefined = ParseAssignment();
                                break;
                                // undefined is last; if more ':' follow, that's an error
                                //if (Check(TokenType.Colon))
                                //    Fatal(Peek(), "Unexpected ':' after ':::' (undefined). Branches must end after undefined-branch.");
                                // return new ConditionalExpression(expr, whenTrue, whenFalse, whenMaybe, whenUndefined, qPos);
                            }
                        default:
                            Fatal(Peek(), "Expected ':', '::', or ':::' after true branch of conditional expression.");
                            break;
                    }
                }

                return new ConditionalExpression(expr, whenTrue, whenFalse, whenMaybe, whenUndefined, qPos);







                //int qPos = Previous().Position;

                //Expression whenTrue = ParseAssignment();

                //Consume(TokenType.Colon, "Expected ':' after true branch of conditional expression.");
                //Expression whenFalse = ParseAssignment();

                //// '::' is optional
                //if (Check(TokenType.Colon))
                //{
                //    Consume(TokenType.Colon, "Expected '::' before maybe-branch of conditional expression.");
                //    Consume(TokenType.Colon, "Expected '::' before maybe-branch of conditional expression.");


                //    // if there is one more ':', this is undefined only


                //    Expression whenMaybe = ParseAssignment();
                //}



                //// ':::'
                //Consume(TokenType.Colon, "Expected ':::' before undefined-branch of conditional expression.");
                //Consume(TokenType.Colon, "Expected ':::' before undefined-branch of conditional expression.");
                //Consume(TokenType.Colon, "Expected ':::' before undefined-branch of conditional expression.");
                //Expression whenUndefined = ParseAssignment();

                //return new ConditionalExpression(expr, whenTrue, whenFalse, whenMaybe, whenUndefined, qPos);
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
                    int pos = Previous().Position;
                    Expression right = ParseComparison();
                    expr = new BinaryExpression(expr, BinaryOperator.Equal, right, pos);
                }
                else if (Match(TokenType.EqualEqual))
                {
                    int pos = Previous().Position;
                    Expression right = ParseComparison();
                    expr = new BinaryExpression(expr, BinaryOperator.DoubleEqual, right, pos);
                }
                else if (Match(TokenType.EqualEqualEqual))
                {
                    int pos = Previous().Position;
                    Expression right = ParseComparison();
                    expr = new BinaryExpression(expr, BinaryOperator.TripleEqual, right, pos);
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
                    int pos = Previous().Position;
                    Expression right = ParseTerm();
                    expr = new BinaryExpression(expr, BinaryOperator.Less, right, pos);
                }
                else if (Match(TokenType.LessEqual))
                {
                    int pos = Previous().Position;
                    Expression right = ParseTerm();
                    expr = new BinaryExpression(expr, BinaryOperator.LessOrEqual, right, pos);
                }
                else if (Match(TokenType.Greater))
                {
                    int pos = Previous().Position;
                    Expression right = ParseTerm();
                    expr = new BinaryExpression(expr, BinaryOperator.Greater, right, pos);
                }
                else if (Match(TokenType.GreaterEqual))
                {
                    int pos = Previous().Position;
                    Expression right = ParseTerm();
                    expr = new BinaryExpression(expr, BinaryOperator.GreaterOrEqual, right, pos);
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
                    int pos = Previous().Position;
                    Expression right = ParseFactor();
                    expr = new BinaryExpression(expr, BinaryOperator.Add, right, pos);
                }
                else if (Match(TokenType.Minus))
                {
                    int pos = Previous().Position;
                    Expression right = ParseFactor();
                    expr = new BinaryExpression(expr, BinaryOperator.Subtract, right, pos);
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
                    int pos = Previous().Position;
                    Expression right = ParseUnary();
                    expr = new BinaryExpression(expr, BinaryOperator.Multiply, right, pos);
                }
                else if (Match(TokenType.Slash))
                {
                    int pos = Previous().Position;
                    Expression right = ParseUnary();
                    expr = new BinaryExpression(expr, BinaryOperator.Divide, right, pos);
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
                int pos = Previous().Position;
                Expression right = ParseUnary();
                return new UnaryExpression(UnaryOperator.Negate, right, pos);
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
                    expr = new CallExpression(expr, args, expr.Position);
                }
                else if (Match(TokenType.LeftBracket))
                {
                    Expression index = ParseExpression();
                    Consume(TokenType.RightBracket, "Expected ']' after index.");
                    expr = new IndexExpression(expr, index, expr.Position);
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
                var t = Previous();
                double value = (double)(t.Literal ?? 0.0);
                return new LiteralExpression(Value.FromNumber(value), t.Position);
            }

            if (Match(TokenType.String))
            {
                var t = Previous();
                string text = t.Literal as string ?? string.Empty;
                return new LiteralExpression(Value.FromString(text), t.Position);
            }

            if (Match(TokenType.Identifier))
            {
                var t = Previous();
                string name = t.Lexeme;
                int pos = t.Position;

                // Booleany i undefined jako literały Dreamberda
                switch (name)
                {
                    case "true":
                        return new LiteralExpression(Value.FromBoolean(true), pos);
                    case "false":
                        return new LiteralExpression(Value.FromBoolean(false), pos);
                    case "maybe":
                        return new LiteralExpression(Value.Maybe, pos);
                    case "undefined":
                        return new LiteralExpression(Value.Undefined, pos);
                    default:
                        return new IdentifierExpression(name, pos);
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
                int pos = Previous().Position;

                var elements = new List<Expression>();
                if (!Check(TokenType.RightBracket))
                {
                    do
                    {
                        elements.Add(ParseExpression());
                    } while (Match(TokenType.Comma));
                }
                Consume(TokenType.RightBracket, "Expected ']' after array literal.");
                return new ArrayLiteralExpression(elements, pos);
            }

            throw new InterpreterException($"Unexpected token '{Peek().Lexeme}'.", Peek().Position);
        }
    }
}
