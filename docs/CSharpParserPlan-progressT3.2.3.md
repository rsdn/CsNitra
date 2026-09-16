# T3.2.3 — C# 3.0 auto-properties (class/struct)

Status: done.

## Goal
Add C# 3.0 auto-properties to the EXISTING `Parsers/CSharp/CSharpGrammar/Cs3.grammar` (class/struct
only; the interface path is C# 1.0 and must NOT change):
- `public int X { get; set; }` (full auto-property, both accessors no body)
- `public int X { get; }` (read-only auto-property)
- `public int X { get; set; } = 5;` (auto-property with initializer)
- `public int X { get { return _x; } set; }` (block getter + auto setter — "auto-setter")
- `public int X { get; set { _x = value; } }` (auto getter + block setter — "auto-getter")

CS3 only. Excluded: `init` (CS9), `required` (CS11), expression-bodied accessors (`get =>`, CS7),
backing-field access (`field`, CS8).

## Baseline (before changes)
- `dotnet build Nitra.sln --no-incremental` → success, 0 errors / 0 warnings.
- `dotnet test Tests/CSharpGrammarTests` → **906 total / 903 passed / 0 failed / 3 skipped**.
- `dotnet test Tests/ParserTests` (regression) → **327 total / 325 passed / 0 failed / 2 skipped**.

## The Cs1 property/accessor structure (exact, verified against Cs1.grammar)
- `Property` (concrete class/struct, Cs1.grammar:188):
  `Attributes? PropertyModifier* Type TypeName "{" AccessorDeclaration+ "}" ";"?`
- `AccessorDeclaration` (Cs1.grammar:204): `AccessorName Block` — EVERY accessor REQUIRES a block body.
- `AccessorName` (Cs1.grammar:199): named union `| "get" | "set"`.
- `InterfaceProperty` (Cs1.grammar:209): `Attributes? PropertyModifier* Type TypeName "{" InterfaceAccessor+ "}" ";"?`
  (a SEPARATE rule; `InterfaceAccessor` = `AccessorName ";"`).
- `AbstractProperty` (Cs1.grammar:194): `... "abstract" ... "{" InterfaceAccessor+ "}" ";"?` (a SEPARATE rule).
- `PropertyModifier` (Cs1.grammar:220): access + static/virtual/override/new/extern/sealed (10 keywords;
  `abstract` is NOT in it — handled by `AbstractProperty`).

Consequences:
- `Property` is referenced ONLY by `ClassMember` (Cs1:110) and `StructMember` (Cs1:123). It is NOT
  referenced by `InterfaceMember` (which uses `InterfaceProperty`). So re-declaring `Property` affects
  ONLY the class/struct concrete-property path — the interface and abstract paths are untouched.
- C# 1.0 has no auto-properties: `class C { int P { get; set; } }` REJECTS at Cs1 (T2.1.2
  `Property_AutoPropertyInClass_Fails`, a Cs1-only test via `LoadCs1Grammar()` — unaffected by Cs3).

## Approach chosen
Re-declare `Property` in Cs3 (append, T0.3 merge) with an alternative that REQUIRES at least one AUTO
(semicolon) accessor (the REQUIRED-new-construct) and allows an optional `= Expression` initializer.
The interface/abstract paths are separate rules and are NOT re-declared (unchanged).

Why re-declare `Property` (and not `AccessorDeclaration`): re-declaring `AccessorDeclaration` to add a
`;` alternative would make it `AccessorName (Block | ";")`, which the Cs1 `Property` (`AccessorDeclaration+`)
would then also match for the ALL-BLOCK case → an equal-length tie between the Cs1 and Cs3 alternatives.
Re-declaring `Property` with a dedicated auto-accessor list (that REQUIRES a `;` accessor) keeps the two
alternatives mutually exclusive (see hand-traces). `InterfaceProperty`/`AbstractProperty` reuse
`InterfaceAccessor` (already `AccessorName ";"`), so they are not affected either way.

