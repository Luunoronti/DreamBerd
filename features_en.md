# DreamBerd (C# interpreter) — feature list: implemented vs missing

This file compares the **current state of our C# interpreter** against the “canonical” DreamBerd specification / README (the GitHub repo sometimes called “Gulf of Mexico”).

- **Project state (this repo ZIP):** DreamBerd interpreter in C# (.NET), console runner + REPL.
- **Goal:** a quick checklist of “what we have” and “what still doesn’t exist” so we don’t lose direction.

Legend:
- ✅ = implemented
- 🟡 = partial / different from the spec
- ❌ = missing

---

## ✅ What we already have (matches the README, or very close)

### Running
- ✅ **File mode:** `DreamberdInterpreter.exe <path>` → run a file.
- ✅ **REPL mode:** no args → reads until an empty line, executes, repeats.

### Lexer + parser
- ✅ Tokenization of the basic syntax (identifiers, numbers, strings, operators, blocks).
- ✅ AST parser for statements + expressions.
- ✅ Errors with `line:column` + a caret pointing at the spot.

### Statement terminators
- ✅ `!` as a statement terminator.
- ✅ `?` as a debug terminator (prints expression value; for identifiers also prints `history(...)`).
- ✅ Any number of `!`/`?` (e.g. `!!!`) is accepted.
- ✅ The number of `!` is used as **declaration priority** (overloading).

### Declarations (mutability)
- ✅ `const const`, `const var`, `var const`, `var var`.
- ✅ `const const const` as a global immutable store (cannot be reassigned or overwritten).
- 🟡 The “editable vs re-assignable” model is simplified (no objects/methods like `push/pop`).

### Types & literals
- ✅ Numbers (double).
- ✅ Strings using `"..."` and `'...'`.
- ✅ 3-state booleans: `true`, `false`, `maybe`.
- ✅ `undefined`.
- 🟡 `null` exists as a runtime value (e.g. statement results), but there is no dedicated `null` literal in the parser yet.

### Expressions & operators
- ✅ Arithmetic: `+ - * /` (division by 0 → `undefined`).
- ✅ Comparisons: `< > <= >=`.
- ✅ Equality: `==` (very loose / stringy), `===` (loose / numeric), `====` (strict).
- ✅ Unary minus: `-x`.
- ✅ Assignment: `x = expr`.
- ✅ Index assignment: `arr[idx] = expr`.

### Conditional operator (4 branches)
- ✅ `cond ? whenTrue`
- ✅ Optional branches (can appear in any order, and can be omitted):
  - `: whenFalse`
  - `:: whenMaybe`
  - `::: whenUndefined`
- ✅ Missing branch → evaluates to `undefined`.

### Control flow
- ✅ `if (cond) ... else ... idk ...`
  - `idk` runs when `cond` is `maybe`.
- ✅ Blocks `{ ... }` create scope (shadowing works).
- ✅ `return expr` inside functions.

### Functions
- ✅ Declarations: `function|func|fun|fn|functi|f name(args) => { ... }`
- ✅ Call stack + function-local variables.
- ✅ Recursion works.

### Arrays
- ✅ Literals: `[a, b, c]`.
- ✅ Indices start at `-1`.
- ✅ Indices can be floats (`double`).
- ✅ Missing index read → `undefined`.
- ✅ `numArray(init, size)` creates a numeric array (indices from -1).

### Lifetimes + declaration overloading
- ✅ Lifetimes: `<N>` (lines), `<N s>` (seconds), `<Infinity>`.
- ✅ Overloading: multiple declarations of the same name within a scope:
  - active decl = highest priority (# of `!`), then newest
  - lifetime expiry can cause fallback to an older declaration
- ✅ Variable history: `previous(x)`, `next(x)`, `history(x)`.

### when(...)
- ✅ `when (condition) { ... }` subscribes to mutations of variables referenced in the condition.
- ✅ If the condition references no variables (e.g. `when (true)`), it runs after every mutation (wildcard `*`).
- ✅ Dispatch uses a queue (prevents recursive re-entry during mutations).

### delete
- ✅ `delete <primitive>` works for number/string/boolean (as per README).
  - after deletion: using that exact value throws an error.

### Mini stdlib
- ✅ `print(...)`
- ✅ IO: `readFile(path)`, `readLines(path)`
- ✅ Strings: `lines(text)`, `trim(text)`, `split(text, sep)`, `charAt(text, idx)`, `slice(text, start)`
- ✅ Conversions: `toNumber(x)` (+ aliases `parseInt`, `parseNumber`)

---

## ✅ Our extensions (NOT in the official DreamBerd README)

- ✅ `while (cond) { ... }` + `break` + `continue` (README says “no loops”).
- ✅ Statement terminators are sometimes optional (e.g. after `if/while` blocks and some statements).
- ✅ Normal meaning of parentheses `()` (README says parentheses “do nothing”).
- ✅ Classic operator precedence (README uses significant whitespace to change precedence).

---

## 🟡 Implemented, but different / incomplete (vs README)

- 🟡 `const var` / `var var` “editable” semantics are not implemented (no objects, no methods like `push/pop`).
- 🟡 Naming: we support Unicode *letters*, but not emoji identifiers, and not full “number naming”.

---

## ❌ Still missing (from the official README / spec)


### Syntax / whitespace / parser quirks
- ❌ “not” operator as `;` (e.g. `if (;false) { ... }`).
- ❌ “Parentheses do nothing” (parentheses are ignored / treated as whitespace).
- ❌ Significant whitespace arithmetic (space controls precedence).
- ❌ Indentation rule: exactly 3 spaces (and -3 spaces).
- ❌ Extended naming: emoji names, empty names, keyword names, full “number naming”.
- ❌ Full “editable vs re-assignable” model (mutating structures/objects like `push/pop`).
- ❌ Deleting keywords/paradigms (`delete class`, `delete delete`, …).
- ❌ AQMI / AI / Copilot gag-features from the README.
- ❌ README-style installer/CLI (we only have our .NET runner).


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

## Suggested next work order

1) Spec compatibility: `;` as not + “no-normal-parentheses” mode (or a compatibility mode).  
2) Naming (wider Unicode + number naming).  
3) “Editable” mutability (or at least sensible array mutation for `const var` / `var var`).  
4) Indentation + significant whitespace.
