# T3.8.3a — C# 8.0 index-from-end `^` prefix operator

Status: DONE — build green, all tests pass (CSharpGrammarTests 1242/0/3, ParserTests 325/0/2).

## Goal
Add the **C# 8.0 index-from-end operator `^`** to the EXISTING `Parsers/CSharp/CSharpGrammar/Cs8.grammar`
(which already has the switch-expression rules from T3.8.1 and the using-declaration rules from T3.8.2):
- **Index-from-end `^`**: `x[^1]` (a `^` PREFIX operator — an index from the end). The `^` is a new TDOPP
  PREFIX operator on `Expression`.
- `[^1]` is used inside an indexer (the `[]` postfix). The `^` operator produces an expression that is used
  as an indexer argument.

CS8 only. The version-purity boundary is v7 rejects / v8 accepts.

SCOPE (tight): ONLY the `^` index-from-end prefix operator. NOT the `..` range operator (T3.8.3b).
`x[^2..^1]` is OUT OF SCOPE.

## Baseline (before changes)
- `dotnet build Nitra.sln --no-incremental` → success, 0 errors / 0 warnings.
- `dotnet test Tests/CSharpGrammarTests` → **1232 total / 1229 passed / 0 failed / 3 skipped**.

## Cs1 structure found
- **`Expression`** (Cs1.grammar:633-673) is a TDOPP rule. Its PREFIX alternatives (a `Seq` that does NOT
  start with a self-`Ref` to `Expression` — `BuildTdoppRulesInternal`, Parser.cs:137-149) are:
  `PrimaryExpr = Primary PostfixOp*` (634), the UNARY PREFIX operators
  `UnaryPlus = "+" Expression : Unary` (635), `UnaryMinus = "-" Expression : Unary` (636),
  `UnaryNot = "!" Expression : Unary` (637), `UnaryBitNot = "~" Expression : Unary` (638),
  `PreInc = "++" Expression : Unary` (639), `PreDec = "--" Expression : Unary` (640), and
  `CastExpr = "(" Type ")" Expression : Cast` (648). Cs5 APPENDS `AwaitExpr = "await" Expression : Unary`
  (Cs5:74); Cs7 APPENDS `RefExpr = "ref" Expression : Unary` (Cs7:295) and
  `ThrowExpression = "throw" Expression : Unary` (Cs7:362). Its POSTFIX (binary) alternatives all start with a
  self-`Ref` to `Expression` (`Mul = Expression "*" Expression : Multiplicative`, …,
  `BitXor = Expression "^" Expression : LogicalXor` (667), …, `Comma = Expression "," Expression : Comma`).
- **The `^` BITWISE-XOR operator** (Cs1.grammar:667) is `BitXor = Expression "^" Expression : LogicalXor` —
  a TDOPP **POSTFIX** (binary) operator (its FIRST element is a self-`Ref` to `Expression`). It is C# 1.0
  (no version gate). It is the ONLY existing use of the `^` symbol in the grammar.
- **`^` is a SYMBOL, not a word keyword.** It is NOT in `ReservedKeyword` (Cs1.grammar:537-618 — word
  keywords only: `abstract`, `as`, `bool`, …, `while`). The important consequence (the task's "reserved"
  framing, corrected): `^` is a symbol token, so it is NOT an `Identifier` and NO `Primary` / `Expression`
  alternative starts with it. The only expression-start alternatives are identifiers, literals, `(`, and the
  reserved-word primaries (`true`/`false`/`null`/`this`/`new`/`typeof`/`sizeof`/`default`/`checked`/
  `unchecked`) plus the contextual-keyword prefixes (`await`/`ref`/`throw`). None starts with the `^` symbol.
  So a `^` PREFIX operator is MUTUALLY EXCLUSIVE with every existing `Expression` alternative at the prefix
  position.
- **`PostfixOp` / `Indexer`** (Cs1.grammar:730-735): `Indexer = "[" (Expression; ",")* "]"`. The `[` postfix
  parses an `Expression` argument (separated by `,`), so the `^` prefix operator is naturally available as an
  indexer argument (`x[^1]`).
