# T3.12.1 — C# 12.0: primary constructors for classes

## Status
DONE. Build 0 errors; CSharpGrammarTests all green (1436 passed / 3 skipped / 0 failed); ParserTests
all green (325 passed / 2 skipped / 0 failed).

## Task
Set up `Cs12.grammar` (new file) and extend the grammar with primary constructors for classes (C# 12.0).
A class primary constructor is a class declaration with a parameter list (like a record's positional
parameter list); the parameters are in scope in the class body:
```csharp
class MyClass(int x, string y)
{
    // x and y are available here
}
```

## Roslyn syntax found
`C:\RSDN\roslyn`, main, `src/Compilers/CSharp/Portable/Parser/LanguageParser.cs`:
- `ParseMainTypeDeclaration` (1789): common parser for class/struct/interface/record/extension.
  - `name = ParseIdentifierToken()` (1821); `typeParameters = ParseTypeParameterList()` (1824).
  - **paramList (1827-1828)**: `CurrentToken.Kind == OpenParenToken || isExtension ?
    ParseParenthesizedParameterList(...) : null` — the parameter list is present only when `(` follows
    the (type-parameter) name. For a class this is the **primary constructor**.
  - `baseList = ParseBaseList()` (1830) — the `": Base"` list.
  - body (1847-1914): `;` (no body) OR `{ members* }` with an optional trailing `;`.
- `constructTypeDeclaration` (1965-2078), **ClassKeyword case (1974-1988)**: builds `ClassDeclaration`
  with the `paramList` slot (1982) — the primary constructor.
- `ParseParenthesizedParameterList` (4750): `requireOneElement:false` (an empty `()` is allowed).
- Version gate: a class primary constructor is a C# 12 feature (binder/semantic). The PARSER accepts
  the form wherever the rule exists; version purity comes from the rule being Cs12-only.

## Rule written
`Cs12.grammar`:
```
ClassDeclaration = Attributes? ClassModifier* "class" TypeName ClassPrimaryConstructorParameterList BaseList? ClassBody ";"?;
ClassPrimaryConstructorParameterList = "(" (Parameter; ",")* ")";
```
Re-declares `ClassDeclaration` (T0.3 append-merge onto Cs1.grammar:34 and Cs2.grammar:67). The new
alternative REQUIRES the `ClassPrimaryConstructorParameterList` (REQUIRED-new-construct), so it is
mutually exclusive with the Cs1 (no param list) and Cs2 (REQUIRED TypeParameterList) alternatives.
The greedy merged TypeName (Cs2.grammar:44) consumes a generic name (`class C<T>(int x) { }` ->
TypeName = `C<T>`), so the generic form needs no explicit TypeParameterList here (Boundary D1).
The `TypeDeclaration` union (Cs1.grammar:27-32) references `ClassDeclaration` by name, so it picks up
the new alternative automatically (no need to re-declare `TypeDeclaration`, unlike the Cs9 record).

## Setup steps
- [x] Create `Parsers/CSharp/CSharpGrammar/Cs12.grammar` (CRLF/no BOM) — first bytes `47 47 32`, CRLF.
- [x] Add `<EmbeddedResource Include="Cs12.grammar" />` to `CSharpGrammar.csproj` after Cs11.
- [x] Add `new(12, "Cs12.grammar", "Cs12.grammar"),` to `EmbeddedGrammar.cs` version table.
- [x] Check `CSharpVersionInfrastructureTests.cs`: no test asserts the exact list for a version >= 12
      (only `LoadGrammarUpTo_11` loads up to 11) -> NO change needed.

## Code iterations
1. Wrote the rule as a re-declaration of `ClassDeclaration` with a REQUIRED
   `ClassPrimaryConstructorParameterList` between the greedy `TypeName` and `BaseList?`. First build
   of the test project FAILED with `NETSDK1022: Duplicate 'Compile' items` for
   `Cs11ParamsSpanTests.cs` + `Cs12PrimaryConstructorTests.cs`.
2. Root cause: the working-tree `CSharpGrammarTests.csproj` had been corrupted by prior subagent work —
   it contained an explicit `<ItemGroup>` with `<Compile Include="Cs11ParamsSpanTests.cs" />` and
   `<Compile Include="Cs12PrimaryConstructorTests.cs" />` (the exact anti-pattern the task warns about).
   The SDK already auto-includes all `.cs` files, so these were duplicates.
3. Fix: `git restore -- Tests/CSharpGrammarTests/CSharpGrammarTests.csproj` (revert to the committed
   state, which has NO explicit Compile items). After the revert, the build succeeded and all 15 new
   tests passed on the first run. No grammar change was needed — the rule was correct from the start.

## Version-purity results
- `CreateParser(12)` ACCEPTS `class MyClass(int x, string y) { }` (and base-list / empty / generic /
  nested / namespace forms).
- `CreateParser(11)` REJECTS `class MyClass(int x, string y) { }`, `class MyClass(int x) : Base { }`,
  and `class MyClass() { }` (the Cs12 alternative is absent at v11; Cs1/Cs2 both fail on the `(`).
- Regression: a REGULAR class (`class MyClass { }`) and a REGULAR generic class (`class MyClass<T> { }`)
  still parse at v12 (the Cs1 ClassDeclaration alternative remains reachable).

## Tests (pos/neg)
`Tests/CSharpGrammarTests/Cs12PrimaryConstructorTests.cs` — 15 tests, all pass.
- Positive (v12): 11 — Simple, WithBase, WithBodyMember, Empty, Public, MultipleBases, Generic,
  Nested, InNamespace, RegularClass_StillSucceedsAtV12, RegularGenericClass_StillSucceedsAtV12.
- Negative (v11 version-purity): 3 — RejectedAtV11, WithBase_RejectedAtV11, Empty_RejectedAtV11.
- Negative (v12 malformed): 1 — MissingCloseParen_Rejected.

## Verification
- `dotnet build Nitra.sln --no-incremental` -> 0 errors (fresh, noIncremental).
- `dotnet test Tests/CSharpGrammarTests` (fresh build) -> Total 1439, Passed 1436, Failed 0, Skipped 3.
- `dotnet test Tests/ParserTests` (regression) -> Total 327, Passed 325, Failed 0, Skipped 2.

## Files changed (T3.12.1 only)
- `Parsers/CSharp/CSharpGrammar/Cs12.grammar` (new)
- `Parsers/CSharp/CSharpGrammar/CSharpGrammar.csproj` (added `<EmbeddedResource Include="Cs12.grammar" />`)
- `Tests/CSharpGrammarTests/EmbeddedGrammar.cs` (added version 12 to the table)
- `Tests/CSharpGrammarTests/Cs12PrimaryConstructorTests.cs` (new, 15 tests)
- `docs/CSharpParserPlan-progressT3.12.1.md` (this file)
- `Tests/CSharpGrammarTests/CSharpGrammarTests.csproj` — REVERTED to committed state (removed the
  pre-existing duplicate-`<Compile>` corruption that caused NETSDK1022); ends up UNCHANGED vs HEAD.

## Working-tree hygiene
- Did NOT stage/commit anything (orchestrator commits).
- Did NOT touch `docs/CSharpParserPlan-checklist.md`, `docs/RecoveryImprovementPlan.md`,
  `docs/antlr4-analysis.md` (left as found).
- The test csproj was reverted (not modified from HEAD) so it is not part of the change set.
