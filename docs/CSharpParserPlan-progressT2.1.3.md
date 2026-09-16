# T2.1.3 — C# 1.0 methods, constructors, destructors (+ interface methods) — Progress

## Status: done (build 0 errors; CSharpGrammarTests 678 passed / 0 failed / 3 pre-existing skips; ParserTests 325 / 0 / 2; full solution 1013 / 0 / 5)

## Task
Add **methods**, **constructors**, and **destructors** to the C# 1.0 grammar in
`Parsers/CSharp/CSharpGrammar/Cs1.grammar`. THIRD of five member sub-points
(T2.1.1 infra+Field → T2.1.2 Property → **T2.1.3 Method/Constructor/Destructor** → T2.1.4
Operator/Indexer/Event → T2.1.5 finalize).

In scope:
1. `Method` (class/struct): `Attributes? MethodModifier* Type TypeName "(" ParameterList? ")" ( Block | ";" )`.
2. `InterfaceMethod` (interface): `Attributes? MethodModifier* Type TypeName "(" ParameterList? ")" ";"` (no body).
3. `Constructor` (class/struct): `Attributes? ConstructorModifier* TypeName "(" ParameterList? ")" ConstructorInitializer? Block`.
4. `Destructor` (class/struct): `~` TypeName **`()`** Block (Roslyn: empty parens REQUIRED — see D1).
5. `MethodModifier` / `ConstructorModifier` sets.
6. `base`-in-expression fix (`BaseMember` Primary alternative) — see D2.

NOT in scope: Operator/Indexer/Event (T2.1.4), base-list refinements (T2.1.5).

## Rules added (exact, `Parsers/CSharp/CSharpGrammar/Cs1.grammar`)
- `Method = Attributes? MethodModifier* Type TypeName "(" ParameterList? ")" MethodBody;`
- `MethodBody = | Block | ";";`
- `Constructor = Attributes? ConstructorModifier* TypeName "(" ParameterList? ")" ConstructorInitializer? Block;`
- `ConstructorInitializer = ":" ConstructorInitializerKeyword ArgumentList;`
- `ConstructorInitializerKeyword = | "base" | "this";`
- `Destructor = Attributes? MethodModifier* "~" TypeName "(" ")" Block;`
- `InterfaceMethod = Attributes? MethodModifier* Type TypeName "(" ParameterList? ")" ";";`
- `MethodModifier` = access + static/virtual/override/abstract/new/extern/sealed/unsafe (12).
- `ConstructorModifier` = access + extern/static/new (7).
- `Primary`: + `BaseMember = "base" "." !ReservedKeyword Identifier`.

Unions updated:
- `ClassMember = | Field | Property | AbstractProperty | Method | Constructor | Destructor | TypeDeclaration;`
- `StructMember = | Field | Property | AbstractProperty | Method | Constructor | Destructor | TypeDeclaration;`
- `InterfaceMember = | InterfaceMethod | InterfaceProperty | TypeDeclaration;`

## Roslyn semantics ported (C:\RSDN\roslyn, src/Compilers/CSharp/Portable/Parser/LanguageParser.cs)
- **Dispatch** (`ParseMemberDeclarationCore` 3238): attrs + `ParseModifiers` (generic loop) → then:
  - identifier + `(` (PeekToken(1)==OpenParen) → `ParseConstructorDeclaration` (3267-3270, 3509-3528).
  - `~` (TildeToken) → `ParseDestructorDeclaration` (3273-3276, 3572-3587).
  - else parse a return `Type` (`ParseReturnType` 3738), then member name, then indexer/property/method
    (`ParseMethodDeclaration` 3382, 3679-3736).
- **`ParseModifiers`** (1347-1484) — generic `while` loop consuming **any** modifier keyword in **any
  order** (`GetModifierExcludingScoped` 1286-1345). Per-kind legality/ordering are **binder** concerns.
  So `MethodModifier*` / `ConstructorModifier*` accept any order (consistent with T2.1.1 D2).
- **`ParseConstructorInitializer`** (3544-3568): `:` + (`base`|`this`) + **required** argument list
  (`ParseParenthesizedArgumentList`, 3556-3561 — parens eaten even when empty). `TryParseConstructorInitializer`
  (3530-3542) triggers on `:` (the `=> this(...)`/`=> base(...)` form is CS6, out of scope).
- **`ParseDestructorDeclaration`** (3572-3587): `~` + name + **empty `ParameterList`**
  (`EatToken(OpenParen)` + `EatToken(CloseParen)`, 3578-3581) + body. **Parens are REQUIRED.**

## Disambiguation Method vs Constructor (longest-match)
A method is `Type TypeName (` (TWO names before `(`), a constructor is `TypeName (` (ONE name). A method
always consumes >= one more name token, so they cannot match the same input at the same length → no
equal-length tie:
- `int Foo() { }`   → Method (Type=int, Name=Foo); Constructor fails (`int` is reserved, `TypeName = !ReservedKeyword Identifier` fails).
- `Foo() { }`       → Constructor (Name=Foo); Method fails (needs a 2nd `TypeName` before `(`, sees `(`).
- `Point GetPoint()`→ Method (Type=Point, Name=GetPoint); Constructor fails (name followed by another name, not `(`).

