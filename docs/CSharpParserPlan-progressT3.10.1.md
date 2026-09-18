# T3.10.1 — C# 10.0: file-scoped namespace

## Status

DONE (pending orchestrator verification + commit). Build green, CSharpGrammarTests green (1386/1389, 3 pre-existing skips), ParserTests green (325/327, 2 pre-existing skips).

## Roslyn syntax found

A file-scoped namespace is `FileScopedNamespaceDeclarationSyntax`, a sibling of the block-scoped
`NamespaceDeclarationSyntax` (both derive `BaseNamespaceDeclarationSyntax`).

- `ParseNamespaceDeclarationCore` — `C:\RSDN\roslyn\src\Compilers\CSharp\Portable\Parser\LanguageParser.cs:247-328`.
  After the `namespace` keyword + `ParseQualifiedName()`, the parser branches on the next token:
  - `SemicolonToken` (line 264-267): eat `;` -> **file-scoped** namespace;
  - `OpenBraceToken` / possible member (268-273): eat `{` -> **block-scoped** namespace.
  The file-scoped path calls `ParseNamespaceBody` (line 292, `parentKind = FileScopedNamespaceDeclaration`)
  and builds `FileScopedNamespaceDeclaration(attributeLists, modifiers, namespaceToken, name, semicolon,
  body.Externs, body.Usings, body.Members)` (295-303). The body (externs/usings/members) is the SAME
  member set a block-scoped namespace body holds; for file-scoped it runs to end-of-file.
- `FileScopedNamespaceDeclarationSyntax` — `C:\RSDN\roslyn\src\Compilers\CSharp\Portable\Generated\CSharpSyntaxGenerator\CSharpSyntaxGenerator.SourceGenerator\Syntax.xml.Syntax.Generated.cs:9789`:
  `NamespaceKeyword` (9813) + `Name` (9815) + `SemicolonToken` (9817) + `Externs`/`Usings`/`Members` (9819-9823).
