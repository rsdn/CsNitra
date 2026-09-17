# T3.8.3b — C# 8.0 range operator `..` (BINARY form only)

Status: DONE (build green, all tests green).

## Scope (tight — ONE operator form ONLY)
The **BINARY** form of `..`: `left .. right` (both operands present), e.g. `1 .. 2`. The unary
prefix form (`.. 2`) and unary postfix form (`2 ..`) are OUT of scope (T3.8.3c / T3.8.3d). The simple
spaced form `1 .. 2` is used (NOT `x[1..^1]`).

## Terminals found / added
- NO `.` terminal and NO `..` terminal exist in `CSharpTerminals.cs`. The single `.` is a grammar
  **LITERAL** used only in `MemberAccess = "." Identifier` (Cs1.grammar:731), `PredefinedMember`, and
  `BaseMember` — all of which REQUIRE an `Identifier` after the `.`.
- **No `DotDot` terminal was added.** `..` is a 2-char grammar **LITERAL** `".."` (a `StartsWith` match,
  Rules.cs:76-77) — the SAME mechanism as every other operator literal in the grammar (`"=="`, `"++"`,
  `"."`, …). Literals in a `.grammar` file are NOT required to be in `CSharpTerminals.GetAll()` (that
  list holds the "real" terminals — identifiers, numbers, strings, trivia — used by the recovery engine
  for resync/injection; operator literals like `"=="`/`"++"` are not in it either).
- **`CSharpTerminals.cs` was NOT modified.**

## Precedence level added
- A NEW `Range` level, inserted in the Cs1 precedence list **between `Unary` and `Multiplicative`**:
  `Cast, Unary, Range, Multiplicative, Additive, Relational, …, Comma`. This matches Roslyn
  `Precedence` (LanguageParser.cs:11197-11221), ordered loosest→tightest `… Multiplicative, Switch,
  Range, Unary, Cast …` — `Range` sits between `Multiplicative` and `Unary`.
- After the insert the list has 17 levels (was 16); `Comma` stays the lowest (bp 1). The RELATIVE order
  of every pre-existing level is unchanged (only absolute binding-power numbers shift by 1), so all
  existing operator behavior is preserved (all comparisons in the engine are relative). At v1-v7 the
  `Range` level exists but NO operator uses it (no-op) — the `RangeBinary` operator is Cs8-only.

## Approach (the binary `..` TDOPP POSTFIX operator)
Added ONE alternative to `Expression` in Cs8.grammar (T0.3 merge, append):
```
Expression =
    | RangeBinary = Expression : Range ".." Expression : Range;
```
- First element is a self-`Ref` to `Expression` → classified as a TDOPP **POSTFIX** (binary) by
  `BuildTdoppRulesInternal` (Parser.cs:137-141). `ReqRef : Ref` (Rules.cs:394), so the `: Range` on the
  first element satisfies the `[Ref rule, ..]` pattern.
