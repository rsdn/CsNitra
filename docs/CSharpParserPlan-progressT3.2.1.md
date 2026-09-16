# T3.2.1 — C# 3.0 lambda expressions

Status: done.

## Goal
Add C# 3.0 lambda expressions to a NEW grammar file `Parsers/CSharp/CSharpGrammar/Cs3.grammar`:
single untyped param (`x => x + 1`), no params (`() => 5`), typed param (`(int x) => x`),
multiple params (`(x, y) => x + y`), statement/block body (`x => { return x; }`), nested lambdas and
lambdas as method arguments. CS3 only (no `async`/`static`/`ref`/`out`/`params` lambda params).

## Baseline (before changes)
- `dotnet build Nitra.sln --no-incremental` → success, 0 errors / 0 warnings.
- `dotnet test Tests/CSharpGrammarTests` (fresh build) → **867 total / 864 passed / 0 failed / 3 skipped**.
- `dotnet test Tests/ParserTests` (regression) → **327 total / 325 passed / 0 failed / 2 skipped**.

## `=>` tokenization finding (the critical blocker check)
**`=>` is a single literal — NO new terminal was needed and NO blocker.**

- `CSharpTerminals.cs` defines **NO multi-char operator terminals** at all. There is no `=>`, `==`,
  `!=`, `<=`, `>=`, `+=`, `&&`, `||` terminal. Operators are grammar **literals** (`"="`, `"=="`,
  `"=>"`, …), not regex terminals.
- A grammar `Literal` matches **directly against the raw input text**: `Literal.TryMatch`
  (`ExtensibleParser/Rules.cs:76`) is `input.AsSpan(startPos).StartsWith(Value.AsSpan(),
  StringComparison.Ordinal) ? Value.Length : -1`. There is **no separate tokenization step** that would
  split `=>` into `=` then `>`. A `Literal("=>")` matches the 2-char sequence `=>` directly.
- Multi-char operators **already work as single literals** in Cs1, proving the mechanism:
  - `OperatorSymbol` (Cs1:356-371) includes `"=="`, `"!="`, `"++"`, `"--"`.
  - The `Expression` rule (Cs1:656-670) uses `"<="`, `">="`, `"&&"`, `"||"` as single literals.
- Roslyn confirms `=>` is one token: `SyntaxKind.EqualsGreaterThanToken`, eaten by
  `this.EatToken(SyntaxKind.EqualsGreaterThanToken)` (LanguageParser.cs:13919 parenthesized /
  :13934 unparenthesized).
- Consequence: the lambda rule uses the literal `"=>"`. The split-arrow negative `x = > x` is REJECTED
  because `Literal("=>").TryMatch` requires the contiguous 2-char sequence `=>` (a space breaks it), so
  `x = > x` is not a lambda (the `Assign` RHS `> x` is not an expression) — covered by
  `Lambda_SplitArrow_Rejected`.

## Cs3.grammar rules (exact)
```
Primary =
    | LambdaExpression = LambdaParameters "=>" LambdaBody;

LambdaParameters =
    | SimpleLambdaParameter = !ReservedKeyword Identifier
    | LambdaParameterList = "(" (LambdaParameter; ",")* ")";

LambdaParameter =
    | LambdaTypedParameter = Type TypeName
    | LambdaUntypedParameter = TypeName;

LambdaBody =
    | Expression
    | Block;
```
- `Primary` is RE-DECLARED (append, T0.3 merge) — the same pattern Cs2 used for `AnonymousMethod`
  (Cs2:198) and Cs11 for raw strings. The lambda is a `Primary`/expression, **NOT** a TDOPP binary
  operator; `=>` is part of the lambda `Primary`, and `=>` is NOT added to any TDOPP precedence list.
- `LambdaParameters` is a named union (the meta-grammar forbids `|` inside a group). `SimpleLambdaParameter`
  is `!ReservedKeyword Identifier` (a reserved keyword cannot be a lambda param name);
  `LambdaParameterList` is `"(" (LambdaParameter; ",")* ")"` (empty list allowed — `() => …`).
