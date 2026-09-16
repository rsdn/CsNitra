# T3.6.3 — C# 7.0 local functions

Status: done (with Deviations D1–D4 — see the Deviations section).

## Goal
Add C# 7.0 local functions to the EXISTING `Parsers/CSharp/CSharpGrammar/Cs7.grammar` (which already has the tuple rules from T3.6.1 and the pattern-matching rules from T3.6.2):
- Basic local function: `void M() { void N() { } N(); }`.
- Local function with return type: `void M() { int N() { return 5; } }`.
- Local function with modifiers: `static`, `async`, `unsafe`.
- Local function with generics: `void M() { void N<T>() { } N<int>(); }`.
- Local function with expression body: `void M() { int N() => 5; }`.
- Local function with constraints: `void M() { void N<T>() where T : struct { } }`.

CS7 only. `CreateParser(6)` must REJECT `void M() { void N() { } }`; `CreateParser(7)` must accept it.

## Baseline (before changes)
- `dotnet build Nitra.sln --no-incremental` → success, 0 errors / 0 warnings.
- `dotnet test Tests/CSharpGrammarTests` → **1100 total / 1097 passed / 0 failed / 3 skipped** (matches T3.6.2).

## Cs1 structure found
- **`Statement`** (Cs1.grammar:772-793): a union of statement forms:
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
      | GotoStatement
      | IfStatement
      | WhileStatement
      | DoWhileStatement
      | ForStatement
      | ForEachStatement
      | SwitchStatement
      | TryStatement
      | UsingStatement
      | LockStatement
      | CheckedStatement
      | UncheckedStatement;
  ```
  A local function is a new STATEMENT form (a method-like declaration inside a block). Re-declare `Statement` in Cs7 to add a local-function alternative.
- **`Method`** (Cs1.grammar:265): `Method = Attributes? MethodModifier* Type TypeName "(" ParameterList? ")" MethodBody;`. The local function mirrors this (minus `Attributes?` and with a local-function modifier set).
- **`MethodBody`** (Cs1.grammar:267-269): `| Block | ";"`. Cs6 re-declares it (T3.5.3) to add `ExpressionBody = "=>" Expression ";"`. So at v7, `MethodBody` = Block | ";" | `=>` Expression ";". The local function reuses `MethodBody` (the task explicitly says to reuse the T3.5.3 `=>` form).
- **`MethodModifier`** (Cs1.grammar:288-300): access + static/virtual/override/abstract/new/extern/sealed/unsafe. Cs5 appends `"async"` (Cs5.grammar:49). The local function uses a SPECIFIC modifier set (`LocalFunctionModifier`), not the full `MethodModifier` (see approach).
- **`ParameterList`** (Cs1.grammar:446): `(Parameter; ",")+` (one or more). The local function uses `"(" ParameterList? ")"` (optional, for the empty `()` list).
- **`ConstraintClause`** (Cs2.grammar, T3.1.1.2): `"where" !ReservedKeyword Identifier ":" ConstraintList;`. Available at v7 (Cs2 is in range).
- **`Block`** (Cs1.grammar:770): `"{" Statement* "}"`.
- **`LocalVariableDeclaration`** (Cs1.grammar:807): `Type VariableDeclarator ("," VariableDeclarator)* ";"`. The main disambiguation target (see hand-traces).
- **`VariableDeclarator`** (Cs1.grammar:811): `!ReservedKeyword Identifier ("=" Expression)?`.
- **`ExpressionStatement`** (Cs1.grammar:803): `Expression ";"`.
- **`LabeledStatement`** (Cs1.grammar:797): `!ReservedKeyword Identifier ":" Statement`.
- **`ReservedKeyword`** (Cs1.grammar:537-618): `static`, `unsafe` ARE reserved. `async` is NOT reserved (contextual keyword, Cs5). `void`/`int`/etc. are reserved (PredefinedType).

## Roslyn references (C:\RSDN\roslyn, main, src/Compilers/CSharp/Portable)
- **Local function structure** — `TryParseLocalFunctionStatementBody` (Parser/LanguageParser.cs:10921-11024) builds `LocalFunctionStatement(attributes, modifiers, type, identifier, typeParameterListOpt, paramList, constraints, blockBody, expressionBody, semicolon)`:
  - modifiers (a token list), return type (`TypeSyntax`), name (`identifier`), optional type-parameter list, parameter list, optional constraint clauses, and a body via `ParseBlockAndExpressionBodiesWithSemicolon` (block | `=>` expression | `;`).
  - The type parameters are parsed by `ParseTypeParameterList` (after the name) — in this grammar the greedy merged `TypeName` (T3.1.1.1 D1) consumes them, so no explicit `TypeParameterList` is needed (same as the Cs2 `Method`).
- **Local function modifiers** — `IsAdditionalLocalFunctionModifier` (LanguageParser.cs:10882-10901): `static`, `async`, `unsafe`, `safe`, `extern` are the valid set; `public`/`internal`/`protected`/`private` are parsed ONLY to give a better error message (binder-invalid). This grammar uses the task's subset `static`/`async`/`unsafe`.
- **Version gate** — `IDS_FeatureLocalFunctions` (Errors/MessageID.cs:131) is checked in `Binder/Binder_Statements.cs:556` (`CheckFeatureAvailability`) — a BINDER (semantic) check, not a parse check. So the parser accepts the form at every version where the rule exists; the version gate is a binder concern (consistent with the parser-vs-binder split used throughout). NOTE: `static` local functions are a C# 8.0 feature in Roslyn (`IDS_FeatureStaticLocalFunctions`, MessageID.cs:183; `StaticFunctions` test, LocalFunctionParsingTests.cs:1710 shows the CS8370 error at C# 7.3) — the task requires them in CS7 (documented deviation D2).
- **Semicolon body** — `LocalFunction_NoBody` (LocalFunctionParsingTests.cs:626): `void local();` is a valid local function (a `;` body, no block). So the `MethodBody` `;` form is part of the local function.
- **Disambiguation** — `IsPossibleLocalDeclarationStatement` (LanguageParser.cs:10485+) decides declaration-vs-expression: a predefined type NOT followed by `.` or `(` is a declaration; `IsLocalFunctionAfterIdentifier` (5778) / `isPossibleLocalFunctionToken` (5554) require the token after the name to be `(` or `<`. `ParseExpressionStatementOrLocalFunctionStartingWithUnsafe` (8476) tries a local declaration first for an `unsafe`-starting statement.
- **Syntax tests** (src/Compilers/CSharp/Test/Syntax/Parsing/LocalFunctionParsingTests.cs):
  - `LocalFunction_NoBody` (:626): `void local();` (semicolon body).
  - `StaticFunctions` (:1710): `static void F() { }`.
  - `AsyncStaticFunctions` (:1794): `static async void F1() { }` / `async static void F2() { }` (any order).
  - `LocalFunctionsWithAwait` (:1372): `async` local functions.
  - `ReturnTypesBeforeStatic`/`ReturnTypeBeforeStatic` (:2341): return type before/after modifiers.
  - All confirmed: local functions are a C# 7.0 feature (basic); `static` is C# 8.0 (task requires it in CS7, D2).

## Approach
1. **`LocalFunctionModifier`** (new Cs7 rule): `| "static" | "async" | "unsafe"`. A local-function-SPECIFIC modifier set (NOT the full `MethodModifier`), matching the task's stated subset exactly and avoiding accepting access/virtual/override/etc. modifiers (binder-invalid on local functions). `static`/`unsafe` are reserved; `async` is a contextual keyword (a WordLiteral, whole-word match). Named union (the meta-grammar forbids `|` inside a group).
2. **`Statement`** re-declaration (append, T0.3 merge): add
   `LocalFunctionStatement = LocalFunctionModifier* Type TypeName "(" ParameterList? ")" ConstraintClause* MethodBody;`
   - `LocalFunctionModifier*` — zero or more modifiers (any order, greedy).
   - `Type` — the return type (a local function ALWAYS has a return type, even `void`).
   - `TypeName` — the name; the greedy merged `TypeName` (T3.1.1.1 D1) consumes the optional `<...>` type parameters, so no explicit `TypeParameterList` is needed.
   - `"(" ParameterList? ")"` — the parameter list (optional, for the empty `()` list).
   - `ConstraintClause*` — zero or more constraint clauses (the `where` literal is the disambiguator; available at v7 via Cs2).
   - `MethodBody` — the body (Block | ";" | `=>` Expression ";"), reusing the T3.5.3 expression-body form.
   - `Attributes?` is OMITTED (not required by the task; a local function with attributes is out of scope — D3).

## Mutual-exclusivity hand-traces
The local function is a `Statement` alternative. It starts with either a modifier (`static`/`unsafe`/`async`) or a return type (`Type`). The disambiguating REQUIRED-new-construct is the `"(" ParameterList? ")"` (a parameter list) after the name, which no other statement form has at that position.

### vs `LocalVariableDeclaration` (`Type VariableDeclarator ("," VariableDeclarator)* ";"`)
- `void N() { }` (local function): `LocalVariableDeclaration` → `Type=void`, `VariableDeclarator=N`, then `;` expected but `(` found → FAILS. `LocalFunctionStatement` → sole match. No tie.
- `int N() { return 5; }`: `LocalVariableDeclaration` → `int N` then `(` → FAILS. `LocalFunctionStatement` → sole match.
- `MyType N() { }` (user-defined return type): `LocalVariableDeclaration` → `MyType N` then `(` → FAILS. `LocalFunctionStatement` → sole match.
- `MyType N = 5;` (local variable): `LocalFunctionStatement` → `MyType N` then `(` expected but `=` found → FAILS. `LocalVariableDeclaration` → sole match.
- **KNOWN TIE** `void N();` (semicolon body): `LocalVariableDeclaration` → `void N ;` MATCHES; `LocalFunctionStatement` (MethodBody=`;`) → `void N() ;` MATCHES. EQUAL length → the FIRST (Cs1 `LocalVariableDeclaration`) wins → parsed as a local variable of type `void` (a binder error). Roslyn parses `void N();` as a local function (`LocalFunction_NoBody`). Not tested (a `;`-body local function is not in the task's test list); documented deviation D1.

### vs `ExpressionStatement` (`Expression ";"`)
- `void N() { }`: `void` is a PredefinedType; in `Primary`, `PredefinedMember = PredefinedType "." Identifier` requires a `.` after the type, so `void` alone is not an expression start → `ExpressionStatement` FAILS. `LocalFunctionStatement` → sole match.
- `MyType N() { }`: `MyType` is a `Primary` (IdentifierName); `N` is not a postfix op, so the expression is just `MyType`; then `;` expected but `N` found → `ExpressionStatement` FAILS. `LocalFunctionStatement` → sole match.

### vs `LabeledStatement` (`!ReservedKeyword Identifier ":" Statement`)
- `void N() { }`: `void` is reserved → `!ReservedKeyword` FAILS. `LocalFunctionStatement` → sole match.
- `async N() { }`: `async` is a valid identifier, but `:` expected after it; next is `N` → FAILS. `LocalFunctionStatement` → sole match (with `Type=async`, `TypeName=N` — a binder error, no type named `async`).

### Modifier-start (clean by leading token)
- `static int N() { }` / `unsafe int N() { }`: `static`/`unsafe` are reserved keywords; NO other `Statement` alternative starts with them → `LocalFunctionStatement` is the sole match.
- `async void N() { }`: `async` is not reserved, but no other statement matches `async <type> <name> ( ... )` (LabeledStatement needs `:`; ExpressionStatement needs `;` after the bare `async`; LocalVariableDeclaration's `VariableDeclarator` fails on the reserved `void`). `LocalFunctionModifier*` (greedy) consumes `async`, then `Type=void`, `TypeName=N`. Sole match.
- Modifier order is NOT enforced (`static async` / `async static` both parse — Roslyn `AsyncStaticFunctions` uses both orders; a generic any-order loop, binder concern).

### Generics + constraints (greedy `TypeName`)
- `void N<T>() { }`: `Type=void`, `TypeName=N<T>` (greedy, T3.1.1.1 D1), `()`, no constraints, `{ }`. Sole match.
- `void N<T>() where T : struct { }`: `Type=void`, `TypeName=N<T>` (greedy), `()`, `ConstraintClause*=where T : struct`, `{ }`. Sole match.
- `void N<T>() { } N<int>();`: the local function is a statement (`void N<T>() { }`); `N<int>();` is a separate `ExpressionStatement` (a method invocation). Both parse in the block.

### Nested
- `class C { void M() { void N() { void O() { } O(); } N(); } }`: the outer `void M() { ... }` is a method (ClassMember); inside its block, `void N() { ... }` is a local function (Statement); inside N's block, `void O() { }` is a local function (Statement); `O();` / `N();` are ExpressionStatements. All parse.

## Version-purity results
- `class C { void M() { void N() { } N(); } }` → **rejects at v6** (no `LocalFunctionStatement`; `void N() { }` is neither a `LocalVariableDeclaration` (needs `;`/`=` after the name) nor an `ExpressionStatement` (a bare type is not an expression start)) / **parses at v7**. ✓
- All pre-existing Cs1/Cs2/Cs3/Cs4/Cs5/Cs6/Cs11 tests stay green (no Cs1–Cs6/Cs11 modification).

## Tests
`Tests/CSharpGrammarTests/Cs7LocalFunctionTests.cs` (CRLF + UTF-8 BOM) — **18 tests, all green**.
- POSITIVE (v7, 10): `LocalFunction_Basic_Succeeds` (`void N() { } N();`), `LocalFunction_ReturnType_Succeeds` (`int N() { return 5; }`), `LocalFunction_StaticModifier_Succeeds` (`static int N() { return 5; }`), `LocalFunction_AsyncModifier_Succeeds` (`async void N() { }`), `LocalFunction_UnsafeModifier_Succeeds` (`unsafe void N() { }`), `LocalFunction_Generics_Succeeds` (`void N<T>() { }` — declaration only, see D4), `LocalFunction_ExpressionBody_Succeeds` (`int N() => 5;`), `LocalFunction_Constraints_Succeeds` (`void N<T>() where T : struct { }`), `LocalFunction_Nested_Succeeds` (`void N() { void O() { } O(); }`), `LocalFunction_ParameterList_Succeeds` (`int N(int x) { return x; }`).
- POSITIVE (v7, Roslyn-derived, 4): `LocalFunction_StaticAsyncModifierOrder_Roslyn_Succeeds` (`static async void F1() { }`, LocalFunctionParsingTests.cs:1794 AsyncStaticFunctions), `LocalFunction_AsyncStaticModifierOrder_Roslyn_Succeeds` (`async static void F2() { }`, :1794), `LocalFunction_GenericConstraint_ExpressionBody_Roslyn_Succeeds` (`int goo<T>() where T : IFace => 5;`, :1155 LocalFuncWithWhitespace), `LocalFunction_GenericConstraint_BlockBody_Roslyn_Succeeds` (`int goo<T>() where T : IFace { return 5; }`, :1155).
- POSITIVE (v6, no-regression, 1): `LocalVariableDeclaration_ParsesAtV6` (`int x = 5;` — a regular local variable declaration still parses at v6, confirming the new local-function Statement alternative does not interfere).
- NEGATIVE (v6, version-purity, 1): `LocalFunction_RejectedAtV6` (`void M() { void N() { } }` — a local function is not available at v6).
- NEGATIVE (v7, malformed, 2): `LocalFunction_MissingBody_Rejected` (`void N()` — missing body), `LocalFunction_MissingParameterList_Rejected` (`void N { }` — missing parameter list).

## Verification
- [x] `dotnet build Nitra.sln --no-incremental` → **success, 0 errors / 0 warnings**.
- [x] `dotnet test Tests/CSharpGrammarTests` (fresh build) → **1118 total / 1115 passed / 0 failed / 3 skipped**. Baseline before T3.6.3 (after T3.6.2): 1100 total / 1097 passed / 3 skipped. Delta = **+18** (all new `Cs7LocalFunctionTests`). All pre-existing Cs1/Cs2/Cs3/Cs4/Cs5/Cs6/Cs11 tests remain green (no Cs1–Cs6/Cs11 modification).
- [x] `dotnet test Tests/ParserTests` (regression; engine + CsNitra meta-grammar unchanged) → **327 total / 325 passed / 0 failed / 2 skipped** — matches baseline.

## Setup note (csproj)
- The orchestrator had added an UNCOMMITTED `<ItemGroup>` of explicit `<Compile Include>` items to `Tests/CSharpGrammarTests/CSharpGrammarTests.csproj` (7 files incl. `Cs7LocalFunctionTests.cs`). This conflicts with the SDK's default compile-item globbing → `NETSDK1022: Duplicate 'Compile' items`. The committed csproj (no explicit items; the SDK default globbing already includes all `.cs`) is correct and is what the baseline build used. **Reverted the csproj to the committed state** (`git checkout HEAD -- ...csproj`) so the build passes. The csproj is NOT among the T3.6.3 files to stage; the working tree now matches HEAD for it.

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs7.grammar` (local-function rules: `LocalFunctionStatement` Statement alternative + `LocalFunctionModifier`).
- `Tests/CSharpGrammarTests/Cs7LocalFunctionTests.cs` (new — 18 tests, CRLF + UTF-8 BOM).
- `docs/CSharpParserPlan-progressT3.6.3.md` (this file).
- `docs/CSharpParserPlan-checklist.md` (T3.6.3 `[~]` → `[✅]` with the D1/D4 notes).

## Deviations / boundary decisions
- **D1 — `void N();` (semicolon body) parses as a `LocalVariableDeclaration`, not a local function.** The Cs1 `LocalVariableDeclaration` and the Cs7 `LocalFunctionStatement` (MethodBody=`;`) both match `void N();` at the same length; the FIRST (Cs1) wins. Roslyn parses it as a local function (`LocalFunction_NoBody`, LocalFunctionParsingTests.cs:626). Not tested (a `;`-body local function is not in the task's list); a binder-level difference (both are syntactically valid). Documented.
- **D2 — `static` local functions are a C# 8.0 feature in Roslyn, but the task requires them in CS7.** `IDS_FeatureStaticLocalFunctions` (MessageID.cs:183) gates `static` local functions at C# 8.0 (`StaticFunctions` test, LocalFunctionParsingTests.cs:1710, shows CS8370 at C# 7.3). The task explicitly lists `static` as a CS7 local-function modifier, so this grammar accepts it in CS7 (the version where local functions are introduced). The version gate is a binder concern (parser-vs-binder split); the PARSER accepts the form. Documented.
- **D3 — `Attributes?` is omitted from the local function.** Roslyn's `LocalFunctionStatement` includes attributes (`LocalFunctionAttribute` test, :354), but the task does not require them and no test case uses them. Omitted to keep the scope minimal (a local function with attributes is out of scope). Documented.
- **D4 — the generic INVOCATION `N<int>()` is a pre-existing grammar gap (not a local-function concern).** The task's generics form is `void N<T>() { } N<int>();`, but the generic INVOCATION `N<int>()` (a method call with a type argument) is NOT handled by the grammar: `TypeArgumentList` (Cs2) only appears in TYPE positions (via the greedy `TypeName = !ReservedKeyword Identifier TypeArgumentList`), not in `Primary`/`Expression` (invocation positions). Verified: `class C { void M() { N<int>(); } }` FAILS at v6 (end=-1), so it is a pre-existing gap, not caused by T3.6.3. The local function DECLARATION `void N<T>() { }` (the CS7 feature) parses fine at v7; the test uses the declaration-only form. Adding generic-invocation support is a separate CS2 concern (out of scope for T3.6.3). Documented.
