# T2.2.3 — C# 1.0 complex statements (switch/try/using/lock/checked/unchecked) — Progress

## Status: done (build 0 errors / 0 warnings; CSharpGrammarTests 537 passed / 0 failed / 3 pre-existing skips; ParserTests 325 / 2 skips)

## Task
Add the C# 1.0 **complex statements** (switch / try / using / lock / checked / unchecked) to the
grammar in `Parsers/CSharp/CSharpGrammar/Cs1.grammar`. THIRD (final) of the three statement
sub-points. T2.2.1 created the central `Statement` union (simple statements) and T2.2.2 added
if/while/do-while/for/foreach. This sub-point ADDS new alternatives to the existing `Statement`
rule (no remove/reorder of existing ones).

## Baseline (before T2.2.3)
- `dotnet build Nitra.sln --no-incremental` → Build succeeded, 0 Warning(s), 0 Error(s).
- `dotnet test Tests/CSharpGrammarTests` (fresh build) → Passed: 481, Failed: 0, Skipped: 3
  (478 from T2.2.2 + 3 from T2.3.x expression sub-points that landed in between).

## Roslyn semantics ported (C:\RSDN\roslyn, src/Compilers/CSharp/Portable/Parser/LanguageParser.cs)

- **`ParseSwitchStatement`** (10160-10228): `switch` `(` expression `)` `{` sections `}`.
  Governing expression via `ParseExpressionCore()`; a `ParenthesizedExpression` is absorbed into the
  switch's own parens (10201-10209). C# 1.0: `switch ( Expression ) { ... }` (no tuple form — C# 7).
