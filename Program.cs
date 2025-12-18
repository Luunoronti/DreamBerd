using System.Diagnostics;
using System.Text;

namespace DreamberdInterpreter
{
    internal static class Program
    {
        static void Main(string[] args)
        {
            var constStore = new InMemoryConstConstConstStore();
            var variables = new VariableStore();
            var evaluator = new Evaluator(variables, constStore);

            Console.OutputEncoding = Encoding.UTF8;

            if (args.Length > 0)
            {
                var path = args[0];
                var source = File.ReadAllText(path);
                evaluator.CurrentDirectory = Path.GetDirectoryName(path) ?? Environment.CurrentDirectory;
                try
                {
                    var time = Stopwatch.GetTimestamp();
                    RunSource(source, evaluator);

                    Console.ForegroundColor = ConsoleColor.DarkGray;

                    var timeS = $" Runtime: {Format(Stopwatch.GetElapsedTime(time))} ";
                    // var iotimeS = $"IO: {Format(evaluator.IOTotalTime)}";
                    // var ml = Math.Max(iotimeS.Length, timeS.Length);
                    var ml = timeS.Length;

                    Console.WriteLine("".PadLeft(ml, '-'));
                    Console.WriteLine(timeS);
                    //Console.WriteLine(iotimeS);
                    Console.WriteLine("".PadLeft(ml, '-'));
                    Console.ResetColor();
                }
                catch (InterpreterException ex)
                {
                    PrintInterpreterError(ex, source, path);
                    Environment.ExitCode = 1;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[EXCEPTION] " + ex);
                    Environment.ExitCode = 1;
                }

                return;
            }

            RunRepl(evaluator);
        }

        public static string Format(TimeSpan ts)
        {
            // TimeSpan only goes down to ticks (100 ns), so we’ll extend manually
            long totalNanoseconds = ts.Ticks * 100;

            var units = new (long factor, string suffix)[]
            {
            (1000000000, "s"),
            (1000000,    "ms"),
            (1000,       "µs"),
            (1,          "ns")
            };

            var parts = new List<string>();
            foreach (var (factor, suffix) in units)
            {
                long value = totalNanoseconds / factor;
                if (value > 0 || parts.Count > 0) // include once we hit the first non-zero
                {
                    parts.Add($"{value} {suffix}");
                    totalNanoseconds %= factor;
                }
            }

            return string.Join(" ", parts);
        }


        private static void RunSource(string source, Evaluator evaluator)
        {
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens, source);
            var program = parser.ParseProgram();
            evaluator.ExecuteProgram(program);
        }

        private static void RunRepl(Evaluator evaluator)
        {
            Console.WriteLine("Dreamberd interpreter");
            Console.WriteLine("Pusta linia = wykonaj kod. 'exit' żeby wyjść.\n");

            var sb = new StringBuilder();

            while (true)
            {
                Console.Write(sb.Length == 0 ? "> " : "| ");
                var line = Console.ReadLine();
                if (line == null)
                    break;

                if (line.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase))
                    break;

                if (string.IsNullOrWhiteSpace(line))
                {
                    if (sb.Length == 0)
                        continue;

                    var source = sb.ToString();
                    sb.Clear();

                    try
                    {
                        RunSource(source, evaluator);
                    }
                    catch (InterpreterException ex)
                    {
                        PrintInterpreterError(ex, source, "<repl>");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("[EXCEPTION] " + ex);
                    }

                    continue;
                }

                sb.AppendLine(line);
            }
        }

        private static void PrintInterpreterError(InterpreterException ex, string source, string sourceName)
        {
            Console.WriteLine("[ERROR] " + ex.Message);

            if (ex.Position is null)
                return;

            var pos = ex.Position.Value;
            if (pos < 0) pos = 0;
            if (pos > source.Length) pos = source.Length;

            // policz line/col oraz wytnij linię
            var line = 1;
            var lineStart = 0;

            for (var i = 0; i < pos && i < source.Length; i++)
            {
                if (source[i] == '\n')
                {
                    line++;
                    lineStart = i + 1;
                }
            }

            var column = (pos - lineStart) + 1;

            var lineEnd = source.IndexOf('\n', lineStart);
            if (lineEnd < 0) lineEnd = source.Length;

            var lineText = source.Substring(lineStart, lineEnd - lineStart);
            if (lineText.EndsWith("\r", StringComparison.Ordinal))
                lineText = lineText.Substring(0, lineText.Length - 1);

            Console.WriteLine($"--> {sourceName}:{line}:{column}");
            Console.WriteLine(lineText);

            // caret
            var caretPos = Math.Max(1, column);
            if (caretPos > lineText.Length + 1) caretPos = lineText.Length + 1;

            Console.WriteLine(new string(' ', caretPos - 1) + '^');
        }
    }
}
