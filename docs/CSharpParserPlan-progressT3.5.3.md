# T3.5.3 — C# 6.0: `?.`, expression-bodied members, `nameof`, binary literals

Status: done.

## Goal
Add the remaining C# 6.0 features to the EXISTING `Parsers/CSharp/CSharpGrammar/Cs6.grammar`
(which already has the interpolated-string rules from T3.5.1/T3.5.2):
1. `?.` (null-conditional operator) — a postfix operator.
2. Expression-bodied members — `int M() => 5;`, `int P => _x;`, `void M() => N();`.
3. `nameof` — `nameof(x)`, `nameof(C.P)`.
4. Binary literals — `0b1010`, `0B1010`.

CS6 only. `CreateParser(5)` must REJECT all four; `CreateParser(6)` must accept them.

## Baseline (before changes)
- `dotnet build Nitra.sln --no-incremental` → success, 0 errors / 0 warnings.
- `dotnet test Tests/CSharpGrammarTests` → **1025 total / 1022 passed / 0 failed / 3 skipped**.
- `dotnet test Tests/ParserTests` → **327 total / 325 passed / 0 failed / 2 skipped**.

## Cs1 structure found
- **`PostfixOp`** (Cs1.grammar:722-727) is a NAMED UNION of postfix operators:
  ```
  PostfixOp =
      | MemberAccess = "." Identifier
      | Indexer      = "[" (Expression; ",")* "]"
      | Invocation   = "(" (Expression; ",")* ")"
      | PostInc      = "++"
      | PostDec      = "--";
  ```
  Applied by `PrimaryExpr = Primary PostfixOp*` (Cs1:634). A NAMED ALTERNATIVE is inlined
  into its parent rule (NOT a separate `RuleSymbol`), so `PostfixOp` must be re-declared to add
  a new alternative (same pattern as T3.3.2 `NamedInvocation`).
- **`MethodBody`** (Cs1.grammar:267-269) is a named union `| Block | ";"`. Referenced by
  `Method` (Cs1:265), `Operator` (Cs1:343), `ConversionOperator` (Cs1:350). `Constructor`/
  `Destructor` use `Block` directly (NOT `MethodBody`) — so they stay non-expression-bodied.
- **`Property`** (Cs1.grammar:188) = `Attributes? PropertyModifier* Type TypeName "{"
  AccessorDeclaration+ "}" ";"?` (requires `{`). Cs3 re-declares it (auto-property, T3.2.3).
- **`Primary`** (Cs1.grammar:675-720) includes `IdentifierName = !ReservedKeyword Identifier`
  (682) — so a non-reserved word (like `nameof`) is a valid `Primary` (a plain identifier).
- **`DecimalIntegerLiteral`** terminal = `[0-9]+`; **`HexIntegerLiteral`** = `0[xX][0-9a-fA-F]+`;
  **`IntegerSuffix`** = `[uU]?[lL]?|[lL][uU]?`. Used by `Primary` `DecInt`/`HexInt` (Cs1:704-705).
  `OctalIntegerLiteral` exists but is NOT used in `Primary`.
- **`ReservedKeyword`** (Cs1.grammar:537-618): `nameof` is NOT in it (verified by grep).
- **TDOPP precedence list** (Cs1.grammar:623-626, merged with `TypeArray, TypePointer, Comma`
  at 631): `Cast, Unary, Multiplicative, Additive, Relational, Equality, LogicalAnd, LogicalXor,
  LogicalOr, CondAnd, CondOr, Conditional, Assignment, Comma` (16 entries; `Comma` lowest = bp 1).

## Engine detail (verified, Parser.cs)
- `ParseSeq` (Parser.cs:810-878) parses elements LEFT-TO-RIGHT; the first element (`Primary`, a
  `Ref`) is parsed via `ParseAlternative` (longest-match across its alternatives) BEFORE the
  `PostfixOp*` (`ZeroOrMany`) is applied. So the `Primary` commits to its longest match first, and
  the postfix loop extends THAT result. Consequence for `nameof`: `NameOf` (longer than
  `IdentifierName`) wins as the `Primary`, and the `IdentifierName`+`Invocation` path is never
  considered → no equal-length tie.
- `Literal` is a StartsWith match (Rules.cs:76) — `"?."` matches the contiguous 2-char `?.`;
  multi-char operators already work as single literals (T3.2.1 `=>`, T3.3.2 `==`/`!=`).

