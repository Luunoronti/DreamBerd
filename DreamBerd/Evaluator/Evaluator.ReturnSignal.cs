// Evaluator.ReturnSignal.cs
namespace DreamberdInterpreter
{
    public sealed partial class Evaluator
    {
        /// <summary>
        /// Wewnętrzny "sygnał" return z funkcji.
        /// Dzięki temu return może przerwać wykonanie dowolnie zagnieżdżonego bloku.
        /// </summary>
        private sealed class ReturnSignal : Exception
        {
            public Value Value
            {
                get;
            }

            public ReturnSignal(Value value)
            {
                Value = value;
            }
        }
    }
}
