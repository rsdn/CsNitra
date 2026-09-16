# T3.6.1 — C# 7.0 tuples

Status: done (with Deviation D1 — the UNNAMED tuple literal version-purity negative is blocked).

## Goal
Add C# 7.0 tuples to a NEW grammar file `Parsers/CSharp/CSharpGrammar/Cs7.grammar`:
- Tuple types: `(int, string)`, `(int a, string b)` (named), `((int, string), bool)` (nested).
- Tuple literals: `(1, "a")`, `(a: 1, b: "b")` (named), `((1, "a"), true)` (nested).
- Deconstruction: `var (x, y) = t;`, `(x, y) = t;` (deconstruction assignment).
- `Item1` access: `t.Item1`, `t.Item2`.

CS7 only. `CreateParser(6)` must REJECT the tuple constructs; `CreateParser(7)` must accept them.

## Cs1 structure found
- **`Type`** (Cs1.grammar:508-512): `| PredefinedType | QualifiedName | PointerType = Type : TypePointer "*" | ArrayType = Type : TypeArray ArrayRankSpecifier`. A parenthesized type `(int)` is NOT a `Type` (verified: `class C { (int) M() { return 1; } }` REJECTS at v6 — D9). So a tuple type `(int, string)` is cleanly mutually exclusive with a parenthesized type.
- **`Primary`** (Cs1.grammar:675-720): includes `Parens = "(" Expression ")"` (707). The `Expression` is a TDOPP rule (minPrecedence 0) that ABSORBS a following `,` via the `Comma` operator (bp 1, the lowest in the precedence list, applicable at minPrecedence 0). So `(1, "a")` is parsed by `Parens` as a parenthesized COMMA EXPRESSION at v6 (verified D1/D2/D14/D15).
- **`Expression`** (Cs1.grammar:633-673): TDOPP rule. The `Comma` operator is `Comma = Expression "," Expression : Comma` (673). The `Assign` operator is `Assign = Expression "=" Expression : Assignment, right` (672).
- **`LocalVariableDeclaration`** (Cs1.grammar:807): `Type VariableDeclarator ("," VariableDeclarator)* ";"`. Cs2 re-declares it (Cs2.grammar:194): `"var" VariableDeclarator ("," VariableDeclarator)* ";"`. `VariableDeclarator = !ReservedKeyword Identifier ("=" Expression)?` (Cs1:811).
- **`Statement`** (Cs1.grammar:772-793): a union; includes `ExpressionStatement = Expression ";"` (803) and `LocalVariableDeclaration` (777).
- **`PostfixOp`** (Cs1.grammar:722-727): includes `MemberAccess = "." Identifier` (723) — so `t.Item1` is a regular member access (verified: parses at v6, D8). No change needed for `Item1`.
- **`ReservedKeyword`** (Cs1.grammar:537-618): `var` is NOT in Cs1 (added in Cs2); the tuple keywords are not reserved.

