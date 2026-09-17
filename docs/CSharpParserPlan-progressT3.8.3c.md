# T3.8.3c — C# 8.0 range operator `..` (UNARY PREFIX form only)

Status: DONE (build green, all tests green).

## Scope (tight — ONE operator form ONLY)
The **UNARY PREFIX** form of `..`: `.. right` (no left operand), e.g. `.. 2`. The binary form
(`1 .. 2`, T3.8.3b) and the start-only postfix form (`2 ..`, T3.8.3d) are OUT of scope. The simple
spaced form `.. 2` is used.

## Roslyn precedence / associativity found
- **Prefix `..` is a UNARY PREFIX operator at `Precedence.Range`** — confirmed in
  `parseUnaryOrPrimaryExpression` (LanguageParser.cs:11478-11486):
  ```csharp
  if (IsAtDotDotToken())
  {
      return _syntaxFactory.RangeExpression(
          leftOperand: null,
          this.EatDotDotToken(),
          CanStartExpression()
              ? this.ParseSubExpression(Precedence.Range)
              : null);
  }
  ```
  `leftOperand: null` → prefix (no left operand); the right operand is `ParseSubExpression(Precedence.Range)`.
- **Precedence** — `GetPrecedence` (LanguageParser.cs:11308-11309): `RangeExpression -> Precedence.Range`.
- **Precedence ordering** — `Precedence` enum (LanguageParser.cs:11197-11221), loosest→tightest
  `… Additive, Multiplicative, Switch, Range, Unary, Cast … Primary`. `Range` sits between
  `Multiplicative` and `Unary` (tighter than Multiplicative, looser than Unary). This is the SAME level
  the CsNitra `Range` precedence level (Cs1.grammar, added in T3.8.3b) represents.
- **Associativity** — a PREFIX unary operator has NO associativity flag (its single operand is on the
  right). The operand binds at `Precedence.Range` (`ParseSubExpression(Precedence.Range)`), so it absorbs
  only operators TIGHTER than Range (Unary, Cast, primary/postfix) and stops at Range or looser. (The
  binary `..` in T3.8.3b is left-associative; the prefix form has no associativity concept.)

## Rule written
Added ONE alternative to `Expression` in Cs8.grammar (T0.3 merge, append):
```
Expression =
    | RangePrefix = ".." Expression : Range;
```
- First element is the `".."` literal (NOT a self-Ref to Expression) → classified as a TDOPP **PREFIX**
  by `BuildTdoppRulesInternal` (Parser.cs:148-149). Tried at the START of an expression.
- The operator's precedence = the `: Range` on the operand (Parser.cs:139-141) → `Range` (matches Roslyn
  `RangeExpression -> Precedence.Range`).
- The operand is `Expression : Range` (a `ReqRef`, minPrecedence = Range): absorbs only operators tighter
  than Range (Unary, Cast, primary/postfix) and stops at Range or looser — matching Roslyn
  `ParseSubExpression(Precedence.Range)`.
- `..` is the SAME 2-char LITERAL as the T3.8.3b binary form (a `StartsWith` match). NOT a terminal.
- **Mutual exclusivity**: the prefix is tried at the START (no left operand); the T3.8.3b binary
  `RangeBinary` is a POSTFIX (a left operand is present) — DIFFERENT TDOPP phases → mutually exclusive by
  position: `.. 2` (starts with `..`) → RangePrefix; `1 .. 2` (starts with `1`) → PrimaryExpr `1` +
  RangeBinary postfix. The `.` member-access (one dot + Identifier, Cs1.grammar:731) needs an Identifier
  after the single `.`, so a bare `..` never matches it (no regression).

## Code iterations
1. **First attempt = final (no iteration needed).** Added `RangePrefix = ".." Expression : Range;` to
   Cs8.grammar (the `Range` level already existed from T3.8.3b — no Cs1.grammar change, no terminal
   change). Built the solution: 0 errors. Ran the new test class: **10/10 passed on the first run**.
   No fix needed. The `: Range` operand level (matching Roslyn `ParseSubExpression(Precedence.Range)`)
   behaved exactly as hand-traced: `.. 2 + 3` = `(.. 2) + 3` (`+` Additive is looser than Range, so it is
   NOT absorbed into the operand), and `.. -5` = `.. (-5)` (Unary `-` is tighter than Range, so it IS
   absorbed).
