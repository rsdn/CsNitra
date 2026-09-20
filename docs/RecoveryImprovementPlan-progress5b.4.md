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
