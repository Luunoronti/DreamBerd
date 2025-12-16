# DreamBerd (C# interpreter) — feature list: implemented vs missing

This file compares the **current state of our C# interpreter** with the “canonical” specification/README of the **DreamBerd** project (a GitHub repo sometimes called “Gulf of Mexico”).

- **Project status (this repo ZIP):** DreamBerd interpreter in C# (.NET), console + REPL.
- **Document goal:** a quick checklist of “what we have” and “what doesn’t exist yet,” so we don’t lose direction.

Legend:
- ✅ = implemented
- 🟡 = partially / different from the spec
- ❌ = missing

---

## ✅ What we already have in this interpreter

### Running
- ✅ **File** mode: `DreamberdInterpreter.exe <path>` → execute and exit.
- ✅ **REPL** mode with no args: paste multiple lines (collect until an empty line), then parse and execute.

### Tokenization / parser
- ✅ Tokens with position (offset in source) and an AST with `Position` (for errors).
- ✅ `// ...` comments to end of line.
- ✅ Basic literals: numbers (double), strings `'...'` and `"..."`.
- ✅ Identifiers (letters/underscore/$ + digits afterwards).

### Statement terminators
- ✅ Every statement ends with `!` or `?`.
- ✅ `?` = debug mode (printing value / history).
- ❌ In our implementation there is **no** multiple `!!!` priority (the spec has this).

### Declarations
- ✅ 4 variants: `const const`, `const var`, `var const`, `var var`.
- ✅ `const const const` as a separate store (globally “untouchable”).
- ✅ Lifetimes: syntax `<N>` and `<N s>` and `<Infinity>` (runtime expiration after statements and/or time).

### Blocks and scopes
- ✅ Blocks `{ ... }`.
- ✅ Block scope (push/pop scope in `VariableStore`) — block variables don’t “leak” outside.
- ✅ Functions have their own scopes (callframe/locals).

### Control flow
- ✅ `if (cond) stmt` and `if (cond) { ... } else { ... }`.
- ✅ `reverse!` / `forward!` — change direction of iterating through the statement list.
- ✅ (Extension beyond the spec) `while`, `break`, `continue`.
- ✅ `return` (as a statement; in functions it works via an internal early-exit mechanism).

### Runtime values
- ✅ Types: Number, String, Boolean (`true/false/maybe`), Null, Undefined, Array.
- ✅ Truthiness:
  - `false`, `null`, `undefined`, `0`, empty string, empty array → falsy
  - `true` and `maybe` → truthy

### Expressions
- ✅ Arithmetic: `+ - * /` (with string concatenation for `+`).
- ✅ Division by 0 → `undefined`.
- ✅ Comparisons: `< <= > >=` (on numbers after conversion).
- ✅ Equalities: `==`, `===`, `====` (our “dreamberd-ish” semantics).
- ✅ Assignments: `x = expr`.
- ✅ Arrays: `[a, b, c]`, indices from `-1` upward, float indexing.
- ✅ Index read/write: `arr[idx]`, `arr[idx] = value` (immutable-by-value: replace whole array).
- ✅ Function calls: `foo(a, b)`.
- ✅ 4-branch conditional operator: `cond ? t : f :: m ::: u`.

### Functions
- ✅ Declarations: any prefix that resembles “function” (`function`, `func`, `fun`, `fn`, `functi`, `f`).
- ✅ Function body: expression **or** block `{ ... }`.
- ✅ Recursion works.

### Built-in stuff
- ✅ `print(...)`.
- ✅ Variable history:
  - `previous(x)`, `next(x)` — move the history cursor
  - `history(x)` — returns the history array
  - `?` on an identifier prints history

### `delete`
- ✅ `delete <primitive>!` deletes: Number / String / Boolean (true/false/maybe).
- ✅ After `delete`, trying to obtain such a value (as an evaluation result) throws an error.
- ❌ Deleting keywords / language constructs (e.g. `delete class!`) — not implemented.

