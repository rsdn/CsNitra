# RecoveryImprovementPlan — 1.3.3 progress

Sub-point: **recovery tests assert reached-EOF** (convert `AssertFails` → `AssertRecoversWithEnd` in `InterpolatedStringTests.cs`).

Status: **DONE with a significant deviation** — 11 of the 18 converted inputs do NOT reach EOF (all in the Raw interpolated-string family). Per the task's "STOP and report which input and why — do not force it," they were left as `AssertRecoversWithEnd` and reported, not forced.

## 1. Call sites converted

- **18** `AssertFails(...)` call sites converted to `AssertRecoversWithEnd(...)`.
- (Line 100, `$"""{x}""""`, was already `AssertRecoversWithEnd` from a prior commit and is not counted among the 18.)

### `AssertFails` deleted?

**Yes.** After conversion there were **no remaining** `AssertFails` call sites in the file, so the `AssertFails` helper (former lines 218–227) was deleted. `AssertParses` and `AssertRecoversWithEnd` were left unchanged (not weakened).

## 2. Verification per converted input (the deviation)

A temporary probe parsed all 18 inputs and reported `reachedEnd` / `RecoveryDiagnostics.Count` / recovery-node count. Result:

| Family | start rule | Inputs | Reached EOF + error? |
|---|---|---|---|
| Regular | `InterpolatedStringLiteral` | 4 | **All 4 PASS** (reachedEnd=True, ≥1 recovery node, ≥1 diagnostic) |
| Verbatim | `VerbatimInterpolatedStringLiteral` | 3 | **All 3 PASS** |
| Raw | `RawInterpolatedStringLiteral` | 11 | **All 11 FAIL** (reachedEnd=False, `end=-1`, 0 diagnostics, 0 recovery nodes, `ErrorInfo` set) |

### The 11 inputs that do NOT reach EOF (all Raw)

Observed for each: `reachedEnd=False`, `RecoveryDiagnostics.Count=0`, `recoveryNodes=0`, `errorInfo=set` — i.e. a **hard `Failure`** (not even a `Partial`), so the S6 bottom / absorber never engages to produce a recovery-to-EOF.

| Line | Input |
|---|---|
| 101 | `$"""{{x}}""""` |
| 102 | `$"""{x""""` |
| 117 | `$$"""{{{{x}}}}"""` |
| 118 | `$$"""{{x}"""` |
| 119 | `$$"""{x}}"""` |
| 136 | `$$$"""{{{{{{x}}}}}}"""` |
| 137 | `$$$"""{{{x}}"""` |
| 139 | `$$$"""..{{{{{43}}}}}}.."""` |
| 161 | `$$$$"""{{{{{{{{x}}}}}}}}"""` |
| 163 | `$$$$"""{{{{x}}}}}}}}"""` |
| 165 | `$$$$$"""{{{{{x}}}"""` |

Note the pre-existing Raw case on line 100, `$"""{x}""""` (a *complete hole* `{x}` followed by a stray quote), **does** recover to Success@EOF. The 11 failing Raw inputs instead involve in-string brace problems (brace-run > D+1 → CS9006/CS9007, unterminated holes, or literal `{{...}}` content), and for these the parser returns a hard `Failure` rather than reaching the S6 bottom.

**Why (hypothesis, not root-caused — parser/recovery engine were out of scope and must not be modified here):** the 1.3.2 guarantee "the parser now ALWAYS reaches EOF on any input (S6 absorbs the trailing region)" does **not** hold for the Raw interpolated-string family. These inputs produce a terminal `Failure` (no `Partial`, no recovery candidate) before S6's absorber can be applied, so no `RecoveryDiagnostic` and no `IsRecovery`/`IsAbsorber` node is produced. This is a genuine gap in the S6 guarantee for raw strings, not a test-side artifact.

**Action taken:** the 11 were kept as `AssertRecoversWithEnd` (the intended reached-EOF+error contract) and NOT reverted to `AssertFails` and NOT forced. This is reported as a deviation; resolving it requires a parser/recovery-engine change (out of scope for this sub-point).

## 3. ParserTests recovery-test audit (report only — no ParserTests file modified)

Directory: `Tests/ParserTests/Recovery/` (23 files; `UnrecoveredTests.cs` does **not** exist).

### Already assert reached-EOF (`Success@EOF` / `end == input.Length`)

