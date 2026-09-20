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

---

# 6.1.2a (A5-2) — node-attached diagnostics via a side-table (infrastructure)

Sub-point 6.1.2a adds the **side-table + registration hook + side-table-based derivation** on
`Parser` for A5-2 (node-attached recovery diagnostics via reference identity). NO engine wiring yet
(S1..S6/S1b do not call `AttachDiagnostic` — that is 6.1.2b) and the public `RecoveryDiagnostics`
list is UNCHANGED (that is 6.1.2c). `DiagnosticDerivation.cs` (6.1.1) and its tests are untouched.

## Side-table field + init

- **Field** (`Parser.Recovery.cs:33`):
  `private Dictionary<WeakReference<ISyntaxNode>, RecoveryDiagnostic[]> _diagSideTable = new(WeakRefNodeKeyComparer.Instance);`
  — non-`readonly` (re-assigned per parse).
- **Init**: the field initializer makes it non-null from construction; a 1-line fresh re-init at the
  start of `Parse` (`Parser.cs:252`): `_diagSideTable = new(WeakRefNodeKeyComparer.Instance);` so each
  parse gets a clean table.

## Stop-if resolution: `ConditionalWeakTable` UNAVAILABLE in netstandard2.0 → Dictionary fallback used

Verified with a clean netstandard2.0 probe project: `ConditionalWeakTable<,>` fails with **CS0246**
(type not found), while `WeakReference<T>` (generic) and non-generic `WeakReference` DO compile. So the
task's prescribed fallback is used: `Dictionary<WeakReference<ISyntaxNode>, RecoveryDiagnostic[]>`.
- **Key** = `WeakReference<ISyntaxNode>` (weak = future-proofing for Wave 8; harmless for per-parse use).
- **Value** = `RecoveryDiagnostic[]` (the original spec's value type; a `Dictionary` can replace values,
  so accumulation is a re-store, unlike `ConditionalWeakTable` which cannot).
- **`WeakReference<T>.Target` is ALSO unavailable** in this ref set (probe: CS1061); only
  `TryGetTarget(out T?)` is. The comparer uses `TryGetTarget`.
- **Comparer** (`WeakRefNodeKeyComparer`, `Parser.Recovery.cs:89`):
  `IEqualityComparer<WeakReference<ISyntaxNode>>` that compares the target node **by reference**
  (`ReferenceEquals`) and **identity-hashes** it. This is the same reference-identity pattern as the
  existing `ReferenceComparer` (5a.5.1, `Parser.cs:66`), which is `Rule`-typed and so **cannot be reused
  directly** for a `WeakReference<ISyntaxNode>` key (it compares the key object itself by reference; a
  fresh `WeakReference` wrapper per lookup is a different object). The identity hash is resolved the same
  way `ReferenceComparer` does (reflection over the BCL `RuntimeHelpers.GetHashCode`, because
  `Shared/NetStandard2_0Support.cs` shadows that type by name).

## `AttachDiagnostic` (registration hook)

`Parser.Recovery.cs:37`: `public void AttachDiagnostic(ISyntaxNode node, RecoveryDiagnostic diag)`.
Creates `new WeakReference<ISyntaxNode>(node)`; if the node has no entry, stores `[diag]`; otherwise
re-stores the existing array with `diag` appended (`[.. existing, diag]`). **Accumulates** multiple
diagnostics on the same node.

## Derive method (side-table, reference-identity)

`Parser.Recovery.cs:52`: `public IReadOnlyList<RecoveryDiagnostic> DeriveRecoveryDiagnostics(ISyntaxNode root)`.
Walks the full tree (`SeqNode.RawElements`, `ListNode.RawElements` + `Delimiters`, `SomeNode.Value`;
leaves = `TerminalNode`/`NoneNode`/`PredicateNode`), and at **every node** looks up the side-table by
reference identity, collecting its diagnostics. Returns them sorted by `(StartPos, EndPos)`. This is the
reference-identity lookup — distinct from (and does not modify) the pure structure-based
`DiagnosticDerivation.DeriveDiagnostics` (6.1.1), which is retired in 6.1.2c.

## Test — `Tests/ParserTests/Recovery/SideTableTests.cs` (new, 3 tests)

