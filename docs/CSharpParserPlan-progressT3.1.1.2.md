# T3.1.1.2 — C# 2.0 method type parameters + constraint clauses

Status: done.

## Goal
Add C# 2.0 method type parameters (`void M<T>(T x) { }`) and constraint clauses
(`where T : struct` / `where T : IBase` / `where T : new()` / `where T : U`, combinable) to the
existing `Parsers/CSharp/CSharpGrammar/Cs2.grammar`. Constraints appear on methods (after the
parameter list, before the body) and on type declarations (after the base list, before the body).
CS2 only — no `unmanaged` (CS8), `notnull` (CS8), `enum<T>` (CS7.3), or `delegate` (CS7.3).

## Baseline (before changes)
- `dotnet test Tests/CSharpGrammarTests` (fresh build) → **795 passed / 0 failed / 3 skipped** (total 798), per T3.1.1.1.

## Cs2.grammar rules

### New rules (constraint grammar)
- `ConstraintClause = "where" !ReservedKeyword Identifier ":" ConstraintList;` — Roslyn `ParseTypeParameterConstraintClause` (LanguageParser.cs:2250): `where` + identifier + `:` + bounds. The type-parameter-name is a simple **true identifier** (Roslyn `IsTrueIdentifier` / `ParseIdentifierName`), hence `!ReservedKeyword Identifier` (a type-parameter name can never be a reserved keyword).
- `ConstraintList = (Constraint; ",")+;` — one or more comma-separated constraints, no trailing comma (SeparatedList `EndBehavior=Forbidden`, same as `TypeParameterList`). Roslyn: first bound + `,` loop (LanguageParser.cs:2261-2309).
- `Constraint = | "struct" | "class" | NewConstraint = "new" "(" ")" | Type;` — Roslyn `ParseTypeParameterConstraint` (LanguageParser.cs:2346), **CS2 subset**: `struct` | `class` | `new()` | a `Type`. Named union (the meta-grammar forbids `|` inside a group). `struct`/`class`/`new` are reserved keywords (Cs1 `ReservedKeyword`), so none can be a `Type` (a `Type` → `QualifiedName` → `TypeName` → `!ReservedKeyword Identifier`); the four alternatives are mutually exclusive. The `Type` alternative covers an interface / base class / another type-parameter name (the parser parses a `Type`; the binder decides what it is).

### Re-declared rules (REQUIRED-new-construct principle)
- `Method = Attributes? MethodModifier* Type TypeName "(" ParameterList? ")" ConstraintClause+ MethodBody;`
- `InterfaceMethod = Attributes? MethodModifier* Type TypeName "(" ParameterList? ")" ConstraintClause+ ";";`
- `ClassDeclaration = Attributes? ClassModifier* "class" TypeName BaseList? ConstraintClause+ ClassBody ";"?;`
- `StructDeclaration = Attributes? StructModifier* "struct" TypeName BaseList? ConstraintClause+ StructBody ";"?;`
- `InterfaceDeclaration = Attributes? InterfaceModifier* "interface" TypeName BaseList? ConstraintClause+ InterfaceBody ";"?;`
- `DelegateDeclaration = Attributes? DelegateModifier* "delegate" Type TypeName "(" ParameterList? ")" ConstraintClause+ ";";`

The REQUIRED-new-construct is **`ConstraintClause+`** (at least one clause, starting with the
`where` literal). It is placed in the Roslyn position: after the parameter list for methods/delegate
(before the body / `;`), after the base list for class/struct/interface (before the body).

## Method type parameters — how they parse (no explicit rule)
The method's `<T>` is consumed by the **greedy merged `TypeName`** (Boundary decision D1, T3.1.1.1),
so no explicit `TypeParameterList` is needed in the method header:
- **v1** (non-greedy `TypeName = !ReservedKeyword Identifier`): on `void M<T>()`, `TypeName` stops
  at `M`; the Cs1 `Method` then expects `(` but finds `<` → **reject**.
- **v2** (greedy `TypeName`): on `void M<T>()`, `TypeName` eats `M<T>`; the **Cs1 `Method`
  alternative** matches (`(`, param list, body). So a method with type parameters but **no
  constraints** is matched by the Cs1 `Method` alternative, and the Cs2 `Method` alternative
  (which requires `ConstraintClause+`) is reachable only when a constraint clause is present.

