# Progress — sub-point 9.1b (Wave 9, V1): C#-corpus probe through the preprocessor + recovery-node count

Task: rewrite `Tests/ParserTests/Recovery/CSharpCorpusProbeTests.cs` so each corpus file's content is run
through the C# preprocessor (`CsPreprocessor.Preprocessor.Run`) before parsing, and the report gains a
recovery-node count (`CostCalculator.CountRecoveryNodes`). Exactly one line added to
`Tests/ParserTests/ParserTests.csproj` (`CsPreprocessor.csproj` ProjectReference). No production code, no
other tests, no commit.

Background: 9.1's one-shot run produced an identical `RecoveryPasses=3 EngineGenerateCalls=3 Memo=3` on all
10 files (A8-absorber signature) — raw input carries `#if`/`#else`/`#endif` directives the grammar cannot
handle, so the recovery floor swallowed each whole file. 9.1b fixes the probe, not the engine.

## Plan

1. Progress file created (this file).
2. Recon: `git status` (note pre-existing staged changes from 9.1/9.2/A8 — leave untouched), BOM/CRLF state
   of the two target files, harness presence at `%TEMP%\opencode\probe-harness.ps1`, `Diagnostic`/
   `PreprocessResult` record shapes.
3. `ParserTests.csproj`: add exactly one ProjectReference line (`CsPreprocessor.csproj`) inside the
   existing ItemGroup.
4. `CSharpCorpusProbeTests.cs`:
   - usings: add `using CsPreprocessor;` and `using ExtensibleParser.Recovery;` (keep existing).
   - before the corpus loop: `string[] symbols = ["NET", "NETCOREAPP", "DEBUG", "TRACE", "NET8_0_OR_GREATER"];`
   - in the loop after `File.ReadAllText`: `var pp = Preprocessor.Run(input, symbols);`
   - parse `pp.Text` instead of `input`.
   - capture root once via `TryGetSuccess(out var root, out var end)`; `successAtEof` vs `pp.Text.Length`.
   - `var recoveryNodes = success ? CostCalculator.CountRecoveryNodes(root) : 0;`
   - report: all existing fields kept in position/format; ADD `RecoveryNodes={n}` and `ppDiag={n}` only when
     `pp.Diagnostics.Count > 0` (precomputed ternary fragment).
   - CRLF, no BOM, no header comment; everything else behavior-identical.
5. Verify: `dotnet build` (repo root, Nitra.sln) → `dotnet test Tests\ParserTests --no-build` (probe must
   show as skipped) → one-shot run: strip ONLY the `[Ignore]` line, rebuild, `dotnet test` with filter
   (timeout ≥ 2400 s) + harness Trace capture, restore `[Ignore]` byte-identical, rebuild, confirm file
   matches pre-run state.
6. Acceptance: per file expect `Memo` in the thousands, `RecoveryNodes` in dozens/hundreds, `elapsedMs`
   1 s – tens of s. If ANY file has `Memo <= 10` → dump that file's `pp.Diagnostics` (Message, Code,
   StartPos, EndPos) into this file and report; do NOT modify preprocessor/grammar/parser.

## Decisions

- `rootNode` instead of the spec-literal `out var root`: the method already has a method-level local
  `var root = ResolveRoslynRoot();` — a same-name local in the nested `foreach` scope is CS0136
  (compile error). Renaming the NEW local is the least-invasive fix (3 existing lines untouched).
- `rootNode!` (null-forgiving) instead of bare `rootNode`: `TryGetSuccess` annotates the out param
  `[MaybeNullWhen(false)]`; the correlation with the separate `success` bool is not tracked by flow
  analysis, so the bare form emits CS8604. The `!` is sound — the value is only read in the
  `success ?` branch.
- BOM stripped from the test file: spec mandates "no BOM"; the 9.1 file carried one (write-tool
  injection observed again in this session — the write tool re-injected EF BB BF, removed at byte
  level). csproj BOM left in place ("no other change" to csproj).