2. Line-ending hygiene: the new test file was written with LF/no-BOM; converted to **CRLF + UTF-8 BOM**
   (matching the other `Cs8*Tests.cs` files) and re-verified with a fresh build + full test run.

## Version-purity results
- **v8 accepts** `.. 2` (and `.. a`, `return .. 2`, `.. -5`, `.. 2 + 3`): the `RangePrefix` prefix is
  present and tried at the START of an expression.
- **v7 rejects** `.. 2`: the prefix is ABSENT; at the start of the initializer Expression, PrimaryExpr
  fails (no Primary matches the `..` symbol) and no other prefix starts with `..` (the RangeBinary
  POSTFIX requires a left operand) → parse failure + recovery diagnostics.
- **v8 binary `1 .. 2` (T3.8.3b) no-regression**: `RangeBinary_ParsesAtV8_Succeeds` green — the prefix
  (tried at the START) and the binary postfix (after a left operand) are in DIFFERENT TDOPP phases, so
  adding the prefix does not interfere with the binary form.
- **v1-v7 member-access `x.y` no-regression**: `MemberAccess` (one dot + Identifier) is untouched; the
  `RangePrefix` needs two dots at the START of an expression. Verified `x.y` parses at v1 AND v7 (and all
  CSharpGrammarTests green, which exercise member access heavily).

## Tests
`Tests/CSharpGrammarTests/Cs8RangePrefixTests.cs` (CRLF + UTF-8 BOM). 10 tests:
- POSITIVE (v8): `RangePrefix_Literal_Succeeds` — `var r = .. 2;`
- POSITIVE (v8): `RangePrefix_Variable_Succeeds` — `var r = .. a;`
- POSITIVE (v8): `RangePrefix_InReturn_Succeeds` — `return .. 2;`
- POSITIVE (v8): `RangePrefix_UnaryMinusOperand_Succeeds` — `var r = .. -5;` (operand `.. (-5)`)
- POSITIVE (v8): `RangePrefix_BindsTighterThanAdditive_Succeeds` — `var r = .. 2 + 3;` (`(.. 2) + 3`)
- POSITIVE (v8, no-regression): `RangeBinary_ParsesAtV8_Succeeds` — `var r = 1 .. 2;` (T3.8.3b binary)
- POSITIVE (v1): `MemberAccess_ParsesAtV1` — `var y = x.y;` (no-regression)
- POSITIVE (v7): `MemberAccess_ParsesAtV7` — `var y = x.y;` (no-regression)
- NEGATIVE (v7, version-purity): `RangePrefix_RejectedAtV7` — `var r = .. 2;`
- NEGATIVE (v8, malformed): `RangePrefix_MissingOperand_Rejected` — `var r = .. ;` (no operand)

## Verification
- `dotnet build Nitra.sln --no-incremental` → Build succeeded, 0 Warning(s), 0 Error(s).
- `dotnet test Tests/CSharpGrammarTests` → Passed: 1259, Failed: 0, Skipped: 3 (pre-existing skips).
  (1249 pre-existing + 10 new = 1259.)
- `dotnet test Tests/ParserTests` → Passed: 325, Failed: 0, Skipped: 2 (pre-existing skips).

## Boundary decisions / deviations
- **Spaced form only (`.. 2`), not no-space (`..2`).** Consistent with T3.8.3b (the spaced form avoids the
  `Real`/`1.` literal trap). The no-space form is out of scope.
- **`.. 2 + 3` parses as `(.. 2) + 3`** (the operand stops at `+` because Additive < Range). This matches
  Roslyn's `ParseSubExpression(Precedence.Range)` for the prefix operand.
- **`..` is a literal, not a terminal** — consistent with every other operator in the grammar and with the
  T3.8.3b binary form.

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs8.grammar` — added the `RangePrefix` prefix operator + T3.8.3c header.
- `Tests/CSharpGrammarTests/Cs8RangePrefixTests.cs` — NEW.
- `docs/CSharpParserPlan-progressT3.8.3c.md` — this file.
- `Parsers/CSharp/CSharpGrammar/Cs1.grammar` — NOT modified (the `Range` level already exists from T3.8.3b).
- `Parsers/CSharp/CSharpGrammar/CSharpTerminals.cs` — NOT modified.
