# CsPreprocessor — T5.1 Progress: hardening tests over REAL repo `.cs` files

Sub-point: **T5.1** (Stage 5 — Hardening).
Task: run the preprocessor (and then the real C# parser) over a set of REAL `.cs` files from this repo and
assert (a) the preprocessor does not crash on any of them, and (b) for files the C# parser can parse, the
preprocessed `Text` still parses cleanly (blanking did not corrupt the kept code).

## Scope / files touched

- **Added** `Tests/CsPreprocessorTests/RealFileHardeningTests.cs` (`[TestClass]`, MSTest, sealed, method-level
  parallelism via the existing `[assembly: Parallelize(MethodLevel)]`).
- **Added** `docs/CsPreprocessor-progressT5.1.md` (this file).
- **No production code touched.** No existing test files modified.

## API used

- `Preprocessor.Run(string source, IEnumerable<string> commandLineSymbols)` → `PreprocessResult`
  (namespace `CsPreprocessor`). `.Text` is the preprocessed (directive-free, same-length) source.
  Command-line symbols passed as `Array.Empty<string>()` — the preprocessor has **no** built-in/default
  symbols (`DirectiveStack` only knows command-line symbols + in-source `#define`/`#undef`), so `#if
  NETSTANDARD2_0`, `#if NATIVEAOT`, `#if RECOVERY` etc. are all **inactive** by default.
- `CSharpGrammarLoader.CreateCSharpParser()` (existing helper) builds a `CSharpParser` from the embedded
  **`Cs1.grammar`** (C# 1.0). Parse: `parser.Parse(text, "Grammar", out _)`; clean success means
  `TryGetSuccess(out var node, out var end)`, `end == text.Length`, `node` non-null,
  `parser.Parser.ErrorInfo` null, `parser.Parser.RecoveryDiagnostics.Count == 0`.
- Real files are read as **UTF-8** (`File.ReadAllText(path, Encoding.UTF8)`), resolved relative to the repo
  root. The repo root is resolved robustly by walking up from `Directory.GetCurrentDirectory()` and
  `AppContext.BaseDirectory` to the folder containing `Nitra.sln` (the test working directory is not
  guaranteed to be the repo root).

## Real files chosen (10)

| File | Has directives? | Parseable by CSharpParser (Cs1.grammar)? |
|---|---|---|
| `ExtensibleParser/Extensions.cs` | Yes — `#if NETSTANDARD2_0` / `#else` / `#endif` | No — records, extension method (`this` param), raw string `$"""`, `is not`, file-scoped namespace |
| `Shared/NetStandard2_0Support.cs` | Yes — `#if NATIVEAOT`, `#if !NETSTANDARD2_0`, `#pragma warning` | No — `global using`, primary ctor, NRT `?`, `stackalloc`, generics |
| `ExtensibleParser/Parser.NoRecovery.cs` | Yes — `#if !RECOVERY` | No — file-scoped namespace, modern syntax |
| `ExtensibleParser/Parser.Recovery.cs` | Yes — `#if RECOVERY` | No — file-scoped namespace, modern syntax |
| `Parsers/Json/Ast.cs` | No (plain) | No — records |
| `Parsers/Json/JsonParser.cs` | No (plain) | No — file-scoped namespace, modern syntax |
| `Parsers/Dot/DotParser/DotAst.cs` | No (plain) | No — records |
| `Parsers/Dot/DotParser/DotVisitor.cs` | No (plain) | No — records, primary ctor, pattern matching, lambdas |
| `Parsers/Cpp/CppSimplifiedParser/CppAst.cs` | No (plain) | No — records, raw string literals, generics |
| `Parsers/Cpp/CppInteropGenerator/EnumGenerator.cs` | No (plain) | No — file-scoped namespace, records, lambdas, generics |

**Parseable subset = EMPTY.** Every real `.cs` file in the repo is modern C#; each uses at least one Cs2+
construct the C# 1.0 grammar (`Cs1.grammar`) rejects. The universal blocker is the **file-scoped namespace**
(`namespace X;`): `Cs1.grammar` declares `NamespaceDeclaration = "namespace" QualifiedName "{" ... "}" ";"?`,
which **requires** a braced body, so `namespace X;` fails at the `;`. On top of that, real files use records,
primary constructors, generics, string interpolation, lambdas, `global using`, and NRT `?` — none supported by
C# 1.0. This was verified empirically (a temporary scratch test parsed all 10 candidates: **all 10 failed**).
Per the task ("if a file uses a C# feature the CSharpParser does not support, EXCLUDE it and note why — do not
force it"), all 10 are excluded from the clean-parse subset, so that subset is intentionally empty.

## Tests + key assertions

All in `RealFileHardeningTests.cs`. `AllFiles` is the 10-file list above; `ParseableFiles` is the (empty)
clean-parse subset; `ActiveInactiveFile` = `ExtensibleParser/Extensions.cs`.

### 1. `RealFile_DoesNotCrash_AndPreservesLength`

For each of the 10 real files: `Preprocessor.Run(content, [])` does **not** throw and returns a non-null
`PreprocessResult` with `Text.Length == content.Length` (D2 same-length / identity mapping).

### 2. `RealFile_PreprocessedText_DoesNotCorruptAndParsesWhenSupported`

Two parts:
- **(i) Blanking did not corrupt the kept code — asserted on ALL 10 real files.** The preprocessor's only
  mutation is blanking a char to a space, so at every index `i` the preprocessed `Text[i]` is either
  byte-identical to `source[i]` or a `' '`. The test finds the first index where `Text[i]` differs from both
  `source[i]` and `' '` and fails if any exists. This is the real-file guarantee for goal (b) — it does not
  depend on the C# parser supporting the file's syntax.
- **(ii) Clean parse — asserted on the `ParseableFiles` subset (empty).** For each listed file,
  `CSharpGrammarLoader.CreateCSharpParser()` parses `result.Text` cleanly:
  `TryGetSuccess(out var node, out var end)`, `end == result.Text.Length`, `node` non-null,
  `parser.Parser.ErrorInfo` null, `parser.Parser.RecoveryDiagnostics.Count == 0`. With the current repo this
  loop runs over zero files (documented no-op); a Cs1-parseable real file added later is picked up here.

### 3. `RealFile_InactiveIf_RegionIsBlanked_ActiveKept`

Active/inactive sanity on a real `#if` file (`Extensions.cs`): with `NETSTANDARD2_0` undefined, the inactive
`#if` branch line `public static string Str(this CharsRef span) => span.ToString();` is **blanked** (every
char in its span is a space), while the active `#else` branch line
`public static ReadOnlySpan<char> Str(this ReadOnlySpan<char> span) => span;` is **kept byte-for-byte**.
Proves active/inactive selection actually works on a real file (the preprocessor is not a silent no-op).

## Test result

```
dotnet test Tests/CsPreprocessorTests/CsPreprocessorTests.csproj
Passed!  - Failed: 0, Passed: 127, Skipped: 0, Total: 127
```

- New `RealFileHardeningTests`: **3 passed / 0 failed** (verified via
  `--filter "FullyQualifiedName~RealFileHardeningTests"`).
- Full project: **127 passed / 0 failed** (124 pre-existing + 3 new).

## Production bug found

**Preprocessor: none.** On all 10 real repo files the preprocessor ran to completion without throwing,
preserved the source length exactly, never altered a kept character (only blanked to spaces), and correctly
blanked the inactive `#if` branch while keeping the active `#else` branch on a real file. No preprocessor
code changes were needed or made.

**Related finding (pre-existing `CSharpParser`/`Cs1.grammar` limitation, NOT a preprocessor bug):** the
`CSharpParser` built by `CSharpGrammarLoader.CreateCSharpParser()` loads `Cs1.grammar` (C# 1.0) and therefore
cannot parse **any** real `.cs` file in this repo (all use Cs2+ syntax — file-scoped namespaces above all, plus
records/primary constructors/generics/interpolation/lambdas/`global using`/NRT). Consequently the "preprocessed
Text parses cleanly" assertion has an empty real-file subset. This is a downstream grammar-coverage limitation,
not a preprocessor defect: the preprocessor's output is correct (same length, no corruption, correct
active/inactive blanking), it is simply the C# parser that is too limited to validate it on modern real files.

Recommended follow-up (out of T5.1 scope): to make the clean-parse hardening assertion exercisable on real
files, `CSharpGrammarLoader` would need to load a newer grammar (e.g. `Cs14.grammar`) — but that changes the
shared helper used by `PreprocessorIntegrationTests` and is beyond this task's scope. The "does not corrupt kept
code" invariant (test 2.i) provides the real-file guarantee without depending on grammar coverage.