- The operator's precedence comes from the FIRST `ReqRef` in `rest` (the right operand `Expression :
  Range`) → precedence = `Range` (Parser.cs:139-141). The `: Range` on the first (left) element is only
  for classification (the left operand is the already-parsed prefix; it is not re-parsed).
- Applied AFTER a left operand: `E1` + `..` + `E2`. `minPrecedence = Range` on the right operand so it
  does not re-enter a `..` at the same level (left-associative) and stops before a `,`.
- **Mutual exclusivity**: the `..` literal is matched ONLY by this postfix (no other postfix starts with
  `..`). The `.` member-access is in the `PostfixOp*` loop (a DIFFERENT phase, run while resolving the
  prefix) and REQUIRES an `Identifier` after the single `.` — a bare `..` (second char is `.`, not an
  identifier) never matches `MemberAccess`. So `x.y` (one dot + identifier) → `MemberAccess`; `1 .. 2`
  (two dots) → `RangeBinary`. No equal-length tie.
- **The `1 .` real-literal trap is AVOIDED by the required spaces**: `DecimalRealLiteral`
  (`[0-9]+\.[0-9]*`) matches `1.` only when a dot immediately follows the digit. In `1 .. 2` the space
  after `1` stops it, so the left operand is `DecInt`=`1` and the `..` postfix applies. (The no-space
  form `1..2` would hit the `Real`/`1.` trap and is OUT OF SCOPE — see Boundary decisions.)

## Code iterations
1. **First attempt = final (no iteration needed).** Added the `Range` precedence level to Cs1.grammar and
   the `RangeBinary` postfix to Cs8.grammar, then built. Build succeeded on the first try (0 errors, 0
   warnings). No terminal change was required (the previous subagent's circular reasoning was about the
   no-space `1..2` form and a `DecimalRealLiteral` fix — neither applies to the spaced `1 .. 2` form this
   task uses).
2. Ran the new test class: 10/10 passed on the first run. No fix needed.
3. Ran the full CSharpGrammarTests (1249 pass) and ParserTests (325 pass): all green on the first run —
   no regression from adding the `Range` precedence level.

## Version-purity results
- **v8 accepts** `1 .. 2` (and `a .. b`, `return 1 .. 2`): the `RangeBinary` postfix is present.
- **v7 rejects** `1 .. 2`: the postfix is ABSENT; the left operand `1` parses, then no postfix consumes
  `..` (MemberAccess fails — the char after the first `.` is `.`, not an identifier), so the statement
  sees `..` where `;` is expected → parse failure + recovery diagnostics.
- **v1-v7 member-access `x.y` no-regression**: `MemberAccess` (one dot + identifier) is untouched; the
  `RangeBinary` postfix needs two dots. Verified `x.y` parses at v1 AND v7 (and all CSharpGrammarTests
  green, which exercise member access heavily).
- **v8 `x[^1]` (T3.8.3a) no-regression**: the `IndexExpr` prefix is untouched (all Cs8IndexTests green).

## Tests
`Tests/CSharpGrammarTests/Cs8RangeBinaryTests.cs` (CRLF + UTF-8 BOM). 10 tests:
- POSITIVE (v8): `RangeBinary_Literals_Succeeds` — `var r = 1 .. 2;`
- POSITIVE (v8): `RangeBinary_Variables_Succeeds` — `var r = a .. b;`
- POSITIVE (v8): `RangeBinary_InReturn_Succeeds` — `return 1 .. 2;`
- POSITIVE (v8, Roslyn RangeExpression_Binary, ExpressionParsingTests.cs:5318 `1..2`):
  `RangeBinary_Roslyn_Binary_Succeeds` — `var r = 5 .. 9;` (spaced adaptation)
- POSITIVE (v8, Roslyn-derived): `RangeBinary_AsArgument_Roslyn_Succeeds` — `M(1 .. 2);` (range as a
  method argument; the Cs4 `Argument` operand `Expression : Comma` stops before `)`)
- POSITIVE (v8): `RangeBinary_TypedLocal_Succeeds` — `object r = 1 .. 2;`
- POSITIVE (v1): `MemberAccess_ParsesAtV1` — `var y = x.y;` (no-regression)
- POSITIVE (v7): `MemberAccess_ParsesAtV7` — `var y = x.y;` (no-regression)
- NEGATIVE (v7, version-purity): `RangeBinary_RejectedAtV7` — `var r = 1 .. 2;`
- NEGATIVE (v8, malformed): `RangeBinary_MissingRightOperand_Rejected` — `var r = 1 .. ;` (start-only
  form is T3.8.3d, not implemented → the binary postfix requires a right operand → rejects)

## Verification
- `dotnet build Nitra.sln --no-incremental` → Build succeeded, 0 Warning(s), 0 Error(s).
- `dotnet test Tests/CSharpGrammarTests` → Passed: 1249, Failed: 0, Skipped: 3 (pre-existing skips).
- `dotnet test Tests/ParserTests` → Passed: 325, Failed: 0, Skipped: 2 (pre-existing skips).

## Boundary decisions / deviations
- **Spaced form only (`1 .. 2`), not no-space (`1..2`).** The task mandates the simple spaced example.
  The no-space form `1..2` does NOT parse: at the digit, `DecimalRealLiteral` (`[0-9]+\.[0-9]*`) matches
  `1.` (2 chars) and wins longest-match over `DecInt` (`1`, 1 char), leaving `.2` unparsable. Fixing that
  (the previous subagent's "D2" — changing the regex to `[0-9]+\.[0-9]+`) would drop the bare `1.` real
  literal and risks pre-existing tests, so it is deliberately NOT done (out of scope; would be a separate
  terminal-level sub-point).
- **Chained `1 .. 2 .. 3` parses as `(1..2)..3`** (left-associative: the inner `Expression : Range` at
  minPrecedence=Range blocks the second `..` from the inner operand, so the outer postfix then applies).
  Real C# REJECTS `1..2..3` (a range cannot be a range operand). This is a known deviation; NOT asserted
  as a positive test (to avoid encoding incorrect C# behavior). Making it reject would require a
  non-associative guard (out of scope).
- **`..` is a literal, not a terminal** — consistent with every other operator in the grammar.

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs1.grammar` — added the `Range` precedence level (between `Unary` and
  `Multiplicative`) + comment.
- `Parsers/CSharp/CSharpGrammar/Cs8.grammar` — added the `RangeBinary` postfix operator + T3.8.3b header.
- `Tests/CSharpGrammarTests/Cs8RangeBinaryTests.cs` — NEW (10 tests).
- `docs/CSharpParserPlan-progressT3.8.3b.md` — this file (overwritten).
- `docs/CSharpParserPlan-checklist.md` — pre-existing orchestrator edit (T3.8.3b reworded to the binary
  form, T3.8.3c/d added); left at `[~]` (NOT marked `[✅]` — the orchestrator marks it after committing).
- `Parsers/CSharp/CSharpGrammar/CSharpTerminals.cs` — NOT modified.
