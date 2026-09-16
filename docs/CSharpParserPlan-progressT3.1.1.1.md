# T3.1.1.1 — C# 2.0 generics (type-level)

Status: done.

## Goal
Add C# 2.0 type-level generics to a NEW grammar file `Parsers/CSharp/CSharpGrammar/Cs2.grammar`:
generic type names (`C<T>`, `C<T,U>`, qualified `N.List<T>`), type-parameter lists on type
declarations (class/struct/interface/delegate), and variance (`in`/`out`). First version file of Stage 3.

## Baseline (before changes)
- `dotnet test Tests/CSharpGrammarTests` (fresh build) → **772 passed / 0 failed / 3 skipped** (total 775), per T3.0.

## Cs2.grammar rules

### New rules
- `TypeArgumentList = "<" (Type; ",")+ ">";` — type arguments are full `Type`s, so `C<int[]>`, `C<Foo<T>>`, `goo<bar<zed>>` work. Roslyn `ParseTypeArgumentList` (LanguageParser.cs:6545): `<` first-type `(, type)*` `>`.
- `TypeParameterList = "<" (TypeParameter; ",")+ ">";` — Roslyn `ParseTypeParameterList` (LanguageParser.cs:6104): `<` + comma-separated TypeParameter (`requireOneElement: true`, `allowTrailingSeparator: false`) + `>`.
- `TypeParameter = VarianceModifier? TypeName;` — Roslyn `ParseTypeParameter` (LanguageParser.cs:6155): `[attributes] (in|out)? identifier`; attributes on type parameters are out of scope here, so `[attributes]` is dropped.
- `VarianceModifier = | "in" | "out";` — named union (the meta-grammar forbids `|` inside a group, CsNitraParser.cs:103).

### Re-declared rules (REQUIRED-new-construct principle)
- `TypeName = !ReservedKeyword Identifier TypeArgumentList;` — re-declares Cs1 `TypeName`; the Cs2 alternative REQUIRES `TypeArgumentList`.
- `ClassDeclaration = Attributes? ClassModifier* "class" TypeName TypeParameterList BaseList? ClassBody ";"?;`
- `StructDeclaration = Attributes? StructModifier* "struct" TypeName TypeParameterList BaseList? StructBody ";"?;`
- `InterfaceDeclaration = Attributes? InterfaceModifier* "interface" TypeName TypeParameterList BaseList? InterfaceBody ";"?;`
- `DelegateDeclaration = Attributes? DelegateModifier* "delegate" Type TypeName TypeParameterList "(" ParameterList? ")" ";";`

The `TypeParameterList` is inserted in the Roslyn position (after the name, before the base list for
class/struct/interface and before the parameter list for delegate — confirmed at LanguageParser.cs:1824
and :5844).

## Mutual-exclusivity verification (hand-traces)

The merged `TypeName` is **longest-match**: on `C<T>` the Cs2 alternative (`C<T>`, longer) beats the
Cs1 alternative (`C`). This greedy behavior drives the declaration cases (Boundary decision D1).

### TypeName
- `C` (followed by non-`<`): Cs1 → `C`; Cs2 fails (no `<`). → **Cs1 only**, no tie.
- `C<T>`: Cs1 → `C`; Cs2 → `C<T>` (longer). → **Cs2 only** (longest), no tie.

### ClassDeclaration (StructDeclaration / InterfaceDeclaration / DelegateDeclaration identical pattern)
- `class C { }`: Cs1 → `TypeName=C`, `ClassBody={ }` → full match. Cs2 → `TypeName=C`, `TypeParameterList` at `{` → **fail**. → **Cs1 only**, no tie.
- `class C<T> { }` (non-variance generic): Cs1 → `TypeName=C<T>` (greedy Cs2 alt), `ClassBody={ }` → full match. Cs2 → `TypeName=C<T>` (greedy), `TypeParameterList` at `{` → **fail**. → **Cs1 only**, no tie. (D1: the non-variance generic declaration is matched by the Cs1 alternative via the greedy TypeName, not by the Cs2 TypeParameterList.)
- `class C<in T> { }` (variance; binder-invalid on a class, but parse-reachable): the greedy `TypeName` **cannot** consume `<in T>` (`in` is reserved → not a `Type` → not a `TypeArgumentList`), so `TypeName=C`. Cs1 → `ClassBody` at `<` → **fail**. Cs2 → `TypeName=C`, `TypeParameterList=<in T>` (`TypeParameter = in T`), `ClassBody={ }` → full match. → **Cs2 only**, no tie.

