# Progress — CSharpCorpusHealthProbeTests (sub-point 3)

## Intentions

1. Create the progress file (this file) before analysis.
2. Verify the surrounding context without re-investigating broadly:
   - Confirm `Tests/CSharpGrammarTests` exists and sibling files' encoding/style (UTF-8 BOM, CRLF, Allman).
   - Confirm signatures: `EmbeddedGrammar.LoadGrammarUpTo`, `CSharpParser` ctor + `Parse`, `CSharpTerminals.Trivia()/GetAll()`,
     `Parser.RecoveryPasses`, `Parser.Memo`, `Result.TryGetSuccess`, `ParseHealth.Analyze` + `ParseHealthReport`.
3. Write exactly one new file: `Tests/CSharpGrammarTests/CSharpCorpusHealthProbeTests.cs`
   - usings: `CSharpGrammar`, `ExtensibleParser`, `ExtensibleParser.Recovery`, `System.Diagnostics`
   - file-scoped namespace `CSharpGrammarTests`
   - one `[TestClass] public sealed class CSharpCorpusHealthProbeTests` with a single
     `[TestMethod] [TestCategory("Probe")] [Ignore(...)] public void Test_CSharp_Corpus_Health_Probe()`
   - body per spec: resolve Roslyn root (env var, else nearest ancestor with `Directory.Build.props` + `roslyn`),
     load Cs1..Cs14 grammars, iterate 10 corpus paths, fresh parser per file, 60s watchdog via `Task.Run`/`Wait`,
     `ParseHealth.Analyze`, counters, `Trace.WriteLine` per file + summary. No asserts.
4. Acceptance: `dotnet build Tests/CSharpGrammarTests --no-incremental` → 0 errors;
   `dotnet test Tests/CSharpGrammarTests --no-build --filter "FullyQualifiedName~CSharpCorpusHealthProbeTests"` → SKIPPED.
5. Report back: file content, build result, verbatim skip outcome. No commits, no other files touched.

## Status log

- [x] Progress file created.
- [x] API verification (all signatures matched the given context):
  - `EmbeddedGrammar.LoadGrammarUpTo(int)` — `Tests/CSharpGrammarTests/EmbeddedGrammar.cs:35`.
  - `CSharpParser(IReadOnlyList<(string, string)>, Terminal, IEnumerable<Terminal>)` — `Parsers/CSharp/CSharpGrammar/CSharpParser.cs:18`;
    `Parse(string, string, out int)` — line 31; `Parser Parser { get; }` — line 10.
  - `CSharpTerminals.Trivia()` (line 127) and `GetAll()` returning `IReadOnlyList<Terminal>` (line 129) — assignable to `IEnumerable<Terminal>`.
  - `Parser.RecoveryPasses` — `ExtensibleParser/Parser.Recovery.cs:150`; `Parser.Memo` — line 310.
  - `Result.TryGetSuccess([MaybeNullWhen(false)] out ISyntaxNode, out int)` — `ExtensibleParser/Result.cs:19`.
  - `ParseHealth.Analyze(ISyntaxNode?, int, bool)` + `ParseHealthReport` + `ParseVerdict { Clean, Recovered, FalseGreen, Failed }`
    — `ExtensibleParser/Recovery/ParseHealth.cs:9-39`.
- [x] File written: `Tests/CSharpGrammarTests/CSharpCorpusHealthProbeTests.cs` (UTF-8 BOM, CRLF, Allman, 4-space).
  - Corpus kept as verbatim strings (`@"src\..."`) so backslashes stay exactly as specified.
  - `new[] { ... }` used for the corpus array instead of a collection expression, per `.editorconfig`
    `dotnet_style_prefer_collection_expression = never`.
  - Roslyn-root fallback walks `DirectoryInfo` parents from `AppContext.BaseDirectory` until a `Directory.Build.props`
    ancestor is found (repo root here), candidate `<that>\roslyn`; missing directory → trace + return.
  - No asserts anywhere; `[Ignore]` + `[TestCategory("Probe")]`.
- [x] Build: `dotnet build Tests/CSharpGrammarTests --no-incremental` → Build succeeded, 0 Warning(s), 0 Error(s).
- [x] Test: `dotnet test ... --filter "FullyQualifiedName~CSharpCorpusHealthProbeTests"` →
  `Skipped Test_CSharp_Corpus_Health_Probe` / `Skipped! - Failed: 0, Passed: 0, Skipped: 1, Total: 1`.

## Notes / deviations

- Only the two named files were created; nothing committed, no other files touched.
- The fallback path resolves to `C:\RSDN\CsNitra\roslyn` (repo root holds `Directory.Build.props`), which does not
  exist on this machine, so without `CSNITRA_ROSLYN_ROOT` the probe traces "Roslyn root not found, skipping".
  The real sibling checkout lives at `C:\RSDN\roslyn` and is reached by setting the env var — spec-conformant.
