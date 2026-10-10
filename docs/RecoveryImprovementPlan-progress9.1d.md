# Progress — sub-point 9.1d (Wave 9, V1): clean up debug leftovers + make the C#-corpus probe report honest metrics

Task: (A) restore `ExtensibleParser/Parser.cs` to HEAD (remove the leftover
`SetDebugInput` method from a user debug session); (B) clean up
`Tests/ParserTests/Recovery/CSharpCorpusProbeTests.cs` (re-activate `[Ignore]`,
remove the `Parser.SetDebugInput(pp.Text);` call + its blank line, keep
`using ExtensibleParser;`); (C) make the per-file probe Trace line report honest
metrics (`unrecovered`, `skipped`, `absorberBytes`, `absorberFraction`, `depth`)
without dropping any existing field. Only these two source files + this progress
file may change. NO commit.

**STATUS: DONE (2026-09-22 17:44 UTC).** Parser.cs restored to HEAD (diff empty,
absent from `git status`). Test file cleaned up + honest metrics added (a depth
formula bug found & fixed during verification, see Incidents). Build 0 warn/0
err; filtered test SKIPPED (Ignore active), 0 failed; one-shot run captured
per-file metrics for all 10 corpus files. Final `[Ignore]` restored byte-
identical; no BOM, CRLF intact.

## Plan

1. Create this progress file.
2. Recon: read the current `CSharpCorpusProbeTests.cs` (working tree), the node
   types (`TerminalNode`/`SeqNode`/`ListNode`/`SomeNode`), and the walk shape
   (`CostCalculator.CountRecoveryNodes`).
3. (A) `git checkout HEAD -- ExtensibleParser/Parser.cs`; verify `git diff` empty +
   absent from `git status --short`.
4. (B) Edit `CSharpCorpusProbeTests.cs`: uncomment `[Ignore]`; remove
   `Parser.SetDebugInput(pp.Text);` + trailing blank line; keep `using
   ExtensibleParser;`.
5. (C) Edit `CSharpCorpusProbeTests.cs`: add the five honest metrics to the
   per-file Trace line (keep every existing field); implement the two tree walks
   (`AbsorberBytes`, `TreeDepth`) as private static helpers.
6. Build `Tests\ParserTests --no-incremental` (plain config) → 0 errors.
7. Filtered test run → SKIPPED (Ignore active), 0 failed.
8. One-shot metrics run: remove ONLY the `[Ignore]` line → build → harness run →
   capture per-file Trace lines → restore the `[Ignore]` line byte-identical →
   rebuild.
9. VERIFY the new metrics against a throwaway tree-dump harness (caught a
   `TreeDepth` formula bug; fixed, then re-ran the one-shot run).
10. Record actual per-file numbers.
11. Final state check + finalize this file.

## Decisions

- Tree walks implemented as private static helper methods (`AbsorberBytes`,
  `TreeDepth`), mirroring `CostCalculator.CountRecoveryNodes`' walk shape
  (recursing over `SeqNode.RawElements`, `ListNode.RawElements`+`Delimiters`,
  `SomeNode.Value`). `absorberBytes`/`depth` return 0 when `success == false`
  (guarded at the call site with `success ? … : 0`).
- `TreeDepth` uses `1 + maxChild` (maxChild starts at 0) so a leaf = 1 and a
  node = 1 + max(child depths). (The first draft used `max(1, maxChild)` which
  is wrong — see Incidents.)
- The `skipped` metric local is named `skippedDiag` to avoid colliding with the
  pre-existing `var skipped = 0;` missing-file counter (which stays untouched,
  as does the summary line). The per-file report field stays `skipped=`.
- `absorberFraction` = `absorberBytes / (double)pp.Text.Length` formatted `:P0`.
- The one-shot run used the existing `%TEMP%\opencode\probe-harness.ps1` (loads
  plain-config `Tests\ParserTests\bin\Debug\net8.0\ParserTests.dll`, attaches a
  `ConsoleTraceListener`, sets `CSNITRA_ROSLYN_ROOT=C:\RSDN\roslyn`, calls
  `Test_CSharp_Corpus_Probe()` directly — runs regardless of the `[Ignore]`
  attribute).
