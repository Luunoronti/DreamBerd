// Evaluator.cs
using System;
using System.Collections.Generic;
using System.Globalization;

namespace DreamberdInterpreter
{
    public sealed class Evaluator
    {
        private readonly Context _context;
        private readonly IConstConstConstStore _constConstConstStore;

        private readonly HashSet<double> _deletedNumbers = new();
        private readonly HashSet<string> _deletedStrings = new();
        private readonly HashSet<bool> _deletedBooleans = new();

        private readonly Dictionary<string, LifetimeInfo> _lifetimes =
            new Dictionary<string, LifetimeInfo>(StringComparer.Ordinal);

        private int _currentStatementIndex;
        private readonly DateTime _startTimeUtc = DateTime.UtcNow;

        // ----- WHEN subscriptions -----

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

        private readonly List<WhenSubscription> _whenSubscriptions = new();

        // ----- Lifetimes -----

        private readonly struct LifetimeInfo
        {
            public LifetimeSpecifier Lifetime
            {
                get;
            }
            public int DeclarationIndex
            {
                get;
            }
            public DateTime CreatedAtUtc
            {
                get;
            }

            public LifetimeInfo(LifetimeSpecifier lifetime, int declarationIndex, DateTime createdAtUtc)
            {
                Lifetime = lifetime;
                DeclarationIndex = declarationIndex;
                CreatedAtUtc = createdAtUtc;
            }
        }

        // ----- Historia zmiennych dla previous/next/history -----

        private sealed class VariableHistory
        {
            public List<Value> Values { get; } = new();
            public int Index { get; set; } = -1;
        }

        private const int MaxHistory = 100;

        private readonly Dictionary<string, VariableHistory> _history =
            new Dictionary<string, VariableHistory>(StringComparer.Ordinal);

        public Evaluator(Context context, IConstConstConstStore constConstConstStore)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _constConstConstStore = constConstConstStore ?? throw new ArgumentNullException(nameof(constConstConstStore));
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
                ExpireLifetimes();

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

        private void ExpireLifetimes()
        {
            if (_lifetimes.Count == 0)
                return;

            var toRemove = new List<string>();

            foreach (var pair in _lifetimes)
            {
                string name = pair.Key;
                var info = pair.Value;
                var lifetime = info.Lifetime;

                switch (lifetime.Kind)
                {
                    case LifetimeKind.Lines:
                        {
                            if (lifetime.Value > 0)
                            {
                                int lines = (int)lifetime.Value;
                                int lastIndex = info.DeclarationIndex + lines - 1;
                                if (_currentStatementIndex > lastIndex)
                                    toRemove.Add(name);
                            }

                            break;
                        }

                    case LifetimeKind.Seconds:
                        {
                            if (lifetime.Value > 0)
                            {
                                double secs = lifetime.Value;
                                double elapsed = (DateTime.UtcNow - info.CreatedAtUtc).TotalSeconds;
                                if (elapsed > secs)
                                    toRemove.Add(name);
                            }

                            break;
                        }

                    case LifetimeKind.Infinity:
                    case LifetimeKind.None:
                    default:
                        break;
                }
            }

            foreach (var name in toRemove)
            {
                _lifetimes.Remove(name);
                _context.DeleteVariable(name);
                _history.Remove(name); // usuwamy też historię
            }
        }

        private Value EvaluateStatement(Statement statement)
        {
            switch (statement)
            {
                case VariableDeclarationStatement vds:
                    {
                        var value = EvaluateExpression(vds.Initializer);

                        if (vds.DeclarationKind == DeclarationKind.ConstConstConst)
                        {
                            _constConstConstStore.Define(vds.Name, value);
                        }
                        else
                        {
                            _context.DeclareVariable(vds.Name, vds.Mutability, value, vds.Priority);

                            if (!vds.Lifetime.IsNone)
                            {
                                _lifetimes[vds.Name] = new LifetimeInfo(
                                    vds.Lifetime,
                                    _currentStatementIndex,
                                    DateTime.UtcNow);
                            }

                            InitHistoryForVariable(vds.Name, value);
                        }

                        return Value.Null;
                    }

                case ExpressionStatement es:
                    {
                        var value = EvaluateExpression(es.Expression);
                        if (es.IsDebug)
                        {
                            DebugPrint(es.Expression, value);
                        }
                        return value;
                    }

                case ReverseStatement rs:
                    {
                        if (rs.IsDebug)
                            Console.WriteLine("[DEBUG] reverse!");
                        return Value.Null;
                    }

                case ForwardStatement fs:
                    {
                        if (fs.IsDebug)
                            Console.WriteLine("[DEBUG] forward!");
                        return Value.Null;
                    }

                case DeleteStatement ds:
                    {
                        var value = EvaluateExpression(ds.Target);
                        MarkDeleted(value);
                        if (ds.IsDebug)
                            Console.WriteLine("[DEBUG] delete {0}", value);
                        return Value.Null;
                    }

                case WhenStatement ws:
                    {
                        _whenSubscriptions.Add(new WhenSubscription(ws.Condition, ws.Body));
                        return Value.Null;
                    }

                default:
                    throw new InterpreterException($"Unknown statement type: {statement.GetType().Name}.");
            }
        }

