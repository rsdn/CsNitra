# CsNitra Error Recovery Completion Plan

> **For agentic workers:** Use `superpowers:executing-plans` for sequential implementation. Use subagents only if the user explicitly selects delegation. Track the steps below and review each deliverable before continuing.

**Goal:** Complete the remaining recovery work while preserving valid parses, source coverage, deterministic repairs, and usable syntax after damaged regions.

**Architecture:** Retain the current S0–S6 candidate engine, injection/memo patches, and rollback. Correct context tracking and scanning first, then separate normal and recovery phases before changing search policy. Use shared grammar-aware boundaries rather than separate scanners with divergent behavior.

**Tech stack:** C#, .NET 8 tests, netstandard2.0 core, MSTest, existing DFA terminals.

**Spec inputs:** `docs/RecoverySystemPlan.v2.md`, `docs/RecoveryImprovementPlan.md` (especially A6, A7, F3+), `docs/RecoveryImprovementPlan-checklist.md`.

**Status:** User authorized sequential execution on 2026-10-10. Implementation and experiments are in the isolated CsNitra recovery worktree; commit and PR publication were subsequently authorized. See [execution report](../../RecoveryCompletionReport-2026-10-10.md) for evidence and design rulings. Local Debug/Release tests and the full consumer build pass; cross-platform acceptance and the exact historical F2 reproduction remain open. Nitrogen is outside scope.

| Task | Execution result |
|---|---|
| 1–5 | Implemented with safety, structural, context, scanner, and diagnostic regressions. |
| 6 | Metrics and effective policy exposed; original short-input repro unavailable. |
| 7 | Phase/rollback correctness implemented; normal context capture retained, replay optimization deferred. |
| 8 | Context-local 64-character repair implemented and measured. |
| 9 | Clustering implemented as opt-in; linear-scaling target not established. |
| 10 | FIRST prototypes removed after quality/performance experiments; no new quiet-zone suppression. |
| 11 | Consumer admission and documentation implemented; 2,170 Release tests pass, five existing skips; consumer build passes; Linux/Windows CI pending. |

The checkboxes below retain the detailed original acceptance checklist; this status table and report distinguish implemented behavior from pending or refined requirements.

## Global constraints

- Work only in CsNitra; preserve unrelated untracked `.DS_Store` and `.idea/` files.
- Inspect checkout and changes again before implementation. Arrange isolation at execution time, not for this documentation task.
- Core stays `netstandard2.0`; tests stay `net8.0`; use the pinned .NET 8 SDK rollforward policy.
- Follow repository Allman braces, file-scoped namespaces, explicit inheritance intent, records for data, and CRLF conventions.
- Keep tests stateless and parallel-safe. Use per-test parsers and unique temporary output paths.
- Preserve `Injection` and `MemoPatch` as repair patch units and preserve rollback of failed candidates.
- Preserve `Success`/`Partial` memoized prefixes during recovery hygiene.
- Do not redefine `Success@EOF` to mean error-free input. Preserve separate diagnostics and compiler bail behavior.
- Do not promise a hard wall-clock deadline: the existing time budget is cooperative degradation.
- Do not change public signatures merely to simplify an internal refactor. New APIs are additive unless a separately reviewed change is necessary.
- Do not introduce a global parsing chart or incremental subtree reuse in this work.
- Keep code changes and performance-policy changes in separate review units.
- No automatic staging, commits, pushes, or PR creation. Prepare reviewable changes; use version-control mutations only when authorized.

## Review focus

1. Recursive occurrences of the same rule must not borrow an outer occurrence's alternative or sequence.
2. Brackets and terminators inside strings/comments must not end a skip; escaped, raw, and interpolated strings need explicit coverage.
3. EOF, strict regions, and exhausted budgets must preserve input and report coarse recovery without inventing successful structure.
4. Rejected candidates must not leave state in snapshots, diagnostics, memo entries, injections, or degradation policy.
5. Dense errors and deeply nested short inputs must terminate while preserving later independent declarations where possible.

These conditions are assigned to Tasks 1–4 and 7–10 below, rather than left to final testing.

