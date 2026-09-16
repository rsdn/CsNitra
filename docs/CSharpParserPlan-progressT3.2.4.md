# T3.2.4 — C# 3.0 extension methods (the `this` parameter modifier)

Status: done.

## Goal
Add C# 3.0 extension methods to the EXISTING `Parsers/CSharp/CSharpGrammar/Cs3.grammar`:
`static class E { static void M(this int x) { } }` — the `this` modifier on the first parameter of a
static method in a static class. The `this` parameter modifier is the key new construct (CS3).
Extension *properties* and other extension-member forms are later versions — excluded.

## Baseline (before changes)
- `dotnet build Nitra.sln --no-incremental` → success, 0 errors / 0 warnings.
- `dotnet test Tests/CSharpGrammarTests` → **927 total / 924 passed / 0 failed / 3 skipped**.

## The Cs1 structure found (exact, verified against Cs1.grammar)
- `Parameter` (Cs1.grammar:448): `Attributes? ParameterModifier* Type TypeName`.
- `ParameterModifier` (Cs1.grammar:450): named union `| "ref" | "out" | "params"` (C# 1.0).
- `ParameterList` (Cs1.grammar:446): `(Parameter; ",")+`.
- `Method` (Cs1.grammar:265): `Attributes? MethodModifier* Type TypeName "(" ParameterList? ")" MethodBody`.
- `MethodModifier` (Cs1.grammar:288): access + **static**/virtual/override/abstract/new/extern/sealed/unsafe.
  **`static` IS a Cs1 MethodModifier (C# 1.0)** — so the `static` in `static void M(...)` needs no change.
- `ClassModifier` (Cs1.grammar:44): public/private/protected/internal/abstract/sealed/unsafe.
  **`static` is NOT a Cs1 ClassModifier.** The task's premise ("static class is already C# 1.0") is only
  half-true: `static` is a Cs1 MethodModifier but NOT a Cs1 ClassModifier.
- `this` (Cs1.grammar:604): **IS a Cs1 ReservedKeyword.**
- `this` as an expression primary (Cs1.grammar:679): `Primary = ... | "this" | ...` — a SEPARATE rule
  from `ParameterModifier`. So `this.M()` (expression) is a different context from `this int x`
  (parameter modifier).
- `TypeName` (Cs1.grammar:23): `!ReservedKeyword Identifier` — so `this` (reserved) can NEVER be a
  `Type`/`TypeName`. No ambiguity between the `this` modifier and the parameter's type.

## `static` class finding (the key deviation)
- `static` class is **C# 2.0**, NOT C# 1.0. Roslyn: `IDS_FeatureStaticClasses` → CSharp2
  (MessageID.cs:728). The Cs1 test `Class_StaticModifier_Fails`
  (Tests/CSharpGrammarTests/Cs1RoslynTypeDeclarationTests.cs:122) asserts `static class a { }` FAILS at
  Cs1 (comment: "'static' types are CS2").
- Consequence: the task's positive tests all use `static class E { ... }`, which currently REJECTS at
  every version (v1/v2/v3/v6/v11) because `static` is not a ClassModifier anywhere. To make them parse
  at v3, `static` must be added to `ClassModifier` at v3.
- DECISION: re-declare `ClassModifier` in Cs3 to APPEND `"static"` (T0.3 merge). This is a documented
  deviation: static class is CS2 per Roslyn, but it is added in the Cs3 file because (a) the task scopes
  all changes to Cs3 ("CS3 only", "only stage Cs3.grammar"), and (b) the extension method positive tests
  require `static class` to parse at v3. `static` is NOT added to `StructModifier` (a struct cannot be
  static — CS0701).

## Approach chosen
Two re-declarations in Cs3 (both T0.3 append-merge):
1. **`ParameterModifier`** — APPEND `"this"` (the CS3 feature, the key new construct). The merged v3
   union is `| "ref" | "out" | "params" | "this"`. Each alternative is a distinct word → no equal-length
   tie. `this` is a reserved keyword → it can never be a `Type`/`TypeName` (Cs1:23) → no ambiguity with
   the parameter's type.
2. **`ClassModifier`** — APPEND `"static"` (CS2 per Roslyn; added in Cs3 so the positive tests parse at
   v3 — documented deviation, see above).

Why re-declare the modifier unions (not the declarations): the Cs1 `Method` (Cs1:265) and
`ClassDeclaration` (Cs1:34) already use these merged modifier unions, so no re-declaration of the
declarations is needed (the same pattern as `partial` in T3.1.3, Cs2.grammar:255-262).

## Mutual-exclusivity hand-traces (no equal-length tie)
### `ParameterModifier` (this modifier)
- `this int x` (v3): `ParameterModifier*` = `this` (matches; `int` is not a modifier → loop stops),
  `Type` = `int`, `TypeName` = `x`. The Cs1 alternative (no `this`) fails at `this` (not a Cs1 modifier,
  not a Type). → Cs3 only. No tie.
- `int x` (v3): `ParameterModifier*` = empty, `Type` = `int`, `TypeName` = `x`. → no `this` modifier. No tie.
- `ref int x` (v3): `ParameterModifier*` = `ref`, `Type` = `int`, `TypeName` = `x`. Distinct from `this`. No tie.
- `this` (reserved) can never be a `Type`/`TypeName` (Cs1:23) → the modifier and the type never compete.
### `ClassModifier` (static class)
- `static class E { }` (v3): `ClassModifier*` = `static`, `"class"`, `TypeName` = `E`, `ClassBody`. → matches.
- `class E { }` (v3): `ClassModifier*` = empty, `"class"`, ... → matches. Different inputs, no tie.
- `static` (reserved) can never be a `TypeName` (Cs1:23) → it is only ever a modifier, never a class name.
### `this` modifier vs `this` expression primary (different contexts, no conflict)
- `this int x` is in a PARAMETER LIST (`Parameter` → `ParameterModifier`); `this.M()` is in an EXPRESSION
  (`Primary`). Re-declaring `ParameterModifier` does NOT touch `Primary` (Cs1:679) → the `this` expression
  primary is UNAFFECTED (still parses at v1/v2).

## Roslyn references (C:\RSDN\roslyn, main)
- `IsParameterModifierExcludingScoped` (LanguageParser.cs:4999-5013): returns true for `ThisKeyword`
  (5003) / Ref / Out / In / Params / ReadOnly — `this` is a parameter modifier, parsed UNCONDITIONALLY
  (no version gate in the parser).
- `ParseParameterModifiers` (LanguageParser.cs:5015): comment 5022 "Normal keyword-modifier
  (in/out/ref/readonly/params/this). Always safe to consume."
- `CheckParameterModifiers` (ParameterHelpers.cs:587): BINDER — `ThisKeyword` →
  `Binder.CheckFeatureAvailability(modifier, MessageID.IDS_FeatureExtensionMethod, ...)` (607). The
  version gate (extension methods = CS3) and the first-parameter restriction are BINDER concerns, not
  parser concerns.
- `IDS_FeatureExtensionMethod` → `LanguageVersion.CSharp3` (MessageID.cs:717-721).
- `IDS_FeatureStaticClasses` → CSharp2 (MessageID.cs:728).
- First-parameter restriction (binder, CS1100): `CS1107ERR_DupParamMod`
  (ParserErrorMessageTests.cs:3472) — `Goo(int this)` → "parameter modifier 'this' which is not on the
  first parameter"; `Goo(this this t)` → CS1107 duplicate; `Goo(this t)` → CS0246 (type not found). All
  BINDER diagnostics — the parser accepts them (at the parse level we allow `this` on ANY parameter; the
  first-parameter-only rule is a binder concern).
- Syntax tests: `DeclarationParsingTests.cs:19226 ScopedInParameter1`
  (`static class C { public static void M(scoped in this int x) { } }` — shows `this` in the parameter
  modifier list; the `scoped in` are CS13/14, dropped here); `SymbolDisplayTests.cs:330
  TestExtensionMethodAsStatic` (canonical `public static TSource M<TSource>(this C1<TSource> source,
  int index) {}`).

## Version-purity results
- `static class E { static void M(this int x) { } }` → **rejects at v2** / **parses at v3**.
- `class E { void M(this int x) { } }` (isolates `this`, no static class) → **rejects at v2** (this not a
  parameter modifier) / **parses at v3**.
- `class C { void M() { this.N(); } }` (this expression primary) → **still parses at v1 and v2** (C# 1.0,
  `Primary` unchanged).

## Tests
`Tests/CSharpGrammarTests/Cs3ExtensionMethodTests.cs` (CRLF + UTF-8 BOM) — **14 tests, all green**.
- POSITIVE (v3, 5): `ExtensionMethod_Simple_Succeeds` (`static class E { static void M(this int x) { } }`),
  `ExtensionMethod_WithExtraParams_Succeeds` (`static class E { static int Add(this int x, int y) { return
  x + y; } }`), `ExtensionMethod_ReferenceType_Succeeds` (`static class E { static void M(this string s)
  { } }`), `ExtensionMethod_ArrayType_Succeeds` (`static class E { static void M(this int[] xs) { } }`),
  `ThisModifier_OnNonFirstParameter_Parses` (`static class E { static void M(int y, this int x) { } }` —
  `this` on a non-first parameter parses; first-parameter-only is a binder concern).
- POSITIVE (v3, Roslyn-derived, 2): `Roslyn_ScopedInParameter_ThisModifier_Succeeds`
  (DeclarationParsingTests.cs:19226 ScopedInParameter1, adapted: dropped the CS13/14 `scoped in`, kept
  the `this`), `Roslyn_ExtensionMethodWithExtraParam_Succeeds` (SymbolDisplayTests.cs:330
  TestExtensionMethodAsStatic, adapted: `static void M(this int source, int index) { }`).
- POSITIVE (v3, isolation, 1): `ThisParameterModifier_Isolated_Succeeds` (`class E { void M(this int x)
  { } }` — isolates the `this` modifier from the `static class` deviation).
- POSITIVE (v1 + v2, the `this` expression primary — C# 1.0, must stay green, 2):
  `ThisExpression_Primary_ParseAtV1_Succeeds` and `ThisExpression_Primary_ParseAtV2_Succeeds`
  (`class C { void M() { this.N(); } }` — `this` as a Primary, Cs1.grammar:679, unchanged).
- VERSION-PURITY (v2, 2): `ExtensionMethod_ThisModifier_RejectedAtV2` (`static class E { static void M
  (this int x) { } }` — the task's exact negative; rejects due to both `static class` and `this`),
  `ThisParameterModifier_Isolated_RejectedAtV2` (`class E { void M(this int x) { } }` — isolates `this`:
  at v2 `this` is not a ParameterModifier and not a Type → rejects).
- NEGATIVE (v3, malformed, 2): `ExtensionMethod_ThisWithoutType_Rejected` (`static class E { static void
  M(this) { } }` — `this` with no type/name: `Parameter` requires Type + TypeName),
  `ExtensionMethod_ThisWithoutName_Rejected` (`static class E { static void M(this int) { } }` — `this`
  with a type but no name).

## Verification
- [x] `dotnet build Nitra.sln --no-incremental` → **success, 0 errors / 0 warnings**.
- [x] `dotnet test Tests/CSharpGrammarTests` (fresh build) → **941 total / 938 passed / 0 failed /
  3 skipped**. Baseline before T3.2.4: 927 total / 924 passed / 3 skipped. Delta = **+14** (all new
  `Cs3ExtensionMethodTests`). Filtered run of `Cs3ExtensionMethodTests` → **14 passed / 0 failed**. All
  pre-existing Cs1 / Cs2 / Cs6 / Cs11 tests remain green, including the Cs1 `Class_StaticModifier_Fails`
  (v1, `static class a { }` still rejects at v1) and the `this` expression-primary tests.
- [x] `dotnet test Tests/ParserTests` (regression; engine + CsNitra meta-grammar unchanged) →
  **327 total / 325 passed / 0 failed / 2 skipped** — matches baseline.

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs3.grammar` (T3.2.4: re-declared `ParameterModifier` + `ClassModifier`).
- `Tests/CSharpGrammarTests/Cs3ExtensionMethodTests.cs` (new).
- `docs/CSharpParserPlan-checklist.md` (T3.2.4 `[~]` → `[✅]`).
- `docs/CSharpParserPlan-progressT3.2.4.md` (this file).

## Boundary decisions / deviations
- **`static` class is CS2 per Roslyn, added in the Cs3 file (deviation).** `IDS_FeatureStaticClasses` →
  CSharp2 (MessageID.cs:728); it is NOT a Cs1 ClassModifier (correctly — Cs1 test
  `Class_StaticModifier_Fails` asserts `static class a { }` fails at Cs1). The task's positive tests use
  `static class`, and the task scopes all changes to Cs3 ("CS3 only"), so `static` is appended to
  `ClassModifier` in Cs3 (parses at v3, rejects at v1/v2). `static` is a Cs1 MethodModifier (Cs1:293), so
  the `static` in `static void M(...)` needs no change.
- **The `this` modifier is allowed on ANY parameter at the parse level.** The first-parameter-only
  restriction (CS1100) is a BINDER concern (Roslyn `CheckParameterModifiers`, ParameterHelpers.cs:587),
  consistent with the parser-vs-binder split used throughout the project.
- **CS3 only**: no extension properties, no `ref this` / `in this` (ref extension methods, CS7.3+), no
  other extension-member forms (later versions).
