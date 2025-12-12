// Evaluator.cs
using System;
using System.Collections.Generic;
using System.Globalization;

namespace DreamberdInterpreter
{
    public sealed class Evaluator
    {
        private readonly VariableStore _variables;
        private readonly IConstConstConstStore _constStore;

        private readonly HashSet<double> _deletedNumbers = new();
        private readonly HashSet<string> _deletedStrings = new();
        private readonly HashSet<BooleanState> _deletedBooleans = new();

        private int _currentStatementIndex;

        // Głębokość zagnieżdżenia pętli while.
        // Używamy tego do walidacji break/continue (poza pętlą = błąd).
        private int _loopDepth;

        private sealed class WhenSubscription
        {
            public Expression Condition
            {
                get;
            }
            public Statement Body
            {
                get;
            }
            public IReadOnlyCollection<string> Dependencies
            {
                get;
            }

            public WhenSubscription(Expression condition, Statement body, IReadOnlyCollection<string> dependencies)
            {
                Condition = condition ?? throw new ArgumentNullException(nameof(condition));
                Body = body ?? throw new ArgumentNullException(nameof(body));
                Dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
            }
        }

        private sealed class FunctionDefinition
        {
            public IReadOnlyList<string> Parameters
            {
                get;
            }
            public Statement Body
            {
                get;
            }

            public FunctionDefinition(IReadOnlyList<string> parameters, Statement body)
            {
                Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
                Body = body ?? throw new ArgumentNullException(nameof(body));
            }
        }

        /// <summary>
        /// Wewnętrzny "sygnał" return z funkcji.
        /// Dzięki temu return może przerwać wykonanie dowolnie zagnieżdżonego bloku.
        /// </summary>
        private sealed class ReturnSignal : Exception
        {
            public Value Value
            {
                get;
            }

            public ReturnSignal(Value value)
            {
                Value = value;
            }
        }

        /// <summary>
        /// Wewnętrzny sygnał break (wyjście z najbliższej pętli while).
        /// </summary>
        private sealed class BreakSignal : Exception
        {
        }

        /// <summary>
        /// Wewnętrzny sygnał continue (następna iteracja najbliższej pętli while).
        /// </summary>
        private sealed class ContinueSignal : Exception
        {
        }

        private sealed class CallFrame
        {
            public Dictionary<string, Value> Locals
            {
                get;
            } =
                new Dictionary<string, Value>(StringComparer.Ordinal);
        }

        private const string WhenWildcard = "*";

        // when(sub.Condition) -> uruchamiaj Body tylko po mutacji zmiennych, które występują w Condition
        // (plus wildcard '*' dla warunków bez żadnych zmiennych).
        private readonly Dictionary<string, List<WhenSubscription>> _whenByVariable =
            new Dictionary<string, List<WhenSubscription>>(StringComparer.Ordinal);

        // Dispatch `when` nie może być rekurencyjny (body może mutować zmienne), więc robimy kolejkę zdarzeń.
        private readonly Queue<string> _whenMutationQueue = new Queue<string>();
        private bool _dispatchingWhen;
        private readonly Dictionary<string, FunctionDefinition> _functions =
            new Dictionary<string, FunctionDefinition>(StringComparer.Ordinal);

        private readonly Stack<CallFrame> _callStack =
            new Stack<CallFrame>();

        public Evaluator(VariableStore variables, IConstConstConstStore constStore)
        {
            _variables = variables ?? throw new ArgumentNullException(nameof(variables));
            _constStore = constStore ?? throw new ArgumentNullException(nameof(constStore));
        }

        public void ExecuteProgram(IReadOnlyList<Statement> statements)
        {
            if (statements == null)
                throw new ArgumentNullException(nameof(statements));

            int index = 0;
            int direction = 1;
            _currentStatementIndex = 0;

            while (index >= 0 && index < statements.Count)
            {
                _currentStatementIndex = index;

                _variables.ExpireLifetimes(_currentStatementIndex, DateTime.UtcNow);

                var statement = statements[index];

                if (statement is ReverseStatement reverseStatement)
                {
                    if (reverseStatement.IsDebug)
                        Console.WriteLine("[DEBUG] reverse!");

                    direction = -direction;
                    index += direction;
                    continue;
                }

                if (statement is ForwardStatement forwardStatement)
                {
                    if (forwardStatement.IsDebug)
                        Console.WriteLine("[DEBUG] forward!");

                    direction = 1;
                    index += direction;
                    continue;
                }

                EvaluateStatement(statement);
                index += direction;
            }
        }

