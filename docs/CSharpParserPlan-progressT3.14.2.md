# T3.14.2 — C# 14.0 `ref` fields

## Status
DONE (pending orchestrator verification). `Cs14.grammar` models the `ref` field (C# 14.0) by appending
`"ref"` to the `FieldModifier` union (T0.3 append merge). `CreateParser(14)` accepts `ref int _value;`
(and `ref readonly int _value;`); `CreateParser(13)` rejects it (version purity). All pre-existing tests
stay green (CSharpGrammarTests 1518 passed, ParserTests 325 passed).

## Task
Extend the grammar with `ref` fields (C# 14.0). A `ref` field is a field declared with the `ref`
modifier:
```csharp
class MyClass {
    ref int _value;
}
```

## The Roslyn syntax (found)
Roslyn source (`C:\RSDN\roslyn`, main, `src/Compilers/CSharp/Portable`):
- `LanguageParser.cs:1322-1323` — `GetModifierExcludingScoped`: `case SyntaxKind.RefKeyword:
  return DeclarationModifiers.Ref;`. So `ref` is a general declaration modifier accepted by the
  generic any-order `ParseModifiers` loop (LanguageParser.cs:1347). A `ref` field is parsed by the
  STANDARD field-declaration path, NOT a special rule.
- `LanguageParser.cs:5200-5220` — `ParseNormalFieldDeclaration(attributes, modifiers, type,
  parentKind)`: the ordinary field parser (VariableDeclaration + `;`). The `ref` rides in the
  `modifiers` list; no dedicated `ref`-field parser exists.
- `LanguageParser.cs:3429-3470` — `IsFieldDeclaration`: a field is `identifier` + (not
  `.`/`::`/`<`/`{`/`=>`/`(`), so `ref int x;` is recognized as a field once the `ref` modifier is
  consumed.
- `LanguageParser.cs:2475` — `IsTypeStart`: `RefKeyword` is a type-start (the binder folds `ref`
  /`ref readonly` into the type via `SkipRefInField`).
- `SourceMemberFieldSymbol.cs:533-534` — `SkipRefInField(out refKind)`;
  `Debug.Assert(refKind is RefKind.None or RefKind.Ref or RefKind.RefReadOnly)`. So BOTH
  `ref int x;` (RefKind.Ref) and `ref readonly int x;` (RefKind.RefReadOnly) are valid C#.
- `MessageID.cs:262` — `IDS_FeatureRefFields = MessageBase + 12826`; `MessageID.cs:557` — a
  "semantic check" (binder) feature, so the PARSER accepts the form and the BINDER enforces the
  version. Consistent with this grammar's parser-vs-binder split.

## The rule written (Cs14.grammar)
```
FieldModifier =
    | "ref";
```
Re-declares `FieldModifier` (T0.3 append merge) to ADD `"ref"` to the Cs1 union
(Cs1.grammar:155-165) + the Cs11 `required` (Cs11.grammar:93). The Cs1 `Field`
(Cs1.grammar:148) = `Attributes? FieldModifier* Type VariableDeclarator ("," VariableDeclarator)* ";"`
then consumes `ref` as a field modifier and parses the type + declarator.

Why a modifier (not a RefType): the grammar models `ref` as a MODIFIER everywhere — Cs7 `MethodModifier`
(`ref` return, Cs7.grammar:237) and Cs7 `StructModifier` (`ref struct`, Cs7.grammar:512). Modeling `ref`
as a `FieldModifier` is the consistent, minimal approach (re-declaring `Type` to carry a `ref` prefix would
be invasive and risk regressions across every type position).

## Code iterations
- v1 (initial): appended `FieldModifier = | "ref";` to `Cs14.grammar` (after the `ExtensionBody` rule).
  `dotnet build Nitra.sln --no-incremental` → 0 errors.
- Build/test blocker (working-tree hygiene, NOT a T3.14.2 deliverable): the first `dotnet test` failed with
  `NETSDK1022: Duplicate 'Compile' items ... 'Cs14RefFieldTests.cs'`. The working-tree
  `Tests/CSharpGrammarTests/CSharpGrammarTests.csproj` carried a LEFTOVER `<Compile Include="Cs14RefFieldTests.cs" />`
  (a stray `<ItemGroup>` added by a previous T3.14.2 attempt). The SDK auto-includes all `.cs`, so the explicit
  item duplicates. Fixed by reverting the csproj to HEAD (`git checkout -- .../CSharpGrammarTests.csproj`),
  exactly as T3.14.1 did for its leftover. The csproj is NOT a T3.14.2 change and is left unmodified at HEAD.
- v2 (after csproj revert): `dotnet test` (specific class) → 11/11 pass. Re-encoded the new test file to
  CRLF + UTF-8 BOM (the `write` tool emitted LF / no-BOM; converted per the test-file convention).

## Version-purity results
- `CreateParser(14)` ACCEPTS: `class C { ref int _value; }`, `class C { ref readonly int _value; }`,
  `ref struct S { ref int _value; }`, `class C { public ref int _value; }`.
- `CreateParser(13)` REJECTS: `class C { ref int _value; }`, `class C { ref readonly int _value; }`,
  `ref struct S { ref int _value; }` (at v13 `ref` is not a FieldModifier; Type=ref fails — ref is reserved).
- `CreateParser(14)` REJECTS (malformed): `class C { ref int; }` (missing variable-declarator name).
- Regression (v14, stay green): `ref int M() { return 0; }` (ref RETURN method still routes to Method, not
  Field), `readonly int _x;` (plain readonly field), `ref struct S { }` (empty ref struct).

## Tests (Cs14RefFieldTests.cs)
- Positive (v14): 4 (simple, ref readonly, in a ref struct, with an access modifier).
- Version-purity (v13): 3 (simple, ref readonly, in a ref struct).
- Regression (v14): 3 (ref-return method, readonly field, empty ref struct).
- Negative/malformed (v14): 1 (missing declarator name).
- Total: 11.

## Verification
- `dotnet build Nitra.sln --no-incremental` → 0 errors, 0 warnings.
- `dotnet test Tests/CSharpGrammarTests` (fresh build) → 1518 passed, 0 failed, 3 skipped, 1521 total. ALL green
  (baseline 1510 + 11 new).
- `dotnet test Tests/ParserTests` (regression) → 325 passed, 0 failed, 2 skipped, 327 total. ALL green
  (matches the T3.14.1 baseline exactly).

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs14.grammar` (append `FieldModifier = | "ref";` + doc comment).
- `Tests/CSharpGrammarTests/Cs14RefFieldTests.cs` (NEW, CRLF + UTF-8 BOM).
- `docs/CSharpParserPlan-progressT3.14.2.md` (NEW, this file).
- `docs/CSharpParserPlan-checklist.md` (T3.14.2 line already `[~]` by the orchestrator; left unmarked per the
  task — the orchestrator marks `[✅]` after verifying/committing).

### Working-tree cleanup (not a T3.14.2 deliverable)
- `Tests/CSharpGrammarTests/CSharpGrammarTests.csproj` — REVERTED to HEAD. A previous T3.14.2 attempt left a
  stray `<Compile Include="Cs14RefFieldTests.cs" />` that caused NETSDK1022. Reverting removes it; the SDK
  auto-includes the new test file, so the build is clean. NOT staged as a T3.14.2 change.
