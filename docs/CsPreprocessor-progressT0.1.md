# T0.1 Progress — CsPreprocessor project shell + types + Run stub

Status: DONE — build succeeded, 0 warnings, 0 errors

## Task
Create project `Parsers/CSharp/CsPreprocessor` (netstandard2.0), core data types
(`PreprocessResult`/`Diagnostic`/`LineDirective` records), and a stub
`Preprocessor.Run`. Add to `Nitra.sln`. Build must pass with 0 errors.
NO grammar parsing yet (that's T1.x). NO `.grammar` file, NO `<EmbeddedResource>`.

## Steps (step by step)
1. [done] Read plan `docs/CsPreprocessor.md` (D1–D5, architecture) and `AGENTS.md`.
2. [done] Read model `Parsers/CSharp/CSharpGrammar/CSharpGrammar.csproj`.
   - Note: CSharpGrammar references `..\..\CsNitra\CsNitraGrammar\CsNitraGrammar.csproj`
     (its own grammar project). For CsPreprocessor we instead reference
     `..\..\CSharp\CSharpGrammar\CSharpGrammar.csproj` per the prompt.
3. [done] Verified all ProjectReference targets exist:
   - ExtensibleParser\ExtensibleParser.csproj  (True)
   - Parsers\CSharp\CSharpGrammar\CSharpGrammar.csproj  (True)
   - Regex\Regex\Regex.csproj  (True)
   - TerminalGenerator\TerminalGenerator.csproj  (True)
   - Shared\Shared.projitems  (True)
4. [note] Shared.projitems imports `global using System; System.Collections.Generic;
   System.IO; System.Linq;` — so those namespaces are already globally available in the
   project (ImplicitUsings=disable, but global usings from Shared still apply). Added
   explicit `using`s anyway for clarity/self-containment.
5. [done] Created project dir `Parsers/CSharp/CsPreprocessor`.
6. [done] Created `CsPreprocessor.csproj` (RootNamespace=CsPreprocessor; refs
   ExtensibleParser + CSharpGrammar + Regex(Analyzer) + TerminalGenerator(Analyzer,no-ref);
   Shared.projitems import; NO EmbeddedResource, NO .grammar).
7. [done] Created data types: `PreprocessResult.cs`, `Diagnostic.cs`, `LineDirective.cs`.
8. [done] Created `Preprocessor.cs` (stub Run returning source unchanged, no diagnostics).
9. [done] `dotnet sln Nitra.sln add Parsers/CSharp/CsPreprocessor/CsPreprocessor.csproj`
   → `Project `Parsers\CSharp\CsPreprocessor\CsPreprocessor.csproj` added to the solution.`
   Verified in Nitra.sln line 70, GUID {888545A6-44F6-4B20-8A19-BACD8DACE9C5}.
10. [done] `dotnet build Parsers/CSharp/CsPreprocessor/CsPreprocessor.csproj`
    → Build succeeded, 0 Warning(s), 0 Error(s).

## Key decisions
- `Diagnostic` shape: positional record
  `Diagnostic(string Message, int StartPos, int EndPos, DiagnosticSeverity Severity, string? Code = null)`.
  Positions 0-based, EndPos exclusive (original source coordinates). `Code` optional
  diagnostic id. `DiagnosticSeverity` enum = { Error, Warning } placed in same file/namespace.
- `LineDirective` shape: positional record
  `LineDirective(int OriginalLine, int? MappedLine, string? FilePath, LineDirectiveState State)`.
  `LineDirectiveState` enum = { Remapped, Default, Hidden, RemappedSpan }.
  Not fully populated yet (full `#line` parsing is T4.3); shape only for now (D5).
- Enums live in the `CsPreprocessor` namespace (not a nested `CsPreprocessor.Diagnostics`
  namespace) to keep it flat and simple.
- All types `sealed` records, file-scoped `namespace CsPreprocessor;`, Allman braces.

## Deviations
- None. Followed the prompt exactly. (Note: enums were placed in the flat
  `CsPreprocessor` namespace rather than a nested `CsPreprocessor.Diagnostics` namespace
  shown in the prompt's example — chosen for simplicity; the prompt allowed deciding a
  clean shape.)

## Open questions
- None.

## Final state
Files created:
- `Parsers/CSharp/CsPreprocessor/CsPreprocessor.csproj`
- `Parsers/CSharp/CsPreprocessor/PreprocessResult.cs`
- `Parsers/CSharp/CsPreprocessor/Diagnostic.cs` (record + `DiagnosticSeverity` enum)
- `Parsers/CSharp/CsPreprocessor/LineDirective.cs` (record + `LineDirectiveState` enum)
- `Parsers/CSharp/CsPreprocessor/Preprocessor.cs` (stub `Run`)
- `docs/CsPreprocessor-progressT0.1.md` (this file)

Files modified:
- `Nitra.sln` (project added via `dotnet sln add`)

Build: `dotnet build Parsers/CSharp/CsPreprocessor/CsPreprocessor.csproj`
→ Build succeeded. 0 Warning(s), 0 Error(s). Output: `bin\Debug\netstandard2.0\CsPreprocessor.dll`.

Scope check: no `.grammar` file created, no `<EmbeddedResource>`, no grammar parsing —
only the project shell + data types + stub `Run`, as required for T0.1.
