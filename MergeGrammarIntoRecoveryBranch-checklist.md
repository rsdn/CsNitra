# MergeGrammarIntoRecoveryBranch — execution checklist

Branch: `error-recovery-completion-2` (verified). Merge `grammar` (571794b, == origin/grammar) into it with ONE merge commit, made only after everything is green. `git merge --no-commit` used so nothing is committed mid-way. No rebase. Branch `grammar` untouched.

- [✅] 1. `git merge --no-commit grammar`; conflicts: `ExtensibleParser/Recovery/RecoveryEngine.cs` (6 hunks), `Tests/ParserTests/Recovery/ExpectedSetApplicationTests.cs` (1 hunk), `docs/RecoveryImprovementPlan-checklist.md` (2 hunks). `Parser.Recovery.cs`/`FollowSetCalculator.cs`/`StackFrame.cs` auto-merged. Side semantics: stage :2: (ours/HEAD) = PR-author hardening; stage :3: (theirs/grammar) = A8/A10/A11/R2 waves.
- [✅] 2. Resolve conflicts per plan (2.1 RecoveryEngine.cs, 2.2 ExpectedSetApplicationTests.cs, 2.3 docs checklist — all resolved, staged)
  - [✅] 2.1 `ExtensibleParser/Recovery/RecoveryEngine.cs` (6 hunks: A11 catch-all filter + PR `seen` dedup combined in S3/S6 stop-set filters; S3/S6 scans merged into new private `FindStopWithTokenJump` = PR boundary mechanism (bracket pairs, opaque regions, scanStops, notePosition) + grammar A10 token-jump; hardcoded bracket counters dropped; ExtensibleParser build 0/0)
  - [✅] 2.2 `Tests/ParserTests/Recovery/ExpectedSetApplicationTests.cs` (1 hunk: method-name-only conflict; kept HEAD name `Test_FatalError_EntryMismatchHasCompleteSnapshot` — matches body asserting snapshot retention; no collision)
  - [✅] 2.3 `docs/RecoveryImprovementPlan-checklist.md` (2 hunks: union both sides — R1 CLOSED line + PR's GarbageScanner line with merge note; "2026-10-10 completion continuation" + grammar's "Wave 9" sections both kept)
- [✅] 3. `dotnet build` from repo root → 0 errors, 0 warnings
- [✅] 4. `dotnet test Tests/ParserTests` → Failed: 1, Passed: 495, Skipped: 4, Total: 500 (only failure = UsingStaticRecoveryTests, expected). Note: `dotnet test --no-incremental` is not a valid dotnet-test switch (MSB1001) → used `dotnet build Tests/ParserTests --no-incremental` + `dotnet test --no-build`
- [✅] 5. `A10A11TokenScanTests` green (1/1 passed)
- [✅] 6. `dotnet test Tests/CSharpGrammarTests` → Passed: 1539, Failed: 0, Skipped: 4 (probe skipped)
- [✅] 7. Optional probe run: 10/10 verdict=Uncovered coversInput=False (absorberBytes now 733–72930 vs 22 pre-merge; Syntax.xml.Internal.Generated.cs 3%); summary `processed=10 clean=0 recovered=0 falseGreen=0 uncovered=10 failed=0`; `[Ignore]` restored (diff vs HEAD clean)
- [✅] 8. Merge commit `f98745a` (parents 00ddea8 + 571794b); `grammar` untouched (571794b == origin/grammar)
- [✅] 9. Final report

## Research: coversInput=False with Success@EOF (post-merge)

