# T3.8.1 — Cs8.grammar setup + C# 8.0 switch expressions

Status: DONE — build green, all tests pass (CSharpGrammarTests 1218/0/3, ParserTests 325/0/2).

## Goal
1. Set up `Cs8.grammar` (version 8): create the file, add the `<EmbeddedResource>` to the csproj,
   and add `new(8, "Cs8.grammar", "Cs8.grammar")` to `EmbeddedGrammar._versions`.
2. Add C# 8.0 **switch expressions** to the new `Cs8.grammar`:
   - `var x = y switch { 0 => "zero", 1 => "one", _ => "other" };` (a `switch` + `{` + arms + `}`
     as an EXPRESSION).
   - Each arm is `Pattern "=>" Expression` (the T3.6.2 `Pattern` rule + the `=>` arrow + an
     expression).
   - CS8 only. The version-purity boundary is v7 rejects / v8 accepts.

## Part A — Cs8.grammar setup (version table)
- Create `Parsers/CSharp/CSharpGrammar/Cs8.grammar` (CRLF, no BOM) with a header comment in the
  same format as `Cs7.grammar`.
- Add `<EmbeddedResource Include="Cs8.grammar" />` to `CSharpGrammar.csproj` (between `Cs7.grammar`
  and `Cs11.grammar`).
- Add `new(8, "Cs8.grammar", "Cs8.grammar")` to `EmbeddedGrammar._versions` (between the version 7
  and version 11 entries).

## Cs1 structure found (Parsers/CSharp/CSharpGrammar/Cs1.grammar)
- **`Expression`** (Cs1:633-673) is a TDOPP rule. Prefixes: `PrimaryExpr = Primary PostfixOp*`,
  `UnaryPlus/Minus/Not/BitNot`, `PreInc/PreDec`, `CastExpr = "(" Type ")" Expression : Cast`.
  Postfixes (self-Ref alternatives): `Mul/Div/Mod` (Multiplicative), `Add/Sub` (Additive),
  `Less/Greater/LessEq/GreaterEq/TypeIs/TypeAs` (Relational), `Equal/NotEqual` (Equality),
  `BitAnd` (LogicalAnd), `BitXor` (LogicalXor), `BitOr` (LogicalOr), `And` (CondAnd), `Or` (CondOr),
  `Conditional` (Conditional), `Assign` (Assignment, right), `Comma` (Comma).
- **`Primary`** (Cs1:675-728): `true/false/null/this`, `IdentifierName = !ReservedKeyword Identifier`,
  `PredefinedMember`, `BaseMember`, String/Verbatim/Char literals, DecInt/HexInt/Real,
  `Parens = "(" Expression ")"`, `NewExpr`, `SizeOf`, `TypeOf`, `DefaultTyped`, `CheckedExpr`,
  `UncheckedExpr`. `Primary` is NOT a TDOPP rule (no self-Ref alternatives, no precedence list).
- **Switch STATEMENT** (Cs1:893): `SwitchStatement = "switch" "(" Expression ")" "{" SwitchSection* "}"`.
  `SwitchSection = SwitchLabel+ Statement*` (Cs1:900). `SwitchLabel = CaseLabel | DefaultLabel`
  (Cs1:904). `CaseLabel = "case" Constant ":"` (Cs1:909). `DefaultLabel = "default" ":"` (Cs1:912).
- **`switch` is a RESERVED keyword** (Cs1 ReservedKeyword:603). So it is not an IdentifierName and no
  other Expression/Primary alternative starts with it.
- **`Pattern`** (Cs7.grammar:116-121, T3.6.2): `DiscardPattern = "_"`,
  `DeclarationPattern = Type !ReservedKeyword Identifier`, `VarPattern = "var" !ReservedKeyword
  Identifier`, `TypePattern = Type`, `ConstantPattern = Expression`.
- **`=>`** is a plain `"=>"` literal (used in Cs6 `ExpressionBody`, Cs3 `LambdaExpression`).
- **Precedence list** (Cs1:623-626, merged with the T1.4 Type list at Cs1:631):
  `Cast, Unary, Multiplicative, Additive, Relational, Equality, LogicalAnd, LogicalXor, LogicalOr,
  CondAnd, CondOr, Conditional, Assignment, [TypeArray, TypePointer], Comma`. Binding power
  `bp = count - index` (CsNitraTypeChecker.ResolvePrecedenceDependencies:56), so `Cast`=16 (tightest),
  `Unary`=15, `Multiplicative`=14, …, `Comma`=1 (loosest).

