# T3.6.2 — C# 7.0 pattern matching

Status: done (with one documented boundary decision — D1: `case int when y > 0:` rejects in this grammar, unlike Roslyn).

## Goal
Add C# 7.0 pattern matching to the EXISTING `Parsers/CSharp/CSharpGrammar/Cs7.grammar` (which already has the tuple rules from T3.6.1):
- Type patterns: `x is int`, `x is string`.
- Declaration patterns: `x is int y`.
- Constant patterns: `x is 5`, `x is "a"`.
- Discard: `x is _`.
- Guard `when`: `case int x when x > 0:` (switch case guard).
- Switch patterns: `case int x:`, `case 5:`, `case var x:`.

CS7 only. `CreateParser(6)` must REJECT declaration patterns, pattern switch, and guards. `CreateParser(7)` must accept them. `x is int` (type pattern, C# 1.0) must STILL parse at v1–v6.

## Cs1 structure found
- **`is` operator** — a TDOPP **postfix** operator (binary operator at the `Relational` level), a NAMED alternative of `Expression`:
  `TypeIs = Expression : Relational "is" Type` (Cs1.grammar:662). The LHS is `Expression : Relational` (minPrecedence = Relational bp 12); the RHS is a plain `Type`. `TypeAs` (663) is the sibling (`"as"`).
- **Engine TDOPP split** (`Parser.BuildTdoppRulesInternal`, Parser.cs:123-161): an alternative whose FIRST element is a `Ref`/`ReqRef` to the rule itself becomes a **postfix operator** (`rest` = the elements after the self-ref, precedence from the `ReqRef`). `ParseRule` (Parser.cs:223-335) tries each PREFIX (base alternative) then applies all applicable POSTFIXES greedily via `ContinueFromPartialPostfix` (Parser.cs:337-424), keeping the longest; equal-length ties keep the FIRST postfix (declaration order). So adding a NEW `is` postfix that extends FURTHER than `TypeIs` wins for the longer pattern forms.
- **`switch` case** — `SwitchStatement = "switch" "(" Expression ")" "{" SwitchSection* "}"` (Cs1:885); `SwitchSection = SwitchLabel+ Statement*` (892); `SwitchLabel = CaseLabel | DefaultLabel` (896-898); **`CaseLabel = "case" Constant ":"`** (901); `DefaultLabel = "default" ":"` (904).
- **`Constant`** (Cs1:469-477): `true | false | StringLiteral | VerbatimStringLiteral | CharLiteral | UnaryConstant = UnarySign Constant | DecimalConstant | HexConstant`. So `case 5:` / `case "a":` / `case 'a':` / `case true:` are all `Constant`s (valid at every version).
- **`Type`** (Cs1:508-512): `PredefinedType | QualifiedName | PointerType | ArrayType`. A true identifier (e.g. `_`, `y`) is a `Type` via `QualifiedName = TypeName = !ReservedKeyword Identifier`. A literal (`5`) is NOT a `Type`.
- **`ReservedKeyword`** (Cs1:537-618): `is`, `case`, `as`, `switch` ARE reserved. `_`, `when`, `var` are NOT reserved in Cs1 (`var` is appended in Cs2, Cs2.grammar:186-187). So `_` and `when` are true identifiers (`!ReservedKeyword Identifier`) at every version.
- **TDOPP precedence** (Cs1:623-626, merged with `TypeArray, TypePointer, Comma` at 631): 16 levels, `Comma` last (bp 1), `Relational` bp 12.

## Roslyn references (C:\RSDN\roslyn, main)
- **`is` pattern** — `ParseTypeOrPatternForIsOperator` (LanguageParser_Patterns.cs:21-31): "Parses the type, or pattern, right-hand operand of an is expression. ... Note that the syntax `_` will be parsed as a type." `IsPatternExpression` built at LanguageParser.cs:11936.
- **Pattern forms** — `ParsePrimaryPattern` (LanguageParser_Patterns.cs:187-252) + `ParsePatternContinued` (284-398):
  - Discard: `UnderscoreToken` → `DiscardPattern` (202-205) [switch]; in `is`, converted to `IdentifierName` (type) (28).
  - Var: `var` + designation → `VarPattern` (290-297) — `var` is parsed as a type (IdentifierName) first, then reclassified.
  - Declaration: `type != null` + designation → `DeclarationPattern` (358-362).
  - Type: `type != null`, no designation, not convertible to expression → `TypePattern` (367).
  - Constant: fallback `ParseSubExpression` → `ConstantPattern` (250-251).
- **Switch pattern label** — `CasePatternSwitchLabel(caseKeyword, pattern, ParseWhenClause(...), colon)` (LanguageParser.cs:10271-10275). `ParseWhenClause` (10704-10714): `when` + `ParseSubExpression`; returns null if not `when`. So the guard is BETWEEN the pattern and the `:`.
- **`when` in switch arm is always the guard keyword** — `IsValidPatternDesignation` (LanguageParser_Patterns.cs:407-416): `case WhenKeyword: return !inSwitchArmPattern;` (so `when` is NOT a designation in a switch arm).
- **Bare `var` / `when` are contextual keywords** — `VarIsContextualKeywordForPatterns01/02` (PatternParsingTests.cs:2813/2849): `case var:` → `CaseSwitchLabel` with `IdentifierName("var")` (a constant, NOT a var pattern); `if (e is var)` → `IsExpression` with `IdentifierName("var")` (a type). A var pattern REQUIRES `var` + a designation.
- **Syntax tests**:
  - `NotDiscardInIsTypeExpression` (PatternParsingTests.cs:5681): `e is _` → `IsExpression` + `IdentifierName("_")` (type, not discard).
  - `DeclarationExpressionTests.cs:227`: `if (e is int x ? true : false) {}` — declaration pattern in `is`.
  - `DeconstructionTests.cs:2676`: `if (e is int _) {}` — declaration pattern, discard designation.
  - `DeconstructionTests.cs:2865`: `switch (e) { case var _: break; }` — var pattern.
  - `WhenAsPatternVariable01` (PatternParsingTests.cs:2879): `switch (e) { case var when: break; }` → ERROR (CS1525) — `when` is the guard keyword, not a designation.
- All confirmed CS7 features (pattern matching is a headline C# 7.0 feature).

## Diagnostic (current v6 behavior, before Cs7 pattern rules)
- D1 `if (x is int y) { }` v6 → REJECTS (no declaration pattern; `TypeIs` matches `x is int`, leaving ` y` unmatched before `)`).
- D2 `switch (x) { case int y: break; }` v6 → REJECTS (`int y` is not a `Constant`).
- D3 `switch (x) { case int y when y > 0: break; }` v6 → REJECTS.
- D4 `switch (x) { case var y: break; }` v6 → REJECTS (`var` is reserved, not a `Constant`).
- D5 `if (x is int) { }` v6 → PARSES (type pattern via Cs1 `TypeIs`). (Must stay green at v1–v6.)
- D6 `if (x is 5) { }` v6 → REJECTS (`5` is not a `Type`).
- D7 `if (x is _) { }` v6 → PARSES (`_` is a valid type name via `QualifiedName`, so Cs1 `TypeIs` matches).

## Approach per feature
1. **`is` pattern (type/declaration/constant/discard)**: re-declare `Expression` (append, T0.3) to add a NEW postfix operator at the `Relational` level:
   `TypeIsPattern = Expression : Relational "is" Pattern`.
   - For `x is int`: Cs1 `TypeIs` and `TypeIsPattern` both match `is int` at the SAME length → the FIRST postfix (`TypeIs`, declaration order) wins → parsed as a type pattern (unchanged from v1).
   - For `x is int y` / `x is 5` / `x is _`: `TypeIsPattern` is LONGER (or the sole match) than `TypeIs` → `TypeIsPattern` wins.
2. **`Pattern`** (new Cs7 rule, shared by `is` and switch):
   ```
   Pattern =
       | DiscardPattern     = "_"
       | DeclarationPattern = Type !ReservedKeyword Identifier
       | VarPattern         = "var" !ReservedKeyword Identifier
       | TypePattern        = Type
       | ConstantPattern    = Expression;
   ```
   Longest-match disambiguates (see hand-traces). `DiscardPattern` first so it wins the equal-length tie for `_` (matching Roslyn's switch discard); in the `is` context the Cs1 `TypeIs` still wins the outer tie for `_` (so `x is _` is a type pattern, matching Roslyn `NotDiscardInIsTypeExpression`).
3. **Switch pattern + guard**: re-declare `CaseLabel` (append, T0.3) to add:
   `PatternCaseLabel = "case" Pattern ("when" Expression)? ":"`.
   - For `case 5:`: Cs1 `CaseLabel` (`"case" Constant ":"`) and `PatternCaseLabel` both match → Cs1 (first) wins → parsed as a constant case (unchanged from v1).
   - For `case int y:` / `case var y:` / `case int y when ...:`: Cs1 `CaseLabel` fails (not a `Constant`) → `PatternCaseLabel` is the sole match.
   - The guard `when Expression` is optional, BETWEEN the pattern and the `:` (Roslyn `ParseWhenClause`).

## Mutual-exclusivity hand-traces
### `is` operator (postfix `TypeIs` vs `TypeIsPattern`, both at Relational bp 12)
- `x is int` (type): `TypeIs` → `is int`; `TypeIsPattern` → `is` + `Pattern`(`int` via TypePattern) = `is int`. SAME length → FIRST (`TypeIs`) wins. Type pattern. ✓ (v1–v6 unchanged)
- `x is int y` (declaration): `TypeIs` → `is int` (stops, `y` left); `TypeIsPattern` → `is` + `Pattern`(`int y` via DeclarationPattern) = `is int y`. `TypeIsPattern` LONGER → wins. ✓
- `x is 5` (constant): `TypeIs` → `5` is not a `Type` → fails; `TypeIsPattern` → `is` + `Pattern`(`5` via ConstantPattern) = `is 5`. Sole match. ✓
- `x is "a"` (constant): `TypeIs` fails (`"a"` not a Type); `TypeIsPattern` → `is "a"`. Sole match. ✓
- `x is _` (discard): `TypeIs` → `Type`=`_` → `is _`; `TypeIsPattern` → `is` + `Pattern`(`_` via DiscardPattern) = `is _`. SAME length → FIRST (`TypeIs`) wins → type pattern (Roslyn-matching). ✓
- `x is` (missing pattern): `TypeIs` → `Type` fails on `)`; `TypeIsPattern` → `Pattern` fails on `)` (no alternative matches `)`). `Expression` = `x` only; `if` needs `)` after → `is` unmatched → REJECTS. ✓
- `x is int == y` (precedence): `is` (Relational bp 12) binds tighter than `==` (Equality bp 11); `TypeIs`/`TypeIsPattern` both match `is int` (tie → `TypeIs`), then `== y`. Unchanged from v1. ✓

### `Pattern` rule (longest-match, first-wins ties)
- `int` → TypePattern (sole; `int` not an Expression). `int y` → DeclarationPattern (len 5) > TypePattern (len 3). `5` → ConstantPattern (sole; `5` not a Type). `_` → DiscardPattern (first, len-1 tie). `var y` → VarPattern (sole; `var` reserved, not a Type/Expression). `y` (identifier) → TypePattern (first, len-1 tie with ConstantPattern). ✓

### Switch case (Cs1 `CaseLabel` vs Cs7 `PatternCaseLabel`)
- `case 5:` → Cs1 `Constant`=`5` and `PatternCaseLabel` both match → Cs1 (first) wins. ✓ (v1–v6 unchanged)
- `case int y:` → Cs1 `Constant` fails; `PatternCaseLabel` → `Pattern`=`int y` (DeclarationPattern), no `when`, `:`. Sole match. ✓
- `case var y:` → Cs1 fails; `PatternCaseLabel` → `Pattern`=`var y` (VarPattern). Sole match. ✓
- `case int y when y > 0:` → Cs1 fails; `PatternCaseLabel` → `Pattern`=`int y`, `when y > 0`, `:`. Sole match. ✓
- `case int when y > 0:` (malformed) → `Pattern` tries DeclarationPattern=`int when` (`when` is a true identifier, len 7) > TypePattern=`int` (len 3) → `int when`; then `("when" Expression)?` sees `y` (not `when`) → skip; then `:` must match `y` → FAILS. → REJECTS. (NOTE: Roslyn would ACCEPT this as a type pattern + guard, because Roslyn excludes `when` from designations in a switch arm; my grammar treats `when` as a designation, so it rejects. Documented boundary — see Deviations.)

## Version-purity results
- `if (x is int y) { N(y); }` → rejects at v6 / parses at v7. ✓
- `switch (x) { case int y: N(y); break; }` → rejects at v6 / parses at v7. ✓
- `switch (x) { case int y when y > 0: N(y); break; }` → rejects at v6 / parses at v7. ✓
- `switch (x) { case var y: N(y); break; }` → rejects at v6 / parses at v7. ✓
- `if (x is int) { }` (type pattern) → parses at v1–v6 AND v7 (unchanged). ✓
- All pre-existing Cs1/Cs2/Cs3/Cs4/Cs5/Cs6/Cs11 tests stay green (no Cs1–Cs6/Cs11 modification).

## Tests
`Tests/CSharpGrammarTests/Cs7PatternTests.cs` (CRLF + UTF-8 BOM) — **22 tests, all green**.
- POSITIVE (v7, `is` patterns, 6): `TypePattern_IsInt_Succeeds` (`if (x is int)`), `TypePattern_IsString_Succeeds` (`if (x is string)`), `DeclarationPattern_IsIntY_Succeeds` (`if (x is int y) { N(y); }`), `ConstantPattern_IsIntLiteral_Succeeds` (`if (x is 5)`), `ConstantPattern_IsStringLiteral_Succeeds` (`if (x is "a")`), `Discard_IsUnderscore_Succeeds` (`if (x is _)`).
- POSITIVE (v7, switch patterns, 4): `SwitchDeclarationPattern_Succeeds` (`case int y: N(y);`), `SwitchConstant_Succeeds` (`case 5: N();`), `SwitchGuard_Succeeds` (`case int y when y > 0: N(y);`), `SwitchVarPattern_Succeeds` (`case var y: N(y);`).
- POSITIVE (v1/v6, type pattern stays green, 2): `TypePattern_ParsesAtV1` (`if (x is int)` at v1), `TypePattern_ParsesAtV6` (`if (x is int)` at v6).
- POSITIVE (v6, constant switch stays green, 1): `SwitchConstant_ParsesAtV6` (`case 5: N();` at v6).
- NEGATIVE (v6, version-purity, 4): `DeclarationPattern_RejectedAtV6` (`if (x is int y)`), `SwitchPattern_RejectedAtV6` (`case int y:`), `SwitchGuard_RejectedAtV6` (`case int y when y > 0:`), `SwitchVarPattern_RejectedAtV6` (`case var y:`).
- NEGATIVE (v7, malformed, 2): `IsMissingPattern_Rejected` (`if (x is)`), `SwitchGuardMissingDeclaration_Rejected` (`case int when y > 0:`).
- POSITIVE (v7, Roslyn-derived, 3): `IsDeclarationPattern_Conditional_Roslyn_Succeeds` (`if (e is int x ? true : false)`, DeclarationExpressionTests.cs:227), `IsDeclarationPattern_DiscardDesignation_Roslyn_Succeeds` (`if (e is int _)`, DeconstructionTests.cs:2676), `SwitchVarPattern_DiscardDesignation_Roslyn_Succeeds` (`case var _:` DeconstructionTests.cs:2865).

## Verification
- [x] `dotnet build Nitra.sln --no-incremental` → **success, 0 errors / 0 warnings**.
- [x] `dotnet test Tests/CSharpGrammarTests` (fresh build) → **1100 total / 1097 passed / 0 failed / 3 skipped**. Baseline before T3.6.2 (after T3.6.1): 1078 total / 1075 passed / 3 skipped. Delta = **+22** (all new `Cs7PatternTests`). All pre-existing Cs1/Cs2/Cs3/Cs4/Cs5/Cs6/Cs11 tests remain green (no Cs1–Cs6/Cs11 modification).
- [x] `dotnet test Tests/ParserTests` (regression; engine + CsNitra meta-grammar unchanged) → **327 total / 325 passed / 0 failed / 2 skipped** — matches baseline.

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs7.grammar` (pattern rules: `TypeIsPattern` postfix, `Pattern` rule, `PatternCaseLabel` CaseLabel alternative).
- `Tests/CSharpGrammarTests/Cs7PatternTests.cs` (new — 22 tests, CRLF + UTF-8 BOM).
- `docs/CSharpParserPlan-progressT3.6.2.md` (this file).
- `docs/CSharpParserPlan-checklist.md` (T3.6.2 `[~]` → `[✅]` with the D1 note).

## Deviations / boundary decisions
- **D1 — `case int when y > 0:` REJECTS in this grammar, but Roslyn ACCEPTS it.** In Roslyn, `when` is never a pattern designation in a switch arm (`IsValidPatternDesignation`, LanguageParser_Patterns.cs:413-416: `case WhenKeyword: return !inSwitchArmPattern;`), so `case int when y > 0:` is a TYPE pattern `int` + guard `y > 0` (valid). In this grammar, `when` is a plain true identifier (not reserved), so the `DeclarationPattern = Type !ReservedKeyword Identifier` consumes it as the designation (`int when`), leaving `y > 0:` unmatched before the `:` → REJECTS. The task lists `case int when y > 0:` as a NEGATIVE (malformed) and this grammar rejects it, so the test passes; the discrepancy with Roslyn is documented. Matching Roslyn would require excluding `when` from switch-arm designations (a lookahead the meta-grammar makes awkward and which is not required by the task).
- **The discard `_` in `is` is a type pattern, not a discard pattern.** `_` is a valid type name (`QualifiedName = TypeName = !ReservedKeyword Identifier`), so the Cs1 `TypeIs` matches `x is _` at the same length as the Cs7 `TypeIsPattern`; the FIRST postfix (`TypeIs`) wins the tie → `x is _` is parsed as a type pattern. This matches Roslyn (`ParseTypeOrPatternForIsOperator` note + `NotDiscardInIsTypeExpression`, PatternParsingTests.cs:5681: `e is _` → `IsExpression` + `IdentifierName("_")`). The `DiscardPattern` alternative is still present (first in `Pattern`) and is the match for a bare `case _:` in a switch (Roslyn parses that as a `DiscardPattern`, with a semantic error). Consequence: `x is _` parses at v1–v6 (as a type pattern), so its version-purity negative is not asserted (the task only requires it to parse at v7).
- **`case var:` (bare `var`, no designation) REJECTS in this grammar.** Roslyn parses a bare `var` in a switch case as a constant pattern (`IdentifierName("var")`, `VarIsContextualKeywordForPatterns01`, PatternParsingTests.cs:2813). In this grammar `var` is a reserved keyword (Cs2), so it is neither a `Type` nor an `Expression`, and `VarPattern` requires a following identifier → no `Pattern` matches → REJECTS. Not tested (a bare `var` constant case is a degenerate edge case); documented for completeness.