- **`IsPossibleSwitchSection`** (10230-10234): a section starts at `case`, or `default` (not followed
  by `(` — the `(` case is C# 7 `default ( ... )` patterns, out of scope). In C# 1.0 both are
  reserved keywords, so a section is label-led.
- **`ParseSwitchSection`** (10236-10306): `do { label } while (IsPossibleSwitchSection())` — one or
  more labels (stacked `case 1: case 2:` = ONE section), then `ParseStatements(..., stopOnSwitchSections:
  true)` — statements stop at the next `case`/`default` or `}`. Case label = `case` <constant-expr> `:`
  (`ParseExpressionOrPatternForSwitchStatement`; C# 1.0 → constant, no patterns/`when` — C# 7).
  Default label = `default` `:`.
- **`ParseTryStatement`** (9348-9417): `try` block, then `while (CurrentToken == CatchKeyword)
  ParseCatchClause()`, then `if (CurrentToken == FinallyKeyword) FinallyClause`. **If there is neither
  a catch nor a finally → `ERR_ExpectedEndTry`** (9393-9402, Roslyn synthesizes a missing `finally` for
  recovery but it is a compile error). So `try { }` (no catch/finally) is INVALID. `finally` is at most
  one and comes after `catch*` (so `finally` before `catch`, or two `finally`, are invalid).
- **`ParseCatchClause`** (9424-9481): `catch` + ( `(` `ParseType()` `IsTrueIdentifier()?` `)` )? +
  `ParsePossiblyAttributedBlock()`. The paren-group is **optional** → `catch { }` (catch-all,
  `Declaration == null`) is VALID (confirmed by `StatementParsingTests.TestTryCatchWithNoExceptionDeclaration`,
  line 1282-1303). The identifier is a true identifier and optional (`catch (E) { }` valid).
  The `when` filter (9451-9474) is C# 6 — out of scope for C# 1.0.
- **`ParseUsingStatement`** (10327-10348): `using` `(` (variable-declaration | expression) `)`
  embedded-statement. The decl-vs-expr decision (`ParseUsingExpression`, 10350-10441) is a
  `ScanType`-based heuristic; the task reduces it to the same **longest-match** decl-vs-expr pattern
  used by T2.2.1/T2.2.2 (two adjacent names ⇒ declaration; a single expression ⇒ expression).
  C# 1.0: only the statement form; the `using T a = b;` declaration (C# 8) is out of scope.
- **`ParseLockStatement`** (10101-10111): `lock` `(` expression `)` embedded-statement.
- **`ParseCheckedStatement`** (9507-9522): if `checked`/`unchecked` is followed by `(` → it is an
  **expression statement** (the `checked ( expr )` expression form); otherwise (followed by `{`) it is
  a `CheckedStatement` with a `ParsePossiblyAttributedBlock()`. C# grammar:
  `checked_statement: checked block | checked expression_statement`.

## Meta-grammar constraint (drives the rule shapes)
The CsNitra meta-grammar allows `|` **only between top-level alternatives of a rule**, not inside a
group `( ... )` — `GroupExpressionAst` wraps a single `RuleExpressionAst` (CsNitraParser.cs:103
`new Seq([new Literal("("), new Ref("RuleExpression"), new Literal(")")], "Group")`; the `RuleExpression`
TDOPP rule has no `|` alternative). So the task's `( A | B )` forms are lifted into small
union rules:
- `SwitchLabel = CaseLabel | DefaultLabel`  (for `( CaseLabel | DefaultLabel )+`)
- `TryTail = CatchClauses | FinallyClause`  (for `( CatchClause+ FinallyClause? | FinallyClause )`)
- `CheckedBody = Block | ExpressionStatement` (for `( Block | ExpressionStatement )`)
- `ResourceAcquisition = ResourceDeclaration | Expression` (for the using decl-vs-expr)

A group containing a plain sequence (no `|`) IS expressible and is used for the optional catch
declaration: `("(" Type !ReservedKeyword Identifier? ")")?` (same `( seq )?` shape as
`VariableDeclarator`'s `("=" Expression)?`).

## Grammar changes (exact rules, `Parsers/CSharp/CSharpGrammar/Cs1.grammar`)
`Statement` union (lines 406-427) — six alternatives APPENDED after `ForEachStatement` (421); the
fifteen existing alternatives (407-421) are unchanged and in the same order:
```
    | SwitchStatement
    | TryStatement
    | UsingStatement
    | LockStatement
    | CheckedStatement
    | UncheckedStatement;
```
New rules (appended after `ForEachStatement`, line 500):
- **`SwitchStatement`** (519): `SwitchStatement = "switch" "(" Expression ")" "{" SwitchSection* "}";`
- **`SwitchSection`** (526): `SwitchSection = SwitchLabel+ Statement*;`
- **`SwitchLabel`** (530-532): `SwitchLabel = | CaseLabel | DefaultLabel;`
- **`CaseLabel`** (535): `CaseLabel = "case" Constant ":";`
- **`DefaultLabel`** (538): `DefaultLabel = "default" ":";`
- **`TryStatement`** (543): `TryStatement = "try" Block TryTail;`
- **`TryTail`** (547-549): `TryTail = | CatchClauses = CatchClause+ FinallyClause? | FinallyClause;`
- **`CatchClause`** (554): `CatchClause = "catch" ("(" Type !ReservedKeyword Identifier? ")")? Block;`
- **`FinallyClause`** (557): `FinallyClause = "finally" Block;`
- **`UsingStatement`** (561): `UsingStatement = "using" "(" ResourceAcquisition ")" Statement;`
- **`ResourceAcquisition`** (566-568): `ResourceAcquisition = | ResourceDeclaration = Type VariableDeclarator ("," VariableDeclarator)* | Expression;`
- **`LockStatement`** (571): `LockStatement = "lock" "(" Expression ")" Statement;`
- **`CheckedStatement`** (578): `CheckedStatement = "checked" CheckedBody;`
- **`UncheckedStatement`** (581): `UncheckedStatement = "unchecked" CheckedBody;`
- **`CheckedBody`** (585-587): `CheckedBody = | Block | ExpressionStatement;`

No existing rule was modified. `ResourceDeclaration` reuses the existing `VariableDeclarator` (439) and
`Type` rules (same shape as `ForVariableDeclaration`, 495).

## Disambiguation / section-boundary analysis

### Switch sections (stacked labels + statement boundary)
`SwitchSection = SwitchLabel+ Statement*`. The label run is `OneOrMany(SwitchLabel)`, so a section MUST
start with a `case`/`default` label — `switch (e) { y = 1; }` (a bare statement) fails because
`SwitchLabel+` cannot match an identifier. Stacked labels `case 1: case 2: x = 3;` are ONE section:
`SwitchLabel+` greedily consumes `case 1:` then `case 2:`, then `Statement*` consumes `x = 3;`.
The section's `Statement*` stops before the next `case`/`default` because both are **reserved keywords**
and no `Statement` alternative begins with a reserved keyword (every alternative begins with `{` /
a true identifier / `;` / `return` / `throw` / `break` / `continue` / `goto` / `if` / `while` / `do` /
`for` / `foreach` / `switch` / `try` / `using` / `lock` / `checked` / `unchecked`). Verified by the
shape tests `Switch_StackedLabels_OneSection_Shape` (1 section, 2 labels) and
`Switch_MultipleSections_Shape` (2 sections).

### Try combos (catch*/finally? with the "at least one" constraint)
`TryTail = | CatchClauses = CatchClause+ FinallyClause? | FinallyClause`. The two alternatives are
mutually exclusive by leading token (`catch` vs `finally`) → no equal-length tie. This encodes Roslyn's
`ERR_ExpectedEndTry` (9393-9402): `try { }` (neither) fails because `CatchClause+` needs a `catch` and
`FinallyClause` needs a `finally`. `finally` is at most one and only after `catch*`, so `finally` before
`catch` and two `finally`s both fail (the second `finally` is left dangling and the enclosing block never
closes). All valid combos parse: `catch`, `finally`, `catch finally`, `catch+`, `catch+ finally`.

### Using decl-vs-expr (longest-match)
`ResourceAcquisition = | ResourceDeclaration = Type VariableDeclarator ("," VariableDeclarator)* | Expression`.
Same pattern as `ForInit` (T2.2.2) and `LocalVariableDeclaration` (T2.2.1): a declaration starts with
**two** names in a row (`Type Identifier`), which is never an `Expression`; a single expression is never
`Type Identifier`. Traced (shape tests): `using (int x = Foo())` → `ResourceDeclaration`; `using (obj)`
→ expression (`PrimaryExpr`, the `Expression` sub-alternative a bare identifier matches). No tie.

### Checked/unchecked body
`CheckedBody = | Block | ExpressionStatement`. A `{` starts a `Block`; otherwise it is an
`ExpressionStatement`. `{` is not an expression start, so the two alternatives are mutually exclusive →
no tie. `checked { x = y + z; }` → `Block`; `checked (x = y + z);` → `ExpressionStatement`
(`(x = y + z);` as a parenthesized-expression statement).

### Leading-token uniqueness
Each new `Statement` alternative begins with its own reserved keyword (`switch`/`try`/`using`/`lock`/
`checked`/`unchecked`) that no existing alternative begins with → disambiguation by leading token, no
longest-match tie anywhere. (`using` here is the statement; the file-level `UsingDirective` lives in the
`NamespaceMember` context and never competes.)

## Boundary decisions / deviations
- **`catch { }` (catch-all) IS valid.** Roslyn `ParseCatchClause` (9424) makes the paren-group optional
  (`if (CurrentToken.Kind == OpenParenToken)`, 9433); `Declaration == null` for `catch { }`. Confirmed by
  `StatementParsingTests.TestTryCatchWithNoExceptionDeclaration` (1282-1303). Modeled as the optional
  `("(" Type !ReservedKeyword Identifier? ")")?`.
- **Catch variable name is a true identifier, optional.** `!ReservedKeyword Identifier?` (guard matches
  `IsTrueIdentifier()`, 9440). `catch (E) { }` (no name) and `catch (E e) { }` both parse; `catch (E default)`
  is rejected.
- **`try { }` is INVALID** (no catch/finally) — Roslyn `ERR_ExpectedEndTry` (9396). Encoded by `TryTail`
  requiring at least one `catch` or `finally`.
- **Case label = `Constant` (not a full `Expression`).** Roslyn parses a constant *expression*
  (`ParseExpressionOrPatternForSwitchStatement`) and leaves constant-ness to binding; the task specifies
  `Constant` (the existing rule used for enum values / attribute args). So `case 1:`, `case 'a':`,
  `case "s":`, `case true:`, `case -1:`, `case 0x1F:` parse; `case x:` (identifier) rejects. **Known
  limitation:** compound/qualified constant expressions (`case 1 + 2:`, `case E.Value:`) are NOT covered
  by `Constant` — they would need a full constant-expression rule (deferred; not required by the task's
  verification list).
- **`checked ( expr )` EXPRESSION form is NOT added (T2.3 follow-up gap).** Roslyn `ParseCheckedStatement`
  (9507) treats `checked`/`unchecked` followed by `(` as an *expression statement* of the
  `checked ( expr )` **expression**. The task defers the `checked ( expr )` *expression* (e.g.
  `int w = checked (x + y);`) to T2.3. For T2.2.3 the statement is modeled as `checked` +
  `ExpressionStatement`, so `checked (x = y + z);` parses as `checked` followed by the parenthesized-
  expression statement `(x = y + z);`. Consequence: `int w = checked (x + y);` does NOT parse yet (the
  `checked` expression is absent from `Expression`) — this is the T2.3 gap.
- **Using: statement form only.** The C# 8 `using T a = b;` declaration is out of scope for C# 1.0
  (Roslyn `TestUsingVarWithDeclaration` uses `Regular8`). `await using` (C# 5) also out of scope.
- **C# 1.0 purity.** No case patterns / `when` guards (C# 7), no `default` literal (C# 7), no `catch`
  `when` filter (C# 6), no generics in catch/using types beyond the C# 1.0 `Type` rule.
- **Meta-grammar `|`-in-group limitation.** The task's `( A | B )` forms are not expressible in the
  CsNitra meta-grammar (`|` is only valid between top-level rule alternatives; a group wraps a single
  `RuleExpression`, CsNitraParser.cs:103). Lifted into union rules: `SwitchLabel`, `TryTail`,
  `CheckedBody`, `ResourceAcquisition`. The optional catch declaration uses a plain `( seq )?` group
  (no `|`), which IS expressible (same shape as `VariableDeclarator`'s `("=" Expression)?`).

## Tests written
All in `Tests/CSharpGrammarTests/`, parsed via `Cs1StatementTestHelper` (start rule `"Block"`).
**59 new tests, all green** (48 in `Cs1SwitchTryTests`, 11 in `Cs1RoslynStatementTests`).

- **`Cs1SwitchTryTests.cs`** (new, 48 tests):
  - POSITIVE (33):
    - switch (13): single-case, multiple-cases, stacked-labels, default, mixed, char-case, string-case,
      bool-case, negative-constant, hex-constant, empty, nested-block, break-in-case.
    - try (9): catch, finally, catch+finally, multiple-catches, multiple-catches+finally, catch-all,
      catch-no-name, catch-qualified-type, with-statements.
    - using (5): declaration, expression, declaration-no-init, expression-member, expression-invocation.
    - lock (2): block, expression.
    - checked/unchecked (4): checked-block, checked-expr-stmt, unchecked-block, unchecked-expr-stmt.
  - SHAPE (4, via new helpers): `Using_Declaration_Shape` (→ `ResourceDeclaration`),
    `Using_Expression_Shape` (→ `PrimaryExpr`), `Switch_StackedLabels_OneSection_Shape` (1 section / 2
    labels), `Switch_MultipleSections_Shape` (2 sections).
  - NEGATIVE (11): `try { }` (no catch/finally), `try { } finally { } finally { }` (two finally),
    `try { } finally { } catch (E e) { }` (finally before catch), `case x:` (non-constant),
    `switch (e) { y = 1; }` (non-labelled section), `using (int x Foo()) { }` (malformed),
    `checked;` (nothing after), `unchecked;`, `lock (obj)` (no statement), `switch (e) { case 1: x = 1;`
    (unclosed brace), `try { } catch (E e) x = 1;` (catch without block).
- **`Cs1RoslynStatementTests.cs`** (11 added, Roslyn-derived; source file:method + adaptation per test):
  - `StatementParsingTests.TestSwitch` (2116) → `{ switch (a) { } }`.
  - `StatementParsingTests.TestSwitchWithCase` (2141) → `{ switch (a) { case 1: ; } }` (adapted: `b`
    identifier → constant `1`).
  - `StatementParsingTests.TestSwitchWithMultipleLabelsOnOneCase` (2256) → `{ switch (a) { case 1: case 2: ; } }`
    (adapted: constants; stacked labels = one section).
  - `StatementParsingTests.TestTryCatch` (1223) → `{ try { } catch (T e) { } }`.
  - `StatementParsingTests.TestTryCatchWithNoExceptionDeclaration` (1282) → `{ try { } catch { } }` (catch-all).
  - `StatementParsingTests.TestTryCatchWithMultipleCatchesAndFinally` (1372) →
    `{ try { } catch (T e) { } catch (T2) { } catch { } finally { } }`.
  - `StatementParsingTests.TestUsingWithDeclaration` (2356) → `{ using (T a = b) { } }`.
  - `StatementParsingTests.TestUsingWithExpression` (2334) → `{ using (a) { } }`.
  - `StatementParsingTests.TestLock` (2095) → `{ lock (a) { } }`.
  - `StatementParsingTests.TestChecked` (1417) → `{ checked { } }`.
  - `StatementParsingTests.TestUnchecked` (1434) → `{ unchecked { } }`.

Helper additions (`Cs1StatementTestHelper.cs`): `UsingResourceKind` (UsingStatement `Elements[2].Kind`),
`SwitchSectionCount` (SwitchStatement `Elements[5]` → section count), `SwitchFirstSectionLabelCount`
(first section's `SwitchLabel+` → label count).

## Verification
- `dotnet build Nitra.sln --no-incremental` → **Build succeeded, 0 Warning(s), 0 Error(s)**.
- `dotnet test Tests/CSharpGrammarTests` (fresh build) → **Passed: 537, Failed: 0, Skipped: 3**
  (3 skips are pre-existing `RawString`/`StringLiteral` tests, unrelated to T2.2.3).
  - New T2.2.3 tests: 59 (`Cs1SwitchTryTests` 48 + `Cs1RoslynStatementTests` 11), all green.
  - Pre-existing 481 CSharpGrammarTests still pass (the change is additive: new rules + alternatives only,
    no existing rule modified).
- `dotnet test Tests/ParserTests` → **Passed: 325, Failed: 0, Skipped: 2** (sanity check; engine +
  CsNitra meta-grammar **NOT changed** — only the C# grammar text + tests).

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs1.grammar` (Statement alternatives + Switch/Try/Using/Lock/Checked rules)
- `Tests/CSharpGrammarTests/Cs1StatementTestHelper.cs` (shape helpers)
- `Tests/CSharpGrammarTests/Cs1SwitchTryTests.cs` (new)
- `Tests/CSharpGrammarTests/Cs1RoslynStatementTests.cs` (Roslyn-derived cases)
- `docs/CSharpParserPlan-progressT2.2.3.md` (this file)
- `docs/CSharpParserPlan-checklist.md` (T2.2.3 → `[✅]`)