**Invariant (no equal-length tie for any re-declared rule):** the Cs2 alternative's REQUIRED
`TypeParameterList` can only match when the position after the (greedy) `TypeName` begins with a
`<...>` that is **not** a valid `TypeArgumentList` — i.e. it contains a variance modifier (`in`/`out`),
which is not a `Type`. In exactly that case the Cs1 alternative's `BaseList?`/body cannot follow the
`<` (a base list needs `:`, a body needs `{`), so Cs1 fails. Conversely, when the `<...>` **is** a valid
`TypeArgumentList`, the greedy `TypeName` consumes it and the Cs2 `TypeParameterList` sees `{`/`:`/`(`
and fails. The two alternatives therefore never match the same input at the same length.

## `in`/`out` reservation
Neither needed to be added: both are **already** in Cs1 `ReservedKeyword` (Cs1.grammar:571 `in`,
:584 `out`). Because they are reserved:
- they cannot be a `TypeName` (`!ReservedKeyword Identifier`), so in `TypeParameter = VarianceModifier? TypeName` a bare `in`/`out` is unambiguously the `VarianceModifier` (the `?` is unambiguous — `TypeName` cannot consume `in`/`out`);
- `VarianceModifier = | "in" | "out"` matches the reserved-keyword tokens directly.
`out` is also already a Cs1 `ParameterModifier`; the two uses are in disjoint contexts (parameter list vs type-parameter list) and do not conflict.

## Setup steps done
- [x] Created `Parsers/CSharp/CSharpGrammar/Cs2.grammar` (CRLF, no BOM).
- [x] Added `<EmbeddedResource Include="Cs2.grammar" />` to `CSharpGrammar.csproj` (between Cs1 and Cs6, matching the existing pattern).
- [x] Added `new(2, "Cs2.grammar", "Cs2.grammar"),` to the `EmbeddedGrammar` version table (ascending order).
- [x] Tests use `CSharpVersionTestHelper.CreateParser(2)` (merges Cs1+Cs2) for positives and `CreateParser(1)` (Cs1 only) for version-purity negatives.

## Version-purity results
- `class C<T> { }` → **rejects at v1**, **parses at v2**. ✓
- `class D { C<T> x; }` (generic field) → **rejects at v1**, **parses at v2**. ✓
- `interface I<T> { }` → **rejects at v1**, **parses at v2**. ✓
- All pre-existing Cs1 programs still parse at v1 (full Cs1 test suite green); Cs6/Cs11 tests still green (they merge Cs1+Cs6 / Cs1+Cs6+Cs11, and Cs2 is not in their range — `LoadGrammarUpTo(6)`/`(11)` include Cs2, so the merged grammars are a superset; verified green below).

## Tests
`Tests/CSharpGrammarTests/Cs2GenericTests.cs` (CRLF + UTF-8 BOM) — **23 tests, all green**.
- POSITIVE (v2, 12): `class C<T> { }`, `class C<T, U> { }`, `struct S<T> { }`, `interface I<T> { }`, `delegate void D<T>(T x);`, `class C<T> { T x; }`, `class C<T> { C<T> y; }`, `class C<T> { C<T, int> z; }`, `class C { System.Collections.Generic.List<int> x; }` (qualified generic), `interface I<out T> { }`, `interface I<in T> { }`, `class Outer { class Inner<T> { } }` (nested).
- POSITIVE (v2, Roslyn-derived, 4): `interface A<out C> { }` (DeclarationParsingTests.cs:1648, attribute dropped), `class C { goo<bar, zed> x; }` (NameParsingTests.cs:219), `class C { goo<bar<zed>> x; }` (NameParsingTests.cs:235), `class C { void M() { T<a> b; } }` (StatementParsingTests.cs:343).
- NEGATIVE (v1, version-purity, 3): `class C<T> { }`, `class D { C<T> x; }`, `interface I<T> { }` — all reject at v1.
- NEGATIVE (v2, malformed, 4): `class C<> { }` (empty), `class C<T,> { }` (trailing comma — `(TypeParameter; ",")+` is `EndBehavior=Forbidden`), `class C { C< x; }` (unclosed), `interface I<in> { }` (variance without name).