## Roslyn references (C:\RSDN\roslyn, main)
- **`?.`**: `ParsePrimaryExpression` comment (LanguageParser.cs:11945) lists `x?.y, x?[y]` as
  postfix primary operators; handled in `parsePostFixExpression`. `?.` is one token
  (`QuestionDotToken`). CS6 feature.
- **Expression-bodied**: `ParseArrowExpressionClause` (LanguageParser.cs:4408-4412) =
  `EatToken(EqualsGreaterThanToken)` + `ParsePossibleRefExpression()` → `=> Expression`. Used by
  methods (3613/4218/4236/4287/4676/11014) and properties. CS6 feature.
- **`nameof`**: contextual keyword (`SyntaxKind.NameOfKeyword`, an `IdentifierToken`); a
  primary expression `nameof ( name_of_argument )` where the argument is an identifier or a
  member access (`name . identifier`, dotted). `NameOfKeyword` gate (LanguageParser_Patterns.cs:85
  checks `PeekToken(1) == OpenParenToken`). CS6 feature.
- **Binary literals**: `ScanNumericLiteral` (Lexer.cs:844-904): after `0`, `ch == 'b' || ch == 'B'`
  (870) → `CheckFeatureAvailability(MessageID.IDS_FeatureBinaryLiteral)` (872) + `isBinary = true`;
  digits via `SyntaxFacts.IsBinaryDigit` (825, `CharacterInfo.IsBinaryDigit` = 0/1). CS6 feature.

## Approach per feature
1. **`?.`**: re-declare `PostfixOp` (append, T0.3) to add
   `NullConditionalMemberAccess = "?." Identifier` (for `?.M`) and
   `NullConditionalIndexer = "?." "[" (Expression; ",")* "]"` (for `?.[i]`). The existing
   `Invocation` postfix handles the trailing `()` after `?.M`.
2. **Expression-bodied members**: re-declare `MethodBody` (append) to add
   `ExpressionBody = "=>" Expression ";"` (covers Method/Operator/ConversionOperator — all valid
   CS6). Re-declare `Property` (append) to add the expression-body form
   `Attributes? PropertyModifier* Type TypeName "=>" Expression ";"?`. The `=>` is the
   REQUIRED-new-construct (mutually exclusive with the Cs1 `{`-body / `;`-body).
3. **`nameof`**: re-declare `Primary` (append) to add `NameOf = "nameof" "(" NameOfArgument ")"`
   with `NameOfArgument = !ReservedKeyword Identifier ("." !ReservedKeyword Identifier)*`.
   `nameof` is NOT reserved (contextual keyword).
4. **Binary literals**: add a new `[Regex]` terminal `BinaryIntegerLiteral` = `0[bB][01]+` to
   `CSharpTerminals.cs` (+ `GetAll()`), and re-declare `Primary` (append) to add
   `BinInt = BinaryIntegerLiteral IntegerSuffix?`. Longest-match beats `DecInt` (`0`).

## Mutual-exclusivity hand-traces (all confirmed by green tests)
- `x?.M()` vs `x ? a : b`: `?.` (2-char) postfix vs `?` (1-char) Conditional. `x?.M()` →
  PrimaryExpr (`?.M` + `()`); Conditional fails (`.M()` is not an Expression after `?`). No tie.
  Confirmed: `NullConditional_Invocation_Succeeds` (v6) + the pre-existing conditional tests stay green.
- `int M() => 5;`: MethodBody alternatives disambiguated by leading token (`{`/`;`/`=>`). No tie.
  Confirmed: `ExpressionBodied_Method_Succeeds` (v6) + the pre-existing block/`;`-body method tests.
- `int P => _x;`: Cs6 Property (`=>` after name) vs Cs1/Cs3 Property (`{` after name) vs Field
  (`;`/`,`/`=` after name). Mutually exclusive. No tie. Confirmed: `ExpressionBodied_Property_Succeeds`
  (v6) + the pre-existing Cs1/Cs3 property tests.
- `nameof(x)`: `NameOf` (Primary, length 9) beats `IdentifierName` (length 6) by longest-match;
  the `IdentifierName`+`Invocation` path is not considered (ParseSeq commits the Primary first). No
  tie. Confirmed: `NameOf_Identifier_Succeeds` / `NameOf_MemberAccess_Succeeds` (v6).
- `0b1010`: `BinInt` (length 6) beats `DecInt` (`0`, length 1) by longest-match. No tie. Confirmed:
  `BinaryLiteral_LowerPrefix_Succeeds` / `BinaryLiteral_UpperPrefix_Succeeds` (v6) + the pre-existing
  decimal/hex literal tests.

