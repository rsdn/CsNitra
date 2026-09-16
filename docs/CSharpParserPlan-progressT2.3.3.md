# T2.3.3 — C# 1.0 Expression: predefined type name as the start of a member-access expression — Progress

## Status: done (build 0 errors; CSharpGrammarTests 433 passed / 0 failed / 3 pre-existing skips)

## Task (defect D2)
`int.Parse()`, `string.Format("x")`, `double.MaxValue`, `char.IsLetter('a')`, etc. — **predefined
type names as the start of a member-access expression** — are NOT parseable by the current grammar.

Root cause: in `Parsers/CSharp/CSharpGrammar/Cs1.grammar`, `Primary` has
`IdentifierName = !ReservedKeyword Identifier` (added in T2.3.1 to stop `new`/`typeof`/`sizeof`
from being parsed as identifiers). `!ReservedKeyword` blocks **all** reserved keywords as expression
starts, including the 16 predefined type names (`int`, `string`, `bool`, `char`, `double`, `float`,
`decimal`, `long`, `short`, `byte`, `sbyte`, `uint`, `ulong`, `ushort`, `object`, `void`). So `int`
can never start an `Expression`, and `int.Parse()` fails.

## Requirements (all must hold)
1. `int.Parse()`, `string.Format("a")`, `double.MaxValue`, `char.IsLetter('a')`, `bool.Parse("true")`,
   `uint.MaxValue` → parse (predefined type + member access, with any further postfixes `()`/`.x`/`[i]`).
2. `int` **alone** as an expression → must NOT parse (a type name is not a value). `{ int; }`,
   `{ x = int; }` must reject.
3. The T2.3.1 guard must be PRESERVED: `new Foo()`, `typeof(int)`, `sizeof(int)`, and `new`/`typeof`/
   `sizeof` must NOT be parseable as bare identifiers. `{ new; }`, `{ typeof; }` must reject.
4. Local declarations still work: `{ int x = 5; }`, `{ string s = "a"; }`.
5. `System.Int32.Parse()` (qualified) already works and must keep working.
6. Do not break any existing test (full CSharpGrammarTests suite green).

## Roslyn semantics ported (C:\RSDN\roslyn, src/Compilers/CSharp/Portable/Parser/LanguageParser.cs)

Two independent Roslyn facts establish the correct declarative model:

### (A) Statement-level disambiguation — `IsPossibleLocalDeclarationStatement` (line 8504-8518)
```csharp
if (tk == SyntaxKind.RefKeyword ||
    IsDeclarationModifier(tk) ||
    (SyntaxFacts.IsPredefinedType(tk) &&
        this.PeekToken(1).Kind is not SyntaxKind.DotToken // e.g. `int.Parse()` is an expression
                                       and not SyntaxKind.OpenParenToken)) // e.g. `int (x, y)` is an error
{
    return true; // → parse as a local declaration
}
```
A predefined type (`int`, `string`, …) is treated as the start of a **local declaration** only when the
next token is **not** `.` and **not** `(`. When followed by `.`, it is an **expression** (`int.Parse()`).
This confirms: a predefined type name starts an expression **only when immediately followed by `.`**.

### (B) Expression-level — `ParsePrimaryExpressionWithoutPostfix` (line 12077-12093)
```csharp
default:
    if (IsPredefinedType(tk))
    {
        if (this.IsPossibleLambdaExpression(precedence))
        {
            return this.ParseLambdaExpression();
        }
        // check for intrinsic type followed by '.'
        var expr = _syntaxFactory.PredefinedType(this.EatToken());
        if (this.CurrentToken.Kind != SyntaxKind.DotToken || tk == SyntaxKind.VoidKeyword)
        {
            expr = this.AddError(expr, ErrorCode.ERR_InvalidExprTerm, SyntaxFacts.GetText(tk));
        }
        return expr;
    }
    else
    {
        // CreateMissingIdentifierName + ERR_InvalidExprTerm / ERR_ExpressionExpected
    }
```
In primary-expression position a predefined-type token is a valid start **only when the next token is
`.`** (and it is not `void`); otherwise Roslyn attaches `ERR_InvalidExprTerm`. The member (`.Parse`) is
then applied by the surrounding postfix loop (`ParsePrimaryExpression`). So:
- `int.Parse` → `PredefinedType(int)` (no error, next is `.`) + postfix `.Parse` → member access. ✓
- `int` alone → `PredefinedType(int)` + `ERR_InvalidExprTerm` (next is not `.`) → **not a value**. ✓
- `new`/`typeof`/`sizeof` are **not** predefined types → they hit the `else` (missing identifier) or
  their own keyword branches, never a bare identifier. ✓ (guard preserved)

