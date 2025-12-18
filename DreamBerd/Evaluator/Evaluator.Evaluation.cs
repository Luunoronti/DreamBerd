// Evaluator.Evaluation.cs
using System.Collections.Generic;
using System.Globalization;

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

            switch (unary.Operator)
            {
                case UnaryOperator.Negate:
                    return Value.FromNumber(-ToNumberAt(operand, unary.Operand.Position));

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

        private Value ResolveAssignable(Expression target, int position, out Action<Value> assign, out string? mutatedName)
        {
            mutatedName = null;

            if (target is IdentifierExpression ident)
            {
                if (_constStore.TryGet(ident.Name, out _))
                    throw new InterpreterException($"Cannot assign to const const const variable '{ident.Name}'.", position);

                // funkcja lokalna
                if (_callStack.Count > 0)
                {
                    var frame = _callStack.Peek();
                    if (frame.Locals.ContainsKey(ident.Name))
                    {
                        var currentLocal = frame.Locals[ident.Name];
                        assign = v =>
                        {
                            frame.Locals[ident.Name] = v;
                        };
                        mutatedName = ident.Name;
                        return currentLocal;
                    }
                }

                if (!_variables.TryGet(ident.Name, out var value))
                    throw new InterpreterException($"Variable '{ident.Name}' is not defined.", position);

                assign = v => _variables.Assign(ident.Name, v, _currentStatementIndex);
                mutatedName = ident.Name;
                return value;
            }

            if (target is IndexExpression idx)
            {
                Value container = EvaluateExpression(idx.Target);
                if (container.Kind != ValueKind.Array || container.Array == null)
                    throw new InterpreterException("Index update is only supported on arrays.", position);

                Value indexVal = EvaluateExpression(idx.Index);
                double index = ToNumberAt(indexVal, idx.Index.Position);

                var dict = new Dictionary<double, Value>(container.Array);
                mutatedName = (idx.Target is IdentifierExpression idt) ? idt.Name : null;

                assign = v =>
                {
                    dict[index] = v;

                    if (idx.Target is IdentifierExpression arrIdent &&
                        !_constStore.TryGet(arrIdent.Name, out _))
                    {
                        Value newArray = Value.FromArray(dict);

                        if (_callStack.Count > 0)
                        {
                            var frame = _callStack.Peek();
                            if (frame.Locals.ContainsKey(arrIdent.Name))
                            {
                                frame.Locals[arrIdent.Name] = newArray;
                                return;
                            }
                        }

                        _variables.Assign(arrIdent.Name, newArray, _currentStatementIndex);
                    }
                };

                return dict.TryGetValue(index, out var currentElement) ? currentElement : Value.Undefined;
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

                // std lib
                if (_stdLibMethods.TryGetValue(name, out var stdFunc))
                {
                    return stdFunc(call);
                }

            }
            string calleeName = call.Callee is IdentifierExpression id ? id.Name : call.Callee.GetType().Name;
            throw new InterpreterException($"Invalid function call on '{calleeName}'.", call.Position);

            //throw new InterpreterException(
            //    "Only the built-in functions print(...), previous(...), next(...), history(...), " +
            //    "and user-defined functions are supported at this time.");
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