### `when`
- ✅ `when (cond) stmt!` (subscription executed after variable mutations).
- 🟡 Differences vs README:
  - in the spec the condition is sometimes written with `=` (there it’s “comparison”), but for us `=` is assignment and comparisons are `==/===/====`.
  - our model checks after every variable mutation (close to the idea, but details may differ).

---

## 🟡 We have it, but different / incomplete

- 🟡 **Identifiers as “any Unicode / string”**: the README allows basically anything (including a name that is a number). We use classic identifier rules (letters/`_`/`$`, then digits).
- 🟡 **Overloading / priorities**: the README has priorities depending on the number of `!` and `¡` (negative). We end statements with a single `!` or `?`, and declaration priority is fixed for now.
- 🟡 **Lifetimes that “persist between runs”**: the README suggests you can set a lifetime longer than a single run. We have no persistence between runs.

---

## ❌ What’s still missing vs the “spec” from the README

Below is a list of features/sections that appear in the DreamBerd README but we don’t support (either at all, or we deviate significantly).

### Syntax / whitespace / parser quirks
- ❌ **“Not” as `;`** (semicolon) instead of `!`.
- ❌ **Meaningful whitespace for arithmetic** (order of operations depends on spaces).
- ❌ **Parentheses do nothing** (in the README they’re ignored and replaced by whitespace).
- ❌ **Indent = exactly 3 spaces** (parser validation).
- ❌ **AI auto-insertion**: AEMI/ABI/AQMI (auto `!`, auto-closing parentheses and quotes).

### Operators / expressions
- ❌ Operator `=` as “super-loose equality” (README mentions “if you want to be much less precise”).
- ❌ `++`, `--`, and other shortcuts.
- ❌ `^` (exponentiation) and other extra operators from examples.

### Strings
- ❌ Any number of quotes (e.g. `''''Lu''''`), including **0** (`name = Luke!`).
- ❌ String interpolation with currencies: `${name}`, `£{name}`, `{name}€`, etc.
- ❌ “Rich text” / links in strings.

### `previous` / `next` / `current` as “keywords”
- ❌ Syntax like `previous score` (no parentheses).
- ❌ `current`.
- ❌ `await next score` and the whole async/await model from the README.

### File structure / import/export
- ❌ File separator via `=====` inside a single file.
- ❌ Naming files via `======= add.gom =======`.
- ❌ `export ... to "..."!` and `import ...!`.

### OOP / classes
- ❌ `class` + rule “only one instance of a class”.
- ❌ `new`, fields, methods, `.`.
- ❌ `className`.

### Time
- ❌ `Date.now()` and the ability to change time via `Date.now() -= ...`.

### DBX / HTML-in-code
- ❌ DBX (HTML/JSX-like in code).
- ❌ `htmlClassName` rules.

### Async / concurrency
- ❌ `async` functions “alternating per line”.
- ❌ `noop` as “waiting” / occupying a line.

### Signals
- ❌ `use(...)` as signals (a function that is both getter/setter).
- ❌ Destructuring `const var [get, set] = use(0)!`.

### Language `delete`
- ❌ `delete class!`, `delete delete!`, etc. (deleting keywords / paradigms).

### Other
- ❌ “Number names” like `one`, `two`.
- ❌ Extended “naming” (e.g. declarations with string names, names being digits, etc.).

---

## Notes: our extensions (beyond the README)

- ✅ `while / break / continue` — README claims “no loops.” We do have loops (practical for tests and development).
- ✅ `return` as a statement + functions with blocks `{ ... }`.

---

## Suggested next work order (optional)

If we want to get closer to the README while not blowing up the parser immediately:
1) Multiple `!` + declaration priority (overloading)
2) `=` as “loose equality” (a separate operator, not Assign)
3) `previous/next/current` as keywords (no parentheses)
4) “Parentheses do nothing” (can be done as a pre-process/token-filter)
5) Only then: meaningful whitespace for arithmetic (this is the biggest upheaval)
