// Evaluator.StdLib.IO.cs
using System.Xml.Linq;

namespace DreamberdInterpreter
{
    public sealed partial class Evaluator
    {
        private delegate Value EvaluatorDelegate(CallExpression call);
        private Dictionary<string, EvaluatorDelegate> _stdLibMethods = [];

        private void RegisterStdLibDefaultMethods()
        {
            _stdLibMethods.Add("readFile", EvaluateReadFileCall);
            _stdLibMethods.Add("readLines", EvaluateReadLinesCall);
            _stdLibMethods.Add("lines", EvaluateLinesCall);
            _stdLibMethods.Add("trim", EvaluateTrimCall);
            _stdLibMethods.Add("split", EvaluateSplitCall);
            _stdLibMethods.Add("charAt", EvaluateCharAtCall);
            _stdLibMethods.Add("slice", EvaluateSliceCall);

            _stdLibMethods.Add("toNumber", EvaluateToNumberCall);
            _stdLibMethods.Add("parseInt", EvaluateToNumberCall);
            _stdLibMethods.Add("parseNumber", EvaluateToNumberCall);

            _stdLibMethods.Add("numArray", EvaluateToArrayCall);
        }


        private static Value MakeNumberArray(List<double> items)
        {
            var dict = new Dictionary<double, Value>(items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                dict[i - 1] = Value.FromNumber(items[i]);
            }
            return Value.FromArray(dict);
        }

        private Value EvaluateToArrayCall(CallExpression call)
        {
            var initVal = EvaluateExpression(call.Arguments[0]);
            var sizeVal = EvaluateExpression(call.Arguments[1]);
            if (initVal.Kind != ValueKind.Number)
                throw new InterpreterException("numArray(init, size) expects init value as a number.", call.Arguments[0].Position);
            if (sizeVal.Kind != ValueKind.Number)
                throw new InterpreterException("numArray(init, size) expects size value as a number.", call.Arguments[1].Position);
            return MakeNumberArray(Enumerable.Repeat(initVal.Number, (int)sizeVal.Number).ToList());
        }

    }
}
