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
