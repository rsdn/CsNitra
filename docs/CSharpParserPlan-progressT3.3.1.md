# T3.3.1 — C# 4.0 `dynamic` (predefined type)

Status: done.

## Goal
Add C# 4.0 `dynamic` (a predefined type usable in any type position) to a NEW grammar file
`Parsers/CSharp/CSharpGrammar/Cs4.grammar`. CS4 only. `dynamic` is a CONTEXTUAL keyword: a plain
identifier in C# 1.0–3.0, a keyword in C# 4.0+.

## Baseline (before changes)
- `dotnet build Nitra.sln --no-incremental` → success, 0 errors.
- `dotnet test Tests/CSharpGrammarTests` → **969 total / 966 passed / 0 failed / 3 skipped** (inferred:
  final 982 minus the 13 new `Cs4DynamicTests`; the 3 `CSharpVersionInfrastructureTests` count tests
  passed against the pre-Cs4 version table).
- `dotnet test Tests/ParserTests` (regression) → **327 total / 325 passed / 0 failed / 2 skipped**.

## Cs1 Type / PredefinedType / TypeName / ReservedKeyword structure (found)
- `TypeName = !ReservedKeyword Identifier;` (Cs1.grammar:23)
- `QualifiedName = TypeName NamespaceSegment*;` (Cs1.grammar:21); `NamespaceSegment = "." TypeName;` (Cs1:25)
- `Type = PredefinedType | QualifiedName | PointerType = Type : TypePointer "*" | ArrayType = Type : TypeArray ArrayRankSpecifier;` (Cs1:508-512)
- `PredefinedType` = the 16 predefined types: bool/byte/char/decimal/double/float/int/long/object/sbyte/short/string/uint/ulong/ushort/void (Cs1:515-531). `dynamic` is NOT in this list.
- `ReservedKeyword` = the C# 1.0 reserved-keyword set (Cs1:537-618). `dynamic` and `var` are NOT in this list (contextual keywords — see T3.1.2/T3.1.3).
- Every type position uses `Type`:
  - `Field = Attributes? FieldModifier* Type VariableDeclarator ("," VariableDeclarator)* ";"` (Cs1:148)
  - `Property = Attributes? PropertyModifier* Type TypeName "{" AccessorDeclaration+ "}" ";"?` (Cs1:188) and the Cs3 auto-property `Property = ... Type TypeName "{" AutoPropertyAccessorList "}" ("=" Expression)? ";"?` (Cs3:176)
  - `Method = Attributes? MethodModifier* Type TypeName "(" ParameterList? ")" MethodBody` (Cs1:265)
  - `Parameter = Attributes? ParameterModifier* Type TypeName` (Cs1:448)
  - `LocalVariableDeclaration = Type VariableDeclarator ("," VariableDeclarator)* ";"` (Cs1:807)
  - `VariableDeclarator = !ReservedKeyword Identifier ("=" Expression)?` (Cs1:811)
- `Identifier` terminal = `[_\l]\w*` (CSharpTerminals.cs:8-9) — matches `dynamic`.

## `dynamic` version-purity analysis (tie-avoidance)
`dynamic` is the SAME challenge as `var` (T3.1.2): a name that is a valid `TypeName` at an older
version but a keyword at a newer one.

- **v3 (Cs1+Cs2+Cs3)**: `dynamic` is NOT reserved (absent from Cs1 `ReservedKeyword`, not appended in
  Cs2/Cs3) → a valid `TypeName` (`!ReservedKeyword Identifier`) → `dynamic x = 5;` parses as a
  type-NAME declaration (type `dynamic`), exactly like `var x = 5;` at v1. `dynamic` is NOT a
  `PredefinedType` at v3.
- **v4 (Cs1+Cs2+Cs3+Cs4)**: `dynamic` IS reserved (appended below) → NOT a `TypeName` → the
  `QualifiedName` route of `Type` fails; `dynamic` IS a `PredefinedType` (appended below) → the
  `PredefinedType` route matches. Only ONE `Type` alternative matches `dynamic` → NO equal-length tie.

**Chosen approach (Option A)** — two Cs4 re-declarations:
1. `ReservedKeyword = | "dynamic";` — append (T0.3 merge): reserves `dynamic` at v4+ only.
2. `PredefinedType = | "dynamic";` — append: makes `dynamic` a valid `Type` at v4+.

Because `dynamic` is a full `Type` at v4, it flows through EVERY type position (field/property/method
return type/method parameter/local variable/array-of-dynamic/...) via the EXISTING Cs1/Cs3 rules —
no per-position re-declaration is needed. This is the key difference from `var`: `var` is NOT a type
(it is only a local-declaration marker), so T3.1.2 needed a dedicated `LocalVariableDeclaration =
"var" ...` alternative. `dynamic` needs no such alternative — the Cs1 `Type`-based
`LocalVariableDeclaration` already handles `dynamic y = x;` at v4.