        /// <summary>
        /// Wykonuje listę statementów (np. blok { ... }).
        ///
        /// Na tym etapie reverse/forward działa lokalnie w obrębie tej listy.
        /// Scope'y blokowe będą dodane później – tutaj to jest tylko grupowanie.
        /// </summary>
        private void ExecuteStatementList(IReadOnlyList<Statement> statements)
        {
            if (statements == null)
                throw new ArgumentNullException(nameof(statements));

            int savedIndex = _currentStatementIndex;

            int index = 0;
            int direction = 1;

            try
            {
                while (index >= 0 && index < statements.Count)
                {
                    _currentStatementIndex = index;

                    _variables.ExpireLifetimes(_currentStatementIndex, DateTime.UtcNow);

                    var statement = statements[index];

                    if (statement is ReverseStatement reverseStatement)
                    {
                        if (reverseStatement.IsDebug)
                            Console.WriteLine("[DEBUG] reverse!");

                        direction = -direction;
                        index += direction;
                        continue;
                    }

                    if (statement is ForwardStatement forwardStatement)
                    {
                        if (forwardStatement.IsDebug)
                            Console.WriteLine("[DEBUG] forward!");

                        direction = 1;
                        index += direction;
                        continue;
                    }

                    EvaluateStatement(statement);
                    index += direction;
                }
            }
            finally
            {
                _currentStatementIndex = savedIndex;
            }
        }

        private Value EvaluateStatement(Statement statement)
        {
            switch (statement)
            {
                case BlockStatement bs:
                    {
                        // Blok { ... } tworzy nowy scope zmiennych.
                        _variables.PushScope();
                        try
                        {
                            ExecuteStatementList(bs.Statements);
                        }
                        finally
                        {
                            _variables.PopScope();
                        }

                        return Value.Null;
                    }

                case VariableDeclarationStatement vds:
                    {
                        Value value = EvaluateExpression(vds.Initializer);

                        if (vds.DeclarationKind == DeclarationKind.ConstConstConst)
                        {
                            _constStore.Define(vds.Name, value);
                        }
                        else
                        {
                            _variables.Declare(
                                vds.Name,
                                vds.Mutability,
                                value,
                                vds.Priority,
                                vds.Lifetime,
                                _currentStatementIndex);
                        }
                        OnVariableMutated(vds.Name);

                        return Value.Null;
                    }

                case ExpressionStatement es:
                    {
                        Value value = EvaluateExpression(es.Expression);
                        if (es.IsDebug)
                        {
                            DebugPrint(es.Expression, value);
                        }
                        return value;
                    }

                case DeleteStatement ds:
                    {
                        Value value = EvaluateExpression(ds.Target);
                        MarkDeleted(value, ds.Position);
                        if (ds.IsDebug)
                        {
                            Console.WriteLine("[DEBUG] delete {0}", value);
                        }
                        return Value.Null;
                    }

                case WhenStatement ws:
                    {
                        var deps = CollectConditionDependencies(ws.Condition);
                        var sub = new WhenSubscription(ws.Condition, ws.Body, deps);
                        RegisterWhenSubscription(sub);
                        return Value.Null;
                    }

                case ReturnStatement rs:
                    {
                        // return jest dozwolony tylko wewnątrz wywołania funkcji.
                        if (_callStack.Count == 0)
                            throw new InterpreterException("return can only be used inside a function.", rs.Position);

                        Value value = rs.Expression != null
                            ? EvaluateExpression(rs.Expression)
                            : Value.Undefined;

                        throw new ReturnSignal(value);
                    }

                case FunctionDeclarationStatement fds:
                    {
                        var def = new FunctionDefinition(fds.Parameters, fds.Body);
                        _functions[fds.Name] = def;
                        return Value.Null;
                    }

                case IfStatement ifs:
                    {
                        Value cond = EvaluateExpression(ifs.Condition);
                        if (cond.IsTruthy())
                        {
                            return EvaluateStatement(ifs.ThenBranch);
                        }
                        else if (ifs.ElseBranch != null)
                        {
                            return EvaluateStatement(ifs.ElseBranch);
                        }
                        return Value.Null;
                    }

                case WhileStatement ws:
                    {
                        _loopDepth++;
                        try
                        {
                            while (EvaluateExpression(ws.Condition).IsTruthy())
                            {
                                try
                                {
                                    EvaluateStatement(ws.Body);
                                }
                                catch (ContinueSignal)
                                {
                                    // pomijamy resztę ciała i zaczynamy kolejną iterację
                                    continue;
                                }
                                catch (BreakSignal)
                                {
                                    // wychodzimy z pętli
                                    break;
                                }
                            }
                        }
                        finally
                        {
                            _loopDepth--;
                        }

                        return Value.Null;
                    }

                case BreakStatement bs:
                    {
                        if (_loopDepth <= 0)
                            throw new InterpreterException("break can only be used inside a while loop.", bs.Position);
                        throw new BreakSignal();
                    }

                case ContinueStatement cs:
                    {
                        if (_loopDepth <= 0)
                            throw new InterpreterException("continue can only be used inside a while loop.", cs.Position);
                        throw new ContinueSignal();
                    }

                case ReverseStatement _:
                case ForwardStatement _:
                    return Value.Null;

                default:
                    throw new InterpreterException($"Unknown statement type: {statement.GetType().Name}.", statement.Position);
            }
        }

