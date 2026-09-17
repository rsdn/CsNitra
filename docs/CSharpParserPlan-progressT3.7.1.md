# T3.7.1 — C# 7.1 `default` literal

Status: done (with Deviations D1–D3 — see the Deviations section).

## Goal
Add the C# 7.1 **`default` literal** to the EXISTING `Parsers/CSharp/CSharpGrammar/Cs7.grammar` (which
already has the CS7.0 rules from T3.6.x and holds CS7.1/7.2 features per the plan's decision — version 7):
- **`default` literal**: `int x = default;` (the `default` keyword as a value of any type, no `(...)`, no `:`).

CS7.1 (per the plan's decision, in Cs7.grammar = version 7; the version-purity boundary is v6 rejects /
v7 accepts). `CreateParser(6)` must REJECT `int x = default;`; `CreateParser(7)` must accept it. The
`default(T)` form and the `default:` switch label must still parse at v1–v6 (no regression).

## Baseline (before changes)
- `dotnet build Nitra.sln --no-incremental` → success, 0 errors / 0 warnings.
- `dotnet test Tests/CSharpGrammarTests` → **1158 total / 1155 passed / 0 failed / 3 skipped** (matches T3.6.5).
- `dotnet test Tests/ParserTests` → **327 total / 325 passed / 0 failed / 2 skipped** (matches T3.6.5).

## Cs1 structure found
- **`Primary`** (Cs1.grammar:675-720): the expression primaries (`true`/`false`/`null`/`this`,
  `IdentifierName = !ReservedKeyword Identifier`, `PredefinedMember`, `BaseMember`, string/char/numeric
  literals, `Parens`, `NewExpr`, `SizeOf`, `TypeOf`, `CheckedExpr`, `UncheckedExpr`). **There was NO
  `default` form here** (verified empirically: `return default(int);` REJECTS at v1 before the change).
- **`ReservedKeyword`** (Cs1.grammar:537-618): `default` IS reserved (line 553). So a bare `default` is
  NOT an `IdentifierName` and NO `Primary` alternative starts with it → a new `default`-started Primary
  is the sole match for a `default`-started expression (no equal-length tie).
- **`DefaultLabel`** (Cs1.grammar:904): `"default" ":"` — the `default:` switch label. It is a
  **SwitchLabel** (`SwitchLabel = CaseLabel | DefaultLabel`, Cs1.grammar:896-898), reached only in a
  switch-section context — NOT an expression. Untouched by the new Primary alternatives.
- **`GotoDefault`** (Cs1.grammar:830): `"goto" "default" ";"` — the `goto default;` statement. A
  **Statement** alternative, not an expression. Untouched.
- **`default(T)` form — ABSENT (false premise).** The task assumed a `default ( Type )` form existed in
  Cs1 (in `Expression`/`Primary`). It does NOT. `default` appears in Cs1 only in `ReservedKeyword`
  (553), `GotoDefault` (830), and `DefaultLabel` (904). `return default(int);` REJECTS at v1 (probe).