- **TDOPP precedence list** (Cs1.grammar:623-626): `Cast, Unary, Multiplicative, Additive, Relational,
  Equality, LogicalAnd, LogicalXor, LogicalOr, CondAnd, CondOr, Conditional, Assignment, Comma`. Binding power
  `bp = count - index`, so `Cast`=16 (tightest), `Unary`=15, `Multiplicative`=14, …, `LogicalXor`=9, …,
  `Comma`=1 (loosest). A prefix operator's operand `Expression : Lvl` is a `ReqRef` with `Precedence = Lvl`
  (RuleGenerator.cs:90-94): minPrecedence = Lvl's binding power.
- **TDOPP classification** (Parser.cs:137-149): an alternative whose FIRST element is a self-`Ref` to the rule
  is a POSTFIX (applied in `ContinueFromPartialPostfix`, Parser.cs:337-424, after a left operand); any other
  alternative is a PREFIX (tried in the prefix loop, Parser.cs:258-322, at the START of the expression). The
  prefix loop keeps the LONGEST successful prefix (Success beats Partial on an equal-length tie, Parser.cs:296-316).

## Roslyn references (C:\RSDN\roslyn, main)
- **Form + precedence** — `GetPrefixUnaryExpression` (Portable/Syntax/SyntaxKindFacts.cs:416-441):
  `SyntaxKind.CaretToken` → `SyntaxKind.IndexExpression` (436-437). `IsExpectedPrefixUnaryOperator`
  (Portable/Parser/LanguageParser.cs:11356-11359) is true for `^` (a prefix unary, not ref/out). So `^` is
  parsed in `parseUnaryOrPrimaryExpression` (LanguageParser.cs:11463-11473) as a `PrefixUnaryExpression` with
  operand `ParseSubExpression(GetPrecedence(IndexExpression))`. `GetPrecedence` (LanguageParser.cs:11300-11301):
  `IndexExpression` → **`Precedence.Unary`** — the SAME level as `AwaitExpression` (11299) and every other
  unary prefix operator. So `IndexExpr = "^" Expression : Unary` is a faithful, drop-in analog.
- **The `^` BITWISE-XOR (the other role)** — `GetBinaryExpression` (SyntaxKindFacts.cs:645-658):
  `SyntaxKind.CaretToken` → `SyntaxKind.ExclusiveOrExpression`. So `^` is BOTH a prefix unary operator
  (index-from-end, CS8) and a binary operator (XOR, CS1). The two roles are disambiguated by POSITION:
  prefix (no left operand) vs infix (a left operand present). This maps exactly onto the TDOPP prefix vs
  postfix split in this grammar.
- **Version gate** — `IDS_FeatureIndexOperator` (Portable/Errors/MessageID.cs:177, in the "C# 8.0 features"
  block at :616 → `LanguageVersion.CSharp8` at :634; `// semantic check`). Checked in
  Binder/Expressions/Binder_Expressions.cs:2683 via `CheckFeatureAvailability` — a BINDER (semantic) check.
  The PARSER accepts the form wherever the rule exists (CS8 only, per the plan).
- **Syntax tests** — `src/Compilers/CSharp/Test/Syntax/Parsing/ExpressionParsingTests.cs`:
  - `IndexExpression` (5280): `^1` → `IndexExpression(CaretToken, NumericLiteralExpression(1))`.
  - `RangeExpression_Binary_WithIndexes` (5337): `^5..^3` — a range whose bounds are index-from-end
    expressions (the `..` range is T3.8.3b; only the `^` bounds are in scope here).
  - `TestIndex` (1049), `TestIndexWithRef` (1070), `TestIndexWithOut` (1095), `TestIndexWithNamedArgument`
    (1120): element-access (`[]`) forms.

## Approach (the `^` prefix operator)
Re-declare `Expression` in Cs8 to APPEND a `^` PREFIX operator, mirroring the Cs5 `await` (Cs5:74) and the
Cs7 `ref` (Cs7:295) / `throw` (Cs7:362) prefix operators EXACTLY:

```
Expression =
    | IndexExpr = "^" Expression : Unary;
```

- **Why a TDOPP prefix operator (not a `Primary`)**: Roslyn parses `^` in the prefix-expression phase
  (`parseUnaryOrPrimaryExpression`, LanguageParser.cs:11463-11473) with operand `ParseSubExpression(
  GetPrecedence(IndexExpression))` = `Precedence.Unary` (LanguageParser.cs:11300-11301) — the SAME level as
  the Cs1 `-`/`!`/`~`/`++`/`--` prefix operators and the Cs5 `await` / Cs7 `ref` / `throw` prefixes. So
  `IndexExpr = "^" Expression : Unary` is a faithful, drop-in analog. It is a PREFIX alternative (it starts
  with the `"^"` literal, NOT a self-`Ref` to `Expression`), so the TDOPP builder classifies it as a prefix
  (Parser.cs:137-149) and it is tried among the prefix alternatives (at the START of an expression).
