# Progress — point 9.1 (Wave 9 V1): manual C#-corpus scale probe test

Task: new file `Tests/ParserTests/Recovery/CSharpCorpusProbeTests.cs` (manual `[Ignore]`d probe) + one
`ProjectReference` line in `Tests/ParserTests/ParserTests.csproj`.

## Plan

1. Progress file created (this file).
2. Recon: neighbor conventions in `Tests/ParserTests/Recovery/`, `EmbeddedGrammar.cs` pattern,
   `CSharpParser` ctor signature, `CSharpTerminals.Trivia()/GetAll()`, csproj ItemGroup.
3. Write `CSharpCorpusProbeTests.cs`.
4. Add single ProjectReference line to `ParserTests.csproj`.
5. Verify: `dotnet build` (repo root) → `dotnet test Tests/ParserTests` (full suite, probe must not run)
   → one-shot probe run with `[Ignore]` removed (filter `CSharpCorpusProbeTests`, timeout ≥ 1500 s)
   → restore `[Ignore]` exactly as specified.

## Decisions

- No header comment in the test file: the overwhelming majority of neighboring files in
  `Tests/ParserTests/Recovery/` start with `#nullable enable` and have no header comment
  (only `RecoveryCorpusTests.cs` has one). Constraint allows a header "if it matches the neighboring
  convention" — it doesn't, so omitted.
- Grammar loading: replicate `EmbeddedGrammar.cs` logic locally (private static method,
  `GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal))` + `StreamReader`)
  for Cs1.grammar … Cs14.grammar in ascending version order, producing
  `IReadOnlyList<(string Text, string Path)>` — per the task spec (the existing `EmbeddedGrammar`
  helper lives in a different test project and is not referenced from `Tests/ParserTests`).
- Roslyn root resolution: `CSNITRA_ROSLYN_ROOT` env var first; otherwise walk up from
  `AppContext.BaseDirectory` to the first ancestor containing `Directory.Build.props` and take its
  sibling `roslyn` directory. If neither exists: Trace "Roslyn root not found, skipping V1" and return.
  Never throw.
- Fresh `CSharpParser` per file (mandatory): grammar build happens inside the ctor per file.

## Log (UTC)

- 2026-09-21 22:21:29 — progress file created; recon started.
- 2026-09-21 22:21 — recon done:
  - `Directory.Build.props` present at repo root (walk-up anchor confirmed).
  - `C:\RSDN\roslyn` exists (sibling of repo root — walk-up fallback will find it).
  - `CSharpGrammar` project is in `Nitra.sln` (line 66).
  - `CSharpParser` ctor at `Parsers/CSharp/CSharpGrammar/CSharpParser.cs:18` matches the spec
    (`IReadOnlyList<(string Text, string Path)>`, `Terminal`, `IEnumerable<Terminal>`).
  - `CSharpTerminals.Trivia()` (CSharpTerminals.cs:127) and `CSharpTerminals.GetAll()`
    (CSharpTerminals.cs:129, returns `IReadOnlyList<Terminal>`) confirmed.
  - csproj ItemGroup with the 7 project references located at lines 23-31.

- 2026-09-21 22:23 — implementation done:
  - `Tests/ParserTests/Recovery/CSharpCorpusProbeTests.cs` created (123 lines, CRLF; write tool initially
    produced LF — converted to CRLF in-place, no content change).
  - `Tests/ParserTests/ParserTests.csproj`: single line added to the project-reference ItemGroup:
    `<ProjectReference Include="..\..\Parsers\CSharp\CSharpGrammar\CSharpGrammar.csproj" />` (first line of the group).
  - Note: `docs/RecoveryImprovementPlan-checklist.md` shows a pre-existing modification (9.1 checkbox
    `[ ]` → `[~]`) that predates this session; left untouched.

- 2026-09-21 22:24 — build attempt 1 failed (own bug, 2 rounds): `const string[] corpus = [...]` →
  CS0133 (collection expression is not a constant); `var corpus = [...]` → CS9176 (no target type).
  Final: `string[] corpus = [ ... ]`. Not an external/F6 incident.
- 2026-09-21 22:25 — build attempt 2: **succeeded** (repo-root `Nitra.sln`, Debug/x64, `--no-incremental`).

## Incidents

(none — no F6/NETSDK1022, no external csproj injection observed)

## Verification

1. `dotnet build` (repo root, Nitra.sln): **PASS** (2026-09-21 22:25 UTC).
2. Full suite:
   - 2026-09-21 22:25 UTC — first run via `roslyn_run_dotnet_test` (noBuild/noRestore):
     `Total: 449 · Passed: 436 · Failed: 11 · Skipped: 2`. Failure details not persisted by the tool
     (no console-output.md for that run found under %TEMP%\RoslynMcpServer; the only same-minute logs
     are the two build runs). Suspect: MCP-tool process environment (it strips inherited env vars) and/or
     documented parallel-load flakiness (RecoveryPerfTests et al. are known flaky under parallel load,
     per RecoveryCorpusTests header). Not reproducible in the raw run below.
   - 2026-09-21 22:25 UTC — raw `dotnet test Tests\ParserTests\ParserTests.csproj --no-build --no-restore`
     from repo root (the exact specified command): **PASS** — `Failed: 0, Passed: 447, Skipped: 3, Total: 450`.
     `Test_CSharp_Corpus_Probe` correctly listed as Skipped (Ignore) and did not run.
   - Green result is final per the one-shot rule.
