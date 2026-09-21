# 7.1.1 (R3) — performance regression: recovery hang — root cause and fix

## Symptom
Six CSharpGrammarTests tests hung (minutes / effectively infinite) with the uncommitted 7.1.1
changes, but passed at normal speed when stashed:

- `GlobalUsingSimple_RejectedAtV9`
- `RefField_InRefStruct_RejectedAtV13`, `RefField_RefReadonly_RejectedAtV13`, `RefField_Simple_RejectedAtV13`
- `Invalid_ForeachNoIdentifier_Fails`, `Invalid_ForUnclosedParen_Fails`

A previous workaround attempt increased a test timeout (900s→18000s) — WRONG; the timeout is not
the disease. Root cause found and fixed below.

## Confirmed root cause

The culprit is **7.1.1 change (c): the check-before-increment rewrite of `BeginParseFrame`**
(i.e. the *removal of the +1 counter leak* of the old increment-then-check). NOT the per-frame
`EnsureSufficientExecutionStack` check (hypothesis disproved by bisection: with that check
disabled the hang remained) and NOT the cap value 400 (for these 37-char inputs the effective
limit is the per-char formula `128 + 4·len = 276`, unchanged by the cap).

Mechanism (verified by instrumentation of `BeginParseFrame`/the recovery loop on
`{ for (int i = 0; i < n; i++ x = 5; }`):

1. The main parse is shallow (depth 19) and fails at e=29. Recovery then runs candidate
   re-parses (S0/S1/…), and some re-parse paths descend all the way to the depth limit 276
   (a non-consuming recursion path cut by the guard) and fire the guard — 514 firings observed
   between two recovery iterations, each a full 276-frame descent.
2. Pre-7.1.1 (increment-then-check), a rejected frame left the counter incremented and
   `EndParseFrame` never ran for it — a permanent +1 leak per firing (unwinding only decrements
   accepted frames). After F firings the counter baseline was F, so descent k accepted only up
   to depth `cap-F-1`; after ~cap firings every re-parse died at frame 1. Total accepted frames
   per parse session were bounded by O(cap²) — the session failed fast. The old tests "passed
   fast" precisely because of this accidental leak.
3. 7.1.1 made the counter clean (a rejected frame leaves no trace). Consequence: every
   backtrack alternative / memo re-descent / recovery re-parse re-pays the FULL descent to the
   limit and re-fires the guard. The recovery loop (e advances 29→34→37→…, up to
   MaxRecoveryIterations=1000, several candidates per point, each a full re-parse) re-pays that
   cost hundreds-to-thousands of times → minutes / hang.

The per-frame `EnsureSufficientExecutionStack` (depth > 20) was measured as not the root cause
and was kept (second line of defense for smaller stacks; cap alone is calibrated for the
measured 1.5MB .NET 8 thread).

## Fix

`ExtensibleParser/Parser.Recovery.cs:468` — new `GuardFired()`, called from both guard-firing
paths in `BeginParseFrame` (depth cap at line 434, stack check at line 449): every firing
shrinks the effective limit by one (`_maxParseDepth--`, floored at 0).

- Restores exactly the pre-7.1.1 per-descent acceptance pattern: after F firings, a descent
  accepts up to depth `cap-F-1` and fires at `cap-F` (identical to the old leak arithmetic), so
  total accepted frames per parse session are bounded by O(cap²) again.
- Once the limit reaches 0, every frame is rejected at frame 1; the recovery loop's fail-safe
  (`e <= ePrev`) ends the session fast.
- The depth counter stays semantically clean (no leak), so `MaxParseDepthReached` records the
  first firing depth exactly — the R3 calibration assertion (`== RecoveryProfile.MaxParseDepthCap`)
  keeps passing.
- The depth cap (400) is unchanged and remains the stack-safety guarantee: a ~350-level nested
  input still fires the guard (~400 frames) long before the measured overflow threshold
  (~575-615 frames).

## Verification

- The 6 hanging tests pass FAST (no timeout changes):
  - `Invalid_ForUnclosedParen_Fails` 158 ms, `Invalid_ForeachNoIdentifier_Fails` 158 ms
  - `GlobalUsingSimple_RejectedAtV9` 294 ms, `RefField_InRefStruct_RejectedAtV13` 295 ms,
    `RefField_RefReadonly_RejectedAtV13` 294 ms, `RefField_Simple_RejectedAtV13` 294 ms
  - (before the fix: >120 s and still running)
- R3 350-level case: `Tests/ParserTests/Recovery/DepthGuardTests.cs`
  - `Test_DeepNesting_GuardFires_NoCrash` PASSED (40 ms): guard fires, `MaxParseDepthReached == 400`
    (exactly the cap), result is recovered (RecoveryDiagnostics > 0), no process crash.
  - `Test_ShallowNesting_CleanParse` PASSED (21 ms): 50 levels / 102 frames parse cleanly.
