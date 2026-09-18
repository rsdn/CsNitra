# T0.2 progress — Tests/CsPreprocessorTests (smoke)

## Done
- [x] Read `docs/CsPreprocessor.md` (plan), `AGENTS.md` (conventions), model project `Tests/CSharpGrammarTests/CSharpGrammarTests.csproj`.
- [x] Created `Tests/CsPreprocessorTests/CsPreprocessorTests.csproj` — modeled exactly on `CSharpGrammarTests.csproj` (Sdk `MSTest.Sdk/3.6.4`, net8.0, LangVersion preview, ImplicitUsings enable, Nullable enable, UseVSTest true, empty RootNamespace). References: `CsPreprocessor`, `CSharpGrammar` (for later T3.2), `ExtensibleParser`, `Regex` (Analyzer), `TerminalGenerator` (Analyzer, ReferenceOutputAssembly=false).
- [x] Created `Tests/CsPreprocessorTests/MSTestSettings.cs` — `[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]`, exact copy of the pattern used by `Tests/CSharpGrammarTests/MSTestSettings.cs` (single line, no explicit using — compiles fine, same as the model project).
- [x] Created `Tests/CsPreprocessorTests/PreprocessorSmokeTests.cs` — `[TestClass]`, one smoke test: `Preprocessor.Run("int x = 1;", Array.Empty<string>())` → asserts `Text == "int x = 1;"`, `Diagnostics` empty, `LineDirectives` empty. Documents stub behavior.
- [x] `dotnet sln Nitra.sln add Tests/CsPreprocessorTests/CsPreprocessorTests.csproj` — confirmed: "Project added to the solution"; `dotnet sln list` shows both `Parsers\CSharp\CsPreprocessor\CsPreprocessor.csproj` and `Tests\CsPreprocessorTests\CsPreprocessorTests.csproj`.
- [x] `dotnet test Tests/CsPreprocessorTests/CsPreprocessorTests.csproj` (workdir repo root) — build OK, **Passed: 1, Failed: 0, Skipped: 0**.

## Decisions
- Followed the model project's `MSTestSettings.cs` pattern verbatim (no explicit `using Microsoft.VisualStudio.TestPlatform;` — the model project builds green with just the attribute line, and so did the new project).
- Test namespace = `CsPreprocessorTests` (file-scoped), matching the `<ProjectName>` convention of the model test project.
- Smoke test uses `Array.Empty<string>()` for `commandLineSymbols` per task spec.

## Deviations
- None.

## Final state
- Files created:
  - `Tests/CsPreprocessorTests/CsPreprocessorTests.csproj`
  - `Tests/CsPreprocessorTests/MSTestSettings.cs`
  - `Tests/CsPreprocessorTests/PreprocessorSmokeTests.cs`
  - `docs/CsPreprocessor-progressT0.2.md` (this file)
- File modified: `Nitra.sln` (new project entry via `dotnet sln add`).
- Test result: 1 passed / 0 failed.
