# T2.3.2 — C# 1.0 Expression: cast `(Type) expr` — Progress

## Status: done (build 0 errors / 0 warnings; CSharpGrammarTests 360 passed / 0 failed / 3 pre-existing skips)

## Task
Add cast expressions `(Type) expr` to the C# 1.0 grammar's TDOPP `Expression` rule in
`Parsers/CSharp/CSharpGrammar/Cs1.grammar`. Two challenges:
1. **Disambiguation** — cast `(Type) expr` vs parenthesized `(expr)`.
2. **Precedence** — cast binds tighter than all binary operators (tightest level, just below postfix).

## Roslyn semantics ported (C:\RSDN\roslyn, src/Compilers/CSharp/Portable/Parser/LanguageParser.cs)

- **Dispatch**: `ParsePrimaryExpression` `case OpenParenToken` → `ParseCastOrParenExpressionOrTuple`
  (LanguageParser.cs:12828). It first calls `ScanCast(forPattern:false, ...)`; if it "looks like a
  cast" it parses `CastExpression(open, ParseType(), close, ParseSubExpression(Precedence.Cast))`
  (LanguageParser.cs:12844-12848); otherwise a parenthesized expression.
- **`ScanCast`** (LanguageParser.cs:12916-13047): after `(` it `ScanType()`s; if not a type or not
  followed by `)` → not a cast. Then, depending on the scanned type's `ScanTypeFlags`:
  - **Unambiguous types** — `MustBeType` (predefined `int`/`string`/…), `PointerOrMultiplication`
    (`Goo*`), `NullableType`, `AliasQualifiedName` (`G::Goo`) → the thing between the parens is
    unambiguously a type → **always a cast** in expression context (LanguageParser.cs:12965-12988).
  - **Ambiguous types** — `NonGenericTypeOrExpression` (a bare identifier `x` / qualified `A.B`,
    which is both a type and an expression) → a cast **only if** `CanFollowCast(nextToken)`
    (LanguageParser.cs:13001-13014). Comment: `"(A)b" is a cast. But "(A)+b" is not a cast.`
- **`CanFollowCast`** (LanguageParser.cs:13184-13243) returns **false** for: `as`/`is`, `;`, `)`, `]`,
  `{`, `}`, `,`, `=`, all compound-assign, `?`, `:`, `||`, `&&`, `|`, `^`, `&`, `==`, `!=`, `<`, `<=`,
  `>`, `>=`, `??=`, `<<`, `>>`, `>>>`, `+`, `-`, `*`, `/`, `%`, `++`, `--`, `[`, `.`, `->`, `??`, `EOF`,
  `switch`, `=>`. Everything else (identifiers, literals, `(`, `!`, …) → **true**.
- **C# 1.0 scope**: `ScanType` here is C#-version-independent for our shapes; no `dynamic` (CS4), no
  generic casts (CS2). Kept: predefined / qualified / array / pointer types.

## Disambiguation approach (declarative, longest-match)

Chosen: **a single `Cast` prefix alternative + rely on the engine's longest-match-wins**, with
`PrimaryExpr` (which contains `Parens = "(" Expression ")"`) tried first as the tie-breaker.

Why it is correct (traced against Roslyn):
- **Unambiguous types** (`int`, `int[]`, `int*`, `System.String`): `int`/`int[]`/`int*` are NOT valid
  expression starts (reserved keyword / no such primary), so `Parens` (and thus `PrimaryExpr`) **fails**
  on them → only `Cast` matches. `System.String` IS a valid expression, so `Parens` matches
  `(System.String)`, but `Cast` matches **longer** (it also consumes the operand `s`) → `Cast` wins by
  length. Matches Roslyn's "unambiguous type → always cast".
- **Ambiguous types** (`x`, `a.b`): both `Parens` (via `PrimaryExpr`) and `Cast` are candidates.
  - If a real cast operand follows that extends the match (`(System.String) s`, `(x) y`), `Cast` is
    longer → cast (Roslyn: `CanFollowCast(identifier)` = true).
  - If the following token cannot start a cast operand (`(x) * y`, `(1 + 2)`, `(x)`, `(a.b)`), `Cast`
    fails and `Parens`/`PrimaryExpr` is the only match → parens (Roslyn: `CanFollowCast(*`/EOF/`)`)=false).
  - The one **tie** case: the cast operand is a unary prefix that also reads as a binary continuation —
    `(x) + 1` / `(x) - 1`. Both `PrimaryExpr` (parens + Add/Sub) and `Cast` (operand = `+1`/`-1`)
    consume the full input. `PrimaryExpr` is the **first** prefix alternative, so it wins the tie →
    parens. This matches Roslyn exactly: `CanFollowCast(PlusToken/MinusToken)` = false → not a cast.

