DreamBerd — Codex Project Context (short)

Work on a C# (.NET) interpreter for the language DreamBerd (we intentionally use this name; ignore “Gulf of Mexico”). The app runs:

File mode: DreamberdInterpreter.exe <path> executes a .dberd file

REPL mode: no args → interactive prompt

Repo conventions

README.md must always be English.

All code comments in Polish (yes, intentionally).

Source files use extension *.dberd.

Language essentials

Statements typically end with !.

The language has 4-state booleans: true, false, maybe, undefined.

Conditionals support multi-branch ternary-like syntax with optional branches:

cond ? whenTrue : whenFalse :: whenMaybe ::: whenUndefined

whenTrue is required; false/maybe/undefined branches are optional (parser must accept missing : / :: / ::: forms per current repo rules).

Key features already touched / expectations

Postfix updates ++ / -- mutate variables.

There are “weird” operators added recently like ** / \\ and they must support postfix-update style chaining, e.g. x******! should mutate x (like ++ does), not only return a value. Also absurd chains like i++++--! should parse/evaluate consistently.

Current hot tasks / bugs

Negation operator: semicolon as unary prefix: ;expr
Truth table:

;true -> false

;false -> true

;maybe -> maybe

;undefined -> undefined

Bug example: if (line ;==== undefined) currently errors because ; isn’t accepted in expressions. Fix lexer/parser precedence so ; works inside expressions (including if (...)), and implement evaluation.

“try again!” statement (loop-ish without loops): inside if/else/idk blocks, try again! should jump back to re-evaluate the originating if condition and re-enter the correct branch again (depending on updated state). Define semantics sanely for nested conditionals/scope.

Debug operator conflict: there was an attempt to support expr? as a debug-print statement, but ? also starts conditional expressions and causes parse errors. Add a disambiguation rule (likely statement-position expr? vs conditional cond ? ...).

Architecture expectation

Span-friendly lexer → recursive descent parser → AST → interpreter with environment. Follow existing repo style and treat the repo as source of truth for any details.