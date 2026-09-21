# Progress — 5b.5 (R2: expected-continuation stop predicate for S3/S6)

### 5b.5 — R2 implementation + tests

`ExtensibleParser/Recovery/RecoveryEngine.cs` only:
- New private static `AddSuffixObligations(FailureSnapshot? snapshot, Parser parser, List<Terminal> stopSet)` (before `GenerateS3`): no-op for null snapshot; calls `GetSuffixObligationFirsts(snapshot, parser, snapshot.Stack.Length - 1, respectAltIndex: true)` (A4-7 — Seq frames, elements after the current index, active alternative); adds First terminals except EOF/ε, dedup via `TerminalComparer.Instance`. `snapshot.Expected` (First of the failed element) is deliberately NOT added — the parser resumes AFTER the failed element and awaits the suffix; adding Expected would give false stops (closed design, verified on the mental scenario `[1] [ $ 2 ] [3]`: stopping at `2` instead of `]` breaks the continuation).
- `GenerateS3`: stop set = `GetTerminators` ∪ suffix obligations (`stopSet`); the `ordered`/`hasEof` construction now iterates `stopSet`; the scan loop (trivia jump, TerminatorCost, curly/paren/bracket, CostCalculator) is unchanged.
- `GenerateS6`: stop set = anchor-First ∪ terminators ∪ suffix obligations (no-op when snapshot == null); the scan and candidate building are unchanged.

Tests: `Tests/ParserTests/Recovery/R2ExpectedStopTests.cs` (3 tests; grammar `Module = Ref(S)`, `S = "a" "b" "c"`, input `"a $ x c"` — E=2, terminators={EOF}, suffix obligations={Literal("c")} @6):
- `Test_S3_Stops_At_SuffixObligation_First` — direct `Generate(e, snapshot, input, parser, Result.Kind.Failure, "Module", 0, 0)` (parseEnd=0 < input.Length so S6 is also generated): the single Rank-3 candidate has Pos == 6.
- `Test_S6_Stops_At_SuffixObligation_First` — same call: the single Rank-6 candidate has Pos == 6.
- `Test_E2E_Resync_At_SuffixObligation_Start` — default profile, full `Parse`: Success@EOF, `parser.ErrorInfo == null`, EndPos of the first IsAbsorber node in the tree == 6 (template copied from `RecoveryCorpusTests.FirstAbsorberEndPos`).

"Fails without fix" proof (subagent, timestamps 18:11 UTC): `git restore ExtensibleParser/Recovery/RecoveryEngine.cs` (new test file untracked, kept) → Test 1 and Test 3 both FAILED with `Actual: 7` (the no-fix EOF stop) → byte-exact restore (file hash verified; `git diff --stat` 24 insertions(+), 5 deletions(-) confirmed).

Orchestrator verification (one-shot, fresh build): ParserTests 446/0/2, CSharpGrammarTests 1524/0/3, CsPreprocessorTests 128/0.

Files changed (UNCOMMITTED — wave 5b final gate): `ExtensibleParser/Recovery/RecoveryEngine.cs` (AddSuffixObligations + S3/S6 stop sets); `Tests/ParserTests/Recovery/R2ExpectedStopTests.cs` (new, 3 tests); `docs/RecoveryImprovementPlan-progress5b.5.md` (this file); `docs/RecoveryImprovementPlan-checklist.md` (5b.5 line). No `.csproj` modified. No commit.
