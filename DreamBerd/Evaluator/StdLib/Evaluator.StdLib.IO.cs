// Evaluator.StdLib.IO.cs
namespace DreamberdInterpreter
{
    public sealed partial class Evaluator
    {
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
                string text = File.ReadAllText(path);
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
                // ReadAllText + our own splitting keeps behaviour consistent with lines(text)
                string text = File.ReadAllText(path);
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
