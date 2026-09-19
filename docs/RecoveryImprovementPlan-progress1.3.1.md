# Wave 1 / 1.3.1 (A4-2) — S6 node shape (shape-preserving memo-patch node)

Status: done

## Task
Fix `RecoveryEngine.GenerateS6` so the memo-patch node it injects is **shape-preserving**
instead of a `SeqNode` whose `Kind` is the start rule's NAME. The old node hid the real prefix
behind a synthetic `SeqNode(startRule, ...)`, so downstream visitors saw the rule name as the
node `Kind` and could not reach the prefix's natural shape. The fix builds the patch node from
the **runtime type** of the successful prefix memo node, keeping the prefix's natural `Kind`
reachable and the absorber (`IsAbsorber`) in the tree (I4: covers `[0,EOF)`); result stays
`Success@EOF`. The test visitor is NOT modified.

## The shape applied per prefix type
`prefixNode` = the memo node at `(currentStartPos, startRule, 0)` (the complete successful
match from the first pass), or `null` when absent. Branch on its runtime type:

1. **Container — `SeqNode`:** same type, natural `Kind` (`seq.Kind`),
   `RawElements = seq.RawElements + [absorber]`, `StartPos = currentStartPos`, `EndPos = s`.
2. **Container — `ListNode`:** same type, natural `Kind` (`list.Kind`),
   `RawElements = list.RawElements + [absorber]`, `Delimiters` / `HasTrailingSeparator` /
   `IsRecovery` preserved from the prefix, `StartPos = currentStartPos`, `EndPos = s`.
3. **Leaf — `TerminalNode`:** `new SomeNode(terminal.Kind, terminal, currentStartPos, s)` —
   the parser's natural transparent wrapper (`Visit(SomeNode) => node.Value.Accept(this)`),
   so the leaf's natural `Kind` stays reachable.
4. **Null / absent:** the `absorber` `TerminalNode` itself is the root node (spanning
   `[parseEnd, s)`).

The absorber stays in `RawElements` (visible to structural walkers) and is filtered from the
public `Elements` by the existing `SyntaxTree.cs` logic — the exact S2/S3 mechanism, unchanged.

## Exact code (RecoveryEngine.cs, GenerateS6, lines 700-713)
```csharp
var absorber = new TerminalNode("Skipped", parseEnd, s, s - parseEnd, IsRecovery: true, IsAbsorber: true);
var prefixNode = parser.Memo.TryGetValue((currentStartPos, startRule, 0), out var prefixResult)
    && prefixResult.TryGetSuccess(out var pn, out _)
        ? pn
        : null;
ISyntaxNode node = prefixNode switch
{
    SeqNode seq => new SeqNode(seq.Kind, [.. seq.RawElements, absorber], currentStartPos, s),
    ListNode list => new ListNode(list.Kind, [.. list.RawElements, absorber], list.Delimiters, currentStartPos, s, list.HasTrailingSeparator, list.IsRecovery),
    TerminalNode terminal => new SomeNode(terminal.Kind, terminal, currentStartPos, s),
    _ => absorber
};
// MaxFailPos = 0: чтобы IsRecoveryPosition(MaxFailPos) == false при e == S (иначе memo отклоняется).
var value = Result.Success(node, s, 0);
```
Only the `node` construction changed; `absorber` and `value = Result.Success(node, s, 0)` are
kept as-is.

## Files changed
- `ExtensibleParser/Recovery/RecoveryEngine.cs` — `GenerateS6` memo-patch node is now built
  shape-preserving from the prefix's runtime type (was a `SeqNode` with the start-rule NAME as
  `Kind`).
- `docs/RecoveryImprovementPlan-progress1.3.1.md` — this progress file (new).

No test visitor modified. `SyntaxTree.cs` not modified (no change needed). S6 budget-exemption /
`MaxRecoveryIterations` NOT touched (sub-point 1.3.2).

## Test results
Build (`dotnet build Tests/ParserTests/ParserTests.csproj`): 0 errors.
- S6BottomTests (`--filter "FullyQualifiedName~S6BottomTests"`): **5 passed / 0 failed** —
  these use CONTAINER prefixes and force S6 to be reached; they exercise the container branch.
- SeparatedListTests (checklist acceptance, `--filter "FullyQualifiedName~SeparatedListTests"`):
  **20 passed / 0 failed**.
- Full suite (`dotnet test Tests/ParserTests/ParserTests.csproj`):
  **Passed 338, Failed 0, Skipped 2, Total 340.**
  The 2 skipped are pre-existing (`ShouldReportErrorForUndefinedRuleReference`,
  `RequiredSubruleNamesAreNotSpecified`). No delta vs 1.2 (338/340) — no test added here.

## Deviations
- The switch expression needs an explicit target type `ISyntaxNode` (not `var`): the four branch
  types (`SeqNode` / `ListNode` / `SomeNode` / `TerminalNode`) have no inferable best common
  type (CS8506 with `var`). `ISyntaxNode` is the type `Result.Success` consumes, so the explicit
  annotation is the minimal, correct form.
- The **leaf** branch (`SomeNode`) is not exercised by the current tests: S6 is not reached for
  leaf-prefix inputs until 1.3.2 raises the budget. Per the task, no leaf-prefix test was added.
- `ListNode` preserves `Delimiters` / `HasTrailingSeparator` / `IsRecovery` from the prefix in
  addition to the mandated `Kind` / `RawElements` / `StartPos` / `EndPos` (its record has those
  extra positional members; preserving them is the shape-preserving choice).
