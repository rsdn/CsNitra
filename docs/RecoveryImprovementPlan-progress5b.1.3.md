# 5b.1.3 — S1 insertion source: rule-level follow → per-site terminators

## Change 1 — one-line production change

`ExtensibleParser/Recovery/RecoveryEngine.cs` — `GenerateS1` (lines 93–95):

Before:
```csharp
if (calculator is { } calc)
    foreach (var t in calc.GetFollowSet(top.RuleName))
        Add(t, 1);
```

After:
```csharp
if (calculator is { } calc)
    foreach (var t in calc.GetTerminatorsPerSite(snapshot.Stack))
        Add(t, 1);
```

`top` and `snapshot` were already in scope; this is the only production change. Nothing else in
`GenerateS1` or any other method was touched. `top` remains used (`ruleName = top.RuleName`).

## Change 2 — new test

`Tests/ParserTests/Recovery/S1PerSiteTests.cs` (MSTest, `namespace Recovery`, matches sibling style):
`Test_S1_List_ContainsComma_NotBrace`.

Approach: build a real `Parser` over
`X := "x"`, `List := X "," X`, `Block := X "}"`, `Start := List` (Block unreferenced by the start
rule but still contributes `'}'` to rule-level `follow(X)`). Manually construct a
`FailureSnapshot(1, [Start,Start,List,List,X frames], comma, [comma])` reusing the grammar's own
`Literal(",")` instance, then call
`RecoveryEngine.Generate(1, snapshot, "xx", parser, Result.Kind.Failure, "Start", 0, 1)` and assert
that among `Id.StartsWith("S1:")` candidates there is a `TerminalKind == ","` and none with
`TerminalKind == "}"`.

Result: PASS (1/1).

## Build / test results

- `dotnet build` — succeeded.
- `dotnet test Tests/ParserTests --filter "FullyQualifiedName~S1PerSiteTests"` — 1 passed, 0 failed.
- Full `dotnet test Tests/ParserTests` — **380 passed, 2 FAILED, 2 skipped, 384 total.**

## STOP-IF triggered: 2 NEW failures in the full ParserTests suite

Both in `Tests/ParserTests/Recovery/CandidateGenerationTests.cs` (braced-parser cases), and both are
caused by this one-line change (verified: with the change stashed they PASS, with the change applied
they FAIL — `Expected:<5>. Actual:<6>`):

- `Test_Candidates_Sorted_By_Rank_Cost_Pos_Rule_Terminal`
- `Test_Generate_Is_Deterministic`

Root cause: these tests hard-code the OLD rule-level S1 behavior for the braced grammar
(`Start := a Body b`, `Body := { Item* }`, `Item := i c`). The test comment documents
`S1: FailedTerminal i + Expected {i} + FollowSet(Item) {i, }} → i, }` and asserts exactly 5
candidates (`S1:Item:i`, `S1:Item:}`, `S3:Item:}`, `S4:Item:EOF`, `S6:Start:}`). Switching S1's
source from `GetFollowSet("Item")` to `GetTerminatorsPerSite(stack)` changes the per-site S1
candidate set for that grammar (now 6 total), so the count and the specific `S1:Item:}` assertion
no longer hold.

Per the task's stop-if ("The full ParserTests suite has any NEW failure" → STOP, do not improvise),
I did **not** modify these two pre-existing tests. They assert the pre-5b.1.3 behavior and need an
explicit decision to be updated to the per-site expectation.

## Side note (external modification, not part of my change)

During the session an external process injected a stray
`<Compile Include="Recovery\S1PerSiteTests.cs" />` into
`Tests/ParserTests/ParserTests.csproj` (it was absent in the initial read). This duplicated the
SDK's auto-glob and broke the build with `NETSDK1022: Duplicate 'Compile' items`. I removed that
single stray line to restore the csproj to its exact original state; `git diff` on the csproj is now
empty (identical to HEAD). My intended change never touched any `.csproj`. (Other pre-existing
working-tree modifications — `Parsers/Cpp/CppInteropGenerator/CppInteropGenerator.csproj` and some
docs — were left untouched; out of scope.)

