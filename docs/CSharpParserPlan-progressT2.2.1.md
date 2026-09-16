# T2.2.1 — C# 1.0 `Block` + simple statements — Progress

## Status: done (build 0 errors / 0 warnings; CSharpGrammarTests 415 passed / 0 failed / 3 pre-existing skips)

## Task
Add the C# 1.0 `Block` and **simple statements** to the grammar in
`Parsers/CSharp/CSharpGrammar/Cs1.grammar`. FIRST of three statement sub-points
(T2.2.1 simple → T2.2.2 loops/conditional → T2.2.3 switch/try/using/lock/checked).
Define the central `Statement` rule with the simple statements; later sub-points ADD
more alternatives to it.

## Baseline
- `dotnet build Nitra.sln --no-incremental` → Build succeeded, 0 Warning(s), 0 Error(s).
- `dotnet test Tests/CSharpGrammarTests --no-build` → Passed: 360, Failed: 0, Skipped: 3.

## Roslyn semantics ported (C:\RSDN\roslyn, src/Compilers/CSharp/Portable/Parser/LanguageParser.cs)

- **Dispatch** — `ParseStatement`/`ParseStatementCore` (LanguageParser.cs:8184-8460). The
  `switch (this.CurrentToken.Kind)` (8298-8353) routes by leading token:
  - `BreakKeyword` → `ParseBreakStatement` (8303); `ContinueKeyword` → `ParseContinueStatement` (8305);
    `GotoKeyword` → `ParseGotoStatement` (8320); `ReturnKeyword` → `ParseReturnStatement` (8329);
    `ThrowKeyword` → `ParseThrowStatement` (8334); `OpenBraceToken` → `ParseBlock` (8345);
    `SemicolonToken` → `EmptyStatement` (8347); `IdentifierToken` → `TryParseStatementStartingWithIdentifier`
    (8349) then `ParseStatementCoreRest` (8355).
- **`ParseBlock`** (9098-9117): `EatToken(OpenBrace)`, `ParseStatements` (loop until `CloseBrace`/EOF,
  9141-9157), `EatToken(CloseBrace)` → `Block(open, statements, close)`. C# 1.0: `block: { statement* }`.
- **`ParseLabeledStatement`** (10466-10477): `ParseIdentifierToken()` (a **true** identifier),
  `EatToken(ColonToken)`, `ParsePossiblyAttributedStatement() ?? EmptyStatement`. Guard
  `IsPossibleLabeledStatement` (8493-8496): `PeekToken(1) == ColonToken && IsTrueIdentifier()`.
  → `label: Statement`, label = non-keyword identifier.
- **Local declaration** — `ParseLocalDeclarationStatement` (10482-10581): parses modifiers +
  `ParseLocalDeclaration` (type + variable declarators, 10517-10527) then
  `LocalDeclarationStatement(..., VariableDeclaration(type, variables), EatToken(Semicolon))`
  (10568-10574). `ParseVariableDeclarator` (5481-5733): `name` (identifier) + optional
  `EqualsValueClause` (`=` + initializer, 5704-5709/5733). → `Type VariableDeclarator ("," VariableDeclarator)* ";"`,
  `VariableDeclarator = Identifier ("=" Expression)?`.
