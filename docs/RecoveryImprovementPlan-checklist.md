# RecoveryImprovementPlan — execution checklist

Plan: `docs/RecoveryImprovementPlan.md` (waves 0 → 7).
Skill: `plan-execution`. One subagent per sub-point, sequential, verify (build + tests) then commit.

Status: `[ ]` not started · `[~]` in progress (exactly one) · `[✅]` done · `[❌]` failed (stop + explain).

## Wave 0 — measurement base (D1, D2-core)
- [✅] 0.1 D1: benchmark corpus — 7 scenarios (D1.1 100+ small errors; D1.2 one error at end of large file; D1.3 error in nested loops / beginning of iteration; D1.4 regex terminals; D1.5 ambiguous longest-match; D1.6 dirty file errors every N lines; D1.7 garbage + broken next construct — "double damage"). Time asserts on recovery per file; baseline before/after each wave.
- [✅] 0.2 D2-core: expose counters `RecoveryPasses`, `EngineGenerateCalls` in the benchmark report (visible per scenario).

## Wave 1 — contract and bottom: "always to the end + all errors"
- [✅] 1.1 A1: S6 "guaranteed progress" last-resort candidate (rank 6, generated always when E<EOF incl. snapshot==null; absorber [E..S) to known-good point or EOF; Ref→memo-patch, loop-level→loop absorber, else Absorb injection; snapshot==null→synthetic start-rule frame).
- [✅] 1.2 A5-7: S6 = panic bottom (stop-set = terminators (FOLLOW-union over stack, fallback rule-level) ∪ anchor-First; no snapshot requirement; no MaxSkip; strict regions excluded C1).
### 1.3 A4-2: "report and continue" (sub-points 1.3.1–1.3.6)
- [✅] 1.3.1 S6 node shape — `GenerateS6` memo-patch now shape-preserving (SeqNode/ListNode → natural Kind + absorber in `RawElements`; TerminalNode → `SomeNode`; null → absorber root). S6BottomTests 5/0, SeparatedListTests 20/0, full ParserTests 338/0/2. Commit 77cf522.
- [✅] 1.3.2 S6 guaranteed bottom — S6 (rank 6) exempt from the per-point budget; `MaxRecoveryIterations` 64→1000. D1.1 → `Success@EOF`; I6 preserved. Full ParserTests 338/0/2. Commit 77cf522-followup.
- [~] 1.3.3 Recovery tests assert reached-EOF — after 1.3.2 (S6 always reaches EOF), update ALL recovery-verifying tests to assert reached-EOF + error: CSharpGrammarTests `AssertFails` → `AssertRecoversWithEnd` for every now-S6-reachable site; ParserTests recovery tests likewise. Acceptance: all recovery tests pass with the reached-EOF assertion.
- [ ] 1.3.4 Unrecovered emission — on per-point budget exhaustion (S0–S5 all no-progress/skip, S6 is the only remaining candidate), emit `RecoveryDiagnostic(Kind.Unrecovered, E, …)`. Acceptance: a test with an unsavable-within-budget error yields ≥1 `Unrecovered` at the correct position. Emit only when S6 is actually the fallback.
- [ ] 1.3.5 Strict regions FatalError — `Recoverable:false` regions still `FatalError` (no S6, no `Unrecovered`). Acceptance: a test with a strict region → `FatalError`, no `Unrecovered`, no S6.
- [ ] 1.3.6 Acceptance tests — D1.1 (120 errors → `Success@EOF`, all reported); D1.2 (3+ errors, one unsavable within budget → all reported + EOF + ≥1 `Unrecovered`); D1.3 (strict region → `FatalError`). Acceptance: all pass.
- [ ] 1.4 A5-4: bottom-contract of result — IDE profile returns `Success<T>` with tree covering whole input (absorbers), not `Failed(FatalError)`; `Failed` only in Compiler profile; `Unrecovered`/`InsufficientStack` diagnostics on tree. Remove `InvalidOperationException` on Partial@EOF in consumers (CsNitraParser.cs:140-153).
- [ ] 1.5 C1: audit progress guarantee (I1) — from `Recover` with `FatalError` exit only when `Recoverable:false`; S6+MaxSkip (S6 ignores MaxSkip), S6+nested strict regions (S6 does not "close" a strict region). Contract tests green.
- [ ] 1.6 C2: Partial-after-recovery semantics — S6 absorbers give `Success@EOF` (not Partial); fix + tests (Partial@EOF with diagnostics unreachable or explicit meaning).