**Why not add `dynamic` to `Type` directly?** Adding `Type = | "dynamic"` would only be tie-free if
`dynamic` were also reserved (otherwise the `QualifiedName`/`TypeName` route matches `dynamic` at the
same 7-char length → tie). Reserving + adding to `PredefinedType` is the minimal, semantically-correct
change (`dynamic` is a predefined type in the C#-spec sense) and a leaf node (no `.`/`*`/`[` to
confuse with a qualified/pointer/array type).

## Mutual-exclusivity hand-traces (no equal-length tie)
- `dynamic x = 5;` (field, v4): `Field` → `Type`: `PredefinedType` matches `dynamic` (7); `QualifiedName`
  → `TypeName` → `!ReservedKeyword` fails (dynamic reserved) → fails. Only PredefinedType → no tie.
  `VariableDeclarator = x = 5`; `;`. **Matches.**
- `dynamic x = 5;` (field, v3): `Type`: `PredefinedType` fails (dynamic not in the 16); `QualifiedName`
  → `TypeName` → `!ReservedKeyword` ok (dynamic not reserved) + `Identifier=dynamic` → `dynamic` (7).
  Only QualifiedName → no tie. Parses as a type-NAME field. **Matches (type-name declaration).**
- `void M(dynamic x) { }` (param, v4): `Method` → `Type=void`; `TypeName=M`; `(`; `Parameter` → `Type`:
  `PredefinedType=dynamic` (7) / `QualifiedName` fails (reserved) → no tie; `TypeName=x`; `)`; Block. **Matches.**
- `dynamic M() { return 5; }` (return type, v4): `Method` → `Type=dynamic` (PredefinedType); `TypeName=M`;
  `()`; Block `{ return 5; }`. `Field` alt: `Type=dynamic`, `VariableDeclarator=M`, then expects `;`/`,`/`=`
  but sees `(` → fails. Only Method → no tie. **Matches.**
- `void M() { dynamic y = x; }` (local, v4): `LocalVariableDeclaration` → `Type=dynamic` (PredefinedType,
  QualifiedName fails — reserved); `VariableDeclarator = y = x`; `;`. **Matches.**
- `dynamic P { get; set; }` (auto-property, v4): Cs3 `Property` → `Type=dynamic`; `TypeName=P`;
  `{ get; set; }` (AutoPropertyAccessorList). **Matches.**
- `class C { int dynamic; }` (field NAME, v3): `dynamic` is a plain identifier at v3 (not reserved) →
  `Field` → `Type=int`; `VariableDeclarator = dynamic` (`!ReservedKeyword Identifier` ok). **Matches** (must stay green).
- `class C { int dynamic; }` (field NAME, v4): at v4 `dynamic` IS reserved → `VariableDeclarator`
  (`!ReservedKeyword Identifier`) fails at `dynamic`; no other Field reading works → **rejects** at v4
  (expected: `dynamic` is a keyword at v4, cannot be a name). Not a required test; documented.

## Roslyn references (C:\RSDN\roslyn, main)
- `dynamic` is a CONTEXTUAL keyword (IdentifierToken): `src/Compilers/CSharp/Portable/Syntax/SyntaxKindExtensions.cs:48`
  ("Note that 'dynamic' is a contextual keyword, so it should never show up here."). Not a hard reserved
  keyword → a plain identifier in C# 1.0–3.0.
- At the SYNTAX level Roslyn parses `dynamic` as an `IdentifierName` (a type name), NOT a PredefinedType:
  `IsPredefinedType` (`src/Compilers/CSharp/Portable/Syntax/SyntaxKindFacts.cs:316-340`) lists the 16
  predefined types and does NOT include dynamic.
- The built-in dynamic type is a BINDER concept: `src/Compilers/CSharp/Portable/Symbols/DynamicTypeSymbol.cs`
  (`ToString() => "dynamic"`); `src/Compilers/CSharp/Portable/Binder/Binder_Symbols.cs:897` (an
  IdentifierName "dynamic" with no user type by that name resolves to the built-in dynamic type). So in
  Roslyn the CS4 feature is a parser-vs-binder split.
- **Deliberate deviation**: our grammar models the version-purity at the PARSE level (`dynamic` is a
  PredefinedType at v4, a TypeName at v3) for consistency with the established `var` (T3.1.2) and
  `partial` (T3.1.3) precedent, rather than Roslyn's pure binder-level handling.
