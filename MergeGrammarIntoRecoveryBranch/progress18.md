# progress18 — Fix C: re-anchor S2 resync to loop-iteration start

Branch: `error-recovery-completion-2` @ 2ad4a7e (fixes A+B already committed). Date: 2026-10-11.
Spec: `MergeGrammarIntoRecoveryBranch/progress-f9-plan.md` §Q4/§Q5; trace evidence: `progress-s2-after.md`.

## Intentions (written BEFORE coding)

**Defect:** when parsing fails INSIDE a loop element (not at its start), the S2 candidate injects at `e`
(branch 3) instead of patching the loop-element rule's memo at the iteration start; recovery cannot return
to the iteration start and falls through to S6 absorb-to-EOF.

**Plan:**
1. Explore current code with Roslyn MCP tools: `LoopFrameLocation` (StackFrame.cs), all its construction
   sites in Parser.cs (`ParseZeroOrMany`, `ParseOneOrMany`, `ParseSeparatedList`, `ParseRepeat`,
   `ParseContextScope`, possibly more), all consumers, and `AddResyncCandidate` in RecoveryEngine.cs
   (branch structure, how `parseEnd` is available, how the snapshot chain is walked).
2. `StackFrame.cs`: extend `LoopFrameLocation` with `IterationStartPos` (init property, default -1 —
   same shape as existing `Element { get; init; }`; no new FrameLocation variant).
3. `Parser.cs`: pass the iteration-start position (`currentPos` at the `WithFrame(new LoopFrameLocation(...))`
   invocation) at EVERY push site.
4. `RecoveryEngine.cs` `AddResyncCandidate`: new branch C AFTER branch 1 (`topIdx==0 && top.RuleName==anchorName`)
   and branch 2 (`failedElement is Ref`), BEFORE branch 3 (injection). Order: 1 → 2 → C → 3.
   Trigger (all parts):
   - snapshot stack contains a `LoopFrameLocation` in the enclosing chain of `top`;
   - `top` is NOT at element index 0 (branch 1 did not fire);
   - the loop element is a `Ref` whose name matches `top.RuleName` or the anchor/loop element rule;
   - CONSERVATIVE PREDICATE: `parseEnd == loop.IterationStartPos` — the failed iteration consumed nothing,
     recovery is genuinely at the iteration start.
   Action: `PatchMemo(<loop-element rule name>, loop.IterationStartPos, Success(Skipped[iterStart..resyncPos), resyncPos, resyncPos))`
   — same value shape as branch 1.
5. Update every consumer of `LoopFrameLocation` so the build stays clean.

**Known-pin rule:** the 4 pins (`FirstWordSpanTests` ×2 absorber-start, `AnchorResyncTests` ×2 candidate cost)
must stay green WITH the predicate. If any fails → STOP, no re-pinning, report verbatim.

**Forbidden:** the `e2 > e` accept criterion in `Parser.Recovery.cs` (HARMFUL, 7 regressions); fixes A/B;
any other test file; `.csproj`; committing; stopping the Roslyn MCP server.

**Verification (each ONCE, repo root, in order):**
1. `dotnet build Tests/ParserTests --no-incremental` → 0 errors.
2. `dotnet test Tests/ParserTests --no-build` → 495/0/5.
3. `dotnet test Tests/CSharpGrammarTests --no-build` → 1539/0/4.
4. Remove `[Ignore]` from `UsingStaticRecoveryTests.Test_UsingStatic_GarbageNotAbsorbedToEof`, run filtered.
   Expected: Verdict=Recovered, AbsorberBytes≈22, AbsorberFraction≈36%. Pass → leave removed; fail → restore
   `[Ignore]` byte-exact, STOP, report.
5. Corpus probe — out of scope.

## Work log

### Exploration findings (before coding)

- Push sites of `LoopFrameLocation` in production: exactly 6, all in `Parser.cs` —
  `ParseOneOrMany` (675 first, 696 subsequent, 724 loopCtx), `ParseZeroOrMany` (745, 773 loopCtx),
  `ParseRepeat` (799). `ParseSeparatedList`/`ParseContextScope` do NOT push loop frames directly
  (SeparatedList transforms into loop rules; ContextScope pushes `SeqFrameLocation`).
- Consumers of `LoopFrameLocation` (pattern-matching on `Element`/`LoopKind`/`Iteration`, unaffected by
  an added init property): `FollowSetCalculator.cs:288`, `Parser.RecoveryClustering.cs:55-56`,
  `RecoveryEngine.cs:95-96, 250, 605, 642, 1064`; tests construct it with 2 positional args
  (`ParseContextTests`, `FollowSetPerSiteTests`, `SpeculativeProbeDerivationTests`) and pattern-match
  (`StackFrameTests`) → adding `IterationStartPos { get; init; } = -1` (same shape as `Element`) keeps
  the build clean; test-constructed frames keep the -1 sentinel.
- `parseEnd` at the `Recover`→`Generate` call site (`Parser.Recovery.cs:883`): first-pass Success/Partial
  NewPos, else 0. `GenerateS2` does not currently receive it → thread it through (call site line 64).
