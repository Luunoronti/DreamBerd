using System;

namespace DreamberdInterpreter
{
    public class InterpreterException : Exception
    {
        /// <summary>
        /// 0-based index w całym źródle. Null = brak informacji o pozycji.
        /// </summary>
        public int? Position { get; }

        public InterpreterException(string message)
            : base(message)
        {
        }

        public InterpreterException(string message, int position)
            : base(message)
        {
            Position = position;
        }
    }
}