- **`ParseBreakStatement`** (9330-9337) / **`ParseContinueStatement`** (9339-9346): `break`/`continue`
  + `EatToken(Semicolon)`. (Modern Roslyn also tolerates an optional identifier for error recovery,
  9335/9344 — NOT a C# 1.0 feature; see Boundary decisions.)
- **`ParseGotoStatement`** (9947-9978): `goto` + ( `case` → `GotoCaseStatement` + `ParseExpressionCore()`
  | `default` → `GotoDefaultStatement` | else → `GotoStatement` + `ParseIdentifierName()` ) + `EatToken(Semicolon)`.
- **`ParseReturnStatement`** (10113-10121): `return` + ( `CurrentToken != Semicolon` ? `ParsePossibleRefExpression()` : null ) + `;`.
  C# 1.0: no `ref` return (CS7) → `Expression?`.
- **`ParseThrowStatement`** (10308-10316): `throw` + ( `CurrentToken != Semicolon` ? `ParseExpressionCore()` : null ) + `;`.
  `throw;` (rethrow) is C# 1.0.

## Statement-disambiguation approach (declarative, longest-match)

`Statement` is a flat union (no TDOPP precedence — it has no operator levels, only prefix alternatives).
The declaration-vs-expression ambiguity is resolved by the engine's **longest-match-wins**, because the two
candidates are structurally mutually exclusive at the "two adjacent names" boundary:

- A **local declaration** starts `Type Identifier …` — **two** name tokens in a row. Two adjacent
  identifiers are never a valid C# expression, so `ExpressionStatement` (`Expression ";"`) **cannot** match a
  declaration.
- An **expression statement** starts with a single expression. A lone expression is never `Type Identifier`
  (two names), so `LocalVariableDeclaration` cannot match it.

Traced cases (all verified by tests):
- `int x = 5;` → declaration. `int` is a reserved keyword → not an expression start → `ExpressionStatement`
  fails; only `LocalVariableDeclaration` matches.
- `X x = 5;` → declaration. `X x` is not an expression (two names) → `ExpressionStatement` fails (`Expression`
  = `X`, then expects `;` but sees `x`); only `LocalVariableDeclaration` matches.
- `x = 5;` → expression statement. After `Type=x`, the next token is `=`, not an identifier →
  `VariableDeclarator` fails → declaration fails; only `ExpressionStatement` matches.
- `x.y = 5;` → expression statement. `Type` may be `x` (then `.` stops the declarator) or the qualified name
  `x.y` (then `=` stops it) → declaration fails; `Expression` = `x.y = 5` → `ExpressionStatement` matches.

The labeled statement (`Identifier ":" Statement`) and the goto forms are unambiguous by their leading token /
the `:` / `case` / `default` keywords, so no tie-break is needed there. No alternative pair produces an
equal-length match, so the engine's "first wins a true tie" rule is never exercised.

## Grammar changes (exact rules, `Parsers/CSharp/CSharpGrammar/Cs1.grammar`)

All appended after `ArgumentList` (line 377). No existing rule was modified.

- **`Block`** (line 394): `Block = "{" Statement* "}";`
- **`Statement`** (lines 396-406) — the central union rule; a flat `Rule` of anonymous
  alternatives referencing the sub-rules below (easy to append to in T2.2.2/T2.2.3):
  ```
  Statement =
      | Block
      | LabeledStatement
      | EmptyStatement
      | ExpressionStatement
      | LocalVariableDeclaration
      | ReturnStatement
      | ThrowStatement
      | BreakStatement
      | ContinueStatement
      | GotoStatement;
  ```
- **`LabeledStatement`** (line 410): `LabeledStatement = !ReservedKeyword Identifier ":" Statement;`
- **`EmptyStatement`** (line 413): `EmptyStatement = ";";`
- **`ExpressionStatement`** (line 416): `ExpressionStatement = Expression ";";`
- **`LocalVariableDeclaration`** (line 420): `LocalVariableDeclaration = Type VariableDeclarator ("," VariableDeclarator)* ";";`
- **`VariableDeclarator`** (line 424): `VariableDeclarator = !ReservedKeyword Identifier ("=" Expression)?;`
- **`ReturnStatement`** (line 427): `ReturnStatement = "return" Expression? ";";`
- **`ThrowStatement`** (line 430): `ThrowStatement = "throw" Expression? ";";`
- **`BreakStatement`** (line 433): `BreakStatement = "break" ";";`
- **`ContinueStatement`** (line 436): `ContinueStatement = "continue" ";";`
- **`GotoStatement`** (lines 440-443) — three named alternatives (a group `( A | B | C )` is not
  expressible in the CsNitra meta-grammar — `|` is only valid between top-level alternatives):
  ```
  GotoStatement =
      | GotoLabel   = "goto" !ReservedKeyword Identifier ";"
      | GotoCase    = "goto" "case" Constant ";"
      | GotoDefault = "goto" "default" ";";
  ```

`Statement` is **not** a TDOPP rule (no operator levels, only prefix alternatives); its recursion
(via `LabeledStatement` and `Block`) is mutual and well-founded (each consumes input before recursing),
so `BuildTdoppRulesInternal` classifies every alternative as a prefix (none has `Ref("Statement")` as its
first element).

## Boundary decisions / deviations

- **`Block` IS a `Statement` alternative.** The task lists `Block = "{" Statement* "}"` in scope and the
  test list requires **nested blocks** (`{ { x = 5; } }`), which only parse if a block is a statement. C# spec:
  `statement: block | … | labeled_statement | …`. So `Statement` = the simple statements **plus** `Block`.
  (The task's "only the simple statements" is read as "not the T2.2.2/T2.2.3 loop/conditional/switch/try
  statements"; `Block` is the compound-statement container, not one of those.)
- **Identifier guards.** A label / goto-label / variable name is a **true identifier** (not a keyword) —
  modeled as `!ReservedKeyword Identifier` (same guard as `Primary`'s `IdentifierName`, Cs1.grammar:333).
  This also prevents a `default`/`case`-as-identifier tie in the goto alternatives.
- **`goto case` takes a `Constant`, not a full `Expression`.** Roslyn parses `ParseExpressionCore()`
  (LanguageParser.cs:9963) and leaves the constant-ness to binding; C# 1.0 requires a constant-expression
  case label, and the task specifies `Constant`. So `goto case 5;` parses, `goto case x;` (identifier) does not.
- **`break;` / `continue;` have NO label.** The task specifies `"break" ";"` / `"continue" ";"`. Modern Roslyn
  tolerates an optional identifier (9335/9344) purely for error recovery; C# 1.0 has no labeled break/continue.
  So `break x;` is a negative test.
- **`return`/`throw` use `Expression?`.** Matches Roslyn's `CurrentToken != Semicolon ? … : null`
  (10119/10314). No `ref` return (CS7) — `Expression?` is the C# 1.0 form.
- **`int.Parse()` is NOT parseable (pre-existing limitation, not a T2.2.1 regression).** Roslyn's
  `IsPossibleLocalDeclarationStatement` (LanguageParser.cs:8514) treats `int.Parse()` as an expression
  (predefined type + `.`). But the current grammar's `Primary` identifier has the guard
  `IdentifierName = !ReservedKeyword Identifier` (Cs1.grammar:333, added in T2.3.1 to keep
  `typeof(int, int)` from parsing as an invocation), so a reserved type (`int`) cannot start an
  `Expression`. Hence `int.Parse();` matches neither `ExpressionStatement` (int is not an expression
  start) nor `LocalVariableDeclaration` (`.` is not a declarator name) → the parse fails. This is a
  pre-existing expression-grammar limitation, out of scope for T2.2.1; the Roslyn-derived test for it was
  dropped (documented in `Cs1RoslynStatementTests.cs`). The declaration side of that Roslyn disambiguation
  (`int x = 2;` → declaration) IS covered (`Roslyn_PredefinedTypeDeclaration_Parses`).

## Tests written

All in `Tests/CSharpGrammarTests/`, parsed via the new `Cs1StatementTestHelper` (start rule `"Block"` —
the input includes the outer `{ }`; the C# 1.0 grammar has no method body yet, T2.1). 55 new tests, all green.

- **POSITIVE: 45**
  - `Cs1StatementTests.cs` — 31:
    - empty/expression (5): `;`, `x = 5;`, `x.y = 5;`, `Foo();`, `x = Foo(a + b);`.
    - local declaration (6): `int x = 5;`, `X x = 5;`, `int x;`, `int[] x = new int[3];`,
      `int a = 1, b = 2;`, `int a, b;`.
    - return/throw (4): `return;`, `return x;`, `throw;`, `throw ex;`.
    - break/continue (2): `break;`, `continue;`.
    - goto (3): `goto l;`, `goto case 5;`, `goto default;`.
    - labeled (3): `l: x = 5;`, `l: return;`, `l: { x = 5; }`.
    - block (3): `{ }`, `{ { x = 5; } }` (nested), `{ x = 5; y = 3; }` (multiple).
    - disambiguation **shape** (5, assert the winning alternative via `FirstStatementKind`):
      `int x = 5;`→`LocalVariableDeclaration`, `X x = 5;`→`LocalVariableDeclaration`,
      `x = 5;`→`ExpressionStatement`, `x.y = 5;`→`ExpressionStatement`, `Foo();`→`ExpressionStatement`.
  - `Cs1RoslynStatementTests.cs` — 14 (Roslyn-derived, see below).
- **NEGATIVE: 10** (`Cs1StatementTests.cs`):
  - malformed: unclosed block `{ x = 5;`, `int = 5;` (no name), `int x = 5 }` (no `;`),
    `return = ;`, `goto;` (no target).
  - C# 1.0 purity: `break x;`, `continue x;` (no labeled break/continue), `goto case x;` (case must be
    a constant), `return: x = 5;` (label not a keyword), `int return;` (var name not a keyword).

Roslyn-derived cases (source file:method + adaptation, documented per test):
- `StatementParsingTests.TestEmptyStatement` → `{ ; }`.
- `StatementParsingTests.TestLabeledStatement` → `{ label: ; }`.
- `StatementParsingTests.TestBreakStatement` → `{ break; }`.
- `StatementParsingTests.TestContinueStatement` → `{ continue; }`.
- `StatementParsingTests.TestGotoStatement` → `{ goto label; }`.
- `StatementParsingTests.TestGotoCaseStatement` → `{ goto case 5; }` (adapted: Roslyn parses an
  expression; C# 1.0 requires a constant → `5`).
- `StatementParsingTests.TestGotoDefault` → `{ goto default; }`.
- `StatementParsingTests.TestReturn` → `{ return; }`.
- `StatementParsingTests.TestReturnExpression` → `{ return a; }`.
- `StatementParsingTests.TestThrow` → `{ throw; }`.
- `StatementParsingTests.TestThrowExpression` → `{ throw a; }`.
- `LanguageParser.IsPossibleLocalDeclarationStatement` (8513) → `{ int x = 2; }` (shape: declaration).
- `LanguageParser.ParseLocalDeclarationStatement` (10568-10574) → `{ int x = 5; }`.
- `LanguageParser.ParseVariableDeclarators` (5294) → `{ int a = 1, b = 2; }`.

## Verification
- `dotnet build Nitra.sln --no-incremental` → **Build succeeded, 0 Warning(s), 0 Error(s)**.
- `dotnet test Tests/CSharpGrammarTests` (fresh build) → **Passed: 415, Failed: 0, Skipped: 3**
  (3 skips are pre-existing `RawString`/`StringLiteral` tests, unrelated to T2.2.1).
  - New T2.2.1 tests: 55 (`Cs1StatementTests` 41 + `Cs1RoslynStatementTests` 14), all green.
  - Pre-existing 360 CSharpGrammarTests still pass (the change is additive: new rules only, no existing
    rule modified).
- `dotnet test Tests/ParserTests --no-build` → **Passed: 325, Failed: 0, Skipped: 2** (sanity check;
  engine + CsNitra meta-grammar **NOT changed** — only the C# grammar text + tests).

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs1.grammar` (Block + Statement + simple-statement rules)
- `Tests/CSharpGrammarTests/Cs1StatementTestHelper.cs` (new)
- `Tests/CSharpGrammarTests/Cs1StatementTests.cs` (new)
- `Tests/CSharpGrammarTests/Cs1RoslynStatementTests.cs` (new)
- `docs/CSharpParserPlan-progressT2.2.1.md` (this file)
- `docs/CSharpParserPlan-checklist.md` (T2.2.1 → `[✅]`)
