# DreamBerd (C# interpreter) — lista funkcji: jest vs brakuje

Ten plik porównuje **aktualny stan naszego interpretera w C#** z „kanoniczną” specyfikacją/README projektu **DreamBerd** (repozytorium na GitHubie, które bywa nazywane „Gulf of Mexico”).

- **Stan projektu (ten repo ZIP):** interpreter DreamBerd w C# (.NET), konsolka + REPL.
- **Cel dokumentu:** szybka checklista „co mamy” i „co jeszcze nie istnieje”, żeby nie zgubić kierunku.

Legenda:
- ✅ = zaimplementowane
- 🟡 = częściowo / inaczej niż w specyfikacji
- ❌ = brak

---

## ✅ Co już mamy w tym interpreterze

### Uruchamianie
- ✅ Tryb **plik**: `DreamberdInterpreter.exe <ścieżka>` → wykonaj i wyjdź.
- ✅ Tryb **REPL** bez argumentów: wklejanie wielu linii (zbieranie aż do pustej linii), potem parsowanie i wykonanie.

### Tokenizacja / parser
- ✅ Tokeny z pozycją (offset w źródle) i AST z `Position` (do błędów).
- ✅ Komentarze `// ...` do końca linii.
- ✅ Podstawowe literały: liczby (double), stringi `'...'` i `"..."`.
- ✅ Identyfikatory (litery/underscore/$ + cyfry dalej).

### Zakończenia instrukcji
- ✅ Każdy statement kończy się `!` albo `?`.
- ✅ `?` = tryb debug (drukowanie wartości / historii).
- ❌ W naszej implementacji **nie ma** wielokrotnych `!!!` jako priorytetu (w specyfikacji to jest).

### Deklaracje
- ✅ 4 warianty: `const const`, `const var`, `var const`, `var var`.
- ✅ `const const const` jako osobny store (globalnie „nie do ruszenia”).
- ✅ Lifetimes: składnia `<N>` i `<N s>` oraz `<Infinity>` (w runtime wygaszanie po statementach i/lub czasie).

### Bloki i scope’y
- ✅ Bloki `{ ... }`.
- ✅ Scope blokowy (push/pop scope w `VariableStore`) – zmienne blokowe nie „wyciekają” na zewnątrz.
- ✅ Funkcje mają osobne scope’y (callframe/locals).

### Kontrola przepływu
- ✅ `if (cond) stmt` oraz `if (cond) { ... } else { ... }`.
- ✅ `reverse!` / `forward!` – zmiana kierunku iterowania po liście statementów.
- ✅ (Rozszerzenie względem specyfikacji) `while`, `break`, `continue`.
- ✅ `return` (jako statement; w funkcjach działa przez wewnętrzny mechanizm przerwania wykonania).

### Wartości runtime
- ✅ Typy: Number, String, Boolean (`true/false/maybe`), Null, Undefined, Array.
- ✅ Truthiness:
  - `false`, `null`, `undefined`, `0`, pusty string, pusta tablica → falsy
  - `true` i `maybe` → truthy

### Wyrażenia
- ✅ Arytmetyka: `+ - * /` (z konkatenacją stringów dla `+`).
- ✅ Dzielenie przez 0 → `undefined`.
- ✅ Porównania: `< <= > >=` (na liczbach po konwersji).
- ✅ Równości: `==`, `===`, `====` (nasza, „dreamberdowa” semantyka).
- ✅ Przypisania: `x = expr`.
- ✅ Tablice: `[a, b, c]`, indeksy od `-1` wzwyż, indeksowanie floatami.
- ✅ Odczyt i zapis indeksu: `arr[idx]`, `arr[idx] = value` (immutable-by-value: podmiana całej tablicy).
- ✅ Wywołania funkcji: `foo(a, b)`.
- ✅ 4-gałęziowy operator warunkowy: `cond ? t : f :: m ::: u`.

