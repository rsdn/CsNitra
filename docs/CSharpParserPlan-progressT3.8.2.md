# T3.8.2 — C# 8.0 using declarations

Status: DONE — build green, all tests pass (CSharpGrammarTests 1229/0/3, ParserTests 325/0/2).

## Goal
Add C# 8.0 **using declarations** to the EXISTING `Parsers/CSharp/CSharpGrammar/Cs8.grammar` (which
already has the switch-expression rules from T3.8.1):
- **Using declaration**: `using FileStream f = new FileStream("a.txt");` — a `using` + type +
  identifier + `=` + expression + `;` (automatic resource disposal at the end of the scope). A new
  STATEMENT form.
- **Using declaration with `var`**: `using var f = new FileStream("a.txt");` — a `using` + `var` +
  identifier + `=` + expression + `;`.

CS8 only. The version-purity boundary is v7 rejects / v8 accepts. The `using` STATEMENT
(`using ( ... ) ...`, C# 1.0) must stay green at v1–v7 (no regression).

## Cs1 structure found (Parsers/CSharp/CSharpGrammar/Cs1.grammar)
- **`Statement`** (Cs1.grammar:780-801): a union of statement forms. `UsingStatement` is at line 798.
  The using declaration is a new STATEMENT form → re-declare `Statement` in Cs8 to add an alternative.
- **`UsingStatement`** (Cs1.grammar:935): `UsingStatement = "using" "(" ResourceAcquisition ")" Statement;`
  (C# 1.0 — only the statement form; the using-declaration `using T a = b;` is C# 8, out of scope here).
- **`ResourceAcquisition`** (Cs1.grammar:940-942): `ResourceDeclaration = Type VariableDeclarator
  ("," VariableDeclarator)* | Expression`.
- **`LocalVariableDeclaration`** (Cs1.grammar:815): `LocalVariableDeclaration = Type VariableDeclarator
  ("," VariableDeclarator)* ";"`. The using declaration is similar, but with a leading `using` keyword
  and a REQUIRED initializer.
- **`VariableDeclarator`** (Cs1.grammar:819): `VariableDeclarator = !ReservedKeyword Identifier
  ("=" Expression)?` (the initializer is OPTIONAL here — the using declaration REQUIRES it).
- **`ExpressionStatement`** (Cs1.grammar:811): `ExpressionStatement = Expression ";"`.
- **`LabeledStatement`** (Cs1.grammar:805): `LabeledStatement = !ReservedKeyword Identifier ":" Statement`.
- **`ReservedKeyword`** (Cs1.grammar:537-618): `using` IS reserved (line 614). So it is not an
  IdentifierName and no other Statement/Expression/Primary alternative starts with it.
- **`Type`** (Cs1.grammar:508-512): `PredefinedType | QualifiedName | PointerType | ArrayType`.
  `TypeName` (Cs1.grammar:23) = `!ReservedKeyword Identifier`.
- **`var` is reserved at v2+** (Cs2.grammar:186-187 appends `"var"` to the merged ReservedKeyword).
  So at v8 `var` is NOT a `TypeName`/`Type` — the `Type | "var"` union is cleanly mutually exclusive.
- **`NewExpr`** (Cs1.grammar:709): `NewExpr = "new" NewBody`; `ObjectCreation = NewBaseType
  ArgumentList` (745) — so `new FileStream("a.txt")` parses as a Primary (a NewExpr).

## Roslyn references (C:\RSDN\roslyn, main, src/Compilers/CSharp\Portable)
- **Structure** — `ParseLocalDeclarationStatement` (Parser/LanguageParser.cs:10482-10579): a using
  declaration is a `LocalDeclarationStatement` with a `usingKeyword` (and optional `awaitKeyword`).
  If the current token is `UsingKeyword` it is eaten (10491-10495), then modifiers + type + variables
  + Semicolon. Form: `[await] using` + modifiers? + type + variables + `;`. The task's form is the
  single-declarator-with-initializer case (`using T id = expr ;` / `using var id = expr ;`).
- **Version gate** — `IDS_FeatureUsingDeclarations` (Errors/MessageID.cs:182, `// semantic check`
  at :620), checked in `Binder/UsingStatementBinder.cs:98` via `CheckFeatureAvailability` — a BINDER
  (semantic) check, C# 8.0. The PARSER accepts the form wherever the rule exists (CS8 only, per the
  plan's decision).
- **Version-purity test** — `UsingDeclarationTests.cs:830` (UsingDeclarationsWithLangVer7_3): `using
  IDisposable x = null;` gives `CS8652` at C# 7.3 ("feature 'using declarations' is not available"),
  and parses cleanly at C# 8.0.
- **Concrete syntax forms** — `IOperationTests_IUsingStatement.cs:7967` (`using var c = new C();`),
  :8406 (`using C c = new C();`), `UsingDeclarationTests.cs:838` (`using IDisposable x = null;`).
- **No regression** — the `using` STATEMENT (`using ( ... ) ...`) is C# 1.0 (Roslyn
  ParseUsingStatement, LanguageParser.cs:10327) and is the UNCHANGED Cs1 `UsingStatement`.

## Approach (the using-declaration statement rule)
Re-declare `Statement` in Cs8 (append, T0.3 merge) to add a using-declaration alternative, plus a
named-union helper for the type-or-`var` position:

```
Statement =
    | UsingDeclaration = "using" UsingDeclarationType !ReservedKeyword Identifier "=" Expression ";";

UsingDeclarationType =
    | Type
    | "var";
```

- **A new STATEMENT form** (mirrors the T3.6.3 local-function statement re-declaration pattern): the
  using declaration is a statement inside a block, not an expression or a member.
- **`UsingDeclarationType` is a named union** (the meta-grammar forbids `|` inside a group `(...)`):
  a real `Type` OR the `"var"` keyword. `"var"` is reserved at v2+ (Cs2:186-187), so `Type` (whose
  `TypeName = !ReservedKeyword Identifier`) NEVER matches `"var"` → the two alternatives are mutually
  exclusive (no equal-length tie).
- **The initializer is REQUIRED** (`"=" Expression`), unlike the Cs1 `VariableDeclarator` (where the
  `("=" Expression)?` is optional). A using declaration without an initializer is malformed (Roslyn
  reports it; this grammar rejects it — see the v8 malformed test).
- **A single declarator** (`!ReservedKeyword Identifier "=" Expression`) — Roslyn's using declaration
  has exactly one variable (multiple declarators is a binder error); the task's form is single.

## Mutual-exclusivity hand-traces
The using declaration is a `Statement` alternative. It starts with the reserved `using` keyword. The
disambiguating REQUIRED-new-construct is the `using` + (type|var) + identifier + `=` + expression + `;`
form (a type/var, not `(`, after `using`).

### vs `UsingStatement` (`"using" "(" ResourceAcquisition ")" Statement`, Cs1:935)
- `using (var f = new X()) { }` (using STATEMENT): after `using`, the next token is `(`.
  `UsingDeclaration` → `UsingDeclarationType`: `Type` fails on `(` (not a type start); `"var"` fails
  on `(`. → `UsingDeclaration` FAILS. `UsingStatement` → `"using" "(" ...` MATCHES. SOLE match.
- `using FileStream f = new FileStream("a.txt");` (using DECLARATION): after `using`, the next token
  is `FileStream` (an identifier). `UsingStatement` → `"("` expected but `FileStream` found → FAILS.
  `UsingDeclaration` → `Type=FileStream`, `f`, `=`, `new FileStream("a.txt")`, `;` MATCHES. SOLE match.
- The two are mutually exclusive: after `using`, a token is either `(` (statement) or a type/var
  (declaration), never both. No equal-length tie.

### vs `LocalVariableDeclaration` (`Type VariableDeclarator ("," VariableDeclarator)* ";"`)
- `using FileStream f = ...`: `LocalVariableDeclaration` → `Type` = QualifiedName = TypeName =
  `!ReservedKeyword Identifier`. The first token `using` is RESERVED → TypeName FAILS → `Type` FAILS
  → `LocalVariableDeclaration` FAILS. `UsingDeclaration` → SOLE match.

### vs `ExpressionStatement` (`Expression ";"`)
- `using ...`: `using` is reserved → not an expression start (no `Expression` alternative starts with
  a reserved keyword) → `ExpressionStatement` FAILS. `UsingDeclaration` → SOLE match.

### vs `LabeledStatement` (`!ReservedKeyword Identifier ":" Statement`)
- `using ...`: `using` is reserved → `!ReservedKeyword` FAILS → `LabeledStatement` FAILS.
  `UsingDeclaration` → SOLE match.

### vs every other Statement alternative
- `using` is a reserved keyword (Cs1 ReservedKeyword:614), so it is NOT an IdentifierName and NO other
  `Statement` alternative starts with it. `UsingDeclaration` is the sole match for a `using`-leading
  declaration form.

### Within `UsingDeclarationType` (`Type | "var"`)
- `using var f = ...`: `var` is reserved (v2+) → `Type` FAILS; `"var"` MATCHES. Sole match.
- `using int f = ...` / `using FileStream f = ...`: `"var"` FAILS; `Type` MATCHES. Sole match.

## Version-purity results (verified empirically with a scratch test, then deleted)
- `class C { void M() { using FileStream f = new FileStream("a.txt"); } }` (using DECLARATION):
  - **v7**: REJECT. `UsingDeclaration` is absent (Cs8-only). `UsingStatement` FAILS (no `(` after
    `using`); `LocalVariableDeclaration`/`ExpressionStatement`/`LabeledStatement` FAIL (the reserved
    `using` is not a Type/expression-start/identifier). No other Statement starts with `using` →
    parse failure.
  - **v8**: ACCEPT. `UsingDeclaration` is present and matches.
- `class C { void M() { using var f = new FileStream("a.txt"); } }` (using DECLARATION, `var`):
  - **v8**: ACCEPT (`Type` FAILS on the reserved `var`; `"var"` matches). **v7**: REJECT.
- `class C { void M() { using int x = 5; } }` (using DECLARATION, predefined type): **v8** ACCEPT,
  **v7** REJECT.
- `class C { void M() { using FileStream f; } }` (using DECLARATION, missing initializer): **v8**
  REJECT (the `UsingDeclaration` REQUIRES `=` Expression after the identifier; after `f` the next
  token is `;`).
- `class C { void M() { using (int x = Foo()) { } } ... }` (using STATEMENT, concrete type):
  - **v1 and v7**: ACCEPT (the Cs1 `UsingStatement` is untouched — no regression).
- `class C { void M() { using (var f = new X()) { } } class X { } }` (using STATEMENT, `var`):
  - **v1**: ACCEPT (`var` is NOT reserved at v1 → a `ResourceDeclaration` whose Type is `var`).
  - **v2–v7**: REJECT (a PRE-EXISTING limitation — see Deviation D1; NOT a regression from T3.8.2,
    which does not touch `UsingStatement`/`ResourceAcquisition`).

## Tests
`Tests/CSharpGrammarTests/Cs8UsingDeclarationTests.cs` (CRLF + UTF-8 BOM) — **11 tests, all green**.
Positives use `CreateParser(8)`; version-purity negatives use `CreateParser(7)`.
- POSITIVE (v8, 3): `UsingDeclaration_Basic_Succeeds` (`using FileStream f = new FileStream("a.txt");`),
  `UsingDeclaration_Var_Succeeds` (`using var f = new FileStream("a.txt");`),
  `UsingDeclaration_SimpleType_Succeeds` (`using int x = 5;`).
- POSITIVE (v1+v7, no-regression, 2): `UsingStatement_ConcreteType_ParsesAtV1` /
  `UsingStatement_ConcreteType_ParsesAtV7` (`using (int x = Foo()) { }` — the pre-existing
  Cs1SwitchTryTests form; the concrete-type using statement parses at v1-v7).
- POSITIVE (v1, no-regression, 1): `UsingStatement_Var_ParsesAtV1` (`using (var f = new X()) { }` —
  the task's no-regression form; parses at v1 where `var` is not reserved; see Deviation D1).
- NEGATIVE (v7, version-purity, 1): `UsingDeclaration_RejectedAtV7` (the using declaration).
- NEGATIVE (v8, malformed, 1): `UsingDeclaration_MissingInitializer_Rejected` (`using FileStream f;`).
- POSITIVE (v8, Roslyn-derived, 3): `UsingDeclaration_ExplicitType_Roslyn_Succeeds`
  (`using C c = new C();`, IOperationTests_IUsingStatement.cs:8406),
  `UsingDeclaration_NullInitializer_Roslyn_Succeeds` (`using D x = null;`, UsingDeclarationTests.cs:838),
  `UsingDeclaration_CoexistsWithUsingStatement_Roslyn_Succeeds` (a using declaration AND a using
  statement in the same block — the two `using`-leading forms are mutually exclusive).

## Verification (all green)
- [x] `dotnet build Nitra.sln --no-incremental` → **Build succeeded, 0 errors / 0 warnings**.
- [x] `dotnet test Tests/CSharpGrammarTests` (fresh build) → **1229 passed, 0 failed, 3 skipped,
      1232 total**. Baseline before T3.8.2 (after T3.8.1): 1218 passed / 3 skipped / 1221 total.
      Delta = **+11** (all new `Cs8UsingDeclarationTests`). All pre-existing Cs1/Cs2/Cs3/Cs4/Cs5/Cs6/
      Cs7/Cs11 tests remain green (no Cs1–Cs7/Cs11 modification).
- [x] `dotnet test Tests/ParserTests` (regression) → **325 passed, 0 failed, 2 skipped, 327 total**
      — matches the T3.8.1 baseline (engine + meta-grammar unchanged).
- [x] `Cs8UsingDeclarationTests` → 11/11.

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs8.grammar` (using-declaration rules: `UsingDeclaration` Statement
  alternative + `UsingDeclarationType` named union).
- `Tests/CSharpGrammarTests/Cs8UsingDeclarationTests.cs` (new).
- `docs/CSharpParserPlan-progressT3.8.2.md` (this file).
- `docs/CSharpParserPlan-checklist.md` (T3.8.2 `[~]` → `[✅]`).

## Deviations / boundary decisions
- **D1 — The task's no-regression form `using (var f = new X()) { }` parses ONLY at v1, not v2–v7.**
  `var` is reserved at v2+ (Cs2.grammar:186-187), and the Cs1 `ResourceAcquisition` (Cs1.grammar:940-
  942) has only two alternatives — `ResourceDeclaration` (Type-based) and `Expression` — with NO `var`
  alternative. So at v2–v7, `var f = new X()` is neither a `ResourceDeclaration` (Type FAILS on the
  reserved `var`) nor an `Expression` (`var` is not an expression start) → the using STATEMENT with
  `var` REJECTS at v2–v7. This is a PRE-EXISTING limitation, NOT a regression from T3.8.2 (which does
  not touch `UsingStatement` or `ResourceAcquisition` — verified: the form REJECTS at v2–v7 both with
  and without the Cs8 change). The no-regression test therefore uses the concrete-type form
  `using (int x = Foo()) { }` (which parses at v1–v7, matching the pre-existing Cs1SwitchTryTests
  form) for v1+v7, plus the task's `var` form at v1 (where it parses). Documented.
- **D2 — The using declaration has a SINGLE declarator with a REQUIRED initializer.** Roslyn's using
  declaration is a `LocalDeclarationStatement` (LanguageParser.cs:10482) that the BINDER restricts to
  exactly one variable (multiple declarators is a semantic error). This grammar models the task's
  single-declarator form (`!ReservedKeyword Identifier "=" Expression`) with a REQUIRED initializer.
  A using declaration WITHOUT an initializer (`using FileStream f;`) is REJECTED (the v8 malformed
  test). Multiple declarators (`using int x = 1, y = 2;`) are out of scope (not modeled). Documented.
- **D3 — `await using` (pattern-based disposal) is out of scope.** Roslyn's using declaration may
  carry an `await` prefix (`await using IAsyncDisposable x = ...`, C# 8 pattern-based disposal,
  UsingDeclarationTests.cs:855). The task's scope is the plain `using` declaration only; the `await`
  prefix is not modeled. Documented.
- **D4 — Modifiers before the using declaration are not modeled.** Roslyn parses modifiers before a
  using declaration but reports them as errors (LanguageParser.cs:10554-10566). The task's form has
  no modifiers; they are out of scope. Documented.
