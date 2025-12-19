
namespace DreamberdInterpreter
{
    public sealed partial class Parser
    {
        private readonly IReadOnlyList<Token> _tokens;
        private readonly string _source;
        private int _current;

        private readonly Stack<HashSet<string>> _scopeStack = new Stack<HashSet<string>>();
        private bool _allowStatementKeywordsAsArgs = true;

        private static readonly Dictionary<string, ulong> NumberUnits = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase)
        {
            ["zero"] = 0,
            ["one"] = 1,
            ["two"] = 2,
            ["three"] = 3,
            ["four"] = 4,
            ["five"] = 5,
            ["six"] = 6,
            ["seven"] = 7,
            ["eight"] = 8,
            ["nine"] = 9,
            ["ten"] = 10,
            ["eleven"] = 11,
            ["twelve"] = 12,
            ["thirteen"] = 13,
            ["fourteen"] = 14,
            ["fifteen"] = 15,
            ["sixteen"] = 16,
            ["seventeen"] = 17,
            ["eighteen"] = 18,
            ["nineteen"] = 19,

            // PL
            ["jeden"] = 1,
            ["dwa"] = 2,
            ["trzy"] = 3,
            ["cztery"] = 4,
            ["piec"] = 5,
            ["szesc"] = 6,
            ["siedem"] = 7,
            ["osiem"] = 8,
            ["dziewiec"] = 9,
            ["dziesiec"] = 10,
            ["jedenascie"] = 11,
            ["dwanascie"] = 12,
            ["trzynascie"] = 13,
            ["czternascie"] = 14,
            ["pietnascie"] = 15,
            ["szesnascie"] = 16,
            ["siedemnascie"] = 17,
            ["osiemnascie"] = 18,
            ["dziewietnascie"] = 19
        };

