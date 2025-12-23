using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
                    var defaultName = Path.GetFileName(path) ?? "main.gom";
                    if (!RunSource(source, evaluator, defaultName))
                    {
                        Environment.ExitCode = 1;
                        return;
                    }

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


        private sealed record FileSection(string Name, string Source);

        private static bool RunSource(string source, Evaluator evaluator, string defaultName)
        {
            var sections = SplitSourceIntoFiles(source, defaultName, out var hadSeparators);
            bool firstSection = true;
            foreach (var section in sections)
            {
                if (hadSeparators)
                {
                    evaluator.ResetNonConstState(preserveExports: !firstSection);
                }

                evaluator.CurrentFileName = section.Name;
                try
                {
                    RunSection(section.Source, evaluator);
                }
                catch (InterpreterException ex)
                {
                    PrintInterpreterError(ex, section.Source, section.Name);
                    return false;
                }

                firstSection = false;
            }

            return true;
        }

        private static void RunSection(string source, Evaluator evaluator)
        {
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens, source);
            var program = parser.ParseProgram();
            evaluator.ExecuteProgram(program);
        }

        private static IReadOnlyList<FileSection> SplitSourceIntoFiles(string source, string defaultName, out bool hadSeparators)
        {
            var sections = new List<FileSection>();
            var current = new StringBuilder();
            string currentName = string.IsNullOrWhiteSpace(defaultName) ? "main.gom" : defaultName;
            int autoIndex = 1;
            bool hadContent = false;
            bool foundSeparator = false;

            using var reader = new StringReader(source ?? string.Empty);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (TryParseFileSeparator(line, out var newName))
                {
                    foundSeparator = true;
                    if (hadContent || current.Length > 0)
                    {
                        sections.Add(new FileSection(currentName, current.ToString()));
                        current.Clear();
                        hadContent = false;
                    }

                    if (!string.IsNullOrWhiteSpace(newName))
                    {
                        currentName = newName!;
                    }
                    else
                    {
                        currentName = $"file-{autoIndex}.gom";
                        autoIndex++;
                    }

                    continue;
                }

                current.AppendLine(line);
                hadContent = true;
            }

            if (current.Length > 0 || sections.Count == 0)
            {
                sections.Add(new FileSection(currentName, current.ToString()));
            }

            hadSeparators = foundSeparator;
            return sections;
        }

        private static bool TryParseFileSeparator(string line, out string? name)
        {
            name = null;
            if (string.IsNullOrWhiteSpace(line))
                return false;

            string trimmed = line.Trim();
            int len = trimmed.Length;
            if (len < 5 || trimmed[0] != '=')
                return false;

            int leading = 0;
            while (leading < len && trimmed[leading] == '=')
                leading++;

            if (leading < 5)
                return false;

            string rest = trimmed.Substring(leading).Trim();
            if (rest.Length == 0)
                return true;

            int trailing = 0;
            int idx = rest.Length - 1;
            while (idx >= 0 && rest[idx] == '=')
            {
                trailing++;
                idx--;
            }

            if (trailing >= 2)
                rest = rest.Substring(0, rest.Length - trailing).Trim();

            name = rest.Length > 0 ? rest : null;
            return true;
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
                        RunSource(source, evaluator, "<repl>");
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