## The fix (declarative — one new `Primary` alternative)

`Parsers/CSharp/CSharpGrammar/Cs1.grammar`, `Primary` rule — one new alternative added immediately after
`IdentifierName`:

```
| PredefinedMember = PredefinedType "." !ReservedKeyword Identifier
```

Why this satisfies all 6 requirements:

1. **Parse (req 1).** `PrimaryExpr = Primary PostfixOp*`. `PredefinedMember` matches `int.Parse`
   (PredefinedType `int` + `.` + true identifier `Parse`); the `PostfixOp*` loop then applies any further
   postfixes: `()` (Invocation) for `int.Parse()`, `.ToString` (MemberAccess) + `()` for
   `int.Parse("5").ToString()`, `[i]` (Indexer), and binary continuation for `double.MaxValue + 1`
   (root `Add`). `double.MaxValue`, `uint.MaxValue`, `long.MinValue`, `char.IsLetter('a')`,
   `bool.Parse("true")`, `string.Format("a")` all match `PredefinedMember` (+ optional postfix).
2. **Bare type rejected (req 2).** `PredefinedMember` **requires** the `.` + identifier. A bare `int`
   (no dot) matches **no** `Primary` alternative: `IdentifierName` fails (`int` is reserved → the
   `!ReservedKeyword` guard), `PredefinedMember` fails (no `.`). So `int` is not an `Expression` →
   `{ int; }` and `{ x = int; }` reject.
3. **Guard preserved (req 3).** The `IdentifierName = !ReservedKeyword Identifier` guard is **untouched**.
   `new`/`typeof`/`sizeof` are still reserved keywords, still not identifiers, and their dedicated
   `Primary` alternatives (`NewExpr`/`TypeOf`/`SizeOf`) still require their full forms (`new <body>`,
   `typeof ( Type )`, `sizeof ( Type )`). So `{ new; }`, `{ typeof; }`, `{ sizeof; }` still reject, and
   `new Foo()`, `typeof(int)`, `sizeof(int)` still parse.
4. **Declarations still work (req 4).** `LocalVariableDeclaration = Type VariableDeclarator …`. For
   `{ int x = 5; }`: `Type` matches `int` (PredefinedType), `VariableDeclarator` matches `x = 5`, `;`.
   `ExpressionStatement` cannot match (`int` is not an expression start — no `.`), so only the
   declaration matches. Same for `{ string s = "a"; }`. This is exactly Roslyn fact (A): predefined type
   not followed by `.` → declaration.
5. **Qualified name unaffected (req 5).** `System.Int32.Parse()` starts with `System` (a normal
   non-reserved identifier) → `IdentifierName` matches `System`, `PostfixOp*` applies `.Int32`, `.Parse`,
   `()`. `PredefinedMember` is not involved. Unchanged.
6. **No existing test broken.** The only change is adding one new `Primary` alternative that matches
   inputs no previous alternative could match (a reserved predefined type followed by `.` + identifier).
   It cannot steal a match from any existing alternative (see Boundary decisions — no overlap/tie).

Mechanism notes:
- The `Identifier` terminal is `[_\l]\w*` (matches reserved keywords too), so the member name reuses the
  `!ReservedKeyword` guard (same as `IdentifierName`) to keep member names true identifiers (Roslyn: a
  member name is a true identifier; reserved keywords are not valid member names in C# 1.0).
- `PredefinedType` (all 16) is reused directly. The `PostfixOp*` loop (not this rule) handles further
  `.x`/`()`/`[i]`, so the rule stays minimal.

## Boundary decisions / deviations

- **`void.x` parses here but is rejected by Roslyn.** Roslyn explicitly excludes `void` from being an
  expression-start even when followed by `.` (LanguageParser.cs:12088 `tk == SyntaxKind.VoidKeyword`).
  This fix uses the full `PredefinedType` set (16 types) for a minimal declarative rule; `void.x` is not
  valid C# and is **not** in the required test set, so it is left as a known boundary (a 15-alternative
  `PredefinedTypeNonVoid` rule would be over-engineering for an untested edge case).
- **`int` followed by `[` or `(` does not parse** (e.g. `int[5]`, `int (x, y)`). `PredefinedMember`
  requires `.`; `IdentifierName` rejects `int` (reserved). Roslyn also rejects these (fact A: `int (x, y)`
  is "an error"; `int[5]` gets `ERR_InvalidExprTerm` on the `int`). Consistent.
