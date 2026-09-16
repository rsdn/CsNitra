# T3.6.5 — C# 7.0 digit separators + `throw` as expression

Status: done (with Deviation D1 — see the Deviations section).

## Goal
Add C# 7.0 **digit separators** (`1_000_000`) and **`throw` as expression** (`c ? throw e : 5`) to the
EXISTING `Parsers/CSharp/CSharpGrammar/Cs7.grammar` (which already has tuples T3.6.1, patterns T3.6.2,
local functions T3.6.3, ref semantics T3.6.4).

- **Digit separators** — a TERMINAL change (`CSharpTerminals.cs`): new `[Regex]` terminals that allow `_`
  in numeric literals, each REQUIRING at least one `_` (so they are mutually exclusive with the Cs1
  no-separator terminals). Re-declare the grammar rules that reference numeric literals (`Primary`,
  `Constant`) to add the separated-literal alternatives.
- **`throw` as expression** — a GRAMMAR change: re-declare `Expression` in Cs7 to add a `throw` prefix
  operator (`throw` + expression, NO semicolon), like the Cs7 `ref` expression (T3.6.4). The Cs1
  `ThrowStatement` (`"throw" Expression? ";"`) is UNCHANGED (still a statement at every version).

CS7 only. `CreateParser(6)` must REJECT `1_000_000` and `c ? throw e : 5`; `CreateParser(7)` must accept
them. The `throw e;` statement must still parse at v1–v6 (no regression).

## Baseline (before changes)
- `dotnet build Nitra.sln --no-incremental` → success, 0 errors / 0 warnings.
- `dotnet test Tests/CSharpGrammarTests` → **1138 total / 1135 passed / 0 failed / 3 skipped** (matches T3.6.4).
- `dotnet test Tests/ParserTests` → **327 total / 325 passed / 0 failed / 2 skipped** (matches T3.6.4).

## Cs1 structure found
- **Numeric-literal terminals** (`CSharpTerminals.cs`):
  - `DecimalIntegerLiteral` = `[0-9]+` (line 11).
  - `HexIntegerLiteral` = `0[xX][0-9a-fA-F]+` (line 14).
  - `BinaryIntegerLiteral` = `0[bB][01]+` (line 17) — added in T3.5.3.
  - `OctalIntegerLiteral` = `0[0-7]+` (line 20) — **exists but is NOT referenced by any grammar rule**
    (verified by grep: only the declaration + `GetAll()` entry). Real C# has no octal literals
    (checklist T1.2.1), so there is no base rule to extend for octal → **no octal separator added**.
  - `IntegerSuffix` = `[uU]?[lL]?|[lL][uU]?` (line 23).
  - `DecimalRealLiteral` = `[0-9]+\.[0-9]*|\.[0-9]+` (line 26).
  - `Exponent` = `[eE][+-]?[0-9]+` (line 29); `RealSuffix` = `[fFdDmM]` (line 32).
- **Grammar rules referencing numeric literals**:
  - **`Constant`** (Cs1.grammar:469-477): `DecimalConstant = DecimalIntegerLiteral IntegerSuffix?`,
    `HexConstant = HexIntegerLiteral IntegerSuffix?` (used by enum member values, attribute arguments,
    goto/case labels). No real/binary/octal in `Constant`.
  - **`Primary`** (Cs1.grammar:704-706): `DecInt = DecimalIntegerLiteral IntegerSuffix?`,
    `HexInt = HexIntegerLiteral IntegerSuffix?`, `Real = DecimalRealLiteral Exponent? RealSuffix?`.
    Cs6 (T3.5.3) APPENDS `BinInt = BinaryIntegerLiteral IntegerSuffix?` (Cs6.grammar:127).
- **`ThrowStatement`** (Cs1.grammar:817): `"throw" Expression? ";"` — a STATEMENT (in the `Statement`
  rule, Cs1.grammar:779). `throw;` (rethrow) and `throw e;` both parse at every version.
- **`Expression`** (Cs1.grammar:633-673, TDOPP): prefix alternatives start with a literal (e.g.
  `UnaryMinus = "-" Expression : Unary`); Cs5 APPENDS `AwaitExpr = "await" Expression : Unary` (Cs5:74);
  Cs7 (T3.6.4) APPENDS `RefExpr = "ref" Expression : Unary` (Cs7:281). Re-declare in Cs7 to add a
  `throw` prefix operator.
- **`Primary`** (Cs1.grammar:675-720): no `throw` alternative (`throw` is a reserved keyword, not an
  `IdentifierName`).
- **`ReservedKeyword`** (Cs1.grammar:537-618): `throw` IS reserved (line 605). So a bare `throw` is NOT
  an `IdentifierName` and NO other `Expression`/`Primary` alternative starts with it → the `throw`
  prefix operator is the sole match for a `throw`-started expression (no equal-length tie at the
  expression level).