## TDOPP Comma fix
- Expression body is followed by `;` (not a `,`), so the `Comma` postfix (bp 1) is not a separator
  concern — a plain `Expression` stops at `;`. No `Expression : Comma` needed (the body is not in a
  comma-separated context). (An inner invocation argument list still collapses `a, b` as the
  pre-existing TDOPP comma behavior — out of scope, T3.2.1/T3.3.2.)

## Version-purity results
- `class C { void M() { x?.M(); } }` → **rejects at v5** (no `?.` postfix) / **parses at v6**. ✓
- `class C { int M() => 5; }` → **rejects at v5** (no `ExpressionBody` in MethodBody) / **parses at v6**. ✓
- `class C { void M() { int x = 0b1010; } }` → **rejects at v5** (no `BinInt`; `0b1010` = `0` + `b1010`
  identifier, not an expression) / **parses at v6**. ✓
- `class C { void M() { var s = nameof(x); } }` → **PARSES at v5** (NOT a reject) as a method
  invocation on a variable named `nameof` (contextual keyword, see Deviation D1). **parses at v6** as
  a NameOf expression. This is the one feature whose v5 form is not a clean reject — documented below.
- All pre-existing Cs1/Cs2/Cs3/Cs4/Cs5/Cs11 tests stay green (no Cs1/Cs2/Cs3/Cs4/Cs5 modification).
  The Cs6 interpolated-string tests (T3.5.2) stay green.

## Tests
`Tests/CSharpGrammarTests/Cs6FeatureTests.cs` (CRLF + UTF-8 BOM) — **24 tests, all green**.
- POSITIVE (v6, `?.`, 4): `NullConditional_Invocation_Succeeds` (`x?.M()`),
  `NullConditional_MemberAccess_Succeeds` (`var y = x?.P`), `NullConditional_Indexer_Succeeds`
  (`x?.[i]`), `NullConditional_ChainedMemberAccess_Succeeds` (`var y = x?.M().P`).
- POSITIVE (v6, expression-bodied, 4): `ExpressionBodied_Method_Succeeds` (`int M() => 5`),
  `ExpressionBodied_Property_Succeeds` (`int P => _x`), `ExpressionBodied_StatementExpression_Succeeds`
  (`void M() => N()`), `ExpressionBodied_Method_ComplexExpression_Succeeds` (`int M() => a + b`).
- POSITIVE (v6, `nameof`, 3): `NameOf_Identifier_Succeeds` (`nameof(x)`),
  `NameOf_MemberAccess_Succeeds` (`nameof(C.P)`), `NameOf_NestedMemberAccess_Succeeds` (`nameof(C.P.Q)`).
- POSITIVE (v6, binary literals, 3): `BinaryLiteral_LowerPrefix_Succeeds` (`0b1010`),
  `BinaryLiteral_UpperPrefix_Succeeds` (`0B1010`), `BinaryLiteral_WithSuffix_Succeeds` (`0b1010L`).
- POSITIVE (v1/v5, contextual `nameof` identifier, 2): `NameOf_AsFieldName_ParsesAtV1` /
  `NameOf_AsFieldName_ParsesAtV5` (`class C { int nameof; }`).
- VERSION-PURITY (v5, 3): `NullConditional_RejectedAtV5`, `ExpressionBodied_Method_RejectedAtV5`,
  `BinaryLiteral_RejectedAtV5`.
- NEGATIVE (v6, malformed, 2): `ExpressionBodied_MissingBody_Rejected` (`int M() => ;`),
  `BinaryLiteral_InvalidDigit_Rejected` (`0b102`).
- DOCUMENT (v6, contextual-keyword edge cases, 3): `NullConditional_MemberAccessOnly_ParsesAtV6`
  (`x?.M` — a complete, valid null-conditional member access, NOT malformed),
  `NameOf_EmptyArgument_ParsesAsInvocationAtV6` (`nameof()` — parses as a method invocation),
  `NameOf_ParsesAsInvocationAtV5` (`nameof(x)` at v5 — parses as a method invocation, Deviation D1).

## Verification
- [x] `dotnet build Nitra.sln --no-incremental` → **success, 0 errors / 0 warnings**.
- [x] `dotnet test Tests/CSharpGrammarTests` (fresh build) → **1049 total / 1046 passed / 0 failed /
  3 skipped**. Baseline before T3.5.3: 1025 total / 1022 passed / 3 skipped. Delta = **+24** (all new
  `Cs6FeatureTests`). All pre-existing Cs1/Cs2/Cs3/Cs4/Cs5/Cs11 + the Cs6 interpolated-string tests
  remain green.
