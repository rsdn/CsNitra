# T3.3.2 — C# 4.0 named arguments

Status: done.

## Goal
Add C# 4.0 named arguments to the EXISTING `Parsers/CSharp/CSharpGrammar/Cs4.grammar`:
- `M(x: 5)` (a single named argument)
- `M(x: 5, y: 6)` (multiple named arguments)
- `M(y: 6, x: 5)` (named arguments in any order)
- `M(1, y: 6)` (positional arguments before named arguments)
- `M(x: a + b)` (complex value)

CS4 only. `CreateParser(3)` (Cs1+Cs2+Cs3, no CS4) must REJECT `M(x: 5)`. `CreateParser(4)` must accept it.

## Baseline (before changes)
- `dotnet build Nitra.sln --no-incremental` → success, 0 errors.
- `dotnet test Tests/CSharpGrammarTests` → **982 total / 979 passed / 0 failed / 3 skipped**.
- The previous subagent's `Argument`/`NamedInvocation` approach was in place but **3 tests failed**:
  `NamedArgument_PositionalBeforeNamed_Succeeds` (`N(1, y: 6)`), `NamedArgument_AnyOrder_Succeeds`
  (`N(y: 6, x: 5)`), `NamedArgument_Multiple_Succeeds` (`N(x: 5, y: 6)`). The single named argument
  (`N(x: 5)`) and the complex-value / method-call-value / Roslyn / version-purity / negative tests passed.

## Cs1 method-call argument-list structure (found)
- **Method calls use `Invocation`**, a NAMED ALTERNATIVE of `PostfixOp` (Cs1.grammar:722-727):
  ```
  PostfixOp =
      | MemberAccess = "." Identifier
      | Indexer      = "[" (Expression; ",")* "]"
      | Invocation   = "(" (Expression; ",")* ")"
      | PostInc      = "++"
      | PostDec      = "--";
  ```
  `N(x: 5)` = Primary(`N`) + PostfixOp(`Invocation`). The argument list is a `SeparatedList` of `Expression`s: `"(" (Expression; ",")* ")"`.
- **`ArgumentList`** (Cs1.grammar:753) is a SEPARATE rule `"(" (Expression; ",")* ")"` used by `ObjectCreation = NewBaseType ArgumentList` (Cs1:737, `new Foo(...)`) and `ConstructorInitializer = ":" ConstructorInitializerKeyword ArgumentList` (Cs1:273, `: base(...)`/`: this(...)`). It is NOT used by method calls.
- `Expression` (Cs1:633-673) is a TDOPP rule; `:` is NOT a TDOPP binary operator (the only `:` is inside `Conditional = Expression "?" Expression ":" Expression`, which requires a leading `?`). So `x: 5` is NOT an Expression — it requires a dedicated NamedArgument rule.
- `Identifier` terminal = `[_\l]\w*` (CSharpTerminals.cs).

## Merge mechanism (verified in engine)
- `Scope.AddSymbol(RuleSymbol)` (Scope.cs:22): re-declaring a rule name APPENDS the new statement(s) to the existing rule's statements. `RuleGenerator.GenerateRules` (RuleGenerator.cs:11-32) flattens ALL statements' alternatives into one alternatives array.
- A NAMED ALTERNATIVE (`| Invocation = ...`) is inlined into its parent rule's alternatives (it is NOT a separate `RuleSymbol`). So `Invocation` CANNOT be re-declared directly; the parent `PostfixOp` must be re-declared to add a new alternative.
- Engine tie handling (`Parser.ParseRule`, Parser.cs:296-316): for equal-length SUCCESS matches the FIRST alternative (declaration order) is kept — the `else if` branches only replace on Partial→Success or ε-recovery. So a positional-only list `(x)` matched by both the Cs1 `Invocation` and the Cs4 alternative resolves to the Cs1 one (no error).

## TDOPP Comma investigation (why the `Expression` absorbs the `,`)
- `Expression` is a TDOPP rule (Cs1:633-673). The `Comma` operator is the LAST entry of the Cs1
  precedence list (`precedence Cast, Unary, …, Assignment, Comma;`, Cs1:623-626), so it has the
  **lowest binding power, 1** (after the T1.4 `TypeArray, TypePointer, Comma` merge the list is 16
  entries and `Comma` is still last → bp 1; `CsNitraTypeChecker.ResolvePrecedenceDependencies`
  computes `bp = count - index`).
