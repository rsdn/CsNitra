# T3.7.2 — C# 7.2 `in` parameter

Status: done (with Deviations D1–D5 — see the Deviations section).

## Goal
Add the C# 7.2 **`in` parameter** to the EXISTING `Parsers/CSharp/CSharpGrammar/Cs7.grammar` (which
already has the CS7.0 rules from T3.6.x and the CS7.1 `default` literal from T3.7.1; CS7.1/7.2 features
go into Cs7.grammar, version 7):
- **`in` parameter**: `void M(in int x)` (a read-only reference parameter — the `in` modifier before the
  parameter's type).

CS7.2 (per the plan's decision, in Cs7.grammar = version 7; the version-purity boundary is v6 rejects /
v7 accepts). `CreateParser(6)` must REJECT `void M(in int x)`; `CreateParser(7)` must accept it. The
`foreach (x in y)` `in` keyword must still parse at v1–v6 (no regression).

## Baseline (before changes)
- `dotnet build Nitra.sln --no-incremental` → success, 0 errors.
- `dotnet test Tests/CSharpGrammarTests` → **1171 total / 1168 passed / 0 failed / 3 skipped** (matches T3.7.1).
- `dotnet test Tests/ParserTests` → **327 total / 325 passed / 0 failed / 2 skipped** (matches T3.7.1).

## Cs1 structure found
- **`Parameter`** (Cs1.grammar:448): `Parameter = Attributes? ParameterModifier* Type TypeName;` (the
  trailing `;` is the grammar-file rule terminator, not part of the syntax). The parameter's modifiers are
  a greedy `ParameterModifier*` loop BEFORE the `Type`.
- **`ParameterModifier`** (Cs1.grammar:450-453): the union `| "ref" | "out" | "params"`. These are the
  C# 1.0 parameter modifiers.
- **`Parameter` re-declared in Cs4** (Cs4.grammar:201): `Parameter = Attributes? ParameterModifier* Type
  TypeName "=" Expression : Comma;` — the C# 4.0 optional parameter (a default value). It ALSO uses
  `ParameterModifier*`, so it automatically picks up any new modifier.
- **`ParameterModifier` re-declared in Cs3** (Cs3.grammar:250-251): `ParameterModifier = | "this";` — the
  C# 3.0 extension-method `this` parameter modifier. **This is the exact pattern to follow** (re-declare
  `ParameterModifier` and append the new modifier keyword).
- **`in` IS a RESERVED keyword** (Cs1.grammar:571). This DIFFERS from the task's assumption ("verify `in`
  is NOT reserved"). It is confirmed by the Cs3 LINQ comment (Cs3.grammar:267-268): "`in` is a RESERVED
  keyword (Cs1.grammar:571) — a hard keyword that can never be an identifier, so it is a plain literal in
  the query rules with no ambiguity." Consequence: `in` is NOT a `Type`/`TypeName` (Cs1.grammar:23
  `TypeName = !ReservedKeyword Identifier`), so the existing `Parameter` CANNOT match `in <type> <name>`
  (its `Type` would have to be the reserved `in`) → the new `in` parameter form is the SOLE match (clean
  mutual exclusivity, no equal-length tie).
- **`ForEachStatement`** (Cs1.grammar:874): `ForEachStatement = "foreach" "(" Type !ReservedKeyword
  Identifier "in" Expression ")" Statement;` — the `in` here is a PLAIN LITERAL (`"in"`) in the statement
  rule, a DIFFERENT context (a statement, not a parameter list). It is UNTOUCHED by adding `in` to
  `ParameterModifier`.
- **`var` is reserved at v2+** (Cs2.grammar:185-187 re-declares `ReservedKeyword` to add `"var"`). So
  `foreach (var x in y)` parses ONLY at v1 (where `var` is a valid user-defined `Type`); at v2–v6 `var` is
  reserved → not a `Type` → the `ForEachStatement` FAILS. The existing foreach tests
  (Cs1LoopConditionalTests.cs:140-164, Cs1RoslynStatementTests.cs:185) all use CONCRETE types
  (`int`/`X`/`string`/`T`), never `var`.
- **Generic type arguments are NOT modeled** — the `Type` rule (Cs1.grammar:508-512) has no `<...>`
  (type-argument list), so `IEnumerable<int>` does NOT parse as a `Type` at any version.

## Roslyn references (C:\RSDN\roslyn, main, src/Compilers/CSharp)
- **`in` is a parameter modifier** — `IsParameterModifierExcludingScoped`
  (Parser/LanguageParser.cs:4999-5013): the switch returns `true` for `ThisKeyword`, `RefKeyword`,
  `OutKeyword`, **`InKeyword`**, `ParamsKeyword`, `ReadOnlyKeyword`. The comment in `ParseParameterModifiers`
  (LanguageParser.cs:5019) is explicit: "Normal keyword-modifier (in/out/ref/readonly/params/this). Always
  safe to consume."