## Verification
- [x] `dotnet build Nitra.sln --no-incremental` → **success, 0 errors / 0 warnings**.
- [x] `dotnet test Tests/CSharpGrammarTests` (fresh build) → **795 passed / 0 failed / 3 skipped** (total 798).
  - Baseline before T3.1.1.1: 772 passed / 3 skipped (total 775). Delta = **+23** (all new `Cs2GenericTests`).
  - Filtered run of `Cs2GenericTests` → **23 passed / 0 failed**.
  - All pre-existing Cs1 / Cs6 / Cs11 tests remain green.
- [x] `dotnet test Tests/ParserTests` (regression; engine + CsNitra meta-grammar unchanged) → **325 passed / 0 failed / 2 skipped** (total 327).

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs2.grammar` (new).
- `Parsers/CSharp/CSharpGrammar/CSharpGrammar.csproj` (added `<EmbeddedResource Include="Cs2.grammar" />`).
- `Tests/CSharpGrammarTests/EmbeddedGrammar.cs` (added version-2 table entry).
- `Tests/CSharpGrammarTests/Cs2GenericTests.cs` (new, 23 tests).
- `Tests/CSharpGrammarTests/CSharpVersionInfrastructureTests.cs` (updated 3 version-table tests to reflect Cs2 now existing: `LoadGrammarUpTo_6` → Cs1/Cs2/Cs6, `LoadGrammarUpTo_11` → Cs1/Cs2/Cs6/Cs11, `LoadGrammarUpTo_3` → renamed `LoadGrammarUpTo_4` testing the Cs2→Cs6 gap). **Required consequence** of the mandated setup step (adding Cs2 to the table); these tests assert the exact table contents.
- `docs/CSharpParserPlan-checklist.md` (T3.1.1.1 `[~]` → `[✅]`).
- `docs/CSharpParserPlan-progressT3.1.1.1.md` (this file).

## Boundary decisions / deviations
- **D1 (declarations):** the merged `TypeName` is longest-match, so in a declaration context it greedily
  consumes `C<T>` as a generic type name. Consequence: a **non-variance** generic declaration
  (`class C<T> { }`) is matched by the **Cs1** `ClassDeclaration` (greedy TypeName + body), while the
  **Cs2** re-declared alternative (TypeName + REQUIRED `TypeParameterList`) is reachable **only** for the
  **variance** case (`interface I<in T>`, `delegate void D<out T>(T x);`), where the greedy TypeName cannot
  consume the `<...>` (variance keywords are not `Type`s). This deviates from the task's literal
  "class C<T> { } → Cs2" but is functionally equivalent (all constructs parse at v2, reject at v1, no
  ties) and is unavoidable without modifying Cs1 (which is forbidden): the declaration's name is the
  shared (now greedy) `TypeName`, so the Cs1 alternative always wins the non-variance case. The
  `TypeParameterList` rule is nonetheless exercised (by variance), and `in`/`out`/`TypeArgumentList` are
  all used. See the invariant above for why no equal-length tie ever arises.
- Variance on a non-interface/delegate (e.g. `class C<in T> { }`) parses at v2 — a **binder** concern, not
  a parse concern (consistent with the task note and the parser-vs-binder split used throughout).
- `TypeParameter` drops Roslyn's optional `[attributes]` prefix (attributes on type parameters are out of
  scope for T3.1.1.1).
