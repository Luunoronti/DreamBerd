// Evaluator.StdLib.cs
namespace DreamberdInterpreter
{
    public sealed partial class Evaluator
    {
        private Value EvaluateLinesCall(CallExpression call)
        {
            if (call.Arguments.Count != 1)
                throw new InterpreterException("lines(text) expects exactly one argument.", call.Position);

            Value textVal = EvaluateExpression(call.Arguments[0]);
            if (textVal.Kind != ValueKind.String)
                throw new InterpreterException("lines(text) expects a string.", call.Arguments[0].Position);

            var items = SplitLines(textVal.String ?? string.Empty);
            return MakeStringArray(items);
        }

        private Value EvaluateTrimCall(CallExpression call)
        {
            if (call.Arguments.Count != 1)
                throw new InterpreterException("trim(text) expects exactly one argument.", call.Position);

            Value sVal = EvaluateExpression(call.Arguments[0]);
            if (sVal.Kind != ValueKind.String)
                throw new InterpreterException("trim(text) expects a string.", call.Arguments[0].Position);

            return Value.FromString((sVal.String ?? string.Empty).Trim());
        }

        private Value EvaluateSplitCall(CallExpression call)
        {
            if (call.Arguments.Count != 2)
                throw new InterpreterException("split(text, sep) expects exactly two arguments.", call.Position);

            Value sVal = EvaluateExpression(call.Arguments[0]);
            Value sepVal = EvaluateExpression(call.Arguments[1]);

            if (sVal.Kind != ValueKind.String)
                throw new InterpreterException("split(text, sep) expects text as a string.", call.Arguments[0].Position);
            if (sepVal.Kind != ValueKind.String)
                throw new InterpreterException("split(text, sep) expects sep as a string.", call.Arguments[1].Position);

            string s = sVal.String ?? string.Empty;
            string sep = sepVal.String ?? string.Empty;

            List<string> parts;

            if (sep.Length == 0)
            {
                parts = new List<string>(s.Length);
                foreach (char c in s)
                    parts.Add(c.ToString());
            }
            else
            {
                var arr = s.Split(new[] { sep }, StringSplitOptions.None);
                parts = new List<string>(arr.Length);
                parts.AddRange(arr);
            }

            return MakeStringArray(parts);
        }

        private Value EvaluateCharAtCall(CallExpression call)
        {
            if (call.Arguments.Count != 2)
                throw new InterpreterException("charAt(text, index) expects exactly two arguments.", call.Position);

            Value sVal = EvaluateExpression(call.Arguments[0]);
            if (sVal.Kind != ValueKind.String)
                throw new InterpreterException("charAt(text, index) expects text as a string.", call.Arguments[0].Position);

            Value idxVal = EvaluateExpression(call.Arguments[1]);
            if (!TryToInt(idxVal, out int index))
                return Value.Undefined;

            string s = sVal.String ?? string.Empty;
            if (index < 0 || index >= s.Length)
                return Value.Undefined;

            return Value.FromString(s[index].ToString());
        }

        private Value EvaluateSliceCall(CallExpression call)
        {
            if (call.Arguments.Count != 2)
                throw new InterpreterException("slice(text, start) expects exactly two arguments.", call.Position);

            Value sVal = EvaluateExpression(call.Arguments[0]);
            if (sVal.Kind != ValueKind.String)
                throw new InterpreterException("slice(text, start) expects text as a string.", call.Arguments[0].Position);

            Value startVal = EvaluateExpression(call.Arguments[1]);
            if (!TryToInt(startVal, out int start))
                return Value.Undefined;

            string s = sVal.String ?? string.Empty;

            // allow negative start (count from end), like in many languages
            if (start < 0)
                start = s.Length + start;

            if (start < 0)
                start = 0;

            if (start >= s.Length)
                return Value.FromString(string.Empty);

            return Value.FromString(s.Substring(start));
        }
        private static Value MakeStringArray(List<string> items)
        {
            var dict = new Dictionary<double, Value>(items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                dict[i - 1] = Value.FromString(items[i]);
            }

            return Value.FromArray(dict);
        }
        private static List<string> SplitLines(string text)
        {
            // normalize: support \r\n, \n, and \r
            string norm = text.Replace("\r\n", "\n").Replace('\r', '\n');

            var raw = norm.Split('\n');

            int count = raw.Length;
            if (count > 0 && raw[count - 1].Length == 0)
                count--;

            var list = new List<string>(count);
            for (int i = 0; i < count; i++)
                list.Add(raw[i]);

            return list;
        }

    }
}
