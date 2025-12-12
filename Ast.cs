// Ast.cs
using System.Collections.Generic;

namespace DreamberdInterpreter
{
    public abstract class AstNode
    {
    }

    public abstract class Statement : AstNode
    {
    }

    public abstract class Expression : AstNode
    {
    }

    public enum Mutability
    {
        ConstConst,
        ConstVar,
        VarConst,
        VarVar
    }

    public enum DeclarationKind
    {
        Normal,
        ConstConstConst
    }

    public enum LifetimeKind
    {
        None,
        Lines,
        Seconds,
        Infinity
    }

    public readonly struct LifetimeSpecifier
    {
        public LifetimeKind Kind
        {
            get;
        }
        public double Value
        {
            get;
        }

        public LifetimeSpecifier(LifetimeKind kind, double value)
        {
            Kind = kind;
            Value = value;
        }

        public static LifetimeSpecifier None => new LifetimeSpecifier(LifetimeKind.None, 0);

        public bool IsNone => Kind == LifetimeKind.None;
    }

    public sealed class VariableDeclarationStatement : Statement
    {
        public DeclarationKind DeclarationKind
        {
            get;
        }
        public Mutability Mutability
        {
            get;
        }
        public string Name
        {
            get;
        }
        public Expression Initializer
        {
            get;
        }
        public int Priority
        {
            get;
        }
        public LifetimeSpecifier Lifetime
        {
            get;
        }

        public VariableDeclarationStatement(
            DeclarationKind declarationKind,
            Mutability mutability,
            string name,
            Expression initializer,
            int priority,
            LifetimeSpecifier lifetime)
        {
            DeclarationKind = declarationKind;
            Mutability = mutability;
            Name = name;
            Initializer = initializer;
            Priority = priority;
            Lifetime = lifetime;
        }
    }

    public sealed class ExpressionStatement : Statement
    {
        public Expression Expression
        {
            get;
        }
        public int Priority
        {
            get;
        }
        public bool IsDebug
        {
            get;
        }

        public ExpressionStatement(Expression expression, int priority, bool isDebug)
        {
            Expression = expression;
            Priority = priority;
            IsDebug = isDebug;
        }
    }

    public sealed class ReverseStatement : Statement
    {
        public int Priority
        {
            get;
        }
        public bool IsDebug
        {
            get;
        }

        public ReverseStatement(int priority, bool isDebug)
        {
            Priority = priority;
            IsDebug = isDebug;
        }
    }

    public sealed class ForwardStatement : Statement
    {
        public int Priority
        {
            get;
        }
        public bool IsDebug
        {
            get;
        }

        public ForwardStatement(int priority, bool isDebug)
        {
            Priority = priority;
            IsDebug = isDebug;
        }
    }

    public sealed class DeleteStatement : Statement
    {
        public Expression Target
        {
            get;
        }
        public int Priority
        {
            get;
        }
        public bool IsDebug
        {
            get;
        }

        public DeleteStatement(Expression target, int priority, bool isDebug)
        {
            Target = target;
            Priority = priority;
            IsDebug = isDebug;
        }
    }

    public sealed class WhenStatement : Statement
    {
        public Expression Condition
        {
            get;
        }
        public Statement Body
        {
            get;
        }

        public WhenStatement(Expression condition, Statement body)
        {
            Condition = condition;
            Body = body;
        }
    }

    public enum BinaryOperator
    {
        Add,
        Subtract,
        Multiply,
        Divide,
        Equal,
        DoubleEqual,
        TripleEqual,
        QuadEqual,
        Less,
        Greater,
        LessOrEqual,
        GreaterOrEqual
    }

    public sealed class BinaryExpression : Expression
    {
        public Expression Left
        {
            get;
        }
        public Expression Right
        {
            get;
        }
        public BinaryOperator Operator
        {
            get;
        }

        public BinaryExpression(Expression left, Expression right, BinaryOperator op)
        {
            Left = left;
            Right = right;
            Operator = op;
        }
    }

    public enum UnaryOperator
    {
        Negate
    }

    public sealed class UnaryExpression : Expression
    {
        public Expression Operand
        {
            get;
        }
        public UnaryOperator Operator
        {
            get;
        }

        public UnaryExpression(UnaryOperator op, Expression operand)
        {
            Operator = op;
            Operand = operand;
        }
    }

    public sealed class LiteralExpression : Expression
    {
        public Value Value
        {
            get;
        }

        public LiteralExpression(Value value)
        {
            Value = value;
        }
    }

    public sealed class IdentifierExpression : Expression
    {
        public string Name
        {
            get;
        }

        public IdentifierExpression(string name)
        {
            Name = name;
        }
    }

    public sealed class AssignmentExpression : Expression
    {
        public string Name
        {
            get;
        }
        public Expression ValueExpression
        {
            get;
        }

        public AssignmentExpression(string name, Expression valueExpression)
        {
            Name = name;
            ValueExpression = valueExpression;
        }
    }

    public sealed class CallExpression : Expression
    {
        public Expression Callee
        {
            get;
        }
        public IReadOnlyList<Expression> Arguments
        {
            get;
        }

        public CallExpression(Expression callee, IReadOnlyList<Expression> arguments)
        {
            Callee = callee;
            Arguments = arguments;
        }
    }

    public sealed class ArrayLiteralExpression : Expression
    {
        public IReadOnlyList<Expression> Elements
        {
            get;
        }

        public ArrayLiteralExpression(IReadOnlyList<Expression> elements)
        {
            Elements = elements;
        }
    }

    public sealed class IndexExpression : Expression
    {
        public Expression Target
        {
            get;
        }
        public Expression Index
        {
            get;
        }

        public IndexExpression(Expression target, Expression index)
        {
            Target = target;
            Index = index;
        }
    }

    public sealed class IndexAssignmentExpression : Expression
    {
        public Expression Target
        {
            get;
        }
        public Expression Index
        {
            get;
        }
        public Expression ValueExpression
        {
            get;
        }

        public IndexAssignmentExpression(Expression target, Expression index, Expression valueExpression)
        {
            Target = target;
            Index = index;
            ValueExpression = valueExpression;
        }
    }
}