## `where` reservation
`where` is **NOT** in Cs1 `ReservedKeyword` (checked: absent from the list at Cs1.grammar:537-618).
It is matched here as a **`WordLiteral`** (raw whole-word text match — `RuleGenerator.cs:77-79`
turns a valid-identifier literal into an `EP.WordLiteral`), so no ambiguity arises: a constraint
clause is recognized only where a rule explicitly expects `"where"`, and the clause requires
`where <id> :` (Roslyn `IsCurrentTokenWhereOfConstraintClause:2234`), so a bare `where` cannot be
mistaken for a type name or base list. (Consequence: `class C where T : struct { }` — a
**non-generic** type with a constraint — parses at v2; this is a **binder** error CS0080, not a
parse error, matching Roslyn's syntax-level acceptance. See the Roslyn-derived positive tests.)

## Type-declaration-constraint approach — **Option A** (chosen)
Option A: re-declare each type declaration in Cs2 with `ConstraintClause+` REQUIRED after the base
list. This works, but the hand-traces (below) show the greedy `TypeName` changes *which* alternative
matches relative to the task's Option-A sketch:

- The T3.1.1.2 type-declaration alternatives **do NOT repeat the `TypeParameterList`**. Because the
  greedy `TypeName` already consumes the non-variance `<...>`, a `TypeParameterList` after it would
  be unreachable (it would see `where`/`{`/`:` and fail). So the T3.1.1.2 alternative places
  `ConstraintClause+` directly after the (greedy) `TypeName` + `BaseList?`.
- The **T3.1.1.1** type-declaration alternatives (with `TypeParameterList`) remain reachable **only
  for the variance case** (`class C<in T> { }`), exactly as in T3.1.1.1 (D1).
- Net matching for a class (identical pattern for struct/interface/delegate):
  - `class C { }` / `class C<T> { }` (no constraints) → **Cs1** alternative (greedy `TypeName`).
  - `class C<in T> { }` (variance, no constraints) → **T3.1.1.1** alternative (`TypeParameterList`).
  - `class C<T> where T : struct { }` (constraints) → **T3.1.1.2** alternative (`ConstraintClause+`).

This is Option A (mutually exclusive, `ConstraintClause+` REQUIRED) with the D1 greedy-`TypeName`
refinement applied. It is functionally complete for all valid CS2 constraint forms and requires no
Cs1 modification.

## Mutual-exclusivity hand-traces (no equal-length tie)

The disambiguating REQUIRED-new-construct is `ConstraintClause+`, which **starts with the `where`
literal**. Every other alternative ends its header with one of `{` (body), `;` (delegate /
interface method), `<` (variance `TypeParameterList`), or `:` (base list). `where` is distinct from
all of these, so the T3.1.1.2 alternative never matches the same input at the same length as the
Cs1 / T3.1.1.1 alternatives.

### Method (alternatives: Cs1, T3.1.1.2)
- No-constraint `void M<T>(T x) { }`: Cs1 → `TypeName=M<T>` (greedy), `(`, `T x`, `)`, `MethodBody={ }` → **match**. T3.1.1.2 → `ConstraintClause+` at `{` → **fail**. → **Cs1 only**, no tie.
- With-constraint `void M<T>(T x) where T : struct { }`: Cs1 → `MethodBody` at `where` → **fail**. T3.1.1.2 → `)`, `ConstraintClause+=where T : struct`, `MethodBody={ }` → **match**. → **T3.1.1.2 only**, no tie.

### InterfaceMethod (alternatives: Cs1, T3.1.1.2)
- No-constraint `void M<T>(T x);`: Cs1 → `)` then `;` → **match**. T3.1.1.2 → `ConstraintClause+` at `;` → **fail**. → **Cs1 only**, no tie.
- With-constraint `void M<T>(T x) where T : struct;`: Cs1 → `;` expected but `where` → **fail**. T3.1.1.2 → `)`, `ConstraintClause+`, `;` → **match**. → **T3.1.1.2 only**, no tie.

### ClassDeclaration (alternatives: Cs1, T3.1.1.1, T3.1.1.2) — Struct/Interface identical
- `class C { }`: Cs1 → `TypeName=C`, `ClassBody={ }` → **match**; T3.1.1.1 → `TypeParameterList` at `{` fail; T3.1.1.2 → `ConstraintClause+` at `{` fail. → **Cs1 only**.
- `class C<T> { }`: Cs1 → `TypeName=C<T>` (greedy), `ClassBody={ }` → **match**; T3.1.1.1 → `TypeParameterList` at `{` fail; T3.1.1.2 → `ConstraintClause+` at `{` fail. → **Cs1 only**.
- `class C<in T> { }`: Cs1 → `TypeName=C` (greedy can't eat `<in T>`), `ClassBody` at `<` fail; T3.1.1.1 → `TypeParameterList=<in T>`, `ClassBody={ }` → **match**; T3.1.1.2 → `ConstraintClause+` at `<` fail. → **T3.1.1.1 only**.
- `class C<T> where T : struct { }`: Cs1 → `ClassBody` at `where` fail; T3.1.1.1 → `TypeParameterList` at `where` fail; T3.1.1.2 → `BaseList?` empty, `ConstraintClause+`, `ClassBody={ }` → **match**. → **T3.1.1.2 only**.
- `class C<T> : B<T> where T : struct { }`: Cs1 → `BaseList=: B<T>`, `ClassBody` at `where` fail; T3.1.1.1 → `TypeParameterList` at `:` fail; T3.1.1.2 → `BaseList=: B<T>`, `ConstraintClause+`, `ClassBody` → **match**. → **T3.1.1.2 only**.

### DelegateDeclaration (alternatives: Cs1, T3.1.1.1, T3.1.1.2)
- No-constraint `delegate void D<T>(T x);`: Cs1 → `TypeName=D<T>` (greedy), `(`, `T x`, `)`, `;` → **match**; T3.1.1.1 → `TypeParameterList` at `(` fail; T3.1.1.2 → `ConstraintClause+` at `;` fail. → **Cs1 only**.
- With-constraint `delegate void D<T>(T x) where T : struct;`: Cs1 → `;` expected but `where` fail; T3.1.1.1 → `TypeParameterList` fail; T3.1.1.2 → `)`, `ConstraintClause+`, `;` → **match**. → **T3.1.1.2 only**.

**Invariant:** for every re-declared rule the T3.1.1.2 alternative's REQUIRED `ConstraintClause+`
starts with `where`; the Cs1 / T3.1.1.1 alternatives end their header with `{` / `;` / `<` / `:`.
Since `where` ≠ any of those, no two alternatives ever match the same input at the same length.

### Known non-match (not a tie, not a regression)
`class C<in T> where T : struct { }` (variance **+** constraints) matches **no** alternative: the
greedy `TypeName` cannot eat `<in T>` (variance), so the T3.1.1.1 alternative (which would supply
the `TypeParameterList`) then fails at the `where`, and the T3.1.1.2 alternative (which relies on
the greedy `TypeName`) fails at the `<`. This is a **binder-invalid** construct on a class (variance
is only legal on interfaces/delegates), so it is acceptable that it does not parse. It is not
required by the task and is not covered by a test. (The same limitation would apply to a
hypothetical `interface I<in T> where T : struct { }`; also not required/tested.)

## Roslyn references (C:\RSDN\roslyn, main, src/Compilers/CSharp/Portable/Parser/LanguageParser.cs)
- `ParseTypeParameterConstraintClauses():2242` — `while (where) add clause` (zero or more clauses).
- `ParseTypeParameterConstraintClause():2250` — `where` + identifier (`IsTrueIdentifier`) + `:` + bounds.
- `ParseTypeParameterConstraint():2346` — `new()` | `struct` | `class` | `default` | `enum`(err) | `delegate`(err) | type. **CS2 subset used: `new()` | `struct` | `class` | type.**
- Method position — `ParseMethodDeclaration():3679` (3691-3698): param list, then constraints, then body.
- Delegate position — `ParseDelegateDeclaration():5835` (5844-5852): name, `TypeParameterList`, param list, constraints, `;`.
- Type-declaration position — `ParseMainTypeDeclaration` (1839-1843): name, type-params, base list, constraints, body.
- `IsCurrentTokenWhereOfConstraintClause():2234` — `where <id> :` lookahead (why a bare `where` is unambiguous).

## Version-purity results
- `class C { void M<T>() { } }` → **rejects at v1**, **parses at v2** (Cs1 Method, greedy TypeName). ✓
- `class C { void M<T>(T x) where T : struct { } }` → **rejects at v1**, **parses at v2** (T3.1.1.2 Method). ✓
- `class C<T> where T : struct { }` → **rejects at v1**, **parses at v2** (T3.1.1.2 ClassDeclaration). ✓
- All pre-existing Cs1 / Cs6 / Cs11 programs still parse (full suite green below).

## Tests
`Tests/CSharpGrammarTests/Cs2ConstraintTests.cs` (CRLF + UTF-8 BOM) — **26 tests, all green**.
- POSITIVE (v2, 13): `Method_StructConstraint`, `Method_InterfaceConstraint`, `Method_NewConstraint`, `Method_TwoConstraintClauses` (`where T : U where U : struct`), `Method_CombinedConstraint` (`where T : IBase, new()`), `Method_ConstraintToEnclosingType` (`where T : C`), `Class_StructConstraint`, `Class_CombinedConstraint` (`where T : IBase, new()`), `Struct_StructConstraint`, `Interface_StructConstraint`, `Class_BaseListAndConstraint` (`class C<T> : Base<T> where T : struct`), `Delegate_StructConstraint`, `InterfaceMethod_StructConstraint`.
- POSITIVE (v2, Roslyn-derived, 5): `Class_TypeConstraintBound` (`class a<b> where b : c { }`, DeclarationParsingTests.cs:1105 `TestClassWithTypeConstraintBound`), `Class_NewConstraintBound` (`class a<b> where b : new() { }`, :1208 `TestClassWithNewConstraintBound`), `Method_GenericTypeConstraintBound` (`class a { b X<c>() where b : d { } }`, :3453 `TestGenericClassMethodWithTypeConstraintBound`), `NonGenericClass_WithConstraint_Parses` (`class a where b : c { }`, :1145 `TestNonGenericClassWithTypeConstraintBound` — parses at syntax level, binder CS0080), `NonGenericMethod_WithConstraint_Parses` (`class a { void M() where b : c { } }`, :1194 `TestNonGenericMethodWithTypeConstraintBound` — parses at syntax level, binder CS0080).
- NEGATIVE (v1, version-purity, 3): `Method_TypeParameter_RejectedAtV1` (`class C { void M<T>() { } }`), `Method_WithConstraint_RejectedAtV1` (`class C { void M<T>(T x) where T : struct { } }`), `Class_TypeConstraint_RejectedAtV1` (`class C<T> where T : struct { }`).
- NEGATIVE (v2, malformed, 5): `Method_MissingColon` (`where T struct`), `Method_MissingName` (`where : struct`), `Method_MissingConstraint` (`where T :`), `Method_TrailingCommaConstraint` (`where T : struct,`), `Method_MissingParameterList` (`void M<T> where T : struct { }` — a method requires the parameter list, so the greedy `TypeName` eats `M<T>` and then `(` is expected but `where` is found → reject).

## Verification
- [x] `dotnet build Nitra.sln --no-incremental` → **success, 0 errors / 0 warnings**.
- [x] `dotnet test Tests/CSharpGrammarTests` (fresh build) → **821 passed / 0 failed / 3 skipped** (total 824).
  - Baseline before T3.1.1.2: 795 passed / 3 skipped (total 798). Delta = **+26** (all new `Cs2ConstraintTests`).
  - Filtered run of `Cs2ConstraintTests` → **26 passed / 0 failed**.
  - Filtered run of pre-existing `Cs2GenericTests` → **23 passed / 0 failed** (no regression).
  - All pre-existing Cs1 / Cs6 / Cs11 tests remain green.
- [x] `dotnet test Tests/ParserTests` (regression; engine + CsNitra meta-grammar unchanged) → **325 passed / 0 failed / 2 skipped** (total 327).

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs2.grammar` (added T3.1.1.2 section: `ConstraintClause`, `ConstraintList`, `Constraint`; re-declared `Method`, `InterfaceMethod`, `ClassDeclaration`, `StructDeclaration`, `InterfaceDeclaration`, `DelegateDeclaration`).
- `Tests/CSharpGrammarTests/Cs2ConstraintTests.cs` (new, 26 tests).
- `docs/CSharpParserPlan-checklist.md` (T3.1.1.2 `[~]` → `[✅]`).
- `docs/CSharpParserPlan-progressT3.1.1.2.md` (this file).

## Boundary decisions / deviations
- **Method type parameters need no explicit rule**: the greedy merged `TypeName` (D1) consumes the
  method's `<...>`, so a method with type parameters but no constraints is matched by the **Cs1
  `Method`** alternative at v2 (and rejected at v1, where `TypeName` is non-greedy). The REQUIRED-
  new-construct that makes the Cs2 `Method` alternative reachable is `ConstraintClause+`, not a
  `TypeParameterList` (which would be unreachable behind the greedy `TypeName`).
- **T3.1.1.2 type-declaration alternatives omit the `TypeParameterList`** (same D1 reason): the
  greedy `TypeName` consumes the non-variance `<...>`, so `ConstraintClause+` is placed directly
  after `TypeName` + `BaseList?`. The T3.1.1.1 alternatives (with `TypeParameterList`) remain for
  the variance case. This is Option A with the D1 refinement; no Cs1 modification.
- **`InterfaceMethod` re-declared** (bonus, beyond the literal test list): interface methods are
  valid CS2 method declarations that can carry constraints (before the `;`); added for completeness
  with one positive test.
- **Constraints on non-generic declarations parse** (`class a where b : c { }`,
  `class a { void M() where b : c { } }`): a **binder** error (CS0080), not a parse error — matches
  Roslyn's syntax-level acceptance (documented in the Roslyn-derived positive tests).
- **Variance + constraints** (`class C<in T> where T : struct { }`) does not parse (see the
  known-non-match note): binder-invalid on a class, not required, not tested.
