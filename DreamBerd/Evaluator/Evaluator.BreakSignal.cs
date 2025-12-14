// Evaluator.BreakSignal.cs
namespace DreamberdInterpreter
{
    public sealed partial class Evaluator
    {
        /// <summary>
        /// Wewnętrzny sygnał break (wyjście z najbliższej pętli while).
        /// </summary>
        private sealed class BreakSignal : Exception
        {
        }
    }
}
