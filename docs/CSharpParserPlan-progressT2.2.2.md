# T2.2.2 — C# 1.0 loop & conditional statements — Progress

## Status: done (build 0 errors / 0 warnings; CSharpGrammarTests 478 passed / 0 failed / 3 pre-existing skips; ParserTests 325 / 2 skips)

## Task
Add the C# 1.0 **loop and conditional statements** (if / while / do-while / for / foreach) to the
grammar in `Parsers/CSharp/CSharpGrammar/Cs1.grammar`. SECOND of three statement sub-points.
T2.2.1 created the central `Statement` union with the simple statements. This sub-point ADDS new
alternatives to the existing `Statement` rule (no remove/reorder of existing ones).

Key challenge: **dangling else** — `if (a) if (b) c; else d;` must bind `else` to the inner if.

## Baseline (before T2.2.2)
- `dotnet build Nitra.sln --no-incremental` → Build succeeded, 0 Warning(s), 0 Error(s).
- `dotnet test Tests/CSharpGrammarTests --no-build` → Passed: 433, Failed: 0, Skipped: 3
  (415 from T2.2.1 + 18 from the T2.3.x expression sub-points that landed in between).

## Roslyn semantics ported (C:\RSDN\roslyn, src/Compilers/CSharp/Portable/Parser/LanguageParser.cs)
- **`ParseIfStatement`** (10013-10076): `if` `(` expr `)` consequence, then `else`? handled via a
  stack. The then-statement (`consequence`, 10026) is parsed FIRST; the `else` (10028-10030) is
  attached to the nearest if. When the then-statement is itself an `if`, the recursive
  `ParseIfStatement` consumes the `else` (its own then-statement `c;` stops at `;`), so the else
  binds to the inner if. `else if` chains are the same loop with `CurrentToken.Kind == IfKeyword`
  (10039-10043).
- **`ParseWhileStatement`** (10454-10464): `while` `(` expr `)` embeddedStatement.
- **`ParseDoStatement`** (9524-9546): `do` embeddedStatement `while` `(` expr `)` `;`.
- **`ParseForStatement`** (9586-9627): `for` `(` for-initialization `;` condition? `;` incrementors? `)`
  embeddedStatement. for-initialization = local-variable-declaration | expression-list?
  (`eatVariableDeclarationOrInitializers`, 9628-9663). condition = expression? (9598-9600).
  incrementors = expression-list? (9607-9609).
