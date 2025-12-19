# DreamBerd (C# interpreter) — feature list: implemented vs missing

This file compares the **current C# interpreter** with the “canonical” `Spec.md`/README for DreamBerd (aka Gulf of Mexico).

Legend:
- ✅ = implemented
- 🟡 = partial / different from the spec
- ❌ = missing
- Progress snapshot (weighting partial = 0.5): 14 ✅, 7 🟡, 13 ❌ → ~52% coverage (counting language/runtime sections only; marketing bits like Installation/Copilot/etc. are excluded).

---

## ✅ What we already have (matches the README or very close)

### Running
- ✅ **File mode:** `DreamberdInterpreter.exe <path>` → run a file.
- ✅ **REPL mode:** no args → reads until an empty line, runs, repeats.

### Lexer + parser
- ✅ Tokenization of core syntax (identifiers, numbers, strings, operators, blocks).
- ✅ AST parser for statements and expressions.
- ✅ Errors with `line:column` + underline of the spot.

### Statement terminators
- ✅ `!` as a terminator.
- ✅ `?` as a debug terminator (prints value; for identifiers also `history(...)`).
- ✅ Any number of `!`/`?` (e.g., `!!!`) is accepted.
- ✅ Number of `!` is used as declaration priority (overloading).

### Declarations (mutability)
- ✅ `const const`, `const var`, `var const`, `var var`.
- ✅ `const const const` as a global, immutable store (cannot be assigned or overwritten).
- 🟡 “editable vs re-assignable” semantics are simplified (no objects/methods like `push/pop`).

### Types and literals
- ✅ Numbers (double).
- ✅ Strings in `"..."` and `'...'`.
- ✅ 3-state booleans: `true`, `false`, `maybe`.
- ✅ `undefined`.
- 🟡 `null` exists as a runtime value (e.g., statement result), but there is no dedicated `null` literal in the parser.

### Expressions and operators
- ✅ Arithmetic: `+ - * /` (division by 0 → `undefined`).
- ✅ Comparisons: `< > <= >=`.
- ✅ Equality: `==` (very loose / stringy), `===` (loose / numeric), `====` (strict).
- ✅ Operator `=` as “super-loose equality”.
- ✅ Unary minus: `-x`.
- ✅ Unary not: `;expr` (true↔false, maybe/undefined pass through).
- ✅ Postfix chains `x++++--!` and power-run `x****!` (DreamBerd style).
- ✅ Significant whitespace for binary precedence (fewer spaces = tighter; ties → classic precedence).
- ✅ Parentheses are ignored/treated as whitespace (calls, conditions, declarations without parens).
- ✅ Assignment: `x = expr`.
- ✅ Index assignment: `arr[idx] = expr`.
- ✅ Update statements `x :+ y!`, `:-`, `:*`, `:/`, `:%`, `:??`, `:<`, `:>`, bitwise `:& :| :^ :<< :>>`, power run `:**!`, root run `:\\!`, etc.
- ✅ Extra operators: abs `||x`; trig `~x`/`~~x`/`~~~x`; min/max aliases `<>` `><` `⌊⌋` `⌈⌉`; clamp/wrap `▷`/`↻` and keywords `clamp`/`wrap` with square-bracket ranges `[lo .. hi]`/`]lo .. hi[`, plus updates `:▷` / `:↻` (wrap supports optional delta before `@`).

### Conditional operator (4 branches)
- ✅ `cond ? whenTrue`
- ✅ Optional branches (any order, can be omitted):
  - `: whenFalse`
  - `:: whenMaybe`
  - `::: whenUndefined`
- ✅ Missing branch → `undefined`.

### Control flow
- ✅ `if cond ... else ... idk ...` (parens optional/ignored)
  - `idk` runs when `cond` is `maybe`.
- ✅ Blocks `{ ... }` create scope (shadowing works).
- ✅ `return expr` in functions.

### Functions
- ✅ Declarations: any prefix of the word `function` (`function|func|fun|fn|functi|f name params => { ... }`), params comma-separated; parens optional/ignored.
- ✅ Call stack + locals.
- ✅ Recursion works.

### Arrays
- ✅ Literals: `[a, b, c]`.
- ✅ Indices start at `-1`.
- ✅ Indices can be float (`double`).
- ✅ Missing index read → `undefined`.
- ✅ `numArray(init, size)` builds numeric arrays (indices from -1).