        private void InitHistoryForVariable(string name, Value initial)
        {
            if (!_history.TryGetValue(name, out var hist))
            {
                hist = new VariableHistory();
                _history[name] = hist;
            }

            hist.Values.Clear();
            hist.Values.Add(initial);
            hist.Index = 0;
        }

        private void UpdateHistoryAfterWrite(string name, Value newValue)
        {
            if (!_history.TryGetValue(name, out var hist))
            {
                hist = new VariableHistory();
                _history[name] = hist;
            }

            if (hist.Values.Count == 0)
            {
                hist.Values.Add(newValue);
                hist.Index = 0;
                return;
            }

            var current = hist.Values[hist.Index];
            if (EqualsStrict(current, newValue))
                return; // nic się nie zmieniło

            // jeżeli jesteśmy w środku historii – obcinamy przyszłość
            if (hist.Index < hist.Values.Count - 1)
            {
                hist.Values.RemoveRange(hist.Index + 1, hist.Values.Count - hist.Index - 1);
            }

            hist.Values.Add(newValue);
            hist.Index = hist.Values.Count - 1;

            if (hist.Values.Count > MaxHistory)
            {
                hist.Values.RemoveAt(0);
                hist.Index--;
                if (hist.Index < 0) hist.Index = 0;
            }
        }

        private void DebugPrint(Expression expr, Value value)
        {
            // x?  → wypisz historię x, jeśli ją mamy
            if (expr is IdentifierExpression id &&
                _history.TryGetValue(id.Name, out var hist) &&
                hist.Values.Count > 0)
            {
                var parts = new List<string>();
                for (int i = 0; i < hist.Values.Count; i++)
                {
                    parts.Add(hist.Values[i].ToString());
                }

                string joined = string.Join(", ", parts);
                Console.WriteLine(
                    $"history({id.Name}): [{joined}] (current index = {hist.Index}, value = {hist.Values[hist.Index]})");
            }
            else
            {
                // cokolwiek innego? wypisz wartość jak klasyczny debug
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

                default:
                    throw new InterpreterException($"Unknown expression type: {expression.GetType().Name}.");
            }

            return CheckDeleted(result);
        }

        private Value EvaluateIdentifier(IdentifierExpression ident)
        {
            if (_constConstConstStore.TryGet(ident.Name, out var globalValue))
                return globalValue;

            if (_context.TryGetVariable(ident.Name, out var localValue))
                return localValue;

            return Value.FromString(ident.Name);
        }

        private Value EvaluateUnary(UnaryExpression unary)
        {
            Value operand = EvaluateExpression(unary.Operand);

            return unary.Operator switch
            {
                UnaryOperator.Negate => Value.FromNumber(-AsNumber(operand)),
                _ => throw new InterpreterException($"Unsupported unary operator {unary.Operator}.")
            };
        }

