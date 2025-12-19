# DreamBerd (C# interpreter) — zgodnosc ze Spec.md

Ten plik porownuje nasz interpreter C# z oficjalnym `Spec.md` (DreamBerd/GulfOfMexico).

Legenda:
- 🟢 zaimplementowane
- 🟡 czesciowe / inne niz w spec
- 🔴 brak

Migawka (partial = 0.5):
- Liczymy 34 sekcje jezykowe z `Spec.md` (bez czysto marketingowych/organizacyjnych: Installation, Copilot, Ownership, Contributing, Compiling, Highlighting, Vision Pro, Edutainment, Examples).
- Wynik: 14 🟢, 7 🟡, 13 🔴 -> ok. 52% pokrycia.

## Pozycje ze Spec.md (liczone)

| Sekcja ze Spec.md | Status | Uwagi |
| --- | --- | --- |
| Exclamation Marks! | 🟢 | `!`/`?` jako terminatory, wiele znakow, `;` to negacja. |
| Declarations | 🟡 | Cztery kombinacje + `const const const`; model "editable vs re-assignable" uproszczony (brak regulek mutacji obiektow). |
| Immutable Data | 🟢 | `const const const` jest niezmienne; zakres tylko w biezacym uruchomieniu. |
| Naming | 🟢 | Dowolne Unicode/emoji/cyfry/keywordy, puste nazwy, token liczbowy moze byc identyfikatorem. |
| Arrays | 🟢 | Literaly, indeks start -1, indeksy float, brakujacy indeks -> `undefined`, helper `numArray`. |
| When | 🟢 | `when` subskrybuje uzyte zmienne; nawiasy opcjonalne; wildcard bez zaleznosci. |
| Lifetimes | 🟢 | `<N>/<Ns>/<Infinity>` i ujemne lifetimes; wygasanie powoduje fallback do starszych overloadow. |
| Loops | 🟡 | W specyfikacji "no loops"; interpreter ma `while` + `break`/`continue`. |
| Booleans | 🟢 | `true` / `false` / `maybe`. |
| Arithmetic | 🟢 | Znaczace spacje, `+ - * /`, unarne; slowne liczby EN/PL (ograniczone, bez zlozen typu twenty-one); dzielenie przez 0 -> `undefined`. |
| Indents | 🔴 | Brak egzekwowania reguly 3 spacji. |
| Equality | 🟢 | `==`, `===`, `====` oraz super-luzne `=`. |
| Functions | 🟢 | Dowolny prefiks slowa "function"; nawiasy opcjonalne; dziala return/rekurencja. |
| Dividing by Zero | 🟢 | `/0` zwraca `undefined`. |
| Strings | 🟡 | Dowolna liczba cudzyslowow, asymetryczne; 0-cudzyslowow w pierwszej kolejnosci szuka identyfikatora (spec: zawsze string). |
| String Interpolation | 🟡 | Podstawowe `{name}` / `$name`; brak wariantow walutowych/typograficznych. |
| Types | 🔴 | Brak obslugi adnotacji typow. |
| Regular Expressions | 🔴 | Brak typu `RegExp`. |
| Previous | 🟡 | `previous/next/current` + `history`; brak `await next`. |
| File Structure | 🔴 | Brak blokow `=====` w jednym pliku. |
| Exporting | 🔴 | Brak `export ... to` / `import ...!`. |
| Classes | 🔴 | Brak klas/singeltonow/pol. |
| Time | 🔴 | Brak `Date.now()` i modyfikacji czasu. |
| Delete | 🟡 | Dziala kasowanie prymitywow; brak kasowania keywordow/paradygmatow. |
| Overloading | 🟡 | Priorytet wg liczby `!`, potem nowosc, fallback przez lifetimes; brak odwroconego `¡`. |
| Semantic naming | 🟢 | Prefiksy typu sName/iAge/bHappy sa dozwolone; dodatkowych semantyk nie potrzeba. |
| Reversing | 🟢 | `reverse!` dziala lokalnie. |
| Class Names | 🔴 | Brak `className` (i brak klas). |
| DBX | 🔴 | Brak wstrzykiwania HTML/DBX. |
| Rich text | 🔴 | Brak rich-text/odnosnikow w stringach. |
| Asynchronous Functions | 🔴 | Brak `async`/`await`/`noop` kolejkowania. |
| Signals | 🔴 | Brak `use()` i getter/setter destructuring. |
| AI | 🔴 | Brak AEMI/ABI/AQMI (automatyczne znaki). |
| Parentheses | 🟢 | Nawiasy w wiekszosci ignorowane/traktowane jak spacje. |

## Nasze dodatki poza specyfikacja

- `while`/`break`/`continue`; miejscami terminator po bloku jest opcjonalny.
- `printsl` oraz helpery stdlib (`readFile`, `readLines`, `trim`, `split`, `lines`, `charAt`, `slice`, `toNumber`/`parseInt`).
- Slowne liczby EN/PL; dodatkowe operatory unarne/tryg/clamp/wrap/potegi/korzenie.
- `history(x)`, fallback z lifetimes, szeroko opcjonalne nawiasy.

## Sekcje ze Spec.md nie liczone do procentu

- Installation: mamy po prostu aplikacje .NET, brak installer/installer-installer.
- Copilot: brak specjalnych zabezpieczen.
- Ownership: interpreter nic nie wymusza.
- Contributing: nie odwzorowujemy sekcji charity.
- Compiling: zamiast workflowu na czacie mamy realny interpreter.
- Highlighting: w repo brak konfiga do VSCode.
- Vision Pro / Edutainment: sekcje marketingowe.
- Examples: mamy pliki testowe, ale nie ma jeszcze odpowiednika `Examples.md`.
