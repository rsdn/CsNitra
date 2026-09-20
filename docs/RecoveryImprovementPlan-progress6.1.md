# 6.1.1 (A5-2) — `DeriveDiagnostics(root)`: the PURE tree → diagnostics derivation

Sub-point 6.1.1 adds the **pure** function `DeriveDiagnostics` that derives the recovery
diagnostics from the final parse tree (single source of truth). It does **not** touch the public
API or `_recoveryDiagnostics` (that wiring is 6.1.2), and does not change S0–S6 or recovery behavior.

Per the plan (`RecoveryImprovementPlan.md:558`, `antlr4-analysis.md:619-623`): walk the
`IsRecovery` nodes — zero insertion → `Inserted`, absorber → `Skipped` with node text, `Unrecovered`
— from the result (A4-2).

## Recovery node types found (in the tree)

All recovery nodes in the final tree are `TerminalNode` with `IsRecovery == true`
(`SyntaxTree.cs:151`). There are exactly **two** structural shapes, disambiguated by
`IsAbsorber` and the node width:

| Node | Fields that identify it | Carries | Maps to |
|---|---|---|---|
| **Zero-width insertion** (missing token) | `IsRecovery=true`, `IsAbsorber=false`, `EndPos == StartPos` (width 0) | the inserted terminal's `Kind` (e.g. `"b"`); no text (zero-width) | `RecoveryKind.Inserted` at `[pos, pos)` |
| **Absorber** (skipped region `[E..S)`) | `IsRecovery=true`, `IsAbsorber=true`, `EndPos > StartPos` | the skipped text = `input[StartPos..EndPos]`; span `[StartPos, EndPos)` | `RecoveryKind.Skipped` at `[StartPos, EndPos)` with the node text |

Where they are created:
- **Insertion**: `Injection.Insert` (Length 0) via `CreateInjectedResult` (`Parser.Recovery.cs:224-229`)
  and `InsertOftenMissed` (`Parser.Recovery.cs:297-300`).
- **Absorber**: `Injection.Absorb` (`IsSkip`, Length>0) via `CreateInjectedResult`
  (`Parser.Recovery.cs:227`) and directly in `RecoveryEngine.cs:244, 251, 675, 691, 908`
  (`new TerminalNode("Skipped"/"Trailing", e, S, S-e, IsRecovery: true, IsAbsorber: true)`).

A **third** recovery-terminal shape exists — a *real* recovery-terminal match (e.g.
`ErrorOperator` matching non-fitting text: `IsRecovery=true`, `IsAbsorber=false`, width>0) — but it is
a genuine match, not a hole, so it emits **no** diagnostic (the `IsAbsorber` marker at
`SyntaxTree.cs:149-150` documents exactly this distinction).

### `Unrecovered` is NOT a tree node (the stop-if finding)

`RecoveryKind.Unrecovered` is **not** represented by any tree node. It is a zero-width marker at the
recovery point `e`, added by `AddUnrecoveredIfS6()` (`Parser.Recovery.cs:464-469`) **only when S6 is
the accepted fallback candidate**. In the tree, S6 leaves an **absorber** node
(`RecoveryEngine.cs:908`) that is structurally identical to any S2/S3/S5 absorber — so it cannot be
distinguished from a plain `Skipped` absorber. This matches the plan's explicit design:
`Unrecovered` is derived **from the result (A4-2)** (`RecoveryImprovementPlan.md:558`), not from the
tree. Consequently the pure tree walk does **not** emit `Unrecovered`; it is combined with the tree
walk when the public list is wired (6.1.2).

## `DeriveDiagnostics` signature + mapping

New file: `ExtensibleParser/Recovery/DiagnosticDerivation.cs` (a recovery helper — the
`RecoveryDiagnostic`/`RecoveryKind` types live in `ExtensibleParser.Recovery`).

```csharp
public static class DiagnosticDerivation
{
    // Чистая функция: дерево + ввод → список. Не читает/не мутирует состояние парсера.
    public static List<RecoveryDiagnostic> DeriveDiagnostics(ISyntaxNode root, string input);
}
```

