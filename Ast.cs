// Ast.cs
using System;
using System.Collections.Generic;

namespace DreamberdInterpreter
{
    public enum UnaryOperator
    {
        Negate
    }

    public enum BinaryOperator
    {
        Add,
        Subtract,
        Multiply,
        Divide,
        SingleEqual, // '=' (ultra-luźna równość)
        Equal,        // '=='
        DoubleEqual,  // '==='
        TripleEqual,  // '===='
        Less,
        Greater,
        LessOrEqual,
        GreaterOrEqual
    }

    public enum Mutability
    {
        VarVar,
        VarConst,
        ConstVar,
        ConstConst
    }

    public enum DeclarationKind
    {
        Normal,
        ConstConstConst
    }

    /// <summary>
    /// Bazowy typ wszystkich nodów AST, z pozycją (0-based) w źródle.
    /// </summary>
    public abstract class Node
    {
        public int Position { get; }

        protected Node(int position)
        {
            Position = position;
        }
    }

    public abstract class Statement : Node
    {
        protected Statement(int position)
            : base(position)
        {
        }
    }

    public sealed class VariableDeclarationStatement : Statement
    {
        public DeclarationKind DeclarationKind { get; }
        public Mutability Mutability { get; }
        public string Name { get; }
        public LifetimeSpecifier Lifetime { get; }
        public int Priority { get; }
        public Expression Initializer { get; }

        public VariableDeclarationStatement(
            DeclarationKind declarationKind,
            Mutability mutability,
            string name,
            LifetimeSpecifier lifetime,
            int priority,
            Expression initializer,
            int position)
            : base(position)
        {
            DeclarationKind = declarationKind;
            Mutability = mutability;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Lifetime = lifetime;
            Priority = priority;
            Initializer = initializer ?? throw new ArgumentNullException(nameof(initializer));
        }
    }

    public sealed class ExpressionStatement : Statement
    {
        public Expression Expression { get; }
        public bool IsDebug { get; }

        public ExpressionStatement(Expression expression, bool isDebug, int position)
            : base(position)
        {
            Expression = expression ?? throw new ArgumentNullException(nameof(expression));
            IsDebug = isDebug;
        }
    }

    public sealed class ReverseStatement : Statement
    {
        public bool IsDebug { get; }

        public ReverseStatement(bool isDebug, int position)
            : base(position)
        {
            IsDebug = isDebug;
        }
    }

    public sealed class ForwardStatement : Statement
    {
        public bool IsDebug { get; }

        public ForwardStatement(bool isDebug, int position)
            : base(position)
        {
            IsDebug = isDebug;
        }
    }

    public sealed class DeleteStatement : Statement
    {
        public Expression Target { get; }
        public bool IsDebug { get; }

        public DeleteStatement(Expression target, bool isDebug, int position)
            : base(position)
        {
            Target = target ?? throw new ArgumentNullException(nameof(target));
            IsDebug = isDebug;
        }
    }

    public sealed class WhenStatement : Statement
    {
        public Expression Condition { get; }
        public Statement Body { get; }

        public WhenStatement(Expression condition, Statement body, int position)
            : base(position)
        {
            Condition = condition ?? throw new ArgumentNullException(nameof(condition));
            Body = body ?? throw new ArgumentNullException(nameof(body));
        }
    }

    /// <summary>
    /// return expr!
    /// return!
    ///
    /// Zwraca wartość z funkcji. Poza funkcją to błąd.
    /// </summary>
    public sealed class ReturnStatement : Statement
    {
        public Expression? Expression { get; }

        public ReturnStatement(Expression? expression, int position)
            : base(position)
        {
            Expression = expression;
        }
    }

    public sealed class FunctionDeclarationStatement : Statement
    {
        public string Name { get; }
        public IReadOnlyList<string> Parameters { get; }
        public Statement Body { get; }

