# T3.1.2 — C# 2.0 `var` (implicit typing) + anonymous methods

Status: done.

## Goal
Add C# 2.0 `var` (implicit local variable typing) and anonymous methods (`delegate { }` /
`delegate (params) { }` as an *expression*) to the existing `Parsers/CSharp/CSharpGrammar/Cs2.grammar`.
CS2 only.

## Baseline (before changes)
- `dotnet build Nitra.sln --no-incremental` → success, 0 errors / 0 warnings.
- `dotnet test Tests/CSharpGrammarTests` (fresh build) → **821 passed / 0 failed / 3 skipped** (total 824), per T3.1.1.2.
- `dotnet test Tests/ParserTests` (regression) → **325 passed / 0 failed / 2 skipped** (total 327), per T3.1.1.2.

## Cs2.grammar rules

### `var` — version-purity via `ReservedKeyword` re-declaration
- `ReservedKeyword = | "var";` — re-declares Cs1 `ReservedKeyword`; the merge (T0.3) APPENDS `"var"`
  as a new alternative. `var` is a **plain identifier in C# 1.0** but a **reserved keyword in C# 2.0**,
  so it is NOT added to Cs1 (that would break C# 1.0 where `var` is a valid type/variable name).
  The merged `ReservedKeyword` (Cs1 + Cs2) reserves `var` only at version 2+.
  - `TypeName = !ReservedKeyword Identifier` (Cs1:23): at v1 `var` is a valid `TypeName`; at v2 it is
    not (now reserved). This is what makes `var` non-usable as a type at v2 while still usable at v1.
  - `Type` → `QualifiedName` → `TypeName` (Cs1:508-512), so `var` is not a `Type` at v2.

### `var` local variable declaration
- `LocalVariableDeclaration = "var" VariableDeclarator ("," VariableDeclarator)* ";";` — re-declares
  Cs1 `LocalVariableDeclaration` (Cs1:807 `Type VariableDeclarator ("," VariableDeclarator)* ";"`);
  the Cs2 alternative REQUIRES the `"var"` literal (REQUIRED-new-construct), so it is mutually
  exclusive with the Cs1 `Type`-based alternative (at v2 `var` is reserved, not a `Type`, so the Cs1
  alternative cannot start with `var`). Reuses `VariableDeclarator` (Cs1:811) = `!ReservedKeyword
  Identifier ("=" Expression)?`, so `var x;` (no initializer) parses at the syntax level — a **binder**
  error CS0815 in real C# (parser-vs-binder split, consistent throughout).

### Anonymous methods (expression)
- `Primary = | AnonymousMethod = "delegate" AnonymousMethodParameterList? Block;` — re-declares Cs1
  `Primary` (append, same pattern Cs11 uses for raw strings, Cs11:22). `Primary` is a plain union
  (not TDOPP). The anonymous method is an *expression* (Roslyn `ParseAnonymousMethodExpression`,
  LanguageParser.cs:13734): `delegate` + optional `(params)` + `{ block }`.
- `AnonymousMethodParameterList = "(" (AnonymousMethodParameter; ",")* ")";` — Roslyn
  `ParseParenthesizedParameterList` (LanguageParser.cs:4750), `requireOneElement:false` (empty parens
  `delegate () { }` allowed).
- `AnonymousMethodParameter = | AnonymousMethodTypedParameter = ParameterModifier* Type TypeName |
  AnonymousMethodUntypedParameter = TypeName;` — a parameter is either explicitly typed (`int x`) or
  an untyped name (`x`). Roslyn `ParseParameter` (LanguageParser.cs:4938) always parses a type; the
  untyped form is a **binder/version** concern (implicit-typed anonymous-method parameters are C# 4.0),
  so the parser accepts the syntactic form (parser-vs-binder split). Named union (the meta-grammar
  forbids `|` inside a group).

## Mutual-exclusivity hand-traces (no equal-length tie)

### `var` vs `Type` (LocalVariableDeclaration, alternatives: Cs1, Cs2)
- `var x = 5;` (v2): Cs1 → `Type` at `var` (reserved at v2) → **fail**. Cs2 → `"var"`,
  `VariableDeclarator=x=5`, `;` → **match**. → **Cs2 only**, no tie.
- `int x = 5;` (v2): Cs1 → `Type=int`, `VariableDeclarator=x=5`, `;` → **match**. Cs2 → `"var"` at
  `int` → **fail**. → **Cs1 only**, no tie.
- `var x = 5;` (v1): Cs2 not loaded. Cs1 → `Type=var` (var is a valid TypeName at v1),
  `VariableDeclarator=x=5`, `;` → **match** (parses as a *typed* declaration whose type is `var`).
  This is the documented v1 behavior.

### Anonymous method vs delegate declaration (disjoint contexts)
- `delegate { }` (expression position, e.g. `Run(delegate { })`): only `Primary`/`Expression`
  alternatives are tried. `AnonymousMethod` → `delegate` + (no `(`) + `{ }` (Block) → **match**.
  `DelegateDeclaration` is a `TypeDeclaration` (namespace/class member), NOT a `Primary`, so it is not
  even considered in expression position. → **AnonymousMethod only**, no tie.
- `delegate void D();` (declaration position, e.g. `class C { delegate void D(); }`): only `ClassMember`
  alternatives are tried. `DelegateDeclaration` (Cs1) → `delegate` + `Type=void` + `TypeName=D` + `()` +
  `;` → **match**. `AnonymousMethod` is a `Primary` (expression), NOT a `ClassMember`, so it is not even
  considered in declaration position. → **DelegateDeclaration only**, no tie.
- After `delegate`, a type-identifier leads to a declaration; `{` or `(` leads to an anonymous method.
  The two live in disjoint parse contexts (expression vs declaration), so they never compete.

### Anonymous method vs existing Primary alternatives
The anonymous method starts with the reserved keyword `delegate`; every existing `Primary`
alternative starts with a different leading token (true/false/null/this/identifier/literal/number/
`(`/`new`/`sizeof`/`typeof`/`checked`/`unchecked`/raw-string). Mutually exclusive, no tie.

### Anonymous method parameter (typed vs untyped, longest-match)
- `int x`: Typed → `int x`; Untyped fails (`int` reserved, not a `TypeName`). → Typed only.
- `x`: Typed fails (needs a 2nd name); Untyped → `x`. → Untyped only.
- `Foo x`: Typed → `Foo x` (longer); Untyped → `Foo` (shorter). → Typed (longest), no tie.

## Anonymous-method parameter forms handled
- `delegate { }` — no parameter list (Roslyn ExpressionParsingTests.cs:2014 `TestAnonymousMethodWithNoArgumentList`).
- `delegate () { }` — empty parameter list (Roslyn ExpressionParsingTests.cs:1984 `TestAnonymousMethodWithNoArguments`).
- `delegate (int x) { }` — typed parameter (Roslyn ExpressionParsingTests.cs:1953 `TestAnonymousMethod`).
- `delegate (x) { }` — untyped parameter (parse-level; C# 4.0 feature, binder-gated; parser-vs-binder split).
- `delegate (int x, int y) { }` — multiple typed parameters.
- `delegate (ref int x) { }` — `ref`/`out` parameter modifier (Roslyn DeclarationParsingTests.cs:12787).

## Roslyn references (C:\RSDN\roslyn, main)
- `ParseAnonymousMethodExpression` / `parseAnonymousMethodExpressionWorker`
  (src/Compilers/CSharp/Portable/Parser/LanguageParser.cs:13734-13783): `delegate` + optional
  `(` `ParseParenthesizedParameterList` `)` + required `{` `ParseBlock` `}`.
- `ParseParenthesizedParameterList` (LanguageParser.cs:4750) → `ParseParameterList`
  (4828) → `ParseParameter(identifierIsOptional:false)` (4938): each parameter = modifiers + `ParseType`
  + identifier.
- Syntax tests: ExpressionParsingTests.cs:1953/1984/2014 (typed / empty / no-list);
  DeclarationParsingTests.cs:12669-12670 (`var f1 = delegate { return 42; };` /
  `var f2 = delegate (int x) { return x * 2; };`), :12787 (`var f = delegate (ref int i) { i = 42; };`).
- `delegate` is the same `DelegateKeyword` for both declarations and anonymous methods; the parser
  disambiguates by context (declaration position vs expression position) — confirmed by the worker
  eating `DelegateKeyword` in expression context (13745) and `ParseDelegateDeclaration` (5835) in
  declaration context.

## Version-purity results
- `class C { void M() { var x = 5; } }` → parses at **v1** (typed declaration, type `var`) and at
  **v2** (implicit typing). Both parse; the interpretation differs.
- `class C { var X; }` (field) → **parses at v1** (`var` is a valid type name) / **rejects at v2**
  (`var` is reserved, not a type; no var-field rule). This is the clean v1-vs-v2 discriminator.
- `class C { void M() { Run(delegate { }); } }` → **rejects at v1** (no anonymous-method Primary) /
  **parses at v2**.

## Tests
`Tests/CSharpGrammarTests/Cs2VarAnonymousMethodTests.cs` (CRLF + UTF-8 BOM) — **23 tests, all green**.
- POSITIVE (v2, `var`, 5): `Var_SingleLocal` (`var x = 5;`), `Var_MultipleLocals` (`var a = 1, b = 2;`),
  `Var_NoInitializer` (`var x;` — binder CS0815), `Var_InLoopBody` (`for (int i = 0; ...) { var x = i; }`),
  `Var_InIfBody` (`if (true) { var x = 5; }`).
- POSITIVE (v2, anonymous method, 7): `AnonymousMethod_NoParams` (`delegate { }`),
  `AnonymousMethod_EmptyParamList` (`delegate () { }`), `AnonymousMethod_TypedParam` (`delegate (int x) { }`),
  `AnonymousMethod_UntypedParam` (`delegate (x) { }`), `AnonymousMethod_MultipleTypedParams`
  (`delegate (int x, int y) { }`), `AnonymousMethod_RefParam` (`delegate (ref int x) { }`),
  `AnonymousMethod_WithBodyStatement` (`delegate { x = 5; }`).
- POSITIVE (v2, Roslyn-derived, 3): `Var_AnonymousMethodCombined`
  (`var f = delegate { return 42; };`, DeclarationParsingTests.cs:12669),
  `Var_AnonymousMethodTypedParam` (`var f = delegate (int x) { return x * 2; };`, :12670),
  `AnonymousMethod_RefParam_Roslyn` (`var f = delegate (ref int i) { i = 42; };`, :12787).
- VERSION-PURITY (3): `Var_AsTypeName_Field_ParsesAtV1` (`class C { var X; }` parses at v1 — var is a
  valid type name), `Var_AsTypeName_Field_RejectedAtV2` (same input rejects at v2 — var is reserved,
  not a type; no var-field rule), `Var_Local_ParsesAtV1_AsTypedDeclaration`
  (`class C { void M() { var x = 5; } }` parses at v1 as a *typed* declaration, type `var`).
- NEGATIVE (v1, version-purity, 1): `AnonymousMethod_RejectedAtV1`
  (`class C { void M() { Run(delegate { }); } }` rejects at v1).
- NEGATIVE (v2, malformed, 4): `Var_MissingIdentifier` (`var = 5;`), `Var_MissingInitializerExpression`
  (`var x = ;`), `AnonymousMethod_UnclosedBlock` (`Run(delegate { ); }`),
  `AnonymousMethod_UnclosedParamList` (`Run(delegate (int x { ); }`).

Note on the task's `var x = 5;` "version-purity negative": at v1 it does NOT reject — it parses as a
typed declaration whose type is `var` (var is a valid TypeName at v1). So it is covered as a v1
*positive* (`Var_Local_ParsesAtV1_AsTypedDeclaration`), and the clean v1-vs-v2 discriminator is the
`var`-typed field (parses at v1, rejects at v2). See the Version-purity results above.

## Verification
- [x] `dotnet build Nitra.sln --no-incremental` → **success, 0 errors / 0 warnings**.
- [x] `dotnet test Tests/CSharpGrammarTests` (fresh build) → **844 passed / 0 failed / 3 skipped** (total 847).
  - Baseline before T3.1.2: 821 passed / 3 skipped (total 824). Delta = **+23** (all new
    `Cs2VarAnonymousMethodTests`).
  - Filtered run of `Cs2VarAnonymousMethodTests` → **23 passed / 0 failed**.
  - All pre-existing Cs1 / Cs2 (T3.1.1.1 + T3.1.1.2) / Cs6 / Cs11 tests remain green.
- [x] `dotnet test Tests/ParserTests` (regression; engine + CsNitra meta-grammar unchanged) →
  **325 passed / 0 failed / 2 skipped** (total 327) — matches baseline.

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs2.grammar` (added T3.1.2 section).
- `Tests/CSharpGrammarTests/Cs2VarAnonymousMethodTests.cs` (new).
- `docs/CSharpParserPlan-checklist.md` (T3.1.2 `[~]` → `[✅]`).
- `docs/CSharpParserPlan-progressT3.1.2.md` (this file).

## Boundary decisions / deviations
- **`var` version-purity via `ReservedKeyword` re-declaration** (not a Cs1 change): `var` is a plain
  identifier in C# 1.0 but a reserved keyword in C# 2.0, so it is APPENDED to the merged
  `ReservedKeyword` in Cs2 (T0.3 merge), never added to Cs1. Consequence: at v1 `var` is a valid
  `TypeName` (so `var x = 5;` parses as a *typed* declaration and `class C { var X; }` parses as a
  field of type `var`); at v2 `var` is reserved (not a `Type`), so the Cs1 `Type`-based
  `LocalVariableDeclaration` cannot start with `var` and the Cs2 `"var"` alternative (REQUIRED-new-
  construct) is the only match. The clean v1-vs-v2 discriminator is a `var`-typed field (parses at v1,
  rejects at v2). This requires no Cs1 modification.
- **`var x;` (no initializer) parses at the syntax level** — a binder error CS0815 in real C#
  (parser-vs-binder split, consistent throughout the project).
- **Untyped anonymous-method parameter `delegate (x) { }` parses at the syntax level** — Roslyn
  `ParseParameter` always parses a type, and implicit-typed anonymous-method parameters are a C# 4.0
  feature; the parser accepts the syntactic form and the binder/version gate rejects it (parser-vs-
  binder split). Handled via a dedicated `AnonymousMethodParameter` union (typed `ParameterModifier*
  Type TypeName` | untyped `TypeName`) so the global `Parameter` rule (which requires a type, used by
  method/field/declaration parameters) is untouched — no regression to typed-parameter contexts.
- **Anonymous method vs delegate declaration disambiguate by context, not by a shared rule**: the
  anonymous method is a `Primary` (expression position only); a delegate declaration is a
  `TypeDeclaration` (namespace/class-member position only). They never compete at the same position,
  so no equal-length tie arises (hand-traces above). No lookahead needed.
- **`var` in `for`-init / `using`-declaration is NOT supported** (out of scope): `ForInit`
  (Cs1:860) and `ResourceAcquisition` (Cs1:932) each have their own `Type VariableDeclarator ...`
  declaration alternative, which were not re-declared. So `for (var x = 0; ...)` and
  `using (var x = ...)` reject at v2 (var is not a Type there and no `var` alternative exists). The
  task scoped `var` to local variable declarations only; a `var` in a loop *body* works
  (`Var_InLoopBody`). This is a documented limitation, not a regression (those forms were never
  parseable with `var`).
- **No `async`/`partial`/`static` anonymous-function modifiers, no default parameter values, no
  lambdas** (CS5 / CS8 / CS3 respectively — later versions; CS2 only).