## Baseline and validation

Observed in this review on macOS ARM64, Debug, .NET SDK 8.0.410:

- ParserTests: 445 passed, 1 failed, 2 skipped.
- Failing case: `MiniC.DepthGuardTests.Test_DeepNesting_GuardFires_NoCrash`; expected depth 400, observed 161; reproduced alone.
- CSharpGrammarTests: 1,524 passed, 3 skipped.
- Regex, workflow, and preprocessor suites were not run in this review. These figures are a starting observation, not a release gate.

Run from repository root:

```sh
dotnet restore
dotnet test Tests/ParserTests --no-restore --filter "FullyQualifiedName~Recovery"
dotnet test Tests/ParserTests --no-restore
dotnet test Tests/CSharpGrammarTests --no-restore
dotnet test Tests/CsPreprocessorTests --no-restore
dotnet test --configuration Release --no-restore
```

The focused `Recovery` filter does not cover every recovery test: several use namespace `MiniC`. Always run the full ParserTests gate after a parser behavior change. GraphViz must be on PATH for the full solution gate. Restricted environments may require permission for the test runner's local communication socket; an aborted runner is not a test failure or pass.

For each task: add a regression that fails for the intended reason, implement the smallest change, run focused checks, then run affected suites. Pure reorganizations use differential tests instead of artificial failing assertions. Record timings outside parallel unit-test assertions: same configuration, isolated process, one warm-up and five measured runs, median and range. Compare trees, diagnostics, scan counts, and accepted strategies as well as elapsed time.

## Task 1 — Establish portable safety and recovery-quality gates

**Files:** Modify `Tests/ParserTests/Recovery/DepthGuardTests.cs`, `RecoveryCorpusTests.cs`; create `Tests/ParserTests/Recovery/RecoveryStructureTests.cs`. Read `Parser.Recovery.cs` guard and finalization paths.

**Interfaces:** Existing `Parser.Parse`, `MaxParseDepthReached`, `RecoveryDiagnostics`, `RecoveryProfile`, and raw syntax-node traversal. No product API change.

- [ ] Reproduce the depth failure and preserve its log. Separate the fixed depth cap from available-stack protection in test expectations.
- [ ] Replace the exact-depth assertion with the actual safety contract:

  ```csharp
  Assert.IsTrue(parser.MaxParseDepthReached > 0);
  Assert.IsTrue(parser.MaxParseDepthReached <= RecoveryProfile.MaxParseDepthCap);
  Assert.IsTrue(result.TryGetSuccess(out var node, out var end));
  Assert.AreEqual(input.Length, end);
  Assert.AreEqual(0, node.StartPos);
  Assert.AreEqual(input.Length, node.EndPos);
  Assert.AreEqual(1, parser.RecoveryDiagnostics.Count(d =>
      d.Kind == RecoveryKind.InsufficientStack));
  ```

- [ ] Keep the shallow nesting control error-free. Add compiler-mode and probe-depth cases so the test change cannot hide guard-state leakage.
- [ ] Add structural fixtures: two intact declarations around garbage; a damaged list item followed by an intact item; nested damage followed by an outer declaration; two independently damaged constructs; strict-region fallback. Assert expected declaration/item identities, retained terminal text, absorber intervals, and diagnostic kinds. Do not assert only EOF.
- [ ] Capture current results and classify existing structural failures as explicit baseline gaps. Add case-specific expectations to the owning later task; do not weaken expected useful structure to match an absorber.

**Exit:** Portable depth tests pass; every later task has a repeatable structural baseline. Run full ParserTests.

## Task 2 — Fix sequence and active-alternative identity (F3/F3+)

**Files:** Modify `ExtensibleParser/Recovery/StackFrame.cs`, `ExtensibleParser/Parser.cs`, `Parser.Recovery.cs`, `Recovery/RecoveryEngine.cs`, and `FollowSetCalculator.cs` only where context propagation requires it. Extend `ExpectedSetTests.cs`, `ExpectedSetApplicationTests.cs`, `FollowSetPerSiteTests.cs`, `R2ExpectedStopTests.cs`; create `ActiveAlternativeContextTests.cs`.