        private void DebugPrint(Expression expr, Value value)
        {
            if (expr is IdentifierExpression id &&
                _variables.TryGetHistory(id.Name, out var hist, out var currentIndex) &&
                hist.Count > 0)
            {
                var parts = new List<string>();
                for (int i = 0; i < hist.Count; i++)
                    parts.Add(hist[i].ToString());

                string joined = string.Join(", ", parts);
                Console.WriteLine(
                    $"history({id.Name}): [{joined}] (current index = {currentIndex}, value = {hist[currentIndex]})");
            }
            else
            {
                Console.WriteLine("[DEBUG] {0}", value);
            }
        }

        private Value EvaluateExpression(Expression expression)
        {
            Value result;

            switch (expression)
            {
                case LiteralExpression lit:
                    result = lit.Value;
                    break;

                case IdentifierExpression ident:
                    result = EvaluateIdentifier(ident);
                    break;

                case UnaryExpression unary:
                    result = EvaluateUnary(unary);
                    break;

                case BinaryExpression binary:
                    result = EvaluateBinary(binary);
                    break;

                case AssignmentExpression assign:
                    result = EvaluateAssignment(assign);
                    break;

                case IndexAssignmentExpression idxAssign:
                    result = EvaluateIndexAssignment(idxAssign);
                    break;

                case CallExpression call:
                    result = EvaluateCall(call);
                    break;

                case ArrayLiteralExpression arrLit:
                    result = EvaluateArrayLiteral(arrLit);
                    break;

                case IndexExpression indexExpr:
                    result = EvaluateIndex(indexExpr);
                    break;

                case ConditionalExpression condExpr:
                    result = EvaluateConditional(condExpr);
                    break;

                default:
                    throw new InterpreterException($"Unknown expression type: {expression.GetType().Name}.", expression.Position);
            }

            return CheckDeleted(result, expression.Position);
        }

        private Value EvaluateIdentifier(IdentifierExpression ident)
        {
            // najpierw zmienne lokalne funkcji
            if (_callStack.Count > 0)
            {
                var frame = _callStack.Peek();
                if (frame.Locals.TryGetValue(ident.Name, out var localValue))
                    return localValue;
            }

            // const const const
            if (_constStore.TryGet(ident.Name, out var globalValue))
                return globalValue;

            // globalne zmienne w VariableStore
            if (_variables.TryGet(ident.Name, out var varValue))
                return varValue;

            // fallback: string z nazwą
            return Value.FromString(ident.Name);
        }

        private static double ToNumberAt(Value value, int position)
        {
            try
            {
                return value.ToNumber();
            }
            catch (InterpreterException ex) when (ex.Position is null)
            {
                // Value.ToNumber() nie ma informacji o pozycji, więc podpinamy ją tutaj.
                throw new InterpreterException(ex.Message, position);
            }
        }


        private Value EvaluateUnary(UnaryExpression unary)
        {
            Value operand = EvaluateExpression(unary.Operand);

            return unary.Operator switch
            {
                UnaryOperator.Negate => Value.FromNumber(-ToNumberAt(operand, unary.Operand.Position)),
                _ => throw new InterpreterException($"Unsupported unary operator {unary.Operator}.", unary.Position)
            };
        }

        private Value EvaluateBinary(BinaryExpression binary)
        {
            Value left = EvaluateExpression(binary.Left);
            Value right = EvaluateExpression(binary.Right);

            switch (binary.Operator)
            {
                case BinaryOperator.Add:
                    return EvaluateAdd(left, right);

                case BinaryOperator.Subtract:
                    return Value.FromNumber(ToNumberAt(left, binary.Left.Position) - ToNumberAt(right, binary.Right.Position));

                case BinaryOperator.Multiply:
                    return Value.FromNumber(ToNumberAt(left, binary.Left.Position) * ToNumberAt(right, binary.Right.Position));

                case BinaryOperator.Divide:
                    return EvaluateDivide(left, right, binary.Left.Position, binary.Right.Position);

                case BinaryOperator.Equal:
                    return Value.FromBoolean(left.VeryLooseEquals(right));

                case BinaryOperator.DoubleEqual:
                    return Value.FromBoolean(left.LooseEquals(right));

                case BinaryOperator.TripleEqual:
                    return Value.FromBoolean(left.StrictEquals(right));

                case BinaryOperator.Less:
                    return Value.FromBoolean(ToNumberAt(left, binary.Left.Position) < ToNumberAt(right, binary.Right.Position));

                case BinaryOperator.Greater:
                    return Value.FromBoolean(ToNumberAt(left, binary.Left.Position) > ToNumberAt(right, binary.Right.Position));

                case BinaryOperator.LessOrEqual:
                    return Value.FromBoolean(ToNumberAt(left, binary.Left.Position) <= ToNumberAt(right, binary.Right.Position));

                case BinaryOperator.GreaterOrEqual:
                    return Value.FromBoolean(ToNumberAt(left, binary.Left.Position) >= ToNumberAt(right, binary.Right.Position));

                default:
                    throw new InterpreterException($"Unsupported binary operator {binary.Operator}.", binary.Position);
            }
        }