- **The `: Unary` precedence sets the operand's minPrecedence to the Unary level (bp 15)**: the operand binds
  tighter than all binary operators (Multiplicative bp 14 and below), so `^ x + y` = `(^ x) + y`, and the
  `Comma` operator (bp 1, the lowest) is NOT applicable to the operand, so the operand does NOT absorb a
  following `,` in a comma-separated context (e.g. an indexer argument list `x[^i, ^j]`). This is the TDOPP
  Comma mechanism (T3.3.2) handled by the `: Unary` precedence — no separate `Expression : Comma` is needed.
- **Why NOT a `Primary`**: a `Primary`-level `IndexExpr = "^" Expression` would have the operand at
  minPrecedence 0, so it WOULD absorb the `Comma` (the T3.3.2 defect), and it would not match Roslyn's
  `Precedence.Unary` placement. The TDOPP prefix form is both more correct and simpler (the same reason the
  Cs5 `await` and Cs7 `ref` are TDOPP prefixes, not `Primary`s).

## Mutual-exclusivity hand-traces (no equal-length tie)
The `^` PREFIX (`IndexExpr`) and the `^` BINARY (`BitXor` postfix, Cs1:667) are in DIFFERENT TDOPP phases:
the prefix is tried at the START of an expression (no left operand); the postfix is applied AFTER a left
operand. They are therefore mutually exclusive.
- `^1` (v8, index-from-end): prefix loop — `PrimaryExpr` FAILS (no `Primary` matches the `^` symbol);
  `IndexExpr` → `"^", Expression : Unary = 1` → `^1` (2 chars). SOLE successful prefix. Postfix loop on `^1`:
  no applicable postfix. → **IndexExpr**.
- `x ^ y` (v1-v8, bitwise-XOR): prefix loop — `PrimaryExpr` → `x` (1 char); `IndexExpr` FAILS (the start is
  `x`, not `^`). SOLE successful prefix = `x`. Postfix loop on `x`: `BitXor` (bp LogicalXor) is applicable
  (the next token is `^`) → `^ y` → `x ^ y`. → **BitXor**. The `^` prefix is NEVER tried (the start is `x`).
- `x[^1]` (v8): prefix `x`; postfix `Indexer` (`"[" (Expression; ",")* "]"`) → inside the `[ ]`, an
  `Expression` starts with `^` → the `IndexExpr` prefix matches `^1`. → `x [ ^1 ]`.
- `x ^ ^1` (v8): prefix `x`; postfix `BitXor` (bp LogicalXor) → RHS `Expression : LogicalXor` starts with
  `^` → `IndexExpr` (`^1`). → `x ^ (^1)`. (The index-from-end binds tighter than the XOR; Roslyn `^` prefix
  is Unary, XOR is LogicalXor — same ordering.)
- `^ a ^ b` (v8): prefix `IndexExpr` (`^ a`, operand `a` at Unary level — the `^`/LogicalXor is below Unary,
  so NOT absorbed); postfix `BitXor` (bp LogicalXor) applies to `(^ a)` → `^ b`. → `(^ a) ^ b`.
- bare `^` (no operand, e.g. `x[^]`): prefix loop — `PrimaryExpr` FAILS; `IndexExpr` FAILS (no `Expression :
  Unary` after the `^` — the next token is `]`, not an expression start). NO successful prefix → the inner
  `Expression` fails → the `Indexer` fails → the whole expression fails → **REJECT** (the malformed case).

## Version-purity analysis
- **v8 (Cs1+…+Cs8)**: the `IndexExpr` prefix is PRESENT. `x[^1]` → the indexer's inner `Expression` matches
  `^1` via `IndexExpr` → PARSES.
- **v7 (Cs1+…+Cs7, no CS8)**: the `IndexExpr` prefix is ABSENT. `x[^1]` → the indexer's inner `Expression`
  starts with `^`; `PrimaryExpr` fails (no `Primary` matches `^`); no other prefix matches `^` → the inner
  `Expression` fails → the `Indexer` fails → REJECTS. (The `^` binary `BitXor` is a POSTFIX and requires a
  left operand, so it cannot start the inner expression.)
