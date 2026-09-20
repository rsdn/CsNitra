# Progress — 5b.1.1 (per-call-site terminators, pure addition)

## Scope
- Add `FollowSetCalculator.GetTerminatorsPerSite(IReadOnlyList<StackFrame>)` + private helpers `TailOf`, `SafeFollow` to `ExtensibleParser/FollowSetCalculator.cs`.
- New test file `Tests/ParserTests/Recovery/FollowSetPerSiteTests.cs` (5 tests).
- No other production file touched; `GetTerminators` untouched; no commits.

## Status
- [x] Read `GetTerminators` (FollowSetCalculator.cs:230-247) — copied dedup/EOF/ordering pattern (`TerminalComparer.Instance`, `EofTerminal.Instance` appended last).
- [x] Verified signatures: `Seq(Rule[] Elements, string Kind)`, `Literal(string Value, string? Kind = null)` (Kind = Value when null), `Ref(string RuleName, string? Kind = null)`, `FollowSetCalculator(Dictionary<string, Rule[]>, params string[] startSymbols)`, `StackFrame(RuleName, Precedence, Location, Expected, Options)`, `SeqFrameLocation(int ElementIndex)`, `RuleFrameLocation(int AltIndex)`, `RecoveryOptions.Terminators`/`Recoverable`.
- [x] Implemented methods (added after `GetTerminators`, before `ComputeFollowSets`).
- [x] Build + tests.

## Decisions
- Codebase convention "collection expression style = never" → used `new[] { EofTerminal.Instance }` and `new Terminal[0]` instead of spec's `[]` collection expressions.
- `TailOf` named properties `(Terms, Nullable)` to match `ComputeFirstForSequence` naming.
- **Test 5 stack deviation (spec says "e.g.")**: the spec's example stack `[RuleFrame("List",0), RuleFrame("X",0)]` does NOT satisfy the equality assertion under this algorithm/grammar: `GetTerminatorsPerSite` walks `TailOf(i-1)`, so for a 2-frame stack the result is `follow(outer)=follow(List)={EOF}` → `[EOF]`, while rule-level `GetTerminators` is `follow(X) ∪ follow(List) = {",", "}", "!", EOF}`. The innermost RuleFrame's own follow is never consulted by the per-site walk. Used instead the real RuleFrame-only stack `[RuleFrame("StartList",0), RuleFrame("List",0)]` (StartList := Ref("List") pushes exactly these two frames, no SeqFrame): follow(List) = follow(StartList) = {EOF}, so both methods return `[EOF]` and the non-EOF kind sets are equal.

## Results
- `dotnet build` (repo root): **Build succeeded, 0 warnings, 0 errors.** (First attempt failed with CS1503 in the new test file only — `CollectionAssert.AreEquivalent` needs `ICollection`, not `HashSet<string>`; fixed by `.ToList()`, re-verified.)
- `dotnet test Tests/ParserTests --filter "FullyQualifiedName~FollowSetPerSiteTests"`: **Passed — 5/5** (Failed: 0, Passed: 5, Skipped: 0).
  - Test_List_ContainsComma_NotBrace: PASS
  - Test_Block_ContainsBrace_NotComma: PASS
  - Test_PerSite_Narrower_Than_RuleLevel: PASS
  - Test_TailEmpty_Passthrough: PASS
  - Test_RuleFrameOnly_Equals_RuleLevel: PASS
- `dotnet test Tests/ParserTests --filter "FullyQualifiedName~FollowSetTests"`: **Passed — 30/30** (Failed: 0, Passed: 30, Skipped: 0). No regressions.

## Stop-if
- None hit. (Tests 1, 2, 4 pass; no second production file touched; no unrelated regressions; constructor signatures determined from FollowSetTests.cs.)

## 5b.1.1a — Bug fix: `TailOf` had no `LoopFrame` branch

### Fix
`ExtensibleParser/FollowSetCalculator.cs`, `TailOf`: added a `LoopFrameLocation` branch BEFORE the final rule-level fallback. Previously a `LoopFrame` fell through to `return (SafeFollow(parent.RuleName), true)`, where `parent.RuleName` is the ENCLOSING rule — yielding `follow(R)` (what follows the whole rule) instead of what follows the loop, over-including terminals.

New branch (mirrors the `SeqFrame` branch: reads the `SeqFrame` directly below the `LoopFrame`, then the `RuleFrame` below that to resolve the `Seq` production):
- `parent.Location is LoopFrameLocation && parentIndex >= 2`:
  - `seqBelow = stack[parentIndex - 1]`; require `seqBelow.RuleName == parent.RuleName` and `seqBelow.Location is SeqFrameLocation { ElementIndex: var loopEi }`.
  - `ruleBelow = stack[parentIndex - 2]`; require `ruleBelow.Location is RuleFrameLocation { AltIndex: var altIdx }`, `_rules.TryGetValue(parent.RuleName, out var alts)`, `0 <= altIdx < alts.Length`, `alts[altIdx] is Seq seq`, `0 <= loopEi < seq.Elements.Length`.
  - `loopBody = seq.Elements[loopEi]`, `remaining = seq.Elements[(loopEi + 1)..]`.
  - `bodyFirst = ComputeFirstForSequence([loopBody])`, `(remFirst, remNullable) = ComputeFirstForSequence(remaining)`.
  - Combine `bodyFirst` + `remFirst` into one list, dedup via `TerminalComparer.Instance` (skipping `EpsilonTerminal`); `return (combined, remNullable)`.
- Any failed condition falls through to the existing rule-level fallback (unchanged). `SeqFrame` branch, `Recoverable:false`, `Options.Terminators`, and the final fallback were NOT altered.

### New regression test
`Tests/ParserTests/Recovery/FollowSetPerSiteTests.cs`: `Test_LoopPerSite_NoOverInclude` — grammar `Item := "i"; Body := "{" ZeroOrMany(Item) "}"; Start := "a" Body "b"`; stack `[Start@RuleFrame(0), Start@SeqFrame(1), Body@RuleFrame(0), Body@SeqFrame(1), Body@LoopFrame("ZeroOrMany",0), Item@RuleFrame(0)]`. Asserts per-site terminators contain `"i"` and `"}"` and do NOT contain `"b"` (follow(Body)).

### Build/test results
- `dotnet build` (repo root, Nitra.sln): **Build succeeded**, 0 errors.
- `dotnet test Tests/ParserTests --filter "FullyQualifiedName~FollowSetPerSiteTests"`: **Passed — 6/6** (new test `Test_LoopPerSite_NoOverInclude`: PASS).
- `dotnet test Tests/ParserTests --filter "FullyQualifiedName~Test_Candidates_Sorted_By_Rank_Cost_Pos_Rule_Terminal"`: **Passed — 1/1** (previously failing; now PASS: S1 = {i, }}, no `b`).
- `dotnet test Tests/ParserTests --filter "FullyQualifiedName~Test_Generate_Is_Deterministic"`: **Passed — 1/1** (previously failing; now PASS).
- Full `dotnet test Tests/ParserTests`: **Total: 385 · Passed: 383 · Failed: 0 · Skipped: 2**. ALL GREEN.

### Stop-if
- None hit. Only `FollowSetCalculator.cs` + `FollowSetPerSiteTests.cs` changed; no `.csproj` touched; no commit.
