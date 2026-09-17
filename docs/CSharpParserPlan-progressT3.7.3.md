# T3.7.3 — C# 7.2 `ref struct` + `readonly struct`

Status: done (see the Deviations section).

## Goal
Add the C# 7.2 **`ref struct`** and **`readonly struct`** to the EXISTING `Parsers/CSharp/CSharpGrammar/Cs7.grammar`
(which already has the CS7.0 rules from T3.6.x, the CS7.1 `default` literal from T3.7.1, and the CS7.2 `in`
parameter from T3.7.2; CS7.1/7.2 features go into Cs7.grammar, version 7):
- **`ref struct`**: `ref struct S { }` (a ref struct — can only contain ref fields, must be allocated on the
  stack). The `ref` is a struct modifier.
- **`readonly struct`**: `readonly struct S { }` (a readonly struct). The `readonly` is a struct modifier.

CS7.2 (per the plan's decision, in Cs7.grammar = version 7; the version-purity boundary is v6 rejects / v7
accepts). `CreateParser(6)` must REJECT `ref struct S { }` and `readonly struct S { }`; `CreateParser(7)` must
accept them. The `readonly int x;` field must still parse at v1–v6 (no regression).

## Baseline (before changes)
- `dotnet build Nitra.sln --no-incremental` → success, 0 errors / 0 warnings.
- `dotnet test Tests/CSharpGrammarTests` → **1181 total / 1178 passed / 0 failed / 3 skipped** (matches T3.7.2).
- `dotnet test Tests/ParserTests` → **327 total / 325 passed / 0 failed / 2 skipped** (matches T3.7.2).

## Cs1 structure found
- **`StructDeclaration`** (Cs1.grammar:36): `Attributes? StructModifier* "struct" TypeName BaseList? StructBody
  ";"?`. The struct modifiers are a greedy `StructModifier*` loop BEFORE the `"struct"` keyword. Re-declared in
  Cs2 (Cs2.grammar:69 `Attributes? StructModifier* "struct" TypeName TypeParameterList BaseList? StructBody ";"?`
  and Cs2.grammar:145 `Attributes? StructModifier* "struct" TypeName BaseList? ConstraintClause+ StructBody ";"?`)
  — both ALSO use `StructModifier*`, so they automatically pick up any new modifier.