The `input` parameter (beyond the literal `root` in the plan sketch) is required to materialize the
absorber's node text (`input[StartPos..EndPos]`), which the task/plan require the diagnostic to carry
— the node itself only stores spans, not text.

Node → diagnostic mapping (in `Emit`, `DiagnosticDerivation.cs`):
- `TerminalNode { IsRecovery, IsAbsorber: true }` → `RecoveryDiagnostic(StartPos, EndPos,
  Skipped, text: input[StartPos..EndPos], Terminal: null, RuleName: null)`.
- `TerminalNode { IsRecovery, IsAbsorber: false, EndPos == StartPos }` →
  `RecoveryDiagnostic(pos, pos, Inserted, $"inserted {Kind}", null, null)`.
- `TerminalNode { IsRecovery, IsAbsorber: false, EndPos > StartPos }` → **no** diagnostic (real match).

The walk covers the full tree — `SeqNode.RawElements`, `ListNode.RawElements` + `Delimiters`,
`SomeNode.Value` (deliberately `RawElements`, not the absorber-filtered `Elements`, so absorbers are
seen). Result is sorted by `(StartPos, EndPos, Kind)` for a deterministic order. `Terminal`/`RuleName`
are `null` because the tree does not carry them (they are accumulated-list metadata, not tree facts).

## Test — `Tests/ParserTests/Recovery/DeriveDiagnosticsTests.cs` (4 tests)

Reuses `RecoveryTerminals` (`Number` `\d+`, `Ident`, `Trivia` `\s*` — defined in
`IterativeRecoveryTests.cs`, namespace `Recovery`).

1. **`Test_DeriveDiagnostics_Insertion`** — `Module := a b c`, input `"a c"` (missing `"b"`). S1
   inserts `"b"` (zero-width, not an absorber) → `Success@EOF`. Asserts the derived list is exactly
   one `Inserted` at `[2,2)` (zero-width).
2. **`Test_DeriveDiagnostics_AbsorberCarriesSkippedText`** — `Module := Expr Expr`, `Expr := Number`,
   input `"12 34 ###"`. The trailing garbage is absorbed by the S6 floor → an absorber `[6..9)` in the
   tree. Asserts exactly one `Skipped` ending at EOF, and that its `Message` **equals the node text**
   (`input[StartPos..EndPos]` = `"###"`).
3. **`Test_DeriveDiagnostics_UnrecoveredIsNotATreeNode`** — same input as (2). Precondition: the
   accumulated `RecoveryDiagnostics` **does** contain an `Unrecovered` (S6 fallback). Then asserts the
   pure tree-derived list contains **0** `Unrecovered` — the marker is result-derived (A4-2), not a
   tree node, so the tree walk does not emit it. This is the documented "not covered by the tree" case.
4. **`Test_DeriveDiagnostics_CombinedOrderedByPosition`** — `Module := a b c`, input `"a c ###"`.
   Produces both a missing `"b"` (insertion at 2) and trailing garbage (absorber). Asserts the derived
   list has both an `Inserted` and a `Skipped`, is non-decreasing in `StartPos`, and the insertion
   precedes the absorber.

Covered kinds: `Inserted`, `Skipped` (with node text). **Not covered by the tree walk:** `Unrecovered`
(result-derived, A4-2 — test 3 pins this). `Extraneous` (S1b single-token deletion) is also a
tree-absorber and would derive as `Skipped`, but is out of scope for this sub-point (the task lists
only Inserted/Skipped/Unrecovered).

## Files changed

- `ExtensibleParser/Recovery/DiagnosticDerivation.cs` — **new**: pure `DeriveDiagnostics(root, input)`
  (tree walk + mapping above).
- `Tests/ParserTests/Recovery/DeriveDiagnosticsTests.cs` — **new**: 4 tests.
- `docs/RecoveryImprovementPlan-progress6.1.md` — this file.

No recovery engine / `Parser` changes. **Net `.csproj` diff vs HEAD is zero** (see Deviations: the
only csproj edit was reverting a broken uncommitted addition from a prior session, restoring the
files to their committed state). Not committed.

## Test results

- `dotnet test Tests/ParserTests` — **Total: 399 · Passed: 397 · Failed: 0 · Skipped: 2**
  (the 2 skipped are pre-existing `[Ignore("WIP")]`; the +4 passed vs. the prior-session baseline is
  exactly the 4 new `DeriveDiagnosticsTests`).
