// Evaluator.Evaluation.cs
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DreamberdInterpreter
{
    public sealed partial class Evaluator
    {
        private static readonly Dictionary<string, ulong> NumberUnits = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase)
        {
            ["zero"] = 0, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5,
            ["six"] = 6, ["seven"] = 7, ["eight"] = 8, ["nine"] = 9, ["ten"] = 10,
            ["eleven"] = 11, ["twelve"] = 12, ["thirteen"] = 13, ["fourteen"] = 14,
            ["fifteen"] = 15, ["sixteen"] = 16, ["seventeen"] = 17, ["eighteen"] = 18, ["nineteen"] = 19,

            // PL
            ["jeden"] = 1, ["dwa"] = 2, ["trzy"] = 3, ["cztery"] = 4, ["piec"] = 5,
            ["szesc"] = 6, ["siedem"] = 7, ["osiem"] = 8, ["dziewiec"] = 9, ["dziesiec"] = 10,
            ["jedenascie"] = 11, ["dwanascie"] = 12, ["trzynascie"] = 13, ["czternascie"] = 14,
            ["pietnascie"] = 15, ["szesnascie"] = 16, ["siedemnascie"] = 17, ["osiemnascie"] = 18, ["dziewietnascie"] = 19
        };

        private static readonly Dictionary<string, ulong> NumberTens = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase)
        {
            ["twenty"] = 20, ["thirty"] = 30, ["forty"] = 40, ["fifty"] = 50,
            ["sixty"] = 60, ["seventy"] = 70, ["eighty"] = 80, ["ninety"] = 90,

            // PL
            ["dwadziescia"] = 20, ["trzydziesci"] = 30, ["czterdziesci"] = 40, ["piecdziesiat"] = 50,
            ["szescdziesiat"] = 60, ["siedemdziesiat"] = 70, ["osiemdziesiat"] = 80, ["dziewiecdziesiat"] = 90
        };

        private static readonly Dictionary<string, ulong> NumberScales = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase)
        {
            ["thousand"] = 1_000,
            ["million"] = 1_000_000,
            ["billion"] = 1_000_000_000,
            ["trillion"] = 1_000_000_000_000,
            ["quadrillion"] = 1_000_000_000_000_000,
            ["quintillion"] = 1_000_000_000_000_000_000,

            // PL
            ["tysiac"] = 1_000,
            ["tysiace"] = 1_000,
            ["milion"] = 1_000_000,
            ["miliard"] = 1_000_000_000,
            ["bilion"] = 1_000_000_000_000,
            ["biliard"] = 1_000_000_000_000_000,
            ["trylion"] = 1_000_000_000_000_000_000
        };

        private static readonly Dictionary<string, string> NumberAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["hundret"] = "hundred",
            ["hundrets"] = "hundred",
            ["hundreds"] = "hundred",
            ["milion"] = "million",
            ["milions"] = "million",
            ["thounsand"] = "thousand",
            ["thounsands"] = "thousand",
            ["thousandths"] = "thousand",
            ["sto"] = "hundred"
        };

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

                case NumberIdentifierExpression numIdent:
                    result = EvaluateNumberIdentifier(numIdent);
                    break;

                case StringIdentifierExpression strIdent:
                    result = EvaluateStringIdentifier(strIdent);
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

                case PostfixUpdateExpression post:
                    result = EvaluatePostfixUpdate(post);
                    break;

                case PowerStarsExpression powStars:
                    result = EvaluatePowerStars(powStars);
                    break;

                case PrefixRootExpression rootPrefix:
                    result = EvaluatePrefixRoot(rootPrefix);
                    break;

                case RootInfixExpression rootInfix:
                    result = EvaluateRootInfix(rootInfix);
                    break;

                case RangeExpression rangeExpr:
                    result = EvaluateRangeLiteral(rangeExpr);
                    break;

                case ConditionalExpression condExpr:
                    result = EvaluateConditional(condExpr);
                    break;

                default:
                    throw new InterpreterException($"Unknown expression type: {expression.GetType().Name}.", expression.Position);
            }

            return CheckDeleted(result, expression.Position);
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
                case DestructuringVariableDeclarationStatement dvds:
                    {
                        Value initVal = EvaluateExpression(dvds.Initializer);
                        ApplyPatternDeclaration(dvds.Pattern, initVal, dvds.Mutability, dvds.DeclarationKind, dvds.Priority, dvds.Lifetime, dvds.Position);
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
                        if (ds.Target is IndexExpression idx && TryDeleteIndexTarget(idx))
                        {
                            if (ds.IsDebug)
                                Console.WriteLine("[DEBUG] delete {0}", "index");
                            return Value.Null;
                        }

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

                case PatternWhenStatement pws:
                    {
                        var deps = CollectPatternWhenDependencies(pws.Target, pws.Pattern, pws.Guard);
                        var sub = new WhenSubscription(null, pws.Target, pws.Pattern, pws.Guard, pws.Body, deps);
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

                case ClassDeclarationStatement cds:
                    {
                        var instanceMethods = new Dictionary<string, FunctionDefinition>(StringComparer.Ordinal);
                        var staticMethods = new Dictionary<string, FunctionDefinition>(StringComparer.Ordinal);
                        foreach (var m in cds.Methods)
                        {
                            var fn = new FunctionDefinition(m.Parameters, m.Body);
                            if (m.IsStatic)
                                staticMethods[m.Name] = fn;
                            else
                                instanceMethods[m.Name] = fn;
                        }

                        var def = new ClassDefinition(cds.Name, instanceMethods, staticMethods, cds.Properties.ToList());

                        foreach (var prop in cds.Properties)
                        {
                            if (prop.IsStatic)
                            {
                                Value init = prop.Initializer != null ? EvaluateExpression(prop.Initializer) : Value.Undefined;
                                def.StaticFields[prop.Name] = init;
                                if (prop.IsFallback)
                                    def.StaticFallback = prop.Name;

                                var hist = GetOrCreateFieldHistory(def.Name, prop.Name, isStatic: true);
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
                            }
                            else
                            {
                                if (prop.IsFallback)
                                    def.InstanceFallback = prop.Name;
                            }
                        }

                        _classes[cds.Name] = def;
                        _classInstances.Remove(cds.Name);
                        ClearFieldHistoryForClass(cds.Name);
                        return Value.Null;
                    }

                case UpdateStatement us:
                    return EvaluateUpdate(us);

                
case IfStatement ifs:
    {
        _ifDepth++;
        try
        {
            while (true)
            {
                Value cond = EvaluateExpression(ifs.Condition);

                try
                {
                    if (cond.IsTruthy())
                    {
                        if (cond.Kind == ValueKind.Boolean && cond.Bool == BooleanState.Maybe)
                        {
                            if (ifs.IdkBranch != null)
                                return EvaluateStatement(ifs.IdkBranch);

                            // Jeśli nie ma idk-branch, a warunek jest maybe, to nic nie robimy.
                            return Value.Null;
                        }

                        return EvaluateStatement(ifs.ThenBranch);
                    }
                    else if (ifs.ElseBranch != null)
                    {
                        return EvaluateStatement(ifs.ElseBranch);
                    }

                    return Value.Null;
                }
                catch (TryAgainSignal)
                {
                    // wróć do warunku i spróbuj jeszcze raz
                    continue;
                }
            }
        }
        finally
        {
            _ifDepth--;
        }
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


case TryAgainStatement tas:
    {
        if (_ifDepth <= 0)
            throw new InterpreterException("try again can only be used inside an if/else/idk block.", tas.Position);
        throw new TryAgainSignal();
    }

                case ReverseStatement _:
                case ForwardStatement _:
                    return Value.Null;

                default:
                    throw new InterpreterException($"Unknown statement type: {statement.GetType().Name}.", statement.Position);
            }
        }

        private void ApplyPatternDeclaration(Pattern pattern, Value value, Mutability mutability, DeclarationKind declarationKind, int priority, LifetimeSpecifier lifetime, int position)
        {
            var bindings = new Dictionary<string, Value>(StringComparer.Ordinal);
            TryMatchPattern(value, pattern, bindings, strict: false);

            if (priority <= 0)
                priority = 1;

            foreach (var kvp in bindings)
            {
                string name = kvp.Key;
                Value boundValue = kvp.Value;

                if (declarationKind == DeclarationKind.ConstConstConst)
                {
                    _constStore.Define(name, boundValue);
                }
                else
                {
                    _variables.Declare(name, mutability, boundValue, priority, lifetime, _currentStatementIndex);
                }

                OnVariableMutated(name);
            }
        }

        private bool TryResolveName(string name, out Value value)
        {
            value = default!;

            // najpierw zmienne lokalne funkcji
            if (_callStack.Count > 0)
            {
                var frame = _callStack.Peek();
                if (frame.Locals.TryGetValue(name, out var localValue))
                {
                    value = localValue;
                    return true;
                }
            }

            // const const const
            if (_constStore.TryGet(name, out var globalValue))
            {
                value = globalValue;
                return true;
            }

            // globalne zmienne w VariableStore
            if (_variables.TryGet(name, out var varValue))
            {
                value = varValue;
                return true;
            }

            return false;
        }

        private Value EvaluateIdentifier(IdentifierExpression ident)
        {
            if (TryResolveName(ident.Name, out var value))
                return value;

            if (_classes.TryGetValue(ident.Name, out var classDef))
            {
                var instance = GetOrCreateInstance(classDef);
                return Value.FromObject(instance);
            }

            // fallback: string z nazwą
            return Value.FromString(ident.Name);
        }

        private Value EvaluateNumberIdentifier(NumberIdentifierExpression ident)
        {
            if (TryResolveName(ident.Name, out var value))
                return value;

            return Value.FromNumber(ident.NumberValue);
        }

        private Value EvaluateStringIdentifier(StringIdentifierExpression ident)
        {
            if (TryResolveName(ident.Name, out var value))
                return value;

            return Value.FromString(ident.Name);
        }
        private Value EvaluateUnary(UnaryExpression unary)
        {
            Value operand = EvaluateExpression(unary.Operand);

            switch (unary.Operator)
            {
                case UnaryOperator.Negate:
                    return Value.FromNumber(-ToNumberAt(operand, unary.Operand.Position));

                case UnaryOperator.Abs:
                    return Value.FromNumber(Math.Abs(ToNumberAt(operand, unary.Operand.Position)));

                case UnaryOperator.Sin:
                    return Value.FromNumber(Math.Sin(ToNumberAt(operand, unary.Operand.Position)));

                case UnaryOperator.Cos:
                    return Value.FromNumber(Math.Cos(ToNumberAt(operand, unary.Operand.Position)));

                case UnaryOperator.Tan:
                    return Value.FromNumber(Math.Tan(ToNumberAt(operand, unary.Operand.Position)));

                case UnaryOperator.Not:
                    // DreamBerd boolean negation operator ';'
                    // true -> false, false -> true, maybe -> maybe, undefined -> undefined
                    if (operand.Kind == ValueKind.Boolean)
                    {
                        return operand.Bool switch
                        {
                            BooleanState.True => Value.FromBooleanState(BooleanState.False),
                            BooleanState.False => Value.FromBooleanState(BooleanState.True),
                            BooleanState.Maybe => Value.FromBooleanState(BooleanState.Maybe),
                            _ => Value.FromBooleanState(BooleanState.False)
                        };
                    }

                    if (operand.Kind == ValueKind.Undefined)
                        return Value.Undefined;

                    throw new InterpreterException("Negation ';' expects a boolean (true/false/maybe) or undefined.", unary.Position);

                default:
                    throw new InterpreterException($"Unsupported unary operator {unary.Operator}.", unary.Position);
            }
        }

        private Value EvaluateBinary(BinaryExpression binary)
        {
            if (binary.Operator == BinaryOperator.Clamp)
            {
                Value leftVal = EvaluateExpression(binary.Left);
                var rangeExpr = binary.Right as RangeExpression
                    ?? throw new InterpreterException("Clamp operator expects a range on the right-hand side.", binary.Right.Position);
                var bounds = EvaluateRange(rangeExpr);
                return ClampValue(leftVal, bounds, binary.Position);
            }

            if (binary.Operator == BinaryOperator.Wrap)
            {
                Value leftVal = EvaluateExpression(binary.Left);
                var rangeExpr = binary.Right as RangeExpression
                    ?? throw new InterpreterException("Wrap operator expects a range on the right-hand side.", binary.Right.Position);
                var bounds = EvaluateRange(rangeExpr);
                return WrapValue(leftVal, bounds, binary.Position);
            }

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

                case BinaryOperator.Min:
                    return Value.FromNumber(Math.Min(ToNumberAt(left, binary.Left.Position), ToNumberAt(right, binary.Right.Position)));

                case BinaryOperator.Max:
                    return Value.FromNumber(Math.Max(ToNumberAt(left, binary.Left.Position), ToNumberAt(right, binary.Right.Position)));

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
                var expr = cond.Bool switch
                {
                    BooleanState.True => condExpr.WhenTrue,
                    BooleanState.False => condExpr.WhenFalse,
                    BooleanState.Maybe => condExpr.WhenMaybe,
                    _ => condExpr.WhenUndefined
                };

                return expr != null ? EvaluateExpression(expr) : Value.Undefined;
            }

            if (cond.Kind == ValueKind.Undefined)
            {
                return condExpr.WhenUndefined != null ? EvaluateExpression(condExpr.WhenUndefined) : Value.Undefined;
            }

            // inne typy -> na podstawie IsTruthy -> true/false
            if (cond.IsTruthy())
            {
                return EvaluateExpression(condExpr.WhenTrue);
            }
            else
            {
                return condExpr.WhenFalse != null ? EvaluateExpression(condExpr.WhenFalse) : Value.Undefined;
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

            if (targetVal.Kind == ValueKind.Array && targetVal.Array != null)
            {
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

            if (targetVal.Kind == ValueKind.Object && targetVal.Object != null)
            {
                Value indexVal = EvaluateExpression(ia.Index);
                string key = ToFieldKey(indexVal);
                Value newValue = EvaluateExpression(ia.ValueExpression);
                string? alias = TryGetName(ia.Target, out var nm) ? nm : null;
                AssignObjectField(targetVal.Object, key, newValue, alias, notifyAlias: true);
                return newValue;
            }

            throw new InterpreterException("Index assignment is only supported on arrays or class instances.", ia.Position);
        }

        private Value EvaluateUpdate(UpdateStatement us)
        {
            Value current = ResolveAssignable(us.Target, us.Position, out var assign, out var mutatedName);

            Value newValue;

            switch (us.Operator)
            {
                case UpdateOperator.Add:
                    {
                        var rhs = EvaluateExpression(us.ValueExpression ?? throw new InterpreterException("Missing right-hand side for ':+' update.", us.Position));
                        newValue = Value.FromNumber(ToNumberAt(current, us.Target.Position) + ToNumberAt(rhs, us.ValueExpression!.Position));
                        break;
                    }
                case UpdateOperator.Subtract:
                    {
                        var rhs = EvaluateExpression(us.ValueExpression ?? throw new InterpreterException("Missing right-hand side for ':-' update.", us.Position));
                        newValue = Value.FromNumber(ToNumberAt(current, us.Target.Position) - ToNumberAt(rhs, us.ValueExpression!.Position));
                        break;
                    }
                case UpdateOperator.Multiply:
                    {
                        var rhs = EvaluateExpression(us.ValueExpression ?? throw new InterpreterException("Missing right-hand side for ':*' update.", us.Position));
                        newValue = Value.FromNumber(ToNumberAt(current, us.Target.Position) * ToNumberAt(rhs, us.ValueExpression!.Position));
                        break;
                    }
                case UpdateOperator.Divide:
                    {
                        var rhs = EvaluateExpression(us.ValueExpression ?? throw new InterpreterException("Missing right-hand side for ':/'.", us.Position));
                        double divisor = ToNumberAt(rhs, us.ValueExpression!.Position);
                        if (Math.Abs(divisor) < double.Epsilon)
                            newValue = Value.Undefined;
                        else
                            newValue = Value.FromNumber(ToNumberAt(current, us.Target.Position) / divisor);
                        break;
                    }
                case UpdateOperator.Modulo:
                    {
                        var rhs = EvaluateExpression(us.ValueExpression ?? throw new InterpreterException("Missing right-hand side for ':%'.", us.Position));
                        double divisor = ToNumberAt(rhs, us.ValueExpression!.Position);
                        if (Math.Abs(divisor) < double.Epsilon)
                            newValue = Value.Undefined;
                        else
                            newValue = Value.FromNumber(ToNumberAt(current, us.Target.Position) % divisor);
                        break;
                    }
                case UpdateOperator.Power:
                    {
                        double exponent = us.ValueExpression != null
                            ? ToNumberAt(EvaluateExpression(us.ValueExpression), us.ValueExpression.Position)
                            : us.RunValue;
                        newValue = Value.FromNumber(Math.Pow(ToNumberAt(current, us.Target.Position), exponent));
                        break;
                    }
                case UpdateOperator.Root:
                    {
                        double degree = us.ValueExpression != null
                            ? ToNumberAt(EvaluateExpression(us.ValueExpression), us.ValueExpression.Position)
                            : us.RunValue;

                        if (Math.Abs(degree) < double.Epsilon)
                        {
                            newValue = Value.Undefined;
                        }
                        else
                        {
                            newValue = Value.FromNumber(Math.Pow(ToNumberAt(current, us.Target.Position), 1.0 / degree));
                        }
                        break;
                    }
                case UpdateOperator.BitAnd:
                    {
                        var rhs = EvaluateExpression(us.ValueExpression ?? throw new InterpreterException("Missing right-hand side for ':&'.", us.Position));
                        long l = ToInt64At(current, us.Target.Position);
                        long r = ToInt64At(rhs, us.ValueExpression!.Position);
                        newValue = Value.FromNumber(l & r);
                        break;
                    }
                case UpdateOperator.BitOr:
                    {
                        var rhs = EvaluateExpression(us.ValueExpression ?? throw new InterpreterException("Missing right-hand side for ':|'.", us.Position));
                        long l = ToInt64At(current, us.Target.Position);
                        long r = ToInt64At(rhs, us.ValueExpression!.Position);
                        newValue = Value.FromNumber(l | r);
                        break;
                    }
                case UpdateOperator.BitXor:
                    {
                        var rhs = EvaluateExpression(us.ValueExpression ?? throw new InterpreterException("Missing right-hand side for ':^'.", us.Position));
                        long l = ToInt64At(current, us.Target.Position);
                        long r = ToInt64At(rhs, us.ValueExpression!.Position);
                        newValue = Value.FromNumber(l ^ r);
                        break;
                    }
                case UpdateOperator.ShiftLeft:
                    {
                        var rhs = EvaluateExpression(us.ValueExpression ?? throw new InterpreterException("Missing right-hand side for ':<<'.", us.Position));
                        long l = ToInt64At(current, us.Target.Position);
                        int shift = (int)ToInt64At(rhs, us.ValueExpression!.Position);
                        newValue = Value.FromNumber(l << shift);
                        break;
                    }
                case UpdateOperator.ShiftRight:
                    {
                        var rhs = EvaluateExpression(us.ValueExpression ?? throw new InterpreterException("Missing right-hand side for ':>>'.", us.Position));
                        long l = ToInt64At(current, us.Target.Position);
                        int shift = (int)ToInt64At(rhs, us.ValueExpression!.Position);
                        newValue = Value.FromNumber(l >> shift);
                        break;
                    }
                case UpdateOperator.NullishAssign:
                    {
                        if (current.Kind != ValueKind.Undefined)
                            return current;

                        var rhs = EvaluateExpression(us.ValueExpression ?? throw new InterpreterException("Missing right-hand side for ':??'.", us.Position));
                        newValue = rhs;
                        break;
                    }
                case UpdateOperator.Min:
                    {
                        var rhs = EvaluateExpression(us.ValueExpression ?? throw new InterpreterException("Missing right-hand side for ':<'.", us.Position));
                        double leftNum = ToNumberAt(current, us.Target.Position);
                        double rightNum = ToNumberAt(rhs, us.ValueExpression!.Position);
                        newValue = Value.FromNumber(Math.Min(leftNum, rightNum));
                        break;
                    }
                case UpdateOperator.Max:
                    {
                        var rhs = EvaluateExpression(us.ValueExpression ?? throw new InterpreterException("Missing right-hand side for ':>'.", us.Position));
                        double leftNum = ToNumberAt(current, us.Target.Position);
                        double rightNum = ToNumberAt(rhs, us.ValueExpression!.Position);
                        newValue = Value.FromNumber(Math.Max(leftNum, rightNum));
                        break;
                    }
                case UpdateOperator.Sin:
                    {
                        double baseVal = ToNumberAt(current, us.Target.Position);
                        newValue = Value.FromNumber(Math.Sin(baseVal));
                        break;
                    }
                case UpdateOperator.Cos:
                    {
                        double baseVal = ToNumberAt(current, us.Target.Position);
                        newValue = Value.FromNumber(Math.Cos(baseVal));
                        break;
                    }
                case UpdateOperator.Tan:
                    {
                        double baseVal = ToNumberAt(current, us.Target.Position);
                        newValue = Value.FromNumber(Math.Tan(baseVal));
                        break;
                    }
                case UpdateOperator.Clamp:
                    {
                        var rangeExpr = us.RangeExpression ?? throw new InterpreterException("Missing range for clamp update.", us.Position);
                        var bounds = EvaluateRange(rangeExpr);
                        newValue = ClampValue(current, bounds, us.Target.Position);
                        break;
                    }
                case UpdateOperator.Wrap:
                    {
                        var rangeExpr = us.RangeExpression ?? throw new InterpreterException("Missing range for wrap update.", us.Position);
                        var bounds = EvaluateRange(rangeExpr);
                        double delta = us.ValueExpression != null
                            ? ToNumberAt(EvaluateExpression(us.ValueExpression), us.ValueExpression.Position)
                            : 0.0;
                        Value baseValue = Value.FromNumber(ToNumberAt(current, us.Target.Position) + delta);
                        newValue = WrapValue(baseValue, bounds, us.Target.Position);
                        break;
                    }
                default:
                    throw new InterpreterException($"Unsupported update operator {us.Operator}.", us.Position);
            }

            assign(newValue);
            if (us.IsDebug)
            {
                DebugPrint(us.Target, newValue);
            }

            if (mutatedName != null)
                OnVariableMutated(mutatedName);

            return newValue;
        }

        private readonly struct RangeBounds
        {
            public RangeBounds(double lower, double upper, bool includeLower, bool includeUpper)
            {
                Lower = lower;
                Upper = upper;
                IncludeLower = includeLower;
                IncludeUpper = includeUpper;
            }

            public double Lower { get; }
            public double Upper { get; }
            public bool IncludeLower { get; }
            public bool IncludeUpper { get; }
        }

        private Value EvaluateRangeLiteral(RangeExpression rangeExpr)
        {
            _ = EvaluateRange(rangeExpr);
            return Value.Undefined;
        }

        private RangeBounds EvaluateRange(RangeExpression rangeExpr)
        {
            double lower = ToNumberAt(EvaluateExpression(rangeExpr.Lower), rangeExpr.Lower.Position);
            double upper = ToNumberAt(EvaluateExpression(rangeExpr.Upper), rangeExpr.Upper.Position);
            return new RangeBounds(lower, upper, rangeExpr.IncludeLower, rangeExpr.IncludeUpper);
        }

        private Value ClampValue(Value input, RangeBounds bounds, int position)
        {
            if (double.IsNaN(bounds.Lower) || double.IsNaN(bounds.Upper))
                return Value.Undefined;

            if (bounds.Upper < bounds.Lower)
                return Value.Undefined;

            double val = ToNumberAt(input, position);
            double result = val;

            if (val < bounds.Lower || (!bounds.IncludeLower && NearlyEqual(val, bounds.Lower)))
            {
                result = bounds.IncludeLower ? bounds.Lower : NextAfter(bounds.Lower, bounds.Upper);
            }
            else if (val > bounds.Upper || (!bounds.IncludeUpper && NearlyEqual(val, bounds.Upper)))
            {
                result = bounds.IncludeUpper ? bounds.Upper : NextAfter(bounds.Upper, bounds.Lower);
            }

            if (!bounds.IncludeLower && result <= bounds.Lower)
                result = NextAfter(bounds.Lower, bounds.Upper);
            if (!bounds.IncludeUpper && result >= bounds.Upper)
                result = NextAfter(bounds.Upper, bounds.Lower);

            return Value.FromNumber(result);
        }

        private Value WrapValue(Value input, RangeBounds bounds, int position)
        {
            if (double.IsNaN(bounds.Lower) || double.IsNaN(bounds.Upper))
                return Value.Undefined;

            double width = bounds.Upper - bounds.Lower;
            if (width <= 0)
                return Value.Undefined;

            double val = ToNumberAt(input, position);

            double wrapped = val - bounds.Lower;
            wrapped %= width;
            if (wrapped < 0)
                wrapped += width;
            wrapped += bounds.Lower;

            if (bounds.IncludeUpper && NearlyEqual(val, bounds.Upper))
                wrapped = bounds.Upper;

            if (!bounds.IncludeLower && NearlyEqual(wrapped, bounds.Lower))
                wrapped = NextAfter(bounds.Lower, bounds.Upper);
            if (!bounds.IncludeUpper && NearlyEqual(wrapped, bounds.Upper))
                wrapped = NextAfter(bounds.Upper, bounds.Lower);

            return Value.FromNumber(wrapped);
        }

        private static bool NearlyEqual(double a, double b, double epsilon = 1e-9)
        {
            return Math.Abs(a - b) <= epsilon * Math.Max(1.0, Math.Max(Math.Abs(a), Math.Abs(b)));
        }

        private static double NextAfter(double start, double towards)
        {
            if (double.IsNaN(start) || double.IsNaN(towards))
                return double.NaN;

            if (towards > start)
                return Math.BitIncrement(start);
            if (towards < start)
                return Math.BitDecrement(start);
            return start;
        }

        private Value ResolveAssignable(Expression target, int position, out Action<Value> assign, out string? mutatedName)
        {
            mutatedName = null;

            if (TryGetName(target, out var idName))
            {
                if (_constStore.TryGet(idName, out _))
                    throw new InterpreterException($"Cannot assign to const const const variable '{idName}'.", position);

                // funkcja lokalna
                if (_callStack.Count > 0)
                {
                    var frame = _callStack.Peek();
                    if (frame.Locals.ContainsKey(idName))
                    {
                        var currentLocal = frame.Locals[idName];
                        assign = v =>
                        {
                            frame.Locals[idName] = v;
                        };
                        mutatedName = idName;
                        return currentLocal;
                    }
                }

                if (!_variables.TryGet(idName, out var value))
                    throw new InterpreterException($"Variable '{idName}' is not defined.", position);

                assign = v => _variables.Assign(idName, v, _currentStatementIndex);
                mutatedName = idName;
                return value;
            }

            if (target is IndexExpression idx)
            {
                Value container = EvaluateExpression(idx.Target);
                if (container.Kind == ValueKind.Array && container.Array != null)
                {
                    Value indexVal = EvaluateExpression(idx.Index);
                    double index = ToNumberAt(indexVal, idx.Index.Position);

                    var dict = new Dictionary<double, Value>(container.Array);
                    mutatedName = TryGetName(idx.Target, out var idxName) ? idxName : null;

                    assign = v =>
                    {
                        dict[index] = v;

                        if (TryGetName(idx.Target, out var arrName) && !_constStore.TryGet(arrName, out _))
                        {
                            Value newArray = Value.FromArray(dict);

                            if (_callStack.Count > 0)
                            {
                                var frame = _callStack.Peek();
                                if (frame.Locals.ContainsKey(arrName))
                                {
                                    frame.Locals[arrName] = newArray;
                                    return;
                                }
                            }

                            _variables.Assign(arrName, newArray, _currentStatementIndex);
                        }
                    };

                    return dict.TryGetValue(index, out var currentElement) ? currentElement : Value.Undefined;
                }

                if (container.Kind == ValueKind.Object && container.Object != null)
                {
                    var instance = container.Object;
                    Value indexVal = EvaluateExpression(idx.Index);
                    string fieldKey = ToFieldKey(indexVal);

                    mutatedName = TryGetName(idx.Target, out var idxName) ? idxName : null;
                    string? aliasForAssign = mutatedName;

                    assign = v =>
                    {
                        AssignObjectField(instance, fieldKey, v, aliasForAssign, notifyAlias: false);
                    };

                    if (IsStaticField(instance, fieldKey))
                        return instance.Definition.StaticFields.TryGetValue(fieldKey, out var staticField) ? staticField : Value.Undefined;

                    return instance.Fields.TryGetValue(fieldKey, out var currentField) ? currentField : Value.Undefined;
                }

                throw new InterpreterException("Index update is only supported on arrays or class instances.", position);
            }

            throw new InterpreterException("Update target must be a variable or array element.", position);
        }

        private long ToInt64At(Value value, int position)
        {
            double number = ToNumberAt(value, position);
            return (long)number;
        }

        private Value EvaluateCall(CallExpression call)
        {
            if (TryGetCalleeName(call.Callee, out var name))
            {
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

                // std lib
                if (_stdLibMethods.TryGetValue(name, out var stdFunc))
                {
                    return stdFunc(call);
                }

            }

            Value calleeValue = EvaluateExpression(call.Callee);
            if (calleeValue.Kind == ValueKind.Method && calleeValue.Method != null)
            {
                return InvokeBoundMethod(calleeValue.Method, call.Arguments);
            }

            string calleeName = TryGetCalleeName(call.Callee, out var idName) ? idName : call.Callee.GetType().Name;
            throw new InterpreterException($"Invalid function call on '{calleeName}'.", call.Position);
        }

        private static bool TryGetCalleeName(Expression expr, out string name)
        {
            switch (expr)
            {
                case IdentifierExpression ident:
                    name = ident.Name;
                    return true;
                case NumberIdentifierExpression numIdent:
                    name = numIdent.Name;
                    return true;
                case StringIdentifierExpression strIdent:
                    name = strIdent.Name;
                    return true;
                default:
                    name = string.Empty;
                    return false;
            }
        }
        private Value EvaluatePreviousCall(CallExpression call)
        {
            if (call.Arguments.Count != 1)
                throw new InterpreterException("previous(x) expects a single identifier or field argument.", call.Position);

            var arg = call.Arguments[0];

            if (TryGetName(arg, out var targetName))
            {
                if (!_variables.TryPrevious(targetName, out var newVal, out var changed))
                    return Value.Undefined;

                if (changed)
                    OnVariableMutated(targetName);

                return newVal;
            }

            if (TryGetFieldContext(arg, out var instance, out var fieldKey, out var alias))
            {
                if (!TryPreviousField(instance, fieldKey, out var newVal, out var changed))
                    return Value.Undefined;

                if (changed)
                {
                    OnVariableMutated(instance.Name);
                    if (!string.IsNullOrEmpty(alias) && alias != instance.Name)
                        OnVariableMutated(alias);
                }

                return newVal;
            }

            throw new InterpreterException("previous(x) expects a single identifier or field argument.", call.Position);
        }

        private Value EvaluateNextCall(CallExpression call)
        {
            if (call.Arguments.Count != 1)
                throw new InterpreterException("next(x) expects a single identifier or field argument.", call.Position);

            var arg = call.Arguments[0];

            if (TryGetName(arg, out var targetName))
            {
                if (!_variables.TryNext(targetName, out var newVal, out var changed))
                    return Value.Undefined;

                if (changed)
                    OnVariableMutated(targetName);

                return newVal;
            }

            if (TryGetFieldContext(arg, out var instance, out var fieldKey, out var alias))
            {
                if (!TryNextField(instance, fieldKey, out var newVal, out var changed))
                    return Value.Undefined;

                if (changed)
                {
                    OnVariableMutated(instance.Name);
                    if (!string.IsNullOrEmpty(alias) && alias != instance.Name)
                        OnVariableMutated(alias);
                }

                return newVal;
            }

            throw new InterpreterException("next(x) expects a single identifier or field argument.", call.Position);
        }

        private Value EvaluateHistoryCall(CallExpression call)
        {
            if (call.Arguments.Count != 1)
                throw new InterpreterException("history(x) expects a single identifier or field argument.", call.Position);

            var arg = call.Arguments[0];

            if (TryGetName(arg, out var targetName))
            {
                if (!_variables.TryGetHistory(targetName, out var values, out var currentIndex) ||
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

            if (TryGetFieldContext(arg, out var instance, out var fieldKey, out _))
            {
                if (!TryGetFieldHistory(instance, fieldKey, out var hist) || hist.Values.Count == 0)
                    return Value.FromArray(new Dictionary<double, Value>());

                var dict = new Dictionary<double, Value>();
                for (int i = 0; i < hist.Values.Count; i++)
                {
                    double idx = i - 1;
                    dict[idx] = hist.Values[i];
                }

                return Value.FromArray(dict);
            }

            if (arg is IndexExpression idxArg)
            {
                if (TryGetName(idxArg.Target, out var idxName))
                {
                    if (!_variables.TryGetHistory(idxName, out var values, out _) || values.Count == 0)
                        return Value.Undefined;

                    var dict = new Dictionary<double, Value>();
                    for (int i = 0; i < values.Count; i++)
                    {
                        double idx = i - 1;
                        dict[idx] = values[i];
                    }

                    double wantedIndex = ToNumberAt(EvaluateExpression(idxArg.Index), idxArg.Index.Position);
                    return dict.TryGetValue(wantedIndex, out var element) ? element : Value.Undefined;
                }

                if (TryGetFieldContext(idxArg.Target, out var targetInstance, out var targetFieldKey, out _))
                {
                    if (!TryGetFieldHistory(targetInstance, targetFieldKey, out var hist) || hist.Values.Count == 0)
                        return Value.Undefined;

                    var dict = new Dictionary<double, Value>();
                    for (int i = 0; i < hist.Values.Count; i++)
                    {
                        double idx = i - 1;
                        dict[idx] = hist.Values[i];
                    }

                    double wantedIndex = ToNumberAt(EvaluateExpression(idxArg.Index), idxArg.Index.Position);
                    return dict.TryGetValue(wantedIndex, out var element) ? element : Value.Undefined;
                }
            }

            throw new InterpreterException("history(x) expects a single identifier or field argument.", call.Position);
        }

        private bool TryGetFieldContext(Expression expr, out ClassInstance instance, out string fieldKey, out string? aliasName)
        {
            instance = null!;
            fieldKey = string.Empty;
            aliasName = null;

            if (expr is CallExpression call)
            {
                if (call.Callee is IndexExpression idxCallee)
                {
                    expr = idxCallee;
                }
                else if (call.Arguments.Count == 1)
                {
                    expr = new IndexExpression(call.Callee, call.Arguments[0], expr.Position);
                }
            }

            if (expr is not IndexExpression idx)
                return false;

            Value targetVal = EvaluateExpression(idx.Target);
            if (targetVal.Kind != ValueKind.Object || targetVal.Object == null)
                return false;

            Value indexVal = EvaluateExpression(idx.Index);
            fieldKey = ToFieldKey(indexVal);
            instance = targetVal.Object;
            aliasName = TryGetName(idx.Target, out var nm) ? nm : null;
            return true;
        }

        private bool TryGetFieldHistory(ClassInstance instance, string fieldKey, out FieldHistory history)
        {
            bool isStatic = IsStaticField(instance, fieldKey);
            string prefix = isStatic ? "static::" : string.Empty;
            string key = $"{prefix}{instance.Name}::{fieldKey}";
            var dict = isStatic ? _staticFieldHistory : _fieldHistory;
            return dict.TryGetValue(key, out history);
        }

        private bool TryPreviousField(ClassInstance instance, string fieldKey, out Value newValue, out bool changed)
        {
            newValue = Value.Undefined;
            changed = false;

            if (!TryGetFieldHistory(instance, fieldKey, out var history) || history.Values.Count == 0)
                return false;

            if (history.Index <= 0)
            {
                newValue = history.Values[0];
            }
            else
            {
                history.Index--;
                newValue = history.Values[history.Index];
            }

            Value currentVal = IsStaticField(instance, fieldKey)
                ? (instance.Definition.StaticFields.TryGetValue(fieldKey, out var stat) ? stat : Value.Undefined)
                : (instance.Fields.TryGetValue(fieldKey, out var inst) ? inst : Value.Undefined);
            changed = !currentVal.StrictEquals(newValue);
            if (changed)
            {
                if (IsStaticField(instance, fieldKey))
                    instance.Definition.StaticFields[fieldKey] = newValue;
                else
                    instance.Fields[fieldKey] = newValue;
            }

            return true;
        }

        private bool TryNextField(ClassInstance instance, string fieldKey, out Value newValue, out bool changed)
        {
            newValue = Value.Undefined;
            changed = false;

            if (!TryGetFieldHistory(instance, fieldKey, out var history) || history.Values.Count == 0)
                return false;

            if (history.Index >= history.Values.Count - 1)
            {
                newValue = history.Values[history.Values.Count - 1];
            }
            else
            {
                history.Index++;
                newValue = history.Values[history.Index];
            }

            Value currentVal = IsStaticField(instance, fieldKey)
                ? (instance.Definition.StaticFields.TryGetValue(fieldKey, out var stat) ? stat : Value.Undefined)
                : (instance.Fields.TryGetValue(fieldKey, out var inst) ? inst : Value.Undefined);
            changed = !currentVal.StrictEquals(newValue);
            if (changed)
            {
                if (IsStaticField(instance, fieldKey))
                    instance.Definition.StaticFields[fieldKey] = newValue;
                else
                    instance.Fields[fieldKey] = newValue;
            }

            return true;
        }

        private static bool TryGetName(Expression expr, out string name)
        {
            switch (expr)
            {
                case IdentifierExpression id:
                    name = id.Name;
                    return true;
                case NumberIdentifierExpression numId:
                    name = numId.Name;
                    return true;
                case StringIdentifierExpression strId:
                    name = strId.Name;
                    return true;
                default:
                    name = string.Empty;
                    return false;
            }
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
                    if (TryParseNumberWords(x.String ?? string.Empty, out double fromWords))
                        return Value.FromNumber(fromWords);
                    return Value.Undefined;

                case ValueKind.Null:
                case ValueKind.Undefined:
                    return Value.Undefined;

                default:
                    return Value.Undefined;
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

        private bool TryParseNumberWords(string text, out double number)
        {
            number = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string[] rawWords = text.Replace("-", " ").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

            ulong total = 0;
            ulong chunk = 0;
            bool sawAny = false;

            try
            {
                foreach (var rawWord in rawWords)
                {
                    string word = NormalizeNumberWord(rawWord);

                    if (string.Equals(word, "and", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(word, "i", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!sawAny)
                            return false; // samo 'and'/'i' nie jest liczba
                        continue;
                    }

                    if (NumberUnits.TryGetValue(word, out var unit))
                    {
                        checked { chunk += unit; }
                        sawAny = true;
                        continue;
                    }

                    if (NumberTens.TryGetValue(word, out var tens))
                    {
                        checked { chunk += tens; }
                        sawAny = true;
                        continue;
                    }

                    if (string.Equals(word, "hundred", StringComparison.OrdinalIgnoreCase))
                    {
                        if (chunk == 0)
                            return false;

                        checked { chunk *= 100; }
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
                            total += chunk;
                        }
                        chunk = 0;
                        sawAny = true;
                        continue;
                    }

                    // nieznane slowo => nie parsujemy (zostajemy przy Undefined)
                    return false;
                }

                checked { total += chunk; }
                if (!sawAny)
                    return false;

                number = total;
                return true;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private string NormalizeNumberWord(string raw)
        {
            string w = raw;

            if (NumberAliases.TryGetValue(w, out var alias))
                w = alias;

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