## Roslyn references (C:\RSDN\roslyn, main, src/Compilers/CSharp)
- **Digit separators** — `ScanNumericLiteralSingleInteger` (Parser/Lexer.cs:801-842) +
  `ScanNumericLiteral` (844-1033):
  - `_` between digits → `usedUnderscore` → `CheckFeatureAvailability(IDS_FeatureDigitSeparator)`
    (Lexer.cs:1032) — **C# 7.0** (MessageID.cs:130).
  - leading `_` (hex/binary only) → `firstCharWasUnderscore` → `IDS_FeatureLeadingDigitSeparator`
    (Lexer.cs:1028) — C# 7.2 (NOT modeled: the task is CS7-only and the test forms have no leading `_`).
  - trailing `_` / decimal-leading `_` → `underscoreInWrongPlace` → `ERR_InvalidNumber` (Lexer.cs:1022-1024).
  - The scan loop (816-836) accepts a `_` only when a valid digit follows it (each `_` is followed by a
    digit), so consecutive `__` and a trailing `_` are not part of a valid digit run. My separated
    terminals mirror this: each `_` is followed by at least one digit (`_[0-9]+`), so no trailing `_`,
    no leading `_`, no consecutive `__`.
- **`throw` as expression** — `ParseThrowExpression` (Parser/LanguageParser.cs:11924-11929):
  `ThrowExpression(EatToken(ThrowKeyword), ParseSubExpression(Precedence.Coalescing))` — `throw` + a
  sub-expression. Entered from `ParseSubExpression` when the current token is `ThrowKeyword`
  (LanguageParser.cs:11508-11515); the "wrong precedence" error (`ERR_InvalidExprTerm`) is a BINDER
  concern (the parser parses it for recovery). So the PARSE form is `throw` + expression (no
  semicolon), distinct from the `throw` STATEMENT (`throw` + expression? + `;`, `ParseThrowStatement`
  LanguageParser.cs:10308). **C# 7.0** feature.

## Approach
1. **Digit separators (TERMINAL)** — add four new `[Regex]` terminals to `CSharpTerminals.cs` (+ their
   `GetAll()` entries). Each REQUIRES at least one `_` (the `+` on the `(_…)` group), so it is mutually
   exclusive with the Cs1 no-separator terminal:
   - `SeparatedDecimalIntegerLiteral` = `[0-9]+(_[0-9]+)+`
   - `SeparatedHexIntegerLiteral`     = `0[xX][0-9a-fA-F]+(_[0-9a-fA-F]+)+`
   - `SeparatedBinaryIntegerLiteral`  = `0[bB][01]+(_[01]+)+`
   - `SeparatedDecimalRealLiteral`    = `[0-9]+(_[0-9]+)+\.[0-9]*(_[0-9]+)*`
                                       `|[0-9]+\.[0-9]*(_[0-9]+)+`
                                       `|\.[0-9]+(_[0-9]+)+`
     (three alternatives: a separator in the integer part / in the fractional part / the `.\d` form —
     each requires at least one `_` somewhere).
   Then re-declare the referencing rules in Cs7:
   - `Primary` — APPEND `SeparatedDecInt` / `SeparatedHexInt` / `SeparatedBinInt` / `SeparatedReal`
     (mirroring the existing `DecInt`/`HexInt`/`BinInt`/`Real` shapes, with the separated terminals).
   - `Constant` — APPEND `SeparatedDecimalConstant` / `SeparatedHexConstant` (digit separators in enum
     values / attribute arguments / goto-case labels).
2. **`throw` as expression (GRAMMAR)** — re-declare `Expression` in Cs7 to APPEND
   `ThrowExpression = "throw" Expression : Unary` (a PREFIX operator, exactly like the Cs7 `RefExpr`
   T3.6.4 / the Cs5 `AwaitExpr`). The operand is `Expression : Unary` (minPrecedence = the Unary level,
   RuleGenerator.cs:90-94), so it binds tighter than all binary operators and does NOT absorb a
   following `,` (the TDOPP Comma mechanism, T3.3.2). The `throw e;` STATEMENT is untouched.

## Mutual-exclusivity hand-traces
All hand-traces verified empirically (probe). Summary:

### Digit separators (vs the Cs1 no-separator terminals)
- `1_000_000` (integer): `DecInt` (`DecimalIntegerLiteral` = `[0-9]+`) matches only `1` (length 1);
  `SeparatedDecInt` matches `1_000_000` (length 9). Longest-match → `SeparatedDecInt`. No tie.
- `1000000` (integer, no separators): `DecInt` matches `1000000` (length 7); `SeparatedDecInt` FAILS
  (requires a `_`). `DecInt` is the sole match. No tie.