        private static Value EvaluateAdd(Value left, Value right)
        {
            if (left.Kind == ValueKind.Number && right.Kind == ValueKind.Number)
            {
                return Value.FromNumber(left.Number + right.Number);
            }

            return Value.FromString(left.ToString() + right.ToString());
        }

        private Value EvaluateDivide(Value left, Value right, int leftPosition, int rightPosition)
        {
            double divisor = ToNumberAt(right, rightPosition);
            if (Math.Abs(divisor) < double.Epsilon)
            {
                return Value.Undefined;
            }

            return Value.FromNumber(ToNumberAt(left, leftPosition) / divisor);
        }

        private Value EvaluateConditional(ConditionalExpression condExpr)
        {
            Value cond = EvaluateExpression(condExpr.Condition);

            // rozróżniamy true / false / maybe / undefined
            if (cond.Kind == ValueKind.Boolean)
            {
                return cond.Bool switch
                {
                    BooleanState.True => EvaluateExpression(condExpr.WhenTrue),
                    BooleanState.False => EvaluateExpression(condExpr.WhenFalse),
                    BooleanState.Maybe => EvaluateExpression(condExpr.WhenMaybe),
                    _ => EvaluateExpression(condExpr.WhenFalse)
                };
            }

            if (cond.Kind == ValueKind.Undefined)
            {
                return EvaluateExpression(condExpr.WhenUndefined);
            }

            // inne typy -> na podstawie IsTruthy -> true/false
            if (cond.IsTruthy())
            {
                return EvaluateExpression(condExpr.WhenTrue);
            }
            else
            {
                return EvaluateExpression(condExpr.WhenFalse);
            }
        }

        private Value EvaluateAssignment(AssignmentExpression assign)
        {
            if (_constStore.TryGet(assign.Name, out _))
                throw new InterpreterException($"Cannot assign to const const const variable '{assign.Name}'.", assign.Position);

            Value value = EvaluateExpression(assign.ValueExpression);

            // jeśli jest w ramce funkcji, przypisujemy lokalnie
            if (_callStack.Count > 0)
            {
                var frame = _callStack.Peek();
                if (frame.Locals.ContainsKey(assign.Name))
                {
                    frame.Locals[assign.Name] = value;
                    OnVariableMutated(assign.Name);
                    return value;
                }
            }

            // globalny VariableStore
            _variables.Assign(assign.Name, value, _currentStatementIndex);
            OnVariableMutated(assign.Name);
            return value;
        }

        private Value EvaluateIndexAssignment(IndexAssignmentExpression ia)
        {
            Value targetVal = EvaluateExpression(ia.Target);

            if (targetVal.Kind != ValueKind.Array || targetVal.Array == null)
                throw new InterpreterException("Index assignment is only supported on arrays.", ia.Position);

            Value indexVal = EvaluateExpression(ia.Index);
            double index = ToNumberAt(indexVal, ia.Index.Position);

            var dict = new Dictionary<double, Value>(targetVal.Array);
            Value newValue = EvaluateExpression(ia.ValueExpression);
            dict[index] = newValue;

            if (ia.Target is IdentifierExpression ident &&
                !_constStore.TryGet(ident.Name, out _))
            {
                Value newArrayValue = Value.FromArray(dict);
                _variables.Assign(ident.Name, newArrayValue, _currentStatementIndex);
                OnVariableMutated(ident.Name);
            }

            return newValue;
        }


        private void RegisterWhenSubscription(WhenSubscription sub)
        {
            foreach (var dep in sub.Dependencies)
            {
                if (!_whenByVariable.TryGetValue(dep, out var list))
                {
                    list = new List<WhenSubscription>();
                    _whenByVariable[dep] = list;
                }

                list.Add(sub);
            }
        }

        private IReadOnlyCollection<string> CollectConditionDependencies(Expression condition)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            CollectDependencies(condition, set, isCallee: false);