- Full suites (Debug, no timeout changes):
  - CSharpGrammarTests: Passed 1524, Failed 0, Skipped 3 (36 s)
  - ParserTests: Passed 418, Failed 0, Skipped 2 (748 ms)
  - CsPreprocessorTests: Passed 128, Failed 0 (5 s)
- No commit; working tree left with the fix in place.

# 7.1.2 (R3) — guard-fired result = A5-4 bottom contract + `InsufficientStack` diagnostic

## Goal

7.1.1 made a ~350-level nested input stop crashing (the depth guard fires first). This sub-point
makes the guard-fired parse produce the A5-4 bottom contract (`Success<T>` with a tree covering the
whole input) **plus** an `InsufficientStack` diagnostic, instead of just a bare `Failure`.

## Findings (verified by instrumentation before coding)

- The bottom contract **already holds**: for the 350-level `Expr := "(" Expr ")" | Digits` input the
  guard fires (`MaxParseDepthReached == 400`), recovery-to-EOF (S6) drives the result to
  `Success@EOF` with a root node spanning `[0..input.Length]`, and `ErrorInfo == null`. No recovery
  behavior change was needed — only the `InsufficientStack` diagnostic was missing.
- The 3.0a repro (trailing garbage at EOF, budget 11) now ends at `Success@EOF` with
  `MaxParseDepthReached == 12` (well under the cap) — the depth guard does **not** fire there, so it
  carries no `InsufficientStack` (correct: the diagnostic is emitted only when the guard fired).

## Changes

**`ExtensibleParser/Recovery/RecoveryDiagnostic.cs`** — `InsufficientStack` added to `RecoveryKind`
(it did **not** exist): `enum RecoveryKind { Inserted, Skipped, Unrecovered, Extraneous, InsufficientStack }`.
No exhaustive switch on `RecoveryKind` exists (verified), so adding a member is non-breaking.

**`ExtensibleParser/Parser.Recovery.cs`** —
- Guard-fired **signal**: fields `_guardFired` (bool) + `_guardFiredPos` (int), reset in
  `SetMaxParseDepth` (per-`Parse`, alongside `_maxParseDepthReached`). A **flag** (not
  `MaxParseDepthReached >= cap`) is used because `GuardFired()` shrinks `_maxParseDepth` on every
  firing, so the effective cap is no longer comparable at finalization; and for short inputs the
  effective cap (`128 + 4·len`) is below the global `MaxParseDepthCap`, so `>= cap` would miss
  legitimate firings.
- `GuardFired(int firedAtPos)` now records the **first** firing's position (the deepest reached point
  — the initial descent hits the cap before any backtrack/re-parse re-fires, since the limit shrinks
  after each firing). Both call sites in `BeginParseFrame` (depth cap + `EnsureSufficientExecutionStack`)
  pass `startPos`.
- **Diagnostic emission** in `FinalizeRecoveryDiagnostics` (the single per-`Parse` finalization point
  reached on every `FinishRecovery` exit): after the tree-derived diagnostics and the `Unrecovered`
  markers, if `_guardFired` append **one** result-level `InsufficientStack` diagnostic at
  `[_guardFiredPos.._guardFiredPos]`. It is a result-level marker (like `Unrecovered`, not a tree node —
  A4-2), so it is appended, not tree-derived, and not attached via the side-table.

## Test

`Tests/ParserTests/Recovery/DepthGuardTests.cs` (extended the two existing methods, no new methods):
- `Test_DeepNesting_GuardFires_NoCrash` (350 levels): asserts `Success@EOF` with the root node spanning
  `[0..input.Length]` (bottom contract, not a bare `Failure`), `ErrorInfo == null`, and **exactly one**
  `InsufficientStack` in the public `RecoveryDiagnostics`.
- `Test_ShallowNesting_CleanParse` (50 levels, control): asserts `Success@EOF`, zero diagnostics, and
  explicitly **no** `InsufficientStack` (the guard did not fire).

## Results

- `ParserTests`: Passed 418, Failed 0, Skipped 2.
- `CSharpGrammarTests`: Passed 1524, Failed 0, Skipped 3 (no regression vs the 7.1.1 baseline).
- `CsPreprocessorTests`: Passed 128, Failed 0.
- 350-level end-state: `Success@EOF`, root `Skipped` node `[0..701]`, public diags = `Skipped[0..351]`,
  `Unrecovered[198]`, `Unrecovered[351]`, `InsufficientStack[200]`.
- No commit; working tree left with the change in place.
