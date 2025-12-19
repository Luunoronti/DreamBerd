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
        private readonly Dictionary<string, ClassDefinition> _classes = new Dictionary<string, ClassDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, ClassInstance> _classInstances = new Dictionary<string, ClassInstance>(StringComparer.Ordinal);
        private readonly Dictionary<string, FieldHistory> _fieldHistory = new Dictionary<string, FieldHistory>(StringComparer.Ordinal);
        private readonly Dictionary<string, FieldHistory> _staticFieldHistory = new Dictionary<string, FieldHistory>(StringComparer.Ordinal);

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

        private void ClearFieldHistoryForClass(string className)
        {
            if (string.IsNullOrEmpty(className))
                return;

            var toRemove = new List<string>();
            foreach (var key in _fieldHistory.Keys)
            {
                if (key.StartsWith(className + "::", StringComparison.Ordinal))
                    toRemove.Add(key);
            }

            foreach (var k in toRemove)
                _fieldHistory.Remove(k);

            toRemove.Clear();
            foreach (var key in _staticFieldHistory.Keys)
            {
                if (key.StartsWith(className + "::", StringComparison.Ordinal))
                    toRemove.Add(key);
            }

            foreach (var k in toRemove)
                _staticFieldHistory.Remove(k);
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

        private Value InvokeUserFunction(string name, FunctionDefinition def, IReadOnlyList<Expression> arguments, BoundMethod? boundThis = null)
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

            if (boundThis != null)
            {
                frame.Locals["source"] = Value.FromObject(boundThis.Target);
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

        private Value InvokeBoundMethod(BoundMethod method, IReadOnlyList<Expression> arguments)
        {
            return InvokeUserFunction(method.Name, method.Definition, arguments, method);
        }

        private ClassInstance GetOrCreateInstance(ClassDefinition def)
        {
            if (_classInstances.TryGetValue(def.Name, out var existing))
                return existing;

            var instance = new ClassInstance(def);
            InitializeInstanceProperties(instance);
            _classInstances[def.Name] = instance;
            InvokeConstructorIfNeeded(instance);
            return instance;
        }

        private void InitializeInstanceProperties(ClassInstance instance)
        {
            foreach (var prop in instance.Definition.Properties)
            {
                if (prop.IsStatic)
                    continue;

                Value init = prop.Initializer != null ? EvaluateExpression(prop.Initializer) : Value.Undefined;

                if (!instance.Fields.ContainsKey(prop.Name))
                {
                    var hist = GetOrCreateFieldHistory(instance, prop.Name);
                    if (hist.Values.Count == 0)
                    {
                        hist.Values.Add(Value.Undefined);
                        hist.Index = 0;
                    }
                    if (!hist.Values[^1].StrictEquals(init))
                    {
                        hist.Values.Add(init);
                        hist.Index = hist.Values.Count - 1;
                    }

                    instance.Fields[prop.Name] = init;
                }
            }
        }

        private void InvokeConstructorIfNeeded(ClassInstance instance)
        {
            if (instance.Initialized)
                return;

            instance.Initialized = true;

            if (instance.Definition.InstanceMethods.TryGetValue("constructor", out var ctor))
            {
                InvokeBoundMethod(new BoundMethod(instance, "constructor", ctor), Array.Empty<Expression>());
            }
        }

        private Value EvaluateIndex(IndexExpression indexExpr)
        {
            Value targetVal = EvaluateExpression(indexExpr.Target);

            if (targetVal.Kind == ValueKind.Array && targetVal.Array != null)
            {
                Value indexVal = EvaluateExpression(indexExpr.Index);
                double index = ToNumberAt(indexVal, indexExpr.Index.Position);

                if (!targetVal.Array.TryGetValue(index, out var element))
                {
                    return Value.Undefined;
                }

                return element;
            }

            if (targetVal.Kind == ValueKind.Object && targetVal.Object != null)
            {
                Value indexVal = EvaluateExpression(indexExpr.Index);
                string key = ToFieldKey(indexVal);
                return GetObjectMember(targetVal.Object, key);
            }

            throw new InterpreterException("Indexing is only supported on arrays or class instances.", indexExpr.Position);
        }

        private static string ToFieldKey(Value indexVal)
        {
            return indexVal.Kind switch
            {
                ValueKind.String => indexVal.String ?? string.Empty,
                ValueKind.Number => indexVal.Number.ToString("G", CultureInfo.InvariantCulture),
                ValueKind.Boolean => indexVal.ToString(),
                ValueKind.Null => "null",
                ValueKind.Undefined => "undefined",
                _ => indexVal.ToString()
            };
        }

        private Value GetObjectMember(ClassInstance instance, string fieldKey)
        {
            if (instance.Definition.StaticMethods.TryGetValue(fieldKey, out var staticMethod))
            {
                return Value.FromMethod(new BoundMethod(instance, fieldKey, staticMethod));
            }

            if (instance.Definition.StaticFields.TryGetValue(fieldKey, out var staticField))
            {
                return staticField;
            }

            if (instance.Definition.InstanceMethods.TryGetValue(fieldKey, out var methodDef))
            {
                return Value.FromMethod(new BoundMethod(instance, fieldKey, methodDef));
            }

            if (instance.Fields.TryGetValue(fieldKey, out var value))
                return value;

            if (instance.Definition.InstanceFallback != null &&
                instance.Fields.TryGetValue(instance.Definition.InstanceFallback, out var fallbackVal))
            {
                return fallbackVal;
            }

            if (instance.Definition.StaticFallback != null &&
                instance.Definition.StaticFields.TryGetValue(instance.Definition.StaticFallback, out var staticFallback))
            {
                return staticFallback;
            }

            return Value.Undefined;
        }

        private static bool IsStaticField(ClassInstance instance, string fieldKey) =>
            instance.Definition.StaticPropertyNames.Contains(fieldKey);

        private FieldHistory GetOrCreateFieldHistory(ClassInstance instance, string fieldKey, bool isStatic = false)
        {
            return GetOrCreateFieldHistory(instance.Name, fieldKey, isStatic);
        }

        private FieldHistory GetOrCreateFieldHistory(string className, string fieldKey, bool isStatic)
        {
            string prefix = isStatic ? "static::" : string.Empty;
            string key = $"{prefix}{className}::{fieldKey}";
            var dict = isStatic ? _staticFieldHistory : _fieldHistory;
            if (!dict.TryGetValue(key, out var hist))
            {
                hist = new FieldHistory();
                dict[key] = hist;
            }
            return hist;
        }

        private void AssignObjectField(ClassInstance instance, string fieldKey, Value newValue, string? aliasName, bool notifyAlias)
        {
            bool isStatic = IsStaticField(instance, fieldKey);
            Value current = isStatic
                ? (instance.Definition.StaticFields.TryGetValue(fieldKey, out var existingStatic) ? existingStatic : Value.Undefined)
                : (instance.Fields.TryGetValue(fieldKey, out var existing) ? existing : Value.Undefined);

            var history = GetOrCreateFieldHistory(instance, fieldKey, isStatic);

            if (history.Values.Count == 0)
            {
                history.Values.Add(current);
                history.Index = 0;
            }

            if (history.Values.Count > 0)
            {
                var currentHistVal = history.Values[history.Index];
                if (!currentHistVal.StrictEquals(newValue))
                {
                    if (history.Index < history.Values.Count - 1)
                        history.Values.RemoveRange(history.Index + 1, history.Values.Count - history.Index - 1);

                    history.Values.Add(newValue);
                    history.Index = history.Values.Count - 1;
                }
            }

            if (isStatic)
                instance.Definition.StaticFields[fieldKey] = newValue;
            else
                instance.Fields[fieldKey] = newValue;

            OnVariableMutated(instance.Name);

            if (notifyAlias && !string.IsNullOrEmpty(aliasName) && !string.Equals(aliasName, instance.Name, StringComparison.Ordinal))
                OnVariableMutated(aliasName);
        }

        private bool TryDeleteIndexTarget(IndexExpression idx)
        {
            Value container = EvaluateExpression(idx.Target);
            if (container.Kind == ValueKind.Object && container.Object != null)
            {
                string key = ToFieldKey(EvaluateExpression(idx.Index));
                var instance = container.Object;
                bool isStatic = IsStaticField(instance, key);
                bool removed = isStatic ? instance.Definition.StaticFields.Remove(key) : instance.Fields.Remove(key);
                bool removedHistory = isStatic
                    ? _staticFieldHistory.Remove($"static::{instance.Name}::{key}")
                    : _fieldHistory.Remove($"{instance.Name}::{key}");

                if (removed || removedHistory)
                {
                    OnVariableMutated(instance.Name);
                    if (TryGetName(idx.Target, out var alias) && !string.Equals(alias, instance.Name, StringComparison.Ordinal))
                        OnVariableMutated(alias);
                }

                return removed;
            }

            return false;
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

        private Value EvaluatePowerStars(PowerStarsExpression pow)
        {
            // Postfix semantics (like x++): return OLD numeric value, then write back x = x^Exponent.
            // Supports identifiers and arr[index] (same as PostfixUpdateExpression).

            int exp = pow.Exponent;
            if (exp < 0)
                throw new InterpreterException("Invalid '**' exponent.", pow.Position);

            // x** / x**** / ...
            if (pow.Target is IdentifierExpression id)
            {
                if (_constStore.TryGet(id.Name, out _))
                    throw new InterpreterException($"Cannot update const const const variable '{id.Name}'.", pow.Position);

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
                        throw new InterpreterException($"Variable '{id.Name}' is not defined.", pow.Position);
                }

                double oldNum = ToNumberAt(current, pow.Position);

                double powered = Math.Pow(oldNum, exp);
                Value newValue = (double.IsNaN(powered) || double.IsInfinity(powered))
                    ? Value.Undefined
                    : Value.FromNumber(powered);

                if (isLocal)
                    _callStack.Peek().Locals[id.Name] = newValue;
                else
                    _variables.Assign(id.Name, newValue, _currentStatementIndex);

                OnVariableMutated(id.Name);
                return Value.FromNumber(oldNum);
            }

            // arr[index]** / arr[index]**** / ...
            if (pow.Target is IndexExpression ix)
            {
                if (ix.Target is not IdentifierExpression arrId)
                    throw new InterpreterException("Postfix '**' on indexing is only supported for identifier arrays (e.g. arr[i]**).", pow.Position);

                if (_constStore.TryGet(arrId.Name, out _))
                    throw new InterpreterException($"Cannot update const const const variable '{arrId.Name}'.", pow.Position);

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
                        throw new InterpreterException($"Variable '{arrId.Name}' is not defined.", pow.Position);
                }

                if (arrayValue.Kind != ValueKind.Array || arrayValue.Array == null)
                    throw new InterpreterException("Indexing power update is only supported on arrays.", pow.Position);

                Value indexVal = EvaluateExpression(ix.Index);
                double index = ToNumberAt(indexVal, ix.Index.Position);

                if (!arrayValue.Array.TryGetValue(index, out var elem))
                    elem = Value.Undefined;

                double oldNum = ToNumberAt(elem, pow.Position);

                double powered = Math.Pow(oldNum, exp);
                Value newElem = (double.IsNaN(powered) || double.IsInfinity(powered))
                    ? Value.Undefined
                    : Value.FromNumber(powered);

                var dict = new Dictionary<double, Value>(arrayValue.Array);
                dict[index] = newElem;
                Value newArrayValue = Value.FromArray(dict);

                if (isLocal)
                    _callStack.Peek().Locals[arrId.Name] = newArrayValue;
                else
                    _variables.Assign(arrId.Name, newArrayValue, _currentStatementIndex);

                OnVariableMutated(arrId.Name);
                return Value.FromNumber(oldNum);
            }

            throw new InterpreterException("Postfix '**' target must be an identifier or an array index.", pow.Position);
        }


        private Value EvaluatePrefixRoot(PrefixRootExpression root)
        {
            double x = ToNumberAt(EvaluateExpression(root.Operand), root.Position);
            int n = root.Degree;

            if (n <= 0)
                return Value.Undefined;

            // Even root of a negative number => undefined (DreamBerd vibe)
            if ((n % 2) == 0 && x < 0)
                return Value.Undefined;

            double abs = Math.Abs(x);
            double r = Math.Pow(abs, 1.0 / n);
            if (x < 0) r = -r;

            if (double.IsNaN(r) || double.IsInfinity(r))
                return Value.Undefined;

            return Value.FromNumber(r);
        }

        private Value EvaluateRootInfix(RootInfixExpression root)
        {
            double a = ToNumberAt(EvaluateExpression(root.Radicand), root.Position);
            double n = ToNumberAt(EvaluateExpression(root.Degree), root.Position);

            if (n == 0)
                return Value.Undefined;

            double r = Math.Pow(a, 1.0 / n);

            if (double.IsNaN(r) || double.IsInfinity(r))
                return Value.Undefined;

            return Value.FromNumber(r);
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
