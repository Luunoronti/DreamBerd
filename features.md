# DreamBerd (C# interpreter) — lista funkcji: jest vs brakuje

Ten plik porównuje **aktualny stan naszego interpretera w C#** z „kanoniczną” specyfikacją/README projektu **DreamBerd** (repozytorium na GitHubie, które bywa nazywane „Gulf of Mexico”).

- **Stan projektu (ten repo ZIP):** interpreter DreamBerd w C# (.NET), konsolka + REPL.
- **Cel dokumentu:** szybka checklista „co mamy” i „co jeszcze nie istnieje”, żeby nie zgubić kierunku.

Legenda:
- ✅ = zaimplementowane
- 🟡 = częściowo / inaczej niż w specyfikacji
- ❌ = brak

---

## ✅ Co już mamy w tym interpreterze (zgodne z README lub bardzo blisko)

### Uruchamianie
- ✅ Tryb **plik**: `DreamberdInterpreter.exe <ścieżka>` → wykonaj plik.
- ✅ Tryb **REPL**: bez argumentów → czyta wejście aż do pustej linii, odpala, powtarza.

### Lekser + parser
- ✅ Tokenizacja podstawowej składni (identyfikatory, liczby, stringi, operatory, bloki).
- ✅ Parser AST dla statementów i wyrażeń.
- ✅ Błędy z `line:column` + podkreśleniem miejsca w linii.

### Zakończenia statementów
- ✅ `!` jako terminator statementu.
- ✅ `?` jako terminator debug (drukuje wartość wyrażenia, a dla identyfikatora także `history(...)`).
- ✅ Dowolna liczba `!`/`?` (np. `!!!`) jest akceptowana.
- ✅ Liczba `!` jest używana jako *priorytet deklaracji* (overloading).

### Deklaracje (mutability)
- ✅ `const const`, `const var`, `var const`, `var var`.
- ✅ `const const const` jako globalny, immutable store (nie da się przypisać ani nadpisać).
- 🟡 Semantyka „editable vs re-assignable” jest uproszczona (nie mamy obiektów/metod typu `push/pop`).

### Typy i literały
- ✅ Liczby (double).
- ✅ Stringi w `"..."` oraz `'...'`.
- ✅ Booleany 3-stanowe: `true`, `false`, `maybe`.
- ✅ `undefined`.
- 🟡 `null` istnieje jako wartość runtime (np. wynik statementów), ale nie ma osobnego literału `null` w parserze.

### Wyrażenia i operatory
- ✅ Arytmetyka: `+ - * /` (dzielenie przez 0 → `undefined`).
- ✅ Porównania: `< > <= >=`.
- ✅ Równość: `==` (very loose / stringowo), `===` (loose / numerycznie), `====` (strict).
- ✅ Unarny minus: `-x`.
- ✅ Przypisanie: `x = expr`.
- ✅ Przypisanie indeksu: `arr[idx] = expr`.

### Operator warunkowy (4 gałęzie)
- ✅ `cond ? whenTrue`
- ✅ Opcjonalne gałęzie (mogą wystąpić w dowolnej kolejności, i mogą być pominięte):
  - `: whenFalse`
  - `:: whenMaybe`
  - `::: whenUndefined`
- ✅ Brakująca gałąź → wynik `undefined`.

### Kontrola przepływu
- ✅ `if (cond) ... else ... idk ...`
  - `idk` odpala się, gdy `cond` jest `maybe`.
- ✅ Bloki `{ ... }` tworzą scope (shadowing działa).
- ✅ `return expr` w funkcjach.

### Funkcje
- ✅ Deklaracje: `function|func|fun|fn|functi|f name(args) => { ... }`
- ✅ Call stack + lokalne zmienne funkcji.
- ✅ Rekurencja działa.

### Tablice
- ✅ Literały: `[a, b, c]`.
- ✅ Indeksy startują od `-1`.
- ✅ Indeksy mogą być float (`double`).
- ✅ Odczyt brakującego indeksu → `undefined`.
- ✅ `numArray(init, size)` tworzy tablicę numeryczną (indeksy od -1).

### Lifetimes + overloading deklaracji
- ✅ Lifetime: `<N>` (linie), `<N s>` (sekundy), `<Infinity>`.
- ✅ Overloading: wiele deklaracji tej samej nazwy w scope:
  - wybór aktywnej: najwyższy priorytet (liczba `!`), potem „najświeższa”
  - wygasanie lifetimes może powodować fallback do starszej deklaracji
- ✅ Historia zmiennych: `previous(x)`, `next(x)`, `history(x)`.

### when(...)
- ✅ `when (condition) { ... }` subskrybuje mutacje zmiennych użytych w condition.
- ✅ Gdy condition nie używa zmiennych (np. `when (true)`), odpala się po każdej mutacji (wildcard `*`).
- ✅ Dispatch przez kolejkę (bez rekurencji przy mutacjach).

### delete
- ✅ `delete <primitive>` działa na number/string/boolean (zgodnie z README).
  - po usunięciu: użycie tej wartości rzuca błąd.

