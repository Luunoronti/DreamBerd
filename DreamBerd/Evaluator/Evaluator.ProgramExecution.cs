// Evaluator.ProgramExecution.cs
namespace DreamberdInterpreter
{
    public sealed partial class Evaluator
    {
        public void ExecuteProgram(IReadOnlyList<Statement> statements)
        {
            if (statements == null)
                throw new ArgumentNullException(nameof(statements));

            int index = 0;
            int direction = 1;
            _currentStatementIndex = 0;

            while (index >= 0 && index < statements.Count)
            {
                _currentStatementIndex = index;

                _variables.ExpireLifetimes(_currentStatementIndex, DateTime.UtcNow);

                var statement = statements[index];

                if (statement is ReverseStatement reverseStatement)
                {
                    if (reverseStatement.IsDebug)
                        Console.WriteLine("[DEBUG] reverse!");

                    direction = -direction;
                    index += direction;
                    continue;
                }

                if (statement is ForwardStatement forwardStatement)
                {
                    if (forwardStatement.IsDebug)
                        Console.WriteLine("[DEBUG] forward!");

                    direction = 1;
                    index += direction;
                    continue;
                }

                EvaluateStatement(statement);
                index += direction;
            }
        }

        /// <summary>
        /// Wykonuje listę statementów (np. blok { ... }).
        ///
        /// Na tym etapie reverse/forward działa lokalnie w obrębie tej listy.
        /// Scope'y blokowe będą dodane później – tutaj to jest tylko grupowanie.
        /// </summary>
        private void ExecuteStatementList(IReadOnlyList<Statement> statements)
        {
            if (statements == null)
                throw new ArgumentNullException(nameof(statements));

            int savedIndex = _currentStatementIndex;

            int index = 0;
            int direction = 1;

            try
            {
                while (index >= 0 && index < statements.Count)
                {
                    _currentStatementIndex = index;

                    _variables.ExpireLifetimes(_currentStatementIndex, DateTime.UtcNow);

                    var statement = statements[index];

                    if (statement is ReverseStatement reverseStatement)
                    {
                        if (reverseStatement.IsDebug)
                            Console.WriteLine("[DEBUG] reverse!");

                        direction = -direction;
                        index += direction;
                        continue;
                    }

                    if (statement is ForwardStatement forwardStatement)
                    {
                        if (forwardStatement.IsDebug)
                            Console.WriteLine("[DEBUG] forward!");

                        direction = 1;
                        index += direction;
                        continue;
                    }

                    EvaluateStatement(statement);
                    index += direction;
                }
            }
            finally
            {
                _currentStatementIndex = savedIndex;
            }
        }


    }
}