## Roslyn references (C:\RSDN\roslyn, main)
- **Structure** — `ParseSwitchExpression` (Parser/LanguageParser_Patterns.cs:593-658):
  `SwitchExpression(governingExpression, switchKeyword, OpenBrace, arms, CloseBrace)` where
  `arms = SeparatedSyntaxList<SwitchExpressionArmSyntax>` (comma-separated) and each arm is
  `SwitchExpressionArm(pattern, whenClause, arrowToken(=>), expression)` (629-637). The arm pattern is
  `ParsePattern(Precedence.Coalescing)` and the arm value is `ParseExpressionCore()`.
- **Operator recognition** — `GetExpressionOperatorTokenKindAndExpressionKind`
  (Parser/LanguageParser.cs:11782): `switch` + Peek(1)==`{` → `SyntaxKind.SwitchExpression`. The
  switch expression is formed in the OPERATOR loop (`ParseExpressionContinued.tryExpandExpression`,
  11557-11612), i.e. it is a POSTFIX-like operator on the governing expression.
- **Precedence** — `GetPrecedence` (LanguageParser.cs:11271-11273): `SwitchExpression` →
  `Precedence.Switch`. The `Precedence` enum (11197-11221) orders (loosest→tightest): …, Additive,
  Multiplicative, **Switch**, Range, Unary, Cast, …, Primary. So the switch expression binds TIGHTER
  than every binary operator (Multiplicative and looser) but LOOSER than Unary/Cast. The governing
  expression is parsed at the `Switch` precedence, so it is a "Unary-tighter" expression (Primary,
  Cast, Unary) — NOT a full binary expression.