            // Jeśli warunek nie odwołuje się do żadnej zmiennej, zachowujemy stary vibe:
            // when(true) odpala się po każdej mutacji.
            if (set.Count == 0)
                set.Add(WhenWildcard);

            return set;
        }

        private void CollectDependencies(Expression expr, HashSet<string> deps, bool isCallee)
        {
            switch (expr)
            {
                case null:
                    return;

                case IdentifierExpression id:
                    // Jeżeli to jest nazwa funkcji (callee), to nie traktujemy tego jako zależności od zmiennej.
                    if (!isCallee)
                        deps.Add(id.Name);
                    return;

                case LiteralExpression:
                    return;

                case UnaryExpression u:
                    CollectDependencies(u.Operand, deps, isCallee: false);
                    return;

                case BinaryExpression b:
                    CollectDependencies(b.Left, deps, isCallee: false);
                    CollectDependencies(b.Right, deps, isCallee: false);
                    return;

                case AssignmentExpression a:
                    deps.Add(a.Name);
                    CollectDependencies(a.ValueExpression, deps, isCallee: false);
                    return;

                case IndexAssignmentExpression ia:
                    CollectDependencies(ia.Target, deps, isCallee: false);
                    CollectDependencies(ia.Index, deps, isCallee: false);
                    CollectDependencies(ia.ValueExpression, deps, isCallee: false);
                    return;

                case IndexExpression ix:
                    CollectDependencies(ix.Target, deps, isCallee: false);
                    CollectDependencies(ix.Index, deps, isCallee: false);
                    return;

                case ArrayLiteralExpression al:
                    foreach (var el in al.Elements)
                        CollectDependencies(el, deps, isCallee: false);
                    return;

                case ConditionalExpression ce:
                    CollectDependencies(ce.Condition, deps, isCallee: false);
                    CollectDependencies(ce.WhenTrue, deps, isCallee: false);
                    CollectDependencies(ce.WhenFalse, deps, isCallee: false);
                    CollectDependencies(ce.WhenMaybe, deps, isCallee: false);
                    CollectDependencies(ce.WhenUndefined, deps, isCallee: false);
                    return;

                case CallExpression call:
                    // Nie traktujemy nazwy funkcji jako zależności od zmiennej.
                    if (call.Callee is not IdentifierExpression)
                        CollectDependencies(call.Callee, deps, isCallee: true);

                    foreach (var arg in call.Arguments)
                        CollectDependencies(arg, deps, isCallee: false);
                    return;

                default:
                    // Jeśli dojdą nowe expressiony w przyszłości, a zapomnimy je tu dodać,
                    // to po prostu nie będą wpływać na zależności `when`.
                    return;
            }
        }

        private void OnVariableMutated(string variableName)
        {
            if (string.IsNullOrEmpty(variableName))
                return;

            if (_whenByVariable.Count == 0)
                return;

            _whenMutationQueue.Enqueue(variableName);

            // jeśli już dispatchujemy, kolejka zostanie opróżniona po powrocie
            if (_dispatchingWhen)
                return;

            _dispatchingWhen = true;
            try
            {
                int steps = 0;

                while (_whenMutationQueue.Count > 0)
                {
                    if (++steps > 100_000)
                        throw new InterpreterException("when dispatch exceeded safety limit (possible infinite loop).");

                    string mutated = _whenMutationQueue.Dequeue();

                    // Zbieramy subskrypcje dla konkretnej zmiennej + wildcard '*'.
                    var toRun = new HashSet<WhenSubscription>();

                    if (_whenByVariable.TryGetValue(mutated, out var specific))
                    {
                        foreach (var sub in specific)
                            toRun.Add(sub);
                    }

                    if (_whenByVariable.TryGetValue(WhenWildcard, out var any))
                    {
                        foreach (var sub in any)
                            toRun.Add(sub);
                    }

                    foreach (var sub in toRun)
                    {
                        Value condVal = EvaluateExpression(sub.Condition);
                        if (condVal.IsTruthy())
                        {
                            EvaluateStatement(sub.Body);
                        }
                    }
                }
            }
            finally
            {
                _dispatchingWhen = false;
                _whenMutationQueue.Clear();
            }
        }

