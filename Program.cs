using System;
using System.IO;

namespace DreamberdInterpreter
{
    internal static class Program
    {
        static void Main(string[] args)
        {
            var context = new Context();
            var globalConstStore = new InMemoryConstConstConstStore();
            var evaluator = new Evaluator(context, globalConstStore);

            if (args.Length > 0)
            {
                var path = args[0];
                if (!File.Exists(path))
                {
                    Console.Error.WriteLine($"File '{path}' not found.");
                    return;
                }

                var source = File.ReadAllText(path);

                try
                {
                    RunSource(source, evaluator);
                }
                catch (InterpreterException ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Error: {ex.Message}");
                    Console.ResetColor();
                }

                
            }
            else
            {
                RunRepl(evaluator);
            }
        }

        private static void RunSource(string source, Evaluator evaluator)
        {
            var lexer = new Lexer(source);
            var tokens = lexer.Tokenize();
            var parser = new Parser(tokens);
            var statements = parser.Parse();

            evaluator.ExecuteProgram(statements);
        }

        private static void RunRepl(Evaluator evaluator)
        {
            Console.WriteLine("DreamBerd interpreter (subset) - REPL mode.");
            Console.WriteLine("Podaj kod DreamBerda (linie kończone ! lub ?). Ctrl+C żeby wyjść.");

            while (true)
            {
                Console.Write("DreamBerd> ");
                var line = Console.ReadLine();
                if (line == null)
                    break;
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    RunSource(line, evaluator);
                }
                catch (InterpreterException ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Error: {ex.Message}");
                    Console.ResetColor();
                }
            }
        }
    }
}
