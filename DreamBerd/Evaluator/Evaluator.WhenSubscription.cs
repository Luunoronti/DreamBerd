// Evaluator.WhenSubscription.cs
namespace DreamberdInterpreter
{
    public sealed partial class Evaluator
    {
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
            public IReadOnlyCollection<string> Dependencies
            {
                get;
            }

            public WhenSubscription(Expression condition, Statement body, IReadOnlyCollection<string> dependencies)
            {
                Condition = condition ?? throw new ArgumentNullException(nameof(condition));
                Body = body ?? throw new ArgumentNullException(nameof(body));
                Dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
            }
        }
    }
}
