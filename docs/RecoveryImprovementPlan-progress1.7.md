# 1.7 A8 — forced bottom on MaxIterations exhaustion — progress

Status: in progress (implementation subagent).

## Plan point
- File: `ExtensibleParser/Parser.Recovery.cs`, `Recover`, branch `if (iter >= MaxRecoveryIterations) break;` (line ~673).
- Replace `break` with direct forced-bottom return: absorber `[start..input.Length)` + wrapped node (GenerateS6 prefixNode switch logic, duplicated locally) + `Unrecovered` diagnostic in `_recoveryDiagnostics` + `return FinishRecovery(Result.Success(node, input.Length, 0), input)`.
- Compiler profile untouched (bail break at line ~666 runs earlier).

## Facts from research (verified against source)
- At the break point: `result`, `e` (set at line 669), `currentStartPos`, `startRule`, `input` in scope; `snapshot` NOT yet computed.
- parseEnd idiom (Recover lines 800/814): `result.TryGetSuccess(out _, out var p1) ? p1 : result.TryGetPartial(out _, out var p2) ? p2 : 0`.
- GenerateS6 node wrap (RecoveryEngine.cs:1010-1023): absorber = `new TerminalNode("Skipped", start, s, s - start, IsRecovery: true, IsAbsorber: true)`; prefixNode from `parser.Memo.TryGetValue((currentStartPos, startRule, 0), ...)` + TryGetSuccess; switch SeqNode/ListNode/TerminalNode/_ → Result.Success(node, s, 0).
- Unrecovered add pattern (line 750): `_recoveryDiagnostics.Add(new RecoveryDiagnostic(e, e, RecoveryKind.Unrecovered, message, null, startRule));` — promoted to public list by FinalizeRecoveryDiagnostics (line 892).
- Success@EOF return from Recover → Parse returns it directly (Parser.cs:267-268), ErrorInfo stays null.

## Subagent log
- research (explore): full context report — Recover loop, FinishRecovery, GenerateS6 wrap (RecoveryEngine.cs:1010-1023), Unrecovered add pattern (line 750), Result.Success signature, MaxRecoveryIterations property (Profile.MaxIterations, default 1000).
- implementation attempt 1: applied A8 hunk; S6BottomTests/A42/PartialAfter green; IterativeRecoveryTests 2 FAILED (assert pre-A8 contract) → reverted byte-exact per stop-if.
- 1.7a: updated the 2 IterativeRecoveryTests to A8 contract (fail on HEAD as expected).
- 1.7b: re-applied A8 hunk + new A8IterationLimitTests.cs (deviation: added `BuildTdoppRules()` in helper). All 5 gate classes green.
- 1.7c: fails-without-fix proof — stash → main assert fails (A8IterationLimitTests.cs:76) at 00:35:51 +03:00; pop → byte-exact, re-test 1/0 at 00:36:03.
- full-suite run revealed 13 more collateral failures (all MaxRecoveryIterations=0 tests asserting pre-A8 limit contract) — researched: all expected A8 changes, no regressions.
- 1.7d: updated the 13 across 5 test files (ExpectedSetApplicationTests, ParseContextTests, SnapshotTests, RecoveryDiagnosticTests, CandidateGenerationTests). ParserTests 447/0/2; CSharpGrammarTests 1524/0/3; CsPreprocessorTests 128/0.
- Status: COMPLETE, UNCOMMITTED (user instruction: do not commit).