- A throwaway C# tree-dump harness (`%TEMP%\opencode\probe91ddiag`, outside the
  repo, referencing the built netstandard2.0 DLLs by HintPath) was used ONLY to
  verify the new metrics; it is not part of the deliverable.

## Log (UTC)

- 2026-09-22 17:05 — progress file created; recon done (read test file, node
  types, CostCalculator walk, Result.TryGetSuccess, CSharpParser ctor/Parse).
- 2026-09-22 17:07 — (A) `git checkout HEAD -- ExtensibleParser/Parser.cs`;
  verified `git diff` empty + absent from `git status --short`.
- 2026-09-22 17:09 — (B) uncommented `[Ignore]`; removed `Parser.SetDebugInput`
  + blank line; kept `using ExtensibleParser;`. Verified no BOM (first4 = `23
  6e 75 6c`), CRLF (133/133).
- 2026-09-22 17:12 — (C) added the five metrics + `AbsorberBytes`/`TreeDepth`
  helpers. File 185 lines, no BOM, CRLF (185/185).
- 2026-09-22 17:14 — build `Tests\ParserTests --no-incremental` (plain config) →
  0 warn/0 err. Filtered run → SKIPPED, 0 failed.
- 2026-09-22 17:16 — recorded pre-run file hash; removed ONLY the `[Ignore]`
  line; rebuilt (0/0). One-shot harness run → all 10 files, but **depth=1 for
  every file** (suspicious).
