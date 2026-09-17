# T3.9.1 — C# 9.0: records (Cs9.grammar)

Status: DONE (build 0 errors; CSharpGrammarTests 1337 passed / 0 failed / 3 skipped; ParserTests 325 passed / 0 failed / 2 skipped)

## Task
Set up `Cs9.grammar` and extend the grammar with the `record` type declaration (C# 9.0).
CS9 only: `CreateParser(8)` must REJECT a record; `CreateParser(9)` must accept it.

## Roslyn syntax found (C:\RSDN\roslyn, main, src/Compilers\CSharp\Portable)

File: `Parser/LanguageParser.cs`
- `ParseTypeDeclaration` dispatch (1763-1787): an `IdentifierToken` whose `ContextualKind` is
  `RecordKeyword` is routed to `ParseMainTypeDeclaration` (1780-1782).
- `ParseMainTypeDeclaration` (1789): the common parser for class/struct/interface/record/extension.
- `tryScanRecordStart` (1931-1963): eats the `record` keyword, then an optional `class`/`struct`
  keyword (`recordModifier = CurrentToken.Kind is ClassKeyword or StructKeyword ? EatToken() : null`,
  1936-1938).
- `name = ParseIdentifierToken()` (1821); `typeParameters = ParseTypeParameterList()` (1824).
- `paramList` (1827-1828): `CurrentToken.Kind == OpenParenToken ? ParseParenthesizedParameterList()
  : null` — the POSITIONAL parameter list is present only when `(` follows the name.
- `baseList` (1830): `ParseBaseList()` (the `: Base` list).
- `constraints` (1839-1843): only when `where` — out of scope here.
- body (1847-1914): `;` (semicolon, no body, 1850-1855) OR `{` members* `}` with an optional
  trailing `;` (openBrace 1858, closeBrace 1909-1911, `TryEatToken(Semicolon)` 1913).
- `constructTypeDeclaration` (1965-2078), `RecordKeyword` case (2038-2058): builds
  `RecordDeclaration` with `classOrStructKeyword: recordModifier` (the optional class/struct keyword).
- `record` is a CONTEXTUAL keyword (an `IdentifierToken` whose `ContextualKind` is `RecordKeyword`),
  recognized only when the feature is enabled (1622-1627). Hence it is NOT added to `ReservedKeyword`
  (it remains a valid type/identifier name, e.g. `class record { }`), consistent with the `partial`
  treatment (Cs2.grammar:214-227).

Version gate: `Errors/MessageID.cs`
- `IDS_FeatureRecords` (:211, :602, :609) -> `LanguageVersion.CSharp9` (a BINDER/semantic check).
  The PARSER accepts the form wherever the rule exists (CS9 only, per the plan); version purity comes
  from the rule being Cs9-only.

## The rule (Cs9.grammar)
```
TypeDeclaration =
    | RecordDeclaration;

RecordDeclaration = Attributes? RecordModifier* "record" RecordKind? TypeName RecordParameterList? BaseList? RecordBody;

RecordKind =
    | "class"
    | "struct";

RecordParameterList = "(" (Parameter; ",")* ")";

RecordBody =
    | RecordBlock = ClassBody ";"?
    | ";";

RecordModifier =
    | "public"
    | "private"
    | "protected"
    | "internal"
    | "abstract"
    | "sealed"
    | "unsafe";
```
Design notes:
- Re-declares `TypeDeclaration` to APPEND `RecordDeclaration` (T0.3 merge semantics).
- `RecordDeclaration` REQUIRES the `record` contextual keyword (REQUIRED-new-construct), so it is
  mutually exclusive with every existing TypeDeclaration alternative (each starts with a reserved
  keyword) -> no equal-length tie.
- `RecordKind` = the optional class/struct after `record` (Roslyn `recordModifier`).
- `RecordParameterList` reuses Cs1 `Parameter`; a dedicated rule because Cs1 `ParameterList`
  requires >= 1 element, whereas a record allows an empty `()`.
- `BaseList` reuses Cs1 `BaseList` (Cs1.grammar:81).
- `RecordBody` reuses Cs1 `ClassBody` for the block body (records hold class-like members) + optional
  trailing `;`, or a bare `;` (no body). Named union (meta-grammar forbids `|` inside a group).
- `RecordModifier` = access + abstract/sealed/unsafe (ClassModifier core). `partial` (CS2) and
  `static` are excluded (boundary decision: not in the task's minimal record set).
- Generics on records (`record Point<T>(...)`) are NOT modeled (out of scope, minimal); the greedy
  merged `TypeName` (Cs2.grammar:44) would consume `Point<T>` as a generic type name anyway.

## Setup steps done
- [x] Created `Parsers/CSharp/CSharpGrammar/Cs9.grammar` (CRLF, no BOM).
- [x] Added `<EmbeddedResource Include="Cs9.grammar" />` to `CSharpGrammar.csproj` after Cs8.
- [x] Added `new(9, "Cs9.grammar", "Cs9.grammar"),` to `Tests/CSharpGrammarTests/EmbeddedGrammar.cs`.

## Code iterations
- **Iteration 1 (the rule)**: Wrote `RecordDeclaration` + `RecordKind`/`RecordParameterList`/`RecordBody`/
  `RecordModifier` and re-declared `TypeDeclaration` to append `RecordDeclaration`. Built the full
  solution → 0 errors. All 23 new record tests passed on the first run (no rule-level rework needed).
- **Iteration 2 (infrastructure test)**: `dotnet test Tests/CSharpGrammarTests` showed 1 failure —
  `CSharpVersionInfrastructureTests.LoadGrammarUpTo_11_...` asserts the exact version-11 grammar list
  (expected 9 entries: Cs1..Cs8, Cs11). Adding Cs9 to the version table legitimately changed it to 10
  entries (Cs1..Cs9, Cs11). Fixed the test: count 9→10, inserted `Cs9.grammar` at index 8, moved
  `Cs11.grammar` to index 9, renamed the method + comment. This test directly tests the version table the
  task required modifying, so updating it is the correct (and only) way to keep it green. Re-ran → green.

## Version-purity results
- CS9 positives (CreateParser(9)): all record forms parse (positional/non-positional, class/struct, base
  lists, bodies, trailing `;`, attributes, modifiers, empty param list, nested, in-namespace).
- CS8 negatives (CreateParser(8)): `record Point(int X, int Y);`, `record struct Point(int X);`,
  `record class Point(int X);` all REJECT (the `RecordDeclaration` alternative is Cs9-only; no other
  TypeDeclaration alternative starts with the contextual `record` keyword). Version purity holds.

## Tests
- File: `Tests/CSharpGrammarTests/Cs9RecordsTests.cs` (23 tests, all green).
- Positives (v9): 18 — Record_Positional, Record_PositionalWithBase, Record_PositionalWithBody,
  Record_PositionalWithBaseAndBody, RecordClass, RecordStruct, Record_NonPositional,
  Record_NonPositionalWithBody, Record_EmptyParameterList, Record_SingleParameter,
  Record_TrailingSemicolon, Record_Public, Record_Attribute, Record_MultipleBases,
  Record_BodyWithMember, Record_Nested, RecordStruct_WithBaseAndBody, Record_InNamespace.
- Version-purity negatives (v8): 3 — Record_RejectedAtV8, RecordStruct_RejectedAtV8,
  RecordClass_RejectedAtV8.
- Malformed negatives (v9): 2 — Record_MissingName, Record_MissingCloseParen.

## Verification
- `dotnet build Nitra.sln --no-incremental` → Build succeeded, 0 errors.
- `dotnet test Tests/CSharpGrammarTests` → Passed: 1337, Failed: 0, Skipped: 3, Total: 1340.
- `dotnet test Tests/ParserTests` (regression) → Passed: 325, Failed: 0, Skipped: 2, Total: 327.

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs9.grammar` (new)
- `Parsers/CSharp/CSharpGrammar/CSharpGrammar.csproj`
- `Tests/CSharpGrammarTests/EmbeddedGrammar.cs`
- `Tests/CSharpGrammarTests/Cs9RecordsTests.cs` (new)
- `Tests/CSharpGrammarTests/CSharpVersionInfrastructureTests.cs` (version-11 list now includes Cs9)
- `docs/CSharpParserPlan-progressT3.9.1.md` (this file)