## Disambiguation Method vs Field (longest-match)
Both start `Type Name`; the token after the name disambiguates — `(` → Method, `;`/`,`/`=` → Field
(VariableDeclarator). Mutually exclusive, no tie:
- `int x = 5;`    → Field (name followed by `=`).
- `int Foo() { }` → Method (name followed by `(`; Field needs `;` after the name, sees `(`).

## Constructor-initializer `:` handling
`ConstructorInitializer = ":" ConstructorInitializerKeyword ArgumentList`. The `:` here follows the
constructor's parameter list `)`. It is distinct from a type-declaration base list (`class C : Base` —
the `:` is after the type name, before the `{`); no conflict, since a constructor initializer only appears
after a parameter list. `ArgumentList` (T2.3.1) already includes the parens and is REQUIRED (Roslyn eats
the parens even when empty — `: base()`).

## Destructor form (Roslyn) — D1 deviation from task premise
Roslyn `ParseDestructorDeclaration` (3572-3587) parses `~` + name + **empty `ParameterList` (`()`)** +
block. Roslyn tests confirm `~C() { }` is the valid form (`DeclarationParsingTests.cs:3616`
`"class a { ~a() { } }"`, `DestructorTests.cs` throughout, `IncrementalParsingTests.cs:159`).
**The task's premise ("NO parentheses", `~C { }`) is WRONG.** Corrected: `~C() { }` is POSITIVE,
`~C { }` (no parens) is NEGATIVE. `Destructor = Attributes? MethodModifier* "~" TypeName "(" ")" Block`.

## `base`/`this`-in-expression finding — D2 fix
- `this` is already a bare `Primary` alternative (Cs1.grammar) → `return this._x;` parses unchanged.
- `base` IS in `ReservedKeyword` (line 316) with NO `Primary` alternative → `base.Foo()` in a method body
  (e.g. `class C : B { void M() { base.Foo(); } }`) would NOT parse (D2-type defect, like `int.Parse()`).
- **Fix** (mirrors `PredefinedMember`, T2.3.3): add `Primary` alternative
  `BaseMember = "base" "." !ReservedKeyword Identifier`. `base` is a valid expression-start only when
  immediately followed by `.` + a true identifier; `base` alone is NOT an expression (C#: `base` is only
  valid as `base.Member`). The `IdentifierName = !ReservedKeyword Identifier` guard is untouched.
- Known boundary: `base[0]` (base indexer) does not parse (`BaseMember` requires `.`), consistent with
  the `PredefinedMember` `int[5]` boundary (T2.3.3).

## Method/constructor modifier sets (C# 1.0)
- `MethodModifier` (12) = access (`public`/`private`/`protected`/`internal`) +
  `static`/`virtual`/`override`/`abstract`/`new`/`extern`/`sealed`/`unsafe`. `unsafe` is core C# 1.0
  (no feature gate; pointer types since T1.4 — `unsafe void M() { }`, `unsafe ~C() { }`).
  Excluded (version-purity / wrong kind): `async` (CS5), `partial` (CS2), `readonly`/`volatile`
  (field-only), `ref` (CS7.2).
- `ConstructorModifier` (7) = access + `extern`/`static`/`new`. `new` is valid on constructors (hides a
  base constructor). Excluded: `virtual`/`override`/`abstract`/`sealed` (not constructor modifiers),
  `unsafe` (D3).

## Boundary decisions / deviations
- **D1: Destructor has empty parens** (Roslyn 3578-3581; task premise corrected). `~C() { }` positive,
  `~C { }` negative.
- **D2: `BaseMember` Primary alternative** added for `base.Foo()` in expressions (see above).
- **D3: `ConstructorModifier` excludes `unsafe`.** Roslyn's generic `ParseModifiers` loop accepts `unsafe`
  before a constructor (binder rejects), but `unsafe` constructors are not in the task's set and are rare;
  `MethodModifier` (used for the destructor) includes `unsafe` so `unsafe ~C() { }` parses.
- **D4: `MethodBody = | Block | ";"` is loose.** A concrete `void Foo();` (semicolon body) is a binder
  error, not a parse error (T1.3.1 split); the parser accepts it. `InterfaceMethod` is NOT loose — it
  REQUIRES `";"` (no block body), so `interface I { void Foo() { } }` rejects (default interface methods
  are CS8).
