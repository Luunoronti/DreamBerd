// Evaluator.cs
using System;
using System.Collections.Generic;

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

            public WhenSubscription(Expression condition, Statement body)
            {
                Condition = condition;
                Body = body;
            }
        }

        private sealed class FunctionDefinition
        {
            public IReadOnlyList<string> Parameters
            {
                get;
            }
            public Expression Body
            {
                get;
            }

            public FunctionDefinition(IReadOnlyList<string> parameters, Expression body)
            {
                Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
                Body = body ?? throw new ArgumentNullException(nameof(body));
            }
        }

        private sealed class CallFrame
        {
            public Dictionary<string, Value> Locals
            {
                get;
            } =
                new Dictionary<string, Value>(StringComparer.Ordinal);
        }

        private readonly List<WhenSubscription> _whenSubscriptions = new();

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
                        ExecuteStatementList(bs.Statements);
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
                        MarkDeleted(value);
                        if (ds.IsDebug)
                        {
                            Console.WriteLine("[DEBUG] delete {0}", value);
                        }
                        return Value.Null;
                    }

                case WhenStatement ws:
                    {
                        _whenSubscriptions.Add(new WhenSubscription(ws.Condition, ws.Body));
                        return Value.Null;
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

                case ReverseStatement _:
                case ForwardStatement _:
                    return Value.Null;

                default:
                    throw new InterpreterException($"Unknown statement type: {statement.GetType().Name}.");
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
                    throw new InterpreterException($"Unknown expression type: {expression.GetType().Name}.");
            }

            return CheckDeleted(result);
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

        private Value EvaluateUnary(UnaryExpression unary)
        {
            Value operand = EvaluateExpression(unary.Operand);

            return unary.Operator switch
            {
                UnaryOperator.Negate => Value.FromNumber(-operand.ToNumber()),
                _ => throw new InterpreterException($"Unsupported unary operator {unary.Operator}.")
            };
        }

        private Value EvaluateBinary(BinaryExpression binary)
        {
            Value left = EvaluateExpression(binary.Left);
            Value right = EvaluateExpression(binary.Right);

            return binary.Operator switch
            {
                BinaryOperator.Add => EvaluateAdd(left, right),
                BinaryOperator.Subtract => Value.FromNumber(left.ToNumber() - right.ToNumber()),
                BinaryOperator.Multiply => Value.FromNumber(left.ToNumber() * right.ToNumber()),
                BinaryOperator.Divide => EvaluateDivide(left, right),
                BinaryOperator.Equal => Value.FromBoolean(left.VeryLooseEquals(right)),
                BinaryOperator.DoubleEqual => Value.FromBoolean(left.LooseEquals(right)),
                BinaryOperator.TripleEqual => Value.FromBoolean(left.StrictEquals(right)),
                BinaryOperator.Less => Value.FromBoolean(left.ToNumber() < right.ToNumber()),
                BinaryOperator.Greater => Value.FromBoolean(left.ToNumber() > right.ToNumber()),
                BinaryOperator.LessOrEqual => Value.FromBoolean(left.ToNumber() <= right.ToNumber()),
                BinaryOperator.GreaterOrEqual => Value.FromBoolean(left.ToNumber() >= right.ToNumber()),
                _ => throw new InterpreterException($"Unsupported binary operator {binary.Operator}.")
            };
        }

        private static Value EvaluateAdd(Value left, Value right)
        {
            if (left.Kind == ValueKind.Number && right.Kind == ValueKind.Number)
            {
                return Value.FromNumber(left.Number + right.Number);
            }

            return Value.FromString(left.ToString() + right.ToString());
        }

        private static Value EvaluateDivide(Value left, Value right)
        {
            double divisor = right.ToNumber();
            if (Math.Abs(divisor) < double.Epsilon)
            {
                return Value.Undefined;
            }

            return Value.FromNumber(left.ToNumber() / divisor);
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
                throw new InterpreterException($"Cannot assign to const const const variable '{assign.Name}'.");

            Value value = EvaluateExpression(assign.ValueExpression);

            // jeśli jest w ramce funkcji, przypisujemy lokalnie
            if (_callStack.Count > 0)
            {
                var frame = _callStack.Peek();
                if (frame.Locals.ContainsKey(assign.Name))
                {
                    frame.Locals[assign.Name] = value;
                    return value;
                }
            }

            // globalny VariableStore
            _variables.Assign(assign.Name, value, _currentStatementIndex);
            OnVariableMutated();
            return value;
        }

        private Value EvaluateIndexAssignment(IndexAssignmentExpression ia)
        {
            Value targetVal = EvaluateExpression(ia.Target);

            if (targetVal.Kind != ValueKind.Array || targetVal.Array == null)
                throw new InterpreterException("Index assignment is only supported on arrays.");

            Value indexVal = EvaluateExpression(ia.Index);
            double index = indexVal.ToNumber();

            var dict = new Dictionary<double, Value>(targetVal.Array);
            Value newValue = EvaluateExpression(ia.ValueExpression);
            dict[index] = newValue;

            if (ia.Target is IdentifierExpression ident &&
                !_constStore.TryGet(ident.Name, out _))
            {
                Value newArrayValue = Value.FromArray(dict);
                _variables.Assign(ident.Name, newArrayValue, _currentStatementIndex);
                OnVariableMutated();
            }

            return newValue;
        }

        private void OnVariableMutated()
        {
            if (_whenSubscriptions.Count == 0)
                return;

            var snapshot = _whenSubscriptions.ToArray();
            foreach (var sub in snapshot)
            {
                Value condVal = EvaluateExpression(sub.Condition);
                if (condVal.IsTruthy())
                {
                    EvaluateStatement(sub.Body);
                }
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

        private Value EvaluatePreviousCall(CallExpression call)
        {
            if (call.Arguments.Count != 1 || call.Arguments[0] is not IdentifierExpression id)
                throw new InterpreterException("previous(x) expects a single identifier argument.");

            if (!_variables.TryPrevious(id.Name, out var newVal, out var changed))
                return Value.Undefined;

            if (changed)
                OnVariableMutated();

            return newVal;
        }

        private Value EvaluateNextCall(CallExpression call)
        {
            if (call.Arguments.Count != 1 || call.Arguments[0] is not IdentifierExpression id)
                throw new InterpreterException("next(x) expects a single identifier argument.");

            if (!_variables.TryNext(id.Name, out var newVal, out var changed))
                return Value.Undefined;

            if (changed)
                OnVariableMutated();

            return newVal;
        }

        private Value EvaluateHistoryCall(CallExpression call)
        {
            if (call.Arguments.Count != 1 || call.Arguments[0] is not IdentifierExpression id)
                throw new InterpreterException("history(x) expects a single identifier argument.");

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
                Value result = EvaluateExpression(def.Body);
                return result;
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
                throw new InterpreterException("Indexing is only supported on arrays.");

            Value indexVal = EvaluateExpression(indexExpr.Index);
            double index = indexVal.ToNumber();

            if (!targetVal.Array.TryGetValue(index, out var element))
            {
                return Value.Undefined;
            }

            return element;
        }

        private void MarkDeleted(Value value)
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
                    throw new InterpreterException("delete only works with primitive values (numbers, strings, booleans).");
            }
        }

        private Value CheckDeleted(Value value)
        {
            switch (value.Kind)
            {
                case ValueKind.Number:
                    if (_deletedNumbers.Contains(value.Number))
                        throw new InterpreterException($"Value '{value}' has been deleted.");
                    break;

                case ValueKind.String:
                    if (value.String != null && _deletedStrings.Contains(value.String))
                        throw new InterpreterException($"Value '{value}' has been deleted.");
                    break;

                case ValueKind.Boolean:
                    if (_deletedBooleans.Contains(value.Bool))
                        throw new InterpreterException($"Value '{value}' has been deleted.");
                    break;
            }

            return value;
        }
    }
}
