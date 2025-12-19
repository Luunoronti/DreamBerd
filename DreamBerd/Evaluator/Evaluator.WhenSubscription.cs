// Evaluator.WhenSubscription.cs
namespace DreamberdInterpreter
{
    public sealed partial class Evaluator
    {
        private sealed class WhenSubscription
        {
            public Expression? Condition
            {
                get;
            }
            public Expression? Target
            {
                get;
            }
            public Pattern? Pattern
            {
                get;
            }
            public Expression? Guard
            {
                get;
            }
            public Statement Body
            {
                get;
            }
            public IReadOnlyCollection<string> Dependencies
            {
                get;
            }

            public WhenSubscription(Expression condition, Statement body, IReadOnlyCollection<string> dependencies)
                : this(condition, null, null, null, body, dependencies)
            {
            }

            public WhenSubscription(Expression? condition, Expression? target, Pattern? pattern, Expression? guard, Statement body, IReadOnlyCollection<string> dependencies)
            {
                Condition = condition;
                Target = target;
                Pattern = pattern;
                Guard = guard;
                Body = body ?? throw new ArgumentNullException(nameof(body));
                Dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
            }
        }
    }
}
