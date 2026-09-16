# T2.1.5 — C# 1.0 type-member grammar finalization — Progress

## Status: done (build 0 errors; CSharpGrammarTests 753 passed / 0 failed / 3 pre-existing skips; ParserTests 325 / 0 / 2)

## Task
**Finalize** the C# 1.0 type-member grammar in `Parsers/CSharp/CSharpGrammar/Cs1.grammar`. FIFTH (FINAL)
of five member sub-points (T2.1.1 infra+Field → T2.1.2 Property → T2.1.3 Method/Constructor/Destructor →
T2.1.4 Operator/Indexer/Event → **T2.1.5 finalize**). This sub-point is about **integration, nested types,
base lists, version-purity, and full verification** — NOT adding new member kinds.

In scope:
1. **Nested types as members** — verify `TypeDeclaration` (already in the `ClassMember`/`StructMember`/
   `InterfaceMember` unions) works fully with ALL the new member kinds inside nested types; deeply-nested
   cases (2-level, 3-level, nested struct/enum/interface/delegate, nested type with operators/indexers/events).
2. **Base lists** — verify `BaseList` (T1.3) works with the new member bodies (single/multiple/qualified
   bases; class/struct/interface; base list + members).
3. **Version-purity sweep** — scan `Cs1.grammar` for accidentally-included CS2+ features; confirm
   generics / `int?` / `var` / `async`/`await` / expression-bodied members / auto-properties /
   object/collection initializers / lambdas / `dynamic` are ABSENT (reject).
4. **Full verification** — build + complete test suite green.

NOT in scope: new member kinds, engine changes, Stage 3 (CS2+).

## Baseline (before T2.1.5)
- `dotnet build Nitra.sln --no-incremental` → 0 errors.
- `dotnet test Tests/CSharpGrammarTests` → 719 passed / 0 failed / 3 pre-existing skips.

