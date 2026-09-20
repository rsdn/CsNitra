# Progress — 5b.3.1 (A5-5 SoftSeparator)

## What was added

A `SoftSeparator` option to the `SeparatedList` rule type (`ExtensibleParser/Rules.cs`).

Exact declaration (primary-constructor record parameter, default `null`):

```csharp
public record SeparatedList(
    Rule Element,
    Rule Separator,
    string Kind,
    SeparatorEndBehavior EndBehavior = SeparatorEndBehavior.Optional,
    bool CanBeEmpty = true,
    Terminal? SoftSeparator = null)
    : Rule(Kind)
```

Plus an XML doc comment for the new parameter, matching the existing parameter docs.

## Pattern followed

`SeparatedList` is a `record` with a **primary constructor**; its existing optional
settings (`EndBehavior`, `CanBeEmpty`) are declared as **constructor parameters with
default values** — there is no separate static factory method or property block for
them (the task mentioned a factory-method argument; none exists on this type, so the
mirrored pattern is constructor-parameter-only, which also acts as the property via
the record's positional parameters).

`SoftSeparator` was appended after `CanBeEmpty` with default `null`, mirroring
`SeparatorEndBehavior EndBehavior = SeparatorEndBehavior.Optional` /
`bool CanBeEmpty = true`.

Additionally, `InlineReferences` carries the new option through, exactly as it
already does for `EndBehavior` and `CanBeEmpty`:

```csharp
return new SeparatedList(inlinedElement, inlinedSeparator, Kind, EndBehavior, CanBeEmpty, SoftSeparator);
```

## Scope

- Declaration only. No parsing behavior (that is 5b.3.2). `SoftSeparator` is not
  read by any parser/recovery code yet; default `null` changes no existing behavior.
- Only `ExtensibleParser/Rules.cs` was modified.

## Build result

`dotnet build` from repo root: **succeeded, 0 errors** (Debug, .NET 8, SDK 8.0.100 pin,
`--no-incremental` check also green).

---

# Progress — 5b.3.2 (A5-5 SoftSeparator behavior)

## What changed in `ParseSeparatedList` (`ExtensibleParser/Parser.cs`)

Single insertion point: the separator-mismatch branch of the element loop. The required
separator is parsed once per iteration at `Parser.cs:992`
(`ParseAlternative(listRule.Separator, currentPos, input)`); on full mismatch
(`!gotSuccess && !gotPartial`) the old code went straight to `EndBehavior.Required`
→ Partial/Failure or `break` (Optional/Forbidden).

New block `Parser.cs:1000-1023` (between the separator parse and the existing
failure handling at `Parser.cs:1025-1039`, which is unchanged):

- Guarded by `listRule.SoftSeparator is { } softSeparator` — **strict no-op when
  `SoftSeparator` is null** (default): the new block is skipped entirely and control
  falls through to the pre-existing `if (!gotSuccess && !gotPartial)` handling, byte
  for byte.
- On required-separator mismatch, tries `ParseAlternative(softSeparator, currentPos,
  input)` (same dispatch used for the required separator; `MaxFailPos` propagated).
- On soft-separator success: emits ONE diagnostic, then reassigns `sepNode`/`newPos`
  and sets `gotSuccess = true; gotPartial = false;` so the existing loop body
  (`Parser.cs:1041-1047`) treats it exactly like a matched required separator —
  the node is added to `ListNode.Delimiters`, `currentPos` advances, and the next
  element is parsed. No S2/S3 candidate is generated; global recovery is not invoked.
- The soft separator node is a regular `TerminalNode` (not `IsRecovery`): the input
  really was consumed; the diagnostic carries the recovery annotation.

## Diagnostic emitted

`RecoveryDiagnostic` (the parser's diagnostic record; there is no `DiagnosticType`
enum — the kind enum is `RecoveryKind { Inserted, Skipped, Unrecovered, Extraneous }`,
`ExtensibleParser/Recovery/RecoveryDiagnostic.cs:3`):

```csharp
new RecoveryDiagnostic(currentPos, softNewPos, RecoveryKind.Skipped,
    $"soft separator '{softSeparator.Kind}' accepted in place of required separator",
    softSeparator, listRule.Kind);
```

Emitted via `_recoveryDiagnostics.Add(...)` (the `Parser`'s session diagnostic list,
`Parser.Recovery.cs:20`, same mechanism S0-S6 use), with two guards:

- `!SuppressSideEffects` — no diagnostic leak from speculative parses
  (mirrors `ReportMismatch`, `Parser.cs:860`);
- `!_recoveryDiagnostics.Contains(softDiagnostic)` — record-value dedup so a
  recovery-loop re-parse of the same list does not duplicate the diagnostic for the
  same position (re-parses happen after the list's memo entry is removed by Hygiene).

One diagnostic per consumed soft separator; N soft separators → N diagnostics.

## Test

`Tests/ParserTests/Recovery/SoftSeparatorTests.cs` (new) — grammar
`Item = Number; List = SeparatedList(Item, ",", Kind: "List", SoftSeparator: <param>)`,
`EmptyTerminal` trivia, `RecoveryTerminals.Number()` (shared recovery test terminals):

1. `SoftSeparator_Mismatch_IsRecoveredInline_WithSingleSkippedDiagnostic` — input `1;2`
   with `SoftSeparator: ";"`: Success@EOF, **exactly 1** `RecoveryKind.Skipped`
   diagnostic (`Terminal.Kind == ";"`, `[1..2)`, RuleName `List`), `ListNode` has 2
   `RawElements` + 1 `Delimiter` (no spurious elements), and **no global recovery**:
   `RecoveryPasses == 1`, `EngineGenerateCalls == 0`, `S2ScanPositions == 0`,
   `S3ScanPositions == 0`.
2. `SoftSeparator_Multiple_Mismatches_OneDiagnosticEach` — input `1;2;3`: exactly 2
   Skipped diagnostics at start positions `{1, 3}`, 3 elements / 2 delimiters,
   `RecoveryPasses == 1`, `EngineGenerateCalls == 0`.
3. `SoftSeparator_Null_NoInlineRecovery_GlobalRecoveryInstead` (control) — same input
   `1;2`, `SoftSeparator: null` (default): pre-existing behavior — the list stops at
   the mismatched separator (1 element, Optional end behavior) and the tail is handled
   by global recovery (`EngineGenerateCalls > 0 || RecoveryPasses > 1`); no
   soft-separator diagnostic is present.

## Results (from `C:\RSDN\CsNitra`)

| Suite | Result |
|---|---|
| `dotnet test Tests/ParserTests` | **Passed: 386, Failed: 0, Skipped: 2** (the 2 = pre-existing `[Ignore("WIP")]` in `GrammarValidationTests`; includes the 3 new tests, all passing) |
| `dotnet test Tests/CSharpGrammarTests` | **Passed: 1524, Failed: 0, Skipped: 3** (pre-existing `[Ignore]`: `StringLiteralRuleTests` ×2, `RawStringLiteralRuleTests` ×1), exit 0 |
| `dotnet test Tests/CsPreprocessorTests` | **Passed: 128, Failed: 0**, exit 0 |

## Deviation (documented, mirrors 1.3.6 / 5a.1)

The working tree contained a **pre-existing uncommitted** line in
`Tests/ParserTests/ParserTests.csproj` — `<Compile Include="Recovery\SoftSeparatorTests.cs" />`
(left by the 5b.3.1 session, before this task's file existed). With the file present,
that explicit include duplicates the SDK default `**/*.cs` glob
(`EnableDefaultCompileItems` is on) and fails **every** build of the project with
`NETSDK1022: Duplicate 'Compile' items` — verified for both direct
(`dotnet build Tests/ParserTests`) and solution (`dotnet build Nitra.sln`) builds.
The line was **removed**, returning the `.csproj` to its committed (HEAD) state; the
default glob compiles the new test file. This is a build-file fix undoing a
pre-existing, build-breaking duplicate — not a parser/engine/test change — and is the
same documented deviation as in `RecoveryImprovementPlan-progress1.3.6.md` and
`RecoveryImprovementPlan-progress5a.1.md`. In the final state `ParserTests.csproj`
is **unmodified from HEAD** (`git status` clean for it).

No other `.csproj` touched by this task. Pre-existing unrelated working-tree changes
left as-is: `Parsers/Cpp/CppInteropGenerator/CppInteropGenerator.csproj` (cosmetic
reformat only) and `docs/RecoveryImprovementPlan-checklist.md` / `docs/antlr4-analysis.md`.