- `dotnet test Tests/CSharpGrammarTests` — **Total: 1527 · Passed: 1524 · Failed: 0 · Skipped: 3** (no regression).
- `dotnet test Tests/CsPreprocessorTests` — **Total: 128 · Passed: 128 · Failed: 0** (no regression).

## Deviations

- **csproj build fix (required), net-zero vs HEAD.** A prior session (uncommitted) had pre-added
  explicit `<Compile Include="Recovery\DiagnosticDerivation.cs" />` and
  `<Compile Include="Recovery\DeriveDiagnosticsTests.cs" />` to `ExtensibleParser.csproj` and
  `ParserTests.csproj`, anticipating exactly these filenames. SDK-style projects already include every
  in-dir `.cs` via default globbing (all other recovery tests rely on it; the committed csproj only
  lists out-of-dir Shared files), so those explicit entries produced `NETSDK1022: Duplicate 'Compile'
  items` once the files existed. The entries are **redundant** — the files still compile via globbing —
  so they were removed. Removing them **restores both csproj files to their committed (HEAD) state**,
  i.e. the net `.csproj` diff vs HEAD is zero and no `.csproj`-behavior was changed; the edit was
  necessary purely to fix the pre-existing duplicate so the build passes. (The unrelated
  `CppInteropGenerator.csproj` reformat and `RecoveryImprovementPlan-checklist.md` edit present in the
  working tree are prior-session changes, untouched by this task.)
- **`input` parameter.** The plan sketch says `DeriveDiagnostics(root)`; the implementation adds the
  `input` string so the absorber diagnostic can carry the node text (the tree stores spans only).
- **`Unrecovered` not tree-derived.** Confirmed against the stop-if: the tree does **not** carry a
  distinct unrecovered node. It is a zero-width result-derived marker (A4-2). The pure walk emits only
  `Inserted`/`Skipped`; `Unrecovered` is combined at 6.1.2. Test 3 pins this behavior.
- Not committed.

---

# 6.1.2 (A5-2) — wire the public diagnostic list to be derived from the tree — **STOP: regression discrepancy**

Sub-point 6.1.2 wires the public `RecoveryDiagnostics` list to `DeriveDiagnostics(finalTree, input)` +
`Unrecovered` (from the result, when S6 was the accepted fallback), keeping `_recoveryDiagnostics` as an
internal cache. The wiring is **implemented** (below), but the **critical regression check FAILED**: the
derived public list is **not** behavior-equivalent to the previously-accumulated public list. Per the
task's stop-if, work STOPs here and the exact discrepancy is reported. **No test was weakened.**

## Where the public list is now computed

- **Call site**: `Parser.Parse` (`ExtensibleParser/Parser.cs:266`) — immediately after
  `var result = Recover(input, startRule, currentStartPos);`, i.e. the single point where both the final
  tree (inside `result.Node`) and the `input` are available, before the Success@EOF early return and
  `FinalizeResult`.
- **Method**: `Parser.FinalizeRecoveryDiagnostics(Result result, string input)`
  (`ExtensibleParser/Parser.Recovery.cs:349-361`, declaration at :354):
  1. extracts the `Unrecovered` diagnostics from the accumulated `_recoveryDiagnostics` cache
     (`Where(d => d.Kind == RecoveryKind.Unrecovered)`) — they were added there by `AddUnrecoveredIfS6`
     (`Parser.Recovery.cs:464-469`) exactly when an S6 candidate was accepted, so the cache IS the
     carrier of the "from the result" marker at the finalization point;
  2. clears the cache;
  3. if the final result is Success or Partial (a tree exists), appends
     `DiagnosticDerivation.DeriveDiagnostics(node, input)` (already sorted by `(StartPos, EndPos, Kind)`);
  4. appends the extracted `Unrecovered` diagnostics **after** the derived part, in their accumulated
     order (deterministic — the recovery loop's `e` only increases, so they are already in position
     order).
- The public property `RecoveryDiagnostics` (`Parser.Recovery.cs:21`) is unchanged; it now exposes the
  derived content because the cache list is refilled in place at finalization.