## Roslyn reference (C:\RSDN\roslyn, src/Compilers\CSharp\Portable\Parser\LanguageParser.cs)
- **`ParseBaseList`** (2170-2232): `TryEatToken(Colon)` → if none, return null (no base list). Then parse
  **at least one** base `Type` (required after the colon), then loop while not `{`/`;`/`where`: on `,` eat
  it and parse another `Type`. → `: Type (, Type)*`. Matches the grammar `BaseList = ":" (Type; ",")+`.
  (The `PrimaryConstructorBaseType` path at 2183-2185 is a C# 12 feature, not C# 1.0; the grammar omits it.)
- **`ParseMainTypeDeclaration`** (1789-...): the base list is parsed (1830) AFTER the type name and BEFORE
  the constraint clauses / body. A nested type declaration is just a `TypeDeclaration` member (the member
  loop at 1872-1895 calls `ParseMemberDeclaration`, which re-dispatches to `ParseMainTypeDeclaration` for
  `class`/`struct`/`interface`/... → natural recursion).
- **Nested-type tests** (C:\RSDN\roslyn\src\Compilers\CSharp\test\Syntax\Parsing\DeclarationParsingTests.cs):
  - `TestNestedClass` (1705): `class a { class b { } }`.
  - `TestNestedPrivateClass` (1745): `class a { private class b { } }`.
  - `TestNestedPublicClass` (1911): `class a { public class b { } }`.
  - `TestNestedDelegate` (2431): `class a { delegate b c(); }`.

## Verification (nested types + base lists)
**Nested types work fully with ALL member kinds — no grammar fix was needed.** `TypeDeclaration` is already
in the `ClassMember`/`StructMember`/`InterfaceMember` unions (T2.1.1), and a nested `ClassDeclaration`/
`StructDeclaration`/... is a `TypeDeclaration` member whose own body is a member-list, so nesting is naturally
recursive. Verified positive (all parse cleanly):
- 2-level nesting with property + method inside the nested class.
- A class nesting a struct (with a field), an enum, an interface (with a method), and a delegate.
- 3-level nesting (`A > B > C`, field at the innermost level).
- A nested type with an operator + indexer + event (T2.1.4 kinds inside nesting).
- A nested class with field + property + method + constructor + operator + indexer + event.
- Nested struct-in-struct, nested interface-in-interface.
- A nested type with its own base list (task 1 + task 2 combined).
- A class nested in a struct that also has its own field (member + nested type in the same body).
- Roslyn-derived: `class a { class b { } }`, `class a { private class b { } }`, `class a { public class b { } }`,
  `class a { delegate b c(); }`.

**Base lists work with the new member bodies — no grammar fix was needed.** `BaseList = ":" (Type; ",")+`
(T1.3) matches Roslyn `ParseBaseList` (`: Type (, Type)*`). Verified positive (all parse cleanly):
- Single base (`class C : Base1 { }`), multiple bases (`class C : Base1, Base2 { }`), three bases.
- Qualified bases (`class C : System.Object, IDisposable { }`).
- Struct base (`struct S : IDisposable { }`), interface base (`interface I : Base1, Base2 { }`).
- Base list + members (`class C : Base { int X { get { return 0; } set { } } void M() { } }`).
- Enum base list (`enum E : int { A, B }`, via `EnumBaseList = ":" Type`).
- An array base type (`class C : int[] { }` — the `Type` rule allows the array postfix in a base list).

Disambiguation: a nested type declaration starts with the `class`/`struct`/`interface`/`enum`/`delegate`
reserved keyword, which cannot be a `Type`/`TypeName` (reserved) → `Field`/`Property`/`Method`/etc. fail on
that token; a member starts with a type-name/modifier/attribute, which is never a declaration keyword →
`TypeDeclaration` fails on that token. Mutually exclusive at the leading token, so no equal-length tie arises
(the T2.1.1 disambiguation argument, re-confirmed for the full member set).

## Fixes made
**No grammar fixes were required** — all nested-type and base-list cases parsed cleanly on the first run.

One **build-hygiene fix** (not a grammar change): the working tree's `Tests/CSharpGrammarTests/
CSharpGrammarTests.csproj` had been modified (by the build process, same timestamp as this session) to add an
explicit `<ItemGroup>` with `<Compile Include="Cs1MemberIntegrationTests.cs" />` +
`<Compile Include="Cs1OperatorIndexerEventTests.cs" />`. These duplicate the SDK's default `.cs` globbing →
**NETSDK1022 (Duplicate 'Compile' items)**, which broke the Roslyn MCP workspace load (the build's own MSBuild
instance masked it). Removed the redundant `<ItemGroup>` (the SDK auto-globs all `.cs` files) — the same fix
applied in T2.1.3 (and T0.1 for ParserTests.csproj). The csproj is back to its committed state.

## Version-purity sweep results
Scanned `Cs1.grammar` for CS2+ feature tokens. **All CS2+ FEATURE forms are ABSENT (reject); no leaks found.**
Confirmed absent (grep for `partial|yield|async|await|dynamic|=>|\bvar\b` in `Cs1.grammar` matched ONLY the
comments that document the exclusion, e.g. lines 152/219/287; and `"var"|"dynamic"|"async"|"await"|"partial"|
"yield"` are NOT in the `ReservedKeyword` rule):

| Feature (version) | Form | Result | Why |
|---|---|---|---|
| Generics (CS2) | `class C<T> { }`, `void M<T>() { }`, `List<int> x;` | **REJECT** | No type-parameter list after a type name; `<` is not a `Type` postfix (no type-argument list). |
| Nullable value types (CS2) | `int? x;` | **REJECT** | `?` is not a `Type` postfix; `int?` is not a valid `Type`, so `x;` cannot be a `VariableDeclarator`. |
| `var` (CS3) | type inference | **ABSENT (feature)** | No special syntax. `var` is not reserved → a valid type name; `var x = 1;` parses as a type-name declaration (correct C# 1.0). The CS3 feature form is syntactically identical to a type-name declaration, so it cannot be rejected at the parse level (D1). |
| `async` (CS5) | `async void Foo() { }` | **REJECT** | `async` is not a method modifier; parsed as a `Type` name, then `void` (reserved) cannot be the method name. |
| `await` (CS5) | `return await x;` | **REJECT** | `await` is not an expression operator; `await x` is not a valid `Expression` (a bare `await x;` parses as a local declaration of type `await` named `x` — valid C# 1.0 — so the test uses `return` to force the expression reading). |
| Expression-bodied members (CS6) | `int P => _x;` | **REJECT** | No `=>` token in the grammar (already negative in T2.1.3 `Method_ExpressionBodied_Fails`). |
| Auto-properties in classes (CS3) | `int P { get; set; }` | **REJECT** | Class/struct accessors require block bodies (already negative in T2.1.2 `Property_AutoPropertyInClass_Fails`). |
| Object initializers (CS3) | `new Foo { X = 1 }` | **REJECT** | `NewBody` requires `[` (ArrayCreation) or `(` (ObjectCreation) after the base type; `{` matches neither. |
| Collection initializers (CS3) | `new Foo { 1, 2 }` | **REJECT** | Same as object initializers; only array initializers `new T[] { ... }` are C# 1.0. |
| Lambdas (CS3) | `N(x => x + 1)` | **REJECT** | No `=>` token; `x` parses as the argument `Expression`, the following `=>` is not a `PostfixOp`/`,`/`)`. |
| `dynamic` (CS4) | dynamic dispatch | **ABSENT (feature)** | No special syntax. `dynamic` is not reserved → a valid type name; `dynamic x = 1;` parses as a type-name declaration (correct C# 1.0). The CS4 feature form is syntactically identical to a type-name declaration (D1). |

Note: `partial` (CS2) and `yield` (CS2) are also absent (not in any rule or in `ReservedKeyword`).

## Kitchen-sink test
`KitchenSink_Succeeds` — a single class with a field, a property, a method, a constructor, a destructor, an
operator, an indexer, an event, and a nested type — all member kinds from T2.1.1–T2.1.4 in one body:
```
class Kitchen { int f; int P { get { return 0; } set { } } void M() { } Kitchen() { } ~Kitchen() { }
  public static Kitchen operator +(Kitchen a, Kitchen b) { return a; }
  public int this[int i] { get { return 0; } set { } } public event EventHandler Changed;
  class Nested { int x; } }
```
Parses cleanly.

## Tests (pos/neg)
All in `Tests/CSharpGrammarTests/Cs1MemberIntegrationTests.cs` (34 new tests, parsed via
`Cs1RoslynTestHelper` from the `"Grammar"` start rule), all green (verified via a fresh full run:
753 passed / 0 failed / 3 pre-existing skips; 753 − 719 baseline = 34 new).

- **POSITIVE: 25**
  - Nested types (task 1, 9): property+method in a nested class; struct/enum/interface/delegate nested in a
    class; 3-level nesting; nested class with operator+indexer+event; nested class with all member kinds;
    nested struct-in-struct; nested interface-in-interface; nested class with a base list; class nested in a
    struct that has its own field.
  - Roslyn-derived nested types (4): `class a { class b { } }` (DeclarationParsingTests.cs:1705),
    `class a { private class b { } }` (:1745), `class a { public class b { } }` (:1911),
    `class a { delegate b c(); }` (:2431).
  - Base lists (task 2, 9): single base; multiple bases; qualified bases; struct base; interface base;
    base list + members; enum base list; three bases; array base type.
  - Kitchen sink (1): all member kinds + a nested type in one class.
  - `var`/`dynamic` as type names (2, boundary D1): `var x = 1;` (local), `dynamic x = 1;` (local).
- **NEGATIVE: 9**
  - Generics (CS2): `class C<T> { }`, `void M<T>() { }`, `List<int> x;`.
  - Nullable value type (CS2): `int? x;`.
  - `async` (CS5): `async void Foo() { }`.
  - `await` (CS5): `return await x;`.
  - Object initializer (CS3): `new Foo { X = 1 }`.
  - Collection initializer (CS3): `new Foo { 1, 2 }`.
  - Lambda (CS3): `N(x => x + 1)`.

## Full verification
- `dotnet build Nitra.sln --no-incremental` → **Build succeeded, 0 Warning(s), 0 Error(s)** (SDK 8.0.411, Debug/x64).
- `dotnet test Tests/CSharpGrammarTests` (fresh build) → **Passed: 753, Failed: 0, Skipped: 3**
  (3 skips are pre-existing `RawString`/`StringLiteral` tests). New T2.1.5 tests: 34
  (`Cs1MemberIntegrationTests`), all green (753 − 719 baseline = 34).
- `dotnet test Tests/ParserTests` → **Passed: 325, Failed: 0, Skipped: 2** (sanity; engine + CsNitra
  meta-grammar NOT changed — only the C# grammar tests + docs).

## Boundary decisions / deviations
- **D1: `var`/`dynamic` as type NAMES parse (POSITIVE); the CS3/CS4 FEATURE forms are not rejected.**
  `var` (CS3) and `dynamic` (CS4) are NOT reserved keywords in C# 1.0 (they became contextual keywords in
  C# 3 and C# 4 respectively), so they are valid identifiers / type names. `var x = 1;` and `dynamic x = 1;`
  parse as local declarations of type `var`/`dynamic` — valid C# 1.0. The CS3 type-inference and CS4
  dynamic-dispatch FEATURE forms are syntactically IDENTICAL to a type-name declaration, so they CANNOT be
  rejected at the parse level without also rejecting valid C# 1.0 code. The task's NEGATIVE list for
  `var x = 1;` (local) and `dynamic` is resolved by the task's own note ("`var`/`dynamic` as type NAMES
  parse — that's correct"); the FEATURE forms are absent from the grammar (no special syntax), which is the
  version-purity guarantee. Tested as POSITIVE with clear comments.
- **D2: `await` tested via `return await x;` (not a bare `await x;`).** A bare `await x;` parses as a local
  declaration of type `await` named `x` (valid C# 1.0 — `await` is not reserved). The `return` forces the
  `await x` to be read as an `Expression`, where it fails (`await` is a Primary/identifier, the following
  `x` is not a `PostfixOp`). This isolates the CS5 `await`-expression FEATURE (absent) from the valid
  `await`-as-name reading.

## Working-tree hygiene notes
- The build process (same timestamp as this session) added a UTF-8 BOM to the pre-existing
  `Tests/CSharpGrammarTests/Cs1OperatorIndexerEventTests.cs` and added the redundant `<Compile Include>`
  `<ItemGroup>` to `CSharpGrammarTests.csproj` (the latter removed — see Fixes made). The BOM on
  `Cs1OperatorIndexerEventTests.cs` is a cosmetic change consistent with the test-file convention; it was NOT
  made by this sub-point and is NOT staged. The pre-existing cosmetic `.csproj` changes (Json, DotParser,
  CppInteropGenerator, RegexTests, etc.) were NOT touched.
- Only T2.1.5 files are staged: `Cs1.grammar` (unchanged — no fix needed), `Cs1MemberIntegrationTests.cs`
  (new), `CSharpParserPlan-progressT2.1.5.md` (this file), `CSharpParserPlan-checklist.md` (T2.1.5 → `[✅]`).
  No commit made (the orchestrator commits).

## Files changed
- `Tests/CSharpGrammarTests/Cs1MemberIntegrationTests.cs` (new; 34 tests).
- `docs/CSharpParserPlan-progressT2.1.5.md` (this file).
- `docs/CSharpParserPlan-checklist.md` (T2.1.5 → `[✅]`).
- `Parsers/CSharp/CSharpGrammar/Cs1.grammar` — **UNCHANGED** (no grammar fix was needed; all nested-type and
  base-list cases parsed cleanly).
- `Tests/CSharpGrammarTests/CSharpGrammarTests.csproj` — restored to committed state (removed the build-added
  redundant `<Compile Include>` `<ItemGroup>`; NETSDK1022 fix, same as T2.1.3).