- **S6BottomTests.cs** — yes. Multiple `Assert.AreEqual(input.Length, end, "S6 bottom must reach EOF")` (+ absorber `EndPos == input.Length`).
- **RecoveryCorpusTests.cs** — yes. `successAtEof = result.TryGetSuccess(...) && end == input.Length`, then `Assert.IsTrue(m.SuccessAtEof, ...)` (e.g. D1.1 1.3.2 acceptance).
- **RecoveryPerfTests.cs** — yes. `Assert.IsTrue(mN.SuccessAtEof, ...)` for N = 1, 5, 10, 20.
- **AbsorberPlacementTests.cs** — yes. `Assert.IsTrue(result.TryGetSuccess(...) && end == input.Length, "Expected Success@EOF...")`.
- **S0IntegrationTests.cs** — yes. `Assert.AreEqual(input.Length, end)`.
- **OftenMissedTests.cs** — yes. `Assert.AreEqual(input.Length, end)`.
- **FinalStateTests.cs** — yes. `Assert.AreEqual(input.Length, end, "S6 bottom must reach EOF")` and `Assert.IsTrue(... && end == input.Length, "S6 bottom must reach EOF")`.
- **PatchRollbackTests.cs** — yes. `Assert.AreEqual(input.Length, end)` and `Assert.IsTrue(... && end == input.Length, "Expected Success@EOF...")`.
- **IterativeRecoveryTests.cs** — yes for positive cases (`Assert.AreEqual(input.Length, end)`); also contains 2 negative controls (`Assert.IsFalse(... && end == input.Length)`).
- **T1AnchorReproTests.cs** — yes. `Assert.IsTrue(... && end == input.Length, "Expected Success@EOF via S2 resync...")`.
- **StackGuardTests.cs** — yes. `Assert.IsTrue(... && end == input.Length, "Expected Success@EOF (recovered, no crash)...")`.
- **RecoveryRuleTests.cs** — **partial.** The `recoverable: true` case asserts reached-EOF (`Assert.IsTrue(ok.TryGetSuccess(...) && endOk == input.Length)`). The `recoverable: false` case is a **negative control** (`Assert.IsFalse(strict.TryGetSuccess(...) && endStrict == input.Length)`) — this negative control is the **pre-existing failure** (see §5).

### Do NOT assert reached-EOF

- **BudgetReachabilityTests.cs** — no. Asserts `Skipped` diagnostics are present; comment explicitly defers full Success@EOF to task 3.0c.
- **RecoveryDiagnosticTests.cs** — no. Asserts `RecoveryDiagnostics.Count == 0` (`#if RECOVERY`).
- **PartialBaseCaseTests.cs** — no. Asserts `LastPartial` / tie-break behavior, `newPos == 4` (`#if RECOVERY`).
- **ParseContextTests.cs** — no. `ParseContext`/`Result`/`TryGetPartial` mechanics (`#if RECOVERY`).
- **TerminalCacheTests.cs** — no. Terminal-caching unit test.
- **CostModelTests.cs** — no. `CostCalculator` unit test.
- **CandidateGenerationTests.cs** — no. Candidate generation (`c.Pos`, `c.EndPos`, injections).
- **SnapshotTests.cs** — no. Snapshot unit test.
- **StackFrameTests.cs** — no. Stack-frame unit test.
- **FollowSetTests.cs** — no. Follow-set computation.
- **FirstSetsTests.cs** — no. First-set computation.

## 4. Test results

### `dotnet build Tests/CSharpGrammarTests/CSharpGrammarTests.csproj`
**0 errors, 0 warnings.**

### `dotnet test Tests/CSharpGrammarTests/CSharpGrammarTests.csproj`
**Failed: 4, Passed: 1514, Skipped: 3, Total: 1521.**

The 4 failing test methods (each stops at its first failing Raw assertion):
- `Raw1_InvalidFragments_Reject` (line 101)
- `Raw2_InvalidFragments_Reject` (line 117)
- `Raw3_InvalidFragments_Reject` (line 136)
- `Raw4Plus_InvalidFragments_Reject` (line 161)

These correspond to the 11 failing Raw inputs in §2 (MSTest halts a method at its first failed assert, so only the first Raw assert per method is reported; the probe confirmed all 11 Raw inputs fail).

### `dotnet test Tests/ParserTests/ParserTests.csproj`
**Failed: 1, Passed: 337, Skipped: 2, Total: 340.**

The 1 failure is **pre-existing and unrelated to this change** (see §5).

## 5. Deviations

1. **11 converted inputs do not reach EOF (all Raw family)** — contrary to the task's expectation that "all reach EOF via S6 with an absorber node." They produce a hard `Failure` (`end=-1`, no `RecoveryDiagnostic`, no recovery node, `ErrorInfo` set). Per the task's "STOP and report ... do not force it," they were left as `AssertRecoversWithEnd` and reported, not reverted and not forced. This is a real gap in the 1.3.2 S6 guarantee for raw interpolated strings and needs a parser/recovery-engine fix (out of scope here).

2. **ParserTests is not "0 failed" as the task expected.** One test fails: `Recovery.RecoveryRuleTests.Test_Recoverable_False_Disables_Epsilon_Acceptance` (RecoveryRuleTests.cs:296). Verified **pre-existing**: it fails identically with this change stashed (re-run in isolation against the stashed tree). It is a negative-control test asserting that with `Recoverable=false` the input does NOT reach EOF — broken by the 1.3.2 "S6 guaranteed bottom" change at HEAD, not by this sub-point's test conversion. No ParserTests file was modified.

## Constraints honored
- Only `Tests/CSharpGrammarTests/InterpolatedStringTests.cs` was modified.
- `AssertParses` and `AssertRecoversWithEnd` were not weakened.
- No parser / recovery-engine / ParserTests changes.
- Not committed.

---

# Remaining part (post-1.5): ParserTests recovery-test purpose audit