        public FunctionDeclarationStatement(string name, IReadOnlyList<string> parameters, Statement body, int position)
            : base(position)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
            Body = body ?? throw new ArgumentNullException(nameof(body));
        }
    }

    public sealed class IfStatement : Statement
    {
        public Expression Condition { get; }
        public Statement ThenBranch { get; }
        public Statement? ElseBranch { get; }

        public IfStatement(Expression condition, Statement thenBranch, Statement? elseBranch, int position)
            : base(position)
        {
            Condition = condition ?? throw new ArgumentNullException(nameof(condition));
            ThenBranch = thenBranch ?? throw new ArgumentNullException(nameof(thenBranch));
            ElseBranch = elseBranch;
        }
    }

    /// <summary>
    /// while (condition) body
    ///
    /// Uwaga: to jest statement, więc samo "while" nie kończy się '!'.
    /// Terminatory są wewnątrz body (np. print(...)!).
    /// </summary>
    public sealed class WhileStatement : Statement
    {
        public Expression Condition { get; }
        public Statement Body { get; }

        public WhileStatement(Expression condition, Statement body, int position)
            : base(position)
        {
            Condition = condition ?? throw new ArgumentNullException(nameof(condition));
            Body = body ?? throw new ArgumentNullException(nameof(body));
        }
    }

    /// <summary>
    /// break!
    /// </summary>
    public sealed class BreakStatement : Statement
    {
        public BreakStatement(int position)
            : base(position)
        {
        }
    }

    /// <summary>
    /// continue!
    /// </summary>
    public sealed class ContinueStatement : Statement
    {
        public ContinueStatement(int position)
            : base(position)
        {
        }
    }

    /// <summary>
    /// Blok { ... }.
    ///
    /// Uwaga: na tym etapie to jest tylko "grupowanie" instrukcji.
    /// Scope'y blokowe dojdą później.
    /// </summary>
    public sealed class BlockStatement : Statement
    {
        public IReadOnlyList<Statement> Statements { get; }

        public BlockStatement(IReadOnlyList<Statement> statements, int position)
            : base(position)
        {
            Statements = statements ?? throw new ArgumentNullException(nameof(statements));
        }
    }

    public abstract class Expression : Node
    {
        protected Expression(int position)
            : base(position)
        {
        }
    }

    public sealed class LiteralExpression : Expression
    {
        public Value Value { get; }

        public LiteralExpression(Value value, int position)
            : base(position)
        {
            Value = value;
        }
    }

    public sealed class IdentifierExpression : Expression
    {
        public string Name { get; }

        public IdentifierExpression(string name, int position)
            : base(position)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }
    }

    public sealed class UnaryExpression : Expression
    {
        public UnaryOperator Operator { get; }
        public Expression Operand { get; }

        public UnaryExpression(UnaryOperator op, Expression operand, int position)
            : base(position)
        {
            Operator = op;
            Operand = operand ?? throw new ArgumentNullException(nameof(operand));
        }
    }

    public sealed class BinaryExpression : Expression
    {
        public Expression Left { get; }
        public BinaryOperator Operator { get; }
        public Expression Right { get; }

        public BinaryExpression(Expression left, BinaryOperator op, Expression right, int position)
            : base(position)
        {
            Left = left ?? throw new ArgumentNullException(nameof(left));
            Operator = op;
            Right = right ?? throw new ArgumentNullException(nameof(right));
        }
    }

    public sealed class AssignmentExpression : Expression
    {
        public string Name { get; }
        public Expression ValueExpression { get; }

        public AssignmentExpression(string name, Expression valueExpression, int position)
            : base(position)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            ValueExpression = valueExpression ?? throw new ArgumentNullException(nameof(valueExpression));
        }
    }

    public sealed class IndexAssignmentExpression : Expression
    {
        public Expression Target { get; }
        public Expression Index { get; }
        public Expression ValueExpression { get; }

        public IndexAssignmentExpression(Expression target, Expression index, Expression valueExpression, int position)
            : base(position)
        {
            Target = target ?? throw new ArgumentNullException(nameof(target));
            Index = index ?? throw new ArgumentNullException(nameof(index));
            ValueExpression = valueExpression ?? throw new ArgumentNullException(nameof(valueExpression));
        }
    }

    public sealed class CallExpression : Expression
    {
        public Expression Callee { get; }
        public IReadOnlyList<Expression> Arguments { get; }

        public CallExpression(Expression callee, IReadOnlyList<Expression> arguments, int position)
            : base(position)
        {
            Callee = callee ?? throw new ArgumentNullException(nameof(callee));
            Arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
        }
    }

    public sealed class ArrayLiteralExpression : Expression
    {
        public IReadOnlyList<Expression> Elements { get; }

        public ArrayLiteralExpression(IReadOnlyList<Expression> elements, int position)
            : base(position)
        {
            Elements = elements ?? throw new ArgumentNullException(nameof(elements));
        }
    }

    public sealed class IndexExpression : Expression
    {
        public Expression Target { get; }
        public Expression Index { get; }

        public IndexExpression(Expression target, Expression index, int position)
            : base(position)
        {
            Target = target ?? throw new ArgumentNullException(nameof(target));
            Index = index ?? throw new ArgumentNullException(nameof(index));
        }
    }

    public sealed class ConditionalExpression : Expression
    {
        public Expression Condition { get; }
        public Expression WhenTrue { get; }
        public Expression WhenFalse { get; }
        public Expression WhenMaybe { get; }
        public Expression WhenUndefined { get; }

        public ConditionalExpression(
            Expression condition,
            Expression whenTrue,
            Expression whenFalse,
            Expression whenMaybe,
            Expression whenUndefined,
            int position)
            : base(position)
        {
            Condition = condition ?? throw new ArgumentNullException(nameof(condition));
            WhenTrue = whenTrue ?? throw new ArgumentNullException(nameof(whenTrue));
            WhenFalse = whenFalse ?? throw new ArgumentNullException(nameof(whenFalse));
            WhenMaybe = whenMaybe ?? throw new ArgumentNullException(nameof(whenMaybe));
            WhenUndefined = whenUndefined ?? throw new ArgumentNullException(nameof(whenUndefined));
        }
    }
}
