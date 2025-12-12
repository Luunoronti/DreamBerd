using System;
using System.Collections.Generic;
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

            if (args.Length > 0)
            {
                string path = args[0];
                string source = File.ReadAllText(path);
                RunSource(source, evaluator);
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
                string? line = Console.ReadLine();
                if (line == null)
                    break;

                if (line.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase))
                    break;

                if (string.IsNullOrWhiteSpace(line))
                {
                    if (sb.Length == 0)
                        continue;

                    string source = sb.ToString();
                    sb.Clear();

                    try
                    {
                        RunSource(source, evaluator);
                    }
                    catch (InterpreterException ex)
                    {
                        Console.WriteLine("[ERROR] " + ex.Message);
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
    }
}
