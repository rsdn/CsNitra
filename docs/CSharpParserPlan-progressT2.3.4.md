# T2.3.4 — C# 1.0 Expression: `checked ( expr )` / `unchecked ( expr )` — Progress

## Status: done (build 0 errors; CSharpGrammarTests 766 passed / 0 failed / 3 pre-existing skips)

## Task
Add the **`checked (expr)` / `unchecked (expr)` EXPRESSION form** to the C# 1.0 grammar's `Primary`
rule in `Parsers/CSharp/CSharpGrammar/Cs1.grammar`. This closes the last C# 1.0 expression gap.

The **statement** form (`checked { ... }`, `checked (expr);` as a statement) is ALREADY done in
T2.2.3 (`CheckedStatement`/`UncheckedStatement`/`CheckedBody`) and is NOT touched.

## Roslyn semantics ported (C:\RSDN\roslyn, src/Compilers/CSharp/Portable/Parser/LanguageParser.cs)

### (A) Expression-level — `checked`/`unchecked` are PRIMARY expressions
`ParsePrimaryExpressionWithoutPostfix` (line 11966-11968):
```csharp
case SyntaxKind.CheckedKeyword:
case SyntaxKind.UncheckedKeyword:
    return this.ParseCheckedOrUncheckedExpression();
```
`ParseCheckedOrUncheckedExpression` (line 12677-12689):
```csharp
var checkedOrUnchecked = this.EatToken();           // `checked` | `unchecked`
var kind = checkedOrUnchecked.Kind == SyntaxKind.CheckedKeyword
    ? SyntaxKind.CheckedExpression : SyntaxKind.UncheckedExpression;
return _syntaxFactory.CheckedExpression(
    kind,
    checkedOrUnchecked,
    this.EatToken(SyntaxKind.OpenParenToken),        // `(`
    this.ParseExpressionForParenthesizedConstruct(), // <full expression>
    this.EatToken(SyntaxKind.CloseParenToken));      // `)`
```
So the form is exactly `checked ( expression )` / `unchecked ( expression )`, and it is a **primary
expression** (highest precedence — it is dispatched from `ParsePrimaryExpressionWithoutPostfix`).

### (B) Inner expression is a FULL expression
`ParseExpressionForParenthesizedConstruct` (line 9980-9981) → `ParseExpressionCore()` (full TDOPP
expression). So `checked (x + y * z)` checks the whole `x + y * z` (the parens delimit the checked
expression). The grammar's `Parens = "(" Expression ")"` already uses the full `Expression`, so the
new rules mirror it: `checked "(" Expression ")"`.

### (C) Statement-level — `checked ( expr );` is an ExpressionStatement, NOT a CheckedStatement
`ParseCheckedStatement` (line 9507-9522):
```csharp
if (this.PeekToken(1).Kind == SyntaxKind.OpenParenToken)
{
    return this.ParseExpressionStatement(attributes);   // `checked ( ... );` → expression statement
}
var keyword = this.EatToken();
return _syntaxFactory.CheckedStatement(..., this.ParsePossiblyAttributedBlock()); // `checked { ... }`
```
Roslyn itself dispatches `checked ( ... )` in statement position to `ParseExpressionStatement` — the
statement is an **ExpressionStatement** whose expression is the `checked` expression. Only the
`checked { ... }` block form is a `CheckedStatement`.

## The fix (declarative — two new `Primary` alternatives)

`Parsers/CSharp/CSharpGrammar/Cs1.grammar`, `Primary` rule — two new alternatives added after `TypeOf`
(lines 719-720):

```
| CheckedExpr   = "checked"   "(" Expression ")"
| UncheckedExpr = "unchecked" "(" Expression ")";
```

These are self-contained primary units (like `Parens`). The inner `Expression` is a full TDOPP
expression.

## Disambiguation / precedence

- **vs `Parens` (no tie).** `Parens = "(" Expression ")"` starts with `(`. `CheckedExpr`/`UncheckedExpr`
  start with the `checked`/`unchecked` **keyword**. Distinct leading tokens → no equal-length tie;
  longest-match-wins is unaffected.
- **`checked`/`unchecked` ARE reserved keywords** (`ReservedKeyword` rule, lines 548 and 611). So the
  `IdentifierName = !ReservedKeyword Identifier` guard blocks them as identifiers — they can only start
  an `Expression` via `CheckedExpr`/`UncheckedExpr`. No other `Primary` alternative starts with
  `checked`/`unchecked`, so there is no competing match.
- **Precedence: primary (highest).** `checked (x) + 1` → `(checked (x)) + 1` (root `Add`, left operand
  `CheckedExpr`). The parens force the inner expression to be exactly `x`, so `checked (x + 1)` is not
  a possible parse of `checked (x) + 1` — asserting root `Add` definitively proves the precedence.

### Interaction with the T2.2.3 statement form (important, verified)
`Statement` is a union rule; the engine (every rule is a `TdoppRule`, `Parser.cs:153`) iterates all
alternatives and keeps the **longest** match; on **equal-length Success** matches the **first
alternative wins** (`Parser.cs:299-316`: strictly-longer replaces; equal-length keeps the first
Success). In `Statement`, `ExpressionStatement` (line 767) precedes `CheckedStatement` (line 783).

- `{ checked { ... } }` → only `CheckedStatement` matches (`CheckedExpr` needs `(`, not `{`) →
  `CheckedStatement` (unchanged).
