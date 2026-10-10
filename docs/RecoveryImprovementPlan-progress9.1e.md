# Progress — sub-point 9.1e-r (Wave 9, 9.1e): feasibility probe for the S6 null-snapshot test

Task: determine whether the specified test (`Tests/ParserTests/Recovery/S6NullSnapshotTests.cs`, input `"### garbage ### namespace Foo; class Bar {}"`) passes WITH the specified `GenerateS6` fix (add `First(startRule)` to the stop-set when `snapshot == null`) and fails the MAIN assert WITHOUT it.
STATUS: DONE (2026-09-22 22:42:42 UTC)

## Q1 — with fix, primary input

**FAIL.** The test aborts on the FIRST assert (the MAIN assert is never reached):

```
Assert.IsTrue failed. expected Success, got Failure
  at Recovery.S6NullSnapshotTests.Test_S6_NullSnapshot_FirstStartRule_ResyncsAfterGarbage() in S6NullSnapshotTests.cs:line 91
```

Real run (fresh `Parser`, default Ide profile):
- `ResultKind=Failure`, `ErrorInfo: Pos=0, Expecteds=[namespace, class]`
- `RecoveryPasses=1`, `EngineGenerateCalls=1`, `HygieneRemovals=6`
- `RecoveryDiagnostics`: `[]` (empty — no candidate was ever accepted)
- No tree (Failure) → tree dump N/A; absorber bytes/fraction N/A (0 bytes, no absorber nodes exist)

Step-by-step emulation of `Parser.Recover` (temporary probe, faithful: real `ApplyPatches`/`ParseRuleOnce`/`RecoveryEngine.Generate`, `_recoveryPoint` set via reflection to match the private field):

- MAIN parse: `Failure@-1 MaxFailPos=0`, `ErrorPos=0`. The zero-progress `ZeroOrMany(NamespaceMember)` does NOT yield an epsilon-`Success` at the start position (epsilon matches are accepted only at recovery positions for recoverable `RecoveryRule`s) — the whole start rule fails at 0.
- `LastSnapshot=null` (no mismatch snapshot captured) → **this is exactly the `snapshot == null` case**; `e=0`, `parseEnd=0`; `GetTerminators(empty stack)=[EOF]`.
- Memo after main parse: `(0,NamespaceMember,0) F`, `(0,CompilationUnit,0) F`, `(0,Module,0) F` (all `MaxFailPos=0`).
- iter=0: `S0` rejected (re-parse `Failure`, `e2=0`). Generated candidates: **`[S6:Module:namespace(r6,p16)]`** — the fix WORKS: stop-set = `{namespace, class} ∪ {EOF}`, resync point moved to pos 16 (the `namespace` keyword).
- S6 trial: patched memo `(0,Module,0) = Success@16 MaxFailPos=0`, **`wouldBeIgnored=True`** (see mechanism below) → re-parse `Failure@-1`, `e2=0` → rejected. No candidates left → break → `FINAL: Failure@-1`.

## Q2 — without fix, primary input

**FAIL.** Same first assert: `Assert.IsTrue failed. expected Success, got Failure` (line 91).

Real run: identical counters — `RecoveryPasses=1`, `EngineGenerateCalls=1`, `HygieneRemovals=6`, `RecoveryDiagnostics=[]`, `Failure@0`, `Expecteds=[namespace, class]`.

Probe: identical to Q1 except the generated candidate is `[S6:Module:EOF(r6,p43)]` (stop-set = `{EOF}` only — the pre-fix behavior) and it is rejected for the same `wouldBeIgnored=True` reason. Absorber fraction: N/A (no tree).

## Q3 — alternatives (executed: the test failed with the fix on the primary input)

With fix (real end-to-end runs, same grammar/asserts, fresh `Parser` per input):

| input | result | MAIN assert (structural node) | fraction | accepted candidate |
|---|---|---|---|---|
| (a) `namespace A; ### garbage ### class Bar {}` (len 41) | **PASS** (Success@41) | present: `NamespaceDeclaration[0..13)` + `ClassDeclaration[29..41)` | 16/41 = 0.390 < 0.5 | `S2:NamespaceMember:T1:29` (rank 2, single pass) |
| (b) `### garbage ### namespace Foo; ### garbage2 ### class Bar {}` (len 60) | **FAIL** (first assert: `expected Success, got Failure`) | n/a (no tree) | n/a | none — `S6:Module:namespace(r6,p16)` rejected at e=0 |
| (c) `namespace Foo ### garbage ### class Bar {}` (len 42) | **PASS** (Success@42) | present: `NamespaceDeclaration[0..30)` (its `;` element = absorber `[14..30)`) + `ClassDeclaration[30..42)` | 16/42 = 0.381 < 0.5 | `S2:NamespaceMember:T1:30` (rank 2, single pass); `S1:NamespaceMember:;` rejected (re-parse `Success@14`, `e2=14` not `> e=14`) |

