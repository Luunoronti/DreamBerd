// Evaluator.FunctionDefinition.cs
namespace DreamberdInterpreter
{
    public sealed partial class Evaluator
    {
        private sealed class FunctionDefinition
        {
            public IReadOnlyList<string> Parameters
            {
                get;
            }
            public Statement Body
            {
                get;
            }

            public FunctionDefinition(IReadOnlyList<string> parameters, Statement body)
            {
                Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
                Body = body ?? throw new ArgumentNullException(nameof(body));
            }
        }
    }
}