## Wave 2 — re-parse speed (B1, A2)
- [ ] 2.1 B1: exact hygiene — `HygieneCore` removes only `Failure` with `pos < E && MaxFailPos >= E` + start-rule record at `currentStartPos`. Test "one error at end of large file": memo removals before/after (D2); re-parse time drops.
- [ ] 2.2 A2: budgets by tiers, not candidates — S1 sub-budget (e.g. 4 insertions); S2 own sub-budget (separate from S3/S6); S3/S6 separate sub-budget; "attempt" = tier. Param in `RecoveryProfile`. Regression: "garbage with identifier tokens" → S3 still tried.

## Wave 3 — "nothing matched" class (R1)
- [ ] 3.1 R1: garbage terminal (Roslyn `BadToken` analogue) — built-in `Garbage`, `TryMatch(p)` = "no terminal of the set matched at p", consumes to first position where someone matches or limit (64 chars / 16 tokens). Local absorber, not explosive S2/S3/S6.

## Wave 4 — profiles and predictability (A3, B4 + A4-5)
- [ ] 4.1 A3: `RecoveryProfile` — `Mode (Ide|Compiler|Test)`, `TimeBudget`, `MaxIterations`, `AttemptsPerTier`, `MaxSkip`, `StrategyMask (S0..S6)`; new type + `Parser` constructor.
- [ ] 4.2 A4-5: runtime strategy instead of `#if RECOVERY` — `Mode.Compiler` = bail (no RecoveryEngine, no candidates, first FatalError/Recoverable:false ends parsing). Removes compile-time branch (Directory.Build.props) and its side effect (missing depth-guard in no-recovery). depth-guard active in all modes (R3-repro test).
- [ ] 4.3 B4: time-budget with degradation ladder — (1) full S0–S6; (2) disable S2 speculation, MaxSkip ×4; (3) only S1 + short S3 + S6; (4) hard limit: accept S6 and stop. Worst IDE delay bounded; "not recovered" degrades to "recovered coarsely", not FatalError.

## Wave 5 — cheap candidates, boundaries, exact FOLLOW
### 5a (cheap)
- [ ] 5a.1 B2: spec-parse cache for whole `Recover` — lift `specCache (rule,pos)→(Ok,EndPos)` from local `GenerateS2` to a field for the duration of `Recover`. Store for A5-1 probe. Hits/misses in D2.
- [ ] 5a.2 B3: cheap S2/S3 scans — token jumps (trivia via one `Trivia.TryMatch`); for `Literal` terminators `input.IndexOf` instead of per-position `TryMatch` (DFA only for regex).
- [ ] 5a.3 A4-1: "quiet zone" — after accepting a candidate at E, mismatches at position ≤ E do not update `ErrorPos`/`_expected` and do not clear snapshot; new `FailureSnapshot` only strictly beyond E.
- [ ] 5a.4 A4-3: single-token deletion as rank-1 candidate — if terminal after E (post trivia) ∈ expected at E → absorber exactly [E..E+1) without speculative parse. `aab` duplicate-token cases → one `extraneous` diagnostic, rank 1, no S2.
- [ ] 5a.5 A4-4: First-prefix filter + expected at boundary — (1) in `ParseAlternative` before trying an alternative, First-check first terminal (terminal cache makes loss free); (2) at `ZeroOrMany`/`OneOrMany`/`SeparatedList` iteration start when `LA(1) ∉ First(body)` write `First(body) ∪ exit-tokens` into `_expected`. Perf assert on clean corpus (principle 5).
### 5b
- [ ] 5b.1 A5-6: per-call-site FOLLOW for recovery — in `GetTerminators` (S3 stop-set) and `Follow(top)` (S1 source) per-call-site value over snapshot stack: `follow_site(i) = First(tail of parent Seq after call element) ∪ (nullable ? follow_site(i-1) : ∅)`, base {EOF}. Seq/Loop per-site; TDOPP + ContextScope → rule-level fallback; unresolved → rule-level. Author `Options.Terminators` still overrides per-site. S6 consumes `GetTerminators` as stable interface. Regression: `List := Ref(X) ',' Ref(X)` / `Block := Ref(X) '}'`.
- [ ] 5b.2 A5-1: bounded speculative probe — (a) S2 T2 `CanStart` derived automatically from snapshot frames (via `Speculative` + specCache B2); (b) T1 anchors also for Ref frames. Accept = consumed ≥K tokens (K=2 default, `RecoveryOptions.SoftDepth`). Depth limit = work ceiling, not accept condition. Predicate set limited to loop-body Refs + top frame (not any Ref). T2 collection cap — first K positions per predicate (sort by Pos). Exclude pure regex-First (`Identifier`) predicates. TDOPP/ContextScope frames not derived. Order deterministic: author anchors first, then by frame proximity top→bottom. Depends on A2+B2+B3+A5-6. D1.7 acceptance test; S3 not silenced (regression).
- [ ] 5b.3 R2: stop predicate from expected set — S3/S6 stop scan at first position matching expected continuation (First-sets of structures expected by higher rule at E ∪ terminators).