Task: for each test in the "Do NOT assert reached-EOF" list (§3), determine its purpose —
(A) verifies a recovery scenario reaches EOF → add reached-EOF + error assertion;
(B) tests a recovery mechanism in isolation → leave as-is.

## Audit result: all 11 files are category (B) — no code changes

| File | Category | Purpose (verified by reading the file) | Action |
|---|---|---|---|
| `BudgetReachabilityTests.cs` (2 tests) | B | Subject = *budget reachability*: with an explicit `MaxRecoveryAttemptsPerPosition=16`, a rank-2/3/5 candidate is attempted and accepted (progress) → asserts ≥1 `Skipped` diagnostic. Test names are `S2Resync_Reachable` / `S3S5_Reachable`; comments explicitly defer full `Success@EOF` to task 3.0c (absorber). Reaching EOF is not the point. | Left as-is |
| `RecoveryDiagnosticTests.cs` (2 tests) | B | Recovery disabled (`MaxRecoveryIterations=0`); asserts `RecoveryDiagnostics` is non-null and `Count == 0` after a successful and a failed *unrecovered* parse. Subject = diagnostics container behavior, not recovery-to-EOF. | Left as-is |
| `PartialBaseCaseTests.cs` (5 tests) | B | Subject = Partial mechanics: `LastPartial` produced by ParseSeq base case (context, `newPos`), ε-loop does not hang, Success-vs-Partial tie-break at equal/greater length, valid input produces no Partial. | Left as-is |
| `ParseContextTests.cs` (17 tests) | B | Subject = parse context: `ParseContext`/`Result.Partial`/`TryGetPartial`/`WithPrefixOnly` structure, frame-location types, expected terminals, nested contexts, `ErrorPos` on recovery-disabled failed parses. | Left as-is |
| `TerminalCacheTests.cs` (4 tests) | B | Subject = terminal cache: mismatch result cached across alternatives (TryMatch called once), injection overrides cache, `TerminalComparer` literal-by-value/instance-by-reference, EOF singleton identity. | Left as-is |
| `CostModelTests.cs` (9 tests) | B | Subject = cost model: `CostCalculator.SkipCost`/`InsertCost`/`TierPenalty`/`CountRecoveryNodes` unit tests + one integration check that a manually generated S5 candidate's cost matches the formula. | Left as-is |
| `CandidateGenerationTests.cs` (10 tests) | B | Subject = candidate generation: calls `RecoveryEngine.Generate` directly on *unrecovered* parses (`MaxRecoveryIterations=0`); asserts candidate ids/pos/cost/diagnostics, `Apply`/`Rollback` injections, sort order, determinism. | Left as-is |
| `SnapshotTests.cs` (6 tests) | B | Subject = snapshot contents: `FailureSnapshot` at farthest mismatch (pos, failed terminal, stack top, expected), farthest-not-first, speculative And/Not predicates do not move ErrorPos / pollute Expected, successful parse leaves no snapshot. | Left as-is |
| `StackFrameTests.cs` (4 tests) | B | Subject = stack frames: `CurrentStackFrames` depth/locations/Expected captured during *successful* parses, empty after success, empty after exception, loop iteration indices. | Left as-is |
| `FollowSetTests.cs` (24 tests) | B | Subject = follow-set computation: `FollowSetCalculator` first/follow sets (optional, separated list, TDOPP, often-missed, literal identity, MiniC grammar, deep nesting, `GetTerminators` ordering/dedup/EOF-at-end/options override). No parsing to EOF involved. | Left as-is |
| `FirstSetsTests.cs` (16 tests) | B | Subject = first-set computation: `FirstSets.Get`/`IsNullable` per rule kind (terminal, seq nullable fallthrough, loops, optional, often-missed, predicates, separated list, ref with/without calculator, TDOPP). | Left as-is |

Note: 8 of the 11 file names are literally the task's own category-(B) examples
("candidate generation, snapshot contents, follow-set computation, budget reachability,
cost model, terminal cache, parse context, stack frame"); the remaining 3
(`RecoveryDiagnostic`, `PartialBaseCase`, `FirstSets`) are likewise mechanism-in-isolation tests.

Cross-check of the audit's other half (grep for `TryGetSuccess` / `end == input.Length` in
`Tests/ParserTests/Recovery/`): exactly the 12 files listed in §3 "Already assert reached-EOF"
contain reached-EOF assertions (`RecoveryRuleTests` partial: positive case line 293 and the
1.5 re-scoped strict case line 301), and exactly the 11 files above do not. The prior audit is accurate.

**No category (A) test was found**, therefore no reached-EOF/error assertion was added, and no
category (A) test failed to reach EOF (nothing to STOP on).

## Verification (this pass)

- `dotnet build Tests/ParserTests/ParserTests.csproj` — **0 errors**.
- `dotnet test Tests/ParserTests/ParserTests.csproj` — **Total: 340 · Passed: 338 · Failed: 0 · Skipped: 2** (matches the 1.5 baseline "ParserTests 338/0/2").

## Deviations (this pass)

None. No test file was modified (all 11 audited files are category B). No parser/recovery-engine changes. Not committed.
