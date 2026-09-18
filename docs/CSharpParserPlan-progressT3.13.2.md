# T3.13.2 — C# 13.0 `event field`

## Status
In progress.

## Task
Extend the grammar with `event field` (C# 13.0): an event declaration with an initializer (like a field).
Example:
```csharp
class MyClass {
    public event Action MyEvent = () => { };
}
```
CS13 only: `CreateParser(12)` must REJECT; `CreateParser(13)` must accept.

## Roslyn syntax found
- `EventFieldDeclarationSyntax : BaseFieldDeclarationSyntax`
  (`C:\RSDN\roslyn\src\Compilers\CSharp\Portable\Generated\CSharpSyntaxGenerator\CSharpSyntaxGenerator.SourceGenerator\Syntax.xml.Syntax.Generated.cs:12407`):
  - `attributeLists`, `modifiers`, `eventKeyword`, `declaration` (a `VariableDeclarationSyntax`), `semicolonToken`.
- `SyntaxKind.EventFieldDeclaration = 8874` (`Syntax/SyntaxKind.cs:827`).
- Parse entry: `ParseEventDeclaration` (`Parser/LanguageParser.cs:5082-5095`): eats `event`, parses a `Type`, then
  `IsFieldDeclaration(isEvent: true, ...)` ? `ParseEventFieldDeclaration` : `ParseEventDeclarationWithAccessors`.
- `IsFieldDeclaration` (`Parser/LanguageParser.cs:3429-3468`): treats the event as a FIELD when the token after the
  name is NOT `{` / `=>` / `(` / `.` / `::` / `<` — i.e. when followed by `=` (initializer), `;`, or `,`.
- `ParseEventFieldDeclaration` (`Parser/LanguageParser.cs:5222-5255`):
  `ParseFieldDeclarationVariableDeclarators(type, flags: 0, parentKind)` (each declarator may carry an `= Expression`
  initializer) then `EatToken(SyntaxKind.SemicolonToken)`.
  → shape: `event <Type> <Name> = <Expression> ;`

So an event field = `event` + Type + name + `=` + Expression + `;`.

## Existing grammar (Cs1.grammar:427-457)
```
Event = Attributes? EventModifier* "event" Type TypeName EventTail;
EventTail =
    | ";"
    | EventAccessors = "{" EventAccessor+ "}";
```
Two existing forms: `event Type Name;` and `event Type Name { add { } remove { } }`. The C# 13 event field adds a
third tail form: `= Expression ;`.

## Rule written
Re-declare `EventTail` (append, T0.3 merge) in `Cs13.grammar` to add the initializer tail:
```
EventTail =
    | EventFieldInit = "=" Expression ";";
```
The three `EventTail` alternatives are mutually exclusive by leading token (`;` vs `{` vs `=`) → no equal-length tie.
`Event` (Cs1) references `EventTail`, so it automatically picks up the merged third alternative. CS13-only: at v<=12
the `= Expression ;` tail is absent → `event ... = ...;` matches no `EventTail` → the `Event` rule fails → REJECT.

## Code iterations
- **Iteration 1 (grammar, worked first try):** re-declared `EventTail` (append) in `Cs13.grammar` with
  `EventFieldInit = "=" Expression ";"`. No changes needed — the merge picked up the third tail
  alternative, `Event` (Cs1) referenced the merged `EventTail`, and the lambda initializer
  `() => { }` parsed as an `Expression` (via `Primary` → `LambdaExpression`, Cs3.grammar:53-77).
  All 14 new tests green on the first run. No grammar failures to fix.
- **Iteration 2 (build — NETSDK1022, discovered + fixed):** the first `roslyn_run_dotnet_build`
  passed only because `CSharpGrammarTests` was already up-to-date. A true fresh build
  (`dotnet build Nitra.sln --no-incremental`) FAILED with `NETSDK1022` — duplicate `Compile` items
  `Cs13EventFieldTests.cs` + `Cs13FieldKeywordTests.cs` — caused by a PRE-EXISTING `<Compile Include>`
  `<ItemGroup>` in `CSharpGrammarTests.csproj` left by a prior subagent. The SDK auto-includes all `.cs`
  files, so the explicit items are duplicates. **Fix:** `git checkout -- Tests/CSharpGrammarTests/CSharpGrammarTests.csproj`
  (reverted the pre-existing csproj modification to its committed state; the SDK auto-include keeps both
  test files compiling). This is removing the exact `<Compile Include>` items the task warns about — the
  opposite of the forbidden action. Rebuild → 0 errors.

## Version-purity results
- `CreateParser(13)` ACCEPTS `class MyClass { public event Action MyEvent = () => { }; }` (and variants).
- `CreateParser(12)` REJECTS the same (the `= Expression ;` `EventTail` is absent at v<=12, so no
  `EventTail` alternative matches `= ...`, the `Event` rule fails, and the class body fails).

## Tests (pos/neg)
`Tests/CSharpGrammarTests/Cs13EventFieldTests.cs` — 14 tests, all green:
- POSITIVE v13 (6): lambda initializer (task example), struct, private, `null` initializer,
  anonymous-method initializer, multiple event fields.
- VERSION-PURITY v12 (2): lambda + `null` initializers REJECT at v12.
- REGRESSION (4): simple event `;` and event-with-accessors `{ add { } remove { } }` still parse at
  v12 AND v13 (no CS13 leak).
- NEGATIVE v13 malformed (2): missing expression (`= ;`) and missing semicolon (`= () => { } }`) REJECT.

## Verification
- `dotnet build Nitra.sln --no-incremental` → 0 errors (build succeeded).
- `dotnet test Tests/CSharpGrammarTests --filter FullyQualifiedName~Cs13EventFieldTests --no-build`
  → Passed 14, Failed 0.
- `dotnet test Tests/CSharpGrammarTests --no-build` (full, fresh build) → Passed 1496, Failed 0, Skipped 3.
- `dotnet test Tests/ParserTests --no-build` (regression) → Passed 325, Failed 0, Skipped 2.

## Working-tree hygiene
- Staged (T3.13.2 files): `Cs13.grammar`, `Cs13EventFieldTests.cs`, `CSharpParserPlan-progressT3.13.2.md`,
  `CSharpParserPlan-checklist.md` (pre-existing, left at T3.13.2 = `[~]`; NOT marked `[✅]`).
- `CSharpGrammarTests.csproj`: REVERTED to committed state (`git checkout --`) — a prior subagent had
  added a `<Compile Include>` `<ItemGroup>` (for `Cs13EventFieldTests.cs` + `Cs13FieldKeywordTests.cs`)
  that breaks a fresh build with `NETSDK1022`. Reverting removes exactly those items the task forbids
  adding; the SDK auto-includes the `.cs` files so nothing is lost. Now clean (no diff).
- NOT staged / left alone: `docs/antlr4-analysis.md`, `docs/RecoveryImprovementPlan.md` (unrelated).
- No commit made (orchestrator commits).

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs13.grammar` (add EventFieldInit to EventTail)
- `Tests/CSharpGrammarTests/Cs13EventFieldTests.cs` (new)
- `docs/CSharpParserPlan-progressT3.13.2.md` (this file)