(a) tree: `Members[0..41) = NamespaceDeclaration[0..13) + Skipped-absorber[13..29) + ClassDeclaration[29..41)`.
(c) tree: `Members[0..42) = NamespaceDeclaration[0..30) + ClassDeclaration[30..42)` (absorber is the injected `;` span `[14..30)`).

WITHOUT fix (same probe):

| input | result | MAIN assert | fraction |
|---|---|---|---|
| (a) | **PASS** — identical (Success@41, S2 accepted; S6 candidate is the same `S6:Module:Ident(r6,p17)` since a snapshot exists at e=13 — the fix branch is never consulted) | present | 0.390 |
| (b) | **FAIL** — `Failure@0` (`S6:Module:EOF(r6,p60)` rejected, e=0) | n/a | n/a |
| (c) | **PASS** — identical (Success@42, S2 accepted) | present | 0.381 |

**No input passes with the fix while failing the MAIN assert without it.**

## Q4 — VERDICT

**INFEASIBLE.** No input among {primary, a, b, c} discriminates the fix: primary/(b) fail in BOTH configurations (test aborts at the first assert, no tree at all); (a)/(c) pass in BOTH configurations (recovered by S2, independent of the fix).

Mechanism — two independent blockers, both empirically confirmed:

1. **The S6 candidate can never be accepted at `e == 0`** (decisive for primary/(b)). With garbage at position 0 the main parse *fails* (zero-progress `ZeroOrMany` ≠ epsilon success at the start position), so `e = 0` and no snapshot is captured — exactly the `snapshot == null` case. The fix works exactly as specified: S6's stop-set gains `First(startRule) = {namespace, class}` and the candidate resyncs to pos 16 instead of EOF=43 (probe: `S6:Module:namespace(r6,p16)` vs `S6:Module:EOF(r6,p43)` without the fix). But S6's `Apply` patches the memo at `(0, "Module", 0)` with `Success(node, 16, MaxFailPos=0)`. On the candidate re-parse, `ParseRule` (Parser.cs:299-309) **ignores any cached entry with `IsRecoveryPosition(cached.MaxFailPos)`**, i.e. `MaxFailPos == _recoveryPoint`; the loop sets `_recoveryPoint = e = 0` before re-parsing, so `MaxFailPos(0) == 0` → the patched memo is discarded ("Ignoring posible failed memo in recovery mode"), the re-parse re-parses from scratch, fails exactly like the main parse (`e2 = 0`, no progress), and the candidate is rolled back. S0 and S6 are the only candidates → unrecovered → `Failure@0`. This degeneracy is specific to `e == 0 == currentStartPos`: every existing S6 test has `e > 0` (garbage after a real prefix), where `MaxFailPos=0 != _recoveryPoint` and the memo is honored. The `GenerateS6` comment ("MaxFailPos = 0: чтобы `IsRecoveryPosition(MaxFailPos) == false` при e == S") implicitly assumes `e == S ≠ 0`.
2. **Even when accepted (`e > 0`), S6 cannot produce a structural node after the garbage** (decisive for the MAIN assert). Each accepted S6 candidate memo-patches the start rule at `currentStartPos` with `Success@S`; the re-parse consumes that memo immediately and stops at `S` — the parser never re-enters the content at `S`. The next S6 iteration scans from `S+1`, so the keyword at `S` is never parsed and is absorbed into the next absorber. The final tree would be an absorber chain covering `[currentStartPos..EOF)` (fraction → 1.0, no structural nodes) — the MAIN assert and the `< 0.5` assert would both fail. The only strategy that yields a post-garbage structural node is **S2** (skip-to-anchor + continue parsing), which uses the snapshot's loop anchors (a snapshot exists whenever a prefix was parsed, so the 9.1e fix branch is never consulted) — hence (a)/(c) pass identically with and without the fix.

## Log

