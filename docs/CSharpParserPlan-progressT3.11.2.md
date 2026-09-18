# T3.11.2 — Generic attributes (C# 11.0)

Status: DONE (build green, all tests green). Checklist item left as `[~]` for the orchestrator to
mark `[✅]` after verifying and committing.

## Task
Extend the grammar with generic attributes: an attribute whose attribute type carries a type
argument list.

    [MyAttribute<int>] class MyClass { }
    [MyAttribute<int, string>] class MyOtherClass { }

## Roslyn syntax (C:\RSDN\roslyn, src/Compilers/CSharp/Portable/Parser/LanguageParser.cs)
- `ParseAttribute` (1181-1191): `Attribute(name, argumentList)` where name = `ParseQualifiedName()`.
- `ParseQualifiedName` (7031-7048): `ParseAliasQualifiedName()` then `.` / `::` segments.
- `ParseSimpleName` (6182-6218): identifier, then if followed by `<`, scan a type argument list and
  build a `GenericName` (identifier + `TypeArgumentList`). This is the generic-attribute shape.
- `ParseTypeArgumentList` (6545): `<` (Type,)+ `>`.

## The rule (Cs11.grammar)
    AttributeTypeName = !ReservedKeyword Identifier "<" (Type; ",")+ ">";

## Key finding / why Cs1.grammar changed
The existing `AttributeName = TypeName NamespaceSegment*` (Cs1.grammar:480) already used the generic
Cs2 `TypeName`, so generic attributes ALREADY parsed at v10 (and v2+). Empirically: `CreateParser(10)`
accepted `[MyAttribute<int>]`. That violated the C# 11-only version-purity requirement. So the
attribute type name was decoupled from the generic `TypeName`:

Cs1.grammar:
    AttributeName = AttributeTypeName AttributeNamespaceSegment*;   // was: TypeName NamespaceSegment*
    AttributeTypeName = !ReservedKeyword Identifier;                 // non-generic, C# 1-10
    AttributeNamespaceSegment = "." AttributeTypeName;

Cs11.grammar:
    AttributeTypeName = !ReservedKeyword Identifier "<" (Type; ",")+ ">";   // generic, C# 11

At v<=10 `AttributeTypeName` is non-generic, so the `<` of a type argument list is left unconsumed and
the attribute list fails (v10 rejects). At v11 the generic alternative wins (longest-match, no tie: the
non-generic alt stops at the identifier, the generic alt consumes `<...>`). `AttributeNamespaceSegment`
mirrors `NamespaceSegment` but stays tied to the attribute name, so qualified generic attributes
(`[N.List<T>]`) are also C# 11-only.

## Code iterations
1. Wrote the test first (`Cs11GenericAttributeTests.cs`). Ran it: v11 positives passed (2), v10
   negatives FAILED (2) — v10 accepted generic attributes. Confirmed the feature already existed at v10
   via the generic `TypeName` in `AttributeName`.
2. First grammar attempt: Cs11 rule used the Cs2 `TypeArgumentList` rule
   (`AttributeTypeName = !ReservedKeyword Identifier TypeArgumentList;`). Build of the full test suite
   FAILED: `Cs11.grammar:56: Symbol 'TypeArgumentList' not found` — `RawStringLiteralRuleTests` (and
   similar) load only Cs1+Cs11 (no Cs2), so the Cs2 rule is unavailable there.
3. Fix: inlined the type argument list in the Cs11 rule so its dependencies are confined to Cs1
   (`ReservedKeyword`, `Identifier` terminal, `Type`):
   `AttributeTypeName = !ReservedKeyword Identifier "<" (Type; ",")+ ">";`
   Body identical to Cs2 `TypeArgumentList`. Full suite green.

## Build blocker (working tree)
The test csproj had been modified in the working tree (not committed) to add explicit `<Compile Include>`
items (including `Cs11GenericAttributeTests.cs`), which collide with SDK auto-include → NETSDK1022
duplicate-Compile. Restored the csproj to its committed (clean) state (`git checkout -- ...csproj`); the
SDK auto-includes the new test file. No csproj content was added.

## Version purity
- `CreateParser(11)`: `[MyAttribute<int>]` and `[MyAttribute<int, string>]` → ACCEPT (2 pos tests).
- `CreateParser(10)`: same inputs → REJECT (2 neg tests).

## Tests (Cs11GenericAttributeTests.cs)
- Positive (v11): `GenericAttribute_SingleTypeArg_Succeeds`, `GenericAttribute_MultipleTypeArgs_Succeeds`.
- Negative (v10): `GenericAttribute_SingleTypeArg_RejectedAtV10`,
  `GenericAttribute_MultipleTypeArgs_RejectedAtV10`.
- 4 tests, all green.

## Verification
- `dotnet build Nitra.sln --no-incremental` → 0 errors.
- `dotnet test Tests/CSharpGrammarTests` → Passed: 1404, Failed: 0, Skipped: 3, Total: 1407.
- `dotnet test Tests/ParserTests` → Passed: 325, Failed: 0, Skipped: 2, Total: 327.

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs1.grammar` (AttributeName decoupled; +AttributeTypeName,
  +AttributeNamespaceSegment)
- `Parsers/CSharp/CSharpGrammar/Cs11.grammar` (+generic AttributeTypeName)
- `Tests/CSharpGrammarTests/Cs11GenericAttributeTests.cs` (new, 4 tests)
- `docs/CSharpParserPlan-progressT3.11.2.md` (this file)
- `docs/CSharpParserPlan-checklist.md` (T3.11.2 `[~]`, left for orchestrator)
