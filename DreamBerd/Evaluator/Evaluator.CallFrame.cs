// Evaluator.CallFrame.cs
namespace DreamberdInterpreter
{
    public sealed partial class Evaluator
    {
        private sealed class CallFrame
        {
            public Dictionary<string, Value> Locals
            {
                get;
            } =
                new Dictionary<string, Value>(StringComparer.Ordinal);
        }
    }
}