**Interfaces:** `GetSuffixObligationFirsts`, `BuildExpectedSet`, and `GetTerminatorsPerSite` retain their public return types. Internal frame metadata must identify the actual sequence occurrence and alternative, not infer ownership from adjacent frame names.

- [ ] Write failing fixtures with two alternatives having distinct suffixes, an intervening wrapper, nested sequences in one alternative, and recursive calls to the same rule. Use a hand-built snapshot to isolate lookup, followed by a real-parse regression.
- [ ] Assert the active suffix is present and the inactive suffix absent from expected and S3/S6 stop sets. Place the inactive suffix earlier in the input and assert it does not become the accepted skip endpoint.
- [ ] Carry explicit occurrence ownership from frame construction to snapshot consumers. Prefer additive internal metadata over searching backward for a same-named rule, which is unsafe under recursion. Preserve existing frame construction call sites through compatible defaults where feasible.
- [ ] Use the exact sequence for context-sensitive suffix/FOLLOW calculations. Retain conservative fallback only for genuinely unavailable context and record when it is used.
- [ ] Run context, expected-set, FOLLOW, R2, snapshot, rollback, and structural tests, then all three parser-related suites.

**Exit:** Recovery does not select a continuation from an inactive alternative in the reproducer, wrappers, or recursive cases. Any effect on S2/S4 is explicitly tested, not an incidental global lookup change.

## Task 3 — Preserve ordering while deduplicating stop sets (A7)

**Files:** Modify `Recovery/RecoveryEngine.cs` and `FollowSetCalculator.cs`; extend `TerminalComparer`/expected/FOLLOW tests and `RecoveryCorpusTests.cs` as needed.

**Interfaces:** Preserve array/list return types, `TerminalComparer.Instance`, insertion order, and deterministic rank/cost tie breaks.

- [ ] Record the post-Task-2 baseline, including a grammar with many duplicate suffix/FOLLOW terminals.
- [ ] For each ordered collection use a membership set alongside the existing list:

  ```csharp
  var seen = new HashSet<Terminal>(TerminalComparer.Instance);
  var ordered = new List<Terminal>();
  void add(Terminal terminal)
  {
      if (seen.Add(terminal))
          ordered.Add(terminal);
  }
  ```

- [ ] Apply this only to repeated deduplication paths in S3, S6, expected sets, and FOLLOW assembly. Do not claim it removes terminal matching's scan-length × terminal-count cost.
- [ ] Verify equal terminals from different instances deduplicate and ordering, chosen candidates, trees, and messages match the baseline.
- [ ] Measure dense-error and large-terminal-set cases. Run ParserTests, CSharpGrammarTests, and CsPreprocessorTests.

**Exit:** Equivalent observable behavior with hash-backed membership checks; report measured effect even if negligible.

## Task 4 — Make S3/S6 scanning share extensible boundaries (A6)

**Files:** Create `ExtensibleParser/Recovery/BracketPairs.cs` and `RecoveryScan.cs`; modify `Parser.Recovery.cs` and `Recovery/RecoveryEngine.cs`; create `BracketPairTests.cs` and `RecoveryScanTests.cs` under recovery tests.

**Interfaces:** Add `Parser.AddBracketPair(Terminal open, Terminal close)` returning `void`, and a read-only `Parser.BracketPairs` collection of immutable `BracketPair` records. Registration is additive, idempotent, order-independent; defaults are the three conventional pairs when no custom pairs are declared. Shared scan state holds nested sets of acceptable closers.