- 2026-09-22 17:22 — built throwaway tree-dump harness; `ExtensionIndexersTests.
  cs` tree is clearly deep (CompilationUnit → OpenUsingDirective → QualifiedName
  → NamespaceSegments → NamespaceSegment → Dot = 6 levels) yet `Depth()`
  returned 1. Instrumented `Depth`: every node returned d=1. ROOT CAUSE: the
  formula was `max(1, maxChild)` (the node's own +1 level never added) instead
  of `1 + maxChild`.
- 2026-09-22 17:27 — fixed `TreeDepth` to `1 + maxChild` (harness confirmed
  maxDepth=6 with correct child depths: OpenUsingDirective=5, leaf Skipped=1);
  applied the SAME fix to the deliverable test file. Rebuilt (0/0).
- 2026-09-22 17:30 — re-ran the one-shot harness with the fixed code → corrected
  per-file metrics captured (depth now 6 for 9 files, 23 for the generated
  file). Restored the `[Ignore]` line byte-identical; rebuilt (0/0).
- 2026-09-22 17:44 — final checks: filtered run SKIPPED 0 failed; `git diff --
  Parser.cs` empty; `git status` shows only the intended files; finalized this
  file.

## Incidents

- **2026-09-22 17:22 UTC — `TreeDepth` off-by-one formula bug (self-caught, NOT
  NETSDK1022 / BOM / external-csproj).** The first `TreeDepth` draft computed
  `depth = max(1, max(childDepths))`, which equals `max(childDepths)` (child
  depths are ≥ 1) — the node's own level (+1) was never added, so EVERY tree
  reported `depth=1` regardless of actual nesting. Caught by a throwaway tree-
  dump harness (real tree depth was 6+). Fixed to `1 + maxChild` (maxChild
  starts at 0). The one-shot run was re-executed after the fix, so the reported
  metrics use the correct formula.
- No BOM injection: the test file had no BOM at the start (first4 = `23 6e 75
  6c`) and has none in the final state. CRLF preserved throughout (CR == LF at
  every check).
- No NETSDK1022 / SDK-mismatch and no external-process csproj (F6-style)
  injection observed. `ParserTests.csproj` and the other pre-existing
  `M`/`MM`/`A`/`??` entries in `git status` are untouched by this session
  (they were present at session start).

## Verification

### Build + filtered test
- `dotnet build Tests\ParserTests --no-incremental` (plain config): **0
  Warning(s), 0 Error(s)** (final, with `[Ignore]` restored).
- `dotnet test Tests\ParserTests --no-build --filter
  "FullyQualifiedName~CSharpCorpusProbeTests"`: **Skipped Test_CSharp_Corpus_
  Probe — Failed: 0, Passed: 0, Skipped: 1, Total: 1** (Ignore active).

### One-shot metrics run (verbatim per-file Trace lines)
Captured via `%TEMP%\opencode\probe-harness.ps1` (fixed `TreeDepth`), log at
`%TEMP%\opencode\probe91d2.log`. The leading `1` line is harness noise (the
return value of `Trace.Listeners.Add`), not a test line.

```
1
NullableReferenceTypesTests.cs sizeBytes=6748452 elapsedMs=196 SuccessAtEof=True RecoveryPasses=3 EngineGenerateCalls=3 Memo=133 RecoveryNodes=2 unrecovered=2 skipped=3 absorberBytes=6747683 absorberFraction=100 % depth=6 memBefore=57776320 memAfter=74999104 delta=17222784
DefaultInterfaceImplementationTests.cs sizeBytes=2777927 elapsedMs=63 SuccessAtEof=True RecoveryPasses=2 EngineGenerateCalls=2 Memo=109 RecoveryNodes=2 unrecovered=2 skipped=2 absorberBytes=2777314 absorberFraction=100 % depth=6 memBefore=77723424 memAfter=86535624 delta=8812200
CollectionExpressionTests.cs sizeBytes=2365618 elapsedMs=56 SuccessAtEof=True RecoveryPasses=2 EngineGenerateCalls=2 Memo=137 RecoveryNodes=2 unrecovered=2 skipped=2 absorberBytes=2364840 absorberFraction=100 % depth=6 memBefore=76081544 memAfter=76521008 delta=439464
ExtensionTests.cs sizeBytes=1966280 elapsedMs=48 SuccessAtEof=True RecoveryPasses=2 EngineGenerateCalls=2 Memo=165 RecoveryNodes=2 unrecovered=2 skipped=2 absorberBytes=1965353 absorberFraction=100 % depth=6 memBefore=77460784 memAfter=82108112 delta=4647328
Syntax.xml.Internal.Generated.cs sizeBytes=1910368 elapsedMs=122 SuccessAtEof=True RecoveryPasses=2 EngineGenerateCalls=2 Memo=1730 RecoveryNodes=2 unrecovered=2 skipped=2 absorberBytes=1906236 absorberFraction=100 % depth=23 memBefore=80572864 memAfter=84423952 delta=3851088
UnionsTests.cs sizeBytes=1826853 elapsedMs=732 SuccessAtEof=True RecoveryPasses=2 EngineGenerateCalls=2 Memo=14650 RecoveryNodes=2 unrecovered=2 skipped=2 absorberBytes=1826216 absorberFraction=100 % depth=6 memBefore=83508536 memAfter=118594472 delta=35085936
StaticAbstractMembersInInterfacesTests.cs sizeBytes=1615314 elapsedMs=1156 SuccessAtEof=True RecoveryPasses=2 EngineGenerateCalls=2 Memo=24194 RecoveryNodes=2 unrecovered=2 skipped=2 absorberBytes=1614505 absorberFraction=100 % depth=6 memBefore=113559112 memAfter=182383592 delta=68824480
OutVarTests.cs sizeBytes=1565840 elapsedMs=41 SuccessAtEof=True RecoveryPasses=2 EngineGenerateCalls=2 Memo=127 RecoveryNodes=2 unrecovered=2 skipped=2 absorberBytes=1565119 absorberFraction=100 % depth=6 memBefore=147152184 memAfter=147589608 delta=437424
RefFieldTests.cs sizeBytes=1550871 elapsedMs=37 SuccessAtEof=True RecoveryPasses=2 EngineGenerateCalls=2 Memo=109 RecoveryNodes=2 unrecovered=2 skipped=2 absorberBytes=1550238 absorberFraction=100 % depth=6 memBefore=78549064 memAfter=78978472 delta=429408
ExtensionIndexersTests.cs sizeBytes=1336887 elapsedMs=32 SuccessAtEof=True RecoveryPasses=2 EngineGenerateCalls=2 Memo=103 RecoveryNodes=2 unrecovered=2 skipped=2 absorberBytes=1336236 absorberFraction=100 % depth=6 memBefore=77449432 memAfter=77716760 delta=267328
summary: processed=10 skipped=0 timedOut=0
```

### Metrics read (actual)
- `NullableReferenceTypesTests.cs` (the 9.1c anchor file): `unrecovered=2
  skipped=3 absorberBytes=6747683 absorberFraction=100% depth=6`.
  - `unrecovered=2` and `skipped=3` match the 9.1c diagnostic list (2 Unrecovered
    + 3 Skipped). `absorberBytes=6747683` EXACTLY equals the known 9.1c absorber
    total `([766..988)=222 + [988..6748449)=6747461`, confirming the `Absorber-
    Bytes` walk.
  - **Divergence from the recorded expectation (`absorberFraction≈99%`,
    `depth≈2`):** actual `absorberFraction` = 6747683 / 6748449 = 0.99989, which
    `:P0` rounds to **100%** (not ≈99%); actual `depth` = **6** (the using-
    directive chain is 5 deep, +1 for the CompilationUnit root), not ≈2. Both
    reported as observed (not forced).
- Every one of the 10 files reports `SuccessAtEof=True`, `RecoveryNodes=2`,
  `unrecovered=2`, `skipped=2` (except the anchor file's `skipped=3`), and
  `absorberFraction=100%`. **No corpus file is "coped with" by the grammar** —
  each still absorbs ~99.99% of its text into a top-level absorber (consistent
  with the 9.1c defect list: the probe now honestly shows the false-green is
  real). `depth` is 6 for 9 files and 23 for `Syntax.xml.Internal.Generated.cs`
  (the large generated file has a deeper real structure before it absorbs).

### Final state
- `git status --short` (this session's files): `AM
  Tests/ParserTests/Recovery/CSharpCorpusProbeTests.cs` and `??
  docs/RecoveryImprovementPlan-progress9.1d.md`. `ExtensibleParser/Parser.cs` is
  **absent** (restored to HEAD). All other entries are pre-existing (present at
  session start) and untouched.
- `git diff -- ExtensibleParser/Parser.cs`: **empty**.
- `git diff -- Tests/ParserTests/Recovery/CSharpCorpusProbeTests.cs`: the final
  diff (see FINAL MESSAGE). No BOM (first4 = `23 6e 75 6c`), CRLF (185/185),
  `[Ignore]` active, `SetDebugInput` absent, `1 + maxChild` present.

## Files created / modified

- **Created (repo):** `docs/RecoveryImprovementPlan-progress9.1d.md` (this file).
- **Restored to HEAD:** `ExtensibleParser/Parser.cs` (leftover `SetDebugInput`
  removed via `git checkout HEAD --`).
- **Modified (repo):** `Tests/ParserTests/Recovery/CSharpCorpusProbeTests.cs`
  (cleanup + honest metrics + `AbsorberBytes`/`TreeDepth` helpers).
- **Throwaway (outside repo, `%TEMP%\opencode\probe91ddiag`):** `probe91ddiag.
  csproj`, `Program.cs` (tree-dump harness, used only to verify the metrics).
- One-shot logs (outside repo): `%TEMP%\opencode\probe91d.log` (pre-fix, depth
  bug), `%TEMP%\opencode\probe91d2.log` (final, corrected).
- **No** other production changes; **no** commit.
