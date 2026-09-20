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
