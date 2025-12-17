// Evaluator.cs
using System;
using System.Collections.Generic;
using System.Globalization;

namespace DreamberdInterpreter
{
    public sealed partial class Evaluator
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

        // Głębokość zagnieżdżenia if/else/idk.
        // Używamy tego do walidacji 'try again' (poza if-em = błąd).
        private int _ifDepth;

        private const string WhenWildcard = "*";

        // when(sub.Condition) -> uruchamiaj Body tylko po mutacji zmiennych, które występują w Condition
        // (plus wildcard '*' dla warunków bez żadnych zmiennych).
        private readonly Dictionary<string, List<WhenSubscription>> _whenByVariable = new Dictionary<string, List<WhenSubscription>>(StringComparer.Ordinal);

        // Dispatch `when` nie może być rekurencyjny (body może mutować zmienne), więc robimy kolejkę zdarzeń.
        private readonly Queue<string> _whenMutationQueue = new Queue<string>();
        private bool _dispatchingWhen;
        private readonly Dictionary<string, FunctionDefinition> _functions = new Dictionary<string, FunctionDefinition>(StringComparer.Ordinal);

        private readonly Stack<CallFrame> _callStack = new Stack<CallFrame>();

        public Evaluator(VariableStore variables, IConstConstConstStore constStore)
        {
            _variables = variables ?? throw new ArgumentNullException(nameof(variables));
            _constStore = constStore ?? throw new ArgumentNullException(nameof(constStore));
            CurrentDirectory = System.Environment.CurrentDirectory;
            RegisterStdLibDefaultMethods();
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

        private void CollectDependencies(Expression? expr, HashSet<string> deps, bool isCallee)
        {
            if (expr == null)
            {
                return;
            }
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

        private Value InvokeUserFunction(string name, FunctionDefinition def, IReadOnlyList<Expression> arguments)
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

        private Value EvaluatePostfixUpdate(PostfixUpdateExpression update)
        {
            // Postfix semantics: return the OLD numeric value, then apply the mutation.
            // Supports:
            //   - x++ / x-- (incl. x++++ etc. via update.Delta)
            //   - arr[i]++ / arr[i]-- (only when arr is an identifier)

            //if (update.Delta == 0)
            //    throw new InterpreterException("Invalid postfix update (delta = 0).", update.Position);

            // Identifier target: x++ / x--
            if (update.Target is IdentifierExpression id)
            {
                if (_constStore.TryGet(id.Name, out _))
                    throw new InterpreterException($"Cannot update const const const variable '{id.Name}'.", update.Position);

                bool isLocal = false;
                Value current;

                if (_callStack.Count > 0 && _callStack.Peek().Locals.TryGetValue(id.Name, out var localVal))
                {
                    isLocal = true;
                    current = localVal;
                }
                else
                {
                    if (!_variables.TryGet(id.Name, out current))
                        throw new InterpreterException($"Variable '{id.Name}' is not defined.", update.Position);
                }

                double oldNum = ToNumberAt(current, update.Position);
                double newNum = oldNum + update.Delta;

                var newValue = Value.FromNumber(newNum);

                if (isLocal)
                {
                    _callStack.Peek().Locals[id.Name] = newValue;
                }
                else
                {
                    _variables.Assign(id.Name, newValue, _currentStatementIndex);
                }

                OnVariableMutated(id.Name);
                return Value.FromNumber(oldNum);
            }

            // Index target: arr[i]++ / arr[i]--
            if (update.Target is IndexExpression ix)
            {
                // To be assignable, the array base must be an identifier (same rule as IndexAssignmentExpression).
                if (ix.Target is not IdentifierExpression arrId)
                    throw new InterpreterException("Postfix ++/-- on indexing is only supported for identifier arrays (e.g. arr[i]++).", update.Position);

                if (_constStore.TryGet(arrId.Name, out _))
                    throw new InterpreterException($"Cannot update const const const variable '{arrId.Name}'.", update.Position);

                bool isLocal = false;
                Value arrayValue;

                if (_callStack.Count > 0 && _callStack.Peek().Locals.TryGetValue(arrId.Name, out var localArr))
                {
                    isLocal = true;
                    arrayValue = localArr;
                }
                else
                {
                    if (!_variables.TryGet(arrId.Name, out arrayValue))
                        throw new InterpreterException($"Variable '{arrId.Name}' is not defined.", update.Position);
                }

                if (arrayValue.Kind != ValueKind.Array || arrayValue.Array == null)
                    throw new InterpreterException("Indexing update is only supported on arrays.", update.Position);

                Value indexVal = EvaluateExpression(ix.Index);
                double index = ToNumberAt(indexVal, ix.Index.Position);

                // Read old element (or Undefined -> ToNumberAt will throw, which is fine).
                if (!arrayValue.Array.TryGetValue(index, out var elem))
                    elem = Value.Undefined;

                double oldNum = ToNumberAt(elem, update.Position);
                double newNum = oldNum + update.Delta;

                var dict = new Dictionary<double, Value>(arrayValue.Array);
                dict[index] = Value.FromNumber(newNum);

                Value newArrayValue = Value.FromArray(dict);

                if (isLocal)
                {
                    _callStack.Peek().Locals[arrId.Name] = newArrayValue;
                }
                else
                {
                    _variables.Assign(arrId.Name, newArrayValue, _currentStatementIndex);
                }

                OnVariableMutated(arrId.Name);
                return Value.FromNumber(oldNum);
            }

            throw new InterpreterException("Postfix ++/-- target must be an identifier or an array index.", update.Position);
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