### Cs3.grammar rules (exact, appended after the T3.2.2 rules)
```
Property = Attributes? PropertyModifier* Type TypeName "{" AutoPropertyAccessorList "}" ("=" Expression)? ";"?;

AutoPropertyAccessorList =
    | AutoGetterOnly        = "get" ";"
    | AutoGetSet            = "get" ";" "set" ";"
    | BlockGetterAutoSetter = "get" Block "set" ";"
    | AutoGetterBlockSetter = "get" ";" "set" Block;
```
- `Property` is RE-DECLARED (append). The Cs3 alternative REQUIRES an auto (semicolon) accessor via
  `AutoPropertyAccessorList` (every shape contains at least one `;`), so it is mutually exclusive with the
  Cs1 all-block alternative (never an equal-length tie).
- `AutoPropertyAccessorList` is a named union (the meta-grammar forbids `|` inside a group). The four
  shapes cover every get-first auto-property (C# properties require a `get` — binder CS0858). They are
  disambiguated by longest-match (a longer shape wins when it matches); no equal-length tie.
- The initializer `("= Expression)?` is part of the Cs3 alternative (Roslyn `ParsePropertyDeclaration`
  4289-4295). NOTE the CS6 deviation (see below).

## Mutual-exclusivity hand-traces (no equal-length tie)

### Cs1 `Property` (all-block) vs Cs3 `Property` (>= one auto)
- `int P { get { } set { } }` (all block): Cs1 `AccessorDeclaration+` = `get { }` + `set { }` → matches.
  Cs3 `AutoPropertyAccessorList` fails (every shape needs a `;` after get/set; here both are followed by
  `{`). → **Cs1 only**. No tie.
- `int P { get; set; }` (all auto): Cs1 fails (`get;` has no block). Cs3 `AutoGetSet` → matches. →
  **Cs3 only**. No tie.
- `int P { get; }` (read-only auto): Cs1 fails. Cs3 `AutoGetterOnly` → matches. → **Cs3 only**.
- `int P { get { return _x; } set; }` (auto-setter): Cs1 fails (`set;` has no block). Cs3
  `BlockGetterAutoSetter` → matches. → **Cs3 only**.
- `int P { get; set { _x = value; } }` (auto-getter): Cs1 fails (`get;` has no block). Cs3
  `AutoGetterBlockSetter` → matches. → **Cs3 only**.
The two alternatives are mutually exclusive: a property is either ALL-block (Cs1) or has >= one auto
(Cs3). They can never both match the same input → no tie.

### Inside `AutoPropertyAccessorList` (longest-match, no tie)
- `get;` → only `AutoGetterOnly` (the others need a 2nd accessor or a `{` after `get`). No tie.
- `get; set;` → `AutoGetterOnly` (len 6) vs `AutoGetSet` (len 11): longest-match → `AutoGetSet`. No tie.
- `get; set { }` → `AutoGetterOnly` (len 6) vs `AutoGetterBlockSetter` (longer): longest-match →
  `AutoGetterBlockSetter`. No tie.
- `get { } set;` → only `BlockGetterAutoSetter` (`{` after `get` kills the `;`-first shapes). No tie.
- `get { } set { }` (all block) → NO shape matches (no `;`) → the whole Cs3 alternative fails → Cs1 wins.

### Property initializer `= Expression`
- `int P { get; set; } = 5;` → Cs3: `AutoGetSet` + `("= Expression)?` = `= 5` + `";"?` = `;` → matches.
  Cs1 fails (no block). → **Cs3 only**. No tie.
- `int P { get; set; }` (no initializer, next token is `}` or another member, not `=`) → the
  `("= Expression)?` is empty → matches `int P { get; set; }`. No over-consumption.
- `int P { get; set; } = }` (missing initializer value) → `"="` matches, `Expression` fails (`}` is not an
  expression start) → the Optional backtracks (the `=` is NOT consumed) → the Cs3 Property matches
  `int P { get; set; }` and stops; the leftover `= }` is not a valid member → the class body fails → REJECT.

## Roslyn references (C:\RSDN\roslyn, main, src/Compilers/CSharp/Portable/Parser/LanguageParser.cs)
- `ParsePropertyDeclaration` (4252-4317): accessor list (4276-4278) + optional expression-body (`=>`,
  4284-4288, CS7) + optional initializer `= ParseVariableInitializer()` (4289-4295) + semicolon (4297-4305).
- `ParseAccessorList` (4375-4406): a loop over `ParseAccessorDeclaration` until `}` — a MIX of block and
  semicolon accessors is parsed naturally (the auto-getter/auto-setter forms).
- `ParseAccessorDeclaration` (4632-4729): three accessor forms — block (`{`, 4683-4687), semicolon/auto
  (`;`, 4688-4690), arrow (`=>`, 4683, CS7 expression-bodied — excluded). The parser accepts the semicolon
  form unconditionally; the BINDER enforces the version (auto-property = C# 3.0).
- Version gates (src/Compilers/CSharp/Portable/Errors/MessageID.cs): `IDS_FeatureInitOnlySetters` →
  CSharp9 (line 601, `init` — excluded); `IDS_FeatureRequiredMembers` → CSharp11 (line 549, `required` —
  excluded); `IDS_FeatureExpressionBodiedAccessor` → CSharp7 (line 679, `get =>` — excluded);
  `IDS_FeatureAutoPropertyInitializer` → **CSharp6** (line 686) — see the deviation below.
- Syntax tests (src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs): `TestClassProperty`
  (4158) = `class a { b c { get; set; } }`; `TestClassAutoPropertyWithInitializer` (4448) =
  `class a { b c { get; set; } = d; }`; `InitializerOnNonAutoProp` (4504) = `class C { int P { set {} } = 0; }`.

## Version-purity results
- `class C { int P { get; set; } }` → **rejects at v2** (no auto-accessor alternative) / **parses at v3**.
- `class C { int P { get; } }` → **rejects at v2** / **parses at v3**.
- `class C { int P { get; set; } = 5; }` → **rejects at v2** / **parses at v3**.
- `interface I { int P { get; set; } }` → **still parses at v1 and v2** (C# 1.0, `InterfaceProperty`
  unchanged).

## Tests
`Tests/CSharpGrammarTests/Cs3AutoPropertyTests.cs` (CRLF + UTF-8 BOM) — **21 tests, all green**.
- POSITIVE (v3, 9): `AutoProperty_GetSet_Succeeds` (`public int X { get; set; }`),
  `AutoProperty_GetOnly_Succeeds` (`public int X { get; }`), `AutoProperty_WithInitializer_Succeeds`
  (`public int X { get; set; } = 5;`), `AutoSetter_BlockGetter_Succeeds`
  (`private int _x; public int X { get { return _x; } set; }`), `AutoGetter_BlockSetter_Succeeds`
  (`private int _x; public int X { get; set { _x = value; } }`), `AutoProperty_Multiple_Succeeds`
  (`public int X { get; set; } public int Y { get; } = 10;`), `AutoProperty_Struct_Succeeds`
  (`struct S { public int X { get; set; } }`), `AutoProperty_ReadOnlyWithInitializer_Succeeds`
  (`public int X { get; } = 10;`), `AutoProperty_ExpressionInitializer_Succeeds`
  (`public int X { get; set; } = 5 + 3;`).
- ROBUSTNESS (v3, 2, the core "no tie" check): `BlockBodyProperty_StillParsesAtV3_Succeeds` (a Cs1
  all-block property still matches at v3 via the Cs1 alternative), `MixedBlockAndAutoProperty_Succeeds`
  (a class holding both a block-body property and an auto-property).
- POSITIVE (v3, Roslyn-derived, 2): `Roslyn_TestClassProperty_Succeeds` (`int X { get; set; }`,
  DeclarationParsingTests.cs:4158), `Roslyn_TestClassAutoPropertyWithInitializer_Succeeds`
  (`int X { get; set; } = 5;`, :4448).
- POSITIVE (v1 + v2, interface — C# 1.0, must stay green, 2): `InterfaceProperty_AutoAccessors_ParseAtV1_Succeeds`
  and `InterfaceProperty_AutoAccessors_ParseAtV2_Succeeds` (`interface I { int P { get; set; } }` — the
  separate `InterfaceProperty` rule is unchanged).
- VERSION-PURITY (v2, 3): `AutoProperty_GetSet_RejectedAtV2` (`class C { int P { get; set; } }`),
  `AutoProperty_GetOnly_RejectedAtV2` (`class C { int P { get; } }`), `AutoProperty_WithInitializer_RejectedAtV2`
  (`class C { int P { get; set; } = 5; }`).
- NEGATIVE (v3, malformed, 3): `AutoProperty_MissingAccessorBody_Rejected` (`int P { get set; }` — missing
  `;`/`{` after the first accessor), `AutoProperty_MissingInitializerValue_Rejected` (`int P { get; set; } = }`
  — the `("= Expression)?` backtracks and the leftover `= }` is not a member), `AutoProperty_SetOutsideAccessorList_Rejected`
  (`int P { get; } set;` — the `set;` sits after the property's closing `}`, not a member).

## Verification
- [x] `dotnet build Nitra.sln --no-incremental` → **success, 0 errors / 0 warnings**.
- [x] `dotnet test Tests/CSharpGrammarTests` (fresh build) → **927 total / 924 passed / 0 failed /
  3 skipped**. Baseline before T3.2.3: 906 total / 903 passed / 3 skipped. Delta = **+21** (all new
  `Cs3AutoPropertyTests`). Filtered run of `Cs3AutoPropertyTests` → **21 passed / 0 failed**. All
  pre-existing Cs1 / Cs2 / Cs6 / Cs11 tests remain green, including the Cs1 `Property_AutoPropertyInClass_Fails`
  / `Property_AutoGetterInClass_Fails` / `Property_AutoSetterInClass_Fails` (v1) and the
  `InterfaceProperty_*` (v1) tests.
- [x] `dotnet test Tests/ParserTests` (regression; engine + CsNitra meta-grammar unchanged) →
  **327 total / 325 passed / 0 failed / 2 skipped** — matches baseline.

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs3.grammar` (T3.2.3 rules: re-declared `Property`, `AutoPropertyAccessorList`).
- `Tests/CSharpGrammarTests/Cs3AutoPropertyTests.cs` (new).
- `docs/CSharpParserPlan-checklist.md` (T3.2.3 `[~]` → `[✅]`).
- `docs/CSharpParserPlan-progressT3.2.3.md` (this file).

## Boundary decisions / deviations
- **Auto-property initializer is CS6 in Roslyn, attached to CS3 here (task deviation).** Roslyn gates
  `IDS_FeatureAutoPropertyInitializer` at C# 6 (MessageID.cs:686; ParserErrorMessageTests.cs:6472
  "Feature 'auto property initializer' is not available in C# 5. Please use language version 6"). The task
  requires the initializer as part of the CS3 auto-property feature (v3 accept / v2 reject), and our
  grammar has no CS6 property rule, so it is attached to the Cs3 `Property` alternative. The version-purity
  tests (v2 reject / v3 accept) pass; the CS5/CS6 split is not modeled (no v5 in this grammar).
- **`get`-first accessor order (C# properties require a getter).** `AutoPropertyAccessorList` requires a
  `get` (all four shapes start with `get`). Set-first (`set { } get;`) and setter-only (`set;`) auto-properties
  are NOT modeled — aligned with the binder rule CS0858 "Property must contain a getter" (Roslyn's PARSER
  accepts setter-only, e.g. `InitializerOnNonAutoProp`, but the binder rejects it; parser-vs-binder split).
  Out of the task's required form set.
- **Trailing `";"?` is optional (consistent with Cs1 `Property`, T2.1.2 D5).** Roslyn REQUIRES the `;`
  when an initializer is present (ParsePropertyDeclaration 4298-4301); here it is optional (loose), matching
  the existing Cs1 `Property` rule. Not in the required negative set.
- **CS3 only**: no `init` (CS9), no `required` (CS11), no expression-bodied accessors `get =>` (CS7), no
  backing-field `field` access (CS8), no accessor modifiers (`private set`, CS3-but-binder-level and not in
  the required set — the Cs1 `PropertyModifier`/`AccessorName` are reused as-is).
