namespace DreamberdInterpreter
{
    public sealed partial class Parser
    {
        private readonly struct TerminatorInfo
        {
            public bool IsDebug
            {
                get;
            }

            /// <summary>
            /// Liczba wykrzykników na końcu statementu.
            /// Dla terminatora '?' wartość wynosi 0.
            /// </summary>
            public int ExclamationCount
            {
                get;
            }

            public TerminatorInfo(bool isDebug, int exclamationCount)
            {
                IsDebug = isDebug;
                ExclamationCount = exclamationCount;
            }
        }
    }
}
