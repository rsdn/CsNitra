# T3.12.2 — Collection expressions (C# 12.0)

Status: DONE

## Task
Extend the grammar with collection expressions (C# 12.0): `var list = [1, 2, 3];`, `var empty = [];`.
A collection expression is a PRIMARY expression that starts with `[`.

## Roslyn syntax found (C:\RSDN\roslyn, main)
- `ParseCollectionExpression` — `src/Compilers/CSharp/Portable/Parser/LanguageParser.cs:13264-13291`:
  `openBracket = EatToken(OpenBracketToken)`, then `ParseCommaSeparatedSyntaxList(IsPossibleCollectionElement,
  ParseCollectionElement, allowTrailingSeparator:true, requireOneElement:false)`, then
  `closeBracket = EatToken(CloseBracketToken)`. Returns `CollectionExpression(openBracket, list, closeBracket)`.
- `IsPossibleCollectionElement` (13293-13296) = `IsPossibleExpression()` — each element is an expression.
- `ParseCollectionElement` (13298+): expression / spread (`...`) / with-element; the minimal task models expressions only.
- Dispatch — `ParsePrimaryExpressionWithoutPostfix`, `case SyntaxKind.OpenBracketToken`
  (LanguageParser.cs:12023-12026): a `[` in primary (expression) position is a collection expression
  (unless a lambda). => a collection expression is a PRIMARY expression.
- `CollectionExpressionSyntax` — `Generated/CSharpSyntaxGenerator/.../Syntax.xml.Main.Generated.cs:4193-4237`:
  `OpenBracketToken` + `SeparatedSyntaxList<CollectionElementSyntax> Elements` + `CloseBracketToken`.
- Version gate: collection expressions are a C# 12.0 binder/semantic feature; the PARSER accepts the form
  wherever the rule exists. Version purity comes from the rule being Cs12-only.

## Rule written (Cs12.grammar)
Re-declared `Primary` (T0.3 append-merge, same pattern as Cs11.grammar:43-45 RawStringLiteral) to APPEND:
```
Primary =
    | CollectionExpr = "[" (Expression; ",")* "]";
```
- `[` + comma-separated expression list (empty allowed) + `]`.
- Disambiguation: no existing Primary alternative starts with `[` (they start with a keyword / identifier /
  quote / digit / `(`), so `CollectionExpr` is the SOLE match for a `[` in primary position -> no tie. The
  `PostfixOp Indexer = "[" ... "]"` (Cs1.grammar:776) is a POSTFIX op (requires a preceding primary), so it
  never competes with a primary-position `[`.
- Boundary: default (Forbidden) trailing-separator behavior (like Cs1 ArrayInitializer, Cs1.grammar:802), so
  `[1, 2, 3,]` (Roslyn allowTrailingSeparator:true) is rejected — minor deviation, acceptable for the minimal
  task. Spread (`...x`) and with-element (`with(...)`) elements are out of scope (expressions only).

## Code iterations
(1) Wrote the rule as above (re-declared Primary, appended CollectionExpr). Built -> 0 errors.
    The sole-match disambiguation held on the first try: no existing Primary alternative starts with `[`,
    and the PostfixOp Indexer `[...]` is a postfix op (needs a preceding primary), so it never competes
    with a primary-position `[`.
(2) Wrote `Cs12CollectionExpressionTests.cs` (CRLF + UTF-8 BOM). Ran the suite -> green.
(3) FULL solution build (`dotnet build Nitra.sln --no-incremental`) FAILED with `NETSDK1022`
    (Duplicate 'Compile' items: `Cs12CollectionExpressionTests.cs`). Root cause: a PREVIOUS (dying)
    subagent had left a spurious `<Compile Include="Cs12CollectionExpressionTests.cs" />` in
    `CSharpGrammarTests.csproj` (visible via `git diff`). The SDK auto-includes all `.cs` files, so the
    explicit include is a duplicate -> NETSDK1022. Fix: `git checkout -- CSharpGrammarTests.csproj` to
    revert the csproj to its committed state (no explicit Compile item). Rebuilt -> 0 errors. This is the
    exact anti-pattern the task warns about; the csproj was restored to the correct (auto-include) form.

## Version-purity results
- v12: `class C { void M() { var list = [1, 2, 3]; } }` PARSES.
- v11: same input REJECTS (CollectionExpr absent -> the `[1, 2, 3]` initializer matches no Primary alternative).
- Confirmed by the 3 version-purity negative tests (all pass).

## Tests (pos/neg) — 11 total in Cs12CollectionExpressionTests, all pass
- Positives (v12): 7
  - `CollectionExpression_Simple_Succeeds` — `var list = [1, 2, 3];`
  - `CollectionExpression_Empty_Succeeds` — `var empty = [];`
  - `CollectionExpression_SingleElement_Succeeds` — `var x = [1];`
  - `CollectionExpression_IdentifierElements_Succeeds` — `var x = [a, b, c];`
  - `CollectionExpression_InvocationElement_Succeeds` — `var x = [f(1)];`
  - `ArrayLiteral_StillSucceedsAtV12` (regression) — `int[] a = new int[] { 1, 2, 3 };`
  - `Indexer_StillSucceedsAtV12` (regression) — `int[] a; a[1, 2] = 0;`
- Negatives: 4
  - `CollectionExpression_Simple_RejectedAtV11` (version-purity)
  - `CollectionExpression_Empty_RejectedAtV11` (version-purity)
  - `CollectionExpression_SingleElement_RejectedAtV11` (version-purity)
  - `CollectionExpression_MissingCloseBracket_Rejected` (v12, malformed: `[1, 2;`)

## Verification
- `dotnet build Nitra.sln --no-incremental` -> 0 errors (after reverting the spurious csproj Compile item).
- `dotnet test Tests/CSharpGrammarTests` -> Total 1450 · Passed 1447 · Failed 0 · Skipped 3 (all green).
- `dotnet test Tests/ParserTests` -> Total 327 · Passed 325 · Failed 0 · Skipped 2 (all green, regression).

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs12.grammar` (added CollectionExpr to Primary)
- `Tests/CSharpGrammarTests/Cs12CollectionExpressionTests.cs` (new, CRLF + UTF-8 BOM)
- `docs/CSharpParserPlan-progressT3.12.2.md` (this file)
- `Tests/CSharpGrammarTests/CSharpGrammarTests.csproj` (REVERTED to committed state — removed a spurious
  `<Compile Include>` left by a previous subagent that caused NETSDK1022; NOT a T3.12.2 change, restored
  to the correct auto-include form)