        private Value EvaluateBinary(BinaryExpression binary)
        {
            Value left = EvaluateExpression(binary.Left);
            Value right = EvaluateExpression(binary.Right);

            Value result;

            switch (binary.Operator)
            {
                case BinaryOperator.Add:
                    if (left.Kind == ValueKind.Number && right.Kind == ValueKind.Number)
                    {
                        result = Value.FromNumber(left.Number + right.Number);
                    }
                    else
                    {
                        result = Value.FromString(left.ToString() + right.ToString());
                    }
                    break;

                case BinaryOperator.Subtract:
                    result = Value.FromNumber(AsNumber(left) - AsNumber(right));
                    break;

                case BinaryOperator.Multiply:
                    result = Value.FromNumber(AsNumber(left) * AsNumber(right));
                    break;

                case BinaryOperator.Divide:
                    double divisor = AsNumber(right);
                    if (Math.Abs(divisor) < double.Epsilon)
                    {
                        result = Value.Undefined;
                    }
                    else
                    {
                        result = Value.FromNumber(AsNumber(left) / divisor);
                    }
                    break;

                case BinaryOperator.Equal:
                    result = Value.FromBoolean(EqualsVeryLoose(left, right));
                    break;

                case BinaryOperator.DoubleEqual:
                    result = Value.FromBoolean(EqualsLoose(left, right));
                    break;

                case BinaryOperator.TripleEqual:
                    result = Value.FromBoolean(EqualsStrict(left, right));
                    break;

                case BinaryOperator.QuadEqual:
                    result = Value.FromBoolean(EqualsVeryStrict(left, right));
                    break;

                case BinaryOperator.Less:
                    result = Value.FromBoolean(AsNumber(left) < AsNumber(right));
                    break;

                case BinaryOperator.Greater:
                    result = Value.FromBoolean(AsNumber(left) > AsNumber(right));
                    break;

                case BinaryOperator.LessOrEqual:
                    result = Value.FromBoolean(AsNumber(left) <= AsNumber(right));
                    break;

                case BinaryOperator.GreaterOrEqual:
                    result = Value.FromBoolean(AsNumber(left) >= AsNumber(right));
                    break;

                default:
                    throw new InterpreterException($"Unsupported binary operator {binary.Operator}.");
            }

            return result;
        }

        private Value EvaluateAssignment(AssignmentExpression assign)
        {
            if (_constConstConstStore.TryGet(assign.Name, out _))
                throw new InterpreterException($"Cannot assign to const const const variable '{assign.Name}'.");

            Value value = EvaluateExpression(assign.ValueExpression);
            _context.AssignVariable(assign.Name, value);

            UpdateHistoryAfterWrite(assign.Name, value);
            OnVariableMutated();
            return value;
        }

        private Value EvaluateIndexAssignment(IndexAssignmentExpression ia)
        {
            var targetVal = EvaluateExpression(ia.Target);

            if (targetVal.Kind != ValueKind.Array || targetVal.Array == null)
                throw new InterpreterException("Index assignment is only supported on arrays.");

            var indexVal = EvaluateExpression(ia.Index);
            double index = AsNumber(indexVal);

            var dict = new Dictionary<double, Value>(targetVal.Array);
            var newValue = EvaluateExpression(ia.ValueExpression);
            dict[index] = newValue;

            if (ia.Target is IdentifierExpression ident &&
                !_constConstConstStore.TryGet(ident.Name, out _))
            {
                var newArrayValue = Value.FromArray(dict);
                _context.AssignVariable(ident.Name, newArrayValue);
                UpdateHistoryAfterWrite(ident.Name, newArrayValue);
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
                var condVal = EvaluateExpression(sub.Condition);
                if (IsTruthy(condVal))
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
            }

            throw new InterpreterException("Only the built-in functions print(...), previous(...), next(...), history(...) are supported at this time.");
        }

        private Value EvaluatePreviousCall(CallExpression call)
        {
            if (call.Arguments.Count != 1 || call.Arguments[0] is not IdentifierExpression id)
                throw new InterpreterException("previous(x) expects a single identifier argument.");

            var varName = id.Name;

            if (!_history.TryGetValue(varName, out var hist) || hist.Values.Count == 0)
                return Value.Undefined;

            if (hist.Index <= 0)
            {
                // nic nie zmieniamy, ale zwracamy aktualną
                return hist.Values[hist.Index];
            }

            hist.Index--;
            var newVal = hist.Values[hist.Index];

            _context.AssignVariable(varName, newVal);
            OnVariableMutated();
            return newVal;
        }

        private Value EvaluateNextCall(CallExpression call)
        {
            if (call.Arguments.Count != 1 || call.Arguments[0] is not IdentifierExpression id)
                throw new InterpreterException("next(x) expects a single identifier argument.");

            var varName = id.Name;

            if (!_history.TryGetValue(varName, out var hist) || hist.Values.Count == 0)
                return Value.Undefined;

            if (hist.Index >= hist.Values.Count - 1)
            {
                // nic nie zmieniamy
                return hist.Values[hist.Index];
            }

            hist.Index++;
            var newVal = hist.Values[hist.Index];

            _context.AssignVariable(varName, newVal);
            OnVariableMutated();
            return newVal;
        }