The tie-break relies on `PrimaryExpr = Primary PostfixOp*` being the first alternative of `Expression`
(it is — see Cs1.grammar). This is the same longest-match mechanism the engine already uses.

## Precedence mechanism (declarative)

Cast is a **TDOPP prefix operator** (like the existing `UnaryPlus = "+" Expression : Unary`), NOT a
`Primary`. The operand is a `ReqRef` at a new tightest precedence level `Cast`:

```
| CastExpr = "(" Type ")" Expression : Cast
```

- A `Seq` whose first element is `Literal("(")` (not a self-`Ref`) is classified as a **prefix** by
  `BuildTdoppRulesInternal` (ExtensibleParser/Parser.cs:137-149). The operand
  `Expression : Cast` is a `ReqRef("Expression", Cast)` → parsed at minPrecedence = Cast binding power
  (Parser.cs:507 `ReqRef r => ParseRule(r.RuleName, r.Precedence, ...)`).
- New precedence level `Cast` is added at the **top** of the precedence list (before `Unary`), so it
  gets the highest binding power (Parser.cs / CsNitraTypeChecker.cs:56-64: binding power decreases from
  the first list element). No binary/unary postfix has a binding power above `Cast`, so the cast
  operand's postfix loop applies nothing → the operand is a primary (with its `PostfixOp*`) plus any
  unary prefixes, but never a binary operator.
- Precedence check:
  - `(int) x + y` → operand `x` (Additive < Cast, not consumed) → `((int)x) + y`. ✓
  - `(int) x * y` → `((int)x) * y`. ✓
  - `(int) x.y` → operand `x.y` (member access is part of the primary `Primary PostfixOp*`) → `((int)x).y`. ✓
  - `(int) ++x` → operand `++x` (unary prefix, always tried) → `((int)(++x))`. ✓
  - `((int) x)` → outer `Parens` wraps the inner cast. ✓
- Adding `Cast` at the top only adds a new highest binding power; every existing level keeps its
  relative order and (because binding power = Count - index) its numeric value, so no existing rule
  changes behaviour.

## Engine file:line refs
- `ExtensibleParser/Parser.cs:123-162` `BuildTdoppRulesInternal` — prefix/postfix classification.
- `ExtensibleParser/Parser.cs:258-322` `ParseRule` prefix loop + longest-match tie-break (Success over
  Partial only; first wins a true tie).
- `ExtensibleParser/Parser.cs:337-424` `ContinueFromPartialPostfix` — postfix loop, `isApplicable =
  postfix.Precedence > minPrecedence || (== && Right)` (line 360).
- `ExtensibleParser/Parser.cs:507` `ReqRef r => ParseRule(r.RuleName, r.Precedence, ...)` — operand
  parsed at the ReqRef precedence.
- `Parsers/CsNitra/CsNitraGrammar/TypeChecking/CsNitraTypeChecker.cs:56-64` — binding power assignment
  (first in list = highest).
- `Parsers/CsNitra/CsNitraGrammar/CsNitraParser.cs:81-125` — meta-grammar `RuleExpression`: a general
  `Sequence` of literals/refs/reqrefs is allowed (so `"(" Type ")" Expression : Cast` is legal).

## Boundary decisions / deviations

- **`(int) x.y` → `(int)(x.y)`, NOT `((int)x).y`** (task's precedence note said `((int)x).y`).
  Per the C# 1.0 spec the cast operand is a `unary-expression`, which includes a
  `primary-expression` → `member_access_expression` (`x.y`); the postfix `.y` is therefore part of
  the cast operand, exactly like `!x.y` → `!(x.y)`. Roslyn agrees: `ParseCastOrParenExpressionOrTuple`
  parses the operand via `ParseSubExpression(Precedence.Cast)` → `ParsePrimaryExpression()` (which
  applies postfix) (LanguageParser.cs:12848). Our grammar gives `(int)(x.y)` (root `CastExpr`, operand
  `x.y`) — asserted in `Precedence_CastOperandIncludesMemberAccess_Shape`. The task's `((int)x).y`
  is not representable by this grammar anyway (a cast is a prefix alternative, not a `Primary`, so a
  postfix cannot hang off it without explicit parens — `((int)x).y` parses via the outer `Parens`).
- **Tie-break depends on `PrimaryExpr` being first.** The one genuine tie (cast operand is a unary
  prefix that also reads as a binary continuation, e.g. `(x) + 1` / `(x) - 1`) is resolved by the
  engine's "first prefix wins a true tie" rule (Parser.cs:299-316), which requires
  `PrimaryExpr = Primary PostfixOp*` to be the first alternative of `Expression` (it is). This matches
  Roslyn (`CanFollowCast(Plus/Minus)` = false → parens). If `PrimaryExpr` were ever reordered after
  `CastExpr`, `(x) + 1` would mis-parse as a cast.
- **`(x) ++y` (and `(x) --y`) parse as a cast here but as parens+error in Roslyn.**
  `CanFollowCast(PlusPlus/MinusMinus)` = false (LanguageParser.cs:13230-13231), so Roslyn rejects the
  cast; our longest-match accepts `(x)(++y)`. Both are "wrong" for this not-valid-C# input (two
  expressions in a row), and it is not in the required test set — noted for completeness only.
