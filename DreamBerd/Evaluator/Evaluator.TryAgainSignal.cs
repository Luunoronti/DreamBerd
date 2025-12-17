// Evaluator.TryAgainSignal.cs
namespace DreamberdInterpreter
{
    public sealed partial class Evaluator
    {
        /// <summary>
        /// Wewnętrzny sygnał 'try again' (powrót do najbliższego if/else/idk).
        /// </summary>
        private sealed class TryAgainSignal : Exception
        {
        }
    }
}
