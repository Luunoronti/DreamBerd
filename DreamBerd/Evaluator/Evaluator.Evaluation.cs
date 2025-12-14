// Evaluator.Evaluation.cs
using System.Globalization;

namespace DreamberdInterpreter
{
    public sealed partial class Evaluator
    {
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
                            if (cond.Kind == ValueKind.Boolean)
                            {
                                if (cond.Bool == BooleanState.Maybe)
                                {
                                    if (ifs.IdkBranch != null)
                                        return EvaluateStatement(ifs.IdkBranch);
                                    else
                                        return Value.Null;
                                }
                            }
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
    }
}
