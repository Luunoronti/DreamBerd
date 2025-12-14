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
        }
    }
}