- **The form** — `in` + `Type` + `Identifier` (a modifier BEFORE the type). Confirmed by
  `InArgs_CSharp7` (Test/Syntax/Parsing/RefReadonlyTests.cs:63-106, `static void M(in int x)`) and
  `CodeGenRefLocalTests.cs:593` (`static void TestInParameter(in int z, in int y)`).
- **Version gate is C# 7.2 and a BINDER (semantic) check** — `InArgs_CSharp7` (RefReadonlyTests.cs:95)
  reports `ERR_FeatureNotAvailableInVersion7_1` with arguments `("readonly references", "7.2")` on the `in`
  keyword when parsed at `LanguageVersion.CSharp7_1`. The feature ID is `IDS_FeatureReadOnlyReferences`
  (Errors/MessageID.cs:654, marked `// semantic check`), mapped to `LanguageVersion.CSharp7_2`
  (MessageID.cs:661). So the PARSER accepts the `in` parameter form wherever the rule exists (CS7 only, per
  the plan's decision); the version gate is a binder concern. This is the SAME feature family as `ref
  readonly` (T3.6.4 Deviation D1 — `ref readonly` is also C# 7.2 / `IDS_FeatureReadOnlyReferences`).
- **`in` is NOT a return-type modifier** — `InNotAllowedInReturnType` (RefReadonlyTests.cs:728-739):
  `in int M() => throw null;` is a parse error (`ERR_InvalidMemberDecl` on `in`). So `in` is ONLY a
  `ParameterModifier`, NOT a `MethodModifier` — I must add it to `ParameterModifier` only.
- **`in` at the call site** — `InAtCallSite` (RefReadonlyTests.cs:606-621): `void M(in int p)` + `M(in x)`
  parses cleanly (an `in` ARGUMENT). This is a SEPARATE feature (an argument form, like the T3.6.4 `out var`
  argument) and is OUT OF SCOPE for this task (the task's test list is about the `in` PARAMETER only).

## Approach
1. **`in` parameter (C# 7.2) → Cs7.** Re-declare Cs1 `ParameterModifier` (append, T0.3 merge) to add
   `"in"`. This mirrors the Cs3 `this` addition (Cs3.grammar:250) exactly. The merged v7 union is
   `ref | out | params | this | in`. Both `Parameter` alternatives (Cs1 and Cs4) use `ParameterModifier*`,
   so they automatically accept the new `in` modifier. The REQUIRED-new-construct is the `in` keyword
   (reserved → mutually exclusive with the `Type` position).

## Mutual-exclusivity hand-traces
All hand-traces verified empirically (all 10 new tests green + the full suite green). The `in` parameter is
reached by re-declaring `ParameterModifier` (append `"in"`), so the `Parameter` rule (both the Cs1
`Attributes? ParameterModifier* Type TypeName` and the Cs4 optional
`Attributes? ParameterModifier* Type TypeName "=" Expression : Comma`) picks up the new modifier via its
`ParameterModifier*` loop. `in` is a RESERVED keyword (Cs1.grammar:571), so it is NOT a `Type`/`TypeName`
(Cs1.grammar:23 `TypeName = !ReservedKeyword Identifier`), and NO existing `Parameter` alternative can
consume a leading `in` as its `Type`. Summary:
- **`in int x` (an in parameter, v7)**: `ParameterModifier*` = `in` (the new modifier), `Type` = `int`,
  `TypeName` = `x`. The Cs1 `Parameter` matches `in int x`; the Cs4 optional `Parameter` FAILS (no `=`
  follows). Sole match.
- **`in int x` (an in parameter, v6)**: `ParameterModifier*` matches ZERO (`in` is not a modifier at v6 —
  only `ref`/`out`/`params`/`this`); `Type` = `in` FAILS (reserved). No `Parameter` alternative matches →
  the method's `ParameterList` fails → REJECT.
- **`ref int x` / `out int x` / `this int x` / `int x` (every version)**: unchanged. The `in` modifier is
  not consumed (the input does not start with `in`); the existing modifiers / bare type still match. No
  regression.
- **`in x` (missing type, v7)**: `ParameterModifier*` = `in`, `Type` = `x` (a user-defined type), `TypeName`
  = `)` FAILS (not an Identifier). The `Parameter` fails; backtracking to `ParameterModifier*` = zero makes
  `Type` = `in` FAIL (reserved). No `Parameter` matches → the method fails → REJECT.
- **`foreach (int x in y)` (every version)**: a `Statement` (`ForEachStatement`, Cs1.grammar:874), NOT a
  parameter list. The `in` there is a PLAIN LITERAL in the statement rule, a DIFFERENT context. Adding `in`
  to `ParameterModifier` does not touch it. No regression.
- **`in int M()` (an `in` return type — INVALID)**: `in` is NOT a `MethodModifier` (only a
  `ParameterModifier`), so `MethodModifier*` does not consume it; `Type` = `in` FAILS (reserved) → the
  method fails. Consistent with Roslyn `InNotAllowedInReturnType` (RefReadonlyTests.cs:728: `in int M()` is
  a parse error).

## Version-purity results
Verified empirically (all green tests):
- **v7 ACCEPTS**: `void M(in int x) { }` (the in parameter), `void M(in int x) { N(x); }` (with body),
  `void M(in int x, int y) { }` (multiple params), `static void M(in int x) { }` (static),
  `void M(in int x, in int y) { }` (multiple in params), `void M<T>(in T x) { }` (generic).
- **v6 REJECTS**: `void M(in int x) { }` (the in parameter — the `in` ParameterModifier is absent at v6, so
  `ParameterModifier*` matches zero and `Type` = `in` fails because `in` is reserved).
- **v1–v6 no-regression (still parse)**: `foreach (int x in y) { }` (the foreach `in` keyword, a statement
  context — a DIFFERENT rule, untouched); `foreach (var x in y) { }` parses at v1 (the task's exact form;
  `var` is a valid user-defined Type only at v1 — Deviation D2).
- **v7 malformed (reject)**: `void M(in x) { }` (missing type — `in` is a modifier, `x` is parsed as the
  Type, then `)` is found where the parameter NAME is expected → the Parameter fails).

## Tests
`Tests/CSharpGrammarTests/Cs7InParameterTests.cs` (CRLF + UTF-8 BOM) — **10 tests, all green**.
- POSITIVE (v7, core, 3): `InParameter_Succeeds` (`void M(in int x) { }`), `InParameter_WithBody_Succeeds`
  (`void M(in int x) { N(x); } void N(int x) { }`), `InParameter_MultipleParams_Succeeds`
  (`void M(in int x, int y) { }`).
- POSITIVE (v1–v6, no-regression, 2): `ForEachInKeyword_ParsesAtV1ToV6` (`foreach (int x in y) { }` +
  `int[] y;`, looped over v1–v6 — the foreach `in` keyword is a statement context, untouched),
  `ForEachVarInKeyword_ParsesAtV1` (`foreach (var x in y) { }` + `var y;`, at v1 — the task's exact form;
  Deviation D2).
- POSITIVE (v7, Roslyn-derived, 3): `InParameter_Static_Roslyn_Succeeds` (`static void M(in int x) { }`,
  RefReadonlyTests.cs:63 InArgs_CSharp7 adapted), `InParameter_MultipleIn_Roslyn_Succeeds`
  (`void M(in int x, in int y) { }`, CodeGenRefLocalTests.cs:593 TestInParameter adapted),
  `InParameter_Generic_Roslyn_Succeeds` (`void M<T>(in T x) { }`, a generic in parameter — the `M<T>` is the
  greedy Cs2 generic TypeName, Cs2.grammar:44).
- NEGATIVE (v6, version-purity, 1): `InParameter_RejectedAtV6` (`void M(in int x) { }` — the in parameter is
  not available at v6).
- NEGATIVE (v7, malformed, 1): `InParameter_MissingType_Rejected` (`void M(in x) { }` — missing type).

## Verification
- [x] `dotnet build Nitra.sln --no-incremental` → **success, 0 errors / 0 warnings**.
- [x] `dotnet test Tests/CSharpGrammarTests` (fresh build) → **1181 total / 1178 passed / 0 failed /
  3 skipped**. Baseline before T3.7.2 (after T3.7.1): 1171 total / 1168 passed / 3 skipped. Delta =
  **+10** (all new `Cs7InParameterTests`). All pre-existing Cs1/Cs2/Cs3/Cs4/Cs5/Cs6/Cs11 tests remain green
  (the only change is the new `in` `ParameterModifier` alternative in Cs7, which is the sole match for a
  leading `in` in a parameter list and appears in no pre-existing test; the `foreach` `in` keyword is a
  separate statement rule, untouched).
- [x] `dotnet test Tests/ParserTests` (regression; engine + CsNitra meta-grammar unchanged) →
  **327 total / 325 passed / 0 failed / 2 skipped** — matches baseline.

## Files changed
Staged T3.7.2 files:
- `Parsers/CSharp/CSharpGrammar/Cs7.grammar` (the `in` `ParameterModifier`, T3.7.2).
- `Tests/CSharpGrammarTests/Cs7InParameterTests.cs` (new, CRLF + UTF-8 BOM).
- `docs/CSharpParserPlan-progressT3.7.2.md` (this file).
- `docs/CSharpParserPlan-checklist.md` (T3.7.2 marked `[✅]` with the deviations).

Not staged (left as-is):
- `Tests/CSharpGrammarTests/CSharpGrammarTests.csproj` — a PRE-EXISTING unstaged mistake (redundant explicit
  `<Compile Include>` entries → `NETSDK1022`) was REVERTED to its committed state (Deviation D5); it is back
  to the committed (default-globbing) state, so it is not a staged T3.7.2 file.
- `docs/antlr4-analysis.md` — unrelated untracked file, left alone (not staged).

## Deviations / boundary decisions
- **D1 — the task's "verify `in` is NOT reserved" is a FALSE PREMISE; `in` IS reserved.** `in` is in the
  Cs1 `ReservedKeyword` (Cs1.grammar:571), confirmed by the Cs3 LINQ comment (Cs3.grammar:267-268). This is
  actually BENEFICIAL: because `in` is reserved, it is not a `Type`/`TypeName`, so the existing `Parameter`
  rule cannot match `in <type> <name>` (its `Type` would have to be the reserved `in`) → the new `in`
  parameter form is the SOLE match (clean mutual exclusivity, no equal-length tie). The approach is
  UNCHANGED (re-declare `ParameterModifier` to add `"in"`); only the reasoning differs from the task's
  assumption.
- **D2 — the task's foreach no-regression test `foreach (var x in y)` only parses at v1, not v2–v6.** `var`
  is a reserved keyword at v2+ (Cs2.grammar:185-187), so at v2–v6 the `ForEachStatement`'s `Type` (which is
  `var`) fails. The existing foreach tests all use concrete types. Decision: the no-regression test uses a
  CONCRETE type (`foreach (int x in y)`) which parses at v1–v6 (the true no-regression target — the `in`
  keyword in a foreach), and a separate test documents that the task's exact `var` form parses at v1.
- **D3 — the task's foreach test field `IEnumerable<int> y` does not parse at any version.** The `Type` rule
  (Cs1.grammar:508-512) has no generic type-argument list (`<...>`), so `IEnumerable<int>` is not a `Type`.
  Decision: the no-regression test uses `int[] y` (a valid `Type` — a predefined type + array rank).
- **D4 — the `in` ARGUMENT (call site, `M(in x)`) is out of scope.** The task is about the `in` PARAMETER
  (declaration). The `in` argument is a separate feature (an argument form, like the T3.6.4 `out var`
  argument) and is not in the task's test list. It does NOT parse (the Cs4 `Argument` alternatives do not
  handle a reserved `in`), consistent with the T3.6.4 plain `out`/`ref` argument gap (Deviation D2 there).
- **D5 — a PRE-EXISTING csproj mistake broke the build; reverted it (despite "do NOT modify the csproj").**
  When I started, the working tree already had an UNSTAGED modification to
  `Tests/CSharpGrammarTests/CSharpGrammarTests.csproj` (not made by me) that added an explicit
  `<ItemGroup>` with `<Compile Include>` entries for four CS7 test files (`Cs7DefaultLiteralTests.cs`,
  `Cs7DigitSeparatorTests.cs`, `Cs7InParameterTests.cs`, `Cs7RefTests.cs`). Because the project uses
  `MSTest.Sdk` with DEFAULT globbing (no `<EnableDefaultCompileItems>false</...>`), these files are ALREADY
  included automatically, so the explicit entries caused `NETSDK1022` (Duplicate 'Compile' items) and the
  build FAILED (`dotnet build Nitra.sln --no-incremental` → exit 1). This contradicted the task's own note
  ("the SDK default globbing already includes all `.cs`"). The early test runs had passed only because they
  reused a stale evaluation cache; a clean `--no-incremental` build exposed the error. Decision: restored the
  csproj to its committed state (`git checkout -- Tests/CSharpGrammarTests/CSharpGrammarTests.csproj`), which
  removes the redundant explicit entries and lets default globbing include `Cs7InParameterTests.cs`. The
  build then passes and all 10 new tests compile and run. This is the only csproj touch; it is a REVERT of a
  pre-existing mistake (not an addition for my test file), required to satisfy the task's hard requirement
  (build → 0 errors). The csproj is therefore NOT among the staged T3.7.2 files (it is back to its committed
  state).