Fresh `Parser` (no full `Parse` call needed — the side-table is non-null from construction). Hand-built
trees (distinct `StartPos`), `AttachDiagnostic` with full-metadata `RecoveryDiagnostic`s (kinds
`Inserted`/`Skipped`/`Extraneous`, distinct `Terminal`/`RuleName`/`Message`):
1. `Test_SideTable_Derive_ReturnsAttachedSortedByPosition` — 5 diags attached (scrambled order) to 5
   different nodes covering every walked node kind (`SeqNode` / `ListNode` element + delimiter /
   `SomeNode` value); un-attached nodes contribute nothing. Derive returns exactly the 5, ordered by
   `(StartPos, EndPos)`, with every metadata field (incl. the exact `Terminal` reference) intact.
2. `Test_SideTable_Accumulate_TwoOnSameNode` — two diags on the SAME node both survive.
3. `Test_SideTable_EndPosTieBreak` — same `StartPos`, different `EndPos` → ordered by `EndPos`.

## Test results (one-shot)

- `dotnet test Tests/ParserTests` — **Total: 402 · Passed: 400 · Failed: 0 · Skipped: 2** (the 2 skipped
  are the pre-existing `[Ignore("WIP")]`; +3 vs the 6.1.1 baseline of 399 is exactly the 3 new
  `SideTableTests`).
- `dotnet test Tests/CSharpGrammarTests` — **Total: 1527 · Passed: 1524 · Failed: 0 · Skipped: 3** (no regression).
- `dotnet test Tests/CsPreprocessorTests` — **Total: 128 · Passed: 128 · Failed: 0** (no regression).

## Files changed

- `ExtensibleParser/Parser.Recovery.cs` — **modified**: side-table field + init, `AttachDiagnostic`,
  `DeriveRecoveryDiagnostics` + `CollectAttached`, and the `WeakRefNodeKeyComparer` (reference-identity
  comparer for `WeakReference<ISyntaxNode>` keys).
- `ExtensibleParser/Parser.cs` — **modified**: 1-line fresh-per-parse init at `Parser.cs:252`.
- `Tests/ParserTests/Recovery/SideTableTests.cs` — **new**: 3 tests.
- `docs/RecoveryImprovementPlan-progress6.1.md` — this file.

No `.csproj` change, no engine wiring (S0–S6 untouched), public `RecoveryDiagnostics` list unchanged,
`DiagnosticDerivation.cs` (6.1.1) + its tests untouched. **Not committed.**

---

# 6.1.2b (A5-2) — strategies register their diagnostics in the side-table (D1 + D4)

Sub-point 6.1.2b makes the recovery strategies' diagnostics (S1b/S1/S3/S6/S2/S4/S5) attach to the
recovery NODES they correspond to, so `DeriveRecoveryDiagnostics(root)` reproduces the accumulated
list's KIND (D1: S1b → `Extraneous`, not `Skipped`) and `Terminal`/`RuleName` metadata (D4). The
public `RecoveryDiagnostics` list is UNCHANGED (6.1.2c); soft-separator (D2) is out of scope (6.1.2b2);
`DiagnosticDerivation.cs` (6.1.1) + its tests, S0, and all `.csproj` files are untouched.

## Mechanism chosen: B (post-hoc position+shape match) for ALL strategies, at SESSION END

Mechanism A (attach at node-creation in the engine) is only directly possible for the engine-created
nodes (S2/S3 memo-absorber branches, S6). But the decisive factor is WHERE the attach must survive:
**iterative recovery re-parses from `currentStartPos` on every loop iteration**, and injection nodes
(S1/S1b/S2-inj/S3-inj/S4/S5 — created by `CreateInjectedResult` on every consumption) are **fresh
instances each re-parse**. An attach at candidate-accept time k would be lost when iteration k+1
re-parses and recreates the node (the side-table is keyed by reference identity). The memo-patched
nodes (S2/S3/S6 absorbers) DO persist across iterations via the memo, so a per-accept attach would
work for them but not for the injection nodes — two mechanisms, one of them unreliable. A **single
position+shape match against the FINAL tree at session end** covers both uniformly, in one place,
with no engine changes.

The match (per accumulated `_recoveryDiagnostics` entry) is reliable because:
- **region diagnostic** (`StartPos < EndPos`) — absorber (S1b/S2/S3-inj/S5/S6): matches the
  `IsRecovery + IsAbsorber` node with the **exact** `[StartPos..EndPos)` span. Unique: one candidate
  is accepted per recovery point, recovery points strictly increase (`e <= ePrev` break), and the
  injection/engine absorber spans are exact (`CreateInjectedResult`: `EndPos = pos + Length`;
  engine: `new TerminalNode("Skipped", e, S, ...)`).