- In `ContinueFromPartialPostfix` (Parser.cs:360) a postfix operator applies iff
  `precedence > minPrecedence || (precedence == minPrecedence && right)`. A plain `Expression`
  referenced as a `Ref` has `minPrecedence 0`, so the `Comma` postfix (bp 1) IS applicable
  (`1 > 0`). A top-level `Expression` therefore greedily absorbs a following `,`.
- **How the Cs1 `Invocation` handles it** (verified): `N(1, 2)` is parsed as `Primary(N)` +
  `Invocation("(" (Expression; ",")* ")")`. The first `Expression` = `1` absorbs the `,` → the Comma
  RHS parses `2`, giving the single comma expression `1, 2`. The `(Expression; ",")*` loop then sees
  `)` (not `,`) and stops. So **`N(1, 2)` parses `1, 2` as ONE comma expression, not two arguments** —
  the two "arguments" are collapsed into one `Comma` node. This is the EXISTING TDOPP comma behavior
  (the same note in T3.2.1 for lambda bodies and `new Foo(1, 2)`). For a positional-only list it does
  NOT cause a parse failure (the whole input is still consumed).
- **Why it becomes a parse failure for named arguments**: in `N(1, y: 6)` the first argument's plain
  `Expression` = `1` absorbs the `,` → the Comma RHS tries to parse `y: 6` as an `Expression`. `y: 6`
  is NOT an Expression (the `:` is not a binary operator), so the Comma RHS parses only `y`, yielding
  `1, y`. The argument list now has one argument `1, y` and the next token is `:` (not `,` or `)`), so
  the `NamedInvocation` fails; the whole parse fails. This is the difference from the positional case:
  the comma RHS (`y: 6`) is not a valid Expression, so absorption cannot complete cleanly.

## The fix — Option A (minPrecedence to exclude the Comma)
Chosen **Option A**: reference the argument value/positional expression as `Expression : Comma` instead
of a plain `Expression`. `Expression : Comma` is a `ReqRef("Expression", Precedence = Comma's binding
power 1)` (the `: <Precedence>` meta-grammar syntax, resolved in `RuleGenerator.GenerateRuleRefExpression`
RuleGenerator.cs:90-94). This raises the inner parse's `minPrecedence` to 1, so the `Comma` postfix is
NOT applicable (`1 > 1` is false, and Comma is left-associative) while every other operator (binding
power ≥ 2) still is. The argument therefore stops at the `,` separator instead of absorbing it. This is
the SAME minPrecedence mechanism Cs1 already uses (`Expression : Relational` in `TypeIs`/`TypeAs`,
`Expression : Cast` in `CastExpr`) — no engine change, no Cs1.grammar change.

Final `Argument` rule (Cs4.grammar):
```
Argument =
    | NamedArgument      = Identifier ":" Expression : Comma
    | PositionalArgument = Expression : Comma;
```
Both alternatives are named (see the two meta-grammar constraints below). The `NamedInvocation`
re-declaration of `PostfixOp` is unchanged: `| NamedInvocation = "(" (Argument; ",")* ")"`.

### Two meta-grammar constraints discovered while applying Option A
1. **The positional alternative must be NAMED.** The meta-grammar's `AnonymousAlternative` is
   `"|" QualifiedIdentifier` (CsNitraParser.cs:68) — it does NOT allow a `RuleRef` with a
   `: <Precedence>` suffix. An anonymous `| Expression : Comma` fails the meta-grammar parse
   (`Expected: "=", ".", "|", ";"`). Only a `NamedAlternative` (`"|" Identifier "=" RuleExpression`,
   CsNitraParser.cs:66) carries a full `RuleExpression` (which includes a `RuleRef` with precedence).
   Hence `PositionalArgument = Expression : Comma` (named), not a bare `| Expression : Comma`.
2. **The `Expression : Comma` must NOT be wrapped in a `(...)` group.** The `TypeCheckerVisitor`
   has no `Visit(GroupExpressionAst)` (TypeCheckerVisitor.cs), so it never recurses into a group and
   never resolves the `PrecedenceSymbol` of a `RuleRef` with precedence inside one. The
   `SymbolReferenceResolver` then leaves `PrecedenceSymbol` null and
   `RuleGenerator.GenerateRuleRefExpression` throws `ArgumentNullException`
   (`node.PrecedenceSymbol.AssertIsNonNull()`, RuleGenerator.cs:90). So the precedence-qualified
   `RuleRef` must appear directly in the sequence (`Identifier ":" Expression : Comma`), not as
   `Identifier ":" (Expression : Comma)`.

