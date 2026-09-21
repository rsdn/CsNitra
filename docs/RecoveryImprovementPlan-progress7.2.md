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

---

# 7.2.2 (A5-3 / R5) — S2 insertion first-token + end-of-previous-line for missing on a new line

Sub-point 7.2.2 handles the two remaining R5 squiggle-placement refinements:
(1) S2 insertion with a region → one of the two diagnostics (Inserted/Skipped) at the first
token of the region; (2) "end of previous line" for a missing node detected at the start of
a new line (R5, `RecoveryImprovementProposal.md` §R5: "Missing-токен, ожидавшийся на новой
строке → нулевая диагностика в конце предыдущей строки", Roslyn SyntaxParser.cs:622-635).

## S2 first-token placement (A5-3: "для вставок с областью (S2) — одна из двух диагностик (Inserted/Skipped) на первом токене")

**What S2 actually produces** (stop-if check): an S2 candidate produces **exactly ONE
diagnostic**, never two simultaneously:

- **region case** (`resyncPos > e`): one `Skipped` diagnostic, spanning the **first word of
  the region `[e..resyncPos)`** — set in 7.2.1 (`RecoveryEngine.cs:274-275`,
  `FirstWordSpan`). The tree may additionally contain zero-width insertion nodes at the
  resync point (the suffix obligations, `RecoveryEngine.cs:294-318`), but they carry **no
  diagnostic of their own** (the candidate's `Diagnostics` list holds only the Skipped one).
- **zero-width case** (`resyncPos == e`): one `Inserted` diagnostic at `(e, e)`
  (`RecoveryEngine.cs:291`) — a pure insertion, no region.

So the spec's "two diagnostics (Inserted/Skipped)" is the conceptual pair of an S2 resync
with a region (the absorber node + the insertion nodes in the tree); the one that MUST sit
at the first token of the region is the **Skipped** one — and it does (since 7.2.1). The
`Inserted` part (if present) sits at the resync point, which is the true insertion position,
not the region start. **No code change was needed** for this item — 7.2.1 already places the
Skipped diagnostic at the first token; 7.2.2 adds the full-parse test that pins it (see
Tests below) and this clarification. The absorber NODE keeps the full region (unchanged).

`RecoveryEngine.cs` was NOT modified in 7.2.2.

## End-of-previous-line mechanism (R5)

**File:line** — `Parser.Recovery.cs`:

- `PlaceMissingNodeDiagnostic` (`Parser.Recovery.cs:879-890`) — the shift itself: a
  zero-width `Inserted` diagnostic at position `p` where `input[p-1]` is `'\n'` (line start)
  is moved to the **end of the previous line**: `p-1` walked back over the trailing
  whitespace — the position AFTER the last non-whitespace char of the previous line
  ("just before the newline" when the line has no trailing whitespace). `p == 0` (first
  line — no previous line) and non-line-start positions are returned unchanged (same
  instance). Only zero-width `Inserted` diagnostics shift: `Unrecovered`/`InsufficientStack`
  are not missing nodes, and a non-zero-width span is not a missing node.
- `FinalizeRecoveryDiagnostics(node, input)` (`Parser.Recovery.cs:837`) — the single
  application point: the shift is applied to the DERIVED (tree) part of the public list
  **AFTER** the session-end node match (`AttachDiagnosticsToTree`). Rationale: the tree node
  (the missing token) stays at the recovery point, so `MatchesRecoveryNode`
  (`Parser.Recovery.cs:958`) is unaffected — the shift is display-level, on the public
  artifact only. Shifting at candidate-creation time (the engine) would break the
  position+shape match (the node is at `e`, the shifted span is elsewhere) and would require
  a new `RecoveryDiagnostic` field (out of the allowed edit set).
  - If any diagnostic was shifted, the derived part is re-sorted (stable `OrderBy` by
    `(StartPos, EndPos)`) to keep it in position order; if none was shifted, the list is left
    untouched (byte-for-byte the previous order — preserves the pinned 6.1.2c contract:
    public == derived (in position order) + `Unrecovered`/`InsufficientStack` appended LAST;
    a global re-sort had broken `PublicDerivedListTests`/`SideTableEngineTests` which pin the
    appended-Last order).
- `FinishRecovery(result, input)` (`Parser.Recovery.cs:808`) + the 3 `Recover` call sites —
  thread `input` through (the finalization point is the single place where the final tree +
  result are available; `input` is needed for the newline/whitespace walk).

**R5 interpretation.** R5 ("конец предыдущей строки" for a missing token expected on a new
line, Roslyn SyntaxParser.cs:622-635) is implemented as: when a missing node (zero-width
`Inserted`) would be placed at the start of a new line, the public diagnostic points to the
**end of the previous line's content** — the position after its last non-whitespace char
(equivalently "just before the newline" when there is no trailing whitespace) — instead of
the start of the new line. Line start = the char before the position is `'\n'` (covers both
LF and CRLF). The choice "after the last non-whitespace char" (over "just before the
newline") makes the squiggle sit at the visible end of the previous line when the line has
trailing whitespace. Whitespace walk is `char.IsWhiteSpace` (char-level, like Roslyn's
token-position logic; a trailing comment is NOT walked back over — the squiggle lands after
it, which is still the line's end). The shift applies to all strategies' zero-width
`Inserted` diagnostics (S1/S2/S4) since they all funnel through `FinalizeRecoveryDiagnostics`.

## Tests

**New focused tests — `Tests/ParserTests/Recovery/FirstWordSpanTests.cs`** (3 added, 6 total):

4. `Test_S2_RegionWithInsertion_DiagnosticOnFirstToken` — MiniC (the AnchorResyncTests
   fixture), input `"int foo() { int x\n### int bar() { int y; }"`: the Stmt `"int x"` is
   missing its `;` (e=18), S2 resyncs to the T1 anchor at 22 with BOTH an absorber
   `[18..22)` (the region) AND a zero-width `}` insertion at 22 (the Block's suffix
   obligation) — a full "insertion with a region". Asserts: the tree has the absorber with
   the FULL region `[18..22)` + the zero-width insertion node at 22; exactly ONE Skipped
   diagnostic, placed at the FIRST TOKEN of the region — `"###" [18..21)` — not the whole
   region and not at the resync point.
5. `Test_MissingNodeAtLineStart_DiagnosticAtEndOfPreviousLine` —
   `Module := '{' ZeroOrMany(Stmt) '}'`, `Stmt := Ident ':' Number ';'`, input
   `"{\na: \n; }"`: the Number is missing at e=6 (a LINE START — `input[5]` is `'\n'`); S1
   inserts it (zero-width node stays at 6) → Success@EOF. Asserts: the Inserted diagnostic
   is at **(4, 4)** — the end of the previous line `"a: "` (after the last non-whitespace
   `:` at 3; the trailing space is walked back over) — NOT at the line start 6; the tree
   node stays at 6.
6. `Test_MissingNodeNotAtLineStart_DiagnosticStaysPut` (control) — same grammar, single-line
   input `"{ a: ; }"`: the Number is missing at e=5 (`input[4]` is a space, not a newline) →
   the Inserted diagnostic stays at **(5, 5)** (no shift).

New helper `HasZeroWidthRecoveryNode(node, pos)` (zero-width IsRecovery non-absorber node at
a position) in the same file. No existing test was changed or weakened.

## Results

- `dotnet test Tests/ParserTests` — **Passed: 424, Failed: 0**, Skipped: 2 (pre-existing WIP;
  426 total = 423 baseline + 3 new).
- `dotnet test Tests/CSharpGrammarTests` — **Passed: 1524, Failed: 0**, Skipped: 3
  (pre-existing). Identical to the 7.2.1 baseline — no regression.
- `dotnet test Tests/CsPreprocessorTests` — **Passed: 128, Failed: 0**.

## Files changed (7.2.2)

- `ExtensibleParser/Parser.Recovery.cs` — `PlaceMissingNodeDiagnostic` (:879-890) + the shift
  in `FinalizeRecoveryDiagnostics` (:837-870) + `FinishRecovery`/call sites threading `input`
  (:639, :792, :798, :808).
- `Tests/ParserTests/Recovery/FirstWordSpanTests.cs` — 3 new tests + `HasZeroWidthRecoveryNode`
  helper.

`RecoveryEngine.cs` NOT modified (the S2 first-word span is 7.2.1's; 7.2.2 only clarifies +
pins it). No `.csproj` change, no recovery-strategy (S0–S6) behavior or depth-guard change.
Not committed.
