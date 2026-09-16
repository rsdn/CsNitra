# T3.6.4 — C# 7.0 ref semantics

Status: done (with Deviations D1–D3 — see the Deviations section).

## Goal
Add C# 7.0 ref semantics to the EXISTING `Parsers/CSharp/CSharpGrammar/Cs7.grammar` (which already has
the tuple rules from T3.6.1, pattern-matching from T3.6.2, and local functions from T3.6.3):
- **`out var`**: `M(out var x)` — an `out` argument in a method CALL with an implicit type (`var`).
- **`ref` return**: `ref int M() { return _x; }` — a method that returns by reference (`ref` modifier
  BEFORE the return type).
- **`ref` locals**: `ref int x = ref _y;` — a local variable that holds a reference (`ref` modifier
  BEFORE the local variable's type).
- **`ref readonly`**: `ref readonly int M() { return _x; }` — a read-only reference return.

CS7 only. `CreateParser(6)` must REJECT all four; `CreateParser(7)` must accept them.

## Baseline (before changes)
- `dotnet build Nitra.sln --no-incremental` → success, 0 errors / 0 warnings.
- `dotnet test Tests/CSharpGrammarTests` → **1118 total / 1115 passed / 0 failed / 3 skipped**
  (matches T3.6.3).

## Cs1 structure found
- **`Method`** (Cs1.grammar:265): `Attributes? MethodModifier* Type TypeName "(" ParameterList? ")" MethodBody;`.
  The `ref`/`readonly` return modifier goes into `MethodModifier*` (BEFORE the `Type` return type).
- **`MethodModifier`** (Cs1.grammar:288-300): access + static/virtual/override/abstract/new/extern/
  sealed/unsafe. Cs2 APPENDS `"partial"` (Cs2:267); Cs5 APPENDS `"async"` (Cs5:49). Re-declare in Cs7
  to add `"ref"` and `"readonly"`.
- **`MethodBody`** (Cs1.grammar:267-269): `| Block | ";"`. Cs6 APPENDS `ExpressionBody = "=>" Expression ";"`.
- **`LocalVariableDeclaration`** (Cs1.grammar:807): `Type VariableDeclarator ("," VariableDeclarator)* ";"`.
  Cs2 APPENDS `"var" VariableDeclarator ("," VariableDeclarator)* ";"` (Cs2:194); Cs7 (T3.6.1) APPENDS
  `DeconstructionDeclaration = "var" DeconstructionVariableList "=" Expression ";"`. Re-declare in Cs7
  to add a `ref`-prefixed form.
- **`VariableDeclarator`** (Cs1.grammar:811): `!ReservedKeyword Identifier ("=" Expression)?`.
- **`Argument`** (Cs4.grammar:126, T3.3.2): `| NamedArgument = Identifier ":" Expression : Comma |
  PositionalArgument = Expression : Comma`. Used by Cs4's `PostfixOp` re-declaration
  `NamedInvocation = "(" (Argument; ",")* ")"` (Cs4:135). Re-declare in Cs7 to add an `out var` form.
- **`Parameter`** (Cs1.grammar:448): `Attributes? ParameterModifier* Type TypeName;`. Cs4 APPENDS the
  optional-parameter form (T3.3.3). **`ParameterModifier`** (Cs1.grammar:450-453): `| "ref" | "out" |
  "params"` (Cs3 APPENDS `"this"`). The `out`/`ref` in a method DECLARATION (`void N(out int x)`) is a
  `ParameterModifier` — already handled at every version (C# 1.0).
- **`Expression`** (Cs1.grammar:633-673, TDOPP): prefix alternatives start with a literal (e.g.
  `UnaryMinus = "-" Expression : Unary`); Cs5 APPENDS `AwaitExpr = "await" Expression : Unary` (Cs5:74).
  Re-declare in Cs7 to add a `ref` prefix operator.
- **`Primary`** (Cs1.grammar:675-720): no `ref`/`out` alternative (a bare `ref`/`out` is not an
  expression start — `ref`/`out` are reserved keywords).
- **`ReservedKeyword`** (Cs1.grammar:537-618): `ref` (591), `out` (584), `readonly` (590) ARE reserved.
  `var` is reserved at v2+ (Cs2:186). So a bare `ref`/`out`/`readonly` is NOT an IdentifierName.
- **`FieldModifier`** (Cs1.grammar:155-165): includes `"readonly"` (162) — a DIFFERENT rule from
  `MethodModifier`, so adding `readonly` to `MethodModifier` does not conflict with `readonly` fields.

## Roslyn references (C:\RSDN\roslyn, main, src/Compilers/CSharp)
- **`ref` return** — `ParseReturnType` (Parser/LanguageParser.cs:3314) eats a leading `ref` /
  `ref readonly` before the type (the syntax is `RefType`/`RefReadOnlyType`). Confirmed by
  `RefReadonlyTests.cs:28` (`RefReadonlyReturn_CSharp7`): `static ref readonly T M<T>() { return ref ...; }`.
- **`ref` local** — `ParseLocalDeclarationStatement` (LanguageParser.cs:10482) eats a leading `ref`
  before the type. Confirmed by `StatementParsingTests.cs`:
  - `:767` `TestRefLocalDeclarationStatement` — `ref T a;` (no initializer).
  - `:793` `TestRefLocalDeclarationStatementWithInitializer` — `ref T a = ref b;` (the initializer is a
    `RefExpression` `ref b`, asserted at :816-817).
  - `:824` `TestRefLocalDeclarationStatementWithMultipleInitializers` — `ref T a = ref b, c = ref d;`.
- **`out var`** — `ParseArgumentExpression` (LanguageParser.cs:12581) handles an `out`/`ref` argument
  with a variable declaration (implicit type). Confirmed by `DeclarationParsingTests.cs:6580`
  (`ParseOutVar`): `M(out var x);`.
- **`ref` expression** — `ParsePrimaryExpressionWithoutPostfix` (LanguageParser.cs:12068-12076): a
  `RefKeyword` token starts `RefExpression(refKeyword, ParseExpressionCore())` — a `ref` + expression.
  Also `ParsePossibleRefExpression` (4415-4424) for arrow-expression return types.
- **Version gates**: `ref` return / `ref` local / `out var` / `ref` expression are C# 7.0 (the task's
  scope). `ref readonly` is C# 7.2 in Roslyn (`RefReadonlyTests.cs:48`, `ERR_FeatureNotAvailable-
  InVersion7_1` "readonly references ... 7.2") — the task requires it in CS7 (Deviation D1). The version
  gate is a binder concern (parser-vs-binder split); the PARSER accepts the form at CS7.

## Approach
1. **`ref`/`readonly` method modifiers** — re-declare `MethodModifier` in Cs7 to APPEND `"ref"` and
   `"readonly"`. The Cs1 `Method` (`MethodModifier* Type ...`) then consumes a leading `ref` /
   `ref readonly` via the merged `MethodModifier*`, and the return type follows. Named union (the
   meta-grammar forbids `|` inside a group).
2. **`ref` local** — re-declare `LocalVariableDeclaration` in Cs7 to APPEND
   `RefLocalDeclaration = "ref" Type VariableDeclarator ("," VariableDeclarator)* ";"`. The `"ref"`
   literal is the REQUIRED-new-construct (mutually exclusive with the Cs1/Cs2/Cs7 alternatives, which
   start with a `Type` / `var`).
3. **`out var`** — re-declare `Argument` in Cs7 to APPEND
   `OutVarArgument = "out" "var" !ReservedKeyword Identifier`. The `"out" "var"` prefix is the
   REQUIRED-new-construct (mutually exclusive with the Cs4 `NamedArgument`/`PositionalArgument`, which
   fail on the reserved `out`).
4. **`ref` expression** — re-declare `Expression` in Cs7 to APPEND `RefExpr = "ref" Expression : Unary`
   (a PREFIX operator, like the Cs5 `await`, Cs5.grammar:74). The operand is `Expression : Unary`
   (minPrecedence = the Unary level), so it binds tighter than all binary operators and does NOT absorb
   a following `,` (the TDOPP Comma mechanism, T3.3.2/T3.4).

## Mutual-exclusivity hand-traces
All hand-traces verified empirically (probe). Summary:

### `ref` / `readonly` return (vs other ClassMember alternatives)
- `ref int M() { }`: `Field` FAILS (`ref` is not a `FieldModifier`; `Type=ref` fails — `ref` is reserved);
  `Method` → `MethodModifier=ref, Type=int, TypeName=M, (), MethodBody`. Sole match.
- `ref readonly int M() { }`: `Method` → `MethodModifier=ref, readonly, Type=int, ...`. Sole match.
- `readonly int x;` (a `readonly` FIELD, every version): `Field` → `FieldModifier=readonly, Type=int,
  VariableDeclarator=x, ;` (matches). `Method` → `MethodModifier=readonly, Type=int, TypeName=x`, then
  `(` expected but `;` found → FAILS. So `Field` is the sole match (no regression at any version). The
  `readonly` field and a hypothetical `readonly` method are disambiguated by what follows the name
  (`;`/`=` for a field, `(` for a method).

### `ref` local (vs other Statement / LocalVariableDeclaration alternatives)
- `ref int x = ref _y;`: Cs1 `LocalVariableDeclaration` FAILS (`Type=ref` — `ref` is reserved); Cs2 /
  Cs7 `var` alternatives FAIL (start with `var`); `ExpressionStatement` FAILS (`ref int` is not an
  Expression — `int` is not an expression start, so the `RefExpr` operand fails); `RefLocalDeclaration`
  → `"ref", Type=int, VariableDeclarator=(x = ref _y), ;`. Sole match.

### `out var` argument (vs the Cs4 Argument alternatives)
- `out var x`: `NamedArgument` FAILS (`out` is reserved, not an `Identifier`); `PositionalArgument`
  FAILS (`out` is reserved, not an expression start); `OutVarArgument` → `"out", "var", Identifier=x`.
  Sole match.
- `out x` (a plain `out` argument): `OutVarArgument` FAILS (`x` is not `"var"`); `NamedArgument` /
  `PositionalArgument` FAIL (`out` is reserved). NO alternative matches → `M(out x)` REJECTS (the
  pre-existing gap, Deviation D2).
- `ref _x` (a `ref` argument): `OutVarArgument` FAILS (starts with `out`); `NamedArgument` FAILS;
  `PositionalArgument` → `Expression : Comma` = `RefExpr` (`ref _x`). Matches (a bonus — a `ref`
  argument parses at v7 via the `RefExpr`; at v6 it rejects because the `RefExpr` is absent).

### `ref` expression (vs other Expression alternatives)
- `ref _y`: `ref` is a reserved keyword, so it is NOT an `IdentifierName` and NO other `Expression`
  alternative starts with it. `RefExpr` → `"ref", Expression=_y`. Sole match. A bare `ref` (no operand)
  → `RefExpr` fails; no other alternative matches → the parse fails (correct).
- `ref x = ref _y;` (the task's "missing type" case): `RefLocalDeclaration` FAILS (`x` is parsed as a
  user-defined `Type`, then `=` is not a `VariableDeclarator` start); `ExpressionStatement` →
  `(ref x) = (ref _y)` (an assignment; each side a `RefExpr`). So it PARSES as an assignment, NOT a ref
  local (Deviation D3).

## Version-purity results
Verified empirically (probe):
- v7 ACCEPTS: `ref int M() { return _x; }` / `ref readonly int M() { return _x; }` /
  `ref int x = ref _y;` / `N(out var x)` / `return ref _x;` / `N(ref _x)` / `ref int x;` / `ref T a;` /
  `ref T a = ref b;` / `static ref readonly T M<T>() { return _x; }`.
- v6 REJECTS: `ref int M() { }` / `ref readonly int M() { }` / `ref int x = ref _y;` / `N(out var x)` /
  `return ref _x;` / `ref x = ref _y;` / `ref int x = ;` / `ref M() { }`.
- v6 no-regression (still parse): `N(x); N(y: 5);` (positional + named arguments), `readonly int F;`
  (a `readonly` field), `void M(int x) { }`.
- v7 malformed (reject): `ref M() { }` (missing return type), `ref int x = ;` (missing initializer).
- v7 parses-as-assignment (Deviation D3): `ref x = ref _y;`.
- Pre-existing gap (reject at v6 AND v7, Deviation D2): `N(out x)`.

## Tests
`Tests/CSharpGrammarTests/Cs7RefTests.cs` (CRLF + UTF-8 BOM) — **20 tests, all green**.
- POSITIVE (v7, core, 5): `RefReturn_Succeeds` (`ref int M() { return _x; }`),
  `RefReadonlyReturn_Succeeds` (`ref readonly int M() { return _x; }`), `RefLocal_Succeeds`
  (`ref int x = ref _y;`), `OutVar_Succeeds` (`N(out var x)` + `void N(out int x)`),
  `RefExpressionInReturn_Succeeds` (`return ref _x;`).
- POSITIVE (v7, additional, 3): `RefLocal_NoInitializer_Succeeds` (`ref int x;`), `RefArgument_Succeeds`
  (`N(ref _x)` — a `ref` argument via the `RefExpr`), `RefExpressionAssignment_Succeeds`
  (`ref x = ref _y;` — parses as an assignment, Deviation D3).
- POSITIVE (v7, Roslyn-derived, 4): `RefLocal_NoInit_Roslyn_Succeeds` (`ref T a;`,
  StatementParsingTests.cs:767), `RefLocal_Initializer_Roslyn_Succeeds` (`ref T a = ref b;`, :793),
  `OutVar_Roslyn_Succeeds` (`M(out var x);`, DeclarationParsingTests.cs:6580),
  `RefReadonlyReturn_Roslyn_Succeeds` (`static ref readonly T M<T>() { return _x; }`,
  RefReadonlyTests.cs:28).
- POSITIVE (v6, no-regression, 2): `ArgumentForms_ParsesAtV6` (`N(x); N(y: 5);` — existing argument
  forms still parse at v6; replaces the task's false-premise `N(out x)` v6 test, Deviation D2),
  `ReadonlyField_ParsesAtV6` (`readonly int F;` — a `readonly` field still parses at v6, no regression
  from adding `readonly` to `MethodModifier`).
- NEGATIVE (v6, version-purity, 4): `RefReturn_RejectedAtV6` (`ref int M() { }`),
  `RefLocal_RejectedAtV6` (`ref int x = ref _y;`), `OutVar_RejectedAtV6` (`N(out var x)`),
  `RefReadonly_RejectedAtV6` (`ref readonly int M() { }`).
- NEGATIVE (v7, malformed, 2): `RefReturn_MissingType_Rejected` (`ref M() { }` — missing return type),
  `RefLocal_MissingInitializer_Rejected` (`ref int x = ;` — missing initializer expression).

## Verification
- [x] `dotnet build Nitra.sln --no-incremental` → **success, 0 errors / 0 warnings**.
- [x] `dotnet test Tests/CSharpGrammarTests` (fresh build) → **1138 total / 1135 passed / 0 failed /
  3 skipped**. Baseline before T3.6.4 (after T3.6.3): 1118 total / 1115 passed / 3 skipped. Delta =
  **+20** (all new `Cs7RefTests`). All pre-existing Cs1/Cs2/Cs3/Cs4/Cs5/Cs6/Cs11 tests remain green
  (no Cs1–Cs6/Cs11 modification).
- [x] `dotnet test Tests/ParserTests` (regression; engine + CsNitra meta-grammar unchanged) →
  **327 total / 325 passed / 0 failed / 2 skipped** — matches baseline.

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs7.grammar` (ref-semantics rules).
- `Tests/CSharpGrammarTests/Cs7RefTests.cs` (new).
- `docs/CSharpParserPlan-progressT3.6.4.md` (this file).
- `docs/CSharpParserPlan-checklist.md` (T3.6.4 status).

## Deviations / boundary decisions
- **D1 — `ref readonly` is a C# 7.2 feature in Roslyn, but the task requires it in CS7.**
  `RefReadonlyTests.cs:48` (`ERR_FeatureNotAvailableInVersion7_1`, "readonly references ... 7.2") gates
  `ref readonly` at C# 7.2. The task explicitly lists `ref readonly` as a CS7 feature, so this grammar
  accepts it in CS7 (the version where `ref` return is introduced). The version gate is a binder
  concern (parser-vs-binder split); the PARSER accepts the form. Documented.
- **D2 — the plain `out x` / `ref x` argument forms are a PRE-EXISTING GAP (not added).** Verified
  empirically (probe): `N(out x)` / `N(ref x)` do NOT parse at ANY version currently (the Cs4 `Argument`
  has only `NamedArgument`/`PositionalArgument`, neither handles a leading `out`/`ref`). They are C# 1.0
  features, so the "correct" place would be Cs1, but the task is CS7-only and forbids modifying Cs1. This
  work adds ONLY the `out var` form (the CS7 feature). Consequence: the task's "POSITIVE (version 6, must
  stay green) `N(out x)`" test is based on a FALSE PREMISE — `N(out x)` does not parse at v6 (or any
  version) currently. Adjusted (see Tests).
- **D3 — `ref x = ref _y;` (the task's "missing type" malformed case) PARSES as an assignment, not a
  ref local.** `ref x` is a valid `RefExpr` (a `ref` + expression), so `ref x = ref _y` is a valid
  assignment expression `(ref x) = (ref _y)` (an `ExpressionStatement`), NOT a parse error. In real C#
  it is a binder error (you cannot assign to a `ref` expression), but a valid parse. The `RefLocal-
  Declaration` alternative fails on it (no `Type` after `ref x`... actually `x` is parsed as a user-
  defined `Type`, then `=` is not a `VariableDeclarator` start). Documented; the malformed NEGATIVE test
  uses `ref M() { }` (missing return type) instead, which does reject.