- 2026-09-22 ~22:05 UTC — baseline `git status --short`: pre-existing uncommitted changes (staged): `M Tests/ParserTests/ParserTests.csproj` (adds CSharpGrammar/CsPreprocessor ProjectReferences, points 9.1/9.2), `M A8IterationLimitTests.cs`, `A CSharpCorpusProbeTests.cs`, `M R2ExpectedStopTests.cs`, `A V2IterationLimitScaleTests.cs`, `MM docs/RecoveryImprovementPlan-checklist.md`, `A` progress files 9.1/9.1b/9.1c/9.1d/9.2. `RecoveryEngine.cs` clean at baseline. None touched.
- 22:07 UTC — applied the exact fix to `GenerateS6` (after the `if (snapshot is not null) {...}` block, before `foreach (var t in terminators)`). `git diff`: only the specified insertion. NOTE: the spec's "7-line insertion" is a miscount — the specified snippet is 6 lines; the diff contains exactly those 6 lines, byte-exact.
- 22:09 UTC — `dotnet build Tests/ParserTests/ParserTests.csproj`: 0 errors.
- 22:10 UTC — wrote `Tests/ParserTests/Recovery/S6NullSnapshotTests.cs` exactly as specified (rule constructions compiled as given; no deviations).
- 22:12 UTC — rebuilt (with test file): 0 errors.
- 22:13 UTC — step 5 run WITH fix: **FAIL**, `expected Success, got Failure` (line 91).
- 22:16 UTC — temporary single-input diagnostic added; first build hit **NETSDK1022**: an external process had added `<Compile Include="Recovery\S6NullSnapshotTests.cs" />` to `Tests/ParserTests/ParserTests.csproj` (detected 2026-09-22 22:19:43 UTC). Per constraints a csproj modified by an external process is reverted — but a full `git checkout HEAD -- <csproj>` would have destroyed the pre-existing staged 9.1/9.2 ProjectReference change (constraint: do not touch pre-existing changes), so ONLY the externally added line was removed. csproj verified back to baseline (unstaged diff empty; staged diff = the two ProjectReferences only). Rebuild: 0 errors.
- 22:21 UTC — diagnostic run (with fix): `Failure@0`, `RecoveryPasses=1`, `EngineGenerateCalls=1`, `HygieneRemovals=6`, `RecoveryDiagnostics=[]`, no tree.
- 22:26 UTC — rewrote the temp diagnostic as a faithful step-by-step `Recover` emulation (real `ApplyPatches`/`ParseRuleOnce`/`RecoveryEngine.Generate`; private `_recoveryPoint` set via reflection only, to match the private loop state). Confirmed: snapshot==null at e=0; fix resyncs to pos 16; patched memo `wouldBeIgnored=True`; S6 rejected; `FINAL: Failure`.
- 22:30 UTC — step 7: `git checkout HEAD -- ExtensibleParser/Recovery/RecoveryEngine.cs`; rebuilt: 0 errors. Re-ran the specified test WITHOUT fix: **FAIL**, same message (`expected Success, got Failure`, line 91). Re-ran the probe: `S6:Module:EOF(r6,p43)`, same rejection.
- 22:33 UTC — re-applied the fix byte-exact; `git diff` verified (only the insertion).
- 22:35 UTC — step 9 (main assert could not pass with the fix — the test failed before reaching it): extended the probe to all 4 inputs + a real end-to-end `Parse` per input. WITH fix: (a) PASS 0.390, (b) FAIL, (c) PASS 0.381, primary FAIL.
- 22:38 UTC — reverted the fix once; rebuilt: 0 errors. Probe WITHOUT fix: (a) PASS identical, (b) FAIL, (c) PASS identical.
- 22:40 UTC — re-applied the fix byte-exact (diff verified); removed ALL temporary probe code — the test file now contains only the specified test; final `dotnet build Tests/ParserTests/ParserTests.csproj`: 0 errors.

## Final state

```
git status --short:
 M ExtensibleParser/Recovery/RecoveryEngine.cs        <- the fix (only unstaged production change)
M  Tests/ParserTests/ParserTests.csproj               <- pre-existing staged 9.1/9.2 change (untouched)
M  Tests/ParserTests/Recovery/A8IterationLimitTests.cs
A  Tests/ParserTests/Recovery/CSharpCorpusProbeTests.cs
M  Tests/ParserTests/Recovery/R2ExpectedStopTests.cs
A  Tests/ParserTests/Recovery/V2IterationLimitScaleTests.cs
MM docs/RecoveryImprovementPlan-checklist.md
A  docs/RecoveryImprovementPlan-progress9.1.md
A  docs/RecoveryImprovementPlan-progress9.1b.md
A  docs/RecoveryImprovementPlan-progress9.1c.md
A  docs/RecoveryImprovementPlan-progress9.1d.md
A  docs/RecoveryImprovementPlan-progress9.2.md
?? Tests/ParserTests/Recovery/S6NullSnapshotTests.cs  <- new test file
?? docs/RecoveryImprovementPlan-progress9.1e.md       <- this report
```

- `git diff --stat -- ExtensibleParser/Recovery/RecoveryEngine.cs`: `1 file changed, 6 insertions(+)` — the diff contains ONLY the specified insertion (the spec's "7-line" figure is a miscount of the 6-line snippet; content is byte-exact: `if (snapshot is null) { foreach (var t in FirstSets.Get(new Ref(startRule), calculator)) if (...) stopSet.Add(t); }` between the anchor-First block and the terminators loop).
- `Tests/ParserTests/Recovery/S6NullSnapshotTests.cs` contains ONLY the specified test (all temporary diagnostic code removed; file compiles, 0 errors).
- No commits made; no other files modified.
