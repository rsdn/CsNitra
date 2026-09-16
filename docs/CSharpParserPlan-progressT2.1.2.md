# T2.1.2 — C# 1.0 properties (class/struct + interface) — Progress

## Status: done (build 0 errors; CSharpGrammarTests 620 passed / 0 failed / 3 pre-existing skips; ParserTests 325 / 0 / 2)

## Task
Add **properties** (with accessors) to the C# 1.0 grammar in `Parsers/CSharp/CSharpGrammar/Cs1.grammar`.
SECOND of five member sub-points (T2.1.1 infra+Field → **T2.1.2 Property** → T2.1.3 Method/
Constructor/Destructor → T2.1.4 Operator/Indexer/Event → T2.1.5 finalize).

In scope:
1. Class/struct property with **block-body** accessors (C# 1.0: NO auto-properties).
2. Interface property with **semicolon** accessors (no body) — valid C# 1.0.
3. Abstract class property with **semicolon** accessors (no body) — valid C# 1.0.
4. Add `Property`/`AbstractProperty` to `ClassMember`+`StructMember`; add `InterfaceProperty` to
   `InterfaceMember`.

NOT in scope: Method/Constructor/Destructor (T2.1.3), Operator/Indexer/Event (T2.1.4), base-list
refinements (T2.1.5).

## Rules added (exact, `Parsers/CSharp/CSharpGrammar/Cs1.grammar`)
(added after `FieldModifier`, before `EnumMemberList`)
- `Property = Attributes? PropertyModifier* Type TypeName "{" AccessorDeclaration+ "}" ";"?;`
- `AbstractProperty = Attributes? PropertyModifier* "abstract" PropertyModifier* Type TypeName "{" InterfaceAccessor+ "}" ";"?;`
- `AccessorName = | "get" | "set";`
- `AccessorDeclaration = AccessorName Block;`
- `InterfaceProperty = Attributes? PropertyModifier* Type TypeName "{" InterfaceAccessor+ "}" ";"?;`
- `InterfaceAccessor = AccessorName ";";`
- `PropertyModifier = public|private|protected|internal|static|virtual|override|new|extern|sealed;`

Unions updated:
- `ClassMember = | Field | Property | AbstractProperty | TypeDeclaration;`
- `StructMember = | Field | Property | AbstractProperty | TypeDeclaration;`
- `InterfaceMember = | InterfaceProperty | TypeDeclaration;`

### Meta-grammar limitation hit (important)
The first draft used `AccessorDeclaration = ("get" | "set") Block` and `InterfaceAccessor = ("get" | "set") ";"`
— i.e. `|` INSIDE a group `( ... )`. This is NOT supported by the CsNitra meta-grammar: a Group is
`(` one RuleExpression `)` (`CsNitraParser.cs:103`), and `|` is only a top-level rule operator, not a
`RuleExpression` operator. The malformed group produced a broken rule structure that caused a **stack
overflow** (infinite `ParseSeq`/`ParseAlternative`/`ParseRule` recursion + `ToString()` cycle) at parse
time — the BUILD still succeeded (the grammar is loaded at runtime via `EmbeddedGrammar.LoadCs1Grammar()`),
so the failure only surfaced when running the tests. Fix: extract the get/set union into a named rule
`AccessorName = | "get" | "set";` and reference it from both accessor forms (same workaround as the
T2.2.3 "no `|` in a group" note). Lesson: never write `|` inside `( ... )` in a `.grammar` file.

## Interface-vs-class accessor decision
- Class/struct CONCRETE accessors REQUIRE a **block body**: `AccessorDeclaration = ("get" | "set") Block`.
- Interface accessors and ABSTRACT-class accessors have **no body** (semicolon):
  `InterfaceAccessor = ("get" | "set") ";"`. The SAME rule is reused for both (identical syntactic form).
- Roslyn `ParseAccessorDeclaration` (LanguageParser.cs:4632-4729) parses an accessor as:
  attrs + accessor-modifiers + name(get/set) + ( block | arrow(CS6) | semicolon ). The block path is
  4683-4687, the semicolon path is 4688-4690. It is the BINDER (not the parser) that decides whether a
  semicolon accessor is legal (abstract/interface/extern) or an error (concrete auto-property in C# 1.0).
- Because the parser cannot distinguish "abstract" from "concrete" at the accessor position (the
  `abstract` modifier appears earlier in the sequence), the grammar gates the semicolon form on the
  `abstract` modifier via a separate `AbstractProperty` rule, while `Property` (concrete) requires a
  block body. This is what makes `class C { int P { get; set; } }` (auto-property, CS3) REJECT while
  `abstract class A { abstract int X { get; set; } }` ACCEPT — both have `get; set;` accessors, only
  the `abstract` modifier differs.

## `value` keyword finding
- `value` is NOT in the grammar's `ReservedKeyword` rule (Cs1.grammar:242-323 — verified by grep, no
  `"value"`). The `Identifier` terminal is `[_\l]\w*` (CSharpTerminals.cs:8), so `value` matches
  `Identifier`, and `IdentifierName = !ReservedKeyword Identifier` therefore matches `value`.
- CONCLUSION: `value` is ALREADY usable as a true identifier in expression position. `set { _x = value; }`
  parses with NO change (no D2-style fix needed). `this` is already a `Primary` alternative
  (Cs1.grammar:384), so `get { return this._x; }` also works unchanged.

## Property modifier set (C# 1.0)
- `PropertyModifier` (concrete class/struct) = access (`public`/`private`/`protected`/`internal`) +
  `static`/`virtual`/`override`/`new`/`extern`/`sealed` (10 keywords).
- `abstract` is the 11th property modifier, handled by the `AbstractProperty` rule (which REQUIRES it)
  rather than by `PropertyModifier` — because abstract properties use semicolon accessors, not block
  bodies. Full task set (access + static/virtual/override/abstract/new/extern/sealed) is covered.
- Ordering NOT enforced (Roslyn `ParseModifiers` LanguageParser.cs:1347-1484 is a generic loop;
  `SafeModifierParsingTests.Property` uses `safe public extern` / `public extern safe` — any order).
  Excluded (version-purity / wrong kind): `async` (CS5), `partial` (CS2), `unsafe` (method/type
  modifier), `const`/`readonly`/`volatile` (field-only).

## Boundary decisions / deviations
- **D1: `AbstractProperty` requires `abstract` via `PropertyModifier* "abstract" PropertyModifier*`.**
  `PropertyModifier` excludes `abstract`, so the surrounding `*` loops cannot greedily consume the
  required `abstract` (avoids a greedy-`*` + required-token conflict). Handles `abstract`,
  `public abstract`, `abstract public` (any order around `abstract`).
- **D2: `extern` property with semicolon accessors is REJECTED (known boundary).** `extern` is a valid
  C# 1.0 property modifier and is accepted in `PropertyModifier` (so `extern int P { get { } set { } }`
  parses as a concrete property, loose). But `extern int P { get; set; }` (a valid C# extern/abstract
  property) is REJECTED: `Property` requires block accessors, `AbstractProperty` requires `abstract`
  (not `extern`). Out of the task's required test set (the task specifies class/struct accessors REQUIRE
  a block body, with the abstract exception only). Documented, not fixed.
