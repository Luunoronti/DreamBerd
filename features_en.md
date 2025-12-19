# DreamBerd (C# interpreter) — spec compliance checklist

This file compares our C# interpreter with the upstream DreamBerd/GulfOfMexico specification in `Spec.md`.

Legend:
- [x] implemented
- [~] partial / different from the spec
- [ ] missing

Progress snapshot (partial = 0.5):
- Counted items: 34 language/runtime sections from `Spec.md` (excluded marketing/orga sections: Installation, Copilot, Ownership, Contributing, Compiling, Highlighting, Vision Pro, Edutainment, Examples).
- Totals: 14 [x], 7 [~], 13 [ ] → ~52% coverage.

## Spec items (counted)

| Spec.md section | Status | Notes |
| --- | --- | --- |
| Exclamation Marks! | [x] | `!`/`?` terminators work; multiple marks; `;` acts as logical not. |
| Declarations | [~] | `const const/var` and `var const/var` implemented incl. `const const const`; “editable vs re-assignable” is simplified (no object mutation rules). |
| Immutable Data | [x] | `const const const` is immutable; scope is per interpreter run (not global to all users). |
| Naming | [x] | Any Unicode/emoji/digit/keyword names; numeric tokens can resolve to identifiers; empty-string names allowed. |
| Arrays | [x] | Literals, start index -1, float indexes, missing index ⇒ `undefined`, `numArray` helper. |
| When | [x] | `when` subscribes to referenced vars; parens optional; wildcard when no deps. |
| Lifetimes | [x] | `<N>/<Ns>/<Infinity>` and negatives for hoisting; expiry falls back to older overload. |
| Loops | [~] | Spec says “no loops”; interpreter has `while` with `break`/`continue`. |
| Booleans | [x] | `true`/`false`/`maybe` implemented. |
| Arithmetic | [x] | Significant whitespace precedence; `+ - * /`, unary; number words EN/PL (limited, no hyphenated/fraction words); division by zero → `undefined`. |
| Indents | [ ] | No enforcement of 3-space indent rule. |
| Equality | [x] | `==`, `===`, `====`, plus super-loose `=` implemented. |
| Functions | [x] | Any prefix of “function”; optional parens; returns/recursion work. |
| Dividing by Zero | [x] | `/0` yields `undefined`. |
| Strings | [~] | Any number of quotes, asymmetry allowed; zero-quote treated as identifier if it exists (spec says always string). |
| String Interpolation | [~] | Basic `{name}` / `$name`; no regional currency/typography variants. |
| Types | [ ] | Type annotations/aliases not supported. |
| Regular Expressions | [ ] | No `RegExp` narrowing. |
| Previous | [~] | `previous/next/current` keywords + `history`; missing `await next`. |
| File Structure | [ ] | No `=====` multi-file blocks. |
| Exporting | [ ] | No `export ... to` / `import ...!`. |
| Classes | [ ] | No classes/singletons/field history. |
| Time | [ ] | No `Date.now()` or time mutation. |
| Delete | [~] | Can delete primitive values; cannot delete keywords/classes. |
| Overloading | [~] | Overloads pick highest `!` then newest; lifetime fallback; inverted `¡` not supported. |
| Semantic naming | [x] | Prefix styles (sName/iAge/bHappy) are allowed; no extra semantics required. |
| Reversing | [x] | `reverse!` implemented (local execution direction toggle). |
| Class Names | [ ] | No `className` alias (classes absent). |
| DBX | [ ] | No HTML/DBX embedding. |
| Rich text | [ ] | No rich-text strings/links. |
| Asynchronous Functions | [ ] | No `async`/`await`/`noop` line interleaving. |
| Signals | [ ] | No `use()` signals or destructuring getters/setters. |
| AI | [ ] | No AEMI/ABI/AQMI insertion helpers. |
| Parentheses | [x] | Parentheses mostly ignored/treated as whitespace for grouping/calls. |

## Extras we support outside the spec

- `while`/`break`/`continue`; some statement terminators are optional after blocks.
- `printsl` plus stdlib helpers (`readFile`, `readLines`, `trim`, `split`, `lines`, `charAt`, `slice`, `toNumber`/`parseInt`).
- Number-word literals in EN/PL; extra unary/operators (abs `||x`, trig `~x`/`~~x`, clamp/wrap + update variants, power/root run operators).
- Variable history helpers (`history(x)`), lifetime-based overload fallback, and broadly optional parentheses.

## Spec sections not counted in the percentage

- Installation: we ship a .NET console app; no installer/installer-installer flow.
- Copilot: not applicable; interpreter does not block tooling.
- Ownership: not enforced by the interpreter.
- Contributing: charity/orga guidance not mirrored here.
- Compiling: we provide a real interpreter instead of the ChatGPT prompt workflow.
- Highlighting: no VSCode highlighting config included.
- Vision Pro / Edutainment: marketing-only sections.
- Examples: we have test `.dberd` files, but no curated `Examples.md` equivalent yet.