## Roslyn references (C:\RSDN\roslyn, main, src/Compilers/CSharp)
- **Both forms, one method** — `ParseDefaultExpression` (Parser/LanguageParser.cs:12633):
  ```csharp
  var keyword = this.EatToken();
  if (this.CurrentToken.Kind == SyntaxKind.OpenParenToken)
      return _syntaxFactory.DefaultExpression(keyword, this.EatToken(OpenParenToken), this.ParseType(), this.EatToken(CloseParenToken));
  else
      return _syntaxFactory.LiteralExpression(SyntaxKind.DefaultLiteralExpression, keyword);
  ```
  The typed form `default ( Type )` (C# 1.0) is the `(` branch; the literal `default` (C# 7.1) is the
  else-branch (`DefaultLiteralExpression`).
- **`default` is a PRIMARY expression** — `ParseDefaultExpression` is entered from
  `parsePrimaryExpressionWithoutPostfix` (LanguageParser.cs:11958-11959) on a `DefaultKeyword` token.
  So both forms are Primary alternatives (this grammar: added to `Primary`).
- **Version gate is a BINDER (semantic) check** — `IDS_FeatureDefaultLiteral` (MessageID.cs:142), marked
  `// semantic check` (MessageID.cs:665); applied at `Binder_Expressions.cs:753`
  (`case SyntaxKind.DefaultLiteralExpression: IDS_FeatureDefaultLiteral.CheckFeatureAvailability(...)`).
  **C# 7.1**. The PARSER accepts the literal form wherever the rule exists; the version gate is a binder
  concern. The typed `DefaultExpression` binds via `BindDefaultExpression` (Binder_Expressions.cs:769) —
  C# 1.0, no version gate.
- **Syntax tests** — `TestTargetTypedDefaultWithCSharp7_1` (Test/Syntax/Parsing/ExpressionParsingTests.cs:4769):
  `default` parses as a `DefaultLiteralExpression` at `LanguageVersion.CSharp7_1`. Typed forms:
  `ForStatementParsingTest.cs:2885` (`for (default(int);default(int);default(int));`) and `:2937`
  (`for (default;default;default);`); `AwaitParsingTests.cs:638` (`async () => await default(Task);`).

## Approach
1. **`default` literal (C# 7.1) → Cs7.** Re-declare Cs1 `Primary` (append, T0.3 merge) to add
   `DefaultLiteral = "default"` (the bare keyword, no `(`, no `:`). The REQUIRED-new-construct is the
   bare `default` keyword (reserved → mutually exclusive with every other Primary).
2. **typed default `default ( Type )` (C# 1.0) → Cs1.** Add `DefaultTyped = "default" "(" Type ")"` to
   Cs1 `Primary`. Added in this task because the test list requires `default(int)` to parse at v1–v6
   (it did not parse before — Deviation D1). Roslyn `ParseDefaultExpression` typed branch.

## Mutual-exclusivity hand-traces
All hand-traces verified empirically (probe). The engine picks the LONGEST `Primary` alternative in
isolation (`ParseRule("Primary")`, Parser.cs:258-322), then applies `PostfixOp*` to the remainder — so a
bare `default` literal followed by a `PostfixOp` is NOT considered as a combined match against a longer
`Primary` alternative. Summary:
- **`default` alone** (e.g. `int x = default;`): `DefaultTyped` FAILS (no `(` after `default`);
  `DefaultLiteral` matches `default`. Sole match.
- **`default(int)`** (a typed default, every version): at the Primary level `DefaultTyped` matches
  `default(int)` (12 chars) and `DefaultLiteral` matches `default` (7 chars) → `DefaultTyped` wins
  (longest). The literal branch's `PostfixOp*` Invocation `(int)` would FAIL anyway (`int` is a reserved
  keyword, not an `Expression`). Parsed as a typed default (the correct shape, NOT a call). No tie.
- **`default(x)`** (an identifier): `DefaultTyped` (Type = `x`, a user-defined type name) matches
  `default(x)` (longer than the bare `default`) → wins at the Primary level. No tie. (In real C# this is
  a binder error if `x` is a variable, but a valid PARSE as a typed default.)
- **`default(5)`** (a numeric literal): `DefaultTyped` FAILS (`5` is not a `Type`); `DefaultLiteral`
  matches `default`, then `PostfixOp*` Invocation `(5)` matches (`5` is an `Expression`) → parsed as
  `default` + invocation (a call). A valid PARSE (binder error in real C#). Not in the task's test list
  (Deviation D3).
- **`default:`** (a switch label, every version): a `SwitchSection` context → `DefaultLabel` (a
  SwitchLabel, not a Primary). The Primary rule is not involved. Unchanged (no regression).
- **`goto default;`** (a statement, every version): a `Statement` alternative (`GotoDefault`), not an
  expression. Unchanged.

## Version-purity results
Verified empirically (all green tests + probe):
- **v7 ACCEPTS**: `int x = default;` / `return default;` / `N(default);` / `x = default;` /
  `N(default, default);` (the literal); `default(int);` / `default(T)` / `default(System.String);`
  (the typed form, no regression).
- **v6 REJECTS**: `int x = default;` (the literal — the Cs7 `DefaultLiteral` is absent and no other
  Primary matches the reserved `default` alone).
- **v6 no-regression (still parse)**: `return default(int);` (typed default, via Cs1 `DefaultTyped`) /
  `switch (x) { default: break; }` (switch label, via Cs1 `DefaultLabel`).
- **v7 malformed (reject)**: `int x = default( ;` (missing type / unclosed paren — `DefaultTyped` fails
  on the missing Type; the bare literal leaves a dangling `(` → the declaration fails).
- **v7 false-premise (parses)**: `int x = default ;` (trailing space — trivia is skipped, so it PARSES;
  Deviation D2).

## Tests
`Tests/CSharpGrammarTests/Cs7DefaultLiteralTests.cs` (CRLF + UTF-8 BOM) — **13 tests, all green**.
- POSITIVE (v7, core, 4): `DefaultLiteral_LocalVariable_Succeeds` (`int x = default; return x;`),
  `DefaultLiteral_Return_Succeeds` (`return default;`), `DefaultLiteral_Argument_Succeeds`
  (`N(default);` + `void N(int x)`), `DefaultLiteral_Assignment_Succeeds` (`x = default;`).
- POSITIVE (v6, no-regression, 2): `DefaultTyped_ParsesAtV6` (`return default(int);` — the C# 1.0 typed
  default), `DefaultLabel_ParsesAtV6` (`switch (x) { default: break; }` — the C# 1.0 switch label).
- NEGATIVE (v6, version-purity, 1): `DefaultLiteral_RejectedAtV6` (`int x = default; return x;` — the
  literal is not available at v6).
- NEGATIVE (v7, malformed, 1): `DefaultLiteral_Malformed_UnclosedParen_Rejected` (`int x = default( ;` —
  missing type / unclosed paren).
- DOCUMENT (v7, false premise, 1): `DefaultLiteral_TrailingSpace_Parses` (`int x = default ;` — the
  task's "trailing space" case PARSES because trivia is skipped; Deviation D2).
- POSITIVE (v7, Roslyn-derived, 4): `DefaultTyped_Roslyn_Generic_Succeeds` (`T M<T>() { return default(T); }`,
  AwaitParsingTests.cs:638 adapted), `DefaultTyped_Roslyn_AsArgument_Succeeds` (`N(default(int));`,
  ForStatementParsingTest.cs:2885 adapted), `DefaultLiteral_Roslyn_Multiple_Succeeds` (`N(default, default);`,
  ExpressionParsingTests.cs:4769 adapted), `DefaultTyped_Roslyn_Qualified_Succeeds`
  (`N(default(System.String));`, ForStatementParsingTest.cs:2885 adapted — a QualifiedName Type).

## Verification
- [x] `dotnet build Nitra.sln --no-incremental` → **success, 0 errors / 0 warnings**.
- [x] `dotnet test Tests/CSharpGrammarTests` (fresh build) → **1171 total / 1168 passed / 0 failed /
  3 skipped**. Baseline before T3.7.1 (after T3.6.5): 1158 total / 1155 passed / 3 skipped. Delta =
  **+13** (all new `Cs7DefaultLiteralTests`). All pre-existing Cs1/Cs2/Cs3/Cs4/Cs5/Cs6/Cs11 tests remain
  green (the only Cs1 change is the new `DefaultTyped` Primary alternative, which is the sole match for a
  `default ( ... )` expression and appears in no pre-existing test).
- [x] `dotnet test Tests/ParserTests` (regression; engine + CsNitra meta-grammar unchanged) →
  **327 total / 325 passed / 0 failed / 2 skipped** — matches baseline.

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs7.grammar` (the `DefaultLiteral` Primary alternative, T3.7.1).
- `Parsers/CSharp/CSharpGrammar/Cs1.grammar` (the `DefaultTyped` Primary alternative — Deviation D1).
- `Tests/CSharpGrammarTests/Cs7DefaultLiteralTests.cs` (new).
- `docs/CSharpParserPlan-progressT3.7.1.md` (this file).
- `docs/CSharpParserPlan-checklist.md` (T3.7.1 status).

## Deviations / boundary decisions
- **D1 — the `default ( Type )` form was a FALSE PREMISE; added to Cs1.** The task assumed a `default(T)`
  form already existed in Cs1 (in `Expression`/`Primary`) and listed `return default(int);` as a
  "version 1–6, must stay green" test. Verified empirically: NO such form existed — `default(int)` REJECTS
  at v1 before the change (`default` appears in Cs1 only in `ReservedKeyword`/`GotoDefault`/`DefaultLabel`).
  The task's test list requires `default(int)` to parse at v1–v6, which is impossible without a `default(T)`
  form. Decision: add `DefaultTyped = "default" "(" Type ")"` to Cs1.grammar (the C# 1.0 typed default,
  Roslyn `ParseDefaultExpression` typed branch). Rationale: (a) required by the task's test list; (b) a
  genuine C# 1.0 feature (Roslyn `DefaultExpression`, present since C# 1.0); (c) SAFE — `default` is a
  reserved keyword, so `DefaultTyped` is the SOLE match for a `default ( ... )` expression (no equal-length
  tie, no regression — it appears in no pre-existing test); (d) makes the v7 disambiguation correct (the
  typed form wins over the bare literal by longest-match, so `default(int)` parses as a typed default, not
  a call). The task's "files to stage" listed Cs7.grammar (not Cs1.grammar) because the author assumed the
  `default(T)` form already existed; adding the missing Cs1 form is a related, in-scope correction required
  by the test list.
- **D2 — the task's "trailing space" malformed case is NOT malformed.** `int x = default ;` PARSES: the
  engine skips trivia after every terminal, so the space between `default` and `;` is ignored and the
  initializer is the bare `default` literal. Documented as a POSITIVE (`DefaultLiteral_TrailingSpace_Parses`);
  the genuine malformed NEGATIVE uses `int x = default( ;` (missing type / unclosed paren), which REJECTS.
- **D3 — `default ( <non-Type> )` parses as a call, not a typed default.** For a non-Type argument (e.g.
  `default(5)` — a numeric literal, or a variable that is an `Expression` but `DefaultTyped` fails because
  it is not a `Type`), `DefaultTyped` FAILS and the `default` literal + a `PostfixOp` Invocation matches
  instead → parsed as `default` + invocation (a call). In real C# this is a binder error (the argument is
  not a type), but a valid PARSE. Not in the task's test list; documented. (For a genuine Type argument —
  predefined / user-defined / qualified / array / generic — `DefaultTyped` wins by longest-match and the
  correct typed-default shape is produced.)