- **D3: `AbstractProperty` is in BOTH `ClassMember` and `StructMember` (loose).** Structs cannot be
  abstract, so `struct S { abstract int X { get; set; } }` is invalid C# and would be rejected by the
  binder, but the PARSER accepts it (loose, consistent with T2.1.1 D2 / Roslyn parser-level behavior).
  Kept for grammar symmetry; not tested.
- **D4: `InterfaceProperty` uses `PropertyModifier*` (loose).** C# 1.0 interface members cannot have
  access/virtual/etc. modifiers (binder rejects), but the PARSER accepts them (Roslyn `ParseModifiers`
  consumes any modifier keyword). Not in the required negative set. `abstract` on an interface member is
  REJECTED (correct) because `PropertyModifier` excludes `abstract`, so `interface I { abstract int X
  { get; set; } }` fails (no rule matches).
- **D5: trailing `";"?` on `Property`/`AbstractProperty`/`InterfaceProperty`** is kept per the task
  spec. It is optional and only matches a (semantically invalid) trailing `;` after the accessor list;
  it never affects the valid cases (the next member or `}` follows).

## Tests
All in `Tests/CSharpGrammarTests/Cs1PropertyTests.cs` (41 new tests, parsed via `Cs1RoslynTestHelper`
from the `"Grammar"` start rule), all green. The two T2.1.1 negative tests in `Cs1MemberTests.cs`
(`Property_NotMemberYet_Fails`, `AutoProperty_NotMemberYet_Fails`) were renamed to
`Property_AutoGetterInClass_Fails` / `Property_AutoPropertyInClass_Fails` (Property IS now a member; the
forms are CS3 auto-properties).