- **zero diagnostic** (`StartPos == EndPos`) — insertion (S1/S2-inj/S4): matches the zero-width
  `IsRecovery` node at `StartPos`; when the diagnostic carries a `Terminal`, only a node with
  `Kind == Terminal.Kind` (injections create the node with `NodeKind == t.Kind`).
- **`Unrecovered`** — never matched (by design NOT a tree node, A4-2; it is the zero marker added by
  `AddUnrecoveredIfS6`).
- **Soft-separator (D2)** — its node is a regular non-`IsRecovery` terminal, so it is not matched
  here (6.1.2b2).

Note: the derived list therefore returns the accumulated `Inserted`/`Skipped`/`Extraneous`
diagnostics (as the EXACT same instances) but not `Unrecovered` — same split as the 6.1.2 stop
report (the Unrecovered marker is result-derived, A4-2).

## Attach points

All in `ExtensibleParser/Parser.Recovery.cs` (no `RecoveryEngine.cs` change):
- `Recover` — the three exit points now return `FinishRecovery(result)`:
  clean success (`Parser.Recovery.cs:500`), recovered-in-iteration (`Parser.Recovery.cs:627`),
  and the loop-end return (`Parser.Recovery.cs:633`).
- `FinishRecovery(Result)` (`Parser.Recovery.cs:642-657`): extracts the final tree node
  (`TryGetSuccess` → `TryGetPartial`; Failure has no node → no-op) and calls `AttachDiagnosticsToTree`.
- `AttachDiagnosticsToTree(ISyntaxNode)` (`Parser.Recovery.cs:666-685`): no-op when
  `_recoveryDiagnostics` is empty (clean parse); otherwise collects all `IsRecovery` `TerminalNode`s
  once and, per diagnostic, attaches it to the first matching node via the existing
  `AttachDiagnostic(node, diag)` hook.
- `CollectRecoveryNodes` (`Parser.Recovery.cs:688-709`) — full-tree walk (`SeqNode.RawElements`,
  `ListNode.RawElements` + `Delimiters`, `SomeNode.Value`), same walk shape as `CollectAttached`.
- `MatchesRecoveryNode` (`Parser.Recovery.cs:711-721`) — the position+shape predicate above.

Per strategy: **S1** insertion → zero-width node at `e` with `Kind == t.Kind`; **S1b** absorber →
absorber `[e..e+len)`; **S2** memo-absorber branches → absorber `[e..resyncPos)` (engine node,
persists via memo), injection branch → absorber `[e..resyncPos)` / zero-width at `e`; **S3**
memo-absorber branches → absorber `[e..foundS)`, injection branch → absorber `[e..foundS)`;
**S4** → zero-width at `input.Length` per inserted terminal; **S5** → absorber `[e..EOF)`;
**S6** → engine absorber `[start..s)` (persists via the start-rule memo patch). All seven
(S1b/S1/S3/S6/S2/S4/S5) are covered by the same matcher.

## Test — `Tests/ParserTests/Recovery/SideTableEngineTests.cs` (new, 3 tests)

1. **D1** `Test_D1_SingleTokenDeletion_DerivedIsExtraneousWithMetadata` — `S := 'a' 'b'`, input
   `"aab"` (S1b). The S1b absorber satisfies the `'b'` slot at `[1..2)`, so the real `'b'@2` is
   trailing garbage: accumulated = `Extraneous [1..2)` (S1b) + `Skipped [2..3)` (S6 floor) +
   `Unrecovered [2..2)` (marker, not a node). Derive returns exactly the first two — the S1b one as
   the **exact accumulated instance** with `Kind == Extraneous` (NOT `Skipped`), `Terminal` = `b`,
   `RuleName` = `S`; asserts no `Skipped` covers the `[1..2)` span.