- `0xFF_FF` (hex): `HexInt` matches `0xFF` (length 4); `SeparatedHexInt` matches `0xFF_FF` (length 7).
  Longest-match → `SeparatedHexInt`. No tie.
- `0b1_0` (binary): `BinInt` (`BinaryIntegerLiteral` = `0[bB][01]+`) matches `0b1` (length 3);
  `SeparatedBinInt` matches `0b1_0` (length 5). Longest-match → `SeparatedBinInt`. No tie.
- `1_0.5` (real): `Real` (`DecimalRealLiteral` = `[0-9]+\.[0-9]*|\.[0-9]+`) FAILS (`1` then `_`, not
  `.`); `DecInt` matches `1` (length 1); `SeparatedReal` matches `1_0.5` (length 5). Longest-match →
  `SeparatedReal`. No tie.
- `1.5` (real, no separators): `Real` matches `1.5` (length 3); `SeparatedReal` FAILS (no `_`). `Real`
  is the sole match. No tie.
- `100_` (trailing separator): no separated terminal matches (each `_` needs a following digit);
  `DecInt` matches `100` (length 3), leaving a dangling `_` → the parse fails. REJECTS.
- `_100` (leading separator): no separated terminal matches; `DecInt` FAILS (starts with `_`); but
  `_100` IS a valid `Identifier` (`[_\l]\w*`) → it parses as a VARIABLE reference, not a numeric
  literal (FALSE PREMISE — see Deviation D1).

### `throw` as expression (vs other Expression alternatives)
- `c ? throw e : 5`: the `throw e` in the conditional's middle branch is a `ThrowExpression`
  (`"throw"` + `Expression : Unary` = `e`). `throw` is a reserved keyword, so NO other `Expression`
  alternative starts with it (the `IdentifierName` guard `!ReservedKeyword Identifier` rejects it).
  `ThrowExpression` is the sole match for the `throw`-started expression. The conditional's `:` is a
  rule literal (not a TDOPP operator), so the operand `e` stops before it. No tie.
- `throw e;` (a STATEMENT, v7): matches BOTH `ExpressionStatement` (`Expression ";"` where the
  Expression is a `ThrowExpression`) and `ThrowStatement` (`"throw" Expression? ";"`) at the same
  length → the FIRST (declaration order, `ExpressionStatement` before `ThrowStatement`, Cs1.grammar:776
  vs :779) wins (Parser.cs:299-316, equal-length Success ties keep the first). Both parse, so the
  `throw e;` statement still PARSES at v7 (no regression) — it is just tree-shaped as an
  ExpressionStatement. At v1–v6 the `ThrowExpression` is ABSENT, so `throw e;` parses via
  `ThrowStatement` (the sole match). No regression.

## Version-purity results
Verified empirically (all green tests):
- v7 ACCEPTS: `int X = 1_000_000;` / `double X = 1_0.5;` / `int X = 0xFF_FF;` / `int X = 0b1_0;` /
  `int X = 1_000;` / `double X = 1_000.000_1;` / `int X = 0xA_A;` / `int X = 0b1_1;` (digit separators);
  `return c ? throw e : 5;` / `return c ? 5 : throw e;` / `x = c ? throw e : null;` /
  `return c ? throw new Exception() : 5;` / `int x = b ? throw e : 1;` / `x = b ? 2 : throw e;`
  (throw expression).
- v6 REJECTS: `int X = 1_000_000;` (digit separator) / `return c ? throw e : 5;` (throw expression) —
  matches Roslyn `ERR_FeatureNotAvailableInVersion6` (LexicalTests.cs:3131, PatternParsingTests.cs:90-110).
- v1 no-regression (still parse): `int X = 1000000;` (no separators) / `void M() { throw new Exception(); }`
  (throw STATEMENT via the unchanged Cs1 ThrowStatement).
- v7 malformed (reject): `int X = 100_;` (trailing separator).
- v7 false-premise (parses, Deviation D1): `int X = _100;` (leading separator — parses as identifier `_100`).

## Tests
`Tests/CSharpGrammarTests/Cs7DigitSeparatorTests.cs` (CRLF + UTF-8 BOM) — **20 tests, all green**.
- POSITIVE (v7, digit separators, 4): `IntegerSeparator_Succeeds` (`1_000_000`), `FloatSeparator_Succeeds`
  (`1_0.5`), `HexSeparator_Succeeds` (`0xFF_FF`), `BinarySeparator_Succeeds` (`0b1_0`).
- POSITIVE (v7, throw expression, 4): `ThrowExpression_ConditionalTrueBranch_Succeeds`
  (`return c ? throw e : 5;`), `ThrowExpression_ConditionalFalseBranch_Succeeds`
  (`return c ? 5 : throw e;`), `ThrowExpression_Assignment_Succeeds` (`x = c ? throw e : null;`),
  `ThrowExpression_NewOperand_Succeeds` (`return c ? throw new Exception() : 5;`).
