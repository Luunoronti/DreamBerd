# DreamBerd — C# interpreter (WIP)

This DreamBerd interpreter is being built **together**: me + **ChatGPT 5.2 Thinking** + my hands-on work in the code (reading, fixing, refactoring, testing).  
This is **team coding** (not “vibe coding”).

> **Organizational note (very important):** to make development easier and code navigation faster for everyone worldwide, all comments in this project are written in **Polish**.  


---

## Upstream specification

The official DreamBerd specification/README lives here:  
- https://github.com/TodePond/GulfOfMexico (README)

The upstream repo is sometimes called “Gulf of Mexico”, but in this project we consistently use the name **DreamBerd**.

---

## Docs in this repo

- Feature list (PL): [features.md](./features.md)  
- Feature list (EN): [features_en.md](./features_en.md)
- Language guide (PL/EN): [LANGUAGE_GUIDE.md](./LANGUAGE_GUIDE.md)

These files are our source of truth for what is implemented, what differs from the spec, and what is still missing.

---

## Spec compatibility

**Estimated: ~74%** compatibility with the upstream README/spec (user-facing behavior), based on the checklist in `features_en.md` (62 implemented, 5 partial counted as 0.5, 20 missing).

This is a *human estimate*, not an automated measurement. Some spec items are pure meme/gag features (AQMI/AI, whitespace-math, "parentheses do nothing", etc.) that we don't have yet, while we already implement a bunch of practical core pieces (parser, if/else/idk, functions, arrays, lifetimes, when(), history(), stdlib).  
For the detailed breakdown see: [features.md](./features.md) / [features_en.md](./features_en.md).

---

## How to download, build, and run

### Requirements
- **.NET SDK 10** (the project uses `TargetFramework=net10.0` in `DreamBerd.csproj`).  
  If you don’t have .NET 10 installed, you can temporarily change `TargetFramework` to e.g. `net9.0` or `net8.0` (as long as the used APIs remain compatible).

### Clone
```bash
git clone <THIS-REPO-URL>
cd <repo-folder>
```

### Build
```bash
dotnet build -c Release
```

### Run a `.dberd` file
```bash
dotnet run -c Release -- <path-to-file.dberd>
```

Example files in this repo:
- `dreamberd_tests.dberd`
- `stdlib_demo.dberd`
- `test_when.dberd`

### REPL
Run without arguments to start the REPL:
```bash
dotnet run -c Release
```

In the REPL:
- **empty line** = execute the buffered code
- type `exit` to quit

---

## Quick notes

- File paths used by stdlib (e.g. `readFile`, `readLines`) are resolved relative to the executed script (we set the `CurrentDirectory` to the script’s folder).

---

## Status

This project is **WIP** and we treat it as an experiment playground: some things will become “more DreamBerd”, some will stay intentionally practical (so we can actually write programs and test ideas).  
To check what’s currently implemented, see: [features_en.md](./features_en.md).