### Why this does not break the Cs1 `Invocation`
- The Cs1 `Invocation` (Cs1:725) is UNCHANGED. For a positional-only list (`N(1, 2)`) BOTH the Cs1
  `Invocation` and the Cs4 `NamedInvocation` match at the same length; the engine keeps the FIRST
  (declaration-order) success (Parser.cs:296-316), so the Cs1 `Invocation` still wins and still parses
  `1, 2` as one comma expression — the pre-existing behavior is preserved. The Cs4 `NamedInvocation`
  is the SOLE match only for lists containing a named argument (where the Cs1 `Invocation` fails
  because `x: 5` is not an `Expression`), and the fix makes that sole match parse correctly.
- `Argument` is a NEW (Cs4-only) rule used only by the Cs4 `NamedInvocation`; it does not reference or
  alter any pre-existing rule.

## Approach (summary)
Re-declare `PostfixOp` (append, T0.3 merge) to add `NamedInvocation = "(" (Argument; ",")* ")"`, with
`Argument = NamedArgument | PositionalArgument` where `NamedArgument = Identifier ":" Expression : Comma`
and `PositionalArgument = Expression : Comma`. Within `Argument`, longest-match disambiguates
(`x: 5` → NamedArgument, longer; `x`/`5` → PositionalArgument). The `Expression : Comma` (minPrecedence
1) prevents the argument from absorbing the `,` separator (see TDOPP Comma investigation).

## Mutual-exclusivity hand-traces
- `x: 5` (inside `Argument`): `NamedArgument` → `x` + `:` + `5` (Expression : Comma) = `x: 5` (length 4).
  `PositionalArgument` → `x` (Expression : Comma; `:` is not a binary operator, so it stops) = `x`
  (length 1). Longest-match → **NamedArgument**. No tie.
- `x` (no `:`): `NamedArgument` → `x` then needs `:` but the next token is not `:` → fails.
  `PositionalArgument` → `x`. → **PositionalArgument only**. No tie.
- `5`: `NamedArgument` → `5` is not an Identifier → fails. `PositionalArgument` → `5`. →
  **PositionalArgument only**. No tie.
- `1, y: 6` (first arg of `N(1, y: 6)`): `PositionalArgument` → `1` (Expression : Comma, minPrecedence 1
  → Comma postfix NOT applicable → stops at `,`) = `1`. The `NamedInvocation` separator then consumes the
  `,`, and the next `Argument` = `y: 6` (NamedArgument). → `N(1, y: 6)` parses. (Before the fix the
  first `Expression` absorbed the `,` → parse failure.)
- `x: 5, y: 6` (first arg of `N(x: 5, y: 6)`): `NamedArgument` → `x` + `:` + `5` (Expression : Comma
  stops at `,`) = `x: 5`. Separator `,`, next `Argument` = `y: 6`. → `N(x: 5, y: 6)` parses.

## `:` disambiguation
The `:` after an identifier inside an argument list is unambiguously a named-argument separator. The
`:` is NOT a TDOPP binary operator (the only `:` in `Expression` is inside `Conditional = Expression "?"
Expression ":" Expression`, which requires a leading `?`), so `x: 5` is NOT an `Expression` and cannot be
produced by `PositionalArgument` — it requires `NamedArgument`. Roslyn uses the same signal:
`ParseArgumentExpression` (LanguageParser.cs:12581) checks `CurrentToken is IdentifierToken &&
PeekToken(1) is ColonToken` to build a `NameColon`. Longest-match gives the same result without a
lookahead.

## Roslyn references (C:\RSDN\roslyn, main, src/Compilers/CSharp/Portable/Parser/LanguageParser.cs)
- `ParseArgumentExpression` (12581): `nameColon = (Identifier && Peek(1)==Colon) ? NameColon(...) : null`;
  `expression = ParseSubExpression(Precedence.Expression)`; `Argument(nameColon, refKind, expr)`.
- `ParseArgumentList` (12475): `(` (ParseArgumentExpression,)* `)` (`allowTrailingSeparator: false`,
  `requireOneElement: false`, so `()` is allowed and a trailing `,` is rejected).
- The ORDER (positional before named) is a BINDER concern (CS1737); the PARSER accepts any order.
- Syntax tests: ExpressionParsingTests.cs:1024 (`a(B: b)`), :1186 (TestNewWithNamedArgument),
  :2280 (TestTupleWithTwoNamedArguments).