- **`ParseForEachStatement`** (9728-9793): `foreach` `(` <type> <identifier> `in` <expr> `)` statement
  (single-variable form, 9788; deconstruction form is C# 7, out of scope).

## Grammar changes (exact rules, `Parsers/CSharp/CSharpGrammar/Cs1.grammar`)
`Statement` union (lines 406-421) — five alternatives APPENDED after `GotoStatement` (417-421); the
ten T2.2.1 alternatives (407-416) are unchanged and in the same order:
```
    | IfStatement
    | WhileStatement
    | DoWhileStatement
    | ForStatement
    | ForEachStatement;
```
New rules (appended after `GotoStatement`, line 458):
- **`IfStatement`** (line 474): `IfStatement = "if" "(" Expression ")" Statement ("else" Statement)?;`
- **`WhileStatement`** (line 476): `WhileStatement = "while" "(" Expression ")" Statement;`
- **`DoWhileStatement`** (line 478): `DoWhileStatement = "do" Statement "while" "(" Expression ")" ";";`
- **`ForStatement`** (line 480): `ForStatement = "for" "(" ForInit? ";" Expression? ";" Expression? ")" Statement;`
- **`ForInit`** (lines 488-490):
  ```
  ForInit =
      | ForVariableDeclaration = Type VariableDeclarator ("," VariableDeclarator)*
      | Expression;
  ```
- **`ForEachStatement`** (line 494): `ForEachStatement = "foreach" "(" Type !ReservedKeyword Identifier "in" Expression ")" Statement;`

`ForVariableDeclaration` reuses the existing `VariableDeclarator` (line 434) and `Type` rules. No
existing rule was modified.

## Dangling-else analysis (how longest-match gives the correct binding)
The standard grammar `IfStatement = "if" "(" Expression ")" Statement ("else" Statement)?` produces
the correct "else binds to nearest if" binding **without any lookahead or tie-break**, for two
independent reasons verified in `ExtensibleParser/Parser.cs`:

1. **Greedy `Optional`.** `ParseOptional` (Parser.cs:543-551) is *not* a longest-match choice — it
   tries the element and, if it matches, always **includes** it (returns at `newPos`); only on failure
   does it return an epsilon `NoneNode`. So the `("else" Statement)?` at the end of an `IfStatement`
   unconditionally consumes a present `else`.

2. **Then-statement parsed before the outer `else` check.** `ParseSeq` (Parser.cs:810-878) walks the
   `IfStatement` sequence left-to-right. The then-`Statement` (element 5) is a `Ref("Statement")`
   resolved by `ParseRule`, which for an `if`-leading then-statement matches the nested `IfStatement`
   **in full** (including its own greedy `else`). Only *after* the then-statement completes does the
   outer if reach its own `("else" Statement)?` — by which point the inner if has already eaten the
   `else`, so the outer one sees nothing and stays a `NoneNode`.

No equal-length tie can occur: a then-statement beginning with the reserved keyword `if` matches only
`IfStatement` (every other `Statement` alternative begins with `{` / an identifier / `;` / `return` /
`throw` / `break` / `continue` / `goto` / `while` / `do` / `for` / `foreach`). The inner then-statement
(`c;` as an `ExpressionStatement`) stops at the first `;` and never reaches the `else`.

Hand-traced (all confirmed by the shape tests below):
- `if (a) if (b) c; else d;` → outer else = none, inner else = some (else on inner if).
- `if (a) { if (b) c; } else d;` → outer else = some, outer then = Block (else on outer if).
- `if (a) if (b) if (c) d; else e;` → else on the innermost if (a,b none; c some).
- `if (a) c; else d;` → outer else = some (simple).

## Boundary decisions / deviations
- **`for`-init supports a local declaration, not just `Expression?`.** The task's formal spec
  `(Expression? ";")` is a simplification, but the required positive test `for (int i = 0; i < n; i++)`
  needs a declaration in the init. C# 1.0 / Roslyn `for-initialization` is
  `local-variable-declaration | expression-list?` (LanguageParser.cs:9628), so `ForInit =
  ForVariableDeclaration | Expression`. The declaration-vs-expression disambiguation is the same
  longest-match pattern as T2.2.1's `LocalVariableDeclaration` vs `ExpressionStatement` (two adjacent
  names ⇒ declaration only; a single expression ⇒ expression only) — no tie.
- **`ForVariableDeclaration` is a new rule, not a refactor of `LocalVariableDeclaration`.** It is the
  declarator-list **without** the trailing `;` (the `;` belongs to the `for` statement). Kept separate
  so no existing rule is touched (lowest-risk; the one-line declarator list is duplicated).