- **Version gate** — `IDS_FeatureSwitchExpression` (Errors/MessageID.cs:191, :631-634) →
  `LanguageVersion.CSharp8`. A BINDER (semantic) check (Binder_Statements.cs:396), so the PARSER
  accepts the form wherever the rule exists (CS8 only, per the plan's decision).
- **Syntax tests** — `src/Compilers/CSharp/Test/Syntax/Parsing/PatternParsingTests.cs`:
  `SwitchExpression01` (2476, `1 switch {a => b, c => d}`), `DiscardInSwitchExpression` (5457,
  `e switch { _ => 1 }`), `ChainedSwitchExpression_01` (7443, `1 switch { 1 => 2 } switch { 2 => 3 }`),
  `ChainedSwitchExpression_02` (7496, `a < b switch { 1 => 2 } < c switch { 2 => 3 }`).

## Approach (the switch expression rule)
The switch expression is modeled as a **TDOPP POSTFIX operator on `Expression`** (NOT a `Primary`
alternative — see Deviation D1). It is re-declared on `Expression` (append, T0.3 merge):

```
Expression =
    | SwitchExpression = Expression : Unary "switch" "{" SwitchArm ("," SwitchArm)* "}";

SwitchArm = Pattern "=>" Expression : Comma;
```

- **Why an `Expression` postfix, not a `Primary`**: the engine (Parser.BuildTdoppRulesInternal,
  Parser.cs:123-162) turns every alternative whose FIRST element is a self-Ref into a POSTFIX
  operator. A `Primary` self-Ref alternative (`SwitchExpression = Primary "switch" ...`) with a plain
  `Primary` operand gets precedence 0 (Parser.cs:144-145: no `ReqRef` in the rest and the first
  element is a plain `Ref`), and a postfix applies iff `precedence > minPrecedence`
  (Parser.cs:360) — `0 > 0` is false, so it would NEVER apply. Giving it a positive precedence
  requires a `ReqRef` operand, but `Primary` has no precedence list, so a `ReqRef` on `Primary` cannot
  be resolved (RuleGenerator.cs:90-95 needs a `PrecedenceSymbol`). `Expression` IS a TDOPP rule with a
  precedence list, so `Expression : Unary` resolves to a real binding power.
- **The precedence is `Unary` (bp 15)**: the switch expression must bind TIGHTER than every binary
  operator (Multiplicative bp 14 and looser) so that `x switch {...} + y` = `(x switch {...}) + y` and
  `x + y switch {...}` = `x + (y switch {...})`. `Unary` is the nearest existing level tighter than
  Multiplicative (Roslyn's `Switch` level sits between Multiplicative and Unary; see Deviation D2).
  No existing POSTFIX uses the Unary level (the unary operators are PREFIXes), so there is no
  equal-precedence tie.
- **The operand is a PREFIX** (a primary-ish expression): the postfix's `rest` is `"switch" "{" ... "}"`
  (Parser.cs:141), so the operand is the base expression matched before the postfix (a Primary, a unary
  expression, or a cast) — matching Roslyn's "governing expression parsed at the Switch precedence".
- **The arm value is `Expression : Comma`** (minPrecedence = Comma bp 1): it stops at the `,` arm
  separator instead of absorbing it via the Comma operator (the TDOPP Comma fix, T3.3.2 — the same
  mechanism Cs4 `Argument` uses). `SwitchArm` is a single-sequence rule ending in a precedence-qualified
  `ReqRef`, which the meta-grammar accepts (a `SimpleRule`, not an anonymous alternative, and not in a
  group — the two T3.3.2 constraints).

## Root cause + fix (the 8 failing positives)

The switch expression rule itself is correct. All 8 failing positives failed because of a bug in the
**`Pattern` rule** (T3.6.2, `Cs7.grammar`): its `ConstantPattern = Expression` alternative is a full
TDOPP `Expression`, which **includes lambda expressions** (the Cs3 `LambdaExpression` Primary:
`LambdaParameters "=>" LambdaBody`, where `LambdaParameters` is a simple `!ReservedKeyword Identifier`
OR a parenthesized `(LambdaParameter; ",")*` list).

In a switch arm `SwitchArm = Pattern "=>" Expression : Comma`, the `Pattern` is parsed first. For an
arm like `_ => "other"`, the `Pattern` union is longest-match-wins, and the `ConstantPattern` matched
the WHOLE arm `_ => "other"` **as a lambda** (the discard `_` is an `Identifier` — the terminal is
`[_\l]\w*` — so it is a valid `SimpleLambdaParameter`; the `=>` is the lambda arrow; `"other"` is the
body), consuming the arm's `=>`. The arm then could not find its `"=>"` literal and failed. Diagnostic
(`Parser.MemoizationVisualazer`): `[51..58) 7 Pattern «_ => 2 »  Kind: PrimaryExpr  Rule: Pattern`
(the `Pattern` matched all 7 chars of the arm as a lambda, not just the 1-char `_`).

The same happened for a bare-identifier arm (`a => b` in the Roslyn `SwitchExpression01` case): the
`ConstantPattern` matched `a => b` as a lambda. Literal arms (`1 => 2`) were unaffected (a literal is
not a `SimpleLambdaParameter`), which is why the chained test (`1 switch { 1 => 2 } switch { 2 => 3 }`)
already passed.

**The fix** (in the `Pattern` rule, `Cs7.grammar` — the rule where `ConstantPattern` is defined): add
two negative lookaheads to the `ConstantPattern` that exclude the two lambda forms, so the
`ConstantPattern` matches only a real constant (literal / identifier / member access / `null` /
`true`/`false`):

```
ConstantPattern = !(!ReservedKeyword Identifier "=>") !("(" (LambdaParameter; ",")* ")" "=>") Expression;
```

- `!(!ReservedKeyword Identifier "=>")` — rejects a simple (unparenthesized) lambda start: a
  non-reserved identifier immediately followed by `=>`. A bare identifier constant NOT followed by
  `=>` (e.g. `case a:`) still matches (the lookahead passes).
- `!("(" (LambdaParameter; ",")* ")" "=>")` — rejects a parenthesized lambda start: a parameter list
  immediately followed by `=>`. It reuses the Cs3 `LambdaParameter` rule (available in the merged
  Cs1+…+Cs7 grammar) so it precisely mirrors `LambdaParameterList`.

**Why it works (mutual exclusivity, per arm):**
- `_ => "other"` (v8): `DiscardPattern` matches `_` (1 char). `ConstantPattern` is rejected by the
  first lookahead (`_` is a non-reserved `Identifier` followed by `=>`). `TypePattern` matches `_`
  (a `QualifiedName`, 1 char). The `DiscardPattern` (first) wins the equal-length tie → the pattern is
  `_` (1 char), the arm's `=>` is then consumed, and the value `"other"` parses. The arm succeeds.
- `a => b` (v8): `DiscardPattern`/`VarPattern`/`DeclarationPattern` fail. `ConstantPattern` is
  rejected by the first lookahead (`a` is a non-reserved `Identifier` followed by `=>`). `TypePattern`
  matches `a` (a `QualifiedName`, the first equal-length alternative) → the pattern is `a`, the arm's
  `=>` is consumed, and the value `b` parses. (A bare-identifier arm resolves to the `TypePattern`
  rather than the `ConstantPattern` — a documented semantic difference; the syntax still parses. This
  matches the pre-existing behavior for identifier patterns, e.g. `case a:`.)
- `int i => "int"` (v8): `DeclarationPattern` (`Type`=`int`, `Identifier`=`i`) matches `int i` (the
  `ConstantPattern` fails on the reserved `int`, so no interference). Unchanged.
- `0 => "zero"` (v8): `0` is a literal (not an `Identifier`, not `(`) → both lookaheads pass;
  `ConstantPattern` matches `0`. Unchanged.

**No regression to T3.6.2 patterns:** the `is` pattern (`TypeIsPattern = … "is" Pattern`) and the
switch-STATEMENT pattern (`PatternCaseLabel = "case" Pattern … ":"`) are never followed by `=>` (they
are followed by `)`/`;`/`when`/`:`), so both lookaheads pass and the `ConstantPattern` behavior is
identical to before. Verified: all 36 `Cs7Pattern*` tests green.

## Mutual-exclusivity hand-traces
The switch expression is a POSTFIX operator; it applies only when the next token after the operand is
the reserved `switch` (followed by `{`). No other postfix operator starts with `switch`, and `switch`
is reserved (not an IdentifierName), so it is MUTUALLY EXCLUSIVE with every existing postfix.
- `x switch { 0 => "zero", _ => "other" }` (v8): prefix `x` (PrimaryExpr). Postfix loop: the
  SwitchExpression postfix (bp 15) is applicable (15 > 0) and matches `switch { 0 => "zero", _ =>
  "other" }` → `x switch { ... }`. The other postfixes fail (the next token after `x` is `switch`, not
  `+`/`-`/…). Longest-match → the switch expression. SOLE match for the full form.
- `x` (no `switch`): the SwitchExpression postfix fails (no `switch` after `x`); the prefix `x` stands
  alone. No tie.
- `x switch {...} + y` (v8): postfix loop iteration 1 → the SwitchExpression postfix gives `x switch
  {...}` (longer than any binary postfix that fails on `switch`); iteration 2 → the `Add` postfix
  (bp 13) applies to the result → `(x switch {...}) + y`. (Switch is tighter than `+`.)
- `x + y switch {...}` (v8): postfix loop → the `Add` postfix (bp 13) is applicable after `x`; its RHS
  is an `Expression` at minPrecedence 13, and the SwitchExpression postfix (bp 15 > 13) applies to `y`
  → `x + (y switch {...})`. (The governing expression is `y`, not `x + y` — matches Roslyn.)
- `1 switch { 1 => 2 } switch { 2 => 3 }` (v8, chained): the inner `1 switch { 1 => 2 }` is itself a
  switch expression (a postfix on the prefix `1`); the outer SwitchExpression postfix applies to that
  result → the chained form parses (Roslyn `ChainedSwitchExpression_01`).
- Switch STATEMENT `switch (x) { case 0: break; }` (v1-v7): a `Statement` (SwitchStatement, Cs1:893),
  a DIFFERENT rule from the switch EXPRESSION (an `Expression` postfix). The statement starts with
  `switch` directly (no preceding operand); the expression requires an operand before `switch`. No
  conflict, no regression.

## Version-purity results
- `class C { string M(int x) { return x switch { 0 => "zero", _ => "other" }; } }`:
  - **v7**: REJECT. The SwitchExpression postfix is absent (Cs8-only); after the prefix `x`, no postfix
    matches `switch` (it is reserved, not a binary operator) → the expression is just `x`, leaving
    `switch {...}` unconsumed → parse failure.
  - **v8**: ACCEPT. The SwitchExpression postfix is present and matches.
- Switch STATEMENT `class C { void M(int x) { switch (x) { case 0: break; default: break; } } }`:
  - **v1-v7**: still ACCEPT (the Cs1 SwitchStatement is untouched — no regression).

## Tests
`Tests/CSharpGrammarTests/Cs8SwitchExpressionTests.cs` (CRLF + UTF-8 BOM). Positives use
`CreateParser(8)`; version-purity negatives use `CreateParser(7)`.
- POSITIVE (v8): basic switch expression; multiple arms; type patterns; declaration patterns; in return.
- POSITIVE (v1-v7, no-regression): the switch STATEMENT.
- NEGATIVE (v7, version-purity): the switch expression.
- NEGATIVE (v8, malformed): a missing arm expression.
- A handful of Roslyn-derived cases (documented below in the test file).

## Verification (all green)
- [x] `dotnet build Nitra.sln --no-incremental` → 0 errors, 0 warnings.
- [x] `dotnet test Tests/CSharpGrammarTests` (fresh build) → **1218 passed, 0 failed, 3 skipped**
      (the 8 previously-failing switch-expression positives are now green; the 3 skipped are
      pre-existing `RawStringLiteral`/`StringLiteral` skips unrelated to this work).
- [x] `dotnet test Tests/ParserTests` (regression) → **325 passed, 0 failed, 2 skipped**.
- [x] `Cs8SwitchExpressionTests` → 13/13 (5 positive v8 + 2 switch-statement no-regression + 1
      Roslyn discard + 1 Roslyn chained + 1 Roslyn constant + 1 Roslyn method-call value + 1 v7
      version-purity negative + 1 v8 malformed negative).
- [x] `Cs7Pattern*` (T3.6.2/T3.7.4, no regression) → 36/36.

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs8.grammar` (new).
- `Parsers/CSharp/CSharpGrammar/CSharpGrammar.csproj` (add `<EmbeddedResource Include="Cs8.grammar" />`).
- `Tests/CSharpGrammarTests/EmbeddedGrammar.cs` (add version 8 to `_versions`).
- `Tests/CSharpGrammarTests/Cs8SwitchExpressionTests.cs` (new, 13 tests).
- `Parsers/CSharp/CSharpGrammar/Cs7.grammar` (MODIFIED — bug fix: two negative lookaheads on
  `ConstantPattern` in the `Pattern` rule to exclude lambda expressions; see Root cause + fix).
- `Tests/CSharpGrammarTests/CSharpVersionInfrastructureTests.cs` (MODIFIED — the version table now has
  9 files (Cs1–Cs8, Cs11); updated `LoadGrammarUpTo_11` to expect 9 (Cs8 inserted before Cs11) and
  added `LoadGrammarUpTo_8`; see Deviation D4).
- `docs/CSharpParserPlan-progressT3.8.1.md` (this file).

## Deviations / boundary decisions
- **D1 — `Expression` postfix, not a `Primary` alternative.** The task suggested a `Primary`
  alternative, but a `Primary` self-Ref alternative gets postfix precedence 0 (never applicable) and
  cannot take a `ReqRef` operand (no `Primary` precedence list). The `Expression` postfix is the
  correct, engine-supported form (same shape as the Cs7 `TypeIsPattern` postfix).
- **D2 — Precedence `Unary` (bp 15), not a new `Switch` level.** Roslyn's `Switch` level sits between
  Multiplicative and Unary. Adding a new level to the shared Cs1 precedence list would re-number every
  level's binding power (risky for v1-v7). `Unary` is the nearest existing level tighter than every
  binary operator and gives the correct grouping for all task cases; no existing postfix uses the Unary
  level, so no tie.
- **D3 — The governing expression is a PREFIX (primary-ish), not a full binary expression.** The
  postfix operand is the base expression (Primary/unary/cast), matching Roslyn's "governing expression
  parsed at the Switch precedence". A full binary operand (`x + 1 switch {...}`) is NOT supported as a
  governing expression (it groups as `x + (1 switch {...})`, exactly as Roslyn does).
- **D4 — A Cs7 change to fix a Cs8 bug (the `ConstantPattern` was too greedy).** The `Pattern` rule
  lives in `Cs7.grammar` (T3.6.2), so fixing the switch-expression arms required MODIFYING a Cs7 rule
  (adding two negative lookaheads to `ConstantPattern`). This is a bug fix, not a new feature: the
  `ConstantPattern = Expression` was too greedy (it matched lambdas). The fix is version-neutral — at
  every version the `ConstantPattern` now excludes the two lambda forms; the T3.6.2 `is`/switch-
  statement patterns are never followed by `=>`, so their behavior is unchanged (verified: 36/36
  `Cs7Pattern*` green). This is the cleanest fix (the task's recommended approach); the alternative
  (restructuring `SwitchArm`) was not needed.
- **D5 — Version-table test updated for the new Cs8 file.** Adding `Cs8.grammar` to
  `EmbeddedGrammar._versions` raised the file count from 8 to 9 (Cs1–Cs8, Cs11). The pre-existing
  `CSharpVersionInfrastructureTests.LoadGrammarUpTo_11_…` asserted the old count (8) and the old order
  (Cs11 at index 7); it now expects 9 with `Cs8.grammar` at index 7 and `Cs11.grammar` at index 8. A
  `LoadGrammarUpTo_8` test was added (mirroring the existing per-version tests). This is a direct,
  necessary consequence of the Cs8 setup (the previous subagent added the version but did not update
  this test, so it was failing before the `ConstantPattern` fix was applied).