## `_recoveryDiagnostics` — still used internally

- **Unrecovered source**: `AddUnrecoveredIfS6` (`Parser.Recovery.cs:468`) still appends `Unrecovered` to
  the cache during the loop (needed by 6.1.2 itself to extract it at finalization).
- **Soft-separator dedup**: `ParseSeparatedList` (`Parser.cs:1015-1016`) still uses
  `_recoveryDiagnostics.Contains(...)` to dedup soft-separator `Skipped` diagnostics across recovery
  re-parses. (These diagnostics are **not** in the derived list — see discrepancy (D2) below.)
- The per-accepted-candidate accumulation (`_recoveryDiagnostics.AddRange(candidate.Diagnostics)`,
  `Parser.Recovery.cs:473,482`) is now **redundant for the public list** (overwritten at
  finalization) but harmless; it was left in place (do-not-change scope: S0–S6 behavior untouched).

## Regression check — **FAILED** (exact discrepancy)

The derived list loses four kinds of information that the accumulated list carried and that existing
tests assert on. The tree cannot express them: (D1) the `Extraneous` kind, (D2) soft-separator
diagnostics (their node is a regular non-`IsRecovery` terminal), (D3) message content, (D4) the
`Terminal`/`RuleName` metadata (the tree stores spans only — documented in 6.1.1).

Exact values (previously-accumulated vs derived, same input, captured by running both code paths):

| # | Input / grammar | Previously accumulated | Derived (new public list) |
|---|---|---|---|
| 1 | `"a c"`, `Module := a b c` (S1 insertion) | `Inserted [2..2) "expected b, found «c»" term=b rule=Module` | `Inserted [2..2) "inserted b" term=- rule=-` |
| 2 | `"12 34 ###"`, `Module := Expr Expr` (S6 floor) | `Skipped [6..9) "bottom skip to 9" term=EOF rule=Module` + `Unrecovered [6..6)` | `Skipped [6..9) "###" term=- rule=-` + `Unrecovered [6..6)` (identical) |
| 3 | `"aab"`, `S := 'a' 'b'` (S1b + S6) | `Extraneous [1..2) "extraneous token, expected b" term=b rule=S` + `Skipped [2..3) "bottom skip to 3" term=EOF rule=S` + `Unrecovered [2..2)` | `Skipped [1..2) "a"` + `Skipped [2..3) "b"` + `Unrecovered [2..2)` — **`Extraneous` kind lost (D1)**, messages/metadata lost |
| 4 | `"1;2"`, `List := SeparatedList(Item, ",", Soft:";")` (A5-5) | `Skipped [1..2) "soft separator ';' accepted in place of required separator" term=; rule=List` | **empty — the diagnostic is lost entirely (D2)** |
| 5 | `"int a; ### int b;"`, T1 anchor grammar (S2 resync) | `Skipped [7..11) "skip to resync point 11" term=- rule=Item` | `Skipped [7..11) "### " term=- rule=-` (D3/D4) |
| 6 | `"{ a: 1+ ### ; }"`, TDOPP grammar (S3 panic) | `Skipped [8..12) "skip to terminator ;" term=; rule=Expr` | `Skipped [8..12) "### " term=- rule=-` (D3/D4) |

Positions and counts of `Inserted`/`Skipped`/`Unrecovered` are preserved; kinds/messages/terminals are
not. **12 existing test methods fail** (all on message/kind/terminal assertions, none on
recovery behavior):