- `{ checked (x = y + z); }` → NOW a tie: `ExpressionStatement` = `checked (x = y + z)` (CheckedExpr) +
  `;` **and** `CheckedStatement` = `checked` + `(x = y + z);`. Equal length → **`ExpressionStatement`
  wins** (first). This is EXACTLY Roslyn's behavior (fact C above) and makes the statement form MORE
  Roslyn-accurate (previously T2.2.3 modeled it as `CheckedStatement` via `CheckedBody = Block |
  ExpressionStatement`, a documented simplification — see grammar line 933). No rule is touched.
- `{ checked; }` → `CheckedExpr` fails (no `(`), no other `Primary` matches `checked` (reserved) →
  `Expression` fails → `ExpressionStatement` fails; `CheckedStatement` also fails → reject (unchanged).

No existing test asserts the `CheckedStatement`/`UncheckedStatement` **Kind** (only clean-parse via
`AssertParses`/`AssertFails`), so the parse-tree shift for `checked (expr);` does not break any test.

## Boundary decisions / deviations
- **`checked (expr);` now parses as `ExpressionStatement(CheckedExpr)`** (was `CheckedStatement` in
  T2.2.3). This matches Roslyn (fact C) and is a fidelity improvement, not a regression. No statement
  rule is modified.
- **`checked`/`unchecked` as bare identifiers are still blocked** (reserved keywords) — consistent with
  the T2.3.1 guard.
- Engine and CsNitra meta-grammar NOT changed (only the C# grammar text + tests + docs).

## Tests written
All in `Tests/CSharpGrammarTests/Cs1ExpressionTests.cs` (new T2.3.4 section). Expression-level via
`Cs1ExpressionTestHelper` (start rule `"Expression"`); statement/declaration context via
`Cs1StatementTestHelper` (start rule `"Block"`). **13 new tests.**

- **POSITIVE (expression, `Cs1ExpressionTests`): 6**
  - `CheckedExpr_AdditiveInner_Succeeds` → `checked (x + y)`.
  - `CheckedExpr_Simple_Succeeds` → `checked (x)`.
  - `CheckedExpr_FullPrecedenceInner_Succeeds` → `checked (x + y * z)`.
  - `UncheckedExpr_MultiplicativeInner_Succeeds` → `unchecked (a * b)`.
  - `UncheckedExpr_NestedChecked_Succeeds` → `unchecked (checked (x))` (nested).
  - `CheckedExpr_InAdditive_Succeeds` → `checked (x) + 1`.
- **POSITIVE (declaration context, `Cs1ExpressionTests` via statement helper): 1**
  - `CheckedExpr_InDeclaration_Succeeds` → `{ int w = checked (x + y); }`.
- **SHAPE: 1**
  - `Precedence_CheckedBindsTighterThanAdditive_Shape` → `checked (x) + 1` → root `Add`, left operand
    `CheckedExpr` (proves primary precedence).
- **Roslyn-derived (statement context): 1**
  - `CheckedExpr_StatementContext_ExpressionStatement` → `{ checked (x + y); }` → `FirstStatementKind`
    == `ExpressionStatement` (Roslyn fact C: `ParseCheckedStatement` → `ParseExpressionStatement`).
- **NEGATIVE (expression): 4**
  - `Invalid_CheckedNoParens_Fails` → `checked x + y` (no parens).
  - `Invalid_CheckedEmptyParens_Fails` → `checked ()` (empty).
  - `Invalid_CheckedUnclosedParen_Fails` → `checked (x` (unclosed).
  - `Invalid_CheckedSemicolon_Fails` → `checked;` (reject).

## Verification
- `dotnet build Nitra.sln --no-incremental` → **Build succeeded, 0 errors** (Debug).
- `dotnet test Tests/CSharpGrammarTests` (fresh build) → **Passed: 766, Failed: 0, Skipped: 3**
  (3 skips are pre-existing `RawString`/`StringLiteral`/`VerbatimString` tests, unrelated to T2.3.4).
  - New T2.3.4 tests: **13**, all green (verified via filtered run `FullyQualifiedName~Checked`
    → **21 passed / 0 failed**: the 13 new tests + the 8 pre-existing at-risk checked-statement tests
    `Checked_ExpressionStatement_Parses`, `Unchecked_ExpressionStatement_Parses`, `Checked_Block_Parses`,
    `Unchecked_Block_Parses`, `Roslyn_CheckedStatement_Parses`, `Roslyn_UncheckedStatement_Parses`,
    `Checked_NothingAfter_Fails`, `Unchecked_NothingAfter_Fails` — all still pass, confirming the
    statement-form tie-break does not break anything).
- Engine (`ExtensibleParser/`) and CsNitra meta-grammar **NOT changed** (only the C# grammar text +
  tests + docs) → `ParserTests` not re-run (per task: only required when engine/meta-grammar changes).
  Confirmed via `git status`: only `Parsers/CSharp/CSharpGrammar/Cs1.grammar`, `Cs1ExpressionTests.cs`,
  `docs/CSharpParserPlan-checklist.md`, and the progress file changed.

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs1.grammar` (Primary: +`CheckedExpr`, +`UncheckedExpr` — lines 719-720)
- `Tests/CSharpGrammarTests/Cs1ExpressionTests.cs` (+13 tests)
- `docs/CSharpParserPlan-checklist.md` (T2.3.4 → `[✅]`)
- `docs/CSharpParserPlan-progressT2.3.4.md` (this file)
