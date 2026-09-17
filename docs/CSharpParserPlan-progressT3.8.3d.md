# CSharpParserPlan — T3.8.3d progress

## Status
IN PROGRESS — extending the `Expression` rule with the UNARY POSTFIX operator `..` (start-only range form, e.g. `2 ..`).

## Task
Add the UNARY POSTFIX `..` (start-only range) operator to the Cs8 `Expression` rule at the `Range`
precedence level. The binary (`1 .. 2`, T3.8.3b) and unary PREFIX (`.. 2`, T3.8.3c) forms already exist.

## Roslyn precedence / associativity (research)
Source: `C:\RSDN\roslyn\src\Compilers\CSharp\Portable\Parser\LanguageParser.cs`.

- **Precedence** — `GetPrecedence` (LanguageParser.cs:11308-11309): `SyntaxKind.RangeExpression ->
  Precedence.Range`. The `Precedence` enum (11197-11221) orders
  `… Multiplicative, Switch, Range, Unary, Cast …` (11213-11217): `Range` sits between `Multiplicative`
  and `Unary` — the SAME level the CsNitra `Range` precedence (Cs1.grammar:627, added in T3.8.3b) uses.
- **UNARY POSTFIX (start-only) form** — parsed in `ParseExpressionContinued.tryExpandExpression`
  (LanguageParser.cs:11617-11625):
  ```csharp
  if (operatorExpressionKind == SyntaxKind.RangeExpression)
  {
      return _syntaxFactory.RangeExpression(
          leftOperand,
          operatorToken,
          CanStartExpression()
              ? this.ParseSubExpression(Precedence.Range)
              : null);
  }
  ```
  When `CanStartExpression()` is FALSE (nothing expression-start follows the `..`), the right operand is
  `null` — this is the start-only / unary POSTFIX form (`2 ..`). The left operand is the already-parsed
  prefix; the `..` is applied AFTER it (a POSTFIX). Confirmed as the task described.
- **Associativity** — `IsRightAssociative` (LanguageParser.cs:11173-11195): `RangeExpression` is NOT in the
  right-associative list → the binary `..` is LEFT-associative. The unary POSTFIX form has no right operand,
  so no associativity flag applies (it is a postfix with only the `..` literal after the left operand).
- **Operator recognition** — `GetExpressionOperatorTokenKindAndExpressionKind` (LanguageParser.cs:11742-11744):
  a `..` (DotDotToken) yields `(DotDotToken, RangeExpression)`.

## The rule
Added to `Cs8.grammar` (re-declares `Expression`, T0.3 merge — APPENDS an alternative):

```
Expression =
    | RangePostfix = Expression : Range "..";
```

Shape: a self-Ref to `Expression` (with `: Range`) followed by the `..` literal and NO right operand.
The TDOPP builder (Parser.cs:137-146) classifies it as a POSTFIX (first element is a self-Ref to the same
rule). Since `rest` (`[".."]`) contains no `ReqRef`, the builder falls back to the first element's
precedence (`rule is ReqRef x ? x.Precedence : 0` → `Range`, Parser.cs:144-145) and emits
`RuleWithPrecedence(Kind: RangePostfix, Seq: [".."], Precedence: Range, Right: false)` (Parser.cs:145).
This is a postfix that matches the bare `..` at the `Range` level with no right operand — exactly the
start-only form.

## Disambiguation with the binary form
Both `RangeBinary` (Seq `["..", ReqRef(Expression,Range)]`) and `RangePostfix` (Seq `[".."]`) are POSTFIX
operators at the `Range` level. The engine is longest-match-wins (Parser.cs:386). Empirically:
- `2 .. 3` → `RangeBinary` consumes `2 .. 3` (longer) and wins over `RangePostfix` (consumes `2 ..`).
- `2 ..` (nothing after) → `RangeBinary` FAILS (no right operand after `..`), `RangePostfix` matches `..`
  → `2 ..` parses.
So the two are disambiguated purely by longest-match: the binary form wins whenever a right operand is
present; the unary postfix form only fires when the binary form fails (no right operand).

## Code iterations
1. **Iteration 1 (the only one needed)** — added `RangePostfix = Expression : Range ".."` to `Cs8.grammar`
   (re-declares `Expression`, T0.3 merge). Built the solution (0 errors). Ran `Tests/CSharpGrammarTests`:
   - Result: 1269 passed, **1 failed**, 3 skipped. The single failure was the PRE-EXISTING T3.8.3b test
     `Cs8RangeBinaryTests.RangeBinary_MissingRightOperand_Rejected`, which asserted `var r = 1 .. ;` REJECTS
     at v8. Its own comment said "`1 ..` (the start-only form) is T3.8.3d (out of scope here)". Now that
     T3.8.3d is implemented, `1 .. ;` IS the valid start-only form and parses → the old assertion is
     obsolete. **This is an expected, direct consequence of adding the start-only form, not a regression.**
2. **Iteration 2** — updated that one obsolete T3.8.3b test to reflect the new reality: renamed
   `RangeBinary_MissingRightOperand_Rejected` → `RangePostfix_StartOnly_ParsesAtV8` (now `AssertParsesV8`),
   and added `RangePostfix_StartOnly_RejectedAtV7` (`AssertFailsV7`) to preserve the version-purity intent
   (at v7 the start-only `..` is absent → rejects). Re-ran: all green.

