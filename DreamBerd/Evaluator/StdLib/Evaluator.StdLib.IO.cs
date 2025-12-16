// Evaluator.StdLib.IO.cs
using System.Diagnostics;

namespace DreamberdInterpreter
{
    public sealed partial class Evaluator
    {

        public TimeSpan IOTotalTime { get; private set; }
        public string CurrentDirectory { get; set; }


        private Value EvaluateReadFileCall(CallExpression call)
        {
            if (call.Arguments.Count != 1)
                throw new InterpreterException("readFile(path) expects exactly one argument.", call.Position);

            Value pathVal = EvaluateExpression(call.Arguments[0]);
            if (pathVal.Kind != ValueKind.String)
                throw new InterpreterException("readFile(path) expects a string path.", call.Arguments[0].Position);

            string path = pathVal.String ?? string.Empty;

            try
            {
                if (!Path.IsPathFullyQualified(path))
                    path = CurrentDirectory + "\\" + path;

                var time = Stopwatch.GetTimestamp();
                string text = File.ReadAllText(path);
                IOTotalTime += Stopwatch.GetElapsedTime(time);
                return Value.FromString(text);
            }
            catch (Exception ex)
            {
                throw new InterpreterException($"readFile(path) failed: {ex.Message}", call.Position);
            }
        }

        private Value EvaluateReadLinesCall(CallExpression call)
        {
            if (call.Arguments.Count != 1)
                throw new InterpreterException("readLines(path) expects exactly one argument.", call.Position);

            Value pathVal = EvaluateExpression(call.Arguments[0]);
            if (pathVal.Kind != ValueKind.String)
                throw new InterpreterException("readLines(path) expects a string path.", call.Arguments[0].Position);

            string path = pathVal.String ?? string.Empty;

            try
            {
                if (!Path.IsPathFullyQualified(path))
                    path = CurrentDirectory + "\\" + path;
                // ReadAllText + our own splitting keeps behaviour consistent with lines(text)
                var time = Stopwatch.GetTimestamp();
                string text = File.ReadAllText(path);
                IOTotalTime += Stopwatch.GetElapsedTime(time);

                var items = SplitLines(text);
                return MakeStringArray(items);
            }
            catch (Exception ex)
            {
                throw new InterpreterException($"readLines(path) failed: {ex.Message}", call.Position);
            }
        }
    }
}
