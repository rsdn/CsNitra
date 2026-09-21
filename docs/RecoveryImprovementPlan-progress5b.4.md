# Progress — 5b.4.1 (A5-1 bounded speculative probe — infrastructure)

Declaration/derivation only. The derived predicate set is computed and unit-tested; it is
**not** wired into S2 candidate generation (that is 5b.4.2/5b.4.3). No recovery behavior
changed.

## (a) `SoftDepth` declaration

Added to `RecoveryOptions` (`ExtensibleParser/Rules.cs:339-342`), per the plan
(`RecoveryImprovementPlan.md` A5-1 row: "K=2 по умолчанию, настраиваемо —
`RecoveryOptions.SoftDepth`"):

```csharp
/// Bounded speculative probe accept depth (A5-1): a probe is accepted when the speculative
/// parse consumes at least this many tokens (K, default 2). Declaration only — the probe is
/// not yet wired into S2 candidate generation (5b.4.2/5b.4.3).
public int SoftDepth { get; init; } = 2;
```

`RecoveryOptions` is the per-rule recovery-options record (the plan names it explicitly);
it is read from stack frames via the existing `StackFrame.Options` plumbing. Default `2`.

## (b) Derived predicate set

New method in `ExtensibleParser/Recovery/RecoveryEngine.cs:448-492` (after
`DeriveLoopAnchors`, which is kept intact and still used by S2/S3/S6):

```csharp
public static List<Ref> DeriveProbePredicates(Parser parser, FailureSnapshot snapshot)
```

INCLUDE/EXCLUDE logic:

- **INCLUDE — top frame's rule if it is a Ref** (`RecoveryEngine.cs:475-479`): each `Ref`
  alternative of `parser.Rules[snapshot.Stack[^1].RuleName]`. A rule that "is a Ref" (an
  alias/union rule, e.g. C# grammar `Grammar = CompilationUnit;`, `Statement = | Block | …`)
  contributes all its Ref alternatives; a rule with no Ref alternatives contributes nothing.
- **INCLUDE — loop-body Refs** (`RecoveryEngine.cs:482-491`): existing
  `DeriveLoopAnchors` behavior, for every `LoopFrameLocation` frame, walked top→bottom.
- **EXCLUDE — pure regex-First** (`RecoveryEngine.cs:507-513`, `IsPureRegexFirst`): a rule
  whose First set (via `FirstSets.Get(ref, parser.FollowCalculator)`) is exactly one
  non-`Literal` terminal (a catch-all regex, e.g. `Identifier` — matches almost anything;
  covered by S3/anchor-First per A5-7). A single-`Literal` First is NOT excluded.
- **EXCLUDE — TDOPP frames** (`RecoveryEngine.cs:501-504`, `IsExcludedFrame`):
  `frame.Location is PostfixFrameLocation` (the A5-6 boundary: "TDOPP-кадры =
  PostfixFrameLocation"). A `RuleFrameLocation` of a TDOPP rule is not a TDOPP frame.
- **EXCLUDE — ContextScope frames** (`RecoveryEngine.cs:501-504`): `ParseContextScope`
  pushes `SeqFrameLocation(0/1)` frames that inherit the enclosing rule name
  (`Parser.cs:781-799` + `PushFrame`, `Parser.Recovery.cs:271-276`) — no dedicated
  `FrameLocation` exists, so the rule definition is the only marker available in a
  snapshot: any frame of a rule whose definition contains a `ContextScope`
  (`alt.GetSubRules<ContextScope>().Any()`) is treated as a ContextScope frame
  (conservative: fewer probes, no false candidates; mirrors the A5-6 rule-level fallback).
- **Dedup + ordering**: by rule name (`StringComparer.Ordinal`), internal frames before
  external (top→bottom): top-frame Refs first, then loop frames top→bottom.

Empty snapshot stack → empty result (no throw).

## (c) Test

New file `Tests/ParserTests/Recovery/SpeculativeProbeDerivationTests.cs` (7 tests;
hand-built `FailureSnapshot`s/stacks in the `FollowSetPerSiteTests` style; the parser is
built with `BuildTdoppRules()` so `FollowCalculator` — and therefore First sets — exist;
single `BuildTdoppRules` call means no rule inlining, so `Ref`s survive as written):

1. `Test_LoopBodyRef_Is_Included` — loop-body Ref (`Body := "[" Item* "]"`) is included.
2. `Test_TopFrameRef_Is_Included` — top frame's rule that is a Ref (`Start := Ref("Outer")`)
   is included.
3. `Test_PureRegexFirst_Is_Excluded` — `IdentLoop := Ident*` with
   `First(Ident) = { Identifier }` (single catch-all regex terminal) → excluded (empty set).
   (Contrast: single-`Literal`-First rules are included — tests 1/2.)
4. `Test_TdoppFrame_Is_Excluded` — top frame `PostfixFrameLocation` of a TDOPP rule →
   excluded; contrast: `RuleFrameLocation` of the same rule → its Ref prefix IS derived.
5. `Test_ContextScopeFrame_Is_Excluded` — ContextScope body frame
   (`SeqFrameLocation(1)` of a rule containing a `ContextScope`) → excluded.
6. `Test_Dedup_And_TopFirstOrdering` — top-frame Ref duplicates the inner loop body:
   top-frame Ref wins the first position, the duplicate is dropped, order is
   top→bottom (`["Body", "Item"]`).
7. `Test_SoftDepth_Default_Is_2` — `RecoveryOptions.SoftDepth` default == 2.

## (d) Results

- `SpeculativeProbeDerivationTests`: **7/7 passed** (`dotnet test Tests/ParserTests
  --no-build --filter "FullyQualifiedName~SpeculativeProbeDerivationTests"`).
- `Tests/ParserTests` (full, `--no-build` against the green build): **385 total, 383
  passed, 0 failed, 2 skipped**.
- `Tests/CSharpGrammarTests`: **1527 total, 1524 passed, 0 failed, 3 skipped**.
- `Tests/CsPreprocessorTests`: **128 total, 128 passed, 0 failed**.

### STOP flag — one-shot `dotnet test Tests/ParserTests` (with build) is blocked by an
external file change, not by this work

At 15:11:32 (the same second the new test file was created) an
`<Compile Include="Recovery\SpeculativeProbeDerivationTests.cs" />` line appeared in
`Tests/ParserTests/ParserTests.csproj` (line 14). It is **not** in HEAD, was **not** added
by this task (no `.csproj` was touched), and is redundant — the SDK default glob already
includes the file. With both the explicit Include and the default glob present, MSBuild
keeps two `Compile` items with identical `ItemSpec` (verified via
`dotnet msbuild -getItem:Compile`), and the SDK's `CheckForDuplicateItems` task fails every
build of `ParserTests` with **NETSDK1022** — so the specified one-shot
`dotnet test Tests/ParserTests` (which builds) cannot go green. A solution build with the
final code had succeeded before the line took effect; all builds after it fail with
NETSDK1022. The task forbids editing any `.csproj`, so this was reported instead of fixed.
Fix (one line, for the orchestrator to approve/apply): delete the explicit
`<Compile Include="Recovery\SpeculativeProbeDerivationTests.cs" />` from
`Tests/ParserTests/ParserTests.csproj`.

## (e) Files changed (no commit)

- `ExtensibleParser/Rules.cs` — `RecoveryOptions.SoftDepth` (declaration, default 2).
- `ExtensibleParser/Recovery/RecoveryEngine.cs` — `DeriveProbePredicates` +
  `IsExcludedFrame` + `IsPureRegexFirst` (derivation only; `DeriveLoopAnchors` untouched;
  S1/S2/S3/S4/S5/S6, `FollowSetCalculator`, accept semantics untouched).
- `Tests/ParserTests/Recovery/SpeculativeProbeDerivationTests.cs` — new (7 tests).
- `docs/RecoveryImprovementPlan-progress5b.4.md` — this file.

No `.csproj` modified by this task. No commit.

## 5b.4.2 (basic wiring)

D1 only: the derived probe predicates from 5b.4.1 (`DeriveProbePredicates`) are now wired
into `GenerateS2` candidate generation, feeding BOTH tiers. No other decision (D2–D5) is
implemented; no acceptance semantics, `Speculative`, tier labels, diagnostics,
`FirstMatchesAt` pre-filter, `AddResyncCandidate`, or other strategy (S0/S1/S3/S4/S5/S6)
changed.

### T1 `anchors` build (`RecoveryEngine.cs:179-194`)

Before: author anchors (`NearestOptions(..., f => f.Options?.Anchors)`, `Ref`-only) followed
by a direct `DeriveLoopAnchors` walk over `LoopFrameLocation` frames, deduped by a
`HashSet<string>` (`seen`, `StringComparer.Ordinal`).

After: author anchors FIRST (unchanged, `Ref`-only filter kept); each author anchor's rule
name is seeded into `seen`. Then the output of `DeriveProbePredicates(parser, snapshot)`
(top-frame Ref alternatives + loop-body Refs, top→bottom, internally deduped and filtered)
is appended, skipping any predicate whose rule name is already in `seen` (`seen.Add`
returns false → author wins). The separate `DeriveLoopAnchors` walk in `GenerateS2` is
removed — `DeriveProbePredicates` is a strict generalization of it (same loop-body
behavior, plus top-frame Ref alternatives, minus pure-regex-First / TDOPP / ContextScope).
`DeriveLoopAnchors` itself is untouched and still used by `DeriveProbePredicates` and S3/S6.

### T2 `canStart` build (`RecoveryEngine.cs:196-210`)

Before: author CanStart only (`NearestOptions(..., f => f.Options?.CanStart)`, `Ref`-only);
a comment said derived T2 "don't exist".

After: author CanStart FIRST (unchanged, `Ref`-only filter kept); each author CanStart rule
name is seeded into a second `HashSet<string>` (`seenCanStart`, `StringComparer.Ordinal`).
Then the `DeriveProbePredicates(parser, snapshot)` output is appended, deduped by rule
name against the author CanStart names (author wins). Derived T2 now exist.

### Dedup/ordering

- Both lists: author predicates first (in their original `NearestOptions` order), followed
  by the `DeriveProbePredicates` output in its own top→bottom order.
- Dedup is by rule name via `HashSet<string>` with `StringComparer.Ordinal` (existing
  convention); an author predicate's rule name suppresses a same-named derived predicate.
  Author predicates are not deduped against each other (unchanged behavior).

### Build result

- `dotnet build ExtensibleParser/ExtensibleParser.csproj`: **succeeded, 0 errors**
  (`--no-incremental`, Debug/x64).
- `roslyn_get_diagnostics_for_file` on `ExtensibleParser/Recovery/RecoveryEngine.cs`:
  no compiler errors or warnings.

### Files changed (no commit)

- `ExtensibleParser/Recovery/RecoveryEngine.cs` — `GenerateS2` T1/T2 list builds only.
- `docs/RecoveryImprovementPlan-progress5b.4.md` — this file.

No `.csproj` modified. No commit. (A pre-existing one-line modification of
`docs/RecoveryImprovementPlan-checklist.md` from the 5b.4.1 session was left as-is.)

### 5b.4.2 test (A5-1, basic) — `DerivedProbePredicateTests`

New file `Tests/ParserTests/Recovery/DerivedProbePredicateTests.cs` (3 tests). No production
code and no `.csproj` touched by this sub-point.

#### Scenario

Grammar (NO author `Anchors`, NO author `CanStart` — no `RecoveryOptions` anywhere, so any S2
resync MUST come from `DeriveProbePredicates`):

```
Module = ZeroOrMany(Ref("Stmt"), "Stmts")
Stmt   = Seq(Literal("let"), Ref("Expr")) | Ref("Expr")   ← top-frame Ref alternative: Expr
Expr   = Seq(Literal("var"), Ident, Literal(";"))
```

Input (E2E test): `"var a; ### var b;"`.

Why the resync requires a derived predicate: the main parse fails at `e=7` — the furthest
mismatch is `Literal("let")` (Stmt alt 0, Seq element 0), so the snapshot top frame is
`("Stmt", SeqFrameLocation(0))`. `DeriveProbePredicates` then yields `[Expr, Stmt]`:
`Expr` is the top-frame Ref alternative (the NEW 5b.4.2 T1 capability — before 5b.4.2 only
loop-body Refs were derived), `Stmt` is the loop-body Ref (the pre-5b.4.2 `DeriveLoopAnchors`
anchor). The S2 scan finds `Expr` at 11 (full speculative parse of `"var b;"` → 17) and
emits the T1 resync `S2:Stmt:T1:11` with the `"let"` absorber over `[7..11)`. The re-parse
continues alt 0 from 11 (`"let"`-absorbed + `Expr` = `"var b;"`) and reaches EOF in ONE
accepted S2 candidate — no S6 floor, no Unrecovered. The resync point (11) is exactly where
the derived predicate `Expr` starts; with no author annotations, nothing else in the grammar
could have driven it.

#### Tests

1. `Test_S2_Resync_Driven_By_Derived_TopFrameRef_Alternative` (E2E):
   - `Success@EOF` + `ErrorInfo == null`;
   - exactly one `Skipped` `"resync point"` diagnostic, span `[7..10)` (first word `"###"`),
     message contains `"resync point 11"`;
   - driving-anchor assertion via `GrammarDiagnostics`:
     `AnchorUsage("Expr") == 1` (the derived top-frame Ref alternative drove the resync —
     `NoteAnchorUse` is called in `AddResyncCandidate` per driving anchor),
     `AnchorUsage("Stmt") == 0` (the pre-5b.4.2 loop-body anchor did NOT drive it),
     `UsedAnchors.Count == 1`.
2. `Test_DeriveProbePredicates_TopFrameRefAlt_Before_LoopAnchor`: on the REAL failure
   snapshot of the same input (`MaxRecoveryIterations = 0` so no recovery interferes):
   snapshot `Pos == 7`, top frame `("Stmt", SeqFrameLocation { ElementIndex: 0 })`, and
   `RecoveryEngine.DeriveProbePredicates(parser, snapshot)` == `["Expr", "Stmt"]`
   (top-frame Ref alt first, loop-body Ref second).
3. `Test_Valid_Input_Parses_Cleanly_Without_Recovery` (control): `"var a; var b;"` on the
   same grammar → clean `Success@EOF`, `ErrorInfo == null`, `RecoveryDiagnostics` empty,
   zero anchor usage.

#### Results

- Filtered: `dotnet test Tests/ParserTests --no-build --filter
  "FullyQualifiedName~DerivedProbePredicate"` → **3/3 passed** (against the 5b.4.2
  `ExtensibleParser.dll`, verified by behavior — see STOP flag 2 below on the stale-DLL
  trap).
- `dotnet test Tests/ParserTests --no-build` (full) → **437 total, 430 passed, 5 failed,
  2 skipped** — the 5 failures are PRE-EXISTING 5b.4.2 regressions, not caused by this test
  (see STOP flag 1).
- `dotnet test Tests/CSharpGrammarTests --no-build` → **1527 total, 1524 passed, 0 failed,
  3 skipped** (green, matches the 5b.4.1 baseline).
- `dotnet test Tests/CsPreprocessorTests --no-build` → **128 total, 128 passed, 0 failed**
  (green).

#### STOP flag 1 — 5 pre-existing failures in `Tests/ParserTests` (5b.4.2 regression)

`dotnet test Tests/ParserTests` (full) cannot be 0-failed with the current working tree,
for reasons unrelated to `DerivedProbePredicateTests`:

- Failing tests (all in `Tests/ParserTests/Recovery/`):
  `S2TriviaJumpTests.S2TriviaJump_SameResync_FewerScannedPositions`,
  `S2TriviaJumpTests.S2TriviaJump_PaddingInsideScanWindow_IsSkipped`,
  `S2IndexOfTests.S2IndexOf_SpecifiedInput_SameResync`,
  `SpecCacheSharedTests.Test_GenerateS2_Writes_To_Shared_SpecCache`,
  `RecoveryMetricsTests.Test_MultiIteration_AcceptRollbackPerStrategy_AndSpecCache`.
- Verified pre-existing: all 5 fail with `DerivedProbePredicateTests.cs` REMOVED from the
  tree, and all 5 PASS against `HEAD`'s `RecoveryEngine.cs` (temporarily swapped in, then
  restored byte-exact). They fail only with the 5b.4.2 `GenerateS2` change.
- Root cause: these tests all use the grammar family
  `Module := '{' ZeroOrMany(Ref("Stmt")) '}', Stmt := Ident ':' Expr ';'` — the derived S2
  anchor is `Ref("Stmt")` with `First(Stmt) = {Ident}`, a single catch-all regex, i.e. a
  PURE REGEX-FIRST. The old code fed `DeriveLoopAnchors` output straight into the S2 anchor
  list (no filter). 5b.4.2 routes it through `DeriveProbePredicates`, whose A5-1 design
  excludes pure-regex-First rules (`IsPureRegexFirst` — "Identifier matches almost
  anything; covered by S3/anchor-First"). Result: `anchors`/`canStart` become empty,
  `GenerateS2` returns early — no S2 scan (`S2ScanPositions == 0`), no speculative parses
  (`SpecCacheMisses == 0`), S3 (panic to `;`/terminator) takes over the repair. Each test
  asserts one of those old S2 behaviors, so it fails.
- This sub-point is constrained to "add test file(s) only; do NOT modify production code;
  do NOT weaken existing tests", so it was reported instead of fixed. For the orchestrator:
  either (a) update the 5 tests to a loop anchor with a Literal-First (e.g.
  `Stmt := 'let' Ident ':' Expr ';'` — the `S2IndexOfTests` file already documents exactly
  this "Keyword" variant for its test 2), or (b) reconsider applying the
  `IsPureRegexFirst` exclusion to loop-body anchors in 5b.4.2 (the A5-1 rationale targets
  top-frame probes; loop-body anchors pre-date it).

#### STOP flag 2 — one-shot BUILD of `Tests/ParserTests` blocked by an externally added
`.csproj` line (repeat of the 5b.4.1 incident)

- At 07:59:37 (after this task's last successful build) a line
  `<Compile Include="Recovery\DerivedProbePredicateTests.cs" />` appeared in
  `Tests/ParserTests/ParserTests.csproj` (line 14). It is not in HEAD, was not added by
  this task (no `.csproj` was touched), and is redundant — the SDK default glob already
  includes the file. With both present, `CheckForDuplicateItems` fails every build of
  `ParserTests` with **NETSDK1022**, so the specified one-shot
  `dotnet test Tests/ParserTests` (which builds) cannot go green.
- Same incident as 5b.4.1 (its STOP flag recorded the identical line appearing for
  `SpeculativeProbeDerivationTests.cs`; that line was later removed by the orchestrator).
- Fix (one line, for the orchestrator to approve/apply): delete the explicit
  `<Compile Include="Recovery\DerivedProbePredicateTests.cs" />` from
  `Tests/ParserTests/ParserTests.csproj`. All results above were produced with
  `--no-build` after a forced (`--no-incremental`) rebuild of `ExtensibleParser`.

#### STOP flag 3 — stale `bin\` DLL trap (environmental, resolved)

During verification a temporary HEAD-swap of `RecoveryEngine.cs` (to prove the 5 failures
are pre-existing) raced the file-restore timestamp with the swap build: the `obj` output of
the HEAD build ended up NEWER than the restored source, so later incremental builds trusted
the stale HEAD `ExtensibleParser.dll` in `bin\` (decompilation confirmed `GenerateS2`
still used `DeriveLoopAnchors`). This made the E2E test fail with
`AnchorUsage("Expr") == 0 / used=[Stmt]` — i.e. it was silently testing HEAD behavior.
Resolved by `roslyn_stop_mcp_server` (the MCP server held file locks on `bin\` DLLs that
also blocked rebuilds) + `dotnet build ExtensibleParser --no-incremental` + re-run:
3/3 green. The source file was verified byte-identical to the pre-swap 5b.4.2 working copy
throughout.

#### Files changed (no commit)

- `Tests/ParserTests/Recovery/DerivedProbePredicateTests.cs` — new (3 tests).
- `docs/RecoveryImprovementPlan-progress5b.4.md` — this file.

No `.csproj` modified by this sub-point. No production code modified. No commit.

### 5b.4.2r — regression fix (Variant A, additive; order refined)

Variant A is the chosen fix: `IsPureRegexFirst` targets the NEW derived predicates (top-frame
Ref alternatives, and future T2 token-acceptance), not a replacement for loop-anchor derivation
— D1 says "in addition to". So `DeriveLoopAnchors` is restored into the S2 T1 anchor list, but
the T1 order is refined to **author anchors → `DeriveProbePredicates` → `DeriveLoopAnchors`
loop**. The reason for the refinement is the S2 scan's driving-anchor selection: it iterates
`anchors` in list order and the FIRST anchor passing First + Speculative at the resync position
becomes the driving anchor (`AddResyncCandidate`). With the first-pass order (author → loop →
derived), the loop anchor listed first shadowed the derived top-frame Ref alternative, so
`DerivedProbePredicateTests.Test_S2_Resync_Driven_By_Derived_TopFrameRef_Alternative` reported
`AnchorUsage("Expr") == 0, used=[Stmt]`. The A5-1 convention orders anchors by frame proximity
top→bottom — the top-frame Ref alternative (derived from the top frame) is closer than loop-body
Refs (from `LoopFrameLocation` frames below it), and `DeriveProbePredicates` itself is ordered
top-frame-first. Swapping the two derived sources to author → derived → loop restores that
proximity order. Dedup via `seen` is unchanged (earlier source wins). In the 5-regression
grammars `DeriveProbePredicates` returns empty (pure-regex-First filter), so their anchor list
is unchanged by the refinement.

Final T1 block (`RecoveryEngine.cs`, `GenerateS2`):

```csharp
        // T1 якоря: авторские (ближайший кадр с записанным полем) первыми, затем выводимые предикаты зонда
        // (A5-1, 5b.4.2/5b.4.2r): Ref-альтернативы верхнего кадра + Ref-тела Loop-кадров (DeriveProbePredicates, с фильтром),
        // затем loop-якоря из Loop-кадров (DeriveLoopAnchors, без фильтра — до-5b.4.2 поведение).
        // Порядок — по близости кадра top→bottom (A5-1, DoD 4): верхний кадр ближе, чем Loop-кадры под ним;
        // первый совпавший якорь в списке — driving anchor S2-скана.
        // Дедупликация по имени правила: более ранний источник побеждает.
        var anchors = new List<Ref>();
        var authorAnchors = NearestOptions(snapshot, f => f.Options?.Anchors);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (authorAnchors is not null)
            foreach (var a in authorAnchors)
                if (a is Ref r)
                {
                    anchors.Add(r);
                    seen.Add(r.RuleName);
                }
        foreach (var anchor in DeriveProbePredicates(parser, snapshot))
            if (seen.Add(anchor.RuleName))
                anchors.Add(anchor);
        for (var i = snapshot.Stack.Length - 1; i >= 0; i--)
        {
            var frame = snapshot.Stack[i];
            if (frame.Location is not LoopFrameLocation)
                continue;
            foreach (var anchor in DeriveLoopAnchors(parser, frame.RuleName))
                if (seen.Add(anchor.RuleName))
                    anchors.Add(anchor);
        }
```

First-pass result: the 5 pre-existing S2 regressions turned green, but
`DerivedProbePredicateTests.Test_S2_Resync_Driven_By_Derived_TopFrameRef_Alternative` went red
with `AnchorUsage("Expr") == 0, used=[Stmt]` (driving-anchor shadowing — the loop anchor `Stmt`
listed before the derived top-frame Ref alternative `Expr`). Second-pass (refined order) smoke
result: `dotnet build Tests/ParserTests --no-incremental` → 0 errors; smoke filter
(S2TriviaJumpTests | S2IndexOfTests | SpecCacheSharedTests | RecoveryMetricsTests |
DerivedProbePredicateTests) → **12 total, 12 passed, 0 failed, 0 skipped**. Per named test:
`S2TriviaJumpTests.S2TriviaJump_PaddingInsideScanWindow_IsSkipped` PASS,
`S2TriviaJumpTests.S2TriviaJump_SameResync_FewerScannedPositions` PASS,
`S2IndexOfTests.S2IndexOf_SpecifiedInput_SameResync` PASS,
`SpecCacheSharedTests.Test_GenerateS2_Writes_To_Shared_SpecCache` PASS,
`RecoveryMetricsTests.Test_MultiIteration_AcceptRollbackPerStrategy_AndSpecCache` PASS, and all
3 `DerivedProbePredicateTests` (`Test_S2_Resync_Driven_By_Derived_TopFrameRef_Alternative`,
`Test_DeriveProbePredicates_TopFrameRefAlt_Before_LoopAnchor`,
`Test_Valid_Input_Parses_Cleanly_Without_Recovery`) PASS.

Files changed (no commit): `ExtensibleParser/Recovery/RecoveryEngine.cs` (GenerateS2 T1 block
only); `docs/RecoveryImprovementPlan-progress5b.4.md` (this section). No `.csproj` modified. No
commit (final gate after all 5b.4.* sub-points).

#### 5b.4.2r — orchestrator verification (one-shot, with build, repo root)

- `dotnet test Tests/ParserTests` → **437 total, 435 passed, 0 failed, 2 skipped**
  (acceptance met: 430+ passed, 0 failed).
- `dotnet test Tests/CSharpGrammarTests` → **1527 total, 1524 passed, 0 failed, 3 skipped**.
- `dotnet test Tests/CsPreprocessorTests` → **128 total, 128 passed, 0 failed**.

Diff audit: `RecoveryEngine.cs` delta vs HEAD = 5b.4.2 T1/T2 wiring + 5b.4.2r (author `seen`
seeding, `DeriveProbePredicates` before the restored `DeriveLoopAnchors` loop in T1; T2
unchanged); no other production file, no test file, no `.csproj`. No commit (final gate after
all 5b.4.* sub-points).

**F6 note (recurring `ParserTests.csproj` duplicate `<Compile Include>` — tech-debt,
highest-frequency item):** the externally added lines (5b.4.1 STOP flag, 5b.4.2 STOP flag 2)
were rolled back net-zero by the orchestrator; `git status` is clean of `.csproj`. New
correlation (user hypothesis, recorded): the same external process may also hold file locks on
`bin\` — 5b.4.2 STOP flag 3 (stale-DLL trap) required stopping the Roslyn MCP server to release
the locks and unblock rebuilds. Until the source is found and stopped, subagent prompts must
(a) forbid `.csproj` edits, (b) forbid stopping the Roslyn MCP server, (c) require a timestamped
report of any NETSDK1022 / lock incident.

**Stale-DLL note (environmental):** `bin\` is centralized (`Directory.Build.props`), so a direct
`dotnet build ExtensibleParser.csproj` does NOT refresh the `ExtensibleParser.dll` copy in
`bin\Debug\net8.0\` that the test project consumes — `dotnet test --no-build` after such a build
tests the STALE DLL (false negative in the first-pass 5b.4.2r smoke: 5 regressions appeared
unfixed). Subagent prompts must build the test project
(`dotnet build Tests/ParserTests --no-incremental`) before any `--no-build` smoke run.