3. One-shot probe run:
   - 2026-09-21 22:27 UTC — removed ONLY the `[Ignore(...)]` attribute; `dotnet test Tests\ParserTests
     --filter "FullyQualifiedName~CSharpCorpusProbeTests"` (timeout cap 2400 s): **PASS**
     (`Failed: 0, Passed: 1, Total: 1, Duration: 2 s`; wall incl. restore+build = 5.4 s).
     Trace lines NOT surfaced in the VSTest console (expected).
   - 2026-09-21 22:28 UTC — `[Ignore(...)]` restored exactly as specified.
   - The ~2 s duration looked too fast to have parsed 10 files, so I observed the method directly via a
     throwaway pwsh 7.6.6 harness in %TEMP%\opencode (loads built ParserTests.dll, adds a
     ConsoleTraceListener, sets CSNITRA_ROSLYN_ROOT, calls the public test method — NO repo changes,
     NO extra logging added to the test). Result: the probe DID run to completion, wallMs=1975, matching
     the VSTest "Duration: 2 s" (the engine is simply fast). Per-file raw numbers (Trace, harness):
     - NullableReferenceTypesTests.cs        sizeBytes=6748452 elapsedMs=180 SuccessAtEof=True RecoveryPasses=3 EngineGenerateCalls=3 Memo=3 memBefore=17932360  memAfter=34784336  delta=16851976
     - DefaultInterfaceImplementationTests.cs sizeBytes=2777927 elapsedMs=95  SuccessAtEof=True RecoveryPasses=3 EngineGenerateCalls=3 Memo=3 memBefore=41045136  memAfter=36009080  delta=-5036056
     - CollectionExpressionTests.cs           sizeBytes=2365618 elapsedMs=63  SuccessAtEof=True RecoveryPasses=3 EngineGenerateCalls=3 Memo=3 memBefore=40668104  memAfter=35184464  delta=-5483640
     - ExtensionTests.cs                      sizeBytes=1966280 elapsedMs=52  SuccessAtEof=True RecoveryPasses=3 EngineGenerateCalls=3 Memo=3 memBefore=39044784  memAfter=38580304  delta=-464480
     - Syntax.xml.Internal.Generated.cs       sizeBytes=1910368 elapsedMs=52  SuccessAtEof=True RecoveryPasses=3 EngineGenerateCalls=3 Memo=3 memBefore=42328920  memAfter=38468568  delta=-3860352
     - UnionsTests.cs                         sizeBytes=1826853 elapsedMs=49  SuccessAtEof=True RecoveryPasses=3 EngineGenerateCalls=3 Memo=3 memBefore=42049952  memAfter=38301432  delta=-3748520
     - StaticAbstractMembersInInterfacesTests sizeBytes=1615314 elapsedMs=45  SuccessAtEof=True RecoveryPasses=3 EngineGenerateCalls=3 Memo=3 memBefore=41459848  memAfter=37878360  delta=-3581488
     - OutVarTests.cs                         sizeBytes=1565840 elapsedMs=45  SuccessAtEof=True RecoveryPasses=3 EngineGenerateCalls=3 Memo=3 memBefore=40937888  memAfter=37779464  delta=-3158416
     - RefFieldTests.cs                       sizeBytes=1550871 elapsedMs=45  SuccessAtEof=True RecoveryPasses=3 EngineGenerateCalls=3 Memo=3 memBefore=40808888  memAfter=37749424  delta=-3059464
     - ExtensionIndexersTests.cs              sizeBytes=1336887 elapsedMs=40  SuccessAtEof=True RecoveryPasses=3 EngineGenerateCalls=3 Memo=3 memBefore=40350992  memAfter=37321472  delta=-3029520
     - summary: processed=10 skipped=0 timedOut=0
   - Raw numbers only — no optimization performed or implied (per constraints). Negative deltas are an
     artifact of `GC.GetTotalMemory(true)` forcing a full GC between the two samples.
    - 2026-09-21 22:32 UTC — post-restore `dotnet build` (repo root, Nitra.sln): **succeeded** (final
      source state with `[Ignore]` restored compiles clean).

## Status

**DONE** (2026-09-21 22:32 UTC). Deliverables in place: new probe test (Ignore restored) + single csproj
line. No production code touched, nothing committed. Throwaway harness:
`%TEMP%\opencode\probe-harness.ps1` / `probe-harness.log` (outside repo).