- [x] `dotnet test Tests/ParserTests` (regression; engine + CsNitra meta-grammar unchanged) →
  **327 total / 325 passed / 0 failed / 2 skipped** — matches baseline.

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs6.grammar` (appended the T3.5.3 rules: `PostfixOp` `?.` alternatives,
  `MethodBody`/`Property` expression-body re-declarations, `Primary` `NameOf`/`BinInt` + `NameOfArgument`).
- `Parsers/CSharp/CSharpGrammar/CSharpTerminals.cs` (added the `BinaryIntegerLiteral` terminal
  `0[bB][01]+` + its `GetAll()` entry).
- `Tests/CSharpGrammarTests/Cs6FeatureTests.cs` (new — 24 tests, CRLF + UTF-8 BOM).
- `docs/CSharpParserPlan-checklist.md` (T3.5.3 `[~]` → `[✅]`).
- `docs/CSharpParserPlan-progressT3.5.3.md` (this file).

## Deviations / boundary decisions
- **D1 — `nameof(x)` is NOT a clean v5 version-purity reject** (the task listed it as one). `nameof`
  is a CONTEXTUAL keyword (a valid `IdentifierName` at every version, NOT reserved — per the task's own
  "Do NOT add it to ReservedKeyword"), so at v5 (where the `NameOf` Primary alternative is absent)
  `nameof(x)` parses as a method INVOCATION on a variable named `nameof` (IdentifierName + Invocation).
  This matches real C#: `nameof(x)` is syntactically valid at C# 5.0 (a method call); the nameof
  FEATURE is a BINDER-level version gate, not a parse-level one. The same contextual-keyword behavior
  as `await;` in T3.4 (which also parses at v4 as a bare identifier rather than rejecting). The clean
  v5-vs-v6 discriminator is that at v6 `nameof(x)` is a `NameOf` primary (longer than IdentifierName)
  while at v5 it is a method invocation. Documented as a positive (`NameOf_ParsesAsInvocationAtV5`),
  not a reject. Making it a reject would require reserving `nameof` (forbidden by the task) or a
  binder (out of scope).
- **D2 — `nameof()` parses as a method invocation** (the task listed it as a v6 malformed negative).
  The `NameOf` alternative fails (no argument), so the IdentifierName+Invocation path wins → `nameof()`
  is an empty-argument method invocation on a variable named `nameof`. A documented deviation from
  Roslyn (where `nameof()` is a parse error because the argument is required). Covered as a positive
  (`NameOf_EmptyArgument_ParsesAsInvocationAtV6`). Same contextual-keyword root cause as D1.
- **D3 — `x?.M` (no invocation) is a COMPLETE, valid C# 6.0 expression** (the task listed it as
  "incomplete — verify; document"). A null-conditional member access `x?.M` evaluates to the member
  value (or null if `x` is null); as a statement expression it is valid. It PARSES (covered as a
  positive, `NullConditional_MemberAccessOnly_ParsesAtV6`).
- **`MethodBody` re-declaration (not `Method`)**: re-declaring the shared `MethodBody` lets the Cs1
  `Method`/`Operator`/`ConversionOperator` rules all gain the expression body (all valid CS6).
  `Constructor`/`Destructor` use `Block` directly (not `MethodBody`), so they stay non-expression-bodied
  (correct for CS6). No per-rule re-declaration needed.
- **No `Expression : Comma` in the expression body**: the body is followed by `;` (not a `,`), so the
  Comma postfix (bp 1) is not a separator concern (T3.3.2 does not apply). A plain `Expression` stops
  at `;`.
- **Binary literal as a dedicated terminal** (`0[bB][01]+`), not a grammar literal: the digit run
  needs a regex (a grammar literal cannot express a run of `0`/`1`). Longest-match makes `BinInt`
  (`0b1010`, length 6) beat `DecInt` (`0`, length 1). `0b102` → `BinInt` matches `0b10` (the `2` is not
  a binary digit), leaving a dangling `2` → the parse fails (invalid digit rejected).
- **`NameOfArgument` = identifier + dotted segments** (`!ReservedKeyword Identifier ("." !ReservedKeyword
  Identifier)*`), NOT a full expression (Roslyn `name_of_argument`). This means `nameof(x + y)` parses
  as a method invocation (the NameOf argument fails), a documented superset deviation (out of scope).
