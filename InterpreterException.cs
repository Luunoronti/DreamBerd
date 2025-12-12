using System;

namespace DreamberdInterpreter
{
    public class InterpreterException : Exception
    {
        public InterpreterException(string message)
            : base(message)
        {
        }
    }
}
