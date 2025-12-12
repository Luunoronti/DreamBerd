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

            if (args.Length > 0)
            {
                var path = args[0];
                var source = File.ReadAllText(path);

                try
                {
                    var time = Stopwatch.GetTimestamp();
                    RunSource(source, evaluator);
                    Console.WriteLine($"Runtime: {Stopwatch.GetElapsedTime(time)}");
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

        private static void RunSource(string source, Evaluator evaluator)
        {
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
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