- [ ] Add failing S3 and S6 tests for nested blocks, mismatched closers, custom and multi-character pairs, overlapping `<` and `<[`, duplicate registrations, and one opener with multiple valid closers.
- [ ] Implement registration with `TerminalComparer.Instance` and deterministic ordering. Require bracket matches to consume input; reject zero-width matches as scan events without looping. Define null/invalid argument handling consistently with existing public APIs rather than swallowing programming errors.
- [ ] Implement longest-match bracket recognition and a nesting stack in the shared scanner. Preserve S3 MaxSkip and S6 coarse fallback behavior; bound stack growth by consumed input and measure allocation on long unmatched-opener input.
- [ ] Add a focused lexical-boundary design within this task: use existing trivia matching and explicitly declared opaque terminals for quoted/string-like regions. Do not treat every regex terminal as opaque. Resolve escape/raw/interpolation boundaries against the actual C# terminals before wiring. Document the small additive registration contract and test it before exposing it publicly.
- [ ] Verify terminators inside comments, escaped strings, raw strings, and interpolated strings cannot spuriously stop a scan; malformed opaque regions must terminate and preserve text. Preserve tests where genuine expected continuation ends a skip.
- [ ] Measure D1.2/D1.6 before and after independently of A7. Investigate clean-input regressions above 5% using repeated isolated runs; keep timing assertions out of parallel tests. Run all parser-related suites.

**Exit:** S3 and S6 agree on nesting and opaque-region boundaries, custom pairs compose deterministically, later outer declarations survive, and measured costs are reported.

## Task 5 — Unify diagnostic placement (F4)

**Files:** Modify `Parser.Recovery.cs`; extend `SideTableTests.cs`, `PublicDerivedListTests.cs`, and `RecoveryDiagnosticTests.cs`.

**Interfaces:** `DeriveRecoveryDiagnostics(root)` and the node-derived portion of `RecoveryDiagnostics` use the same canonical display positions. `Unrecovered`/`InsufficientStack` remain result-level markers. Node source spans stay unchanged.

- [ ] Add failing LF/CRLF, leading whitespace, first-line, multiple insertion, and subtree cases exposing the current position mismatch.
- [ ] Apply missing-token placement once before attaching canonical diagnostics to nodes; remove the second public-list-only transformation.
- [ ] Preserve terminal/rule/message metadata, stable ordering, and exact diagnostic instances shared between side-table and public APIs.
- [ ] Assert subtree results equal the corresponding canonical node-derived diagnostics from the full tree; keep result-level markers separate.
- [ ] Run diagnostic/side-table and structural suites, then full ParserTests.

**Exit:** Consumers obtain consistent diagnostic positions regardless of query path.

## Task 6 — Explain and instrument depth degradation (F1/F2)

**Files:** Modify `Parser.Recovery.cs`, `Recovery/RecoveryMetrics.cs`, and `RecoveryProfile.cs`; extend `DepthGuardTests.cs`, `ProbeDepthCeilingTests.cs`, and `RecoveryMetricsTests.cs`.

**Interfaces:** Add observation-only maximum-depth and guard-firing metrics, reset per parse. Expose effective depth separately from configured depth; preserve probe scratch isolation.

- [ ] First add per-parse maximum observed depth and guard-firing counts without changing policy. Record normal parse versus speculative probe contributions separately.
- [ ] Reproduce the documented short-input/excessive-guard case from the progress records and preserve it as a regression fixture. Compare requested depth, observed depth, and memo/candidate activity.
- [ ] Name the existing shrinking-depth policy explicitly and expose its effective limit. Preserve behavior initially; do not combine a policy rewrite with instrumentation.
- [ ] If the investigation identifies unnecessary recursion or repeated work, add its failing bounded-work regression and fix that cause in a separate change. Report whether the original symptom is reproduced rather than claiming an unexplained metric fixes it.
- [ ] Test repeated parses, rejected candidates, and alternating normal/probed calls for state isolation. Run guard, probe, metrics, rollback, and full ParserTests.

**Exit:** Depth degradation is explainable through metrics; excessive recursive work has a verified resolution or an explicit remaining blocker.

## Task 7 — Separate normal parsing from recovery (P)

**Files:** Modify `Parser.cs`, `Parser.Recovery.cs`, and snapshot/speculation helpers. Create `RecoveryPhaseTests.cs`; extend snapshot, rollback, partial-result, and valid-input corpus tests.

