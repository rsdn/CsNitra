# T3.14.1 — C# 14.0 `extension` container

## Status
DONE (pending orchestrator verification). The `extension` container (C# 14.0) is modeled in a new
`Cs14.grammar` and re-declared onto the `TypeDeclaration` union (T0.3 append merge, like the Cs9 record).
`CreateParser(14)` accepts it; `CreateParser(13)` rejects it (version purity). All pre-existing tests stay green.

## Task
Set up `Cs14.grammar` (new file) and extend the grammar with the `extension` container declaration (C# 14.0).
Roslyn syntax documented in `docs/CSharpParserPlan-progressT3.13.3.md` (READ FIRST).

## Setup steps (done)
1. Created `Parsers/CSharp/CSharpGrammar/Cs14.grammar` (CRLF / no BOM) with the header
   `// Cs14.grammar — C# 14.0: extension container (T3.14.1).`.
2. Added `<EmbeddedResource Include="Cs14.grammar" />` to `CSharpGrammar.csproj`, AFTER the
   `Cs13.grammar` line.
3. Added `new(14, "Cs14.grammar", "Cs14.grammar"),` to the `_versions` table in
   `Tests/CSharpGrammarTests/EmbeddedGrammar.cs`, AFTER the `new(13, ...)` line.
4. Checked `Tests/CSharpGrammarTests/CSharpVersionInfrastructureTests.cs`: NO test asserts the exact
   grammar list for a version >= 14 (tests exist only for 1/4/5/6/7/8/11). Nothing to update. The
   container tests exercise `CreateParser(14)`/`CreateParser(13)`, which implicitly verify the table loads Cs14.

## The rule written (Cs14.grammar)
```
TypeDeclaration =
    | ExtensionDeclaration;

ExtensionDeclaration = "extension" TypeParameterList? ExtensionParameterList ConstraintClause* ExtensionBody;

ExtensionParameterList = "(" (Parameter; ",")+ ")";

ExtensionBody =
    | ClassBody
    | ";";
```
- Re-declares `TypeDeclaration` (T0.3 append merge) to add `ExtensionDeclaration` — the same pattern as the
  Cs9 record (`Cs9.grammar:59-60`). The Cs1 `TypeDeclaration` union (Cs1.grammar:27-32) picks it up.
- `ExtensionDeclaration` = `extension` + optional `TypeParameterList` (Cs2.grammar:51) + REQUIRED
  `ExtensionParameterList` + optional `ConstraintClause*` (Cs2.grammar:107) + `ExtensionBody`.
- `ExtensionParameterList = "(" (Parameter; ",")+ ")"` — comma-separated, at least one (Roslyn
  `requireOneElement:true`). Mirrors Cs1 `ParameterList` (Cs1.grammar:465) = `(Parameter; ",")+` wrapped in
  parens. Reuses Cs1 `Parameter` (Cs1.grammar:472); the `this` modifier is the Cs3 feature (Cs3.grammar:251),
  present at v14.
- `ExtensionBody` = `ClassBody` (Cs1.grammar:102) | `";"` — a named union because the meta-grammar forbids
  `|` inside a group (CsNitraParser.cs:103); same workaround as Cs1 `MethodBody`.

### Double-paren resolution (intended-shape sketch)
The progress file's intended shape showed `"(" ExtensionParameterList ")"` in the main rule AND
`ExtensionParameterList = "(" Parameter+ ")"` — a double-paren contradiction. Resolved by letting
`ExtensionParameterList` CARRY the literal parens and having `ExtensionDeclaration` reference it WITHOUT
extra literal parens. Functionally identical to the sketch; the sketch's `(ClassBody | ";")` group was likewise
replaced by the `ExtensionBody` named union (meta-grammar constraint). The sketch's `Parameter+` was corrected
to `(Parameter; ",")+` in iteration v2 (see Code iterations) so comma-separated parameter lists parse.

## Why no `extension` reservation is needed (version purity)
`extension` is NOT a reserved keyword (absent from Cs1 `ReservedKeyword`). In a member/namespace position,
a declaration LEADING with `extension` followed by `(` matches ONLY `ExtensionDeclaration`: the Cs1
`ClassDeclaration`/`StructDeclaration`/... alternatives all require a declaration keyword (class/struct/...)
after optional attributes/modifiers, so they FAIL on `extension` (no equal-length tie). At v<=13 the
`ExtensionDeclaration` alternative is ABSENT, so `extension ( ... )` matches no `TypeDeclaration` alternative
and REJECTS. No `ReservedKeyword` re-declaration required (keeps the change minimal, per the task).

## Code iterations
- v1 (initial): wrote the rule with `ExtensionParameterList = "(" Parameter+ ")"`. Build green. Tests: 10/11
  passed; `ExtensionContainer_MultipleParameters_Succeeds` (`extension (this int x, int y) { }`) FAILED at the
  comma (errorPos=21): `Parameter+` has NO comma separator, so it stops after the first parameter and then
  expects `)` but finds `,`.
- v2 (fix): changed `ExtensionParameterList` to `"(" (Parameter; ",")+ ")"` (comma-separated one-or-more,
  mirroring Cs1 `ParameterList`). Re-normalized CRLF. Build green. All 11 tests pass.
- Working-tree hygiene: `CSharpGrammarTests.csproj` carried a LEFTOVER `<Compile Include="Cs14ExtensionContainerTests.cs" />`
  from a previous T3.14.1 attempt → NETSDK1022 (duplicate Compile; the SDK auto-includes all .cs). Reverted the
  csproj to HEAD (not a T3.14.1 file; the SDK glob picks up the new test file). Build then green.
- Confirmed `this int x` parses as a `Parameter` at v14 (`this` = Cs3 `ParameterModifier`).
- Confirmed `extension () { }` REJECTS (`(Parameter; ",")+` requires >= 1 element).

## Version-purity results
- `CreateParser(14)` ACCEPTS: simple body, empty body, semicolon body, type params, constraints,
  multiple members, multiple parameters.
- `CreateParser(13)` REJECTS: simple body, type params, semicolon body (ExtensionDeclaration absent at v13).
- `CreateParser(14)` REJECTS (malformed): `extension () { }` (empty parameter list).

## Tests (Cs14ExtensionContainerTests.cs)
- Positive (v14): 7 (simple, empty body, semicolon body, type params, constraints, multiple members,
  multiple parameters).
- Version-purity (v13): 3 (simple, type params, semicolon body).
- Negative/malformed (v14): 1 (empty parameter list).
- Total: 11.

## Verification
- `dotnet build Nitra.sln --no-incremental` → 0 errors, 0 warnings.
- `dotnet test Tests/CSharpGrammarTests` (fresh build) → 1507 passed, 0 failed, 3 skipped, 1510 total. ALL green.
- `dotnet test Tests/ParserTests` (regression) → 325 passed, 0 failed, 2 skipped, 327 total. ALL green.

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs14.grammar` (NEW).
- `Parsers/CSharp/CSharpGrammar/CSharpGrammar.csproj` (added `<EmbeddedResource Include="Cs14.grammar" />`).
- `Tests/CSharpGrammarTests/EmbeddedGrammar.cs` (added version-14 table row).
- `Tests/CSharpGrammarTests/Cs14ExtensionContainerTests.cs` (NEW).
- `docs/CSharpParserPlan-progressT3.14.1.md` (NEW, this file).

### Working-tree cleanup (not a T3.14.1 deliverable)
- `Tests/CSharpGrammarTests/CSharpGrammarTests.csproj` — REVERTED to HEAD. A previous T3.14.1 attempt left a
  stray `<Compile Include="Cs14ExtensionContainerTests.cs" />` that caused NETSDK1022. Reverting removes it; the
  SDK auto-includes the new test file, so the build is clean. NOT staged as a T3.14.1 change.
