# DreamBerd Language Guide / Przewodnik po DreamBerd

- [English](#english)
- [Polski](#polski)

---

## English

- [Overview](#overview)
- [Literals](#literals)
- [Identifiers & Names](#identifiers--names)
- [Expressions](#expressions)
- [Statements](#statements)
- [Update Operators](#update-operators)
- [Ranges, Clamp & Wrap](#ranges-clamp--wrap)
- [Control Flow](#control-flow)
- [Functions](#functions)
- [Classes](#classes)
- [History & Time Travel](#history--time-travel)
- [Pattern Matching & Destructuring](#pattern-matching--destructuring)
- [Delete](#delete)
- [Special Whitespace Rules](#special-whitespace-rules)
- [Keywords](#keywords)

### Overview
- DreamBerd is a joke/speculative language: parentheses are optional and act like whitespace.
- Statements end with `!` (or `?` for debug prints of expressions).
- Arrays start at index `-1`. Objects are singleton class instances.
- Truthy: numbers ≠ 0, non-empty strings, arrays/objects, boolean `true/maybe`.

### Literals
- Numbers: `123`, `-4.2`, or words (`one hundred`, `dwa tysiące trzy`).
- Strings: `"text"` (empty string allowed as identifier name).
- Booleans: `true`, `false`, `maybe`.
- `null`, `undefined`.
- Arrays: `[a, b, c]` → indices `-1,0,1`.
- Ranges: `[0 .. 10]`, `[0 .. 10)`, `(0 .. 1]`, `(0 .. 1)`.

### Identifiers & Names
- Any word/emoji/keyword/number/empty string can be a name: `var var 5 = 0!`, `var var "" = 1!`, `function return x => ...`.
- Keywords are allowed as identifiers.

### Expressions
- Arithmetic: `+ - * /`.
- Comparison: `< > <= >=`.
- Equality: `==` (very loose), `===` (loose/number coercion), `====` (strict).
- Min/Max: `<>` or `⌊⌋` (min), `><` or `⌈⌉` (max).
- Unary: `-x`, logical not `;x` (via boolean negation operator), abs `||x`, sin `~x`, cos `~~x`, tan `~~~x`.
- Power postfix: `x**` (square), `x****` (cube), etc.
- Root prefix: `\\x` (sqrt), `\\\\x` (cbrt), etc.
- Root infix: `a \\ n`.
- Conditional: `cond ? t : f : m : u` (4-branch for true/false/maybe/undefined).
- Clamp/Wrapping: see below.

### Statements
- Expression statement: `expr!` or `expr?` (debug print of value).
- Variable declarations: `var var x = 1!`, `const const y = 2!`, `const const const z = 3!`.
- Destructuring declaration: `var var [a, b=5, ...rest] = expr!`, `var var {foo, bar: alias, missing=99} = obj!`.
- Delete: `delete x!`, `delete arr[i]!`, `delete obj["k"]!`.
- Blocks: `{ ... }` create scope.

### Update Operators
All are statement forms `target :op value!` (or specialized forms):
- Arithmetic: `:+` `:-` `:*` `:/` `:%`.
- Power: `:*!` (square), `:**!` (cube), `:***!` etc.
- Root: `:\\!` (sqrt), `:\\\\!` (cbrt), `:\\ n!` (n-th root).
- Bitwise: `:&` `:|` `:^` `:<<` `:>>`.
- Nullish assign: `:??` (only if target is `undefined`).
- Min/Max: `:<` (min), `:>` (max).
- Trig: `:~` (sin), `:~~` (cos), `:~~~` (tan).
- Clamp: `:? [lo .. hi]!` or `:? clamp [lo .. hi]!`.
- Wrap: `:? x @ [lo .. hi)!` or `:? wrap [lo .. hi)!`.

### Ranges, Clamp & Wrap
- Ranges allow inclusive/exclusive ends: `[a .. b]`, `(a .. b]`, `[a .. b)`, `(a .. b)`.
- Clamp infix: `value ▷ [lo .. hi]` or `value clamp [lo .. hi]`.
- Wrap (mod) infix: `value ↻ [lo .. hi)` or `value wrap [lo .. hi)`.
- Update forms use `:?` with range after `@` for wrap.

### Control Flow
- `if cond { ... } [else { ... }] [idk { ... }]` (no parentheses needed).
- `while cond { ... }`, `break!`, `continue!`.
- `try again!` inside `if/else/idk` reevaluates condition.
- `when expr { ... }` subscribes to mutations of vars in `expr` (or all if none).
- Pattern when: `when <target> matches <pattern> [where guard] { ... }`.

### Functions
- Declaration: `function name a, b => expression!` or block body.
- Call without parentheses: `name 1, x+2, "ok"!`.
- `return expr!` or `return!`.
- Built-ins: `print`, `previous`, `next`, `history`, `toNumber`.

### Classes
- Decl: `MyClass is a class { ... }` (singleton per class name).
- Methods: `function foo x => { ... }` (instance), `static function bar x => { ... }`.
- Properties: `prop : default 123!` (auto-prop), `static staticProp : default 5!`.
- Fallbacks: `fallback : default val!` (instance), `static fallback : default val!`.
- Access: `MyClass["field"]`, `obj["method"] args!`; `source` inside methods is `this`.
- History works on fields; `delete obj["k"]!` removes field and history.

### History & Time Travel
- `history(x)` → array of past values indexed from `-1`.
- `previous(x)` moves one step back; `next(x)` moves forward (applies to vars and fields).

### Pattern Matching & Destructuring
- Array pattern: `[a, b=5, ...rest]`.
- Object pattern: `{key, key: alias, missing=99}`; defaults apply when value is `undefined`.
- `_` ignores a binding.
- `when target matches pattern where guard { ... }` binds variables only inside body/guard.

### Delete
- `delete` works on primitives or fields/indices; deleted primitive values throw if read again.

### Special Whitespace Rules
- Parentheses are treated as whitespace; precedence is driven by spaces:
  - Fewer spaces around operator → tighter binding. Example: `1*2 + 3` → `(1*2)+3`; `1 * 2+3` → `1*(2+3)`.
- Calls: `foo 1, 2` is valid (no parentheses).

### Keywords
- Control: `if`, `else`, `idk`, `while`, `break`, `continue`, `when`, `return`, `delete`, `reverse`, `forward`, `try again`.
- Decls: `var`, `const`, `class`, `function`, `static`, `fallback`.
- Operators as words: `clamp`, `wrap`.

---

## Polski

- [Przegląd](#przegląd)
- [Literały](#literały)
- [Nazwy i identyfikatory](#nazwy-i-identyfikatory)
- [Wyrażenia](#wyrażenia)
- [Instrukcje](#instrukcje)
- [Operatory update](#operatory-update)
- [Przedziały, clamp i wrap](#przedziały-clamp-i-wrap)
- [Sterowanie przepływem](#sterowanie-przepływem)
- [Funkcje](#funkcje)
- [Klasy](#klasy)
- [Historia i cofanie](#historia-i-cofanie)
- [Pattern matching i destrukturyzacja](#pattern-matching-i-destrukturyzacja)
- [Delete](#delete-1)
- [Specjalne reguły odstępów](#specjalne-reguły-odstępów)
- [Słowa kluczowe](#słowa-kluczowe)

### Przegląd
- DreamBerd to żartobliwy język: nawiasy są opcjonalne i traktowane jak spacje.
- Instrukcje kończą się `!` (lub `?` dla debugowego wydruku wartości).
- Tablice startują od indeksu `-1`. Obiekty to singletony klas.
- Prawdziwość: liczby ≠ 0, niepuste stringi, tablice/obiekty, boolean `true/maybe`.

### Literały
- Liczby: `123`, `-4.2` lub słowne (`one hundred`, `dwa tysiące trzy`).
- Stringi: `"tekst"` (pusty string może być nazwą).
- Boolean: `true`, `false`, `maybe`.
- `null`, `undefined`.
- Tablice: `[a, b, c]` → indeksy `-1,0,1`.
- Przedziały: `[0 .. 10]`, `[0 .. 10)`, `(0 .. 1]`, `(0 .. 1)`.

### Nazwy i identyfikatory
- Dowolne słowo/emoji/keyword/liczba/pusty string może być nazwą: `var var 5 = 0!`, `var var "" = 1!`, `function return x => ...`.
- Słowa kluczowe można używać jako identyfikatorów.

### Wyrażenia
- Arytmetyka: `+ - * /`.
- Porównania: `< > <= >=`.
- Równość: `==` (bardzo luźna), `===` (luźna/konwersja liczb), `====` (ściśle).
- Min/Max: `<>` lub `⌊⌋` (min), `><` lub `⌈⌉` (max).
- Unarne: `-x`, negacja `;x`, wartość bezwzględna `||x`, sin `~x`, cos `~~x`, tan `~~~x`.
- Potęga postfix: `x**` (kwadrat), `x****` (sześcian) itd.
- Pierwiastek prefix: `\\x` (sqrt), `\\\\x` (cbrt) itd.
- Pierwiastek infix: `a \\ n`.
- Operator warunkowy 4-gałęziowy: `cond ? t : f : m : u`.
- Clamp/Wrap: patrz niżej.

### Instrukcje
- Instrukcja wyrażeniowa: `expr!` lub `expr?` (debug).
- Deklaracje: `var var x = 1!`, `const const y = 2!`, `const const const z = 3!`.
- Destrukturyzacja: `var var [a, b=5, ...rest] = expr!`, `var var {foo, bar: alias, missing=99} = obj!`.
- Delete: `delete x!`, `delete arr[i]!`, `delete obj["k"]!`.
- Bloki `{ ... }` tworzą scope.

### Operatory update
- Arytmetyczne: `:+` `:-` `:*` `:/` `:%`.
- Potęgowe: `:*!` (kwadrat), `:**!` (sześcian), `:***!` itd.
- Pierwiastki: `:\\!` (sqrt), `:\\\\!` (cbrt), `:\\ n!` (n-ty pierwiastek).
- Bitowe: `:&` `:|` `:^` `:<<` `:>>`.
- Nullish assign: `:??` (tylko gdy `undefined`).
- Min/Max: `:<` (min), `:>` (max).
- Trygonometria: `:~` (sin), `:~~` (cos), `:~~~` (tan).
- Clamp: `:? [lo .. hi]!` lub `:? clamp [lo .. hi]!`.
- Wrap: `:? x @ [lo .. hi)!` lub `:? wrap [lo .. hi)!`.

### Przedziały, clamp i wrap
- Końce mogą być domknięte/otwarte: `[a .. b]`, `(a .. b]`, `[a .. b)`, `(a .. b)`.
- Clamp infix: `value ▷ [lo .. hi]` lub `value clamp [lo .. hi]`.
- Wrap (mod) infix: `value ↻ [lo .. hi)` lub `value wrap [lo .. hi)`.
- Update z `:?` i ewentualnym `@ [range]` dla wrap.

### Sterowanie przepływem
- `if cond { ... } [else { ... }] [idk { ... }]` (bez nawiasów).
- `while cond { ... }`, `break!`, `continue!`.
- `try again!` w if/else/idk ponawia warunek.
- `when expr { ... }` subskrybuje mutacje zmiennych z `expr` (lub wszystkich, gdy brak).
- Pattern when: `when <target> matches <pattern> [where guard] { ... }`.

### Funkcje
- Deklaracja: `function name a, b => expression!` lub ciało blokowe.
- Wywołania bez nawiasów: `name 1, x+2, "ok"!`.
- `return expr!` lub `return!`.
- Wbudowane: `print`, `previous`, `next`, `history`, `toNumber`.

### Klasy
- Deklaracja: `MyClass is a class { ... }` (singleton na nazwę).
- Metody: `function foo x => { ... }` (instancja), `static function bar x => { ... }`.
- Własności: `prop : default 123!` (auto-prop), `static staticProp : default 5!`.
- Fallbacki: `fallback : default val!` (instancja), `static fallback : default val!`.
- Dostęp: `MyClass["field"]`, `obj["method"] args!`; `source` w metodach to `this`.
- Historia działa na polach; `delete obj["k"]!` usuwa pole i historię.

### Historia i cofanie
- `history(x)` → tablica poprzednich wartości od indeksu `-1`.
- `previous(x)` cofa o krok; `next(x)` idzie naprzód (działa dla zmiennych i pól).

### Pattern matching i destrukturyzacja
- Tablica: `[a, b=5, ...rest]`.
- Obiekt: `{key, key: alias, missing=99}`; default gdy wartość `undefined`.
- `_` pomija bindowanie.
- `when target matches pattern where guard { ... }` wiąże zmienne tylko w strażniku/ciele.

### Delete
- `delete` na prymitywach lub polach/indeksach; usunięte wartości prymitywne rzucają błąd przy ponownym użyciu.

### Specjalne reguły odstępów
- Nawiasy to whitespace; priorytet wymuszają spacje:
  - Mniej spacji przy operatorze → silniejsze wiązanie. Przykład: `1*2 + 3` → `(1*2)+3`; `1 * 2+3` → `1*(2+3)`.
- Wywołania: `foo 1, 2` jest poprawne (bez nawiasów).

### Słowa kluczowe
- Sterowanie: `if`, `else`, `idk`, `while`, `break`, `continue`, `when`, `return`, `delete`, `reverse`, `forward`, `try again`.
- Deklaracje: `var`, `const`, `class`, `function`, `static`, `fallback`.
- Słowne operatory: `clamp`, `wrap`.
