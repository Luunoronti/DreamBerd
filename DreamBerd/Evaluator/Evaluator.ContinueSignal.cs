// Evaluator.ContinueSignal.cs
namespace DreamberdInterpreter
{
    public sealed partial class Evaluator
    {
        /// <summary>
        /// Wewnętrzny sygnał continue (następna iteracja najbliższej pętli while).
        /// </summary>
        private sealed class ContinueSignal : Exception
        {
        }
    }
}