**Design decision before implementation:** Define internal `ParsePhase.Normal` and `ParsePhase.Recovery`, with scoped restoration around speculative work. Preserve compiler bail mode and `ParseSubRule`. Specify how the first failure gets a complete recovery snapshot if normal mode stops collecting rich frame side effects: either retain the necessary lightweight context or perform one instrumented replay. Measure both before selecting; do not gate capture off and lose the first failure.

- [ ] Pin normal/recovery transition behavior, valid-input tree equivalence, compiler bail, predicate failures, and snapshot completeness.
- [ ] Implement phase ownership and explicit mismatch/snapshot behavior without FIRST pruning or new repair strategies.
- [ ] Restore phase on every exception, speculative exit, rejected candidate, and parser reuse. Keep memo entries phase-safe wherever results differ; prove compatibility or include phase in affected keys.
- [ ] Verify recovery-only behavior on valid input yields the same tree and zero repairs; test ambiguous and extensible alternatives, not only literals.
- [ ] Run all parser-related suites and measure valid-input overhead and damaged-input replay cost.

**Exit:** Optimization of normal parsing no longer silently removes failure context required by recovery.

## Task 8 — Absorb unrecognized input locally (R1)

**Files:** Add `Recovery/GarbageScanner.cs`; modify `Recovery/RecoveryEngine.cs` and relevant rule declarations only after the contract below is settled; create `GarbageRecoveryTests.cs`.

**Design decision before implementation:** Define the relevant terminal vocabulary from the active grammar/context. An unrelated terminal elsewhere in the grammar must not cause false recognition. Keep garbage recognition recovery-only so it cannot change valid longest-match selection. The legacy “64 characters / 16 tokens” needs a defined unit: use a 64-character bound initially, with progress in characters and no undefined token counter in this lexerless parser.

- [ ] Add cases for invalid characters between intact declarations, immediately before a valid token, EOF, all garbage, whitespace/comments, and token-prefix overlap.
- [ ] Implement a bounded consuming scan that stops before recognized input or an opaque/nesting boundary using Task 4 facilities. It must consume at least one character whenever applied.
- [ ] Integrate as a local repair candidate before broad resynchronization, preserving existing rank/cost policy unless explicit new ranking tests justify a change.
- [ ] Assert preserved later declarations and full source coverage, plus fewer broad fallback operations on the local-garbage corpus. Do not assert that the locally skipped fragment is syntactically valid.
- [ ] Run candidate ordering, ambiguity, corpus, rollback, and all parser-related suites.

**Exit:** Small unknown fragments are repaired locally without changing valid input or swallowing the following construct.

## Task 9 — Avoid repeated speculation in dense damage (B5)

**Files:** Modify `RecoveryProfile.cs`, `Parser.Recovery.cs`, and `RecoveryMetrics.cs`; create `RecoveryClusteringTests.cs`; extend corpus/performance tests.

**Design decision before implementation:** Define a damaged region by actual accepted skipped intervals plus a configurable nearby-position distance. Use deterministic attempt/work counts to trigger clustering; elapsed time remains observational and the existing global degradation mechanism remains separate. Configure thresholds in the profile and reset state per parse.

- [ ] Add deterministic fixtures for repeated expensive errors nearby, separated errors with intact constructs between them, and rejected speculative candidates.
- [ ] Record attempts/work at accepted recovery points; when the reviewed threshold is exceeded, skip expensive speculation at subsequent points in the same region and use existing coarse tiers.
- [ ] Reset the cluster when an independent intact construct separates errors or the configured region distance is exceeded. Do not let a costly early error degrade the remainder of the file indefinitely.
- [ ] Compare retained declarations and diagnostics with clustering disabled; any quality tradeoff must be documented and explicitly accepted before enabling by default.
- [ ] Measure increasing dense-error inputs and report scaling, strategy counts, latency, and memory. Avoid claiming linear complexity from a few green timing assertions.

**Exit:** Dense-damage speculative work is bounded by an explicit policy with tested quality tradeoffs and parser-reuse isolation.

## Task 10 — Reassess FIRST pruning and quiet zones

**Files:** `Parser.cs`, `Parser.Recovery.cs`, `Recovery/FirstSets.cs`; dedicated phase/pruning tests and corpus measurements.