        private Value EvaluateCall(CallExpression call)
        {
            if (call.Callee is IdentifierExpression ident)
            {
                var name = ident.Name;

                // built-iny
                if (string.Equals(name, "print", StringComparison.Ordinal))
                {
                    foreach (var argExpr in call.Arguments)
                    {
                        Value v = EvaluateExpression(argExpr);
                        Console.WriteLine(v.ToString());
                    }

                    return Value.Null;
                }

                if (string.Equals(name, "readFile", StringComparison.Ordinal))
                {
                    return EvaluateReadFileCall(call);
                }

                if (string.Equals(name, "readLines", StringComparison.Ordinal))
                {
                    return EvaluateReadLinesCall(call);
                }

                if (string.Equals(name, "lines", StringComparison.Ordinal))
                {
                    return EvaluateLinesCall(call);
                }

                if (string.Equals(name, "trim", StringComparison.Ordinal))
                {
                    return EvaluateTrimCall(call);
                }

                if (string.Equals(name, "split", StringComparison.Ordinal))
                {
                    return EvaluateSplitCall(call);
                }

                if (string.Equals(name, "charAt", StringComparison.Ordinal))
                {
                    return EvaluateCharAtCall(call);
                }

                if (string.Equals(name, "slice", StringComparison.Ordinal))
                {
                    return EvaluateSliceCall(call);
                }

                if (string.Equals(name, "toNumber", StringComparison.Ordinal) ||
                    string.Equals(name, "parseInt", StringComparison.Ordinal) ||
                    string.Equals(name, "parseNumber", StringComparison.Ordinal))
                {
                    return EvaluateToNumberCall(call);
                }

                if (string.Equals(name, "previous", StringComparison.Ordinal))
                {
                    return EvaluatePreviousCall(call);
                }

                if (string.Equals(name, "next", StringComparison.Ordinal))
                {
                    return EvaluateNextCall(call);
                }

                if (string.Equals(name, "history", StringComparison.Ordinal))
                {
                    return EvaluateHistoryCall(call);
                }

                // funkcja użytkownika
                if (_functions.TryGetValue(name, out var funcDef))
                {
                    return InvokeUserFunction(name, funcDef, call.Arguments);
                }
            }

            throw new InterpreterException(
                "Only the built-in functions print(...), previous(...), next(...), history(...), " +
                "and user-defined functions are supported at this time.");
        }

        // ------------------------------------------------------------
        // Built-in helpers (stdlib-ish) implemented in C#
        // ------------------------------------------------------------

        private Value EvaluateReadFileCall(CallExpression call)
        {
            if (call.Arguments.Count != 1)
                throw new InterpreterException("readFile(path) expects exactly one argument.", call.Position);

            Value pathVal = EvaluateExpression(call.Arguments[0]);
            if (pathVal.Kind != ValueKind.String)
                throw new InterpreterException("readFile(path) expects a string path.", call.Arguments[0].Position);

            string path = pathVal.String ?? string.Empty;

            try
            {
                string text = File.ReadAllText(path);
                return Value.FromString(text);
            }
            catch (Exception ex)
            {
                throw new InterpreterException($"readFile(path) failed: {ex.Message}", call.Position);
            }
        }

        private Value EvaluateReadLinesCall(CallExpression call)
        {
            if (call.Arguments.Count != 1)
                throw new InterpreterException("readLines(path) expects exactly one argument.", call.Position);

            Value pathVal = EvaluateExpression(call.Arguments[0]);
            if (pathVal.Kind != ValueKind.String)
                throw new InterpreterException("readLines(path) expects a string path.", call.Arguments[0].Position);

            string path = pathVal.String ?? string.Empty;

            try
            {
                // ReadAllText + our own splitting keeps behaviour consistent with lines(text)
                string text = File.ReadAllText(path);
                var items = SplitLines(text);
                return MakeStringArray(items);
            }
            catch (Exception ex)
            {
                throw new InterpreterException($"readLines(path) failed: {ex.Message}", call.Position);
            }
        }

        private Value EvaluateLinesCall(CallExpression call)
        {
            if (call.Arguments.Count != 1)
                throw new InterpreterException("lines(text) expects exactly one argument.", call.Position);

            Value textVal = EvaluateExpression(call.Arguments[0]);
            if (textVal.Kind != ValueKind.String)
                throw new InterpreterException("lines(text) expects a string.", call.Arguments[0].Position);

            var items = SplitLines(textVal.String ?? string.Empty);
            return MakeStringArray(items);
        }

        private Value EvaluateTrimCall(CallExpression call)
        {
            if (call.Arguments.Count != 1)
                throw new InterpreterException("trim(text) expects exactly one argument.", call.Position);

            Value sVal = EvaluateExpression(call.Arguments[0]);
            if (sVal.Kind != ValueKind.String)
                throw new InterpreterException("trim(text) expects a string.", call.Arguments[0].Position);

            return Value.FromString((sVal.String ?? string.Empty).Trim());
        }