- `SingleTokenDeletionTests.Test_SingleTokenDeletion_ExtraneousDiagnostic` — asserts `Any(Extraneous)` (D1)
- `SoftSeparatorTests.SoftSeparator_Mismatch_IsRecoveredInline_WithSingleSkippedDiagnostic` — expects 1 diag with `Terminal=";"` (D2)
- `SoftSeparatorTests.SoftSeparator_Multiple_Mismatches_OneDiagnosticEach` — expects 2 diags with `Terminal=";"` (D2)
- `T1AnchorReproTests.Test_T1_AuthorAnchor_ResyncToNextItem` — asserts `Message.Contains("resync point")` (D3)
- `T1AnchorReproTests.Test_T2_AuthorCanStart_DoubleError` — same (D3)
- `S0IntegrationTests.MissingParen_RecoveredByS1_HasInsertedDiagnostic` — asserts `Terminal?.Kind == "("` (D4)
- `S2IndexOfTests.S2IndexOf_SpecifiedInput_SameResync` — asserts ≥1 diag with `Message.StartsWith("skip to resync point")` (D3)
- `S2IndexOfTests.S2IndexOf_MultiCharKeyword_JumpSkipsPositions` — same (D3)
- `S2IndexOfTests.S2IndexOf_MixedFirst_DisablesJump_SameResync` — same (D3)
- `S2TriviaJumpTests.S2TriviaJump_SameResync_FewerScannedPositions` — asserts ≥1 diag with `Message.StartsWith("skip to terminator")` (D3)
- `S3TriviaJumpTests.S3TriviaJump_ClosingBraceInsideBlockComment_NotCounted` — same (D3)
- `TierBudgetTests.Test_S1ManyCandidates_S3StillTried_NotStarved` — asserts ≥1 Skipped with `Message.Contains("skip to terminator")` (D3)

Reconciling would require either changing recovery behavior (e.g. marking S1b absorbers / soft-separator
consumption in the tree) or weakening the 12 tests — both out of scope → **STOP**, per the task's
stop-if. A design decision is needed (options: enrich the tree with the metadata; keep the accumulated
list public and make the derived list a separate API for the IDE subtree scenario (6.1.3); or accept
the derived message/kind contract and re-point the 12 tests at positions/kinds — a separate, explicit
decision).

## Test (added)

`Tests/ParserTests/Recovery/DeriveDiagnosticsTests.cs` — two new tests (both **pass** with the wiring):

5. **`Test_PublicList_EqualsDerived_InsertionCase`** — `"a c"` (`Module := a b c`, S1 insertion, no S6):
   `parser.RecoveryDiagnostics` == `DeriveDiagnostics(tree, input)` exactly (no `Unrecovered`).
6. **`Test_PublicList_EqualsDerivedPlusUnrecovered_S6FallbackCase`** — `"12 34 ###"` (`Module :=
   Expr Expr`, S6 floor): `parser.RecoveryDiagnostics` == `DeriveDiagnostics(tree, input)` + the
   `Unrecovered` marker, appended last; the `Unrecovered` is exactly one, at `[6..6)` (the recovery
   point `e`).

## Test results (with the wiring in place)

- `dotnet test Tests/ParserTests` — **Total: 401 · Passed: 387 · Failed: 12 · Skipped: 2** — the 12
  failures are exactly the discrepancy set above (baseline was 399/397/0/2; +2 = the new tests, both
  green).
- `dotnet test Tests/CSharpGrammarTests` — **Total: 1527 · Passed: 1524 · Failed: 0 · Skipped: 3** (no
  regression; these suites only assert `Count == 0` on valid code / `Count > 0` on malformed code).
- `dotnet test Tests/CsPreprocessorTests` — **Total: 128 · Passed: 128 · Failed: 0** (no regression).

## Deviations

- **csproj build fix (required), net-zero vs HEAD.** While creating a *temporary* diagnostic-dump test
  (`Recovery\TmpDiscrepancyDumpTests.cs`, deleted after the dump), an external process on this machine
  (same behavior documented in the 6.1.1 section) added
  `<Compile Include="Recovery\TmpDiscrepancyDumpTests.cs" />` to `ParserTests.csproj`, producing
  `NETSDK1022: Duplicate 'Compile' items` (SDK default globbing already includes the file). The line was
  removed, restoring `ParserTests.csproj` to its committed (HEAD) state; net `.csproj` diff vs HEAD is
  zero. The temporary dump file itself was deleted after capturing the accumulated lists.
- **Wiring left in place, uncommitted.** The working tree is deliberately red (12 tests) as the
  evidence for the stop-if report. Reverting is a small change (the `Parser.cs:266` call + the
  `FinalizeRecoveryDiagnostics` method in `Parser.Recovery.cs:349-361` + the 2 tests in
  `DeriveDiagnosticsTests.cs:116-162`).
- Not committed.