- `LambdaParameter` is a named union: typed (`Type TypeName`) OR untyped (`TypeName`). **No
  `ParameterModifier*`** — Roslyn `ParseLambdaParameter` (LanguageParser.cs:14007) parses
  ref/out/params/in/readonly modifiers, but all are version-gated to CS7.2+/CS8/CS9, so CS3 allows
  none. (The task's suggested `ParameterModifier*` was dropped after verifying with Roslyn.)
- `LambdaBody` is a named union: `Expression` OR `Block`, disambiguated by leading token (`{` → Block,
  else → Expression) — Roslyn `ParseLambdaBody` (LanguageParser.cs:13945).

## Mutual-exclusivity hand-traces (no equal-length tie)

### Lambda vs plain identifier (`x => …` vs `x`)
- `x => x + 1`: `Primary` tries all alternatives. `IdentifierName` → `x` (stops before `=>`, 1 name).
  `LambdaExpression` → `SimpleLambdaParameter=x` + `=>` + body `x + 1` → matches `x => x + 1` (longer).
  Longest-match → **LambdaExpression**. No tie.
- `x` (no `=>`): `IdentifierName` → `x`. `LambdaExpression` → `SimpleLambdaParameter=x`, then `"=>"`
  fails (next is not `=>`) → LambdaExpression fails. → **IdentifierName only**. No tie.

### Lambda vs parenthesized expression (`(x) => …` vs `(x)`)
- `(x) => x`: `Parens = "(" Expression ")"` → `(x)` (stops after `)`, shorter).
  `LambdaExpression` → `LambdaParameterList=(x)` + `=>` + body `x` → matches `(x) => x` (longer).
  Longest-match → **LambdaExpression**. No tie.
- `(x)` (no `=>`): `Parens` → `(x)`. `LambdaExpression` → `LambdaParameterList=(x)`, then `"=>"` fails
  (next is not `=>`) → LambdaExpression fails. → **Parens only**. No tie.

### Lambda parameters: simple vs parenthesized (inside `LambdaParameters`)
- `x`: `SimpleLambdaParameter` → `x`; `LambdaParameterList` fails (no `(`). → simple only.
- `(x)`: `SimpleLambdaParameter` fails (`(` is not an identifier); `LambdaParameterList` → `(x)`. → list only.

### Lambda parameter: typed vs untyped (inside `LambdaParameter`, longest-match)
- `int x`: typed → `int x` (longer); untyped fails (`int` reserved, not a `TypeName`). → typed.
- `x`: typed fails (needs a 2nd name); untyped → `x`. → untyped.
- `Foo x`: typed → `Foo x` (longer); untyped → `Foo` (shorter). → typed (longest). No tie.

### Lambda body: expression vs block (inside `LambdaBody`)
- `{ … }`: `Expression` fails (`{` is not an expression start — no `Primary` alternative begins with
  `{`); `Block` → `{ … }`. → Block only.
- `expr`: `Block` fails (no `{`); `Expression` → expr. → Expression only. Different leading tokens,
  mutually exclusive, no tie.

## TDOPP body stops-at-separator verification
The expression body is a full TDOPP `Expression` referenced as a plain `Ref` (minPrecedence 0).
Precedence binding powers (Cs1:623-626, `bp = count - index`, 14 entries): `Comma` is last → **bp 1**,
not right-associative. In `ContinueFromPartialPostfix` (Parser.cs:360) a postfix applies iff
`precedence > minPrecedence || (== && right)`.
- **Stops at `)` and `}`**: neither is a binary operator, so no postfix matches — the body stops there.
  Verified by `Run(x => x + 1)`, `var f = () => 5;`, `Run(x => Run(y => x + y))`,
  `Run(x => x > 0 ? x : -x)` (all parse, body stops before `)`/`;`).
- **Consumes a following `,`**: the `Comma` postfix (bp 1) IS applicable at minPrecedence 0
  (`1 > 0`), so a lambda body like `x + 1` followed by `,` extends to `x + 1, …`. This is the
  **EXISTING** grammar behavior for ANY top-level `Expression` in an argument list (e.g.
  `new Foo(1, 2)` already parses `1, 2` as one comma expression — the `Comma` operator is defined in
  Cs1:673 and is the only top-level comma consumer), **not** a lambda-specific defect. It does NOT
  cause a parse failure: in `N(x => x + 1, y => y + 1)` the first lambda body greedily absorbs
  `, y => y + 1`, the argument list then has one element, and the whole input is still consumed →
  the positive test PASSES. Documented as a boundary decision below.

## Roslyn references (C:\RSDN\roslyn, main)
- `ParseLambdaExpression` / `parseLambdaExpressionWorker`
  (src/Compilers/CSharp/Portable/Parser/LanguageParser.cs:13891-13943): `(` → `ParseLambdaParameterList`
  + `EqualsGreaterThanToken` + `ParseLambdaBody`; else identifier + `EqualsGreaterThanToken` +
  `ParseLambdaBody`.
- `ParseLambdaBody` (LanguageParser.cs:13945-13948): `{` → `ParseBlock`, else `ParsePossibleRefExpression`.
- `ParseLambdaParameterList` (LanguageParser.cs:13950): `(` `(ParseLambdaParameter,)*` `)` with
  `requireOneElement:false` (empty `()` allowed).
- `ParseLambdaParameter` (LanguageParser.cs:14007): modifiers (ref/out/params/in/readonly —
  version-gated, not CS3) + optional type (`ShouldParseLambdaParameterType` 14038) + identifier.
- `IsPossibleLambdaExpression` (LanguageParser.cs:13054): disambiguation checks
  `PeekToken(1) == EqualsGreaterThanToken` — the same signal longest-match uses here.
- `=>` is one token: `SyntaxKind.EqualsGreaterThanToken` (LanguageParser.cs:13919/13934).
- CS3 vs later: async=CS5, static=CS8, ref/out params=CS7.2/CS9, in/readonly params=CS7.2/CS8,
  params keyword=CS9. CS3 allows none of these modifiers on lambda params.
- Syntax tests: ExpressionParsingTests.cs:2039 (`a => b`), :2075 (`a => { }`), :2095 (`() => b`),
  :2135 (`() => { }`), :2157 (`(a) => b`), :2181 (`(a, a2) => b`), :2208 (`(T a) => b`).

## Setup steps done
- [x] Created `Parsers/CSharp/CSharpGrammar/Cs3.grammar` (CRLF, no BOM — verified 77 CRLF / 0 LF-only).
- [x] Added `<EmbeddedResource Include="Cs3.grammar" />` to `CSharpGrammar.csproj` (between Cs2 and Cs6,
  following the Cs1/Cs2/Cs6/Cs11 pattern).
- [x] Added `new(3, "Cs3.grammar", "Cs3.grammar"),` to `EmbeddedGrammar.cs` `_versions` (ascending,
  between version 2 and version 6).
- [x] Updated `CSharpVersionInfrastructureTests.cs` (3 tests that assert the exact version-table
  contents, which changed when Cs3 was added — directly related to the version-table edit, not an
  unrelated file): `LoadGrammarUpTo_6_…` now yields Cs1/Cs2/Cs3/Cs6 (4), `LoadGrammarUpTo_11_…` now
  yields Cs1/Cs2/Cs3/Cs6/Cs11 (5), `LoadGrammarUpTo_4_…` now yields Cs1/Cs2/Cs3 (3, Cs4 absent).
- [x] Created `Tests/CSharpGrammarTests/Cs3LambdaTests.cs` (CRLF + UTF-8 BOM — verified 167 CRLF /
  0 LF-only, BOM EF BB BF).
- [x] `CSharpTerminals.cs` was **NOT** modified (no `=>` terminal needed — see tokenization finding).
- [x] Removed a pre-staged redundant `<Compile Include="Cs3LambdaTests.cs" />` from
  `Tests/CSharpGrammarTests/CSharpGrammarTests.csproj`. The .NET SDK auto-includes all `.cs` files in
  the project directory (no `EnableDefaultCompileItems=false`), so the explicit include produced a
  `NETSDK1022` duplicate-`Compile`-item build error once the file existed. No other test file is
  explicitly listed in that csproj (they all come from the default glob), so removing the redundant
  line is consistent. (The line had been pre-staged in the working tree before this task started;
  it is directly related to the new test file, not an unrelated file.)

## Version-purity results
- `class C { void M() { Run(x => x + 1); } }` → **rejects at v2** (no lambda Primary) / **parses at v3**.
- `class C { void M() { var f = () => 5; } }` → **rejects at v2** / **parses at v3**.
- At v2 the lambda is not a `Primary`, so `Run(x => x + 1)` fails (after `x`, `=>` is not a postfix
  operator and not a separator) and `var f = () => 5;` fails (`( )` is empty, `=>` is not an
  expression start). All pre-existing Cs1/Cs2/Cs6/Cs11 tests stay green (no Cs1/Cs2 modification).

## Tests
`Tests/CSharpGrammarTests/Cs3LambdaTests.cs` (CRLF + UTF-8 BOM) — **21 tests, all green**.
- POSITIVE (v3, 8): `Lambda_SingleUntypedParam_ExpressionBody` (`Run(x => x + 1)`),
  `Lambda_NoParams_ExpressionBody` (`var f = () => 5;`), `Lambda_TypedParam_ExpressionBody`
  (`Run((int x) => x)`), `Lambda_MultipleUntypedParams_ExpressionBody` (`Run((x, y) => x + y)`),
  `Lambda_SingleUntypedParam_BlockBody` (`Run(x => { return x; })`), `Lambda_MultipleLambdasAsArguments`
  (`N(x => x + 1, y => y + 1)`), `Lambda_NestedLambdas` (`Run(x => Run(y => x + y))`),
  `Lambda_ComplexExpressionBody` (`Run(x => x > 0 ? x : -x)`).
- POSITIVE (v3, Roslyn-derived, 7): `Roslyn_SimpleLambda_ExpressionBody` (`a => b`,
  ExpressionParsingTests.cs:2039), `Roslyn_SimpleLambda_BlockBody` (`a => { }`, :2075),
  `Roslyn_NoParameters_ExpressionBody` (`() => b`, :2095), `Roslyn_NoParameters_BlockBody` (`() => { }`,
  :2135), `Roslyn_OneUntypedParameter` (`(a) => b`, :2157), `Roslyn_TwoUntypedParameters`
  (`(a, a2) => b`, :2181), `Roslyn_OneTypedParameter` (`(T a) => b`, :2208).
- VERSION-PURITY (v2, 2): `Lambda_SingleUntypedParam_RejectedAtV2` (`Run(x => x + 1)` rejects at v2),
  `Lambda_NoParams_RejectedAtV2` (`var f = () => 5;` rejects at v2).
- NEGATIVE (v3, malformed, 4): `Lambda_MissingBody_Rejected` (`var f = x => ;`),
  `Lambda_MissingParams_Rejected` (`var f = => x;`), `Lambda_UnclosedParamList_Rejected`
  (`Run((x => x + 1)`), `Lambda_SplitArrow_Rejected` (`var f = x = > x;`).

## Verification
- [x] `dotnet build Nitra.sln --no-incremental` → **success, 0 errors / 0 warnings**.
- [x] `dotnet test Tests/CSharpGrammarTests` (fresh build) → **888 total / 885 passed / 0 failed /
  3 skipped**. Baseline before T3.2.1: 867 total / 864 passed / 3 skipped. Delta = **+21** (all new
  `Cs3LambdaTests`). Filtered run of `Cs3LambdaTests` → **21 passed / 0 failed**. All pre-existing
  Cs1 / Cs2 (T3.1.1.1 + T3.1.1.2 + T3.1.2 + T3.1.3) / Cs6 / Cs11 tests remain green.
- [x] `dotnet test Tests/ParserTests` (regression; engine + CsNitra meta-grammar unchanged) →
  **327 total / 325 passed / 0 failed / 2 skipped** — matches baseline.

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs3.grammar` (new).
- `Parsers/CSharp/CSharpGrammar/CSharpGrammar.csproj` (added `<EmbeddedResource Include="Cs3.grammar" />`).
- `Tests/CSharpGrammarTests/EmbeddedGrammar.cs` (added version-3 entry).
- `Tests/CSharpGrammarTests/CSharpVersionInfrastructureTests.cs` (updated 3 version-table assertions).
- `Tests/CSharpGrammarTests/Cs3LambdaTests.cs` (new).
- `Tests/CSharpGrammarTests/CSharpGrammarTests.csproj` (removed pre-staged redundant `<Compile
  Include="Cs3LambdaTests.cs" />` that caused a `NETSDK1022` duplicate-item build error).
- `docs/CSharpParserPlan-checklist.md` (T3.2.1 `[~]` → `[✅]`).
- `docs/CSharpParserPlan-progressT3.2.1.md` (this file).
- `CSharpTerminals.cs` — **NOT changed** (no `=>` terminal needed).

## Boundary decisions / deviations
- **No `=>` terminal (no blocker)**: the fat arrow is a grammar `Literal("=>")` matched directly against
  the raw input (`Literal.TryMatch`, Rules.cs:76). `CSharpTerminals.cs` has no multi-char operator
  terminals; multi-char operators already work as single literals in Cs1 (`==`/`!=`/`++`/`--` in
  `OperatorSymbol`, `<=`/`>=`/`&&`/`||` in `Expression`). So `"=>"` matches the contiguous 2-char
  sequence; the split `= >` is correctly rejected. No shared terminal infrastructure touched.
- **No `ParameterModifier*` on lambda params** (deviation from the task's suggested rule shape): Roslyn
  `ParseLambdaParameter` (LanguageParser.cs:14007) parses ref/out/params/in/readonly, but every one is
  version-gated to CS7.2+/CS8/CS9. CS3 allows none, so `LambdaParameter` is `Type TypeName | TypeName`.
- **Lambda body consumes a following `,`** (documented, not a regression): the TDOPP `Comma` postfix
  (bp 1) is applicable at minPrecedence 0, so a lambda expression body greedily absorbs a following
  comma (e.g. `N(x => x + 1, y => y + 1)` parses the first body as `x + 1, y => y + 1`). This is the
  EXISTING behavior of the `Expression` rule for any top-level expression in an argument list (the
  `Comma` operator, Cs1:673, already makes `new Foo(1, 2)` a single comma expression) and does not
  cause a parse failure — the whole input is still consumed, so the positive test passes. Changing this
  would require altering the shared TDOPP `Expression`/`Comma` rule (out of scope, risky); left as-is.
- **Lambdas are a `Primary`, not a TDOPP operator**: `=>` is part of the lambda `Primary`; it is NOT
  added to any TDOPP precedence list (per the design principle). Disambiguation from a plain identifier
  / parenthesized expression is by longest-match (the lambda is longer when `=>` follows), matching
  Roslyn's `IsPossibleLambdaExpression` (`PeekToken(1) == EqualsGreaterThanToken`).
- **CS3 only**: no `async` (CS5), no `static` (CS8), no `ref`/`out`/`params`/`in`/`readonly` lambda
  params (CS7.2+/CS8/CS9), no explicit return type, no attributed lambdas.