## Wave 6 — diagnostics and metrics
- [ ] 6.1 A5-2: diagnostics derived from tree (single source of truth) — `_recoveryDiagnostics` as cache; add `DeriveDiagnostics(root)` walking IsRecovery nodes (`SyntaxTree.cs`): zero insertion→`Inserted`, absorber→`Skipped` with node text, `Unrecovered` from result. Public list derived from tree. Test: list == tree walk after rollbacks/iterations; subtree diagnostics query works.
- [ ] 6.2 D2 (full): counters by strategy — accept/rollback per S (S0..S6); hygiene removals before/after B1; specCache hits/misses (B2); time by phase (main parse / generation / re-parses).
- [ ] 6.3 A4-6: grammar quality channel — public `GrammarDiagnostics` (separate from `RecoveryDiagnostics`): anchor used N times / never; all recovery points of a rule identical; `Recoverable:false` region never fires on corpus; S2 anchor redundant. Generated on D1 corpus.

## Wave 7 — polish
- [ ] 7.1 R3: depth-guard calibration — recalibrate `_maxParseDepth` under real stack budget (empirics: 350 nesting levels = crash); preventive check in hot points; guard-fired result = A5-4 bottom contract with `InsufficientStack` diagnostic. 3.0a + 350-level case pass; process does not die.
- [ ] 7.2 A5-3 / R5: diagnostics on first word of dirty region — absorber span `[E..S)` → first non-whitespace "word" inside; missing node with skipped text → diagnostic at first skipped token; "end of previous line" for missing on new line (R5).
- [ ] 7.3 B5: clustering of expensive points — after a point that spent many attempts/time (threshold from profile), next iterations in same "dirty" region start immediately with coarse tier (S3/S6), skipping S2. D1.6 time grows linearly, not quadratically.
- [ ] 7.4 A5-5: SoftSeparator for `SeparatedList` — option `SoftSeparator(t)` — foreign separator at separator position consumed with `Skipped` diagnostic and treated as needed. Declarative (grammar-driven), not lambda.
- [ ] 7.5 A4-7: exact expected in final message — `FatalError`/`Unrecovered` expected = top frame `Expected` ∪ `GetTerminators(stack)` (per-site after A5-6) ∪ First of suffix obligations (S2/S4 mechanics → shared function).
- [ ] 7.6 A5-8 (optional): K-candidates in S3 — not `break` on first stop point, but K nearest (3–5) as separate candidates.
- [ ] 7.7 E: structurally for code generation — keep `RecoveryEngine.Generate` clean; `Injection` + `MemoPatch` only patch units (do not complicate); document "re-pass of prefix is cheap" property; do not introduce global chart.

## Deviations
- 1.2: pure verification + tests (no production change — 1.1's S6 already satisfied all 4 A5-7 requirements). `}`-in-string scan is NOT string-aware (documented baseline, stops at in-string `}`); R1/B3 (Waves 3/5) will refine.
- 1.1: `ApplyPatches` reordered to Hygiene-before-Apply (S6 needs it); this made S2/S3 `PatchMemo` a no-op → real regression + stack overflow (NotPredicate cycle no longer broken). Fixed by `PatchMemo` creating a prec-0 key when none exist. S6 absorber starts at `parseEnd` (not `e`) to cover the actual trailing region when `ErrorPos > parseEnd`. Full suite 336/0/2.
- 0.1+0.2 combined into one subagent (tightly coupled: the D1 corpus report surfaces the D2-core counters). Done together in `RecoveryCorpusTests.cs`.
- D1.1 (120 errors) baseline: Success@EOF=False (120 > MaxRecoveryIterations=64), passes=65. This is the Wave-1 acceptance metric (A4-2/A3). D1.7 baseline resync=11 (Wave-5 A5-1 acceptance metric).
- D1.3/D1.4/D1.7 tests set `MaxRecoveryAttemptsPerPosition=16` (test-only instance setting, established pattern) so S2/S3 are reached within budget.
- Pre-existing `Nitra.sln` build-artifact modification (VS version stamp + Shared.projitems) excluded from Wave-0 commit (was modified before execution started).
