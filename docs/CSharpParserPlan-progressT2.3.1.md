# T2.3.1 — C# 1.0 Expression: `new` / `is` / `as` / `sizeof` / `typeof` — Progress

## Status: done (build 0 errors; CSharpGrammarTests 335 passed / 0 failed / 3 pre-existing skips)

## Task
Complete the provisional TDOPP `Expression` rule in `Parsers/CSharp/CSharpGrammar/Cs1.grammar` to
support C# 1.0 expression constructs currently missing:
1. `new` — object creation (`new Foo`, `new Foo(1, 2)`) and array creation
   (`new int[5]`, `new int[2, 3]`, `new int[] { 1, 2, 3 }`).
2. `is` / `as` — type test: `expr is Type`, `expr as Type`.
3. `sizeof` / `typeof` — `sizeof(Type)`, `typeof(Type)`.
NOT in scope: cast `(Type) expr` (T2.3.2).

## Roslyn semantics ported (C:\RSDN\roslyn, src/Compilers/CSharp/Portable/Parser/LanguageParser.cs)

- **`is` / `as`** — binary operators in the operator loop at **`Precedence.Relational`**
  (GetPrecedence, LanguageParser.cs:11267-11268; same level as `<` `>` `<=` `>=`).
  - `as` → `BinaryExpression(AsExpression, left, op, ParseType(ParseTypeMode.AsExpression))`
    (LanguageParser.cs:11602-11606) — RHS is a **Type**.
  - `is` → `ParseIsExpression` (LanguageParser.cs:11931-11940) → `ParseTypeOrPatternForIsOperator`.
    In C# 1.0 only the Type branch applies (patterns are CS7+) — RHS is a **Type**.
  - Precedence-inversion guard (LanguageParser.cs:11583-11600): a tighter operator immediately after
    the type is an error because the RHS is a Type, not an Expression. (Not reproduced here — the
    grammar's longest-match + full-consumption already rejects the malformed shapes.)
- **`typeof` / `sizeof`** — **primary expressions** (ParsePrimaryExpressionWithoutPostfix,
  LanguageParser.cs:11956-11961), precedence `Unary` (LanguageParser.cs:11291-11292).
  - `typeof` → `typeof ( ParseTypeOrVoid )` (LanguageParser.cs:12624-12631).
  - `sizeof` → `sizeof ( ParseType )` (LanguageParser.cs:12650-12657).
  - Both are self-contained `keyword ( Type )` units → modeled as **Primary** alternatives.
- **`new`** — `ParseNewExpression` → `ParseArrayOrObjectCreationExpression`
  (LanguageParser.cs:13245-13428). Two shapes:
  - **Array creation**: type is an `ArrayType` (a rank specifier is present) → optional array
    initializer `{ ... }` follows. Sizes live inside the rank specifier in `NewExpression` type mode
    (ParseArrayRankSpecifier, LanguageParser.cs:7860-7918 — `[ expr (, expr)* ]` or `[]`).
  - **Object creation**: type is a non-array type → optional argument list `( ... )`; optional
    object/collection initializer `{ ... }`. **If neither an argument list nor an initializer is
    present → `ERR_BadNewExpr` (CS1526)** (LanguageParser.cs:13417-13423).
  - Confirmed negative: `new int;` → `ERR_BadNewExpr` (ParserErrorMessageTests.cs
    `CS1526ERR_BadNewExpr`, line 4361-4375). So **`new Foo` (no parens) is a parse error**, not a
    positive form (see Boundary decisions — the task listed `new Foo` as positive; Roslyn rejects it).

## Type-operand challenge — DECISION (mechanism chosen: option (a), declarative)

`is`/`as` take a **Type** as their RHS, not an `Expression`. The provisional TDOPP table assumes both
sides of a binary operator are `Expression`. The problem: the RHS `int[]` is a Type and is NOT a valid
Expression, and — even if parsed as one — the Type's own postfix ranks (`TypeArray`/`TypePointer`) sit
at low binding power, so a RHS parsed at the operator's precedence would drop the `[]`/`*` ranks.

**Chosen mechanism (a): the TDOPP postfix references `Type` (a non-`Expression`) as its operand.**
Concretely, the self-recursive alternative is written as:

```
TypeIs = Expression : Relational "is" Type
TypeAs = Expression : Relational "as" Type
```

Why this works (verified in `ExtensibleParser/Parser.cs:123-162` `BuildTdoppRulesInternal`):
- A self-recursive alternative is detected as `Seq [ Ref self, .. rest ]`. The postfix `Seq` is `rest`
  (the first self-ref is dropped — it is the "left side" marker, already parsed as the prefix).
- The postfix **precedence** is taken from a `ReqRef` in `rest`; if there is none, it falls back to the
  **first element's** precedence when that element is a `ReqRef` (`Parser.cs:144`).
- Here `rest = [ Literal("is"), Ref("Type") ]` has **no** `ReqRef`, so the precedence comes from the
  first element `Expression : Relational` → the `is`/`as` postfix binds at **Relational**.
- The RHS is a **plain** `Ref("Type")` → parsed at `minPrecedence 0` → **all** Type postfixes
  (`TypeArray`, `TypePointer`) apply → `int[]`, `int*`, `int[]*` are complete.

This **decouples** the operator precedence (Relational) from the Type parse (complete type at prec 0).
It is the exact same first-element-`ReqRef` pattern the `Type` rule already uses for its own postfixes
(`PointerType = Type : TypePointer "*"`, `ArrayType = Type : TypeArray ArrayRankSpecifier`,
Cs1.grammar:162-163), so it is an established, declarative pattern in this codebase — no hand-written
Terminal, no precedence reordering, no engine change.

Precedence check (C# spec: `is`/`as` are "Relational and type testing", tighter than `==`, looser than
`+`/`-`):
- `x is int == y` → `(x is int) == y` (is=Relational binds tighter than ==/Equality). ✓
- `a + b is int` → `(a + b) is int` (Additive binds tighter than is/Relational). ✓

## Grammar changes (exact rules, `Parsers/CSharp/CSharpGrammar/Cs1.grammar`)

### `Expression` rule — two new postfix alternatives (lines 299-304, after the relational operators)
```
| TypeIs        = Expression : Relational "is" Type
| TypeAs        = Expression : Relational "as" Type
```

### `Primary` rule — one changed + three new alternatives (lines 321-334)
- **Changed** (line 323): bare `Identifier` → `IdentifierName = !ReservedKeyword Identifier`.
  Required so reserved words (`new`/`typeof`/`sizeof`/…) are not parsed as identifiers — otherwise
  `typeof(int, int)` is swallowed as an invocation and `new` alone parses as an identifier. Same
  `!ReservedKeyword` guard as `TypeName` (line 23). (Named alternative: the CsNitra meta-grammar
  only allows a rule-ref/literal as an *anonymous* alternative — a predicate/sequence needs a name.)
- **New** (lines 332-334):
```
| NewExpr = "new" NewBody
| SizeOf  = "sizeof" "(" Type ")"
| TypeOf  = "typeof" "(" Type ")"
```

### New rules (expression-context, lines 349-366)
```
NewBody =
    | ArrayCreation  = NewBaseType NewArrayRank ArrayInitializer?
    | ObjectCreation = NewBaseType ArgumentList;

NewBaseType =
    | PredefinedType
    | QualifiedName;

NewArrayRank = "[" (Expression; ",")* "]";

ArrayInitializer = "{" (Expression; ",")* "}";

ArgumentList = "(" (Expression; ",")* ")";
```

## Boundary decisions / deviations

- **`Primary` identifier guard added** (`Identifier` → `!ReservedKeyword Identifier`): a required,
  in-scope fix. Without it the new keywords (`new`/`typeof`/`sizeof`) fall back to the bare
  `Identifier` alternative, producing false positives (`typeof(int, int)` → invocation; `new` alone
  → identifier). It mirrors the existing `TypeName = !ReservedKeyword Identifier` guard. Only affects
  the provisional `Expression` rule (no pre-existing test exercises it).
- **`new Foo` (no parens, no sizes, no initializer) is a NEGATIVE test** (task listed it positive).
  Roslyn reports `ERR_BadNewExpr` (CS1526) when neither an argument list nor an initializer follows the
  type (LanguageParser.cs:13417-13423; ParserErrorMessageTests.cs `CS1526ERR_BadNewExpr`). `new Foo()`
  and `new Foo(1,2)` are the positive object-creation forms.
- **Object/collection initializers on object creation are excluded** (`new Foo { ... }` is CS3).
  Array initializers (`new int[] { ... }`) ARE C# 1.0 and are kept.
- **`NewBaseType` excludes pointer types** (only `PredefinedType | QualifiedName`). `new int*` is
  invalid C# anyway; arrays of pointers (`new int*[5]`, unsafe) are a niche case left out (boundary).
- **Comma inside `(Expression; ",")*` lists**: the provisional `Expression` includes the `Comma`
  operator, so `new Foo(1, 2)` parses `1, 2` as a single comma-expression element rather than two
  arguments. This is a pre-existing property of the provisional TDOPP table (T3.5.1 will replace it);
  the forms still parse cleanly (0 errors, full consumption), which is what the tests assert.
- **`sizeof`/`typeof` modeled as Primary** (self-contained `keyword ( Type )` units), matching Roslyn's
  primary-expression dispatch, rather than as Unary-level operators.

## Tests written
All in `Tests/CSharpGrammarTests/`, parsed via the new `Cs1ExpressionTestHelper` (start rule
`"Expression"` — the C# 1.0 grammar has no statement/declaration that embeds an Expression, so the
TDOPP `Expression` rule is exercised directly).

- **POSITIVE: 40**
  - `Cs1ExpressionTests.cs` — 33 (new object/array, is/as incl. array/pointer/qualified RHS,
    sizeof/typeof, 8 precedence-interaction cases).
  - `Cs1RoslynExpressionTests.cs` — 7 (Roslyn-derived, see below).
- **NEGATIVE: 14**
  - `Cs1ExpressionTests.cs` — 13 (version purity: generics/nullable/object+collection init;
    malformed: `new Foo` no-parens, unclosed bracket/paren, `typeof(int,int)`, missing type).
  - `Cs1RoslynExpressionTests.cs` — 1 (`new Foo` no-parens, from `CS1526ERR_BadNewExpr`).

Roslyn-derived cases (adapted to bare Expression; source file:method documented per test):
- `ForStatementParsingTest.TestVariousExpressions_Typeof` → `typeof(int)`.
- `ForStatementParsingTest.TestVariousExpressions_ArrayCreation` → `new int[] { }`.
- `ForStatementParsingTest.TestVariousExpressions_ObjectCreation1` → `new A()`.
- `RoundTrippingTests.Bug909419` → `x is T ? 0 : 1` and `null == x as T ? 0 : 1`.
- `NullableParsingTests` (is-type-test-array) → `x is T[] ? y : z`.
- `PatternParsingTests` (is-type-test jagged array) → `o is A[][] ? b : c`.
- `ParserErrorMessageTests.CS1526ERR_BadNewExpr` → `new Foo` (negative).

## Verification
- `dotnet build Nitra.sln --no-incremental` → **Build succeeded, 0 Warning(s), 0 Error(s)**.
- `dotnet test Tests/CSharpGrammarTests --no-build` → **Passed: 335, Failed: 0, Skipped: 3**
  (3 skips are pre-existing `RawString`/`StringLiteral` tests, unrelated to T2.3.1).
  - New T2.3.1 tests: `Cs1ExpressionTests` (46) + `Cs1RoslynExpressionTests` (8) = 54, all green.
- Engine / CsNitra meta-grammar NOT changed (only the C# grammar text + tests) → `ParserTests`
  not re-run (per task: only required when engine/meta-grammar changes).

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs1.grammar` (Expression: +is/+as; Primary: +new/+sizeof/+typeof,
  Identifier guard; +NewBody/NewBaseType/NewArrayRank/ArrayInitializer/ArgumentList rules)
- `Tests/CSharpGrammarTests/Cs1ExpressionTestHelper.cs` (new)
- `Tests/CSharpGrammarTests/Cs1ExpressionTests.cs` (new, 46 tests)
- `Tests/CSharpGrammarTests/Cs1RoslynExpressionTests.cs` (new, 8 tests)
- `docs/CSharpParserPlan-progressT2.3.1.md` (this file)