### Lifetimes + declaration overloading
- ✅ Lifetimes: `<N>` (lines), `<N s>` (seconds), `<Infinity>`.
- ✅ Overloading: multiple decls of same name in scope:
  - pick highest priority (number of `!`), then newest
  - expiry of lifetimes can fall back to older decls
- ✅ Variable history: `previous(x)`, `next(x)`, `history(x)`.
- ✅ No-paren forms: `previous x`, `next x`, `current x`.

### when(...)
- ✅ `when condition { ... }` subscribes to mutations of variables used in the condition (parens optional/ignored).
- ✅ If the condition uses no vars (e.g., `when (true)`), it fires after every mutation (wildcard `*`).
- ✅ Dispatch via queue (prevents recursive reentry on mutations).

### delete
- ✅ `delete <primitive>` works on number/string/boolean (per README).
  - after deletion: using that exact value throws an error.

### Mini stdlib
- ✅ `print(...)`
- ✅ `printsl(...)`
- ✅ IO: `readFile(path)`, `readLines(path)`
- ✅ Strings: `lines(text)`, `trim(text)`, `split(text, sep)`, `charAt(text, idx)`, `slice(text, start)`
- ✅ Conversions: `toNumber(x)` (+ aliases `parseInt`, `parseNumber`)

---

## 🟣 Our extensions (outside the official DreamBerd README)

- ✅ `while (cond) { ... }` + `break` + `continue` (spec says “no loops”).
- ✅ Statement terminator may be optional after some blocks (e.g., `if/while`).
- ✅ Extra unary/trig/abs/clamp/wrap operators and the power/root update runs.

---

## 🟡 Implemented, but different / incomplete (vs README)

- 🟡 Mutability `const var` / `var var` does not support “object mutation” rules (no `push/pop`, no objects).
- ✅ Naming: Unicode/emoji identifiers, keywords as names, digit-only names; empty names via `""` work. A numeric token first tries to resolve a name, otherwise stays a literal.
- 🟡 Strings without quotes: 0-quote falls back to identifier if it exists.
- 🟡 String interpolation is minimal (`{name}` / `$name`, no currency/typography variants).
- 🟡 Number words: English (`zero`..`nineteen`, `twenty`..`ninety`, scales to `quintillion`) and Polish (`jeden`..`dziewiętnaście`, `dwadzieścia`.., scales to `trylion`); parsed only if words aren’t names in scope and until an unknown word, after which the literal becomes the full input string. Digit tokens can be names (fallback to literal if missing). No fractions/hyphenated forms/negatives.
- 🟡 `previous` / `next` / `current` keywords work; `await next` / full async model not present.
- 🟡 Overloading: picks by count of `!`, then newest, then lifetime fallback; inverted `¡` priority not supported.
- 🟡 Classes: `Name is a class { ... }` works, but singleton/aliasing/full field history vs spec is incomplete.

---

## ❌ Still missing (from the official README / spec)

### Syntax / whitespace / parser quirks
- ❌ Indents enforced to exactly 3 spaces (and -3).
- ❌ Full “editable vs re-assignable” object/structure mutation model.
- ❌ Deleting keywords/paradigms (`delete class`, `delete delete`, …).
- ❌ AQMI / AI / Copilot gag-features from the README.
- ❌ Installer/CLI as per README (we ship a .NET runner).

### Operators / expressions
- ❌ `^` exponent and other extra operators from the examples.

### Strings
- ❌ “Rich text” / links in strings.

### File structure / import-export
- ❌ File separators `=====` inside one file.
- ❌ Naming file blocks `======= add.gom =======`.
- ❌ `export ... to "..."!` and `import ...!`.

### OOP / classes
- ❌ Spec’d singleton/aliasing/field-history behavior as documented.

### Time
- ❌ `Date.now()` and mutating time via `Date.now() -= ...`.

### DBX / HTML-in-code
- ❌ DBX (HTML/JSX-like) embedding.
- ❌ `htmlClassName` rules.

### Async / concurrency
- ❌ `async` functions that alternate per line.
- ❌ `noop` as a waiting/line-occupying step.

### Signals
- ❌ `use(...)` signals (getter/setter function).
- ❌ Destructuring `const var [get, set] = use(0)!`.

### Language-level delete
- ❌ `delete class!`, `delete delete!`, etc. (deleting keywords/paradigms).

### Other
