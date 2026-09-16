# T3.3.3 — C# 4.0 optional parameters

Status: done.

## Goal
Add C# 4.0 optional parameters to the EXISTING `Parsers/CSharp/CSharpGrammar/Cs4.grammar`:
- `void M(int x = 5)` (a single optional parameter)
- `void M(int x = 5, int y = 6)` (multiple optional parameters)
- `void M(int x, int y = 6)` (required before optional)
- `void M(string s = null)` (default value is `null`)
- `void M(int x = Compute())` (default value is a complex expression)

CS4 only. The "optional parameters must come after required parameters" rule is a BINDER concern —
the parser accepts any order. `CreateParser(3)` (Cs1+Cs2+Cs3, no CS4) must REJECT `void M(int x = 5)`.
`CreateParser(4)` must accept it.

## Cs1 `Parameter` structure (found)
- **`Parameter = Attributes? ParameterModifier* Type TypeName;`** (Cs1.grammar:448) — NO default value
  (C# 1.0 has no optional parameters). This is the EXACT rule to re-declare.
- `ParameterList = (Parameter; ",")+;` (Cs1.grammar:446) — a `SeparatedList` of `Parameter` separated
  by `,` (one or more). So `void M(int x = 5, int y = 6)` = `Parameter(int x = 5)` + `,` +
  `Parameter(int y = 6)`.
- `ParameterModifier = | "ref" | "out" | "params"` (Cs1:450-453); Cs3 APPENDS `| "this"` (Cs3:250).
  The Cs4 re-declaration keeps `ParameterModifier*` so it works with any modifier.
- `Method = Attributes? MethodModifier* Type TypeName "(" ParameterList? ")" MethodBody` (Cs1:265).
  `MethodBody = | Block | ";"` (Cs1:267-269).
- `Type` (Cs1:508-512), `TypeName = !ReservedKeyword Identifier` (Cs1:23), `Expression` (TDOPP,
  Cs1:633-673). The `Comma` operator is the LAST entry of the merged precedence list (binding power
  1, the lowest) — see T3.3.2.

## Roslyn references (C:\RSDN\roslyn, main, src/Compilers/CSharp/Portable/Parser/LanguageParser.cs)
- **`ParseParameter` (4938)**: `attributes` + `ParseParameterModifiers` + `ParseType(mode: Parameter)`
  + `ParseIdentifierToken` + `var equalsToken = TryEatToken(SyntaxKind.EqualsToken)` (4981) +
  `equalsToken == null ? null : _syntaxFactory.EqualsValueClause(equalsToken, this.ParseExpressionCore())`
  (4988). So the form is `Type Identifier = Expression`; the default value is a FULL `Expression`
  (`ParseExpressionCore`), and the `=` is a LITERAL token (not a TDOPP operator).
- **CS4 version gate**: `src/Compilers/CSharp/Test/Syntax/Parsing/ParserErrorMessageTests.cs:6232
  OptionalParameterBeforeCSharp4` — `class C { void M(int x = 1) { } }` under `TestOptions.Regular3`
  -> CS8024 "Feature 'optional parameter' is not available in C# 3" at the `=` token; no error under
  `Regular4`. Confirms optional parameters are a CS4 feature.
- **"Optional after required" is a BINDER concern**: `src/Compilers/CSharp/Portable/Symbols/Source/
  ParameterHelpers.cs:888-893` — `else if (firstDefault != -1 && parameterIndex > firstDefault &&
  !isDefault && !isParams) { diagnostics.Add(ErrorCode.ERR_DefaultValueBeforeRequiredValue, loc); }`
  (CS1737 "Optional parameters must appear after all required parameters"). This is in `Symbols/Source`
  (symbol creation / semantic analysis), NOT the parser. `ParseParameter` does NOT check the order —
  it parses each parameter independently with an optional `= expression`. So the PARSER accepts any
  order (e.g. `void M(int y = 6, int x)` parses; CS1737 is reported by the binder).
- **Roslyn syntax tests to adapt**:
  - `ParserErrorMessageTests.cs:6232` — `void M(int x = 1) { }` (single optional parameter).
  - `RoundTrippingTests.cs:1497 RegressError4AttributeWithNamedParam` — `public TestAttribute(int i = 0,
    int j = 1) { }` (multiple optional parameters).
  - `LambdaParameterParsingTests.cs:2485` — `(string x = null) => x` (a `= null` default value).
  - `DeclarationParsingTests.cs:5821 TestAnonymousMethodWithDefaultParameter` — `delegate (int x = 0)`
    (a parameter with a `= 0` default value; the syntax is a valid parameter-with-default).

## Approach
Re-declare `Parameter` in Cs4 (append, T0.3 merge) to ADD the default value. The re-declared
alternative REQUIRES the new construct (`"=" Expression : Comma`), so it is mutually exclusive with
the Cs1 `Parameter` (no default value) — no equal-length tie.

Final rule (Cs4.grammar):
```
Parameter = Attributes? ParameterModifier* Type TypeName "=" Expression : Comma;
```

- The `=` is a LITERAL token (a plain string in the rule), NOT a TDOPP operator. The default value is
  parsed as `Expression : Comma` AFTER the `=` literal, so the TDOPP `=` (Assignment, bp 4) is not
  involved at the `=` position.
- **`Expression : Comma` (the TDOPP Comma fix, from T3.3.2)**: the default value is a full `Expression`
  (TDOPP, minPrecedence 0), which greedily absorbs a following `,` (the Comma operator, bp 1, is
  applicable at minPrecedence 0). `Expression : Comma` (a `ReqRef` with Precedence = Comma's binding
  power 1) raises minPrecedence to 1, so the Comma postfix is NOT applicable (`1 > 1` is false) while
  every other operator (bp >= 2) still is. The default value therefore stops at the `,` separator of
  the `ParameterList`. This is the same minPrecedence mechanism Cs1 already uses (`Expression :
  Relational` in TypeIs/TypeAs, `Expression : Cast` in CastExpr) — no engine change.
- **Why `Expression : Comma` is required (not just a plain `Expression`)**: the second parameter's
  type may be a qualified name starting with an identifier (e.g. `System.String`, `Foo.Bar`). A plain
  `Expression` for the first parameter's default value would absorb the `,` and parse the second
  parameter's qualified type (`System.String`) as a comma-expression RHS (a member-access expression),
  leaving the second parameter's identifier dangling -> parse failure. `Expression : Comma` stops at
  the `,`, so the second parameter parses correctly. (See the `QualifiedTypeSecondParam` test.)

## Mutual-exclusivity hand-traces (optional vs required parameter)
The merged `Parameter` has TWO alternatives (declaration order):
1. Cs1: `Attributes? ParameterModifier* Type TypeName` (no default).
2. Cs4: `Attributes? ParameterModifier* Type TypeName "=" Expression : Comma` (default REQUIRED).

- `int x` (required, no default):
  - Alt 1 (Cs1) -> `int x` (length 4).
  - Alt 2 (Cs4) -> `int x` then needs `=` but the next token is not `=` (`,` or `)`) -> FAILS.
  - Only alt 1 matches. -> **Cs1 `Parameter`**. No tie.
- `int x = 5` (optional):
  - Alt 1 (Cs1) -> `int x` (length 4; stops at `x`, does not consume `= 5`).
  - Alt 2 (Cs4) -> `int x = 5` (length 9).
  - Longest-match: alt 2 (length 9) > alt 1 (length 4). -> **Cs4 `Parameter`**. Different lengths -> no
    tie. (Even though alt 1 "succeeds" on the prefix `int x`, the engine keeps the LONGEST success.)
- `int x = 5, int y = 6` (two optional; the `Expression : Comma` fix in action):
  - First `Parameter`: alt 2 -> `int x` + `=` + `Expression : Comma`. The `Expression : Comma` (minPrec
    1) parses `5`; the Comma postfix (bp 1) is NOT applicable (`1 > 1` false) -> stops at the `,`.
    -> `int x = 5`.
  - `ParameterList` separator `,` consumed.
  - Second `Parameter`: alt 2 -> `int y = 6` (the `Expression : Comma` parses `6`; next is `)`, no
    Comma) -> `int y = 6`.
  - -> `void M(int x = 5, int y = 6)` parses. (Without the fix, a plain `Expression` would absorb the
    `,` -> parse failure.)
- `int x, int y = 6` (required before optional):
  - First `Parameter`: alt 1 -> `int x` (alt 2 fails: no `=`). -> `int x`.
  - Separator `,`.
  - Second `Parameter`: alt 2 -> `int y = 6` (longest over alt 1's `int y`). -> `int y = 6`.
  - -> `void M(int x, int y = 6)` parses.
- `string s = null` (default is `null`):
  - Alt 2 -> `Type=string`; `TypeName=s`; `=`; `Expression : Comma` -> `null` (Primary = "null"). ->
    `string s = null`. -> `void M(string s = null)` parses.
- `int x = Compute()` (complex default value):
  - Alt 2 -> `Type=int`; `TypeName=x`; `=`; `Expression : Comma` -> `Compute()` (Primary `Compute` +
    `Invocation` postfix). -> `int x = Compute()`. -> `void M(int x = Compute())` parses.
- `int x = 5, System.String s` (qualified type as the second parameter's type — the fix's motivation):
  - First `Parameter`: alt 2 -> `int x = 5` (the `Expression : Comma` stops at the `,`; a plain
    `Expression` would have absorbed the `,` and parsed `System.String` as a comma RHS -> failure).
  - Separator `,`.
  - Second `Parameter`: alt 2/alt 1 -> `System.String s` (Type = QualifiedName `System.String`,
    TypeName = `s`). -> parses.

## `=` disambiguation
The `=` in the default value is a LITERAL token in the `Parameter` rule (matched explicitly), NOT a
TDOPP binary operator. The TDOPP `=` (Assignment, `Assign = Expression "=" Expression : Assignment`,
right-associative, bp 4) is only relevant WITHIN the default-value `Expression` (e.g. a hypothetical
`int x = y = 5` parses as `Type=int, TypeName=x, =, Expression=(y = 5)` — a binder error, but a valid
parse). At the `=` position between the parameter name and the default value, the literal `=` is
consumed by the rule, so there is no conflict with the TDOPP expression.

## Version-purity results
- `class C { void M(int x = 5) { } }` -> **rejects at v3** (the Cs4 `Parameter` alternative is absent;
  the Cs1 `Parameter` matches only `int x`, leaving `= 5` -> the `ParameterList`/`Method` fails) /
  **parses at v4** (the Cs4 `Parameter` alternative is present).

## Tests
`Tests/CSharpGrammarTests/Cs4OptionalParameterTests.cs` (CRLF + UTF-8 BOM) — **14 tests, all green**.
- POSITIVE (v4, core, 6): `OptionalParameter_Single_Succeeds` (`void M(int x = 5) { }`),
  `OptionalParameter_Multiple_Succeeds` (`void M(int x = 5, int y = 6) { }`),
  `OptionalParameter_RequiredBeforeOptional_Succeeds` (`void M(int x, int y = 6) { }`),
  `OptionalParameter_NullDefault_Succeeds` (`void M(string s = null) { }`),
  `OptionalParameter_ComplexDefault_Succeeds` (`void M(int x = Compute()) { }`),
  `OptionalParameter_UseTheParameter_Succeeds` (`void M(int x = 5) { N(x); }`).
- POSITIVE (v4, the `Expression : Comma` fix, 1):
  `OptionalParameter_QualifiedTypeSecondParam_Succeeds` (`void M(int x = 5, System.String s) { }` — a
  qualified type name as the second parameter's type; a plain `Expression` default value would fail).
- POSITIVE (v4, any order — parser accepts, binder rejects, 1):
  `OptionalParameter_AnyOrder_Succeeds` (`void M(int y = 6, int x) { }`).
- POSITIVE (v4, Roslyn-derived, 3): `OptionalParameter_Single_Roslyn_Succeeds` (`void M(int x = 1) { }`,
  ParserErrorMessageTests.cs:6232), `OptionalParameter_Multiple_Roslyn_Succeeds`
  (`void TestAttribute(int i = 0, int j = 1) { }`, RoundTrippingTests.cs:1497),
  `OptionalParameter_NullDefault_Roslyn_Succeeds` (`void M(string name = null) { }`,
  LambdaParameterParsingTests.cs:2485).
- VERSION-PURITY (v3, 1): `OptionalParameter_RejectedAtV3` (`void M(int x = 5)` rejects at v3,
  ParserErrorMessageTests.cs:6232).
- NEGATIVE (v4, malformed, 2): `OptionalParameter_MissingDefaultValue_Rejected` (`void M(int x =) { }`),
  `OptionalParameter_MissingName_Rejected` (`void M(int = 5) { }`).

## Verification
- [x] `dotnet build Nitra.sln --no-incremental` -> **success, 0 errors / 0 warnings**.
- [x] `dotnet test Tests/CSharpGrammarTests` (fresh build) -> **1007 total / 1004 passed / 0 failed /
  3 skipped**. Baseline before T3.3.3: 993 total / 990 passed / 3 skipped (T3.3.2). Delta = **+14**
  (all new `Cs4OptionalParameterTests`). All pre-existing Cs1 / Cs2 / Cs3 / Cs6 / Cs11 tests remain
  green.
- [x] `dotnet test Tests/ParserTests` (regression; engine + CsNitra meta-grammar unchanged) ->
  **327 total / 325 passed / 0 failed / 2 skipped** — matches baseline.

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs4.grammar` (the `Parameter` re-declaration).
- `Tests/CSharpGrammarTests/Cs4OptionalParameterTests.cs` (new).
- `docs/CSharpParserPlan-checklist.md` (T3.3.3 `[~]` -> `[✅]`).
- `docs/CSharpParserPlan-progressT3.3.3.md` (this file).

## Boundary decisions / deviations
- **`Expression : Comma` (minPrecedence 1) for the default value, not a plain `Expression`** (the
  TDOPP Comma fix, T3.3.2): it reuses the engine's existing TDOPP minPrecedence mechanism (the same
  one Cs1 uses for `TypeIs`/`TypeAs`/`CastExpr`), is a one-rule change, and does not touch Cs1.grammar
  or the engine. It excludes ONLY the `Comma` operator (bp 1); every other operator (bp >= 2) still
  applies, so complex default values (`Compute()`, `a + b`, `null`, `5`) parse unchanged. This is
  REQUIRED (not just defensive): when the NEXT parameter's type is a qualified name starting with an
  identifier (`System.String`, `Foo.Bar`), a plain `Expression` default value would absorb the `,` and
  parse that qualified type as a comma-expression RHS (member access), leaving the next parameter's
  identifier dangling -> parse failure. See the `OptionalParameter_QualifiedTypeSecondParam_Succeeds`
  test.
- **The Cs4 `Parameter` alternative is a SINGLE (non-union) re-declaration** `Parameter = Attributes?
  ParameterModifier* Type TypeName "=" Expression : Comma;` — no named-union constraint applies (the
  T3.3.2 named-alternative rule is only for `|` alternatives). `Expression : Comma` appears directly in
  the sequence (NOT wrapped in a `(...)` group), satisfying the T3.3.2 meta-grammar constraint 2
  (the `TypeCheckerVisitor` does not recurse into groups, so a precedence-qualified `RuleRef` inside a
  group would throw in `RuleGenerator`).
- **The `=` is a literal, not a TDOPP operator**: it is matched explicitly by the rule between the
  parameter name and the default value. The TDOPP `=` (Assignment, bp 4, right-associative) is only
  relevant WITHIN the default-value `Expression` (a hypothetical `int x = y = 5` parses as
  `Type=int, TypeName=x, =, Expression=(y = 5)` — a binder error, but a valid parse). No conflict at
  the `=` position.
- **The "optional after required" rule (CS1737) is a BINDER concern, not modeled**: Roslyn reports it
  in `Symbols/Source/ParameterHelpers.cs:888-893` (symbol creation), and `ParseParameter` (the parser)
  does NOT check the order. So the Cs4 `Parameter` accepts any order; `void M(int y = 6, int x)`
  PARSES at v4 (the `OptionalParameter_AnyOrder_Succeeds` test documents this). This is the parser-
  vs-binder split (the same split as the named-argument order in T3.3.2).
- **No Cs1/Cs2/Cs3 modification**: the Cs1 `Parameter` (no default) is unchanged; the Cs4 alternative
  is APPENDED (T0.3 merge). Version purity holds because at v3 the Cs4 alternative is absent.