- [✅] 10. Research: root cause found — `RecoveryEngine.cs:1113` (`prefixNode` `TerminalNode`-arm drops the new absorber; S6#2 with null-snapshot stop-set `{EOF}` patches start-rule memo to `Success@EOF` over a lying node). Report: `MergeGrammarIntoRecoveryBranch/progress-covers-regress.md`. Instrumentation restored byte-exact (git diff empty).

## Round 3 (2026-10-11): fix S6 absorber loss + trivia-aware CoversInput

- [✅] 11. Part A: `RecoveryEngine.cs` `GenerateS6` — FINAL form (user decision after regression): unconditional `TerminalNode terminal => new SeqNode(terminal.Kind, [terminal, absorber], currentStartPos, s)` + visitor-side wrapper handling in `SeparatedListVisitor` (criterion: exactly 2 RawElements, 2nd is `TerminalNode { IsAbsorber: true }`; test methods untouched). Interim `when s > start` guard tried and rejected empirically (absorber non-empty in visitor case).
- [✅] 12. Part B: `ParseHealth.cs` — trivia-aware `CoversInput` (gaps consumed by `trivia.TryMatch`); signature `Analyze(root, string input, bool successAtEof, Terminal trivia)`; `UnionCoverage` added; callers updated
- [✅] 13. Verify: build 0/0; targeted UsingStaticRecoveryTests FAIL by design (CoversInput=True, UnionCoverage=60, Fraction=75); full ParserTests 495/1/4 (baseline restored); full CSharpGrammarTests with probe active: 1540/0/3, zero failures; probe verdicts ×10 FalseGreen coversInput=True; `[Ignore]` restored, filtered re-run Skipped: 1
- [✅] 14. Round-3 report

## Deviations (round 3, additional)

10. Prescribed Part A fix broke 3 AST-visitor tests (`RequiredCallWith1/2/3Args`); user chose: keep unconditional fix + add wrapper case to `SeparatedListVisitor` (tests untouched). An interim `when s > start` guard was tried per user suggestion and rejected empirically (absorber non-empty in the visitor case).

## Deviations (round 3)

7. `Analyze` signature: plan said add 4th param `Terminal trivia` keeping `int inputLength`, but trivia-gap checking requires the input text (`Terminal.TryMatch(string input, int startPos)` returns match length or -1). Resolution: `Analyze(ISyntaxNode? root, string input, bool successAtEof, Terminal trivia)` — `inputLength` replaced by `input.Length`.
8. `ParseHealthReport` gains `int UnionCoverage` (merged terminal-span coverage in bytes) — required by the round-3 probe report format (`unionCoverage=` column).
9. Expected-probe note: plan expects `verdict=Recovered` while also expecting `absorberBytes≈6.7M` on a 6.7MB file — contradictory (fraction ≈100% ⇒ `FalseGreen` by the verdict rules). Implement per spec; report the actual verdict.

## Resolution policy (from the task)

- Conflicts resolved in favor of `grammar` as base; our A8/A10/A11/R2 kept; PR-author improvements layered on top where they are explicit upgrades (exact sequence context, opaque regions, phases, rollback on exceptions, LocalGarbage).
- RecoveryEngine GenerateS3/GenerateS6: keep token-wise scan (`CollectGrammarTerminals`) + `IsCatchAllRegexTerminal` filter (Kind ∈ {Identifier, Ident}) + `AddSuffixObligations`; take boundaries from PR shared scanner (bracket pairs, opaque regions).
- FollowSetCalculator GetTerminatorsPerSite: PR exact-sequence via `SeqFrameLocation.Sequence` REPLACES our reverse searches (F3 goal); R2 `AddSuffixObligations` suffix-firsts stay on top. (No conflict — auto-merged.)
- Parser.Recovery Recover/TryCandidate: keep BOTH our A8 and PR `_phase` flag + rollback on exception. (No conflict — auto-merged.)
- StackFrame: PR adds `Sequence` to `SeqFrameLocation`. (No conflict — auto-merged.)
- ParseHealth.cs: ours wins if collided (grammar has none). (No conflict.)
- Unresolvable conflict → keep `grammar` side, record in report. (Not needed.)
- Unexpected test failures → `git merge --abort`, report, do not push through. (Not needed.)

## Deviations

1. `dotnet test --no-incremental` rejected (MSB1001 — it is a `dotnet build` switch). Used `dotnet build Tests/ParserTests --no-incremental` then `dotnet test --no-build`.
2. RecoveryEngine S3/S6: subagent introduced a private `FindStopWithTokenJump` (port of the PR `RecoveryScan.Find` boundary algorithm + grammar A10 token-jump) rather than editing `RecoveryScan.cs` (out of the one-file scope). Behavior preserved (A10A11TokenScanTests green); minor duplication, follow-up noted in progress2.1.md.
3. ExpectedSetApplicationTests conflict was method-name-only; kept HEAD/PR name `Test_FatalError_EntryMismatchHasCompleteSnapshot` (body asserts snapshot retention). No test-name collision.
4. docs checklist resolved as union of both sides (kept both R1 entries + both new sections), not "grammar wins" — both are additive historical log entries.

## Research: S2 accept -> why valid 'namespace N; class C {}' at 37 got absorbed

- [✅] 15. Research Q1-Q4: after the correct S2 resync patch (absorber [15..37)), why did the parser absorb [37..60) instead of parsing 'namespace N; class C {}'. Deliverable `MergeGrammarIntoRecoveryBranch/progress-s2-after.md`. Temp instrumentation + temp un-ignore of UsingStaticRecoveryTests, restore byte-exact; no commits.

## Research: F9 fix plan (spurious-progress accept + stale Success-memo)

- [✅] 16. Research Q1-Q5: done — minimal set A+B+C, Q1 accept-criterion change HARMFUL (7 regressions). how to correctly fix F9. Deliverable `MergeGrammarIntoRecoveryBranch/progress-f9-plan.md`. Temp experiments allowed with byte-exact restore; no commits.

## Implement F9 fixes A+B (one commit)

- [✅] 17. Implement A (propagate prefix MaxFailPos in `ContinueFromPartialPostfix`, Parser.cs) + B (narrow Success staleness in `HygieneCore`, Parser.Recovery.cs). Only these 2 files + `PatchRollbackTests.cs` if justified. Verify 495/0/5 + 1540/0/3, then ONE commit.

## Implement F9 fix C (re-anchor S2 to loop-iteration start)

- [✅] 18. Implement C: `IterationStartPos` in `LoopFrameLocation` + 6 push-sites in `Parser.cs` + new branch in `AddResyncCandidate` (`RecoveryEngine.cs`) with conservative predicate `parseEnd == loop.IterationStartPos`. Do NOT touch accept criterion `e2 > e`. Verify 495/0/5 + 1539/0/4 + UsingStaticRecoveryTests un-ignored green. NO commit in this subtask.

## Experiment: fix C in series + probe after C

- [✅] 19. Experiment (no commit, no new research files): (1) second [TestMethod] in UsingStaticRecoveryTests.cs - two `using static` garbage regions with valid code between; report actual values, no fudging. (2) corpus probe with [Ignore] temporarily off, record 10 verdicts vs baseline (all FalseGreen 99%), restore [Ignore] byte-exact.
