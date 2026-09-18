# Wave 1 / 1.1 (A1) — S6 "guaranteed progress" last-resort candidate

Status: done

## Task
Add `GenerateS6` (rank 6) to `RecoveryEngine`, called from `Generate` always when `e < input.Length`
(including `snapshot == null`), gated off in strict (`Recoverable:false`) regions.

## Mechanism chosen for snapshot==null / non-loop case
- The naive `Injection.Absorb` at `(e, firstTerminalOfStartRule)` is only READ during re-parse if the
  parser actually reaches that terminal at `e`. For a fixed `Seq` start rule (`Module := Expr Expr`) the
  Seq is complete at `e` and nothing is parsed at `e`, so the injection is never read.
- Chosen mechanism: **memo-patch the start rule at `currentStartPos`** to a `Result.Success` whose node is
  a `SeqNode` wrapping (a) the real prefix node (extracted from the memo at `(currentStartPos, startRule, 0)`)
  and (b) a trailing absorber `TerminalNode [e..S)`. The re-parse `ParseRule(startRule, 0, currentStartPos)`
  hits this memo on its very first step and returns Success@S (=EOF), with the tail covered by the absorber.
- Hygiene interaction: `HygieneCore` removes the start-rule record at `currentStartPos`, which would wipe the
  patch (Apply runs before Hygiene in `ApplyPatches`). Resolved by reordering `ApplyPatches` to run Hygiene
  BEFORE `candidate.Apply`, so the S6 start-rule memo-patch survives. (One-line reorder; existing S1-S5 patches
  are at positions other than `(currentStartPos, startRule)`, so their behaviour is unchanged.)
- **Reorder regression + fix (finished on resume):** the reorder made S2/S3 memo-patches a **no-op**: Hygiene
  (scope c) removes the target `Failure` memo at `(e, rule)` *before* `candidate.Apply`, and `PatchMemo` only
  patches *existing* keys → the absorber never lands. Consequences: (a) S2/S3 resync broken (T1/anchor,
  between-functions, trailing-garbage tests regressed to Success@<EOF>); (b) the missing memo no longer breaks
  the `Function → Block → NotPredicate(Function)` cycle → **stack overflow** in the re-parse that crashed the
  test host (and made the suite count vary: 215/229/292 — all partial, aborted runs). Fix: `PatchMemo` now
  creates the key at precedence 0 when no `(pos, rule)` keys exist, so the S2/S3 absorber survives the
  Hygiene→Apply order. Precedence 0 is correct for the S2/S3 targets (`Ref` → prec 0, non-TDOPP loop elements).

## Stop-set / S computation
- stop-set = anchor-First (First terminals of loop-derived anchors, snapshot != null) ∪ terminators
  (`parser.GetTerminators(stack)`, snapshot stack if present else rule-level fallback = [EOF]).
- S = first position > e where any stop-set terminal matches, else EOF. No MaxSkip (bottom).

## Files changed
- `ExtensibleParser/Recovery/RecoveryEngine.cs` — new `GenerateS6`, restructured `Generate` (strict-region
  gates S1-S6, S6 always when e<EOF), `Generate` signature + `startRule`/`currentStartPos`; S6 comment fix.
- `ExtensibleParser/Parser.Recovery.cs` — `ApplyPatches` reorder (Hygiene before Apply); `Generate` call site;
  **`PatchMemo` creates the key at prec 0 when absent** (fixes the reorder regression — S2/S3 no longer no-op).
- test call sites of `RecoveryEngine.Generate` updated for the new signature.
- new tests: `Tests/ParserTests/Recovery/S6BottomTests.cs`.

## Test results
Clean build (`bin`/`obj` removed, `dotnet build Tests/ParserTests/ParserTests.csproj`): 0 errors.
Full suite (`dotnet test Tests/ParserTests/ParserTests.csproj`): **Passed 336, Failed 0, Skipped 2, Total 338**.
The 2 skipped are pre-existing (`ShouldReportErrorForUndefinedRuleReference`, `RequiredSubruleNamesAreNotSpecified`).

The earlier "215 total" was a **partial count**: the S2/S3 reorder regression caused a stack overflow in the
`Function → Block → NotPredicate(Function)` re-parse that crashed the test host and aborted the run, so only a
subset of tests completed (count varied 215/229/292 across runs). After the `PatchMemo` fix the run completes
cleanly at 338.

D1 corpus (Wave 0 baseline): D1.2/D1.3/D1.4/D1.5/D1.6/D1.7 all `success@EOF=True`; D1.1 `success@EOF=False`
(expected — 120 errors > `MaxRecoveryIterations=64`, Wave 1.3/A4-2's job, not fixed here).

Acceptance (A1) all hold:
- `Module := Expr Expr` on `12 34 56 78` → Success@EOF, tail `[6..EOF)` covered by an `IsAbsorber` node
  (`S6BottomTests.Test_S6_FixedSeq_StartRule_TailCoveredByAbsorber`).
- S6 not generated in a strict (`Recoverable:false`) region (`S6BottomTests.Test_S6_Not_Generated_In_Strict_Region`).
- S6 is rank 6 (tried last); S0–S5 outcomes unchanged (full suite green, T1/anchor + D1 resync tests pass).

## Deviations
- The prior subagent's note attributed the D1.5 failure to `MaxRecoveryAttemptsPerPosition` (default 3 < rank 6).
  That budget was **already set to 16** in `NewLongestMatchParser`; the real cause was the same S2/S3 reorder
  regression (S2 resync no-op'd). No budget change was needed — the `PatchMemo` fix resolved D1.5.
- `PatchMemo` now creates an absent key at precedence 0 (was a strict no-op). This is the minimal production
  change required to keep the Hygiene→Apply order (needed by S6) without breaking S2/S3. It only affects the
  recovery engine's absorber patches; the `RecordMemo`/patch-log rollback still restores the pre-patch state
  (verified by `PatchRollbackTests`).
