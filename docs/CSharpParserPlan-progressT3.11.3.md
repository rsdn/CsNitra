# T3.11.3 — `required` modifier (C# 11.0)

Status: DONE (build green, all tests green). Checklist item left as `[~]` for the orchestrator to
mark `[✅]` after verifying and committing.

## Task
Extend the grammar with the `required` modifier (a new modifier on properties and fields):

    class Person {
        public required string Name { get; set; }
        public required int Age;
    }

## Roslyn syntax (C:\RSDN\roslyn, src/Compilers/CSharp/Portable)
- `required` is a CONTEXTUAL keyword (NOT reserved): `SyntaxKindFacts.cs:1427-1428`
  (`case "required": return SyntaxKind.RequiredKeyword;` in the contextual-kind switch;
  `SyntaxKind.RequiredKeyword = 8447`, SyntaxKind.cs:420).
- `LanguageParser.cs:1324-1332` (`GetModifierExcludingScoped`): an `IdentifierToken` whose
  `ContextualKind` is `RequiredKeyword` maps to `DeclarationModifiers.Required` (1331-1332),
  inside the generic any-order `ParseModifiers` loop (1347).
- `ModifierUtils.cs:352-353` (`Required` -> text) and `:409-410` (`RequiredKeyword` ->
  `DeclarationModifiers.Required`); `:113` (`checkFeature(DeclarationModifiers.Required,
  IDS_FeatureRequiredMembers)`).
- Version gate: `MessageID.cs:549` (`IDS_FeatureRequiredMembers`) -> `LanguageVersion.CSharp11`
  (`MessageID.cs:560`). `LanguageParser.cs:1420` and `SourceMemberContainerSymbol.cs:532` report
  the feature.
- `DeclarationTreeBuilder.cs:1050`: `modifiers.Any((int)SyntaxKind.RequiredKeyword)`.
- Syntax tests: `Test/Syntax/Parsing/MemberDeclarationParsingTests.cs` (many
  `N(SyntaxKind.RequiredKeyword)` nodes, e.g. 1166/1222/1281).

## The rule (Cs11.grammar)
`required` is APPENDED to the Cs1 `FieldModifier` and `PropertyModifier` unions (T0.3 merge
semantics — the same pattern as `partial` in Cs2.grammar T3.1.3 and `this` in Cs3.grammar T3.2.4):

    FieldModifier =
        | "required";

    PropertyModifier =
        | "required";

No Cs1 change. `required` is NOT added to ReservedKeyword (it is contextual — `int required;` /
`class required { }` are valid C# where `required` is a plain identifier).

## Code iterations
1. Wrote the rule first (Cs11.grammar): appended `| "required"` to the Cs1 `FieldModifier` and
   `PropertyModifier` unions (T0.3 merge — the exact `partial`/`this` pattern), plus the test file
   `Cs11RequiredModifierTests.cs`. No Cs1 change; `required` NOT added to ReservedKeyword.
2. First build (`dotnet build Nitra.sln --no-incremental`) → 0 errors on the first try (no grammar
   symbol-resolution issues: the rule references only the already-present `FieldModifier`/
   `PropertyModifier` names and a single word literal).
3. Ran the full CSharpGrammarTests + ParserTests: all green, 0 failures. No iteration needed — the
   append-merge of a contextual-keyword literal onto the modifier unions is sufficient (the Cs3
   auto-`Property` and Cs1 `Field`/`Property` all route through the merged `PropertyModifier*` /
   `FieldModifier*`).
4. Encoding: the new test file was written LF/no-BOM by the editor; converted to CRLF + UTF-8 BOM
   (matching `Cs11GenericAttributeTests.cs`). Cs11.grammar stayed CRLF/no-BOM (the edit preserved
   CRLF).

## Version purity
- `CreateParser(11)`: `public required string Name { get; set; }` (Cs3 auto-property) and
  `public required int Age;` (Cs1 field), plus the block-body property and multi-declarator field
  forms → ACCEPT. `int required;` (a field NAMED `required`) still ACCEPTS (contextual keyword
  preserved — not over-reserved).
- `CreateParser(10)`: `public required string Name { get; set; }`, `public required int Age;`, and
  the block-body property form → REJECT. At v10 `required` is not a FieldModifier/PropertyModifier,
  so it falls into the Type position; the following reserved type-name / variable-declarator
  (`string`/`int`) then fails, so no ClassMember alternative matches and the class body fails.

## Tests (Cs11RequiredModifierTests.cs)
- Positive (v11): `RequiredProperty_Auto_Succeeds`, `RequiredField_Succeeds`,
  `RequiredProperty_BlockBody_Succeeds`, `RequiredField_MultiDeclarator_Succeeds`,
  `Required_AsFieldName_Succeeds` (5).
- Negative (v10): `RequiredProperty_Auto_RejectedAtV10`, `RequiredField_RejectedAtV10`,
  `RequiredProperty_BlockBody_RejectedAtV10` (3).
- 8 tests, all green (verified via `--filter` on the class).

## Verification
- `dotnet build Nitra.sln --no-incremental` → 0 errors.
- `dotnet test Tests/CSharpGrammarTests` → Passed: 1412, Failed: 0, Skipped: 3, Total: 1415
  (baseline T3.11.2 was 1407; +8 = the new tests).
- `dotnet test Tests/ParserTests` → Passed: 325, Failed: 0, Skipped: 2, Total: 327
  (identical to the T3.11.2 baseline — no regression).
- Full solution `dotnet test Nitra.sln` → Total: 1752, Passed: 1747, Failed: 0, Skipped: 5.

## Build blocker (working tree) — restored, not a code change
On start the working tree was dirty: `CSharpGrammarTests.csproj` carried an explicit
`<Compile Include="Cs11RequiredModifierTests.cs" />` (the NETSDK1022 duplicate-Compile trap — the
SDK auto-includes all `.cs`). Restored it to its committed state (`git checkout -- ...csproj`); a
fresh build + test run after the restore is still green, confirming the SDK auto-includes the new
test file. No csproj content was added. (The `docs/CSharpParserPlan-checklist.md` modification —
T3.11.2 `[~]`→`[✅]`, T3.11.3 `[ ]`→`[~]` — was pre-existing from the T3.11.2 subagent; left as-is.)

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs11.grammar` (+`required` in FieldModifier/PropertyModifier)
- `Tests/CSharpGrammarTests/Cs11RequiredModifierTests.cs` (new, 8 tests)
- `docs/CSharpParserPlan-progressT3.11.3.md` (this file)
- `docs/CSharpParserPlan-checklist.md` (T3.11.3 `[~]`, left for orchestrator)
- `Tests/CSharpGrammarTests/CSharpGrammarTests.csproj` — RESTORED to committed state (no net change)