## Roslyn references (C:\RSDN\roslyn, main, src/Compilers/CSharp/Portable/Parser/LanguageParser.cs)
- **Tuple type** — `ParseTupleType` (7920): `"(" (ParseTupleElement,)+ ")"` (`list.Count < 2` → `ERR_TupleTooFewElements`). `ParseTupleElement` (7954): `ParseType()` + `(IsTrueIdentifier() ? ParseIdentifierToken() : null)` — the element NAME comes AFTER the type (`int a` = type `int`, name `a`). **Correction to the task**: the named tuple-type element is `Type Identifier?` (type FIRST, name AFTER), NOT `Identifier Type`. Entered from `ParseUnderlyingType` (7989) when the current token is `(`.
- **Tuple literal** — `ParseCastOrParenExpressionOrTuple` (12828): `"(" + first expression`, then if next is `,` (12858) or first is `Identifier + ":"` (12866) → `ParseTupleExpressionTail` (12882): `firstArg + ("," + argument)*` (`list.Count < 2` → `ERR_TupleTooFewElements`). Each argument is an expression, optionally with a name colon (`Identifier ":" Expression`, 12892-12897).
- **Deconstruction** — `IsPossibleDeconstructionLeft` (12231): `"var" + "(" + ScanDesignator (12246) + "="`; `ScanDesignator` = `identifier | "(" designator ("," designator)* ")"` (recursive — nested deconstruction). The deconstruction ASSIGNMENT `(x, y) = t;` is an assignment EXPRESSION (`ParseExpressionContinued`, 11532) whose LHS is a parenthesized variable designation — an EXPRESSION, not a separate statement.
- **`Item1` access** — a regular member access (`.` `Identifier`); no special syntax. Confirmed by the Cs1 `MemberAccess` postfix.
- All confirmed CS7 features (tuples / deconstruction are C# 7.0).

## Diagnostic (current v6 behavior, before Cs7)
- D1 `var t = (1, "a");` v6 → **PARSES** (parenthesized comma expression via `Parens`+`Comma`).
- D2 `var t = (1, 2);` v6 → **PARSES** (comma expression).
- D3 `var t = (1);` v6 → **PARSES** (parenthesized expression).
- D5 `(int, string) M() { return (1, "a"); }` v6 → **REJECTS** (tuple type is not a `Type` at v6).
- D6 `var (x, y) = t;` v6 → **REJECTS** (deconstruction declaration absent).
- D7/D13 `(x, y) = t;` v6 → **PARSES** (as `Assign(Parens(Comma(x,y)), t)` — an expression statement).
- D8 `var x = t.Item1;` v6 → **PARSES** (regular member access).
- D9 `class C { (int) M() { return 1; } }` v6 → **REJECTS** (a single parenthesized type is not a `Type`).
- D11 `var t = (a: 1, b: 2);` v6 → **REJECTS** (named — `a: 1` is not an `Expression`, so `Parens` fails; no `TupleLiteral` at v6).
- D15 `var t = ((1, 2), true);` v6 → **PARSES** (nested comma expression).

## Approach per feature
1. **Tuple type**: re-declare `Type` (append, T0.3) to add `TupleType = "(" TupleTypeElement ("," TupleTypeElement)+ ")"`. REQUIRES a comma (>= 2 elements) → mutually exclusive with a parenthesized type (a single type in parens, which is not a `Type`). `TupleTypeElement = Type !ReservedKeyword Identifier?` (type FIRST, name AFTER — Roslyn `ParseTupleElement`).
2. **Tuple literal**: re-declare `Primary` (append, T0.3) to add `TupleLiteral = "(" TupleLiteralElement ("," TupleLiteralElement)+ ")"`. REQUIRES a comma (>= 2 elements). `TupleLiteralElement = NamedTupleElement | PositionalTupleElement` where `NamedTupleElement = !ReservedKeyword Identifier ":" Expression : Comma` and `PositionalTupleElement = Expression : Comma` (the TDOPP Comma fix, T3.3.2 — `Expression : Comma` raises minPrecedence to 1 so the element does NOT absorb the `,` separator).
3. **Deconstruction declaration**: re-declare `LocalVariableDeclaration` (append, T0.3) to add `DeconstructionDeclaration = "var" DeconstructionVariableList "=" Expression ";"`. REQUIRES the `(` after `var` → mutually exclusive with the Cs2 `"var" VariableDeclarator ...` (which fails on `(`) and the Cs1 `Type ...` (var is reserved, not a `Type`). `DeconstructionVariableList = "(" !ReservedKeyword Identifier ("," !ReservedKeyword Identifier)* ")"`.
4. **Deconstruction assignment**: `(x, y) = t;` is an assignment EXPRESSION (Roslyn `ParseExpressionContinued`). In this grammar it is ALREADY parsed at every version via the Cs1 `ExpressionStatement` (as `Assign(Parens(Comma(x,y)), t)`). No new rule is required for it to parse (verified: parses at v6, D7/D13). NOT added as a separate statement rule (Deviation D2).
5. **`Item1` access**: already handled by the Cs1 `MemberAccess` postfix (`.` `Identifier`). No change needed (verified: parses at v6, D8).

## Mutual-exclusivity hand-traces
- **Tuple type** `(int, string)` vs parenthesized type `(int)`: the tuple type REQUIRES a `,` (>= 2 elements); a parenthesized type `(int)` is not a `Type` at any version (D9). Mutually exclusive. `(int, string)` → `TupleType` only (no tie).
- **Tuple type** `(int a, string b)`: `TupleTypeElement` = `Type` + optional name. `int a` → Type=`int`, name=`a`; `string b` → Type=`string`, name=`b`. The name is a true identifier (`!ReservedKeyword`). No tie.
- **Tuple literal** `(a: 1, b: 2)` (NAMED) vs `Parens`: `Parens` FAILS (`a: 1` is not an `Expression` — `:` is not a binary operator), so `TupleLiteral` is the SOLE match. Clean version-purity (rejects at v6, D11).
- **Tuple literal** `(1, 2)` (UNNAMED) vs `Parens`: `Parens` MATCHES (the inner `Expression` absorbs the `,` via the `Comma` operator) as a comma expression, at the SAME length as `TupleLiteral`. The FIRST (Cs1 `Parens`) wins the tie (Parser.cs:296-316) → parsed as a comma expression, NOT a tuple literal. The `TupleLiteral` is dead for unnamed elements. **Consequence**: the unnamed tuple literal PARSES at v6 → its version-purity negative is BLOCKED (Deviation D1).
- **Deconstruction declaration** `var (x, y) = t;` vs Cs2 `var` declaration: the Cs2 `VariableDeclarator` (an identifier) fails on `(`, so only the Cs7 `DeconstructionDeclaration` matches. No tie.
- **Deconstruction assignment** `(x, y) = t;`: parsed via the Cs1 `ExpressionStatement` (as `Assign(Parens(Comma(x,y)), t)`) at every version. No new rule.

## TDOPP Comma fix
The tuple literal elements use `Expression : Comma` (minPrecedence 1) so they do NOT absorb the `,` separator (the same fix as T3.3.2 `Argument` and T3.3.3 optional parameters). This is required for the TUPLE LITERAL's own element list to parse correctly (`(1, 2)` → two elements, not one comma expression). It does NOT, however, prevent the Cs1 `Parens` alternative from matching `(1, 2)` as a comma expression (that uses a plain `Expression`, minPrecedence 0) — see Deviation D1.

## Setup steps
- [x] Create `Parsers/CSharp/CSharpGrammar/Cs7.grammar` (CRLF, no BOM).
- [x] Add `<EmbeddedResource Include="Cs7.grammar" />` to `CSharpGrammar.csproj` (between Cs6 and Cs11).
- [x] Add version-table entry `new(7, "Cs7.grammar", "Cs7.grammar")` to `EmbeddedGrammar.cs` (ascending, between 6 and 11).
- [x] Add `Tests/CSharpGrammarTests/Cs7TupleTests.cs` (CRLF + UTF-8 BOM).
- [x] Update `CSharpVersionInfrastructureTests.cs` (new `LoadGrammarUpTo_7_...` test; `LoadGrammarUpTo_11_...` count 7→8 with `Cs7.grammar` at index 6, `Cs11.grammar` at index 7).

## Version-purity results
- `class C { (int, string) M() { return (1, "a"); } }` → **rejects at v6** (tuple type is not a `Type` at v6) / **parses at v7**. ✓
- `class C { void M() { var (x, y) = t; } }` → **rejects at v6** (deconstruction declaration absent) / **parses at v7**. ✓
- `class C { void M() { var t = (a: 1, b: "b"); } }` (NAMED) → **rejects at v6** (`Parens` fails on `a: 1`) / **parses at v7**. ✓
- `class C { void M() { var t = (1, "a"); } }` (UNNAMED) → **PARSES at v6** (as a parenthesized comma expression via Cs1 `Parens`+`Comma`) / **parses at v7**. ✗ (Deviation D1 — the version-purity negative is BLOCKED; the task listed it as a v6 reject, but it cannot be satisfied without modifying Cs1/the engine).
- `class C { void M() { var t = (1); } }` (parenthesized expression) → **parses at v6 and v7** (a single-element parenthesized expression is valid at every version; `(int)` is not a `Type`, but `(1)` is a `Parens` expression).
- All pre-existing Cs1/Cs2/Cs3/Cs4/Cs5/Cs6/Cs11 tests stay green (no Cs1/Cs2/Cs3/Cs4/Cs5/Cs6/Cs11 modification).

## Tests
`Tests/CSharpGrammarTests/Cs7TupleTests.cs` (CRLF + UTF-8 BOM) — **28 tests, all green**.
- POSITIVE (v7, tuple types, 6): `TupleType_Unnamed_Succeeds` (`(int, string) M() { return (1, "a"); }`), `TupleType_Named_Succeeds` (`(int a, string b) M() { return (a: 1, b: "b"); }`), `TupleType_Nested_Succeeds` (`((int, string), bool) M() { return ((1, "a"), true); }`), `TupleType_ThreeElements_Succeeds` (`(int, string, bool) M() { return (1, "a", true); }`), `TupleType_AsField_Succeeds` (`(int, string) F;`), `TupleType_AsParameter_Succeeds` (`void M((int, string) t) { }`).
- POSITIVE (v7, tuple literals, 4): `TupleLiteral_Unnamed_Succeeds` (`var t = (1, "a");`), `TupleLiteral_Named_Succeeds` (`var t = (a: 1, b: "b");`), `TupleLiteral_Nested_Succeeds` (`var t = ((1, "a"), true);`), `TupleLiteral_ThreeElements_Succeeds` (`var t = (1, "a", true);`).
- POSITIVE (v7, deconstruction, 3): `Deconstruction_Declaration_Succeeds` (`var (x, y) = t;`), `Deconstruction_Assignment_Succeeds` (`(x, y) = t;` — via Cs1 ExpressionStatement, D2), `Deconstruction_ThreeElements_Succeeds` (`var (x, y, z) = t;`).
- POSITIVE (v7, Item1/Item2 access, 2): `Item1_Access_Succeeds` (`var x = t.Item1;`), `Item2_Access_Succeeds` (`var x = t.Item2;`).
- POSITIVE (v7, Roslyn-derived, 4): `TupleExpression_TwoArguments_Roslyn_Succeeds` (`(a, a2)`, ExpressionParsingTests.cs:2260), `TupleExpression_TwoNamedArguments_Roslyn_Succeeds` (`(arg1: (a, a2), arg2: a2)`, ExpressionParsingTests.cs:2280), `LocalDeclaration_TupleType_Roslyn_Succeeds` (`(int, int) a;`, StatementParsingTests.cs:238), `TupleType_TwoItemAsTypeArgument_Roslyn_Succeeds` (`new Dictionary<(int, string), int>()`, TypeArgumentListParsingTests.cs:531).
- POSITIVE (v6, 1): `ParenthesizedExpression_ParsesAtV6` (`var t = (1);` — a parenthesized expression is valid at every version).
- VERSION-PURITY (v6, 3): `TupleType_RejectedAtV6` (`(int, string) M() { return (1, "a"); }`), `DeconstructionDeclaration_RejectedAtV6` (`var (x, y) = t;`), `TupleLiteral_Named_RejectedAtV6` (`var t = (a: 1, b: "b");`).
- DOCUMENT (v6, 1): `TupleLiteral_Unnamed_ParsesAtV6` (`var t = (1, "a");` — PARSES at v6 as a comma expression; Deviation D1).
- NEGATIVE (v7, malformed, 4): `TupleLiteral_TrailingComma_Rejected` (`var t = (1,);`), `TupleLiteral_LeadingComma_Rejected` (`var t = (,1);`), `TupleType_TrailingComma_Rejected` (`(int, string,) M() { ... }`), `Deconstruction_TrailingComma_Rejected` (`var (x,) = t;`).

## Verification
- [x] `dotnet build Nitra.sln --no-incremental` → **success, 0 errors / 0 warnings**.
- [x] `dotnet test Tests/CSharpGrammarTests` (fresh build) → **1078 total / 1075 passed / 0 failed / 3 skipped**. Baseline before T3.6.1 (after T3.5.3): 1049 total / 1046 passed / 3 skipped. Delta = **+29** (28 new `Cs7TupleTests` + 1 new `CSharpVersionInfrastructureTests.LoadGrammarUpTo_7_...`; the `LoadGrammarUpTo_11_...` test was updated in place, not added). All pre-existing Cs1/Cs2/Cs3/Cs4/Cs5/Cs6/Cs11 tests remain green.
- [x] `dotnet test Tests/ParserTests` (regression; engine + CsNitra meta-grammar unchanged) → **327 total / 325 passed / 0 failed / 2 skipped** — matches baseline.

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs7.grammar` (new — tuple type, tuple literal, deconstruction declaration).
- `Parsers/CSharp/CSharpGrammar/CSharpGrammar.csproj` (added `<EmbeddedResource Include="Cs7.grammar" />`).
- `Tests/CSharpGrammarTests/EmbeddedGrammar.cs` (version-table entry for version 7).
- `Tests/CSharpGrammarTests/Cs7TupleTests.cs` (new — 28 tests, CRLF + UTF-8 BOM).
- `Tests/CSharpGrammarTests/CSharpVersionInfrastructureTests.cs` (new `LoadGrammarUpTo_7_...` test; `LoadGrammarUpTo_11_...` updated for the 8-file set).
- `docs/CSharpParserPlan-checklist.md` (T3.6.1 `[~]` → `[✅]` with the D1 note).
- `docs/CSharpParserPlan-progressT3.6.1.md` (this file).

## Deviations / boundary decisions
- **D1 — the UNNAMED tuple literal `(1, 2)` version-purity negative is BLOCKED.** The Cs1 `Parens = "(" Expression ")"` (Cs1.grammar:707) matches `(1, 2)` as a parenthesized COMMA EXPRESSION (the inner `Expression` absorbs the `,` via the `Comma` operator, bp 1, applicable at minPrecedence 0) at the SAME length as the Cs7 `TupleLiteral`. The FIRST (Cs1 `Parens`) wins the tie (Parser.cs:296-316), so `(1, 2)` is parsed as a comma expression, NOT a tuple literal — at v6 AND v7. Consequence: `var t = (1, "a");` PARSES at v6 (as a comma expression), so the task's version-purity negative for the UNNAMED tuple literal cannot be satisfied without modifying Cs1 (to remove the phantom `Comma` operator or change `Parens`) or the engine. The NAMED tuple literal `(a: 1, b: 2)` is NOT affected (its version-purity works: `Parens` fails on `a: 1`, so it rejects at v6). See the final report.
- **D2 — the deconstruction assignment `(x, y) = t;` is NOT a separate statement rule.** It is an assignment EXPRESSION (Roslyn `ParseExpressionContinued`, 11532) whose LHS is a parenthesized variable designation. In this grammar it is ALREADY parsed at every version via the Cs1 `ExpressionStatement` (as `Assign(Parens(Comma(x,y)), t)`). Adding a separate `Statement` alternative would be dead code (it loses the tie to `ExpressionStatement`). So it is documented, not added.
- **D3 — the deconstruction variable list is a FLAT list, not recursive.** Roslyn `ScanDesignator` (12246) is recursive (nested deconstruction `((x, y), z)`). This grammar models the FLAT list required by the task (`(x, y)`); nested deconstruction is out of scope (documented simplification).