- **New tightest precedence level `Cast`** is added before `Unary`. It only adds a new highest binding
  power; every existing level keeps its relative order and numeric binding power (bp = Count − index),
  so no existing rule changes behaviour (verified: all 335 pre-existing CSharpGrammarTests still pass).
- **C# 1.0 scope**: cast of any C# 1.0 type (predefined / qualified / array / pointer). Generic casts
  (`(Foo<int>) x`, CS2) and `dynamic` (CS4) are rejected (see `Invalid_CastGeneric_Fails`).

## Tests written
All in `Tests/CSharpGrammarTests/`, parsed via `Cs1ExpressionTestHelper` (start rule `"Expression"`).
25 new tests, all green.

- **POSITIVE: 21**
  - `Cs1ExpressionTests.cs` — 17:
    - cast: `(int) x`, `(int[]) x`, `(int*) x`, `(int[]*) x`, `(System.String) s`, `((int) x)`,
      `(int) x.y`, `(int) ++x` (8).
    - parens (must NOT become cast): `(x) + 1`, `(x) - 1`, `(x)`, `(a.b)`, `(1 + 2)` (5).
    - shape (precedence): `(int) x + y` → root `Add`/child `CastExpr`; `(int) x * y` → root `Mul`/
      child `CastExpr`; `(int) x.y` → root `CastExpr`, operand `x.y`; `(int) ++x` → root `CastExpr`,
      operand `++x` (4).
  - `Cs1RoslynExpressionTests.cs` — 4 (Roslyn-derived, see below).
- **NEGATIVE: 4** (`Cs1ExpressionTests.cs`): `(int)` (no operand), `(int` (unclosed type),
  `(int x` (unclosed paren), `(Foo<int>) x` (generic cast, CS2).

Roslyn-derived cases (source file:method + adaptation documented per test):
- `ExpressionParsingTests.TestCast` → `(a) b` (cast of type `a`, expr `b`).
- `ForStatementParsingTest.TestVariousExpressions_Cast` → `(int)0` (for-init → bare Expression).
- `ExpressionParsingTests.TestParenthesizedExpression` → `(goo)` (parens, not cast).
- `ForStatementParsingTest.TestVariousExpressions_Parenthesized` → `(a)` (parens, not cast).

## Verification
- `dotnet build Nitra.sln --no-incremental` → **Build succeeded, 0 Warning(s), 0 Error(s)**.
- `dotnet test Tests/CSharpGrammarTests` (fresh build) → **Passed: 360, Failed: 0, Skipped: 3**
  (3 skips are pre-existing `RawString`/`StringLiteral` tests, unrelated to T2.3.2).
  - New T2.3.2 tests: 25 (`Cs1ExpressionTests` +17, `Cs1RoslynExpressionTests` +4), all green.
  - Pre-existing 335 CSharpGrammarTests still pass (precedence-level addition is behaviour-preserving).
- Engine (`ExtensibleParser/`) and CsNitra meta-grammar **NOT changed** (only the C# grammar text +
  tests) → `ParserTests` not re-run (per task: only required when engine/meta-grammar changes).

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs1.grammar` (precedence: +`Cast` at top; Expression: +`CastExpr`
  prefix alternative `"(" Type ")" Expression : Cast`)
- `Tests/CSharpGrammarTests/Cs1ExpressionTestHelper.cs` (+`ParseExpression` for tree-shape asserts)
- `Tests/CSharpGrammarTests/Cs1ExpressionTests.cs` (+17 tests: cast/parens/shape/negative)
- `Tests/CSharpGrammarTests/Cs1RoslynExpressionTests.cs` (+4 Roslyn-derived cast tests)
- `docs/CSharpParserPlan-progressT2.3.2.md` (this file)
- `docs/CSharpParserPlan-checklist.md` (T2.3.2 → `[✅]`)