- Pin analysis with the predicate `parseEnd == loop.IterationStartPos`:
  - `FirstWordSpanTests.Test_S3_Region_...`: first pass = Failure → parseEnd=0; failed loop iteration
    (Stmt iter 0) started at 2 → 0≠2 → C does not fire. Green.
  - `FirstWordSpanTests.Test_S2_RegionWithInsertion_...`: first pass Module=ZeroOrMany(Function)
    Success@0 → parseEnd=0; nearest loop = inner Statement loop iterStart=12 → 0≠12 → no fire;
    outer Function loop excluded by name check (Ref "Function" ≠ top.RuleName "Statement"). Green.
  - `AnchorResyncTests` T1/T2: direct `Generate(..., parseEnd: e=18)`; nearest loop iterStart=12 →
    18≠12 → no fire. Green.
  - UsingStatic @ e=21 (with A+B): snapshot@21 top.RuleName="NamespaceMember" (ClassDeclaration Seq
    frame, element 2 — speculative NotPredicate mismatch at 21 is suppressed by `SuppressSideEffects`,
    so the pass-0 snapshot survives, `SnapshotAt(e)` matches pos==e); nearest loop frame
    ZeroOrMany(NamespaceMembers) iter 1, IterationStartPos=15; parseEnd=15 → C fires →
    PatchMemo(NamespaceMember, 15, Success(Skipped[15..37),37,37)).
- Match form for the loop element Ref: `element.RuleName == top.RuleName || element.RuleName == loopFrame.RuleName`
  (the "enclosing loop rule" = the loop frame's rule name = loop Kind). Matching on `anchorName` alone
  would re-break FirstWordSpan test 4 (outer Function loop, anchor "Function", iterStart=0=parseEnd).
  Only the NEAREST loop frame is considered (conservative).
- Diagnostic: branch C's skipped region is [iterStart..resyncPos) → use iterStart as the FirstWordSpan
  region start for C (branches 1/2/3 keep `e`); no pinned test fires C, so no expectation moves.

### Implementation

1. `StackFrame.cs`: `IterationStartPos { get; init; } = -1` on `LoopFrameLocation`.
2. `Parser.cs`: pass `IterationStartPos = currentPos` at all 6 push sites (loopCtx sites: `currentPos`
   after the loop break == start of the stopped iteration).
### Verification results (each run ONCE, repo root)

1. `dotnet build Tests/ParserTests --no-incremental` → **Build succeeded, 0 Error(s)**.
2. `dotnet test Tests/ParserTests --no-build` → **Failed: 0, Passed: 495, Skipped: 5, Total: 500**
   (expected 495/0/5 ✓; the 4 known pins — FirstWordSpan ×2, AnchorResync ×2 — stayed green WITH the
   predicate; no re-pinning needed).
3. `dotnet test Tests/CSharpGrammarTests --no-build` → **Failed: 0, Passed: 1539, Skipped: 4, Total: 1543**
   (expected 1539/0/4 ✓).
4. `[Ignore]` removed from `Test_UsingStatic_GarbageNotAbsorbedToEof`;
   `dotnet test Tests/ParserTests --no-build --filter "FullyQualifiedName~UsingStatic"` →
   **Failed: 0, Passed: 1, Skipped: 0, Total: 1 — PASSED**. Actual health (via temporary
   `Console.WriteLine` probe, reverted byte-exact after capture, SHA256
   `1A1A1821B1DD8ED750D3B27F3FBE58394B8FF3335845629D27501C7359F844C5` verified):
   `ParseHealthReport { SuccessAtEof = True, AbsorberBytes = 22, AbsorberFraction = 36, UnionCoverage = 60, RecoveryNodes = 1, CoversInput = True, Verdict = Recovered }`
   — matches the research expectation (Recovered, 22B, 36%). `[Ignore]` LEFT REMOVED (goal of C).
5. Corpus probe — out of scope, not run.

### Push sites updated (Parser.cs, post-edit line numbers)

- `ParseOneOrMany`: 675 (first element), 696 (subsequent), 724 (loopCtx) — `IterationStartPos = currentPos`
- `ParseZeroOrMany`: 745 (iteration), 773 (loopCtx)
- `ParseRepeat`: 799 (iteration i)
(`ParseSeparatedList`/`ParseContextScope` push no `LoopFrameLocation` — nothing to update there.)

### Final state

- Modified: `ExtensibleParser/Recovery/StackFrame.cs`, `ExtensibleParser/Parser.cs` (6 push sites),
  `ExtensibleParser/Recovery/RecoveryEngine.cs` (parseEnd threading + branch C + `FindLoopReanchor`),
  `Tests/ParserTests/Recovery/UsingStaticRecoveryTests.cs` (`[Ignore]` removal only).
- Not touched: `Parser.Recovery.cs` (accept criterion `e2 > e` untouched), fixes A/B, all other tests,
  `.csproj`. No commits made.
- Assessment: C closes F9 for the UsingStatic input (with A+B): at e=21 the engine now emits
  `PatchMemo(NamespaceMember, 15, Success(Skipped[15..37),37,37))`, the loop resumes at 37 and parses
  `namespace N;` + `class C {}` to EOF. Expected to help on any input where a loop element fails
  internally after a spurious-progress accept moved the recovery point inside the element, provided the
  failed iteration consumed nothing (predicate). No regressions in ParserTests/CSharpGrammarTests.