- **v1-v7 no-regression (bitwise-XOR)**: `x ^ y` parses via the Cs1 `BitXor` postfix at every version
  (the `^` prefix is absent at v1-v7; at v8 it is present but only applies at the START of an expression, so
  it does not interfere with `x ^ y`). No regression.

## Tests
`Tests/CSharpGrammarTests/Cs8IndexTests.cs` (CRLF + UTF-8 BOM) — **10 tests, all green**. Positives use
`CreateParser(8)`, version-purity negatives use `CreateParser(7)`.
- POSITIVE (v8, core, 3): `IndexFromEnd_Basic_Succeeds` (`class C { void M(string x) { var y = x[^1]; } }`),
  `IndexFromEnd_Variable_Succeeds` (`class C { void M(string x, int i) { var y = x[^i]; } }`),
  `IndexFromEnd_InReturn_Succeeds` (`class C { char M(string x) { return x[^1]; } }`).
- POSITIVE (v8, Roslyn-derived, 3):
  - `IndexFromEnd_Literal_Roslyn_Succeeds` (`class C { void M(string x) { var y = x[^2]; } }` — Roslyn
    ExpressionParsingTests.cs:5280 `IndexExpression`, `^1` -> IndexExpression(CaretToken,
    NumericLiteralExpression); adapted to the `^2` literal form as an indexer argument).
  - `IndexFromEnd_TwoInBinary_Roslyn_Succeeds` (`class C { void M(string x) { var y = x[^5] + x[^3]; } }` —
    derived from Roslyn ExpressionParsingTests.cs:5337 `RangeExpression_Binary_WithIndexes`, `^5..^3`, where
    `^5`/`^3` are index-from-end bounds of a `..` range; the `..` range is T3.8.3b, so only the two `^`
    index-from-end expressions are in scope — adapted to two indexers combined with `+`).
  - `IndexFromEnd_LocalDeclaration_Roslyn_Succeeds` (`class C { char M(string x) { char c = x[^1]; return
    c; } }` — an index-from-end as the initializer of a typed local declaration).
- POSITIVE (v1 and v7, no-regression, bitwise-XOR, 2): `BitwiseXor_ParsesAtV1` / `BitwiseXor_ParsesAtV7`
  (`class C { int M(int x, int y) { return x ^ y; } }` — the `^` BITWISE-XOR (Cs1 `BitXor` postfix) is
  UNCHANGED; the `^` prefix is absent at v1-v7 and at v8 only applies at the START of an expression, so it
  does not interfere with `x ^ y`).
- NEGATIVE (v7, version-purity, 1): `IndexFromEnd_RejectedAtV7` (`class C { void M(string x) { var y =
  x[^1]; } }` — the index-from-end is CS8-only; at v7 the inner Expression of the indexer fails on the `^`).
- NEGATIVE (v8, malformed, 1): `IndexFromEnd_MissingOperand_Rejected` (`class C { void M(string x) { var y =
  x[^]; } }` — the `IndexExpr` prefix requires an `Expression : Unary` after the `^`; `]` is not an
  expression start -> the prefix fails -> REJECTS).

