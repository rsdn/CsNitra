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