- Syntax tests (C:\RSDN\roslyn, main, src/Compilers\CSharp\Test\Syntax\Parsing):
  - `StatementParsingTests.cs:313 TestLocalDeclarationStatementWithDynamic` — `dynamic a;` (a local
    declaration whose `Type` is an `IdentifierName` "dynamic", no initializer; asserts 0 parse errors and
    `Declaration.Type.Kind() == IdentifierName`). Adapted → `class C { void M() { dynamic a; } }`.
  - `RoundTrippingTests.cs:1528` — `public delegate void TypeName<T>(ref T t, dynamic d);` (a `dynamic d`
    parameter). Adapted → `class C { delegate void D(dynamic d); }`.
  - `RoundTrippingTests.cs:1529` — `public delegate Y @dynamic<X, Y>(X u, params dynamic[] ary);`
    (`params dynamic[]` — an array of dynamic as a parameter type; also shows `@dynamic` as a verbatim
    identifier type NAME, distinct from the dynamic keyword). Adapted → `class C { void M(params dynamic[] args) { } }`.

## Cs4.grammar rules
- `ReservedKeyword = | "dynamic";` (append — reserved at v4+).
- `PredefinedType = | "dynamic";` (append — predefined type at v4+).

## Setup steps
- [x] Create `Parsers/CSharp/CSharpGrammar/Cs4.grammar` (CRLF, no BOM).
- [x] Add `<EmbeddedResource Include="Cs4.grammar" />` to `CSharpGrammar.csproj` (between Cs3 and Cs6).
- [x] Add version-table entry `new(4, "Cs4.grammar", "Cs4.grammar")` to `EmbeddedGrammar.cs` (ascending).
- [x] Add `Tests/CSharpGrammarTests/Cs4DynamicTests.cs` (CRLF + UTF-8 BOM).
- [x] Update `CSharpVersionInfrastructureTests.cs` count/order tests for the new Cs4 (necessary
  consequence of the version-table entry — see Boundary decisions).
- [x] Remove the pre-staged duplicate `<Compile Include="Cs4DynamicTests.cs" />` from
  `CSharpGrammarTests.csproj` (NETSDK1022 — the SDK auto-includes all `.cs`; see Boundary decisions).

## Version-purity results
- `class C { dynamic x = 5; }` → **parses at v3** (type-NAME field, `dynamic` is a plain identifier /
  valid TypeName) and **parses at v4** (predefined-type field). Both parse; the interpretation differs
  (the documented non-strict version-purity, exactly analogous to `var x = 5;` at v1 vs v2 in T3.1.2).
- `class C { int dynamic; }` (a field NAMED `dynamic`) → **parses at v3** (dynamic is a plain
  identifier) / **rejects at v4** (dynamic is reserved → not a valid name). This is the clean v3-vs-v4
  discriminator (the `dynamic` keyword is NOT reserved at v3, IS reserved at v4).
- `class C { void M() { dynamic y = x; } }` (local) → parses at both v3 (type-name local) and v4
  (predefined-type local).
- The `dynamic` TYPE (predefined type) is available at v4+; at v3 `dynamic` is a plain identifier
  (type name). Confirmed: `dynamic` as a PredefinedType is v4-only.

## Tests
`Tests/CSharpGrammarTests/Cs4DynamicTests.cs` (CRLF + UTF-8 BOM) — **13 tests, all green**.
- POSITIVE (v4, 5): `Dynamic_Field_Succeeds` (`class C { dynamic x = 5; }`),
  `Dynamic_MethodParam_Succeeds` (`class C { void M(dynamic x) { } }`),
  `Dynamic_ReturnType_Succeeds` (`class C { dynamic M() { return 5; } }`),
  `Dynamic_Local_Succeeds` (`class C { void M() { dynamic y = x; } }`),
  `Dynamic_AutoProperty_Succeeds` (`class C { dynamic P { get; set; } }`).
- POSITIVE (v4, Roslyn-derived, 3): `Dynamic_Local_NoInitializer_Roslyn_Succeeds`
  (`class C { void M() { dynamic a; } }`, StatementParsingTests.cs:313),
  `Dynamic_ParamsArray_Roslyn_Succeeds` (`class C { void M(params dynamic[] args) { } }`,
  RoundTrippingTests.cs:1529), `Dynamic_DelegateParam_Roslyn_Succeeds`
  (`class C { delegate void D(dynamic d); }`, RoundTrippingTests.cs:1528).
- VERSION-PURITY (3): `Dynamic_AsTypeName_Field_ParsesAtV3` (`class C { dynamic x = 5; }` parses at v3
  as a type-name field), `Dynamic_AsFieldName_ParsesAtV3` (`class C { int dynamic; }` parses at v3 —
  dynamic is a plain identifier), `Dynamic_AsFieldName_RejectedAtV4` (same input rejects at v4 — dynamic
  is reserved, not a valid name).
- NEGATIVE (v4, malformed, 2): `Dynamic_MissingIdentifier_Rejected` (`class C { void M() { dynamic = 5; } }`),
  `Dynamic_WithoutName_Rejected` (`class C { dynamic; }`).

