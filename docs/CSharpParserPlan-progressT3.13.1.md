# T3.13.1 — C# 13.0 `field` keyword

## Status
DONE — build clean, all tests green (CSharpGrammarTests 1482 passed / 3 skipped; ParserTests 325 passed / 2 skipped).

## Roslyn syntax found
- `SyntaxKind.FieldKeyword = 8412` — `src/Compilers/CSharp/Portable/Syntax/SyntaxKind.cs:349`.
- `FieldExpressionSyntax` — single-token node: `src/Compilers/CSharp/Portable/Generated/CSharpSyntaxGenerator/CSharpSyntaxGenerator.SourceGenerator/Syntax.xml.Main.Generated.cs:3015`
  (`SyntaxFactory.FieldExpression(SyntaxFactory.Token(SyntaxKind.FieldKeyword))`).
- Parsed in primary-expression position — `src/Compilers/CSharp/Portable/Parser/LanguageParser.cs:12009-12012`:
  `else if (IsCurrentTokenFieldInKeywordContext() && PeekToken(1).Kind != SyntaxKind.ColonColonToken)
  return _syntaxFactory.FieldExpression(this.EatContextualToken(SyntaxKind.FieldKeyword));`
- `IsCurrentTokenFieldInKeywordContext` — `LanguageParser.cs:6097-6102`:
  `CurrentToken.ContextualKind == SyntaxKind.FieldKeyword && IsInFieldKeywordContext &&
  IsFeatureEnabled(MessageID.IDS_FeatureFieldKeyword)`.
- Field-keyword context set in the property accessor body — `LanguageParser.cs:4286`, `4639`
  (`ParserSyntaxContextResetter(this, isInFieldKeywordContext: ...)`).
- Version gate: `field` is a C# 13.0 feature — `MessageID.IDS_FeatureFieldKeyword`
  (`MessageID.cs:290`), checked at `Binder_Expressions.cs:1572`.

## Rule written (Cs13.grammar)
1. `Primary = | FieldExpr = "field";` — the `field` keyword as a primary expression.
2. `ReservedKeyword = | "field";` — reserve `field` at v13 so `FieldExpr` is the sole Primary match
   (no equal-length tie with `IdentifierName`).
3. `Property = Attributes? PropertyModifier* Type TypeName "{" ExpressionBodiedAccessorList "}" ";"?;`
   + `ExpressionBodiedAccessorList` (`get => Expr ;` / `get => Expr ; set => Expr ;`) — expression-bodied
   accessors, re-declaring `Property` (append, T0.3 merge), mutually exclusive with the Cs1 (block) and
   Cs3 (auto) alternatives.

## Setup steps
- [x] Create `Parsers/CSharp/CSharpGrammar/Cs13.grammar` (CRLF/no BOM).
- [x] Add `<EmbeddedResource Include="Cs13.grammar" />` to `CSharpGrammar.csproj` (after Cs12).
- [x] Add `new(13, "Cs13.grammar", "Cs13.grammar"),` to `EmbeddedGrammar.cs` (after v12).
- [x] Check `CSharpVersionInfrastructureTests.cs` for a v>=13 grammar-list assertion — NONE exists
      (tests cover v1,4,5,6,7,8,11 only), so no update needed.

## Code iterations
- v1 (first attempt, PASSED on first build+run — no failed iterations):
  - Wrote Cs13.grammar with the three rules (FieldExpr primary, `field` reservation, expression-bodied
    accessors via re-declared `Property` + `ExpressionBodiedAccessorList`).
  - Design choice: reserve `field` at v13 (re-declare `ReservedKeyword`) so `FieldExpr` is the SOLE
    Primary match for `field` (avoids an equal-length tie with `IdentifierName = !ReservedKeyword
    Identifier`). Trade-off: a bare identifier use (`int field;`) is accepted at v<=12 but rejected at
    v13 — a documented deviation from Roslyn (where `field` is a contextual keyword valid as an
    identifier at every version). Not exercised by any test.
  - Design choice: expression-bodied accessors (`get => Expr ;`) are the REQUIRED-new-construct that
    gives version purity — at v12 the `=>` accessor form is absent, so `get => field;` REJECTS even
    though `field` is a valid identifier there.
  - Re-declared `Property` (append, T0.3 merge) with a new alternative using
    `ExpressionBodiedAccessorList` (the Cs3 pattern), NOT re-declaring `AccessorDeclaration` — avoids
    the tie the Cs3.grammar comment warns about (re-declaring `AccessorDeclaration` would let the Cs1
    `AccessorDeclaration+` also match the new form).
  - `ExpressionBodiedAccessorList` is a named union of two shapes (`ExprGetterOnly`, `ExprGetSet`);
    longest-match disambiguates them (no equal-length tie).

## Version-purity results
- v12 (no CS13): `get => field;` REJECT (no `=>` accessor form) — 3/3 version-purity negatives pass.
- v12 (no CS13): pre-existing property forms still parse (block-body + auto-property) — 2/2 regression pass.
- v13: `get => field;` / `set => field = value;` ACCEPT — 6/6 positives pass.

## Tests (Cs13FieldKeywordTests.cs, 12 total)
- Positives (v13): 6 — getter+setter, getter-only, setter-assignment, struct, multiple-properties,
  field-in-complex-getter (`field + 1`).
- Version-purity negatives (v12): 3 — getter+setter, getter-only, setter-assignment.
- Regression (v12): 2 — block-body property, auto-property (no CS13 leak).
- Negatives (v13, malformed): 1 — missing `;` after the expression body.

## Verification
- `dotnet build Nitra.sln --no-incremental` → Build succeeded, 0 Warning(s), 0 Error(s).
- `dotnet test Tests/CSharpGrammarTests` → Passed! Failed: 0, Passed: 1482, Skipped: 3, Total: 1485
  (the 3 skipped are pre-existing string-literal tests, unrelated).
- `dotnet test Tests/ParserTests` → Passed! Failed: 0, Passed: 325, Skipped: 2, Total: 327
  (the 2 skipped are pre-existing, unrelated).

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs13.grammar` (NEW, CRLF/no BOM).
- `Parsers/CSharp/CSharpGrammar/CSharpGrammar.csproj` (add `<EmbeddedResource Include="Cs13.grammar" />`).
- `Tests/CSharpGrammarTests/EmbeddedGrammar.cs` (add v13 to the version table).
- `Tests/CSharpGrammarTests/Cs13FieldKeywordTests.cs` (NEW, CRLF + UTF-8 BOM, 12 tests).
- `docs/CSharpParserPlan-progressT3.13.1.md` (NEW, this file).
- `docs/CSharpParserPlan-checklist.md` — already carried a T3.13.1 `[~]` entry from a prior step; left
  as `[~]` (NOT marked `[✅]`, per instructions — the orchestrator marks it after verifying/committing).