- **`StructModifier`** (Cs1.grammar:53-59): the union `| "public" | "private" | "protected" | "internal" |
  "abstract" | "unsafe"`. (Note: NO `sealed` — a struct is implicitly sealed, `sealed struct` is a binder error
  CS0261; and NO `readonly`/`ref` — those are the C# 7.2 additions here.) Cs2 APPENDS `"partial"`
  (Cs2.grammar:264-265). Re-declare in Cs7 to add `"ref"` and `"readonly"`.
- **`FieldModifier`** (Cs1.grammar:155-165): includes `"readonly"` (Cs1.grammar:162) — a DIFFERENT rule from
  `StructModifier`, so adding `readonly` to `StructModifier` does NOT conflict with `readonly` fields.
- **`ReservedKeyword`** (Cs1.grammar:537-618): `ref` (Cs1.grammar:591) and `readonly` (Cs1.grammar:590) ARE
  reserved; `struct` (Cs1.grammar:602) is reserved. So a bare `ref`/`readonly` is NOT an `IdentifierName`/`Type`/
  `TypeName`.
- **`TypeName`** (Cs1.grammar:23): `!ReservedKeyword Identifier`. **`QualifiedName`** (Cs1.grammar:21): `TypeName
  NamespaceSegment*`. **`Type`** (Cs1.grammar:508-512): `| PredefinedType | QualifiedName | PointerType |
  ArrayType`; `PredefinedType` (Cs1.grammar:515-531) is the 16 predefined types (`bool`…`void`). `struct` is a
  reserved keyword → NOT a `PredefinedType`, NOT a `TypeName`/`QualifiedName` → **`Type = struct` FAILS**. This is
  the crux of the mutual exclusivity (see below).
- **`MethodModifier`** (Cs1.grammar:288-300): access + static/virtual/override/abstract/new/extern/sealed/unsafe.
  T3.6.4 APPENDS `"ref"`/`"readonly"` in Cs7. So at v7 `Method` (`MethodModifier* Type ...`) can consume a leading
  `ref`/`readonly`, but then `Type = struct` FAILS (reserved) → `Method` rejects `ref struct`/`readonly struct`.
- **`TypeDeclaration`** (Cs1.grammar:27-32): `| ClassDeclaration | StructDeclaration | InterfaceDeclaration |
  EnumDeclaration | DelegateDeclaration`. **`NamespaceMember`** (Cs1.grammar:5-9): `| UsingDirective |
  ExternAliasDirective | NamespaceDeclaration | TypeDeclaration`; **`CompilationUnit = NamespaceMember*`**
  (Cs1.grammar:3); **`Grammar = CompilationUnit`** (Cs1.grammar:1). So a TOP-LEVEL `ref struct S { }` is a
  `NamespaceMember` → `TypeDeclaration` → `StructDeclaration` (top-level type declarations parse — confirmed by the
  existing `Cs2PartialSealedTests.cs`, which uses top-level `partial class C { }` / `partial struct S { }`).

## Roslyn references (C:\RSDN\roslyn, main, src/Compilers/CSharp)
- **The form** — `ref`/`readonly` is a MODIFIER on the struct declaration (BEFORE the `struct` keyword).
  `DeclarationTreeBuilder.cs:798-805`: `ReadOnlyKeyword` + `DeclarationKind.Struct` → `IDS_FeatureReadOnlyStructs`;
  `RefKeyword` + `DeclarationKind.Struct` → `IDS_FeatureRefStructs`. Confirmed by the syntax parsing tests:
  - `RefStructs.cs:28` (`RefStructSimple`): `ref struct S1{}` / `public ref struct S2{}` (parses cleanly at Latest).
  - `ReadOnlyStructs.cs:27` (`ReadOnlyStructSimple`): `readonly struct S1{}` / `public readonly struct S2{}` /
    `readonly public struct S3{}` (parses cleanly at Latest; `readonly public` = flexible modifier ORDER).
  - `ReadOnlyStructs.cs:138` (`ReadOnlyRefStruct`): `readonly ref struct S1{}` / `unsafe readonly public ref struct
    S2{}` (BOTH modifiers combined, any order).
- **Version gates (both C# 7.2, both `// semantic check`)** — `MessageID.cs:152` (`IDS_FeatureRefStructs`),
  `MessageID.cs:153` (`IDS_FeatureReadOnlyStructs`); both map to `LanguageVersion.CSharp7_2`
  (`MessageID.cs:657-658`, in the "C# 7.2 features" block `MessageID.cs:652-661`). Both are marked `// semantic
  check` → BINDER checks, not parse checks → the PARSER accepts the form wherever the rule exists (CS7 only, per
  the plan's decision). Confirmed by the version-gate tests:
  - `RefStructs.cs:44` (`RefStructSimpleLangVer`): at `LanguageVersion.CSharp7`, `ref struct S1{}` →
    `ERR_FeatureNotAvailableInVersion7` "ref structs" "7.2".
  - `ReadOnlyStructs.cs:64` (`ReadOnlyStructSimpleLangVer`): at `LanguageVersion.CSharp7`, `readonly struct S1{}` →
    `ERR_FeatureNotAvailableInVersion7` "readonly structs" "7.2".
- **`ref`/`readonly` are ONLY struct modifiers** (NOT class/interface/enum/delegate) — `RefStructs.cs:67`
  (`RefStructErr`): `ref class` / `ref interface` / `ref delegate` → `ERR_BadMemberFlag` "The modifier 'ref' is not
  valid for this item". `ReadOnlyStructs.cs:92` (`ReadOnlyClassErr`): `readonly class` / `readonly delegate` /
  `readonly interface` → `ERR_BadMemberFlag` "The modifier 'readonly' is not valid for this item". So they are added
  to `StructModifier` ONLY (NOT to `ClassModifier`/`InterfaceModifier`/`EnumModifier`/`DelegateModifier`).

## Approach
Re-declare Cs1 `StructModifier` in Cs7 (append, T0.3 merge) to add `"ref"` and `"readonly"`. The merged v7 union is
`public | private | protected | internal | abstract | unsafe | partial | ref | readonly`. This is the EXACT pattern
of the established modifier-union additions:
- T3.6.4 added `"ref"`/`"readonly"` to `MethodModifier` (Cs7.grammar:222-224).
- T3.7.2 added `"in"` to `ParameterModifier` (Cs7.grammar:430-431).
- Cs2 added `"partial"` to `StructModifier` (Cs2.grammar:264-265).
- Cs3 added `"this"` to `ParameterModifier` (Cs3.grammar:250).

No `StructDeclaration` re-declaration is needed: the `StructDeclaration` rule (Cs1:36 + the Cs2 re-declarations)
already uses `StructModifier*`, so the merged `StructModifier*` loop consumes a leading `ref`/`readonly` before the
`"struct"` keyword. The REQUIRED-new-construct is the `ref`/`readonly` keyword (reserved → mutually exclusive with
the `Type` position, see the hand-traces).

## Mutual-exclusivity hand-traces
The `StructModifier` union is referenced ONLY by `StructDeclaration` (Cs1:36, Cs2:69, Cs2:145) — no other rule uses
it (verified by grep across all `.grammar` files). So adding `ref`/`readonly` to it enables ONLY `ref struct` /
`readonly struct` (and combinations with the existing modifiers). `ref` (Cs1:591) and `readonly` (Cs1:590) are
RESERVED keywords, so neither is a `Type`/`TypeName` (Cs1:23), and `struct` (Cs1:602) is reserved so `Type = struct`
FAILS. Summary (all verified empirically — the new tests are green):
- **`ref struct S { }` (a ref struct, v7)**: `StructDeclaration` → `StructModifier*` = `ref`, `"struct"`,
  `TypeName` = `S`, `StructBody` = `{ }`. `Field` FAILS (`ref` is not a `FieldModifier`; `Type = ref` fails — `ref`
  is reserved). `Method` FAILS (`MethodModifier*` = `ref`, then `Type = struct` fails — `struct` is reserved). Sole
  match.
- **`readonly struct S { }` (a readonly struct, v7)**: `StructDeclaration` → `StructModifier*` = `readonly`,
  `"struct"`, `TypeName` = `S`, `StructBody` = `{ }`. `Field` FAILS (`FieldModifier*` = `readonly`, then `Type =
  struct` fails — `struct` is reserved). `Method` FAILS (`MethodModifier*` = `readonly`, then `Type = struct` fails).
  Sole match.
- **`ref struct S { }` (v6)**: `StructModifier*` matches ZERO (`ref` is not a `StructModifier` at v6); `"struct"`
  expected but the reserved `ref` found → FAILS. No other `TypeDeclaration` alternative matches. REJECT.
- **`readonly struct S { }` (v6)**: `StructModifier*` matches ZERO (`readonly` is not a `StructModifier` at v6);
  `"struct"` expected but the reserved `readonly` found → FAILS. No other `TypeDeclaration` alternative matches.
  REJECT.
- **`readonly int x;` (a readonly FIELD, every version)**: `Field` → `FieldModifier*` = `readonly`, `Type` = `int`,
  `VariableDeclarator` = `x`, `;` (matches). `StructDeclaration` → `StructModifier*` = `readonly`, then `"struct"`
  expected but `int` found → FAILS. So `Field` is the sole match (NO regression at any version). The `readonly`
  STRUCT modifier and the `readonly` FIELD modifier are DIFFERENT rules (`StructModifier` vs `FieldModifier`) — no
  conflict.
- **`readonly ref struct S { }` (both modifiers, v7)**: `StructModifier*` = `readonly`, `ref`, `"struct"`, `S`,
  `{ }`. Parses (Roslyn `ReadOnlyRefStruct`, ReadOnlyStructs.cs:138 — both modifiers, any order). A correct
  superset.
- **`ref struct { }` (missing name, v7)**: `StructModifier*` = `ref`, `"struct"` matches, `TypeName` =
  `!ReservedKeyword Identifier` → the next token is `{` (not an identifier) → FAILS. No other `TypeDeclaration`
  matches. REJECT.

## Version-purity results
Verified empirically (all green tests):
- **v7 ACCEPTS**: `ref struct S { }`, `ref struct S { int M() { return 5; } }`, `readonly struct S { }`, `readonly
  struct S { int M() { return 5; } }`, `public ref struct S { }`, `public readonly struct S { }`, `readonly ref
  struct S { }`, `readonly public struct S { }`.
- **v6 REJECTS**: `ref struct S { }` (the `ref` `StructModifier` is absent at v6 → `StructModifier*` matches zero →
  `"struct"` expected but reserved `ref` found), `readonly struct S { }` (same, with `readonly`).
- **v1–v6 no-regression (still parse)**: `class C { readonly int x; }` (the `readonly` FIELD — a `FieldModifier`,
  Cs1:162, a DIFFERENT rule from `StructModifier`, untouched).
- **v7 malformed (reject)**: `ref struct { }` (missing name — `TypeName` fails on `{`).

## Tests
`Tests/CSharpGrammarTests/Cs7RefStructTests.cs` (CRLF + UTF-8 BOM) — **12 tests, all green**.
- POSITIVE (v7, core, 4): `RefStruct_Succeeds` (`ref struct S { }`), `RefStruct_WithMember_Succeeds` (`ref struct S
  { int M() { return 5; } }`), `ReadonlyStruct_Succeeds` (`readonly struct S { }`), `ReadonlyStruct_WithMember-
  Succeeds` (`readonly struct S { int M() { return 5; } }`).
- POSITIVE (v1–v6, no-regression, 1): `ReadonlyField_ParsesAtV1ToV6` (`class C { readonly int x; }`, looped over
  v1–v6 — the `readonly` field is a `FieldModifier`, a different rule, untouched).
- POSITIVE (v7, Roslyn-derived, 4): `RefStruct_Public_Roslyn_Succeeds` (`public ref struct S { }`,
  RefStructs.cs:35 `public ref struct S2{}`), `ReadonlyStruct_Public_Roslyn_Succeeds` (`public readonly struct S {
  }`, ReadOnlyStructs.cs:34 `public readonly struct S2{}`), `ReadonlyRefStruct_Roslyn_Succeeds` (`readonly ref
  struct S { }`, ReadOnlyStructs.cs:143 `readonly ref struct S1{}`), `ReadonlyStruct_MemberOrder_Roslyn_Succeeds`
  (`readonly public struct S { }`, ReadOnlyStructs.cs:36 `readonly public struct S3{}` — flexible modifier order).
- NEGATIVE (v6, version-purity, 2): `RefStruct_RejectedAtV6` (`ref struct S { }`), `ReadonlyStruct_RejectedAtV6`
  (`readonly struct S { }`).
- NEGATIVE (v7, malformed, 1): `RefStruct_MissingName_Rejected` (`ref struct { }` — missing name).

## Verification
- [x] `dotnet build Nitra.sln --no-incremental` → **success, 0 errors / 0 warnings**.
- [x] `dotnet test Tests/CSharpGrammarTests` (fresh build) → **1193 total / 1190 passed / 0 failed / 3 skipped**.
  Baseline before T3.7.3 (after T3.7.2): 1181 total / 1178 passed / 3 skipped. Delta = **+12** (all new
  `Cs7RefStructTests`). All pre-existing Cs1/Cs2/Cs3/Cs4/Cs5/Cs6/Cs11 tests remain green (the only change is the new
  `ref`/`readonly` `StructModifier` alternatives in Cs7, which are the sole match for a leading `ref`/`readonly`
  before a `struct` keyword and appear in no pre-existing test; the `readonly` field is a separate `FieldModifier`
  rule, untouched).
- [x] `dotnet test Tests/ParserTests` (regression; engine + CsNitra meta-grammar unchanged) → **327 total / 325
  passed / 0 failed / 2 skipped** — matches baseline.

## Files changed
Staged T3.7.3 files:
- `Parsers/CSharp/CSharpGrammar/Cs7.grammar` (the `ref`/`readonly` `StructModifier`, T3.7.3).
- `Tests/CSharpGrammarTests/Cs7RefStructTests.cs` (new, CRLF + UTF-8 BOM).
- `docs/CSharpParserPlan-progressT3.7.3.md` (this file).
- `docs/CSharpParserPlan-checklist.md` (T3.7.3 marked `[✅]` with the deviations).

Not staged (left as-is):
- `docs/antlr4-analysis.md` — unrelated untracked file, left alone (not staged).

## Deviations / boundary decisions
- **D1 — `readonly struct` is a C# 7.2 feature in Roslyn (not C# 8.0 as sometimes misremembered).** Both `ref
  struct` and `readonly struct` map to `LanguageVersion.CSharp7_2` in Roslyn (`MessageID.cs:657-658`, the "C# 7.2
  features" block), both `// semantic check`. This matches the task's statement that both are C# 7.2. No deviation
  from the task; documented for accuracy (the version gate is a binder concern — the PARSER accepts the form at CS7
  per the plan's decision that CS7.1/7.2 features go into Cs7.grammar, version 7).
- **D2 — `ref`/`readonly` are added to `StructModifier` ONLY, not to the other modifier unions.** Roslyn
  `RefStructErr` (RefStructs.cs:67) and `ReadOnlyClassErr` (ReadOnlyStructs.cs:92) confirm `ref`/`readonly` are NOT
  valid for `class`/`interface`/`delegate` (`ERR_BadMemberFlag`). Adding them to `ClassModifier`/`InterfaceModifier`/
  `EnumModifier`/`DelegateModifier` would wrongly accept `ref class`/`readonly interface`/etc. at v7. So they are
  added to `StructModifier` only (the task's instruction).
- **D3 — the task's "verify `ref`/`readonly` are reserved" is CORRECT (unlike T3.7.2's `in` false-premise).** `ref`
  (Cs1:591) and `readonly` (Cs1:590) ARE in the Cs1 `ReservedKeyword`. This is beneficial: because they are reserved,
  they are not a `Type`/`TypeName`, so the existing `StructDeclaration` cannot match `ref struct ...` / `readonly
  struct ...` without the new modifier (`StructModifier*` matches zero and the leading reserved token is not a
  `Type`) → the new `ref struct`/`readonly struct` forms are the SOLE match (clean mutual exclusivity, no
  equal-length tie).
- **D4 — `readonly ref struct` / `readonly public struct` (combined / reordered modifiers) parse at v7 (a correct
  superset).** The greedy `StructModifier*` loop accepts any order of the merged modifiers, matching Roslyn's generic
  any-order `ParseModifiers` loop (Roslyn `ReadOnlyRefStruct`, ReadOnlyStructs.cs:138; `ReadOnlyStructSimple`
  `readonly public struct S3{}`, ReadOnlyStructs.cs:36). Per-kind legality/ordering is a binder concern (consistent
  with the Cs1 modifier-ordering note, Cs1.grammar:98-101). Not a task-listed test, but covered by the Roslyn-derived
  positives.