## Not done (per task constraints)

- Did NOT modify `GetTerminators`, `GetTerminatorsPerSite`, `TailOf`, `SafeFollow`, S1b/S2–S6,
  `HygieneCore`/`ApplyPatches`/`RollbackPatches`/`PatchMemo`, or any `.csproj` (no `<Compile Include>`
  added).
- Did NOT commit.

## 5b.1.3 follow-up — broken-test triage (per-site S1 correctness call)

Determined whether the NEW per-site S1 candidate set is more correct for the two failing braced-parser
tests (`NewBracedParser`, input `"a { x { y } } b"`). **Verdict: BUG — an over-inclusion. Tests NOT updated.**

### Snapshot stack (e = 4, `parser.LastSnapshot.Stack`, outer → inner)

| idx | RuleName | Location |
|-----|----------|----------|
| 0 | Start | `RuleFrameLocation { AltIndex = 0 }` |
| 1 | Start | `SeqFrameLocation { ElementIndex = 1 }` |
| 2 | Body  | `RuleFrameLocation { AltIndex = 0 }` |
| 3 | Body  | `SeqFrameLocation { ElementIndex = 1 }` |
| 4 | Body  | `LoopFrameLocation { LoopKind = ZeroOrMany, Iteration = 0 }` |
| 5 | Item  | `RuleFrameLocation { AltIndex = 0 }` |
| 6 | Item  | `SeqFrameLocation { ElementIndex = 0 }`  ← FailedTerminal `i` |

### Exact NEW 6-item candidate list (in `Sort` order)

`[S1:Item:b, S1:Item:i, S1:Item:}, S3:Item:}, S4:Item:EOF, S6:Start:}`

The NEW S1 set is `{ i, }, b }` (old rule-level set was `{ i, }`). The only S1 terminal not in the old
set is **`b`**.

### Correctness call (per new S1 terminal not in old `{ i, }`)

- **`b`** — **OVER-INCLUSION.** `b` is `Start`'s element `[2]`, which only comes *after* `Body` fully
  closes (after `Body`'s closing `}`). At e=4 the failure is inside `ZeroOrMany(Ref(Item))` of `Body`,
  where `Body` has not closed; the valid immediate continuations are `i` (start another `Item`) and
  `}` (exit the loop → `Body`'s closing brace). `b` is not reachable at the failure point.

### Sorting verdict

The new 6-item list IS correctly sorted by `(Rank, Cost, Pos, RuleName ordinal, TerminalKind ordinal)`:
Ranks `1,1,1,3,4,6` ascending; within the rank-1 group (all `Cost=InsertCost`, `Pos=4`, `Rule=Item`) the
TerminalKind ordinal order is `b`(0x62) < `i`(0x69) < `}`(0x7d). ✓

### Root cause (`GetTerminatorsPerSite` / `TailOf`)

`TailOf` (`ExtensibleParser/FollowSetCalculator.cs:260–284`) has **no branch for `LoopFrameLocation`**.
The frame at stack index 4 — `Body@LoopFrameLocation(ZeroOrMany, 0)` — is not a `SeqFrameLocation`, so it
falls through to the final rule-level fallback `return (SafeFollow(parent.RuleName), true)` at line 283,
yielding `SafeFollow("Body") = follow(Body) = { b }` with `nullable = true`. That `b` then rides the
nullable passthrough in `GetTerminatorsPerSite` outward into the S1 set. The correct per-site terminators
for that loop frame would be the enclosing `Seq` tail after the loop element (`Body`'s element `[2]` =
`}`) ∪ the loop's re-entry first set (`first(Item)` = `i`) — i.e. `{ i, }`, not the rule-level
`follow(Body) = { b }`. Because `TailOf` does not model loops, it over-broadly emits `b`.

### Decision

**STOP — bug report.** Because the new S1 terminal `b` is an over-inclusion (not a valid continuation at
the failure point), the two tests were **NOT** updated (they remain failing, asserting the pre-5b.1.3
count of 5). No production file and no `.csproj` were touched by this triage; the temporary
diagnostic line used to capture the stack/IDs was removed (test file `git diff` is empty). No commit.