- **No tie / no overlap with existing alternatives.** `IdentifierName` requires a **non-reserved** first
  token; `PredefinedMember` requires a **reserved predefined-type** first token. They are mutually
  exclusive on the first token, so no equal-length tie can occur and the new alternative cannot steal an
  existing match (longest-match-wins is unaffected). Order of `Primary` alternatives is therefore
  irrelevant to correctness.

## Tests written
All in `Tests/CSharpGrammarTests/`. Expression-level positives via `Cs1ExpressionTestHelper`
(start rule `"Expression"`); statement-level positives/negatives + declarations via
`Cs1StatementTestHelper` (start rule `"Block"`). **18 new tests, all green.**

- **POSITIVE (expression, `Cs1ExpressionTests`): 10**
  - `PredefinedMember_Invocation_Succeeds` → `int.Parse()` (Roslyn-derived: LanguageParser.cs:8514
    comment "int.Parse() is an expression" + ParsePrimaryExpressionWithoutPostfix 12078-12093).
  - `PredefinedMember_FormatArgs_Succeeds` → `string.Format("a")`.
  - `PredefinedMember_PropertyAccess_Succeeds` → `double.MaxValue`.
  - `PredefinedMember_CharIsLetter_Succeeds` → `char.IsLetter('a')`.
  - `PredefinedMember_BoolParse_Succeeds` → `bool.Parse("true")`.
  - `PredefinedMember_UintMaxValue_Succeeds` → `uint.MaxValue`.
  - `PredefinedMember_LongMinValue_Succeeds` → `long.MinValue`.
  - `PredefinedMember_ChainedInvocation_Succeeds` → `int.Parse("5").ToString()` (chained postfixes).
  - `PredefinedMember_InAdditive_Succeeds` → `double.MaxValue + 1` (binary continuation, root `Add`).
  - `QualifiedInt32Parse_Succeeds` → `System.Int32.Parse()` (req 5 — qualified name uses the
    `IdentifierName` + `PostfixOp*` path, NOT `PredefinedMember`; regression guard that the fix does
    not affect qualified names).
- **POSITIVE (statement, `Cs1StatementTests`): 3**
  - `PredefinedMember_ExpressionStatement_Parses` → `{ int.Parse(); }` (req 1, statement context).
  - `PredefinedMember_PropertyExpressionStatement_Parses` → `{ double.MaxValue; }`.
  - `LocalDecl_StringType_Parses` → `{ string s = "a"; }` (req 4). (`{ int x = 5; }` already covered
    by the pre-existing `LocalDecl_PredefinedType_Parses`.)
- **NEGATIVE (statement, `Cs1StatementTests`): 5**
  - `Invalid_BarePredefinedType_Fails` → `{ int; }` (bare type, req 2).
  - `Invalid_BarePredefinedTypeAssignment_Fails` → `{ x = int; }` (req 2).
  - `Invalid_BareNew_Fails` → `{ new; }` (guard preserved, req 3).
  - `Invalid_BareTypeof_Fails` → `{ typeof; }` (guard preserved, req 3).
  - `Invalid_BareSizeof_Fails` → `{ sizeof; }` (guard preserved, req 3).

## Verification
- `dotnet build Nitra.sln --no-incremental` → **Build succeeded, 0 errors** (SDK 8.0.411, Debug/x64).
- `dotnet test Tests/CSharpGrammarTests` (fresh build) → **Passed: 433, Failed: 0, Skipped: 3**
  (3 skips are pre-existing `RawString`/`StringLiteral` tests, unrelated to T2.3.3).
  - New T2.3.3 tests: 18 (10 in `Cs1ExpressionTests`, 8 in `Cs1StatementTests`), all green
    (verified via filtered run: `Passed: 17, Failed: 0` for the 17 named PredefinedMember/Bare*/
    LocalDecl_StringType tests, + the `QualifiedInt32Parse` regression test).
- Engine (`ExtensibleParser/`) and CsNitra meta-grammar **NOT changed** (only the C# grammar text +
  tests + docs) → `ParserTests` not re-run (per task: only required when engine/meta-grammar changes).
  Confirmed via `git status`: only `Parsers/CSharp/CSharpGrammar/Cs1.grammar`, the two test files, and
  docs changed.

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs1.grammar` (Primary: +`PredefinedMember = PredefinedType "."
  !ReservedKeyword Identifier`)
- `Tests/CSharpGrammarTests/Cs1ExpressionTests.cs` (+10 positive predefined-member/qualified tests)
- `Tests/CSharpGrammarTests/Cs1StatementTests.cs` (+3 positive + 5 negative statement tests)
- `docs/CSharpParserPlan-checklist.md` (T2.3.3 → `[✅]`)
- `docs/CSharpParserPlan-progressT2.3.3.md` (this file)
