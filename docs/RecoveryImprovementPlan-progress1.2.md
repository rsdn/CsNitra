# Wave 1 / 1.2 (A5-7) — S6 = panic bottom (refinement/verification of A1)

Status: done (verification + tests; no production change)

## Task
A5-7 refines the S6 candidate already implemented in 1.1 (`RecoveryEngine.GenerateS6`).
Verify the four A5-7 requirements against the existing S6 and add the missing acceptance tests.

## A5-7 requirements — all already satisfied by 1.1 (no production change needed)
1. **Stop-set = terminators ∪ anchor-First.** Already in place. `GenerateS6` uses
   `parser.GetTerminators(snapshot?.Stack ?? Array.Empty<StackFrame>())` — the rule-level
   fallback A5-7 explicitly allows (per-call-site FOLLOW is A5-6, Wave 5 — NOT done here) —
   ∪ anchor-First from the snapshot's loop frames (`DeriveLoopAnchors` → `FirstSets.Get`).
   (`RecoveryEngine.cs:644-663`.)
2. **No snapshot requirement.** Already in place. `Generate` calls `GenerateS6` whenever
   `parseEnd < input.Length`, independent of `snapshot` (`RecoveryEngine.cs:36-37`);
   `GenerateS6` handles `snapshot == null` via `snapshot?.Stack ?? Array.Empty<StackFrame>()`
   and the `if (snapshot is not null)` guard around the anchor-First loop.
3. **No MaxSkip (bottom scans to EOF).** Already in place. `GenerateS6` never calls
   `GetMaxSkip`; the scan loop runs `for (pos = e + 1; pos <= input.Length; pos++)` — a true
   bottom, uncapped by `DefaultMaxSkip=1000`. (`RecoveryEngine.cs:665-691`.)
4. **Strict regions excluded (C1).** Already in place. `Generate` returns an empty candidate
   list when any snapshot frame has `Recoverable:false`, before S1..S6 are generated
   (`RecoveryEngine.cs:20-21`).

Result: **no production code was modified** — this sub-point is pure verification + tests.

## New tests added (Tests/ParserTests/Recovery/S6BottomTests.cs)
1. `Test_S6_Region_LongerThanMaxSkip_FullyCovered` (the key NEW A5-7 test):
   `Module := ZeroOrMany(Number)` on `"12 34 56 " + new string('#', 1500)` — a correct short
   prefix followed by 1500 chars of trailing garbage (longer than `DefaultMaxSkip=1000`).
   Asserts Success@EOF with the whole tail covered by a single absorber whose span is >1000
   chars. Proves S6 is a true bottom, not capped at 1000.
2. `Test_S6_ClosingBraceInsideString_DocumentedBaseline` (documented `}`-in-string baseline):
   `}` is put in the stop-set via `RecoveryOptions.Terminators` on the `Body` frame; the input
   tail is the string `"abc}def"` (with `}` inside) + garbage. Documents the CURRENT
   non-string-aware behavior: the scan stops at the `}` inside the string — the final tree
   contains an absorber ending exactly at the `}` position (observed absorbers `[3..7) [7..20)`
   on input `{x "abc}def" GARBAGE`, `}` at pos 7). Non-failing baseline; R1/B3 (Waves 3/5)
   will make the scan string-aware (then the tail is covered by one absorber to EOF, no stop
   at the in-string `}`).

(Requirement 1's acceptance test — `Module := Expr Expr` on `12 34 56 78` → Success@EOF with
the tail covered — was already present from 1.1 as
`Test_S6_FixedSeq_StartRule_TailCoveredByAbsorber` and still passes.)

## Test results
Build (`dotnet build Tests/ParserTests/ParserTests.csproj`): 0 errors.
Full suite (`dotnet test Tests/ParserTests/ParserTests.csproj`):
**Passed 338, Failed 0, Skipped 2, Total 340.**
The 2 skipped are pre-existing (`ShouldReportErrorForUndefinedRuleReference`,
`RequiredSubruleNamesAreNotSpecified`). S6BottomTests: 5/5 pass (3 from 1.1 + 2 new).
Delta vs 1.1 (336 passed / 338 total) = exactly the 2 new tests.

## Deviations
- None. No production change (as intended for a verification sub-point). The `}`-in-string test
  asserts the current (non-string-aware) behavior as a documented baseline rather than the
  ideal string-aware outcome, per the A5-7 note that string-aware scanning is R1/B3 (later waves).
- The `}`-in-string test asserts on the final parse tree (an absorber ending at the in-string
  `}`) rather than on a directly-invoked `GenerateS6`, because `parser.LastSnapshot` after a
  full `Parse` is the last snapshot (at EOF) and does not reflect the trailing-garbage point;
  the observable tree behavior is the stable contract to document.