        private Value EvaluateSplitCall(CallExpression call)
        {
            if (call.Arguments.Count != 2)
                throw new InterpreterException("split(text, sep) expects exactly two arguments.", call.Position);

            Value sVal = EvaluateExpression(call.Arguments[0]);
            Value sepVal = EvaluateExpression(call.Arguments[1]);

            if (sVal.Kind != ValueKind.String)
                throw new InterpreterException("split(text, sep) expects text as a string.", call.Arguments[0].Position);
            if (sepVal.Kind != ValueKind.String)
                throw new InterpreterException("split(text, sep) expects sep as a string.", call.Arguments[1].Position);

            string s = sVal.String ?? string.Empty;
            string sep = sepVal.String ?? string.Empty;

            List<string> parts;

            if (sep.Length == 0)
            {
                parts = new List<string>(s.Length);
                foreach (char c in s)
                    parts.Add(c.ToString());
            }
            else
            {
                var arr = s.Split(new[] { sep }, StringSplitOptions.None);
                parts = new List<string>(arr.Length);
                parts.AddRange(arr);
            }

            return MakeStringArray(parts);
        }

        private Value EvaluateCharAtCall(CallExpression call)
        {
            if (call.Arguments.Count != 2)
                throw new InterpreterException("charAt(text, index) expects exactly two arguments.", call.Position);

            Value sVal = EvaluateExpression(call.Arguments[0]);
            if (sVal.Kind != ValueKind.String)
                throw new InterpreterException("charAt(text, index) expects text as a string.", call.Arguments[0].Position);

            Value idxVal = EvaluateExpression(call.Arguments[1]);
            if (!TryToInt(idxVal, out int index))
                return Value.Undefined;

            string s = sVal.String ?? string.Empty;
            if (index < 0 || index >= s.Length)
                return Value.Undefined;

            return Value.FromString(s[index].ToString());
        }

        private Value EvaluateSliceCall(CallExpression call)
        {
            if (call.Arguments.Count != 2)
                throw new InterpreterException("slice(text, start) expects exactly two arguments.", call.Position);

            Value sVal = EvaluateExpression(call.Arguments[0]);
            if (sVal.Kind != ValueKind.String)
                throw new InterpreterException("slice(text, start) expects text as a string.", call.Arguments[0].Position);

            Value startVal = EvaluateExpression(call.Arguments[1]);
            if (!TryToInt(startVal, out int start))
                return Value.Undefined;

            string s = sVal.String ?? string.Empty;

            // allow negative start (count from end), like in many languages
            if (start < 0)
                start = s.Length + start;

            if (start < 0)
                start = 0;

            if (start >= s.Length)
                return Value.FromString(string.Empty);

            return Value.FromString(s.Substring(start));
        }