- `SyntaxKind.FileScopedNamespaceDeclaration` — `Syntax/SyntaxKind.cs:800`; `IsNamespaceDeclaration` — `Syntax/SyntaxKindFacts.cs:399`.
- Version gate is a BINDER/semantic (C# 10.0) feature; the PARSER accepts the form wherever the rule
  exists. Version purity comes from the rule being Cs10-only.

## Rule written (Cs10.grammar)

```
NamespaceMember =
    | FileScopedNamespaceDeclaration;

FileScopedNamespaceDeclaration = "namespace" QualifiedName ";" NamespaceBody?;
```

- A NEW `NamespaceMember` alternative (T0.3 append-merge, as Cs9 appends to `TypeDeclaration`/
  `CompilationUnit`/`InterfaceMember`). A file-scoped namespace is a member (like the block-scoped
  `NamespaceDeclaration`, Cs1.grammar:17) whose body is the SAME `NamespaceBody` (Cs1.grammar:19,
  `NamespaceMember*`). The greedy body consumes the rest of the file (Roslyn: body runs to EOF, 292),
  so all subsequent using/extern/namespace/type members belong to it.
- Disambiguation `FileScopedNamespaceDeclaration` vs `NamespaceDeclaration` (longest-match, mutually
  exclusive): both start `"namespace" QualifiedName`; the token after the name disambiguates — `{` ->
  block-scoped (requires the literal `{`, Cs1.grammar:17), `;` -> file-scoped. A token cannot be both,
  so no equal-length tie.

## Setup steps

1. Created `Parsers/CSharp/CSharpGrammar/Cs10.grammar` (CRLF, no BOM).
2. Added `<EmbeddedResource Include="Cs10.grammar" />` to `CSharpGrammar.csproj` (after the Cs9 line).
3. Added `new(10, "Cs10.grammar", "Cs10.grammar"),` to the version table in `EmbeddedGrammar.cs`
   (between the v9 and v11 rows).
4. Updated `CSharpVersionInfrastructureTests.LoadGrammarUpTo_11_...`: count 10 -> 11, inserted
   `Cs10.grammar` at index 9, moved `Cs11.grammar` to index 10; renamed the test to
   `LoadGrammarUpTo_11_YieldsCs1Cs2Cs3Cs4Cs5Cs6Cs7Cs8Cs9Cs10Cs11InOrder`.

## Code iterations

1. **First attempt**: `FileScopedNamespaceDeclaration = "namespace" QualifiedName ";" NamespaceBody;`
   (body = `NamespaceMember*` directly). 10/11 Cs10 tests passed; `FileScopedEmptyBody_Succeeds`
   (`namespace A;`) FAILED with a FatalError at EOF (pos 12) whose expected set was the
   `NamespaceMember` first-tokens (Using/Extern/Namespace/Class/Struct/...).
2. **Diagnosis**: parsed rules directly via `ParseRuleOnce` for input `namespace A;`:
   `NamespaceMember@0` = Failure, `NamespaceBody@12` = Failure, `QualifiedName@10` = Success.
   The engine's zero-or-many reports a **Failure** when its element cannot match at the position
   (here: EOF for an empty body); that Failure is not swallowed, so the whole rule fails.
3. **Fix**: wrap the body in Optional — `... ";" NamespaceBody?;` — exactly like the block-scoped
   `NamespaceDeclaration` (Cs1.grammar:17) which already uses `NamespaceBody?`. The Optional swallows
   the empty-body Failure (NoneNode), so `namespace A;` alone now parses. All 11 Cs10 tests green.

## Version-purity results

- `CreateParser(10)` (Cs1..Cs10): ACCEPTS `namespace A; class C { }`, `namespace A;`,
  `namespace A; using System; class C { }`, multiple types, nested types, dotted names, and a
  using-before-namespace. (11 positive tests.)
- `CreateParser(9)` (Cs1..Cs9, no CS10): REJECTS `namespace A; class C { }`, `namespace A;`,
  `namespace A; using System; class C { }`. At v9 the `FileScopedNamespaceDeclaration` alternative is
  absent, so `namespace A;` matches no `NamespaceMember` (the block-scoped form needs `{`) -> unconsumed
  -> rejects. (3 negative tests.)
- Pre-existing v1 negative tests still green (Cs1 parser, `LoadCs1Grammar`):
  `Cs1RoslynDirectiveTests.Namespace_FileScopedSemicolon_Fails` / `...WithUsing_Fails` / `...WithExternAlias_Fails`.

## Tests (Cs10FileScopedNamespaceTests.cs)

- Positive (v10): 8 — simple single type, multiple types, nested type, empty body, using-inside,
  using-before, dotted name, block-scoped regression.
- Negative (v9, version-purity): 3 — single type, empty body, with-using.
- Total: 11.

## Verification

- `dotnet build Nitra.sln --no-incremental` -> 0 warnings, 0 errors.
- `dotnet test Tests/CSharpGrammarTests` (fresh build) -> Passed 1386, Failed 0, Skipped 3
  (pre-existing skips: RawStringLiteral_CloseRunPrefix_MatchLengthSemantics, StringLiteral_RawNewline_Fails,
  VerbatimStringLiteral_ExactBoundary_MatchesPrefixOnly). Total 1389.
- `dotnet test Tests/ParserTests` (regression) -> Passed 325, Failed 0, Skipped 2
  (pre-existing skips: RequiredSubruleNamesAreNotSpecified, ShouldReportErrorForUndefinedRuleReference).
  Total 327.

## Files changed

- `Parsers/CSharp/CSharpGrammar/Cs10.grammar` (new)
- `Parsers/CSharp/CSharpGrammar/CSharpGrammar.csproj` (added `<EmbeddedResource Include="Cs10.grammar" />`)
- `Tests/CSharpGrammarTests/EmbeddedGrammar.cs` (added v10 row)
- `Tests/CSharpGrammarTests/CSharpVersionInfrastructureTests.cs` (updated v11 list test + name)
- `Tests/CSharpGrammarTests/Cs10FileScopedNamespaceTests.cs` (new)
- `docs/CSharpParserPlan-progressT3.10.1.md` (this file)