No grammar/engine changes were needed — the TDOPP builder (Parser.cs:137-146) already handles a postfix
whose `rest` has no `ReqRef` (it falls back to the first element's precedence, Parser.cs:144-145).

## Disambiguation — verified empirically (build + test)
- `2 .. 3` (both operands) → `RangeBinary` consumes `2 .. 3` (longer) and wins over `RangePostfix`
  (consumes `2 ..`). Test `RangeBinary_BothOperands_ParsesAtV8` green.
- `2 ..` (nothing after) → `RangeBinary` FAILS (no right operand after `..`), `RangePostfix` matches the
  bare `..` → parses. Test `RangePostfix_Literal` green.
- The T3.8.3c prefix `.. 2` (no left operand) is unaffected (a prefix, tried at the START of the
  expression; the postfix requires a left operand). Test `RangePrefix_ParsesAtV8` green.
- The `.` member-access (`x.y`, one dot + Identifier, a PostfixOp-loop postfix) is unaffected (needs an
  Identifier after the single `.`; a bare `..` never matches it). Tests `MemberAccess_ParsesAtV1/V7` green.

## Version-purity results
- `CreateParser(7)` REJECTS the start-only form: `var r = 2 .. ;` fails at v7 (no `RangePostfix` postfix
  present → trailing `..` unconsumed → statement sees `..` where `;` expected). Test `RangePostfix_RejectedAtV7`
  green. Also `RangePostfix_StartOnly_RejectedAtV7` (from the updated T3.8.3b file) green.
- `CreateParser(8)` ACCEPTS it: `var r = 2 .. ;` parses. Test `RangePostfix_Literal` green.
- The binary `1 .. 2` and prefix `.. 2` forms are UNCHANGED at v1-v8 (no regression).

## Tests (pos/neg)
New file `Tests/CSharpGrammarTests/Cs8RangePostfixTests.cs` (CRLF + UTF-8 BOM), 11 tests:
- POSITIVE (v8): `RangePostfix_Literal` (`2 ..`), `RangePostfix_Variable` (`a ..`), `RangePostfix_InReturn`
  (`return 2 ..`), `RangePostfix_MemberLeftOperand` (`x ..`), `RangeBinary_ParsesAtV8` (`1 .. 2`),
  `RangeBinary_BothOperands_ParsesAtV8` (`2 .. 3`), `RangePrefix_ParsesAtV8` (`.. 2`),
  `MemberAccess_ParsesAtV1` (`x.y`), `MemberAccess_ParsesAtV7` (`x.y`).
- NEGATIVE: `RangePostfix_RejectedAtV7` (v7 version-purity, `2 ..`), `Range_BareDotDotNoOperands_Rejected`
  (v8, bare `.. ;` — no left AND no right operand → not the start-only form → rejects).

Updated `Tests/CSharpGrammarTests/Cs8RangeBinaryTests.cs` (1 obsolete test → 2 tests):
- `RangePostfix_StartOnly_ParsesAtV8` (v8 positive, was the obsolete reject), `RangePostfix_StartOnly_RejectedAtV7`
  (v7 negative, version-purity).

## Verification results
- `dotnet build Nitra.sln --no-incremental` → **Build succeeded, 0 Warning(s), 0 Error(s)**.
- `dotnet test Tests/CSharpGrammarTests` (fresh build) → **Passed! Failed: 0, Passed: 1271, Skipped: 3,
  Total: 1274** (the 3 skips are pre-existing string-literal tests, unrelated).
- `dotnet test Tests/ParserTests` (regression) → **Passed! Failed: 0, Passed: 325, Skipped: 2, Total: 327**
  (the 2 skips are pre-existing, unrelated).

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs8.grammar` — added the `RangePostfix = Expression : Range ".."` rule
  (with the T3.8.3d comment block). CRLF / no BOM.
- `Tests/CSharpGrammarTests/Cs8RangePostfixTests.cs` — NEW (11 tests). CRLF + UTF-8 BOM.
- `Tests/CSharpGrammarTests/Cs8RangeBinaryTests.cs` — updated 1 obsolete test (its premise resolved by
  T3.8.3d) into 1 positive + 1 v7-negative test. CRLF + UTF-8 BOM.
- `docs/CSharpParserPlan-progressT3.8.3d.md` — this progress file.
- `docs/CSharpParserPlan-checklist.md` — (pre-existing working-tree state from the prior subagent: T3.8.3c
  `[✅]`, T3.8.3d `[~]` in-progress). T3.8.3d is NOT marked `[✅]` here — the orchestrator marks it after
  verifying and committing.

## Working-tree hygiene
Staged ONLY the T3.8.3d files (Cs8.grammar, Cs8RangePostfixTests.cs, Cs8RangeBinaryTests.cs, progress file,
checklist). Left UNRELATED files alone and UNSTAGED: `docs/antlr4-analysis.md`, `docs/RecoveryImprovementPlan.md`.
Did NOT commit (the orchestrator commits). Did NOT modify any `.csproj`.