        private Value EvaluateToNumberCall(CallExpression call)
        {
            if (call.Arguments.Count != 1)
                throw new InterpreterException("toNumber(x) expects exactly one argument.", call.Position);

            Value x = EvaluateExpression(call.Arguments[0]);

            switch (x.Kind)
            {
                case ValueKind.Number:
                    return x;

                case ValueKind.Boolean:
                    return x.Bool switch
                    {
                        BooleanState.False => Value.FromNumber(0),
                        BooleanState.True => Value.FromNumber(1),
                        BooleanState.Maybe => Value.FromNumber(0.5),
                        _ => Value.Undefined
                    };

                case ValueKind.String:
                    if (double.TryParse(x.String ?? string.Empty, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                        return Value.FromNumber(d);
                    return Value.Undefined;

                case ValueKind.Null:
                case ValueKind.Undefined:
                    return Value.Undefined;

                default:
                    return Value.Undefined;
            }
        }

        private static bool TryToInt(Value v, out int i)
        {
            i = 0;

            double d;
            try
            {
                d = v.Kind switch
                {
                    ValueKind.Number => v.Number,
                    ValueKind.Boolean => v.Bool switch
                    {
                        BooleanState.False => 0,
                        BooleanState.True => 1,
                        BooleanState.Maybe => 0.5,
                        _ => double.NaN
                    },
                    ValueKind.String => double.TryParse(v.String ?? string.Empty, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : double.NaN,
                    _ => double.NaN
                };
            }
            catch
            {
                return false;
            }

            if (double.IsNaN(d) || double.IsInfinity(d))
                return false;

            i = (int)Math.Truncate(d);
            return true;
        }

        private static List<string> SplitLines(string text)
        {
            // normalize: support \r\n, \n, and \r
            string norm = text.Replace("\r\n", "\n").Replace('\r', '\n');

            var raw = norm.Split('\n');

            int count = raw.Length;
            if (count > 0 && raw[count - 1].Length == 0)
                count--;

            var list = new List<string>(count);
            for (int i = 0; i < count; i++)
                list.Add(raw[i]);

            return list;
        }

        private static Value MakeStringArray(List<string> items)
        {
            var dict = new Dictionary<double, Value>(items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                dict[i - 1] = Value.FromString(items[i]);
            }

            return Value.FromArray(dict);
        }



        private Value EvaluatePreviousCall(CallExpression call)
        {
            if (call.Arguments.Count != 1 || call.Arguments[0] is not IdentifierExpression id)
                throw new InterpreterException("previous(x) expects a single identifier argument.", call.Position);

            if (!_variables.TryPrevious(id.Name, out var newVal, out var changed))
                return Value.Undefined;

            if (changed)
                OnVariableMutated(id.Name);

            return newVal;
        }

        private Value EvaluateNextCall(CallExpression call)
        {
            if (call.Arguments.Count != 1 || call.Arguments[0] is not IdentifierExpression id)
                throw new InterpreterException("next(x) expects a single identifier argument.", call.Position);

            if (!_variables.TryNext(id.Name, out var newVal, out var changed))
                return Value.Undefined;

            if (changed)
                OnVariableMutated(id.Name);

            return newVal;
        }

        private Value EvaluateHistoryCall(CallExpression call)
        {
            if (call.Arguments.Count != 1 || call.Arguments[0] is not IdentifierExpression id)
                throw new InterpreterException("history(x) expects a single identifier argument.", call.Position);

            if (!_variables.TryGetHistory(id.Name, out var values, out var currentIndex) ||
                values.Count == 0)
            {
                return Value.FromArray(new Dictionary<double, Value>());
            }

            var dict = new Dictionary<double, Value>();
            for (int i = 0; i < values.Count; i++)
            {
                double idx = i - 1;       // -1, 0, 1, 2...
                dict[idx] = values[i];
            }

            return Value.FromArray(dict);
        }

        private Value InvokeUserFunction(
            string name,
            FunctionDefinition def,
            IReadOnlyList<Expression> arguments)
        {
            var frame = new CallFrame();

            int paramCount = def.Parameters.Count;
            for (int i = 0; i < paramCount; i++)
            {
                string paramName = def.Parameters[i];

                Value argValue = i < arguments.Count
                    ? EvaluateExpression(arguments[i])
                    : Value.Undefined;

                frame.Locals[paramName] = argValue;
            }

            _callStack.Push(frame);
            try
            {
                try
                {
                    // Wykonujemy ciało funkcji (może być ReturnStatement albo blok { ... }).
                    // Jeśli nie ma return, zwracamy undefined.
                    EvaluateStatement(def.Body);
                    return Value.Undefined;
                }
                catch (ReturnSignal rs)
                {
                    return rs.Value;
                }
            }
            finally
            {
                _callStack.Pop();
            }
        }

        private Value EvaluateArrayLiteral(ArrayLiteralExpression arrLit)
        {
            var dict = new Dictionary<double, Value>();

            for (int i = 0; i < arrLit.Elements.Count; i++)
            {
                Value elementValue = EvaluateExpression(arrLit.Elements[i]);
                double index = i - 1;
                dict[index] = elementValue;
            }

            return Value.FromArray(dict);
        }

        private Value EvaluateIndex(IndexExpression indexExpr)
        {
            Value targetVal = EvaluateExpression(indexExpr.Target);

            if (targetVal.Kind != ValueKind.Array || targetVal.Array == null)
                throw new InterpreterException("Indexing is only supported on arrays.", indexExpr.Position);

            Value indexVal = EvaluateExpression(indexExpr.Index);
            double index = ToNumberAt(indexVal, indexExpr.Index.Position);

            if (!targetVal.Array.TryGetValue(index, out var element))
            {
                return Value.Undefined;
            }

            return element;
        }

        private void MarkDeleted(Value value, int position)
        {
            switch (value.Kind)
            {
                case ValueKind.Number:
                    _deletedNumbers.Add(value.Number);
                    break;

                case ValueKind.String:
                    if (value.String != null)
                        _deletedStrings.Add(value.String);
                    break;

                case ValueKind.Boolean:
                    _deletedBooleans.Add(value.Bool);
                    break;

                default:
                    throw new InterpreterException("delete only works with primitive values (numbers, strings, booleans).", position);
            }
        }

        private Value CheckDeleted(Value value, int position)
        {
            switch (value.Kind)
            {
                case ValueKind.Number:
                    if (_deletedNumbers.Contains(value.Number))
                        throw new InterpreterException($"Value '{value}' has been deleted.", position);
                    break;

                case ValueKind.String:
                    if (value.String != null && _deletedStrings.Contains(value.String))
                        throw new InterpreterException($"Value '{value}' has been deleted.", position);
                    break;

                case ValueKind.Boolean:
                    if (_deletedBooleans.Contains(value.Bool))
                        throw new InterpreterException($"Value '{value}' has been deleted.", position);
                    break;
            }

            return value;
        }
    }
}