### Funkcje
- ✅ Deklaracje: dowolny prefix „function” (`function`, `func`, `fun`, `fn`, `functi`, `f`).
- ✅ Ciało funkcji: expression **lub** blok `{ ... }`.
- ✅ Rekursja działa.

### Wbudowane rzeczy
- ✅ `print(...)`.
- ✅ Historia zmiennych:
  - `previous(x)`, `next(x)` – przesuwanie kursora historii
  - `history(x)` – zwraca tablicę historii
  - `?` na identyfikatorze wypisuje historię

### `delete`
- ✅ `delete <primitive>!` usuwa: Number / String / Boolean (true/false/maybe).
- ✅ Po `delete` próba uzyskania takiej wartości (wyniku evaluate) powoduje błąd.
- ❌ Usuwanie słów kluczowych / konstrukcji języka (np. `delete class!`) – niezaimplementowane.

### `when`
- ✅ `when (cond) stmt!` (subskrypcja wykonywana po mutacjach zmiennych).
- 🟡 Różnice vs README:
  - w specyfikacji warunek bywa zapisany przez `=` (tam to „porównanie”), u nas `=` to przypisanie, a porównania to `==/===/====`.
  - nasz model odpala sprawdzanie po każdej mutacji zmiennej (to blisko idei, ale szczegóły mogą się różnić).

---

## 🟡 Mamy, ale inaczej / niepełne

- 🟡 **Identyfikatory „dowolny Unicode / string”**: README dopuszcza właściwie wszystko (włącznie z nazwą będącą liczbą). U nas identyfikator ma klasyczne reguły (litery/`_`/`$`, potem cyfry).
- 🟡 **Overloading / priorytety**: README ma priorytety zależne od ilości `!` oraz `¡` (ujemne). U nas statement kończy się pojedynczym `!` albo `?`, a priorytet w deklaracji jest na razie stały.
- 🟡 **Lifetimes „trwają między uruchomieniami”**: w README jest sugestia, że da się ustawić lifetime dłuższy niż pojedynczy run. U nas nie ma persistence między uruchomieniami.

---

## ❌ Co jeszcze brakuje względem „specyfikacji” z README

Poniżej lista funkcji/sekcji, które występują w README DreamBerd, a których nie obsługujemy (albo w ogóle, albo znacząco odbiegamy).

### Składnia / whitespace / parser-quirks
- ❌ **„Not” jako `;`** (semi-kolon) zamiast `!`.
- ❌ **Znaczące whitespace dla arytmetyki** (kolejność działań zależna od spacji).
- ❌ **Nawiasy nic nie robią** (w README są ignorowane i zastępowane whitespace).
- ❌ **Indent = dokładnie 3 spacje** (walidacja w parserze).
- ❌ **AI auto-wstawianie**: AEMI/ABI/AQMI (auto `!`, auto domykanie nawiasów i cudzysłowów).

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
- ❌ „Number names” typu `one`, `two`.
- ❌ Rozbudowane „naming” (np. deklaracje ze stringową nazwą, nazwy będące cyframi, itp.).

---

## Notatki: nasze rozszerzenia (poza README)

- ✅ `while / break / continue` – README twierdzi, że „nie ma pętli”. U nas pętle istnieją (praktyczne do testów i rozwoju).
- ✅ `return` jako statement + funkcje z blokami `{ ... }`.

---

## Sugestia kolejności dalszych prac (opcjonalnie)

Jeżeli chcemy zbliżać się do README, a jednocześnie nie wysadzić parsera od razu:
1) `!` wielokrotne + priorytet deklaracji (overloading)
2) `=` jako „luźna równość” (osobny operator, nie Assign)
3) `previous/next/current` jako keywordy (bez nawiasów)
4) „Parentheses do nothing” (można zrobić jako pre-process/token-filter)
5) dopiero potem: znaczące whitespace dla arytmetyki (to jest największy przewrót)