- [ ] Reproduce the earlier FIRST-filter regression against the post-Task-7 implementation using the existing affected tests.
- [ ] Prototype filtering only where FIRST is sound and cannot suppress a required snapshot, predicate, nullable branch, context effect, or extension.
- [ ] Keep the optimization only if all valid/recovered tree comparisons pass and representative measurements show a repeatable benefit. Otherwise close it as intentionally deferred; retaining a failed optimization is not a completion requirement.
- [ ] For quiet zones, first exhibit an actual cascading-diagnostic defect. If none is reproduced, retain observation-only state and document that suppression is unnecessary. If reproduced, add the smallest regression before specifying suppression boundaries.
- [ ] Run phase, snapshot, corpus, and full parser-related tests for any retained changes.

**Exit:** Each experiment ends with an evidence-backed keep/defer decision; no open “partially implemented” behavior is silently shipped.

## Task 11 — Verify integration and reconcile documentation

**Files:** Update `docs/RecoveryAuthorGuide.md`, both recovery checklists, and `README.md`; extend `.github/workflows/dotnet.yml` only where platform checks are missing.

- [ ] Run all parser-related suites in Debug and the complete solution in Release with GraphViz available. Report skipped tests and unrelated baseline failures explicitly.
- [ ] Verify the guard and scan cases on Linux, Windows, and macOS through the existing CI matrix. A local pass does not close portability.
- [ ] Publish a before/after report for structural retention, skipped spans, diagnostic cascades, candidate counts, timing distributions, and memory on the fixed corpus. Use S6/EOF coverage as one metric, not the success definition.
- [ ] Reconcile stale “uncommitted”, mask, soft-separator, and superseded loop/hygiene entries with commits and implementation. Document compiler bail, strict-region fallback, cooperative time budgets, custom boundaries, and diagnostic positions.
- [ ] Review every public consumer of recovered results for incorrect assumptions that `Success` means clean syntax; add targeted consumer tests where that assumption exists. Do not redesign unrelated semantic compilation.
- [ ] Inspect final changes for unrelated files and perform a focused review of rollback, termination, phase isolation, and public API compatibility.

**Exit:** No unexplained recovery test failures, a cross-platform acceptance record, and documentation that matches implemented behavior. Remaining optional work is named rather than marked complete.

## Deferred extensions and their entry conditions

| Item | Entry condition and deliverable |
|---|---|
| Real compiler two-stage integration | Select an actual compiler consumer; specify clean/failed/recovered admission and diagnostic ownership, then build one end-to-end consumer slice. Compiler bail mode itself already exists. |
| Multiple nearby S3 candidates | Show a corpus case where the nearest stop loses useful structure and a later stop improves it; cap candidates and measure extra reparsing. |
| Span/range input | Select a caller requiring bounded subranges; specify absolute versus relative offsets, EOF meaning, and terminal API compatibility before changing the parser input contract. |
| Code-generation preparation | Require an actual generation consumer; otherwise limit this work to focused scanner/context helpers and documentation. |
| Persistent subtree reuse / Wave 8 | Separate project covering node ownership, memo invalidation, diagnostic side-table lifetime/cleanup, and subtree queries. Not part of recovery completion. |
| Historical build watcher/duplicate Compile issue (F6) | Investigate only if reproduced in the current environment. Record the external process and timestamps; do not change project files or stop services speculatively. |

## Execution order and review boundaries

`1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 9 → 10 → 11`

- First milestone: Tasks 1–6 — safety, context correctness, boundaries, and diagnostics.
- Second milestone: Tasks 7–9 — explicit phases and measured local/dense-error improvements.
- Final milestone: Tasks 10–11 — evidence-based optimization decisions and integration acceptance.
- Task 5 can be implemented earlier if needed; avoid concurrent edits because most tasks touch the same parser and recovery engine.
- Pause only on a concrete unresolved contract, failing prerequisite, or quality/performance regression requiring a decision. Do not add another approval pause for routine steps within a subsequently approved scope.
- A task is complete only after its exit criteria and affected checks pass. Do not treat an updated checklist as evidence of completion.