        private static readonly Dictionary<string, ulong> NumberTens = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase)
        {
            ["twenty"] = 20,
            ["thirty"] = 30,
            ["forty"] = 40,
            ["fifty"] = 50,
            ["sixty"] = 60,
            ["seventy"] = 70,
            ["eighty"] = 80,
            ["ninety"] = 90,

            // PL
            ["dwadziescia"] = 20,
            ["trzydziesci"] = 30,
            ["czterdziesci"] = 40,
            ["piecdziesiat"] = 50,
            ["szescdziesiat"] = 60,
            ["siedemdziesiat"] = 70,
            ["osiemdziesiat"] = 80,
            ["dziewiecdziesiat"] = 90
        };

        private static readonly Dictionary<string, ulong> NumberScales = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase)
        {
            ["thousand"] = 1_000,
            ["million"] = 1_000_000,
            ["billion"] = 1_000_000_000,
            ["trillion"] = 1_000_000_000_000,
            ["quadrillion"] = 1_000_000_000_000_000,
            ["quintillion"] = 1_000_000_000_000_000_000, // max w ulong

            // PL
            ["tysiac"] = 1_000,
            ["tysiace"] = 1_000,
            ["milion"] = 1_000_000,
            ["miliard"] = 1_000_000_000,
            ["bilion"] = 1_000_000_000_000,
            ["biliard"] = 1_000_000_000_000_000,
            ["trylion"] = 1_000_000_000_000_000_000
        };

        // aliasy / najczestsze literowki, zanim trafimy do wlasciwych slownikow
        private static readonly Dictionary<string, string> NumberAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["hundret"] = "hundred",
            ["hundrets"] = "hundred",
            ["hundreds"] = "hundred",

            ["milion"] = "million",   // pl-lite
            ["milions"] = "million",

            ["thounsand"] = "thousand",
            ["thounsands"] = "thousand",
            ["thousandths"] = "thousand", // pelny plural - tez sprowadzamy
            ["sto"] = "hundred"
        };

        // Precedens bazowy poszczegolnych operatorow; realny precedens wyznacza liczba spacji.
        private const int WhitespacePrecedenceWeight = 1000;
        private const int PrecEquality = 10;
        private const int PrecComparison = 20;
        private const int PrecAdd = 30;
        private const int PrecMultiply = 40;
        private const int PrecRoot = 50;
        private const int PrecClamp = 45;

        private delegate Expression BinaryBuilder(Expression left, Expression right, int position);

        private sealed record BinaryOperatorDescriptor(int BasePrecedence, BinaryBuilder Builder);

        private readonly struct BinaryOperatorMatch
        {
            public BinaryOperatorDescriptor Descriptor
            {
                get;
            }

            public Token OperatorToken
            {
                get;
            }

            public int TokensToConsume
            {
                get;
            }

            public int Precedence
            {
                get;
            }

            public bool Negate
            {
                get;
            }

            public BinaryOperatorMatch(BinaryOperatorDescriptor descriptor, Token operatorToken, int tokensToConsume, int precedence, bool negate)
            {
                Descriptor = descriptor;
                OperatorToken = operatorToken;
                TokensToConsume = tokensToConsume;
                Precedence = precedence;
                Negate = negate;
            }
        }

        private static readonly Dictionary<TokenType, BinaryOperatorDescriptor> BinaryOperatorTable = new()
        {
            { TokenType.Plus, new BinaryOperatorDescriptor(PrecAdd, (l, r, pos) => new BinaryExpression(l, BinaryOperator.Add, r, pos)) },
            { TokenType.Minus, new BinaryOperatorDescriptor(PrecAdd, (l, r, pos) => new BinaryExpression(l, BinaryOperator.Subtract, r, pos)) },
            { TokenType.Star, new BinaryOperatorDescriptor(PrecMultiply, (l, r, pos) => new BinaryExpression(l, BinaryOperator.Multiply, r, pos)) },
            { TokenType.Slash, new BinaryOperatorDescriptor(PrecMultiply, (l, r, pos) => new BinaryExpression(l, BinaryOperator.Divide, r, pos)) },
            { TokenType.ClampSymbol, new BinaryOperatorDescriptor(PrecClamp, (l, r, pos) => new BinaryExpression(l, BinaryOperator.Clamp, r, pos)) },
            { TokenType.ClampKeyword, new BinaryOperatorDescriptor(PrecClamp, (l, r, pos) => new BinaryExpression(l, BinaryOperator.Clamp, r, pos)) },
            { TokenType.WrapSymbol, new BinaryOperatorDescriptor(PrecClamp, (l, r, pos) => new BinaryExpression(l, BinaryOperator.Wrap, r, pos)) },
            { TokenType.WrapKeyword, new BinaryOperatorDescriptor(PrecClamp, (l, r, pos) => new BinaryExpression(l, BinaryOperator.Wrap, r, pos)) },
            { TokenType.Root, new BinaryOperatorDescriptor(PrecRoot, (l, r, pos) => new RootInfixExpression(l, r, pos)) },
            { TokenType.Less, new BinaryOperatorDescriptor(PrecComparison, (l, r, pos) => new BinaryExpression(l, BinaryOperator.Less, r, pos)) },
            { TokenType.LessEqual, new BinaryOperatorDescriptor(PrecComparison, (l, r, pos) => new BinaryExpression(l, BinaryOperator.LessOrEqual, r, pos)) },
            { TokenType.Greater, new BinaryOperatorDescriptor(PrecComparison, (l, r, pos) => new BinaryExpression(l, BinaryOperator.Greater, r, pos)) },
            { TokenType.GreaterEqual, new BinaryOperatorDescriptor(PrecComparison, (l, r, pos) => new BinaryExpression(l, BinaryOperator.GreaterOrEqual, r, pos)) },
            { TokenType.MinOp, new BinaryOperatorDescriptor(PrecComparison, (l, r, pos) => new BinaryExpression(l, BinaryOperator.Min, r, pos)) },
            { TokenType.MinUnicode, new BinaryOperatorDescriptor(PrecComparison, (l, r, pos) => new BinaryExpression(l, BinaryOperator.Min, r, pos)) },
            { TokenType.MaxOp, new BinaryOperatorDescriptor(PrecComparison, (l, r, pos) => new BinaryExpression(l, BinaryOperator.Max, r, pos)) },
            { TokenType.MaxUnicode, new BinaryOperatorDescriptor(PrecComparison, (l, r, pos) => new BinaryExpression(l, BinaryOperator.Max, r, pos)) },
            { TokenType.Equal, new BinaryOperatorDescriptor(PrecEquality, (l, r, pos) => new BinaryExpression(l, BinaryOperator.Equal, r, pos)) },
            { TokenType.EqualEqual, new BinaryOperatorDescriptor(PrecEquality, (l, r, pos) => new BinaryExpression(l, BinaryOperator.DoubleEqual, r, pos)) },
            { TokenType.EqualEqualEqual, new BinaryOperatorDescriptor(PrecEquality, (l, r, pos) => new BinaryExpression(l, BinaryOperator.TripleEqual, r, pos)) },
        };

        private static bool IsNameTokenType(TokenType type) => type switch
        {
            TokenType.Identifier => true,
            TokenType.Number => true,
            TokenType.String => true,
            TokenType.Const => true,
            TokenType.Var => true,
            TokenType.Reverse => true,
            TokenType.Forward => true,
            TokenType.Delete => true,
            TokenType.When => true,
            TokenType.If => true,
            TokenType.Else => true,
            TokenType.Idk => true,
            TokenType.Return => true,
            TokenType.While => true,
            TokenType.Break => true,
            TokenType.Continue => true,
            TokenType.Class => true,
            TokenType.Is => true,
            TokenType.A => true,
            _ => false
        };

        private static bool IsStatementKeywordToken(TokenType type) =>
            type == TokenType.Return
            || type == TokenType.Break
            || type == TokenType.Continue;

        private Token ConsumeNameToken(string message)
        {
            if (IsNameTokenType(Peek().Type))
                return Advance();

            Fatal(Peek(), message);
            return Peek(); // unreachable
        }

        private static string TokenToName(Token token) =>
            token.Type == TokenType.String ? (token.Literal as string ?? string.Empty) : token.Lexeme;

        public Parser(IReadOnlyList<Token> tokens, string source)
        {
            _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
            _source = source ?? string.Empty;
            PushScope(); // global scope
        }

        private bool IsAtEnd() => Peek().Type == TokenType.EndOfFile;

        private Token Peek() => _tokens[_current];

        private Token Previous() => _tokens[_current - 1];

        private TokenType PeekType(int offset)
        {
            int idx = _current + offset;
            if (idx < 0 || idx >= _tokens.Count)
                return TokenType.EndOfFile;
            return _tokens[idx].Type;
        }

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

        private void PushScope() => _scopeStack.Push(new HashSet<string>(StringComparer.Ordinal));

        private void PopScope() => _scopeStack.Pop();

        private void DeclareName(string name)
        {
            if (_scopeStack.Count == 0)
                PushScope();
            _scopeStack.Peek().Add(name);
        }

        private bool IsNameShadowed(string name)
        {
            foreach (var scope in _scopeStack)
            {
                if (scope.Contains(name))
                    return true;
            }
            return false;
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
            if (!(lex == "function"
                || lex == "func"
                || lex == "fun"
                || lex == "fn"
                || lex == "functi"
                || lex == "f"))
                return false;

            // nazwa moze byc po keywordzie; zezwalamy na brak nawiasów
            return IsNameTokenType(PeekType(1));
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

            if (Check(TokenType.Identifier)
                && PeekType(1) == TokenType.Is
                && PeekType(2) == TokenType.A
                && PeekType(3) == TokenType.Class)
            {
                Token nameTok = Advance(); // class name
                Advance(); // is
                Advance(); // a
                Advance(); // class
                return ParseClassDeclaration(nameTok);
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

            if (TryParseUpdateStatement(out var updateStmt))
                return updateStmt;

            // domyślnie: wyrażenie jako statement
            Expression expr = ParseExpression();
            bool debug = ParseTerminatorIsDebug();
            return new ExpressionStatement(expr, debug, expr.Position);
        }

        private Statement ParseBlockStatementAfterOpeningBrace(int position)
        {
            PushScope();
            var statements = new List<Statement>();

            try
            {
                while (!Check(TokenType.RightBrace) && !IsAtEnd())
                {
                    statements.Add(ParseStatement());
                }

                Consume(TokenType.RightBrace, "Expected '}' to close block.");
                return new BlockStatement(statements, position);
            }
            finally
            {
                PopScope();
            }
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


        private Statement ParseFunctionDeclaration(bool declareInEnclosingScope = true)
        {
            // keyword: function / func / fun / fn / functi / f
            int funcPos = Peek().Position;
            Advance(); // zjadamy keyword (Identifier)

            Token nameTok = ConsumeNameToken("Expected function name.");
            string name = TokenToName(nameTok);
            if (declareInEnclosingScope)
            {
                DeclareName(name); // nazwa funkcji widoczna w otaczajacym scope
            }

            PushScope(); // scope funkcji (parametry + cialo)
            try
            {
                var parameters = new List<string>();

                while (!Check(TokenType.Arrow))
                {
                    if (Match(TokenType.Comma))
                        continue;

                    Token paramTok = ConsumeNameToken("Expected parameter name or '=>' after function name.");
                    string paramName = TokenToName(paramTok);
                    parameters.Add(paramName);
                    DeclareName(paramName);
                }

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
                    // Dla kompatybilnosci: function f(x) => expr!
                    // zachowuje sie jak "return expr".
                    Expression bodyExpr = ParseExpression();
                    bodyStmt = new ReturnStatement(bodyExpr, bodyExpr.Position);
                }

                bool isDebug = ParseTerminatorIsDebug(); // na razie ignorujemy isDebug
                _ = isDebug;

                return new FunctionDeclarationStatement(name, parameters, bodyStmt, funcPos);
            }
            finally
            {
                PopScope();
            }
        }

        private Statement ParseClassDeclaration(Token nameTok)
        {
            string name = TokenToName(nameTok);
            int pos = nameTok.Position;

            Consume(TokenType.LeftBrace, "Expected '{' after class declaration.");

            // Klasa ma wlasny scope nazw podczas parsowania metod.
            PushScope();
            var methods = new List<FunctionDeclarationStatement>();
            try
            {
                while (!Check(TokenType.RightBrace) && !IsAtEnd())
                {
                    if (!IsFunctionKeyword())
                        Fatal(Peek(), "Only function declarations are allowed inside a class.");

                    var methodStmt = ParseFunctionDeclaration(declareInEnclosingScope: false);
                    if (methodStmt is FunctionDeclarationStatement funcDecl)
                    {
                        methods.Add(funcDecl);
                    }
                    else
                    {
                        Fatal(Peek(), "Expected function declaration inside class.");
                    }
                }

                Consume(TokenType.RightBrace, "Expected '}' after class body.");
            }
            finally
            {
                PopScope();
            }

            return new ClassDeclarationStatement(name, methods, pos);
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

            Token nameTok = ConsumeNameToken("Expected variable name.");
            string name = TokenToName(nameTok);

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
            bool prevAllow = _allowStatementKeywordsAsArgs;
            _allowStatementKeywordsAsArgs = false;
            Expression condition = ParseExpression();
            _allowStatementKeywordsAsArgs = prevAllow;

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

        private bool TryParseUpdateStatement(out Statement stmt)
        {
            stmt = null!;
            int start = _current;

            Expression? target = TryParseUpdateTarget();
            if (target == null)
            {
                _current = start;
                return false;
            }

            if (!Match(TokenType.Colon))
            {
                _current = start;
                return false;
            }

            UpdateOperator? op = null;
            Expression? rhs = null;
            RangeExpression? rangeExpr = null;
            int runValue = 0;

            if (Match(TokenType.Plus))
            {
                op = UpdateOperator.Add;
                rhs = ParseExpression();
            }
            else if (Match(TokenType.Minus))
            {
                op = UpdateOperator.Subtract;
                rhs = ParseExpression();
            }
            else if (Match(TokenType.Star))
            {
                op = UpdateOperator.Multiply;
                rhs = ParseExpression();
            }
            else if (Check(TokenType.StarRun))
            {
                var tok = Advance();
                int starCount = tok.Lexeme.Length;
                if ((starCount & 1) != 0)
                    Fatal(tok, "Power operator requires an even number of '*' (it's repeated \"**\").");

                runValue = 1 + (starCount / 2);
                op = UpdateOperator.Power;

                if (!(Check(TokenType.Bang) || Check(TokenType.Question) || Check(TokenType.EndOfFile)))
                {
                    rhs = ParseExpression();
                }
            }
            else if (Match(TokenType.Slash))
            {
                op = UpdateOperator.Divide;
                rhs = ParseExpression();
            }
            else if (Match(TokenType.Percent))
            {
                op = UpdateOperator.Modulo;
                rhs = ParseExpression();
            }
            else if (Check(TokenType.Root))
            {
                int rootCount = 0;
                while (Match(TokenType.Root))
                    rootCount++;

                runValue = rootCount + 1; // sqrt = 2, cbrt = 3 ...
                op = UpdateOperator.Root;

                if (!(Check(TokenType.Bang) || Check(TokenType.Question) || Check(TokenType.EndOfFile)))
                {
                    rhs = ParseExpression();
                }
            }
            else if (Match(TokenType.Ampersand))
            {
                op = UpdateOperator.BitAnd;
                rhs = ParseExpression();
            }
            else if (Match(TokenType.Pipe))
            {
                op = UpdateOperator.BitOr;
                rhs = ParseExpression();
            }
            else if (Match(TokenType.Caret))
            {
                op = UpdateOperator.BitXor;
                rhs = ParseExpression();
            }
            else if (Match(TokenType.ShiftLeft))
            {
                op = UpdateOperator.ShiftLeft;
                rhs = ParseExpression();
            }
            else if (Match(TokenType.ShiftRight))
            {
                op = UpdateOperator.ShiftRight;
                rhs = ParseExpression();
            }
            else if (Match(TokenType.QuestionOp))
            {
                if (!Match(TokenType.QuestionOp))
                {
                    _current = start;
                    return false;
                }

                op = UpdateOperator.NullishAssign;
                rhs = ParseExpression();
            }
            else if (Match(TokenType.MinOp) || Match(TokenType.MinUnicode))
            {
                op = UpdateOperator.Min;
                rhs = ParseExpression();
            }
            else if (Match(TokenType.MaxOp) || Match(TokenType.MaxUnicode))
            {
                op = UpdateOperator.Max;
                rhs = ParseExpression();
            }
            else if (Match(TokenType.Less))
            {
                op = UpdateOperator.Min;
                rhs = ParseExpression();
            }
            else if (Match(TokenType.Greater))
            {
                op = UpdateOperator.Max;
                rhs = ParseExpression();
            }
            else if (Check(TokenType.Tilde))
            {
                int tildeCount = 0;
                int tildePos = -1;
                while (Match(TokenType.Tilde))
                {
                    if (tildePos < 0) tildePos = Previous().Position;
                    tildeCount++;
                }

                op = tildeCount switch
                {
                    1 => UpdateOperator.Sin,
                    2 => UpdateOperator.Cos,
                    3 => UpdateOperator.Tan,
                    _ => throw new InterpreterException("Too many '~' operators in update.", tildePos)
                };
            }
            else if (Match(TokenType.ClampSymbol) || Match(TokenType.ClampKeyword))
            {
                op = UpdateOperator.Clamp;
                rangeExpr = ParseUpdateRange();
            }
            else if (Match(TokenType.WrapSymbol) || Match(TokenType.WrapKeyword))
            {
                op = UpdateOperator.Wrap;

                if (!Check(TokenType.At) && !IsRangeStart(Peek().Type))
                {
                    rhs = ParseExpression();
                }

                if (Match(TokenType.At))
                {
                    // opcjonalny separator miedzy delta a zakresem
                }

                rangeExpr = ParseUpdateRange();
            }
            else
            {
                _current = start;
                return false;
            }

            bool isDebug = ParseTerminatorIsDebug();
            stmt = new UpdateStatement(target, op.Value, rhs, rangeExpr, runValue, isDebug, target.Position);
            return true;

            RangeExpression ParseUpdateRange()
            {
                if (Match(TokenType.LeftBracket))
                    return ParseRangeLiteral(true, Previous().Position);
                if (Match(TokenType.RightBracket))
                    return ParseRangeLiteral(false, Previous().Position);

                throw new InterpreterException("Expected range literal after update operator.", Peek().Position);
            }

            static bool IsRangeStart(TokenType type) =>
                type == TokenType.LeftBracket || type == TokenType.RightBracket;
        }

        private Expression? TryParseUpdateTarget()
        {
            if (!IsNameTokenType(Peek().Type))
                return null;

            Advance();
            var idTok = Previous();
            string name = TokenToName(idTok);
            Expression expr = new IdentifierExpression(name, idTok.Position);

            while (Match(TokenType.LeftBracket))
            {
                var indexExpr = ParseExpression();
                Consume(TokenType.RightBracket, "Expected ']' after index expression.");
                expr = new IndexExpression(expr, indexExpr, expr.Position);
            }

            return expr;
        }

        private Statement ParseIfStatement(int position)
        {
            bool prevAllow = _allowStatementKeywordsAsArgs;
            _allowStatementKeywordsAsArgs = false;
            var condition = ParseExpression();
            _allowStatementKeywordsAsArgs = prevAllow;

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
            bool prevAllow = _allowStatementKeywordsAsArgs;
            _allowStatementKeywordsAsArgs = false;
            Expression condition = ParseExpression();
            _allowStatementKeywordsAsArgs = prevAllow;

            // podobnie jak w if: body może być pojedynczym statementem albo blokiem
            Statement body = ParseStatement();
            return new WhileStatement(condition, body, position);
        }

        private Expression ParseExpression() => ParseAssignment();

        private Expression ParseAssignment()
        {
            Expression expr = ParseBinaryWithWhitespace();

            if (Match(TokenType.Assign))
            {
                Expression value = ParseAssignment();

                if (expr is IdentifierExpression ident)
                {
                    return new AssignmentExpression(ident.Name, value, ident.Position);
                }

                if (expr is NumberIdentifierExpression numIdent)
                {
                    return new AssignmentExpression(numIdent.Name, value, numIdent.Position);
                }

                if (expr is StringIdentifierExpression strIdent)
                {
                    return new AssignmentExpression(strIdent.Name, value, strIdent.Position);
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

        private Expression ParseBinaryWithWhitespace(int minPrecedence = int.MinValue)
        {
            Expression left = ParseUnary();

            while (true)
            {
                var op = TryPeekBinaryOperator();
                if (op == null || op.Value.Precedence < minPrecedence)
                    break;

                _current += op.Value.TokensToConsume;
                Expression right = ParseBinaryWithWhitespace(op.Value.Precedence + 1);

                Expression built = op.Value.Descriptor.Builder(left, right, op.Value.OperatorToken.Position);
                if (op.Value.Negate)
                    built = new UnaryExpression(UnaryOperator.Not, built, op.Value.OperatorToken.Position);

                left = built;
            }

            return left;
        }

        private BinaryOperatorMatch? TryPeekBinaryOperator()
        {
            if (IsAtEnd())
                return null;

            int opIndex = _current;
            bool negate = false;

            // Prefiksowy ';' nadal neguje operator porownania/rownosci.
            if (Peek().Type == TokenType.Semicolon &&
                _current + 1 < _tokens.Count &&
                BinaryOperatorTable.ContainsKey(_tokens[_current + 1].Type) &&
                IsNegatableOperator(_tokens[_current + 1].Type))
            {
                negate = true;
                opIndex = _current + 1;
            }

            if (!BinaryOperatorTable.TryGetValue(_tokens[opIndex].Type, out var descriptor))
                return null;

            int precedence = ComputeEffectivePrecedence(opIndex, descriptor.BasePrecedence);
            int tokensToConsume = (opIndex - _current) + 1; // 1 lub 2 (gdy jest poprzedzajacy ';')

            return new BinaryOperatorMatch(descriptor, _tokens[opIndex], tokensToConsume, precedence, negate);
        }

        private static bool IsNegatableOperator(TokenType type) =>
            type == TokenType.Equal ||
            type == TokenType.EqualEqual ||
            type == TokenType.EqualEqualEqual ||
            type == TokenType.Less ||
            type == TokenType.LessEqual ||
            type == TokenType.Greater ||
            type == TokenType.GreaterEqual;

        private int ComputeEffectivePrecedence(int operatorIndex, int basePrecedence)
        {
            int leftSpaces = CountSpacesToLeft(operatorIndex);
            int rightSpaces = CountSpacesToRight(operatorIndex);
            int spaceCount = leftSpaces + rightSpaces;

            // Mniej spacji => wieksze binding power; roznica jednej spacji bije bazowy precedens.
            return (-spaceCount * WhitespacePrecedenceWeight) + basePrecedence;
        }

        private int CountSpacesToLeft(int operatorIndex)
        {
            int leftIndex = operatorIndex - 1;
            while (leftIndex >= 0 && _tokens[leftIndex].Type == TokenType.Semicolon)
                leftIndex--;

            if (leftIndex < 0)
                return 0;

            return CountSpacesBetweenTokens(leftIndex, operatorIndex);
        }

        private int CountSpacesToRight(int operatorIndex)
        {
            int rightIndex = operatorIndex + 1;
            while (rightIndex < _tokens.Count && _tokens[rightIndex].Type == TokenType.Semicolon)
                rightIndex++;

            if (rightIndex >= _tokens.Count || _tokens[rightIndex].Type == TokenType.EndOfFile)
                return 0;

            return CountSpacesBetweenTokens(operatorIndex, rightIndex);
        }

        private int CountSpacesBetweenTokens(int leftIndex, int rightIndex)
        {
            int start = leftIndex >= 0
                ? _tokens[leftIndex].Position + _tokens[leftIndex].Lexeme.Length
                : 0;

            int end = _tokens[rightIndex].Position;
            if (end <= start)
                return 0;

            int spaces = 0;
            for (int i = start; i < end && i < _source.Length; i++)
            {
                char c = _source[i];
                if (c == ' ' || c == '\t' || c == '(' || c == ')')
                    spaces++;
            }

            return spaces;
        }

        private bool HasNoRealSpacesBetweenTokens(int leftIndex, int rightIndex)
        {
            int start = leftIndex >= 0
                ? _tokens[leftIndex].Position + _tokens[leftIndex].Lexeme.Length
                : 0;

            int end = _tokens[rightIndex].Position;
            if (end <= start)
                return true;

            for (int i = start; i < end && i < _source.Length; i++)
            {
                char c = _source[i];
                if (c == ' ' || c == '\t')
                    return false;
            }

            return true;
        }

        private Expression ParseEquality()
        {
            Expression expr = ParseComparison();

            while (true)
            {
                // Allow "operator negation" before equality operators, e.g.:
                //   a ;==== b   => ;(a ==== b)
                int negPos = -1;
                if (Check(TokenType.Semicolon) &&
                    (PeekType(1) == TokenType.Equal || PeekType(1) == TokenType.EqualEqual || PeekType(1) == TokenType.EqualEqualEqual))
                {
                    Advance(); // consume ';'
                    negPos = Previous().Position;
                }

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
                    if (negPos >= 0)
                        Fatal(Peek(), "Expected an equality operator after ';'.");
                    break;
                }

                if (negPos >= 0)
                    expr = new UnaryExpression(UnaryOperator.Not, expr, negPos);
            }

            return expr;
        }

        private Expression ParseComparison()
        {
            Expression expr = ParseTerm();

            while (true)
            {
                // Allow "operator negation" before comparison operators, e.g.:
                //   a ;< b   => ;(a < b)
                int negPos = -1;
                if (Check(TokenType.Semicolon) &&
                    (PeekType(1) == TokenType.Less ||
                     PeekType(1) == TokenType.LessEqual ||
                     PeekType(1) == TokenType.Greater ||
                     PeekType(1) == TokenType.GreaterEqual))
                {
                    Advance(); // consume ';'
                    negPos = Previous().Position;
                }

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
                    if (negPos >= 0)
                        Fatal(Peek(), "Expected a comparison operator after ';'.");
                    break;
                }

                if (negPos >= 0)
                    expr = new UnaryExpression(UnaryOperator.Not, expr, negPos);
            }

            return expr;
        }

        //        else
        //        {
        //            break;
        //        }
        //    }

        //    return expr;
        //}

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

        private
Expression ParseFactor()
        {
            Expression expr = ParsePower();

            while (true)
            {
                if (Match(TokenType.Star))
                {
                    int pos = Previous().Position;
                    Expression right = ParsePower();
                    expr = new BinaryExpression(expr, BinaryOperator.Multiply, right, pos);
                }
                else if (Match(TokenType.Slash))
                {
                    int pos = Previous().Position;
                    Expression right = ParsePower();
                    expr = new BinaryExpression(expr, BinaryOperator.Divide, right, pos);
                }
                else
                {
                    break;
                }
            }

            return expr;
        }

        private

Expression ParsePower()
        {
            Expression left = ParseUnary();

            // Infix nth-root: a \\ b  => b-th root of a
            while (Match(TokenType.Root))
            {
                int pos = Previous().Position;
                Expression degreeExpr = ParseUnary();
                left = new RootInfixExpression(left, degreeExpr, pos);
            }

            return left;
        }

        Expression ParseUnary()
        {
            // Root prefix: \x, \\x, \\\x, ...
            int rootCount = 0;
            int rootPos = -1;

            while (Match(TokenType.Root))
            {
                if (rootPos < 0) rootPos = Previous().Position;
                rootCount++;
            }

            if (rootCount > 0)
            {
                // 1x "\\" => degree 2 (sqrt), 2x => degree 3 (cbrt), ...
                int degree = rootCount + 1;
                Expression operand = ParseUnary(); // binds to the right
                return new PrefixRootExpression(operand, degree, rootPos);
            }

            if (Match(TokenType.DoublePipe))
            {
                int pos = Previous().Position;
                Expression operand = ParseUnary();
                return new UnaryExpression(UnaryOperator.Abs, operand, pos);
            }

            int tildeCount = 0;
            int tildePos = -1;
            while (Match(TokenType.Tilde))
            {
                if (tildePos < 0) tildePos = Previous().Position;
                tildeCount++;
            }

            if (tildeCount > 0)
            {
                UnaryOperator op = tildeCount switch
                {
                    1 => UnaryOperator.Sin,
                    2 => UnaryOperator.Cos,
                    3 => UnaryOperator.Tan,
                    _ => throw new InterpreterException("Too many '~' operators in a row. Use up to three.", tildePos)
                };

                Expression operand = ParseUnary();
                return new UnaryExpression(op, operand, tildePos);
            }


            if (Match(TokenType.Semicolon))
            {
                int pos = Previous().Position;
                Expression right = ParseUnary();
                return new UnaryExpression(UnaryOperator.Not, right, pos);
            }

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

            // 1) Najpierw klasyczne postfixy: call / index (i ewentualnie kolejne chainy)
            while (true)
            {
                // Index: arr[expr] - tylko gdy '[' jest przyklejony (brak spacji / tylko nawiasy)
                if (Check(TokenType.LeftBracket) &&
                    HasNoRealSpacesBetweenTokens(_current - 1, _current))
                {
                    Advance(); // consume '['
                    var indexExpr = ParseAssignment();
                    Consume(TokenType.RightBracket, "Expected ']' after index expression.");
                    expr = new IndexExpression(expr, indexExpr, Previous().Position);
                    continue;
                }

                // Call bez nawiasów: foo 1, 2, albo foo(1,2) (nawiasy to whitespace)
                if (TryParseCallArguments(ref expr))
                    continue;

                break;
            }

            bool TryParseCallArguments(ref Expression target)
            {
                if (IsAtEnd())
                    return false;

                var nextType = Peek().Type;

                // jesli nastepny token to operator binarny (albo ';' negujace operator), to NIE jest call
                if (BinaryOperatorTable.ContainsKey(nextType))
                    return false;

                if (nextType == TokenType.Semicolon &&
                    _current + 1 < _tokens.Count &&
                    BinaryOperatorTable.ContainsKey(_tokens[_current + 1].Type))
                    return false;

                bool startsRangeArgument = nextType == TokenType.RightBracket && LooksLikeRangeLiteral(_current + 1);

                if (!IsArgumentStart(nextType) && !startsRangeArgument)
                    return false;

                // '[' bez odstępu po callee to index, nie argument
                if (Peek().Type == TokenType.LeftBracket &&
                    HasNoRealSpacesBetweenTokens(_current - 1, _current))
                    return false;

                var args = new List<Expression>();
                do
                {
                    args.Add(ParseAssignment());
                } while (Match(TokenType.Comma));

                int callPos = args.Count > 0 ? args[0].Position : target.Position;
                target = new CallExpression(target, args, callPos);
                return true;
            }

            bool IsArgumentStart(TokenType type) =>
                (IsNameTokenType(type) && (_allowStatementKeywordsAsArgs || !IsStatementKeywordToken(type))) ||
                type == TokenType.Semicolon ||
                type == TokenType.LeftBracket;


            // postfix power UPDATE: **, ****, ******, ...
            // DreamBerd twist (like ++++): operator is repeated "**" pairs.
            // Exponent = 1 + (starCount / 2).
            // Examples:
            //   x**       => x becomes x^2
            //   x****     => x becomes x^3
            //   x******   => x becomes x^4
            // Returns OLD numeric value (postfix semantics), then writes back the powered value.
            bool IsAssignable(Expression e)
            {
                return e is IdentifierExpression
                    || e is NumberIdentifierExpression
                    || e is StringIdentifierExpression
                    || e is IndexExpression
                    || (e is PostfixUpdateExpression p &&
                        (p.Target is IdentifierExpression || p.Target is NumberIdentifierExpression || p.Target is StringIdentifierExpression || p.Target is IndexExpression));
            }

            while (Match(TokenType.StarRun))
            {
                var tok = Previous();
                int starCount = tok.Lexeme.Length;

                if (starCount < 2)
                    throw new InterpreterException("Invalid '**' operator.", tok.Position);

                if ((starCount & 1) != 0)
                    throw new InterpreterException("Power operator requires an even number of '*' (it's repeated \"**\").", tok.Position);

                int exponent = 1 + (starCount / 2);

                if (!IsAssignable(expr))
                    throw new InterpreterException("Postfix '**' power update requires an assignable target (variable or arr[index]).", tok.Position);

                expr = new PowerStarsExpression(expr, exponent, tok.Position);
            }

            // 2) Potem postfix update chain: pozwalamy mieszać ++ i --
            int delta = 0;
            bool sawAny = false;
            int opPos = -1;

            while (true)
            {
                if (Match(TokenType.PlusPlus))
                {
                    if (!sawAny) opPos = Previous().Position;
                    delta++;
                    sawAny = true;
                    continue;
                }

                if (Match(TokenType.MinusMinus))
                {
                    if (!sawAny) opPos = Previous().Position;
                    delta--;
                    sawAny = true;
                    continue;
                }

                break;
            }

            if (sawAny)
            {
                // Target musi być assignable: ident albo arr[index]
                if (!IsAssignable(expr))
                    throw new InterpreterException("Postfix ++/-- requires an assignable target (variable or arr[index]).", opPos);

                // delta==0 -> no-op, ale nadal legalne (np. x++--)
                if (delta != 0)
                    expr = new PostfixUpdateExpression(expr, delta, opPos);
            }

            return expr;
        }


        private Expression ParsePrimary()
        {
            if (Match(TokenType.Number))
            {
                var t = Previous();
                double value = (double)(t.Literal ?? 0.0);
                return new NumberIdentifierExpression(t.Lexeme, value, t.Position);
            }

            if (Match(TokenType.String))
            {
                var t = Previous();
                string text = t.Literal as string ?? string.Empty;
                if (text.Length == 0)
                    return new StringIdentifierExpression(text, t.Position);
                return new LiteralExpression(Value.FromString(text), t.Position);
            }


            if (IsNameTokenType(Peek().Type))
            {
                if (Peek().Type == TokenType.Identifier)
                {
                    int savedIndex = _current;
                    if (TryParseNumberNameLiteral(out var numValue, out var consumed, out var litPos, out var fallbackToString, out var fallbackStart))
                    {
                        _current += consumed;
                        return new LiteralExpression(Value.FromNumber((double)numValue), litPos);
                    }

                    if (fallbackToString)
                    {
                        int j = fallbackStart;
                        var parts = new List<string>();
                        while (j < _tokens.Count &&
                               _tokens[j].Type != TokenType.Bang &&
                               _tokens[j].Type != TokenType.Question &&
                               _tokens[j].Type != TokenType.EndOfFile)
                        {
                            parts.Add(_tokens[j].Lexeme);
                            j++;
                        }

                        _current = j;
                        string raw = string.Join(" ", parts);
                        return new LiteralExpression(Value.FromString(raw), litPos);
                    }

                    _current = savedIndex;
                }

                Advance();

                var t = Previous();
                string name = TokenToName(t);
                int pos = t.Position;

                // Booleany i undefined jako literaly Dreamberda
                if (t.Type == TokenType.Identifier)
                {
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
                    }
                }

                return new IdentifierExpression(name, pos);
            }

            if (Match(TokenType.LeftParen))
            {
                Expression expr = ParseExpression();
                Consume(TokenType.RightParen, "Expected ')' after expression.");
                return expr;
            }

            if (Match(TokenType.LeftBracket) || Match(TokenType.RightBracket))
            {
                Token open = Previous();
                bool includeLower = open.Type == TokenType.LeftBracket;
                bool looksRange = LooksLikeRangeLiteral(_current);

                if (!includeLower && !looksRange)
                    throw new InterpreterException("Unexpected ']'.", open.Position);

                if (looksRange || !includeLower)
                    return ParseRangeLiteral(includeLower, open.Position);

                int pos = open.Position;
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

        private bool LooksLikeRangeLiteral(int startIndex)
        {
            int depth = 1;
            for (int i = startIndex; i < _tokens.Count; i++)
            {
                var type = _tokens[i].Type;

                if (type == TokenType.RangeDots && depth == 1)
                    return true;

                if (type == TokenType.LeftBracket)
                    depth++;
                else if (type == TokenType.RightBracket)
                {
                    depth--;
                    if (depth == 0)
                        break;
                }
                else if (type == TokenType.Comma && depth == 1)
                {
                    // wyglada jak array, bo mamy wiecej niz jeden element rozdzielony przecinkiem
                    return false;
                }
            }

            return false;
        }

        private RangeExpression ParseRangeLiteral(bool includeLower, int startPosition)
        {
            Expression lower = ParseExpression();
            Consume(TokenType.RangeDots, "Expected '..' in range literal.");
            Expression upper = ParseExpression();

            bool includeUpper;
            if (Match(TokenType.RightBracket))
            {
                includeUpper = true;
            }
            else if (Match(TokenType.LeftBracket))
            {
                includeUpper = false;
            }
            else
            {
                throw new InterpreterException("Expected ']' or '[' to close range literal.", Peek().Position);
            }

            return new RangeExpression(lower, upper, includeLower, includeUpper, startPosition);
        }

        private bool TryParseNumberNameLiteral(out ulong value, out int consumedTokens, out int literalPosition, out bool fallbackToString, out int fallbackStartIndex)
        {
            value = 0;
            consumedTokens = 0;
            literalPosition = Peek().Position;
            fallbackToString = false;
            fallbackStartIndex = _current;

            ulong chunk = 0;
            int i = _current;
            bool sawAny = false;

            while (i < _tokens.Count && _tokens[i].Type == TokenType.Identifier)
            {
                string raw = _tokens[i].Lexeme;
                string word = NormalizeNumberWord(raw);

                // jezeli slowo jest juz zarezerwowane w scope (zmienna/funkcja), to nie robimy z niego liczby
                if (IsNameShadowed(word))
                    return false;

                if (string.Equals(word, "and", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(word, "i", StringComparison.OrdinalIgnoreCase))
                {
                    if (!sawAny)
                        return false; // samo 'and'/'i' nie rozpoczyna liczby
                    i++;
                    consumedTokens++;
                    continue;
                }

                if (NumberUnits.TryGetValue(word, out var unit))
                {
                    checked { chunk += unit; }
                    i++;
                    consumedTokens++;
                    sawAny = true;
                    continue;
                }

                if (NumberTens.TryGetValue(word, out var tens))
                {
                    checked { chunk += tens; }
                    i++;
                    consumedTokens++;
                    sawAny = true;
                    continue;
                }

                if (string.Equals(word, "hundred", StringComparison.OrdinalIgnoreCase))
                {
                    if (chunk == 0)
                        return false;

                    checked { chunk *= 100; }
                    i++;
                    consumedTokens++;
                    sawAny = true;
                    continue;
                }

                if (NumberScales.TryGetValue(word, out var scale))
                {
                    if (chunk == 0)
                        return false;

                    checked
                    {
                        chunk *= scale;
                        value += chunk;
                    }
                    chunk = 0;
                    i++;
                    consumedTokens++;
                    sawAny = true;
                    continue;
                }

                // Nieznane slowo: jesli juz bylismy w srodku liczby, to przechodzimy na tryb fallback (string).
                if (sawAny)
                {
                    fallbackToString = true;
                    return false;
                }

                return false; // na startowym slowie: to nie jest liczba
            }

            checked { value += chunk; }
            return consumedTokens > 0;
        }

        private string NormalizeNumberWord(string raw)
        {
            string w = raw;

            if (NumberAliases.TryGetValue(w, out var alias))
                w = alias;

            // "thousandS", "millionS" itd. - zdejmujemy pojedyncze 's' z konca
            if (w.Length > 1 && w.EndsWith("s", StringComparison.OrdinalIgnoreCase))
            {
                string trimmed = w.Substring(0, w.Length - 1);
                if (NumberUnits.ContainsKey(trimmed) || NumberTens.ContainsKey(trimmed) || NumberScales.ContainsKey(trimmed) || string.Equals(trimmed, "hundred", StringComparison.OrdinalIgnoreCase))
                    w = trimmed;
            }

            return w;
        }
    }
}
