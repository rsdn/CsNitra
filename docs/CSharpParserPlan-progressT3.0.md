# T3.0 — Versioning infrastructure (merged grammar up to a version)

Status: in progress.

## Goal
Test-only infrastructure to load a merged C# grammar up to a given version
(Cs1 + Cs2 + ... + CsN, ascending) so Stage 3 (CS2–CS14) version tests and
version-purity tests (parse at N, reject at N-1) are natural.

## Baseline (before changes)
- `dotnet test Tests/CSharpGrammarTests` (fresh build) → **766 passed / 0 failed / 3 skipped** (total 769).

## API added

### 1. `EmbeddedGrammar.LoadGrammarUpTo(int version)` — `Tests/CSharpGrammarTests/EmbeddedGrammar.cs`
```csharp
public static IReadOnlyList<(string Text, string Path)> LoadGrammarUpTo(int version)
```
Returns the grammar texts for all versions `<= version`, sorted ascending by
version. `Path` in each tuple is the file name (e.g. `"Cs1.grammar"`) for error
messages. Only existing files are included (the table lists only files that
exist), so `LoadGrammarUpTo(3)` → `[Cs1]` (Cs2/Cs3 do not exist yet).

- `LoadGrammarUpTo(1)` → `[Cs1]`
- `LoadGrammarUpTo(6)` → `[Cs1, Cs6]`
- `LoadGrammarUpTo(11)` → `[Cs1, Cs6, Cs11]`
- `LoadGrammarUpTo(3)` → `[Cs1]`

### 2. `CSharpVersionTestHelper.CreateParser(int version)` — `Tests/CSharpGrammarTests/CSharpVersionTestHelper.cs` (new)
```csharp
public static CSharpParser CreateParser(int version)
```
Wraps the multi-text `CSharpParser` constructor:
`new CSharpParser(EmbeddedGrammar.LoadGrammarUpTo(version), CSharpTerminals.Trivia(), CSharpTerminals.GetAll())`.

## Version-table design
Static `IReadOnlyList<GrammarVersion>` in `EmbeddedGrammar`, where
`GrammarVersion` is a `sealed record` with fields `(int Version, string Suffix, string Path)`.
`Suffix` is the embedded-resource suffix used to locate the resource; `Path` is the
file name shown in error messages (currently identical strings).

```csharp
private static readonly IReadOnlyList<GrammarVersion> _versions =
[
    new(1,  "Cs1.grammar",  "Cs1.grammar"),
    new(6,  "Cs6.grammar",  "Cs6.grammar"),
    new(11, "Cs11.grammar", "Cs11.grammar"),
];
```

### One-line recipe to add a new version
Add one line to `_versions` (keeping ascending order):
```csharp
new(2, "Cs2.grammar", "Cs2.grammar"),
```
(plus the `Cs2.grammar` file + its `<EmbeddedResource>` entry in
`CSharpGrammar.csproj` when the grammar file itself is added — that part is a
later Stage-3 task, not T3.0).

## Tests added
`Tests/CSharpGrammarTests/CSharpVersionInfrastructureTests.cs` (new):
- `LoadGrammarUpTo_1_YieldsCs1Only`
- `LoadGrammarUpTo_6_YieldsCs1Cs6InOrder`
- `LoadGrammarUpTo_11_YieldsCs1Cs6Cs11InOrder`
- `LoadGrammarUpTo_3_YieldsOnlyExistingFiles`
- `CreateParser_11_ParsesCs1Program` (smoke: merged grammar parses a Cs1 program)
- `CreateParser_11_ParsesCs11RawStringProgram` (smoke: merged grammar parses a Cs11 raw string)

## Files changed
- `Tests/CSharpGrammarTests/EmbeddedGrammar.cs` (added `LoadGrammarUpTo` + version table; per-file loaders untouched)
- `Tests/CSharpGrammarTests/CSharpVersionTestHelper.cs` (new)
- `Tests/CSharpGrammarTests/CSharpVersionInfrastructureTests.cs` (new)
- `docs/CSharpParserPlan-progressT3.0.md` (this file)

## Verification
- [ ] `dotnet build Nitra.sln --no-incremental` → 0 errors
- [ ] `dotnet test Tests/CSharpGrammarTests` (fresh build) → all green