        private Value EvaluateHistoryCall(CallExpression call)
        {
            if (call.Arguments.Count != 1 || call.Arguments[0] is not IdentifierExpression id)
                throw new InterpreterException("history(x) expects a single identifier argument.");

            var varName = id.Name;

            if (!_history.TryGetValue(varName, out var hist) || hist.Values.Count == 0)
            {
                // zwracamy pustą tablicę
                return Value.FromArray(new Dictionary<double, Value>());
            }

            var dict = new Dictionary<double, Value>();
            // robimy tablicę od -1, 0, 1, ... (tak jak literal [a,b,c])
            for (int i = 0; i < hist.Values.Count; i++)
            {
                double idx = i - 1;
                dict[idx] = hist.Values[i];
            }

            return Value.FromArray(dict);
        }

        private Value EvaluateArrayLiteral(ArrayLiteralExpression arrLit)
        {
            var dict = new Dictionary<double, Value>();

            for (int i = 0; i < arrLit.Elements.Count; i++)
            {
                var elementValue = EvaluateExpression(arrLit.Elements[i]);
                double index = i - 1; // -1, 0, 1, ...
                dict[index] = elementValue;
            }

            return Value.FromArray(dict);
        }

        private Value EvaluateIndex(IndexExpression indexExpr)
        {
            var targetVal = EvaluateExpression(indexExpr.Target);

            if (targetVal.Kind != ValueKind.Array || targetVal.Array == null)
                throw new InterpreterException("Indexing is only supported on arrays.");

            var indexVal = EvaluateExpression(indexExpr.Index);
            double index = AsNumber(indexVal);

            if (!targetVal.Array.TryGetValue(index, out var element))
            {
                return Value.Undefined;
            }

            return element;
        }

        private static double AsNumber(Value value)
        {
            if (value.Kind == ValueKind.Number)
                return value.Number;

            if (value.Kind == ValueKind.Boolean)
                return value.Boolean ? 1.0 : 0.0;

            if (value.Kind == ValueKind.String)
            {
                if (double.TryParse(value.String, NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
                    return n;
            }

            throw new InterpreterException($"Cannot convert value '{value}' to number.");
        }

        private static bool EqualsVeryLoose(Value a, Value b)
        {
            return string.Equals(a.ToString(), b.ToString(), StringComparison.Ordinal);
        }

        private static bool EqualsLoose(Value a, Value b)
        {
            if (a.Kind == b.Kind)
            {
                return EqualsStrict(a, b);
            }

            try
            {
                return Math.Abs(AsNumber(a) - AsNumber(b)) < 1e-9;
            }
            catch
            {
                return false;
            }
        }

        private static bool EqualsStrict(Value a, Value b)
        {
            if (a.Kind != b.Kind)
                return false;

            return a.Kind switch
            {
                ValueKind.Number => Math.Abs(a.Number - b.Number) < 1e-9,
                ValueKind.String => string.Equals(a.String, b.String, StringComparison.Ordinal),
                ValueKind.Boolean => a.Boolean == b.Boolean,
                ValueKind.Null => true,
                ValueKind.Undefined => true,
                ValueKind.Array => ReferenceEquals(a.Array, b.Array),
                _ => false
            };
        }

        private static bool EqualsVeryStrict(Value a, Value b)
        {
            if (!EqualsStrict(a, b))
                return false;

            if (a.Kind == ValueKind.Number)
            {
                string sa = a.Number.ToString("R", CultureInfo.InvariantCulture);
                string sb = b.Number.ToString("R", CultureInfo.InvariantCulture);
                return string.Equals(sa, sb, StringComparison.Ordinal);
            }

            return true;
        }

        private void MarkDeleted(Value value)
        {
            switch (value.Kind)
            {
                case ValueKind.Number:
                    _deletedNumbers.Add(value.Number);
                    break;
                case ValueKind.String:
                    _deletedStrings.Add(value.String);
                    break;
                case ValueKind.Boolean:
                    _deletedBooleans.Add(value.Boolean);
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
                    if (_deletedStrings.Contains(value.String))
                        throw new InterpreterException($"Value '{value}' has been deleted.");
                    break;
                case ValueKind.Boolean:
                    if (_deletedBooleans.Contains(value.Boolean))
                        throw new InterpreterException($"Value '{value}' has been deleted.");
                    break;
            }

            return value;
        }

        private static bool IsTruthy(Value value)
        {
            return value.Kind switch
            {
                ValueKind.Boolean => value.Boolean,
                ValueKind.Null => false,
                ValueKind.Undefined => false,
                ValueKind.Number => Math.Abs(value.Number) > double.Epsilon,
                ValueKind.String => !string.IsNullOrEmpty(value.String),
                ValueKind.Array => value.Array != null && value.Array.Count > 0,
                _ => false
            };
        }
    }
}
