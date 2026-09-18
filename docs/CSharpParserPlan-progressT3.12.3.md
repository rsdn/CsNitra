# CSharpParserPlan — T3.12.3 progress (list patterns, C# 12.0)

Status: DONE

## Task
Extend the grammar with list patterns (C# 12.0): a new kind of PATTERN that matches a list.
```
if (list is [1, 2, 3]) { }
if (list is [var first, ..]) { }
if (list is [1, .., 3]) { }
```

## Roslyn syntax found
- `C:\RSDN\roslyn\src\Compilers\CSharp\Portable\Parser\LanguageParser_Patterns.cs`
  - `ParsePrimaryPattern` (187-252): `case SyntaxKind.OpenBracketToken: return this.ParseListPattern(inSwitchArmPattern);` (209-210) — a `[` in pattern position is a LIST PATTERN.
  - `ParseListPattern` (660-678): `openBracket = EatToken(OpenBracketToken)`, then `ParseCommaSeparatedSyntaxList(IsPossibleSubpatternElement, ParsePattern(Precedence.Conditional), allowTrailingSeparator:true, requireOneElement:false)`, then `EatToken(CloseBracketToken)`. Returns `ListPattern(openBracket, list, closeBracket, TryParseSimpleDesignation(...))`.
  - Slice pattern — `ParsePrimaryPattern` (211-216): `case SyntaxKind.DotToken when IsAtDotDotToken(): return _syntaxFactory.SlicePattern(EatDotDotToken(), IsPossibleSubpatternElement() ? ParsePattern(...) : null)` — a `..` optionally followed by a sub-pattern.
- Version gate: list patterns are a C# 12.0 feature (binder/semantic); the PARSER accepts the form wherever the rule exists. Version purity comes from the rule being Cs12-only.

## Existing pattern model (Cs7.grammar:130-135)
```
Pattern =
    | DiscardPattern     = "_"
    | DeclarationPattern = Type !ReservedKeyword Identifier
    | VarPattern         = "var" !ReservedKeyword Identifier
    | TypePattern        = Type
    | ConstantPattern    = !(!ReservedKeyword Identifier "=>") !("(" (LambdaParameter; ",")* ")" "=>") Expression;
```
`Pattern` is a plain (non-TDOPP) rule, used in `TypeIsPattern` (Cs7:108), `PatternCaseLabel` (Cs7:145), `SwitchArm` (Cs8:43).

## Rule written (Cs12.grammar)
```
Pattern =
    | ListPattern  = "[" (Pattern; ",")* "]"
    | SlicePattern = ".." Pattern?;
```
- `ListPattern` goes in the `Pattern` rule (a PATTERN, used in `is` / `case`), NOT in `Primary` (the collection expression from T3.12.2 is a PRIMARY EXPRESSION). Different syntactic contexts -> no conflict.
- `SlicePattern` models the `..` element required by the tests (`[var first, ..]`, `[1, .., 3]`). `..` is already a grammar literal (Cs8 range, Cs8.grammar:194/231/271), so it is matchable.

## Disambiguation
- `ListPattern` starts with `[`, which no existing Pattern alternative starts with -> sole match for `[` in pattern position.
- `SlicePattern` starts with `..`, which no existing Pattern alternative starts with -> sole match for `..` in pattern position.
- All-constant list pattern `[1, 2, 3]`: the Cs7 ConstantPattern (a full Expression, which at v12 includes the CollectionExpr) ALSO matches at the same length; the FIRST (Cs7 ConstantPattern) wins the tie, so `[1, 2, 3]` in a pattern context is parsed as a ConstantPattern/CollectionExpr, NOT a ListPattern. Documented semantic difference — the input still fully parses, and version purity is preserved (at v11 the CollectionExpr is absent, so `[1, 2, 3]` matches no Pattern alternative and REJECTS). The non-constant forms (`[var first, ..]`, `[1, .., 3]`) are parsed as a ListPattern (the ConstantPattern/CollectionExpr fails on the `var` / `..` element).

## Code iterations
1. First attempt: appended `Pattern = | ListPattern = "[" (Pattern; ",")* "]" | SlicePattern = ".." Pattern?;` to Cs12.grammar (re-declaring the Cs7 `Pattern` rule, T0.3 merge). Built the solution (`dotnet build Nitra.sln --no-incremental` → 0 errors). Wrote the 11 tests. Ran `dotnet test Tests/CSharpGrammarTests --filter FullyQualifiedName~Cs12ListPatternTests` → all 11 passed on the first run. No fixes were needed — the rule worked as written.
   - The `SlicePattern` was included (not just `ListPattern`) because the required tests `[var first, ..]` and `[1, .., 3]` contain the `..` element, which must itself be a valid `Pattern`. `..` is already a grammar literal (Cs8 range, Cs8.grammar:194/231/271), so `".."` is matchable.
   - The `Pattern?` trailing on `SlicePattern` mirrors Roslyn `SlicePattern(EatDotDotToken(), ... ? ParsePattern(...) : null)`; in the test inputs `..` is always followed by `,` or `]`, so `Pattern?` matches zero and it behaves as a bare `..`.

## Version-purity results
- v12 (`CreateParser(12)`): ACCEPTS all four forms — `[1, 2, 3]`, `[var first, ..]`, `[1, .., 3]`, `[]`.
- v11 (`CreateParser(11)`): REJECTS all four forms (the Cs12 ListPattern/SlicePattern are absent and the CollectionExpr is absent from the ConstantPattern/Expression, so `[...]` matches no Pattern alternative).
- Regression at v12: the C# 7.0 type `is` pattern (`x is int`) and the T3.12.2 collection expression (`var list = [1, 2, 3];`) still parse.

## Tests (pos/neg)
File: `Tests/CSharpGrammarTests/Cs12ListPatternTests.cs` (CRLF + UTF-8 BOM). 11 tests, all green.
- Positive (v12): `ListPattern_Simple_Succeeds` (`[1, 2, 3]`), `ListPattern_VarFirst_Succeeds` (`[var first, ..]`), `ListPattern_SliceMiddle_Succeeds` (`[1, .., 3]`), `ListPattern_Empty_Succeeds` (`[]`), `TypePattern_IsInt_StillSucceedsAtV12` (regression), `CollectionExpression_StillSucceedsAtV12` (regression) — 6.
- Negative (v11 version-purity): `ListPattern_Simple_RejectedAtV11`, `ListPattern_VarFirst_RejectedAtV11`, `ListPattern_SliceMiddle_RejectedAtV11`, `ListPattern_Empty_RejectedAtV11` — 4.
- Negative (v12 malformed): `ListPattern_MissingCloseBracket_Rejected` (`[1, 2 {` missing `]`) — 1.

## Verification
- `dotnet build Nitra.sln --no-incremental` → Build succeeded, 0 Warning(s), 0 Error(s).
- `dotnet test Tests/CSharpGrammarTests` (fresh build) → Passed: 1458, Failed: 0, Skipped: 3, Total: 1461.
- `dotnet test Tests/ParserTests` (regression) → Passed: 325, Failed: 0, Skipped: 2, Total: 327.

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs12.grammar` (added `ListPattern` + `SlicePattern` to the `Pattern` rule; CRLF / no BOM preserved)
- `Tests/CSharpGrammarTests/Cs12ListPatternTests.cs` (new; CRLF + UTF-8 BOM)
- `docs/CSharpParserPlan-progressT3.12.3.md` (this file)
- `docs/CSharpParserPlan-checklist.md` (NOT modified by this task — left as `[~]` for T3.12.3; the `[~]` was set by the prior T3.12.2 subagent and the `[✅]` mark is left to the orchestrator)