- **POSITIVE: 33**
  - Concrete class property, block-body accessors (21): get/set, get-only, static, `this` in getter,
    each access modifier (public/private/protected/internal), virtual, override, sealed override, new,
    extern, multi-modifier (`public static`), out-of-order (`static public`), attributed, array type,
    qualified type, field-before-property, struct, struct multi-modifier.
  - Abstract class property, semicolon accessors (5): get/set, get-only, `public abstract`, out-of-order
    (`abstract public`), `protected abstract`.
  - Interface property, semicolon accessors (4): get/set, get-only, mixed with nested type, multiple.
  - Roslyn-derived (3): `SafeModifierParsingTests.Property` (any-order modifiers, `safe`→`static`),
    `AccessorOverriddenOrHiddenMembersTests.cs:247` (`public abstract int P2 { get; }`),
    `SemanticErrorTests.cs:8036` (`abstract protected object P { get; }`).
- **NEGATIVE: 8**
  - Auto-property/auto-getter/auto-setter in a class (CS3): `int P { get; set; }`, `int X { get; }`,
    `int X { set; }`.
  - Interface accessor with a body: `interface I { int X { get { return 1; } } }` (reject).
  - Generic property (CS2, version purity): `int X<T> { get { ... } }`.
  - Empty accessor list: `int X { }`.
  - Keyword name: `int class { get { ... } }`.
  - Abstract property with a block body: `abstract int X { get { return 1; } }` (reject).

## Verification
- `dotnet build Nitra.sln --no-incremental` → **Build succeeded, 0 Warning(s), 0 Error(s)**.
- `dotnet test Tests/CSharpGrammarTests` (fresh build, default platform) → **Passed: 620, Failed: 0,
  Skipped: 3** (3 skips are pre-existing `RawString`/`StringLiteral` tests). New T2.1.2 tests: 41
  (`Cs1PropertyTests`), all green (verified via filtered run: `Passed: 41, Failed: 0`).
- `dotnet test Tests/ParserTests` → **Passed: 325, Failed: 0, Skipped: 2** (sanity; engine + CsNitra
  meta-grammar NOT changed — only the C# grammar text + tests + docs).
- Note: mid-task, the Roslyn MCP x64 runner reported `FileNotFoundException: CSharpGrammar` because a
  stale incremental x64 state (after manually deleting `CSharpGrammar.dll`/`obj` to force a re-embed)
  left `CSharpGrammar.dll` out of the x64 test output. A clean-from-scratch build fixes it: verified both
  the canonical CI path (default/AnyCPU) AND `-p:Platform=x64` are **620 passed / 0 failed / 3 skipped**
  after `Remove-Item bin,obj` + `dotnet build --no-incremental`. No build config was changed by T2.1.2.

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs1.grammar` (unions + Property/AbstractProperty/AccessorName/
  AccessorDeclaration/InterfaceProperty/InterfaceAccessor/PropertyModifier).
- `Tests/CSharpGrammarTests/Cs1MemberTests.cs` (rename the two property negative tests).
- `Tests/CSharpGrammarTests/Cs1PropertyTests.cs` (new; 41 tests).
- `docs/CSharpParserPlan-checklist.md` (T2.1.2 → `[✅]`).
- `docs/CSharpParserPlan-progressT2.1.2.md` (this file).