2. **D4** `Test_D4_S1Insertion_DerivedCarriesTerminalAndRuleName` — IterativeRecoveryTests grammar,
   input `"int f ) { int x; }"` (missing `'('`, no OftenMissed for it → S1 insertion at `e=6`).
   Derive returns exactly the accumulated `Inserted [6..6)` as the exact instance with non-null
   `Terminal` (`Kind == "("`) and non-null `RuleName` (`"Function"` — the top frame's rule name).
3. **Iterative** `Test_Iterative_TwoInsertions_BothDerived` — `"int f ) { int x; } int g ) { int y; }"`:
   two S1 insertions across two iterations (`e=6`, `e=25`). Pins the session-end design: f's node
   instance survives via the memo, g's is fresh — derive returns both, in position order, as the
   exact accumulated instances with full metadata.

## Test results (one-shot)

- `dotnet test Tests/ParserTests` — **Total: 405 · Passed: 403 · Failed: 0 · Skipped: 2** (the 2
  skipped are the pre-existing `[Ignore("WIP")]`; +3 vs the 6.1.2a baseline of 402 is exactly the 3
  new `SideTableEngineTests`).
- `dotnet test Tests/CSharpGrammarTests` — **Total: 1527 · Passed: 1524 · Failed: 0 · Skipped: 3** (no regression).
- `dotnet test Tests/CsPreprocessorTests` — **Total: 128 · Passed: 128 · Failed: 0** (no regression).

## Files changed

- `ExtensibleParser/Parser.Recovery.cs` — **modified**: `FinishRecovery` + `AttachDiagnosticsToTree`
  + `CollectRecoveryNodes` + `MatchesRecoveryNode`; the three `Recover` exit points wrapped in
  `FinishRecovery`; one comment update on `AttachDiagnostic`.
- `Tests/ParserTests/Recovery/SideTableEngineTests.cs` — **new**: 3 tests.
- `docs/RecoveryImprovementPlan-progress6.1.md` — this file.

No `RecoveryEngine.cs` change, no `.csproj` change, public `RecoveryDiagnostics` list unchanged,
`DiagnosticDerivation.cs` (6.1.1) + its tests untouched, S0 untouched, no stop-if triggered.
**Not committed.**

## Deviations

- **csproj build fix (required), net-zero vs HEAD.** The same external process documented in the
  6.1.1/6.1.2/6.1.2a sections added explicit
  `<Compile Include="Recovery\SideTableEngineTests.cs" />` and
  `<Compile Include="Recovery\SideTableTests.cs" />` to `ParserTests.csproj` (NETSDK1022: Duplicate
  'Compile' items — SDK default globbing already includes both files). Both lines were removed,
  restoring `ParserTests.csproj` to its committed (HEAD) state; net `.csproj` diff vs HEAD is zero.
  The same process also added a BOM to `SideTableTests.cs` (cosmetic, no content change); the file
  was restored to its committed state via `git checkout`.
- **Session-end attach, not per-accept.** The task's mechanism B said "at candidate-accept time";
  the attach is instead done once at session end (same file, same loop). Rationale: iterative
  re-parses recreate injection nodes as fresh instances, so a per-accept attach is lost by the next
  iteration (test 3 pins this). The position+shape correlation itself is exactly as specified.
- **Derived list excludes `Unrecovered`** (no tree node, A4-2) — same split as the 6.1.2 stop
  report; combining it with the result marker is 6.1.2c's job.
- Not committed.

---

# 6.1.2b2 (A5-2) — attach the SOFT-SEPARATOR diagnostic to its node (D2)

Sub-point 6.1.2b2 closes **D2**: the soft-separator (5b.3.2, A5-5) `Skipped` diagnostic is attached
to the consumed soft-separator node **directly, at the emission point** in `ParseSeparatedList` —
unlike the 6.1.2b session-end match, which only sees `IsRecovery` nodes and therefore cannot match
this one (the consumed node is a **regular terminal**, `IsRecovery == false`). The public
`RecoveryDiagnostics` list is UNCHANGED (6.1.2c); the 6.1.2b session-end match,
`DiagnosticDerivation.cs` (6.1.1), S0–S6, and all `.csproj` files are untouched.

## Attach point

`ExtensibleParser/Parser.cs:1021-1028` — the 5b.3.2 soft-separator block in `ParseSeparatedList`
(the `if (softResult.TryGetSuccess(out var softNode, out var softNewPos))` branch, `Parser.cs:1009`):

- **Node attached**: `sepNode` **after** the 5b.3.2 reassignment (`sepNode = softNode.AssertIsNonNull();`,
  `Parser.cs:1016`) — i.e. the node returned by `ParseAlternative(softSeparator, currentPos, input)`
  (the consumed soft separator, a regular `TerminalNode`, `IsRecovery == false`). It is the exact
  instance later added to `ListNode.Delimiters` (`delimiters.Add(sepNode)`, `Parser.cs:1053`), so
  `DeriveRecoveryDiagnostics`'s walk (which covers `ListNode.Delimiters`) reaches it.
- **Diagnostic attached**: `softDiagnostic` — the very `RecoveryDiagnostic(currentPos, softNewPos,
  RecoveryKind.Skipped, "soft separator '<Kind>' accepted in place of required separator",
  softSeparator, listRule.Kind)` instance emitted at `Parser.cs:1012-1014` (the exact accumulated
  instance, so derive returns it by reference).

## Guards mirrored

The attach is under the **exact same guard as the emission** (`Parser.cs:1024`):
`if (!SuppressSideEffects && !_recoveryDiagnostics.Contains(softDiagnostic))` — both the
`!SuppressSideEffects` suppression (speculative parses) and the dedup (re-parse must not duplicate
the diagnostic for the same position). The `Add` and the `AttachDiagnostic` share ONE guard block,
so the attach happens **exactly when** the diagnostic is actually recorded (a post-`Add` re-check of
`Contains` would always be false-positive, so the two statements are in the same `if`). The
local reassignments (`sepNode`/`newPos`/`gotSuccess`/`gotPartial`) moved above the guard — pure
local assignments, behavior-neutral reordering; node construction is unchanged
(`softNode.AssertIsNonNull()` as before).

## D2 test — `Tests/ParserTests/Recovery/SideTableSoftSepTests.cs` (new, 2 tests)

Grammar from `SoftSeparatorTests`: `Item = Number`; `List = SeparatedList(Item, ",", Kind: "List",
SoftSeparator: ";")`.

1. **`Test_D2_SoftSeparator_SkippedDiagnosticDerivedFromNode`** — input `"1;2"`. Single pass
   (Success@EOF, no engine). Accumulated list = exactly one `Skipped [1..2)` with `Terminal` = `;`,
   `RuleName` = `List`. `DeriveRecoveryDiagnostics(root)` returns exactly that diagnostic — the
   **exact accumulated instance** (`ReferenceEquals`) with full metadata, at `[1..2)`. Before this
   sub-point this input yielded **nothing** for the soft separator (D2 open: the session-end match
   cannot see a non-`IsRecovery` node).
2. **`Test_D2_SoftSeparator_Multiple_AllDerived`** — input `"1;2;3"`. Two soft separators consumed;
   derive returns both `Skipped` diagnostics (at `[1..2)` and `[3..4)`) as the exact accumulated
   instances, in position order.

## Test results (one-shot)

- `dotnet test Tests/ParserTests` — **Total: 407 · Passed: 405 · Failed: 0 · Skipped: 2** (the 2
  skipped are the pre-existing `[Ignore("WIP")]`; +2 vs the 6.1.2b baseline of 405 is exactly the 2
  new `SideTableSoftSepTests`; both verified 2/2 by a `--filter FullyQualifiedName~SideTableSoftSepTests` run).
- `dotnet test Tests/CSharpGrammarTests` — **Total: 1527 · Passed: 1524 · Failed: 0 · Skipped: 3** (no regression).
- `dotnet test Tests/CsPreprocessorTests` — **Total: 128 · Passed: 128 · Failed: 0** (no regression).

## Files changed

- `ExtensibleParser/Parser.cs` — **modified**: the 5b.3.2 soft-separator block in `ParseSeparatedList`
  (`Parser.cs:1009-1029`): reordering of the local reassignments + the single shared guard now
  containing `AttachDiagnostic(sepNode, softDiagnostic)` alongside the existing
  `_recoveryDiagnostics.Add(softDiagnostic)`.
- `Tests/ParserTests/Recovery/SideTableSoftSepTests.cs` — **new**: 2 tests.
- `docs/RecoveryImprovementPlan-progress6.1.md` — this file.

No session-end-match change (6.1.2b), no `DiagnosticDerivation.cs` (6.1.1) change, S0–S6 untouched,
public `RecoveryDiagnostics` list unchanged, no stop-if triggered. **Not committed.**

## Deviations

- **csproj build fix (required), net-zero vs HEAD.** The same external process documented in the
  6.1.1/6.1.2/6.1.2a/6.1.2b sections added
  `<Compile Include="Recovery\SideTableSoftSepTests.cs" />` to `ParserTests.csproj` mid-session
  (NETSDK1022: Duplicate 'Compile' items — SDK default globbing already includes the file; the
  identical event was observed at 22:57 for the 6.1.2b files and fixed the same way). The line was
  removed, restoring `ParserTests.csproj` to its committed (HEAD) state; `git diff` on the `.csproj`
  is empty (net-zero). The task's "do not modify any .csproj" is honored in net effect — the only
  edit was the required removal of the external duplicate, exactly per the established pattern.
  (An external workaround file `C:\Users\user\AppData\Local\Temp\opencode\fix-dup.targets` was used
  transiently to verify the suite before the `.csproj` restore; it lives outside the repo.)
- Not committed.