## Verification
- [x] `dotnet build Nitra.sln --no-incremental` → **success, 0 errors** (one transient MSB3061 file-lock
  warning in `RegexTests`, unrelated to this change).
- [x] `dotnet test Tests/CSharpGrammarTests` (fresh build) → **982 total / 979 passed / 0 failed /
  3 skipped**. Baseline 966 passed / 3 skipped (969 total). Delta = **+13** (all new
  `Cs4DynamicTests`); the 3 `CSharpVersionInfrastructureTests` count tests were updated for Cs4 and pass.
  All pre-existing Cs1 / Cs2 / Cs3 / Cs6 / Cs11 tests remain green.
- [x] `dotnet test Tests/ParserTests` (regression; engine + CsNitra meta-grammar unchanged) →
  **327 total / 325 passed / 0 failed / 2 skipped** — matches baseline.

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs4.grammar` (new — `dynamic` reservation + predefined type).
- `Parsers/CSharp/CSharpGrammar/CSharpGrammar.csproj` (added `<EmbeddedResource Include="Cs4.grammar" />`).
- `Tests/CSharpGrammarTests/EmbeddedGrammar.cs` (version-table entry for version 4).
- `Tests/CSharpGrammarTests/Cs4DynamicTests.cs` (new — 13 tests).
- `Tests/CSharpGrammarTests/CSharpVersionInfrastructureTests.cs` (3 count/order tests updated for Cs4).
- `Tests/CSharpGrammarTests/CSharpGrammarTests.csproj` (removed pre-staged duplicate `<Compile>` entry).
- `docs/CSharpParserPlan-checklist.md` (T3.3.1 `[~]` → `[✅]`).
- `docs/CSharpParserPlan-progressT3.3.1.md` (this file).

## Boundary decisions / deviations
- **`dynamic` version-purity via `ReservedKeyword` + `PredefinedType` re-declaration** (Option A, not a
  Cs1 change): `dynamic` is a plain identifier in C# 1.0–3.0 but a keyword in C# 4.0+, so it is APPENDED
  to the merged `ReservedKeyword` (reserves it at v4+) and to the merged `PredefinedType` (makes it a
  valid `Type` at v4+). Consequence: at v4 `dynamic` matches ONLY the `PredefinedType` route of `Type`
  (the `QualifiedName`/`TypeName` route fails because `dynamic` is reserved) → NO equal-length tie; at v3
  `dynamic` is a valid `TypeName` (the `PredefinedType` route fails) → `dynamic x = 5;` parses as a
  type-name field. The clean v3-vs-v4 discriminator is a field NAMED `dynamic` (parses at v3, rejects at
  v4). No Cs1 modification.
- **`dynamic` needs no dedicated `LocalVariableDeclaration` alternative** (unlike `var` in T3.1.2):
  `dynamic` IS a full `Type` at v4, so the existing Cs1 `Type`-based `LocalVariableDeclaration` (and every
  other type position: field/property/parameter/return type) handles it directly. `var` is NOT a type, so
  it needed a dedicated `"var"` alternative.
- **Roslyn models `dynamic` as an `IdentifierName` (type name) + a binder `DynamicTypeSymbol`, NOT a
  syntax-level `PredefinedType`** (`IsPredefinedType`, SyntaxKindFacts.cs:316-340, excludes dynamic). Our
  grammar deliberately models the version-purity at the PARSE level (a `PredefinedType` at v4, a
  `TypeName` at v3) for consistency with the established `var` (T3.1.2) and `partial` (T3.1.3) precedent.
- **Expression-position `dynamic` (member access / invocation / indexer on a dynamic VALUE) is a binder
  concern**: its syntax is a plain `IdentifierName` + postfix operators, already parsed by Cs1 — no grammar
  change needed (out of scope here).
- **Updated `CSharpVersionInfrastructureTests.cs`** (3 count/order tests): adding a version to the table
  necessarily changes the `LoadGrammarUpTo(n)` file count/order; these tests hardcode the counts, so they
  were updated (v4: 3→4, v6: 4→5, v11: 5→6, with `Cs4.grammar` inserted at index 3). In-scope: a direct,
  necessary consequence of the required version-table entry (the same update a Cs6/Cs11 task would have
  needed). Not "unrelated".
- **Removed a pre-staged duplicate `<Compile Include="Cs4DynamicTests.cs" />`** from
  `CSharpGrammarTests.csproj`: the SDK auto-includes all `.cs` files (`EnableDefaultCompileItems` is not
  disabled), so the explicit entry caused `NETSDK1022` (duplicate `Compile` item). The test file is
  auto-included; no explicit entry is needed (consistent with every other test file in the project).