- csproj line placement: directly after the `CSharpGrammar.csproj` reference (C#-parser grouping).

## Log (UTC)

- 2026-09-21 23:09 — progress file created; recon started.
- 2026-09-21 23:10 — recon done:
  - `git status` shows pre-existing staged changes from prior sessions (9.1/9.2/A8): `ParserTests.csproj`
    (CSharpGrammar ref), `A8IterationLimitTests.cs`, `CSharpCorpusProbeTests.cs` (new, 9.1),
    `R2ExpectedStopTests.cs`, `V2IterationLimitScaleTests.cs` (new, 9.2), checklist, progress9.1/9.2.
    All left untouched.
  - Both target files currently carry a UTF-8 BOM (EF BB BF) + CRLF. Spec requires no BOM for the test
    file → strip it as part of the rewrite. csproj: spec says "no other change" → BOM stays, one line only.
  - Harness `%TEMP%\opencode\probe-harness.ps1` still exists (9.1 session), loads plain-config
    `bin\Debug\net8.0`, sets `CSNITRA_ROSLYN_ROOT=C:\RSDN\roslyn`, calls the test method, ConsoleTraceListener.
  - `Diagnostic` record confirmed: `(string Message, int StartPos, int EndPos, DiagnosticSeverity Severity, string? Code = null)`.
  - pwsh 7.6.6 available (same as 9.1 session).
- 2026-09-21 23:14 — implementation done:
  - `ParserTests.csproj`: exactly one line added (CsPreprocessor ProjectReference after CSharpGrammar
    line); diff verified = single `+` line; BOM/other bytes untouched.
  - `CSharpCorpusProbeTests.cs`: rewritten per spec (usings, `symbols`, `pp`, parse `pp.Text`, single
    `TryGetSuccess` capture, `recoveryNodes`, report gains `RecoveryNodes=` + conditional `ppDiag=`);
    BOM stripped, CRLF enforced, no header comment. Deviations: `rootNode`/`rootNode!` (see Decisions).
- 2026-09-21 23:14 UTC — `dotnet build` (repo root): 0 warnings, 0 errors.
- 2026-09-21 23:15 UTC — suite run 1: 1 flake (`ProbeDepthCeilingTests.Test_Shallow_Success_NoCut`,
  see Incidents); identical re-run: green (0/447/4/451), probe listed as Skipped.
- 2026-09-21 23:16 UTC — one-shot: snapshot → strip `[Ignore]` → rebuild → filtered `dotnet test`
  PASS (1/1, 12 s) → harness Trace capture (wallMs=11492, 10/10 processed) → restore `[Ignore]`
  byte-identical (SHA256 verified) → final rebuild clean.

## Incidents

- 2026-09-21 23:15 UTC — full-suite run 1 failed with a single flake:
  `Recovery.ProbeDepthCeilingTests.Test_Shallow_Success_NoCut` threw inside
  `System.String.Format(String format, Object arg0, Object arg1, Object arg2)` called from
  `ExtensibleParser.Parser.ParseRule` (`ExtensibleParser/Parser.cs:353`, the
  `Log($"  Prefix at {newPos} success: {node.Kind} prefixResult: {prefixResult}")` interpolation) via
  `ParseRuleOnceProbed` (`Parser.Recovery.cs:357`). Engine code, untouched by this task. One identical
  re-run (`dotnet test Tests\ParserTests --no-build`) → 0 failed (flake, consistent with the
  documented method-level parallel-load flakiness in this repo). Green result is final per the one-shot
  rule.
- Write-tool BOM re-injection (see Decisions) — handled at byte level, not a build/test incident.

## Verification

1. `dotnet build` (repo root, Nitra.sln, plain config): **PASS** — 0 warnings, 0 errors
   (2026-09-21 23:14 UTC). `CsPreprocessor.dll` + `ParserTests.dll` produced in `bin\Debug\net8.0` /
   `bin\Debug\netstandard2.0` (plain-config dirs the harness loads).
2. Full suite `dotnet test Tests\ParserTests --no-build`:
   - run 1 (2026-09-21 23:15 UTC): Failed 1 (flake, see Incidents), Passed 446, Skipped 4, Total 451.
   - re-run, identical command (2026-09-21 23:15 UTC): **PASS** — Failed 0, Passed 447, Skipped 4,
     Total 451. `Test_CSharp_Corpus_Probe` listed as Skipped (Ignore) and did not run.
3. One-shot probe run:
   - 2026-09-21 23:16 UTC — pre-run snapshot of the test file: SHA256
     `09EA9046E0351AE03FF1ECC6C57F27EEE979BFE6AC73D4A35AECAE246488542B` (backup
     `%TEMP%\opencode\probe-9.1b-with-ignore.cs.bak`).
   - 2026-09-21 23:16 UTC — stripped ONLY the `[Ignore(...)]` line; rebuilt (0 warnings, 0 errors);
     `dotnet test Tests\ParserTests --no-build --filter "FullyQualifiedName~CSharpCorpusProbeTests"`
     (timeout cap 2400 s): **PASS** — `Failed: 0, Passed: 1, Total: 1, Duration: 12 s`.
   - 2026-09-21 23:17 UTC — harness (`%TEMP%\opencode\probe-harness.ps1`, reused from 9.1, pwsh 7.6.6,
     plain-config `bin\Debug\net8.0`, `CSNITRA_ROSLYN_ROOT=C:\RSDN\roslyn`, ConsoleTraceListener)
     captured all Trace lines to `%TEMP%\opencode\probe-9.1b.log`; harness `wallMs=11492`. Note: the log's
     first line is a stray `1` — identical harness artifact present in the 9.1 log as well (not a test line).
     Per-file Trace lines (verbatim):
     - `NullableReferenceTypesTests.cs sizeBytes=6748452 elapsedMs=193 SuccessAtEof=True RecoveryPasses=3 EngineGenerateCalls=3 Memo=133 RecoveryNodes=2 memBefore=57778304 memAfter=75001088 delta=17222784`
     - `DefaultInterfaceImplementationTests.cs sizeBytes=2777927 elapsedMs=64 SuccessAtEof=True RecoveryPasses=2 EngineGenerateCalls=2 Memo=109 RecoveryNodes=2 memBefore=77710736 memAfter=86537280 delta=8826544`
     - `CollectionExpressionTests.cs sizeBytes=2365618 elapsedMs=55 SuccessAtEof=True RecoveryPasses=2 EngineGenerateCalls=2 Memo=137 RecoveryNodes=2 memBefore=76083224 memAfter=76522688 delta=439464`
     - `ExtensionTests.cs sizeBytes=1966280 elapsedMs=89 SuccessAtEof=True RecoveryPasses=2 EngineGenerateCalls=2 Memo=165 RecoveryNodes=2 memBefore=77470688 memAfter=82118016 delta=4647328`
     - `Syntax.xml.Internal.Generated.cs sizeBytes=1910368 elapsedMs=203 SuccessAtEof=True RecoveryPasses=2 EngineGenerateCalls=2 Memo=1730 RecoveryNodes=2 memBefore=80582760 memAfter=84433848 delta=3851088`
     - `UnionsTests.cs sizeBytes=1826853 elapsedMs=1216 SuccessAtEof=True RecoveryPasses=2 EngineGenerateCalls=2 Memo=14650 RecoveryNodes=2 memBefore=83510216 memAfter=118596152 delta=35085936`
     - `StaticAbstractMembersInInterfacesTests.cs sizeBytes=1615314 elapsedMs=1982 SuccessAtEof=True RecoveryPasses=2 EngineGenerateCalls=2 Memo=24194 RecoveryNodes=2 memBefore=113569016 memAfter=182385272 delta=68816256`
     - `OutVarTests.cs sizeBytes=1565840 elapsedMs=61 SuccessAtEof=True RecoveryPasses=2 EngineGenerateCalls=2 Memo=127 RecoveryNodes=2 memBefore=147162088 memAfter=147599512 delta=437424`
     - `RefFieldTests.cs sizeBytes=1550871 elapsedMs=66 SuccessAtEof=True RecoveryPasses=2 EngineGenerateCalls=2 Memo=109 RecoveryNodes=2 memBefore=78558968 memAfter=78988376 delta=429408`
     - `ExtensionIndexersTests.cs sizeBytes=1336887 elapsedMs=52 SuccessAtEof=True RecoveryPasses=2 EngineGenerateCalls=2 Memo=103 RecoveryNodes=2 memBefore=77459336 memAfter=77726664 delta=267328`
     - `summary: processed=10 skipped=0 timedOut=0`
   - 2026-09-21 23:18 UTC — `[Ignore]` restored byte-identical (SHA256 matches pre-run snapshot
     `09EA9046...`); final rebuild: 0 warnings, 0 errors. File verified to match pre-run state.

## Acceptance

- **`Memo <= 10` check: PASS for all 10 files** (min Memo = 103) → the preprocessor handled every input;
  no `pp.Diagnostics` dump required. No `ppDiag=` field appeared on any report line →
  `pp.Diagnostics.Count == 0` for all 10 files.
- Deviations from the plan's EXPECT (recorded, NOT fixed — task is probe-only, no engine/grammar/
  preprocessor changes allowed):
  - `RecoveryNodes=2` on every file (expected: dozens/hundreds). `CountRecoveryNodes` recursively counts
    `IsRecovery` nodes (inserted tokens + absorbers) — 2 per file is consistent with the absorber
    signature: the parse ends each file with a small recovery insertion.
  - `Memo`: 3 files in the thousands (1730 / 14650 / 24194), 7 files in the low hundreds (103–165)
    (expected: thousands across the board).
  - `elapsedMs`: 7 files at 52–203 ms, `UnionsTests` 1216 ms, `StaticAbstractMembersInInterfacesTests`
    1982 ms (expected: 1 s – tens of s).
  - `SuccessAtEof=True` on all 10; `RecoveryPasses`/`EngineGenerateCalls` = 2 (or 3 for the first file).
  - Comparison with 9.1: the uniform `Memo=3 / RecoveryPasses=3` A8 signature is gone — Memo is now
    input-dependent (correlates with file size), but 7 of 10 files still terminate after ~2 recovery
    passes with only 2 recovery nodes, i.e. the recovery floor still swallows most of each file. This is
    an engine/grammar finding for a later plan point, not a 9.1b defect.

## Status

**DONE** (2026-09-21 23:19 UTC). Deliverables in place: rewritten probe test (preprocessor before
parsing, `RecoveryNodes`/`ppDiag` report fields, `[Ignore]` restored byte-identical) + exactly one
csproj line + this progress file. No production code touched, nothing committed. Preprocessor
acceptance check passed (no `Memo <= 10`, zero preprocessor diagnostics); the plan's numeric EXPECT
(RecoveryNodes dozens/hundreds, most files in the 1 s – tens of s range) was NOT met — recorded as an
engine/grammar finding under Acceptance, per probe-only constraints. Throwaway artifacts (outside
repo): `%TEMP%\opencode\probe-harness.ps1` (reused), `probe-9.1b.log`, `probe-9.1b-with-ignore.cs.bak`,
`9.1b-suite-rerun.log`.