- **`foreach` variable is a true identifier** (`!ReservedKeyword Identifier`), matching the guard on
  `VariableDeclarator`/`TypeName`/labels. This rejects `foreach (int if in arr)`. (The task's formal
  `Type Identifier "in"` is read with this guard for C# 1.0 correctness.)
- **`foreach` single-variable form only.** Deconstruction declarations are C# 7 (Roslyn 9737/9792),
  out of scope for C# 1.0.
- **No `var`** in `for`/`foreach` (C# 3). The Roslyn-derived `for(var a = 0;;)` and
  `foreach(var a in b)` cases were adapted to an identifier type (`T`) or dropped.
- **C# 1.0 purity.** `if`/`while`/`do`/`for`/`foreach` are all C# 1.0. No `default` in `for`-init,
  no tuple patterns, no generics in the `foreach` type (the `Type` rule has none).

## Tests written
All in `Tests/CSharpGrammarTests/`, parsed via `Cs1StatementTestHelper` (start rule `"Block"`).
**45 new tests, all green.**

- **`Cs1LoopConditionalTests.cs`** (new, 36 tests):
  - POSITIVE (24): if (6: then-block, then-expr, else-block, else-expr, else-if-else, nested-then);
    while (3: block, expr, nested); do-while (3: block, expr, nested); for (7: empty-parts,
    declaration-init, expression-init, no-condition, no-update, block, nested); foreach (5:
    predefined-type, identifier-type, string-type, block, indexer-expression).
  - Dangling-else **shape** (5): `DanglingElse_Simple_Parses`, `DanglingElse_BindsToInnerIf_Shape`
    (asserts outer else=none + then=IfStatement + inner else=some), `DanglingElse_BlockBodyBindsToOuterIf_Shape`
    (outer else=some + then=Block), `DanglingElse_ThreeNestedBindsToInnermost_Shape` (a,b none; c some),
    `DanglingElse_ElseIfChain_Parses`.
  - NEGATIVE (7): `do { } while (x)` (missing `;`), `for (int i = 0 i < n; )` (missing `;`),
    `for (int i = 0; i < n; i++` (unclosed `)`), `foreach (int in arr)` (missing identifier),
    `foreach (int if in arr)` (var name is a keyword), `if (a x = 5;` (unclosed `)`),
    `while (x { i++; }` (unclosed `)`).
- **`Cs1RoslynStatementTests.cs`** (9 added, Roslyn-derived; source file:method + adaptation per test):
  - `StatementParsingTests.TestIf` (2012) → `{ if (a) { } }`.
  - `StatementParsingTests.TestIfElse` (2035) → `{ if (a) { } else { } }`.
  - `StatementParsingTests.TestIfElseIf` (2061) → `{ if (a) { } else if (b) { } }`.
  - `StatementParsingTests.TestWhile` (1468) → `{ while (a) { } }`.
  - `StatementParsingTests.TestDoWhile` (1490) → `{ do { } while (a); }`.
  - `StatementParsingTests.TestFor` (1515) → `{ for ( ; ; ) { } }` (empty parts).
  - `StatementParsingTests.TestForWithVariableDeclaration` (1541) → `{ for (T a = 0; ; ) { } }`
    (adapted: `T` identifier type, declaration init).
  - `StatementParsingTests.TestForEach` (1919) → `{ foreach (T a in b) { } }` (adapted: `T` identifier type).
  - `LanguageParser.ParseIfStatement` (10013) → `{ if (a) if (b) c; else d; }` (shape: else on inner if).

Helper additions (`Cs1StatementTestHelper.cs`): `FirstStatementNode`, `IfThenKind` (Elements[4]),
`IfElsePresent` (Elements[5] is SomeNode ⇒ "some" else "none"); `FirstStatementKind` refactored to
delegate to `FirstStatementNode` (same behavior).

## Verification
- `dotnet build Nitra.sln --no-incremental` → **Build succeeded, 0 Warning(s), 0 Error(s)**.
- `dotnet test Tests/CSharpGrammarTests` (fresh build) → **Passed: 478, Failed: 0, Skipped: 3**
  (3 skips are pre-existing `RawString`/`StringLiteral` tests, unrelated to T2.2.2).
  - New T2.2.2 tests: 45 (`Cs1LoopConditionalTests` 36 + `Cs1RoslynStatementTests` 9), all green.
  - Pre-existing 433 CSharpGrammarTests still pass (the change is additive: new rules + alternatives only,
    no existing rule modified).
- `dotnet test Tests/ParserTests` → **Passed: 325, Failed: 0, Skipped: 2** (sanity check; engine +
  CsNitra meta-grammar **NOT changed** — only the C# grammar text + tests).

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs1.grammar` (Statement alternatives + If/While/DoWhile/For/ForInit/ForEach rules)
- `Tests/CSharpGrammarTests/Cs1StatementTestHelper.cs` (FirstStatementNode/IfThenKind/IfElsePresent)
- `Tests/CSharpGrammarTests/Cs1LoopConditionalTests.cs` (new)
- `Tests/CSharpGrammarTests/Cs1RoslynStatementTests.cs` (9 Roslyn-derived loop/conditional cases + using)
- `docs/CSharpParserPlan-progressT2.2.2.md` (this file)
- `docs/CSharpParserPlan-checklist.md` (T2.2.2 → `[✅]`)