- CS4 version gate: ParserErrorMessageTests.cs:6140 (NamedArgumentBeforeCSharp4, CS8024 "Feature 'named
  argument' is not available in C# 3").

## Version-purity results
- `class C { void M() { N(x: 5); } }` → **rejects at v3** (no `NamedInvocation` alternative; the Cs1
  `Invocation` fails because `x: 5` is not an `Expression`) / **parses at v4** (the `NamedInvocation`
  is present).
- All pre-existing Cs1/Cs2/Cs3/Cs6/Cs11 tests stay green (no Cs1/Cs2/Cs3 modification).

## Tests
`Tests/CSharpGrammarTests/Cs4NamedArgumentTests.cs` — **11 tests, all green**.
- POSITIVE (v4, 5): `NamedArgument_Single_Succeeds` (`N(x: 5)`), `NamedArgument_Multiple_Succeeds`
  (`N(x: 5, y: 6)`), `NamedArgument_AnyOrder_Succeeds` (`N(y: 6, x: 5)`),
  `NamedArgument_PositionalBeforeNamed_Succeeds` (`N(1, y: 6)`), `NamedArgument_ComplexValue_Succeeds`
  (`N(x: a + b)`).
- POSITIVE (v4, 2): `NamedArgument_ValueIsMethodCall_Succeeds` (`N(x: M2())`),
  `NamedArgument_Call_Roslyn_Succeeds` (`N(B: b)`, ExpressionParsingTests.cs:1024).
- VERSION-PURITY (v3, 1): `NamedArgument_RejectedAtV3` (`N(x: 5)` rejects at v3).
- NEGATIVE (v4, 3): `NamedArgument_MissingColon_Rejected` (`N(x 5)`),
  `NamedArgument_MissingName_Rejected` (`N(: 5)`), `NamedArgument_MissingValue_Rejected` (`N(x:)`).

## Verification
- [x] `dotnet build Nitra.sln --no-incremental` → **success, 0 errors / 0 warnings**.
- [x] `dotnet test Tests/CSharpGrammarTests` (fresh build) → **993 total / 990 passed / 0 failed /
  3 skipped**. Baseline before T3.3.2: 982 total / 979 passed / 3 skipped. Delta = **+11** (all new
  `Cs4NamedArgumentTests`). The 3 previously-failing tests (`NamedArgument_PositionalBeforeNamed_Succeeds`,
  `NamedArgument_AnyOrder_Succeeds`, `NamedArgument_Multiple_Succeeds`) now pass; the single named
  argument (`N(x: 5)`) and every pre-existing Cs1/Cs2/Cs3/Cs6/Cs11 test remain green.
- [x] `dotnet test Tests/ParserTests` (regression; engine + CsNitra meta-grammar unchanged) →
  **327 total / 325 passed / 0 failed / 2 skipped** — matches baseline.

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs4.grammar` (the `Argument` rule: `Expression` → `Expression : Comma`,
  both alternatives named; `NamedInvocation`/`PostfixOp` re-declaration unchanged).
- `Tests/CSharpGrammarTests/Cs4NamedArgumentTests.cs` (new; unchanged by the fix — the 3 failing tests
  now pass as written).
- `docs/CSharpParserPlan-checklist.md` (T3.3.2 `[~]` → `[✅]`).
- `docs/CSharpParserPlan-progressT3.3.2.md` (this file).

## Boundary decisions / deviations
- **`Expression : Comma` (minPrecedence 1), not a lookahead or a restructured loop** (Option A over
  B/C/D): it reuses the engine's existing TDOPP minPrecedence mechanism (the same one Cs1 uses for
  `TypeIs`/`TypeAs`/`CastExpr`), is a one-rule change, and does not touch Cs1.grammar or the engine.
  It excludes ONLY the `Comma` operator (bp 1); every other operator (bp ≥ 2) still applies, so
  complex argument values (`a + b`, `M2()`, `a ? b : c`) parse unchanged.
- **Both `Argument` alternatives are named** (meta-grammar constraint 1): the positional argument is
  `PositionalArgument = Expression : Comma`, not an anonymous `| Expression : Comma` (the meta-grammar's
  `AnonymousAlternative` does not accept a precedence-qualified `RuleRef`).
- **No `(...)` group around `Expression : Comma`** (meta-grammar constraint 2): the `TypeCheckerVisitor`
  does not recurse into groups, so a precedence-qualified `RuleRef` inside a group would leave its
  `PrecedenceSymbol` null and throw in `RuleGenerator`.
- **The Cs1 `Invocation` comma behavior is left as-is**: `N(1, 2)` still parses `1, 2` as one comma
  expression (the Cs1 `Invocation` wins the equal-length tie over the Cs4 `NamedInvocation`). This is
  the pre-existing TDOPP comma behavior (T3.2.1) and is out of scope; the fix only corrects the
  Cs4-only `NamedInvocation` path.