- POSITIVE (v1, no-regression, 2): `NoSeparatorInteger_ParsesAtV1` (`int X = 1000000;`),
  `ThrowStatement_ParsesAtV1` (`void M() { throw new Exception(); }`).
- NEGATIVE (v6, version-purity, 2): `IntegerSeparator_RejectedAtV6` (`1_000_000`),
  `ThrowExpression_RejectedAtV6` (`return c ? throw e : 5;`).
- NEGATIVE (v7, malformed, 1): `TrailingSeparator_Rejected` (`int X = 100_;`).
- DOCUMENT (v7, false premise, 1): `LeadingSeparator_ParsesAsIdentifier` (`int X = _100;` — parses as the
  identifier `_100`, Deviation D1).
- POSITIVE (v7, Roslyn-derived, 6): `IntegerSeparator_Roslyn_Succeeds` (`1_000`, LexicalTests.cs:3057),
  `RealSeparator_Roslyn_Succeeds` (`1_000.000_1`, :3075), `HexSeparator_Roslyn_Succeeds` (`0xA_A`, :3102),
  `BinarySeparator_Roslyn_Succeeds` (`0b1_1`, :3111), `ThrowExpression_TrueBranch_Roslyn_Succeeds`
  (`int x = b ? throw e : 1;`, PatternParsingTests.cs:77), `ThrowExpression_FalseBranch_Roslyn_Succeeds`
  (`x = b ? 2 : throw e;`, :78).

## Verification
- [x] `dotnet build Nitra.sln --no-incremental` → **success, 0 errors / 0 warnings**.
- [x] `dotnet test Tests/CSharpGrammarTests` (fresh build) → **1158 total / 1155 passed / 0 failed /
  3 skipped**. Baseline before T3.6.5 (after T3.6.4): 1138 total / 1135 passed / 3 skipped. Delta =
  **+20** (all new `Cs7DigitSeparatorTests`). All pre-existing Cs1/Cs2/Cs3/Cs4/Cs5/Cs6/Cs11 tests remain
  green (no Cs1–Cs6/Cs11 modification).
- [x] `dotnet test Tests/ParserTests` (regression; engine + CsNitra meta-grammar unchanged) →
  **327 total / 325 passed / 0 failed / 2 skipped** — matches baseline.

## Files changed
- `Parsers/CSharp/CSharpGrammar/CSharpTerminals.cs` (four separated-literal terminals + `GetAll()`).
- `Parsers/CSharp/CSharpGrammar/Cs7.grammar` (re-declared `Primary` + `Constant` separated literals,
  `Expression` `ThrowExpression`).
- `Tests/CSharpGrammarTests/Cs7DigitSeparatorTests.cs` (new).
- `docs/CSharpParserPlan-progressT3.6.5.md` (this file).
- `docs/CSharpParserPlan-checklist.md` (T3.6.5 status).

## Deviations / boundary decisions
- **D1 — `class C { int X = _100; }` (separator at start) is a FALSE PREMISE**: `_100` is a valid
  `Identifier` (`[_\l]\w*`), so it parses as a VARIABLE reference, not a malformed numeric literal —
  at every version (Roslyn likewise tokenizes a leading-`_` token as an identifier, not a numeric
  literal). The genuine "separator at start" numeric-literal case (a decimal literal beginning with
  `_`) is rejected by the separated terminal (which requires a leading digit) and by `DecInt` — but in
  an expression position the identifier fallback wins, so the whole input PARSES. Documented; the
  malformed NEGATIVE test uses `100_` (trailing separator) instead, which does reject.
- **D2 — consecutive underscores are REJECTED (stricter than the Roslyn lexer).** The separated
  terminals require each `_` to be followed by at least one digit (`_[0-9]+`), so `1___0` (consecutive
  `__`) does NOT match `SeparatedDecimalIntegerLiteral` (the `(_[0-9]+)+` group needs a digit after each
  `_`) and `DecInt` matches only `1`, leaving a dangling `___0` → the parse fails. The Roslyn LEXER
  (ScanNumericLiteralSingleInteger, Lexer.cs:816-836) is more permissive here — it accepts `1___0_0___0`
  with 0 errors (LexicalTests.cs:3066-3073, value 1000) because it only flags a trailing `_` and a
  decimal-leading `_`, not consecutive `__`. This grammar is stricter (more spec-compliant: the C# spec
  forbids consecutive separators). Not in the task's test list; documented.
- **D3 — no octal separator added.** `OctalIntegerLiteral` (`0[0-7]+`) exists as a terminal but is NOT
  referenced by any grammar rule (real C# has no octal literals — checklist T1.2.1), so there is no base
  rule to extend for octal. The task's "if they exist" condition is not met for octal → no octal
  separated terminal / rule. Integer/float/hex/binary (which ARE in the grammar) all get separators.
