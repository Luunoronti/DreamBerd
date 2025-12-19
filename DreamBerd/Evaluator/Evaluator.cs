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
        private readonly Dictionary<string, Dictionary<string, ExportedBinding>> _exportsByFile = new Dictionary<string, Dictionary<string, ExportedBinding>>(StringComparer.OrdinalIgnoreCase);

        private readonly Stack<CallFrame> _callStack = new Stack<CallFrame>();

        private enum ExportedBindingKind
        {
            Value,
            Function,
            Class
        }

        private sealed class ExportedBinding
        {
            public ExportedBindingKind Kind { get; }
            public Value Value { get; }
            public FunctionDefinition? Function { get; }
            public ClassDefinition? Class { get; }

            private ExportedBinding(ExportedBindingKind kind, Value value, FunctionDefinition? function, ClassDefinition? @class)
            {
                Kind = kind;
                Value = value;
                Function = function;
                Class = @class;
            }

            public static ExportedBinding FromValue(Value value) =>
                new ExportedBinding(ExportedBindingKind.Value, value, null, null);

            public static ExportedBinding FromFunction(FunctionDefinition function) =>
                new ExportedBinding(ExportedBindingKind.Function, Value.Undefined, function, null);

            public static ExportedBinding FromClass(ClassDefinition @class) =>
                new ExportedBinding(ExportedBindingKind.Class, Value.Undefined, null, @class);
        }

        public string CurrentFileName { get; set; } = "main.gom";

        public Evaluator(VariableStore variables, IConstConstConstStore constStore)
        {
            _variables = variables ?? throw new ArgumentNullException(nameof(variables));
            _constStore = constStore ?? throw new ArgumentNullException(nameof(constStore));
            CurrentDirectory = System.Environment.CurrentDirectory;
            RegisterStdLibDefaultMethods();
        }

        private static string NormalizeFileName(string name) =>
            string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();

        private bool IsNameDefined(string name)
        {
            if (_constStore.TryGet(name, out _))
                return true;
            if (_variables.TryGet(name, out _))
                return true;
            if (_functions.ContainsKey(name))
                return true;
            if (_classes.ContainsKey(name))
                return true;
            return false;
        }

        private ExportedBinding ResolveExportBinding(string name, int position)
        {
            if (_functions.TryGetValue(name, out var fn))
                return ExportedBinding.FromFunction(fn);

            if (_classes.TryGetValue(name, out var cls))
                return ExportedBinding.FromClass(cls);

            if (_constStore.TryGet(name, out var constVal))
                return ExportedBinding.FromValue(constVal);

            if (_variables.TryGet(name, out var varVal))
                return ExportedBinding.FromValue(varVal);

            throw new InterpreterException($"Cannot export '{name}' because it is not defined.", position);
        }

        private void RegisterExport(string targetFile, string name, int position)
        {
            string fileKey = NormalizeFileName(targetFile);
            if (string.IsNullOrEmpty(fileKey))
                throw new InterpreterException("Export target file name cannot be empty.", position);

            var binding = ResolveExportBinding(name, position);
            if (!_exportsByFile.TryGetValue(fileKey, out var exports))
            {
                exports = new Dictionary<string, ExportedBinding>(StringComparer.Ordinal);
                _exportsByFile[fileKey] = exports;
            }

            exports[name] = binding;
        }

        private bool TryImportExportedBinding(string name, int position)
        {
            string fileKey = NormalizeFileName(CurrentFileName);
            if (!_exportsByFile.TryGetValue(fileKey, out var exports))
                return false;

            if (!exports.TryGetValue(name, out var binding))
                return false;

            if (IsNameDefined(name))
                return true;

            switch (binding.Kind)
            {
                case ExportedBindingKind.Value:
                    _variables.Declare(name, Mutability.ConstConst, binding.Value, 1, LifetimeSpecifier.None, _currentStatementIndex);
                    OnVariableMutated(name);
                    return true;
                case ExportedBindingKind.Function:
                    if (binding.Function == null)
                        return false;
                    _functions[name] = binding.Function;
                    return true;
                case ExportedBindingKind.Class:
                    if (binding.Class == null)
                        return false;
                    _classes[name] = binding.Class;
                    _classInstances.Remove(name);
                    ClearFieldHistoryForClass(name);
                    return true;
            }

            return false;
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

        private IReadOnlyCollection<string> CollectPatternWhenDependencies(Expression target, Pattern pattern, Expression? guard)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            CollectDependencies(target, set, isCallee: false);
            CollectPatternDependencies(pattern, set);
            CollectDependencies(guard, set, isCallee: false);

            if (set.Count == 0)
                set.Add(WhenWildcard);

            return set;
        }

        private void CollectPatternDependencies(Pattern pattern, HashSet<string> deps)
        {
            switch (pattern)
            {
                case BindingPattern bp:
                    CollectDependencies(bp.DefaultExpression, deps, isCallee: false);
                    break;
                case ArrayPattern ap:
                    foreach (var el in ap.Elements)
                        CollectPatternDependencies(el, deps);
                    if (ap.Rest != null)
                        CollectPatternDependencies(ap.Rest, deps);
                    break;
                case ObjectPattern op:
                    foreach (var prop in op.Properties)
                    {
                        CollectPatternDependencies(prop.ValuePattern, deps);
                        CollectDependencies(prop.DefaultExpression, deps, isCallee: false);
                    }
                    break;
            }
        }

        private void BindBindingPattern(BindingPattern bp, Value value, Dictionary<string, Value> bindings)
        {
            Value finalVal = value;
            if (finalVal.Kind == ValueKind.Undefined && bp.DefaultExpression != null)
            {
                finalVal = EvaluateExpression(bp.DefaultExpression);
            }

            if (!bp.Ignore)
            {
                bindings[bp.Name] = finalVal;
            }
        }

        private bool TryMatchPattern(Value value, Pattern pattern, Dictionary<string, Value> bindings, bool strict)
        {
            switch (pattern)
            {
                case BindingPattern bp:
                    BindBindingPattern(bp, value, bindings);
                    return true;

                case ArrayPattern ap:
                    return MatchArrayPattern(value, ap, bindings, strict);

                case ObjectPattern op:
                    return MatchObjectPattern(value, op, bindings, strict);

                default:
                    return false;
            }
        }

        private bool MatchArrayPattern(Value value, ArrayPattern pattern, Dictionary<string, Value> bindings, bool strict)
        {
            if (value.Kind != ValueKind.Array || value.Array == null)
            {
                if (strict)
                    return false;

                foreach (var el in pattern.Elements)
                    TryMatchPattern(Value.Undefined, el, bindings, strict: false);

                if (pattern.Rest != null)
                    BindBindingPattern(pattern.Rest, Value.FromArray(new Dictionary<double, Value>()), bindings);

                return true;
            }

            var array = value.Array;

            for (int i = 0; i < pattern.Elements.Count; i++)
            {
                double idx = i - 1;
                Value elementVal = array.TryGetValue(idx, out var found) ? found : Value.Undefined;
                TryMatchPattern(elementVal, pattern.Elements[i], bindings, strict);
            }

            if (pattern.Rest != null)
            {
                var restDict = new Dictionary<double, Value>();
                var keys = new List<double>(array.Keys);
                keys.Sort();

                var consumed = new HashSet<double>();
                for (int i = 0; i < pattern.Elements.Count; i++)
                    consumed.Add(i - 1);

                double restIndex = -1;
                foreach (var k in keys)
                {
                    if (consumed.Contains(k))
                        continue;

                    restDict[restIndex] = array[k];
                    restIndex++;
                }

                BindBindingPattern(pattern.Rest, Value.FromArray(restDict), bindings);
            }

            return true;
        }

        private bool MatchObjectPattern(Value value, ObjectPattern pattern, Dictionary<string, Value> bindings, bool strict)
        {
            if (value.Kind != ValueKind.Object || value.Object == null)
            {
                if (strict)
                    return false;

                foreach (var prop in pattern.Properties)
                {
                    Value propVal = prop.DefaultExpression != null
                        ? EvaluateExpression(prop.DefaultExpression)
                        : Value.Undefined;

                    TryMatchPattern(propVal, prop.ValuePattern, bindings, strict: false);
                }

                return true;
            }

            var instance = value.Object;

            foreach (var prop in pattern.Properties)
            {
                Value propVal = GetObjectMember(instance, prop.Key);
                if (propVal.Kind == ValueKind.Undefined && prop.DefaultExpression != null)
                    propVal = EvaluateExpression(prop.DefaultExpression);

                if (!TryMatchPattern(propVal, prop.ValuePattern, bindings, strict))
                    return false;
            }

            return true;
        }

        private void RunWithPatternBindings(IReadOnlyDictionary<string, Value> bindings, Action action)
        {
            if (_callStack.Count > 0)
            {
                var frame = _callStack.Peek();
                var previous = new Dictionary<string, (bool existed, Value value)>(StringComparer.Ordinal);

                foreach (var kvp in bindings)
                {
                    if (frame.Locals.TryGetValue(kvp.Key, out var oldVal))
                        previous[kvp.Key] = (true, oldVal);
                    else
                        previous[kvp.Key] = (false, Value.Undefined);

                    frame.Locals[kvp.Key] = kvp.Value;
                }

                try
                {
                    action();
                }
                finally
                {
                    foreach (var kvp in bindings)
                    {
                        var info = previous[kvp.Key];
                        if (info.existed)
                            frame.Locals[kvp.Key] = info.value;
                        else
                            frame.Locals.Remove(kvp.Key);
                    }
                }
            }
            else
            {
                var frame = new CallFrame();
                foreach (var kvp in bindings)
                    frame.Locals[kvp.Key] = kvp.Value;

                _callStack.Push(frame);
                try
                {
                    action();
                }
                finally
                {
                    _callStack.Pop();
                }
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
                        if (sub.Pattern == null)
                        {
                            if (sub.Condition == null)
                                continue;

                            Value condVal = EvaluateExpression(sub.Condition);
                            if (condVal.IsTruthy())
                            {
                                EvaluateStatement(sub.Body);
                            }
                        }
                        else
                        {
                            ExecutePatternWhen(sub);
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

        private void ExecutePatternWhen(WhenSubscription sub)
        {
            if (sub.Target == null || sub.Pattern == null)
                return;

            Value targetVal = EvaluateExpression(sub.Target);
            var bindings = new Dictionary<string, Value>(StringComparer.Ordinal);
            if (!TryMatchPattern(targetVal, sub.Pattern, bindings, strict: true))
                return;

            RunWithPatternBindings(bindings, () =>
            {
                if (sub.Guard != null)
                {
                    Value guardVal = EvaluateExpression(sub.Guard);
                    if (!guardVal.IsTruthy())
                        return;
                }

                EvaluateStatement(sub.Body);
            });
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