### Mini stdlib
- ✅ `print(...)`
- ✅ IO: `readFile(path)`, `readLines(path)`
- ✅ Strings: `lines(text)`, `trim(text)`, `split(text, sep)`, `charAt(text, idx)`, `slice(text, start)`
- ✅ Konwersje: `toNumber(x)` (+ aliasy `parseInt`, `parseNumber`)

---

## ✅ Nasze rozszerzenia (poza oficjalnym README DreamBerd)

- ✅ `while (cond) { ... }` + `break` + `continue` (README mówi „no loops”).
- ✅ Terminator statementu bywa opcjonalny (np. po `if/while` i po niektórych statementach).
- ✅ Normalne znaczenie nawiasów `()` (w README nawiasy „nic nie robią”).
- ✅ Klasyczne priorytety operatorów (w README priorytet wynika z whitespace).

---

## 🟡 Mamy, ale inaczej / niepełne (względem README)

- 🟡 Mutability `const var` / `var var` nie wspiera „mutacji obiektów” (brak metod jak `push/pop`, brak obiektów).
- 🟡 Naming: wspieramy Unicode *litery*, ale nie wspieramy emoji jako nazw, ani pełnego „number naming”.
- 🟡 "Number names": slowa liczb po angielsku (`zero`..`nineteen`, `twenty`..`ninety`, skale do `quintillion`) i po polsku (`jeden`..`dziewietnascie`, `dwadziescia`.., skale do `trylionu`); parsujemy na literal tylko gdy slowa nie sa nazwami w scope i dopoki nie trafimy na nieznane slowo (wtedy literal zmienia sie w string calkowitego wejscia). `toNumber("...")` rozumie te same slowa. Brak ulamkow / `twenty-one` / polskich ulamkow / znaku minus.

---

## ❌ Co jeszcze brakuje (z oficjalnego README / specyfikacji)

### Składnia / whitespace / parser-quirks

- ❌ Operator „not” jako `;` (np. `if (;false) { ... }`).
- ❌ „Parentheses do nothing” (nawiasy ignorowane / zamieniane na whitespace).
- ❌ „Significant whitespace” dla kolejności działań w arytmetyce.
- ❌ Narzucone indenty: dokładnie 3 spacje (i -3 spacje).
- ❌ Rozszerzone nazewnictwo: emoji, puste nazwy, nazwy będące keywordami, pełny „number naming”.
- ❌ Pełny model „editable vs re-assignable” (mutacje struktur/obiektów jak `push/pop`).
- ❌ Kasowanie keywordów/paradygmatów (`delete class`, `delete delete`, …).
- ❌ AQMI / AI / Copilot gag-features z README.
- ❌ Instalator / CLI zgodny z README (tu mamy tylko nasz .NET runner).


### Operatory / wyrażenia
- ❌ Operator `=` jako „super-luźna równość” (README wspomina „jeśli chcesz być dużo mniej precyzyjny”).
- ❌ `++`, `--` i inne skróty.
- ❌ `^` (potęgowanie) i inne dodatkowe operatory z przykładów.

### Stringi
- ❌ Dowolna liczba cudzysłowów (np. `''''Lu''''`), włącznie z **0** (`name = Luke!`).
- ❌ String interpolation z walutami: `${name}`, `£{name}`, `{name}€` itd.
- ❌ „Rich text” / linki w stringach.

### `previous` / `next` / `current` jako „keywordy”
- ❌ Składnia typu `previous score` (bez nawiasów).
- ❌ `current`.
- ❌ `await next score` i w ogóle async/await model z README.

### Struktura plików / import/export
- ❌ Separator plików przez `=====` w jednym pliku.
- ❌ Nadawanie nazw plikom `======= add.gom =======`.
- ❌ `export ... to "..."!` i `import ...!`.

### OOP / klasy
- ❌ `class` + reguła „tylko jedna instancja klasy”.
- ❌ `new`, pola, metody, `.`.
- ❌ `className`.

### Czas
- ❌ `Date.now()` i możliwość zmiany czasu przez `Date.now() -= ...`.

### DBX / HTML-in-code
- ❌ DBX (HTML/JSX-like w kodzie).
- ❌ `htmlClassName` zasady.

### Asynchroniczność / współbieżność
- ❌ `async` funkcje „na zmianę po liniach”.
- ❌ `noop` jako „czekanie”/zajmowanie linii.

### Signals
- ❌ `use(...)` jako sygnały (funkcja będąca jednocześnie getterem/setterem).
- ❌ Destrukturyzacja `const var [get, set] = use(0)!`.

### `delete` języka
- ❌ `delete class!`, `delete delete!` itd. (kasowanie słów kluczowych / paradygmatów).

### Inne
- ❌ Rozbudowane „naming” (np. deklaracje ze stringową nazwą, nazwy będące cyframi, itp.).


---

## Sugestia kolejności dalszych prac

1) Dopiąć zgodność ze spec: `;` jako not + tryb bez-normalnych-nawiasów (albo tryb kompatybilności).  
2) Naming (szerszy Unicode + number naming).  
3) Mutability „editable” (albo przynajmniej sensowna mutacja tablic dla `const var` / `var var`).  
4) Indenty + significant whitespace.