- **D5: `Destructor` reuses `MethodModifier`** (loose, faithful to Roslyn's generic loop). `~` is
  unambiguous (no other member starts with `~`), so no disambiguation issue; the binder rejects
  invalid destructor modifiers (e.g. `public ~C() { }`).
- **D6: `Constructor` body is a required `Block`** (C# constructors always have a block body; `C() ;` is
  invalid). No `MethodBody`-style `";"` alternative for constructors.

## Tests
All in `Tests/CSharpGrammarTests/Cs1MethodTests.cs` (58 new tests, parsed via `Cs1RoslynTestHelper`
from the `"Grammar"` start rule), all green (verified via filtered run: `Passed: 58, Failed: 0`).

- **POSITIVE: 48**
  - Methods basic (5): `void Foo() { }`, `int Add(int a, int b) { return a + b; }`, `void Foo()
    { return; }`, `static void Main() { }`, `public override int GetHashCode() { return 0; }`.
  - Methods modifiers (7): virtual, sealed override, new, unsafe, attributed, multi-modifier
    (`public static`), out-of-order (`static public`).
  - Methods return-type/params (5): array return, qualified return (`A.B`), pointer return
    (`unsafe int*`), `params int[]`, `ref`/`out` params.
  - Methods bodies (2): `this._x` in body, `base.Foo()` in body (verifies the `BaseMember` fix).
  - Method struct (1): `struct S { void Foo() { } }`.
  - Method-vs-field (1): `int x = 5; int Foo() { return x; }` (field + method in one body).
  - Method-vs-constructor disambiguation (3, task examples): `int Foo() { return 0; }` (method),
    `Point GetPoint() { return null; }` (method), `Foo() { }` (constructor — single name + parens).
  - Constructors (11): no-params, params, `: base()`, `: this(x)`, `: this(x, y)`, static, access
    modifier, `new`, attributed, mixed-with-field, struct.
  - Destructors (4): `~C() { }`, `unsafe ~C() { }`, `[Attr] ~C() { }`, `~C() { GC.SuppressFinalize(this); }`.
  - Interface methods (4): `void Foo();`, `int Add(int a, int b);`, multiple, mixed-with-nested-type.
  - Abstract/extern (2): `abstract int Foo();`, `extern void Foo();`.
  - Roslyn-derived (3): `class a { ~a() { } }` (DeclarationParsingTests.cs:3616 — destructor parens),
    `public static extern void M();` (SafeModifierParsingTests.Method, `safe`→`static`),
    `static C() : this() { }` (ParserErrorMessageTests.cs:986 CS0514 — parses, binder rejects).
- **NEGATIVE: 10**
  - Destructor no parens: `class C { ~C { } }` (D1 — Roslyn requires `()`).
  - Expression-bodied member (CS6): `class C { int P => _x; }`.
  - async modifier (CS5): `class C { async void Foo() { } }`.
  - Missing body/`;`: `class C { void Foo() }`.
  - Generic method (CS2): `class C { void Foo<T>() { } }`.
  - partial modifier (CS2): `class C { partial void Foo() { } }`.
  - Interface method with body (CS8): `interface I { void Foo() { } }`.
  - Constructor missing block: `class C { C() }`.
  - Constructor initializer without parens: `class C { C() : base { } }`.
  - Reserved method name: `class C { void void() { } }`.

## Verification
- `dotnet build Nitra.sln --no-incremental` → **Build succeeded, 0 Warning(s), 0 Error(s)**
  (SDK 8.0.411, Debug/x64).
- `dotnet test Tests/CSharpGrammarTests` (fresh build) → **Passed: 678, Failed: 0, Skipped: 3**
  (3 skips are pre-existing `RawString`/`StringLiteral` tests). New T2.1.3 tests: 58
  (`Cs1MethodTests`), all green (verified via filtered run: `Passed: 58, Failed: 0`).
- `dotnet test Tests/ParserTests` → **Passed: 325, Failed: 0, Skipped: 2** (sanity; engine + CsNitra
  meta-grammar NOT changed — only the C# grammar text + tests + docs + a `.csproj` fix).
- Full solution `dotnet test Nitra.sln` → **Passed: 1013, Failed: 0, Skipped: 5**.

## Build fix (pre-existing NETSDK1022, unblocked by this task)
A prior session had added explicit `<Compile Include="Cs1MethodTests.cs" />` +
`<Compile Include="Cs1PropertyTests.cs" />` to `Tests/CSharpGrammarTests/CSharpGrammarTests.csproj`.
These duplicate the SDK's default `.cs` globbing → **NETSDK1022 (Duplicate 'Compile' items)**, which an
incremental build masked until a forced rebuild. Removed the redundant `<ItemGroup>` (the SDK auto-globs
all `.cs` files) — consistent with the AGENTS.md NETSDK1022 convention (T0.1 removed the same from
ParserTests.csproj). No test file was lost (all 26 `.cs` files still compile via the SDK glob).

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs1.grammar` (unions + Method/MethodBody/Constructor/
  ConstructorInitializer/ConstructorInitializerKeyword/Destructor/InterfaceMethod/MethodModifier/
  ConstructorModifier + Primary `BaseMember`).
- `Tests/CSharpGrammarTests/Cs1MethodTests.cs` (new; 58 tests).
- `Tests/CSharpGrammarTests/CSharpGrammarTests.csproj` (removed redundant `<Compile Include>` entries
  — NETSDK1022 fix).
- `docs/CSharpParserPlan-checklist.md` (T2.1.3 → `[✅]`).
- `docs/CSharpParserPlan-progressT2.1.3.md` (this file).
