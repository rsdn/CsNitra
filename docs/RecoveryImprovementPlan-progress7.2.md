# 7.2.1 (A5-3 / R5) — diagnostic on the first word of a dirty region

Sub-point 7.2.1 (A5-3): the `RecoveryDiagnostic` span of a Skipped/absorber region `[E..S)` is
not the whole region — it is the **first non-trivia "word"** inside `[E..S)` (IDE squiggle
quality: a short label on the first "guilty" word, not an underline of the whole skipped
region). The absorber NODE in the tree keeps the full region span `[E..S)` — only the
diagnostic span shrinks. Roslyn source: `GetDiagnosticSpanForMissingNodeOrToken`
(SyntaxParser.cs:783-880) — a node with skipped text gets a diagnostic at the FIRST skipped
token.

## The first-word mechanism

**Reused preview:** the spec points at `RecoveryEngine.cs:712` — that line reference is STALE
(in the current file it is inside `GenerateS3`'s candidate list, in every revision checked).
The actual existing "first word" preview is the **S1b first-non-trivia mechanism**
(`GenerateS1b`, `RecoveryEngine.cs:134`): `parser.Trivia.TryMatch(input, pos)` returns the
leading trivia run, so the first non-trivia character — i.e. the first word — starts at
`pos + Trivia.TryMatch(input, pos)`. That mechanism is reused as-is.

**New helper** (applied at every Skipped-region site):

- `RecoveryEngine.FirstWordSpan(input, trivia, start, end)` — `RecoveryEngine.cs:1050`.
  Word start = `start + Trivia.TryMatch(input, start)` (the S1b mechanism, trivia-aware —
  covers comments when the grammar's Trivia includes them); word end = the next position
  where `Trivia.TryMatch > 0` (a non-trivia char always matches trivia with length 0, so the
  run is exact). Returns `(wordStart, wordEnd)`; the region is otherwise untouched.

## Where the span is applied

All four engine Skipped-region diagnostics now carry the first-word span (the absorber node
and the injection/memo patch still cover the full region — strategy behavior S0–S6 is
UNCHANGED, only the diagnostic's `StartPos`/`EndPos`):

| Site | Region | File:line |
|---|---|---|
| S2 resync (`"skip to resync point {pos}"`) | `[e..resyncPos)` | `RecoveryEngine.cs:274-275` |
| S3 panic (`"skip to terminator {kind}"`) | `[e..foundS)` | `RecoveryEngine.cs:683-684` |
| S5 trailing (`"trailing garbage: N chars"`) | `[e..EOF)` | `RecoveryEngine.cs:847, 858` |
| S6 floor (`"bottom skip to {s}"`) | `[start..s)` | `RecoveryEngine.cs:924-925` |

Not changed (intentionally):

- S1b `Extraneous` (`RecoveryEngine.cs:162`) — already a single-token span, kind is not Skipped.
- Soft-separator Skipped (`Parser.cs:1012`) — the span IS the matched token (already one word).
- Zero-width diagnostics (S1/S2/S4 `Inserted`, `Unrecovered`, `InsufficientStack`) — already
  zero-width markers at the recovery point.

**Session-end match (6.1.2b) — `Parser.Recovery.cs`:** `MatchesRecoveryNode`
(`Parser.Recovery.cs:911-917`) matched a region diagnostic to the absorber node by EXACT span
`[StartPos..EndPos)`. With the first-word span the diagnostic is a sub-span of the node, so
the region branch now matches by **containment**: `node.IsAbsorber && node.StartPos <=
diag.StartPos && diag.EndPos <= node.EndPos`. The match stays unique: absorber regions are
non-overlapping (one candidate per recovery point; points strictly increase and the next
point is at/after the previous absorber's end). The 6.1.2b contract comment
(`Parser.Recovery.cs:851-857`) was updated accordingly.

**Pure derivation (6.1.1) — `DiagnosticDerivation.cs`:** `Emit`
(`DiagnosticDerivation.cs:71-87`) emitted the absorber's FULL region span. It now emits the
first-word span too (same span semantics as the engine). The pure function has no Trivia
terminal, so its private `FirstWordSpan` (`DiagnosticDerivation.cs:93`) is
**whitespace-based** (`char.IsWhiteSpace`) rather than trivia-based — a documented
limitation: for a region that starts with a COMMENT the engine's word starts after the
comment, the pure derivation's word starts at the comment. Existing `DeriveDiagnosticsTests`
stay green unchanged (their fixtures have no comments).

## All-whitespace fallback

An empty or all-trivia/whitespace region has no word: the **original region span `[E..S)` is
kept** (both helpers return `(start, end)`). Choice rationale:

1. The full span matches the absorber node exactly, so the session-end shape match
   (containment) is unchanged in that case.
2. A zero-width span at `E` would be misclassified as an insertion by the session-end shape
   match (`StartPos == EndPos` → zero-width `!IsAbsorber` branch) and would attach to the
   wrong node (or none).

In practice the fallback is defensive: S2/S3 regions start at `e` (a non-trivia failure
position — the parser skips trivia before matching) and S5/S6 regions start at a parse end
position (also after trailing trivia), so all four engine regions start with a word. The
fallback is exercised by a hand-built-tree test through `DiagnosticDerivation`.

## Tests

**New focused tests — `Tests/ParserTests/Recovery/FirstWordSpanTests.cs`** (3 tests):

1. `Test_S6_TrailingRegion_DiagnosticSpansFirstWordOnly` — `Module := Expr Expr`, input
   `"12 34 ### bar baz"`: the S6 region is `[6..17)` `"### bar baz"`; the Skipped diagnostic
   spans the first word `"###" [6..9)` only (asserted `EndPos < region end`), while the
   absorber NODE in the tree keeps the full `[6..17)`.
2. `Test_S3_Region_DiagnosticSpansFirstWordOnly` — `Module := '{' ZeroOrMany(Stmt) '}'`,
   input `"{ a: 1 ### b: 2 ; }"`: region `[7..11)` `"### "` (skip to the loop terminator
   `Ident b`); the diagnostic spans `"###" [7..10)` (starts at `e` — no leading whitespace),
   the absorber node keeps `[7..11)`.
3. `Test_AllWhitespaceRegion_FallsBackToFullSpan` — hand-built tree, absorber `[3..6)` over
   `"abc   def"` (region `"   "` all whitespace): `DeriveDiagnostics` keeps the ORIGINAL span
   `[3..6)` and the full region text in Message (the documented fallback).

**Updated existing tests** (they pinned the OLD full-region span semantics — updated to the
new semantics WITHOUT weakening; the resync-position assertions are preserved by reading the
resync position from its true source: the message `"skip to resync point {pos}"` for S2, or
the absorber node's `EndPos` in the final tree for S3, whose message carries no position):

- `CandidateGenerationTests.Test_S3_Finds_Terminator_With_Nesting` — S3 diagnostic EndPos
  `12` → `5` (region `"x { y } "`, first word `"x"`).
- `AnchorResyncTests.Test_T1_Garbage_Between_E_And_Anchor_Absorber` — S2 diagnostic EndPos
  `22` → `21` (region `"### "`, first word `"###"`).
- `S2IndexOfTests` — `Normalize` no longer subtracts the padding shift from Skipped lengths
  (first-word length is padding-invariant); resync positions read from the message via a new
  `ResyncPositions` helper (3 tests).
- `S2TriviaJumpTests` — `Normalize` as above; resync positions read from absorber nodes in
  the final tree via a new `AbsorberEnds` helper (2 tests).
- `S3TriviaJumpTests` — resync positions read from absorber nodes via `AbsorberEnds`
  (1 test).

`PublicDerivedListTests` needed NO change: its S6 fixture `"12 34 ###"` parses to NewPos 6
(trailing trivia included), so the region `[6..9)` has no leading whitespace — first word ==
whole region, and the Unrecovered marker (at `e=6`) still equals the diagnostic start.

## Results

- `dotnet test Tests/ParserTests` — **Passed: 421, Failed: 0**, Skipped: 2 (pre-existing WIP).
- `dotnet test Tests/CSharpGrammarTests` — **Passed: 1524, Failed: 0**, Skipped: 3
  (pre-existing). No CSharpGrammarTests test asserted the full region — no stop-if triggered.
- `dotnet test Tests/CsPreprocessorTests` — **Passed: 128, Failed: 0**.

## Files changed

- `ExtensibleParser/Recovery/RecoveryEngine.cs` — `FirstWordSpan` helper (:1050) + the 4
  Skipped sites (S2 :274, S3 :683, S5 :847/:858, S6 :924).
- `ExtensibleParser/Parser.Recovery.cs` — `MatchesRecoveryNode` region branch → containment
  (:911-917) + contract comment (:851-857).
- `ExtensibleParser/Recovery/DiagnosticDerivation.cs` — `Emit` first-word span + whitespace
  `FirstWordSpan` (:71-101).
- `Tests/ParserTests/Recovery/FirstWordSpanTests.cs` — **new**, 3 focused tests.
- `Tests/ParserTests/Recovery/{CandidateGenerationTests,AnchorResyncTests,S2IndexOfTests,
  S2TriviaJumpTests,S3TriviaJumpTests}.cs` — span-semantics updates (no weakening).

No `.csproj` change (an auto-registered `<Compile Include>` for the new test file was
reverted — the SDK default glob picks the file up; verified green after the revert). No
recovery-strategy (S0–S6) behavior or depth-guard (7.1) change. Not committed.