## Empirical mutual-exclusivity confirmation (probe, not a permanent test)
A throwaway probe (created, run green, deleted) confirmed the `^` prefix works as the RHS of the `^` BINARY
and in combined forms at v8: `a ^ 1` (XOR with a literal), and `a[^1] ^ b[^2]` (XOR of two index-from-end
expressions) both parse. This directly exercises the prefix/postfix mutual exclusivity documented in the
hand-traces (the `BitXor` postfix's RHS is an `Expression` that may start with the `^` prefix). Kept out of
the permanent suite to keep the scope tight (the task's specified test list + the Roslyn-derived cases above
already cover the prefix-in-a-binary context via `x[^5] + x[^3]` and the XOR via `x ^ y`).

## Verification (all green)
- [x] `dotnet build Nitra.sln --no-incremental` → **success, 0 errors / 0 warnings**.
- [x] `dotnet test Tests/CSharpGrammarTests` (fresh build) → **1242 total / 1239 passed / 0 failed / 3
  skipped**. Baseline before T3.8.3a: 1232 total / 1229 passed / 3 skipped. Delta = **+10** (all new
  `Cs8IndexTests`). All pre-existing Cs1/Cs2/Cs3/Cs4/Cs5/Cs6/Cs7/Cs11 tests remain green (no Cs1-Cs7/Cs11
  modification).
- [x] `dotnet test Tests/ParserTests` (regression; engine + CsNitra meta-grammar unchanged) → **327 total /
  325 passed / 0 failed / 2 skipped** — matches baseline.
- [x] `Cs8IndexTests` → **10/10** (3 positive v8 core + 3 positive v8 Roslyn-derived + 2 positive v1/v7
  bitwise-XOR no-regression + 1 v7 version-purity negative + 1 v8 malformed negative).

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs8.grammar` (the `IndexExpr` prefix operator).
- `Tests/CSharpGrammarTests/Cs8IndexTests.cs` (new).
- `docs/CSharpParserPlan-progressT3.8.3a.md` (this file).

## Boundary decisions / deviations
- **D1 — `^` is a SYMBOL, not a word keyword (the task's "reserved keyword" framing, corrected).** The
  task says "`^` is a reserved keyword (verify in the Cs1 `ReservedKeyword`)". Verified: `^` is NOT in
  `ReservedKeyword` (Cs1.grammar:537-618 — word keywords only: `abstract`, `as`, `bool`, …, `while`). `^`
  is a symbol token (the same token the Cs1 `BitXor` postfix uses, Cs1.grammar:667). The CONSEQUENCE the
  task intends still holds: `^` is not an `Identifier`, so NO `Primary`/`Expression` alternative starts with
  it, and the `^` PREFIX operator is mutually exclusive with every existing `Expression` alternative at the
  prefix position. No reservation is needed (and adding `^` to `ReservedKeyword` would be wrong — that rule
  is for word keywords matched by the `Identifier` terminal, and `^` is a symbol).
- **D2 — The `^` index-from-end is a TDOPP PREFIX operator (`Expression : Unary`), NOT a `Primary`.** It
  mirrors Roslyn exactly (`GetPrefixUnaryExpression(CaretToken) = IndexExpression`, SyntaxKindFacts.cs:436-437;
  `GetPrecedence(IndexExpression) = Precedence.Unary`, LanguageParser.cs:11300-11301) and the Cs1 `-`/`!`/`~`
  prefix operators, the Cs5 `await` (Cs5:74), and the Cs7 `ref` (Cs7:295) / `throw` (Cs7:362) prefixes. The
  `: Unary` precedence sets the operand's minPrecedence to the Unary level (bp 15), which EXCLUDES the
  `Comma` operator (bp 1) — so the T3.3.2 comma-absorption defect does not apply and no separate
  `Expression : Comma` is needed (e.g. an indexer argument list `x[^i, ^j]` keeps the `,` as the separator).
  A `Primary`-level `^` would have the operand at minPrecedence 0 (absorbing the Comma) and would not match
  Roslyn's `Precedence.Unary` placement.
- **D3 — The `^` PREFIX vs the `^` BINARY (bitwise-XOR) are disambiguated by TDOPP PHASE, not by any
  lookahead.** The `^` prefix (`IndexExpr`) is tried at the START of an expression (no left operand); the
  `^` binary (`BitXor` postfix, Cs1:667) is applied AFTER a left operand. They are in different phases of
  `ParseRule` (prefix loop, Parser.cs:258-322, vs `ContinueFromPartialPostfix`, Parser.cs:337-424), so they
  are mutually exclusive by construction — no equal-length tie, no lookahead needed. `^1` (prefix) and
  `x ^ y` (postfix) never compete. This is the exact Roslyn split (`^` as prefix unary → IndexExpression vs
  `^` as binary → ExclusiveOrExpression, SyntaxKindFacts.cs:436-437 / :657-658).
- **D4 — The `..` range operator is OUT OF SCOPE (T3.8.3b).** Only the `^` index-from-end prefix is added.
  The Roslyn-derived test `IndexFromEnd_TwoInBinary_Roslyn_Succeeds` is adapted from `^5..^3` (which needs
  the `..` range) to `x[^5] + x[^3]` (two `^` index-from-end expressions combined with `+`), so it stays
  within the `^`-only scope.
- **No csproj / version-table change needed.** `Cs8.grammar` already exists (T3.8.1) and is already
  registered in `CSharpGrammar.csproj` (`<EmbeddedResource Include="Cs8.grammar" />`) and in
  `EmbeddedGrammar._versions` (version 8). Adding a rule to the EXISTING Cs8.grammar file requires no csproj
  or version-table change (and the task forbids touching the csproj — the SDK default globbing already
  includes all `.cs`).
