# T2.1.4 — C# 1.0 operators, indexers, events — Progress

## Status: done (build 0 errors; CSharpGrammarTests 719 passed / 0 failed / 3 pre-existing skips; ParserTests 325 / 0 / 2)

## Task
Add **operators** (user-defined + conversion), **indexers**, and **events** to the C# 1.0 grammar in
`Parsers/CSharp/CSharpGrammar/Cs1.grammar`. FOURTH of five member sub-points
(T2.1.1 infra+Field → T2.1.2 Property → T2.1.3 Method/Constructor/Destructor → **T2.1.4
Operator/Indexer/Event** → T2.1.5 finalize).

In scope:
1. User-defined operator (class/struct): `[ret-type] operator <symbol> ( ParameterList ) ( Block | ";" )`.
2. Conversion operator (class/struct): `(explicit | implicit) operator Type ( ParameterList ) ( Block | ";" )`.
3. Indexer (class/struct block form + interface semicolon form): `Type "this" "[" ParameterList "]" { accessors }`.
4. Event (class/struct: simple `;` or `{ add { } remove { } }`; interface: simple `;` only).
5. Modifier sets: `OperatorModifier` / `IndexerModifier` / `EventModifier`.

NOT in scope: base-list refinements, full version-purity sweep, final verification (T2.1.5).

## Roslyn semantics ported (C:\RSDN\roslyn, src/Compilers\CSharp/Portable/Parser/LanguageParser.cs)
- **Dispatch** (`ParseMemberDeclarationCore` 3238): attrs + `ParseModifiers` (generic loop) → then:
  - `event` keyword → `ParseEventDeclaration` (3285-3288).
  - `TryParseConversionOperatorDeclaration` (3297) — for `operator (implicit|explicit) Type ...`.
  - else parse a return `Type` (`ParseReturnType` 3314), then `IsOperatorStart` (3335) →
    `ParseOperatorDeclaration` (3337) for user-defined operators.
- **`ParseOperatorDeclaration`** (3992-4190): `[ret-type]` (passed in) + `operator` keyword (4027) +
  operator token (4033-4072) + `ParseParenthesizedParameterList` (4117) +
  `ParseBlockAndExpressionBodiesWithSemicolon` (4166 → block | `;`). The ret-type comes BEFORE
  `operator`.
- **`TryParseConversionOperatorDeclaration`** (3760-3977): `(implicit|explicit)` keyword (3837) +
  `operator` keyword (3873) + `ParseType` (3880, the TARGET type) + param list + body. Order confirmed
  by Roslyn tests (`implicit operator`, `explicit operator` — e.g.
  Test/Semantic/Semantics/BetterCandidates.cs:885 `public static implicit operator C(A a) => null;`).
- **`ParseIndexerDeclaration`** (4192-4250): `[ret-type]` + `this` keyword +
  `ParseBracketedParameterList` (4209, `[` params `]`) + `ParseAccessorList` (4223). A generic
  indexer (`this<T>`) is rejected (4203-4207, binder).
- **`ParseEventDeclaration`** (5082-5095): `event` + `ParseType` → `IsFieldDeclaration(isEvent:true)`
  → `ParseEventFieldDeclaration` (5222, `event Type Name;`) OR `ParseEventDeclarationWithAccessors`
  (5097, `event Type Name { add { } remove { } }`).
- **`ParseAccessorDeclaration`** (4632-4729): accessor name (add/remove for events, get/set for
  properties) + ( block | `;` ). Event accessors use add/remove; the `value` identifier is usable in
  the bodies (T2.1.2 finding: `value` is not reserved → a true identifier).
- **Operator token set** (SyntaxKindFacts.cs:487-558): `IsAnyOverloadableOperator` =
  `IsOverloadableBinaryOperator` (494: `+ - * / % ^ & | == < <= << > >= >> >>> !=`) ∪
  `IsOverloadableUnaryOperator` (521: `+ - ~ ! ++ -- true false`) ∪
  `IsOverloadableCompoundAssignmentOperator` (539: `+= -= *= /= %= &= |= ^= <<= >>= >>>=`).

## Operator-symbol set decision (boundary)
The task lists 16 symbols: `+ - * / % & | ^ ~ < > == != ++ --`. Roslyn's full overloadable set is
LARGER (adds `<= >= << >> >>> ! true false` and the compound-assignment operators `+= -= *= /= %= &= |= ^=
<<= >>= >>>=`). However:
- The **standalone** compound-assignment operators (`operator +=(a,b)` as a first-class operator, and the
  zero-arg `operator++()`) are a **C# 14** feature per `IDS_FeatureUserDefinedCompoundAssignmentOperators`
  (Test/Emit3/Symbols/UserDefinedCompoundAssignmentOperatorsTests.cs:46-51: "not available in C# 13.0, use
  14.0"). So the compound-assignment symbols are NOT C# 1.0 in the standalone form.
- To keep C# 1.0 purity and follow the task's explicit list, the grammar implements exactly the task's
  16 symbols. The additional Roslyn symbols are documented here and deferred (see Boundary decision D1).

## Rules added (exact, `Parsers/CSharp/CSharpGrammar/Cs1.grammar`)
- `Operator = Attributes? OperatorModifier* Type "operator" OperatorSymbol "(" ParameterList ")" MethodBody;`
- `ConversionOperator = Attributes? OperatorModifier* ConversionKind "operator" Type "(" ParameterList ")" MethodBody;`
  - NOTE: implicit/explicit comes BEFORE `operator` (the task's form text `operator (explicit|implicit) Type`
    was wrong; its example `public static implicit operator int(A a)` is correct). Roslyn
    TryParseConversionOperatorDeclaration eats implicit/explicit (3837) then `operator` (3873).
- `ConversionKind = | "explicit" | "implicit";`
- `OperatorSymbol = | "+" | "-" | "*" | "/" | "%" | "&" | "|" | "^" | "~" | "<" | ">" | "==" | "!=" | "++" | "--";`
- `OperatorModifier = public|private|protected|internal|static|extern|unsafe;`
- `Indexer = Attributes? IndexerModifier* Type "this" "[" ParameterList "]" "{" AccessorDeclaration+ "}";`
- `InterfaceIndexer = Attributes? IndexerModifier* Type "this" "[" ParameterList "]" "{" InterfaceAccessor+ "}";`
- `IndexerModifier = public|private|protected|internal|static|virtual|override|new|extern|sealed;`
- `Event = Attributes? EventModifier* "event" Type TypeName EventTail;`
- `EventTail = | ";" | EventAccessors = "{" EventAccessor+ "}";`
  - NOTE: the accessor-list alternative is a SEQUENCE, so it must be a NAMED alternative
    (`| EventAccessors = ...`). In the CsNitra meta-grammar an anonymous alternative (`| Element`)
    only supports a single element (a QualifiedIdentifier or a Literal), not a sequence
    (CsNitraParser.cs:64-71 `Alternative` rule). The first draft `| "{" EventAccessor+ "}"` failed
    the grammar build with `(411,24): Expected: "="`.
- `EventAccessor = EventAccessorName Block;`
- `EventAccessorName = | "add" | "remove";`
- `InterfaceEvent = Attributes? EventModifier* "event" Type TypeName ";";`
- `EventModifier = public|private|protected|internal|static|abstract|new|virtual|override|sealed;`

Unions updated:
- `ClassMember = | Field | Property | AbstractProperty | Method | Constructor | Destructor | Operator | ConversionOperator | Indexer | Event | TypeDeclaration;`
- `StructMember = | Field | Property | AbstractProperty | Method | Constructor | Destructor | Operator | ConversionOperator | Indexer | Event | TypeDeclaration;`
- `InterfaceMember = | InterfaceMethod | InterfaceProperty | InterfaceIndexer | InterfaceEvent | TypeDeclaration;`

## Disambiguation (longest-match, no ties)
- Operator vs Method: after `Type`, `operator` keyword → Operator; a `TypeName` → Method.
- Operator vs ConversionOperator: `Type operator ...` (ret-type first, e.g. `bool operator ==`) vs
  `(implicit|explicit) operator ...` (implicit/explicit first, e.g. `implicit operator int`).
  Mutually exclusive: a ret-Type can never be `implicit`/`explicit` (reserved), and `implicit`/`explicit`
  can never be a ret-Type.
- Indexer vs Property: after `Type`, `this` keyword → Indexer; a `TypeName` → Property.
- Event vs everything: starts with the `event` reserved keyword (no other member does).
- EventTail: `;` vs `{` first token.

## Parser-vs-binder decisions
- **Missing `static` on an operator is a BINDER concern, not a parse concern.** Roslyn's `ParseModifiers`
  is a generic loop; the `static`-required check is in the binder (`SourceUserDefinedOperatorSymbol`).
  So `class C { bool operator ==(C a, C b) { ... } }` (valid ret-type, missing static) is a POSITIVE parse.
  `OperatorModifier*` is zero-or-more (no required `static`). The task's `operator + (C a) { }` example is
  missing the ret-type (not just static) -> it is a NEGATIVE parse (see D3).
- **`event C Changed;` parses for any `Type`** (the delegate-ness is a binder concern, CS0039). POSITIVE parse.

## Boundary decisions / deviations
- **D1: OperatorSymbol = the task's 16 symbols** (`+ - * / % & | ^ ~ < > == != ++ --`). Roslyn's full
  overloadable set (SyntaxKindFacts.cs:487-558) is larger (adds `<= >= << >> >>> ! true false` and the
  compound-assignment `+= -= *= /= %= &= |= ^= <<= >>= >>>=`), but the standalone compound-assignment
  operators are a **C# 14** feature (UserDefinedCompoundAssignmentOperatorsTests.cs:46-51). Following the
  task's explicit C# 1.0-safe list; the extra symbols are deferred (version-purity).
- **D2: No `AbstractIndexer` rule.** `IndexerModifier` excludes `abstract` (like `PropertyModifier` in
  T2.1.2). An abstract indexer (`abstract int this[int i] { get; set; }`, semicolon accessors) is not
  handled — same boundary as the T2.1.2 `extern`-property-with-semicolon-accessors gap. Not in the task's
  required test set.
- **D3: The task's `operator + (C a) { }` negative is missing the ret-type, not (only) `static`.** A
  user-defined operator REQUIRES a ret-type before `operator` (Roslyn ParseReturnType 3314), so
  `operator + (C a) { }` (no ret-type) is a NEGATIVE parse. The pure "missing static" case (with a valid
  ret-type) is a POSITIVE parse (binder concern) — covered by `Operator_MissingStatic_Parses`.
- **D4: The task's generic-operator negative `operator <(T a, T b)` is actually a valid `<` operator**
  (`<` is an OperatorSymbol; `(T a, T b)` are two params of type `T`). So it PARSES. The corrected
  generic-operator form `operator <T>(T a, T b)` (a type-parameter list `<T>` after the symbol) is the
  NEGATIVE test: after `operator <` (the OperatorSymbol) the grammar expects `(` but sees `T` -> rejects.
- **D5: `IndexerModifier`/`EventModifier` reuse the loose-parser pattern** (T2.1.1 D2): the modifier sets
  include all 4 access modifiers even where the binder rejects some (e.g. `private` on an operator);
  per-kind legality is a binder concern. `EventModifier` includes `abstract` (abstract events are valid
  C# 1.0 in abstract classes) but NOT `extern`/`unsafe` (not event modifiers).

## Tests
All in `Tests/CSharpGrammarTests/Cs1OperatorIndexerEventTests.cs` (41 new tests, parsed via
`Cs1RoslynTestHelper` from the `"Grammar"` start rule), all green (verified via filtered run:
`Passed: 41, Failed: 0`).

- **POSITIVE: 34**
  - User-defined operators, each of the 16 symbols (15): `+ - * / % & | ^ ~ < > == != ++ --`
    (binary + unary forms; `~`/`++`/`--` unary, the rest binary).
  - Operator body/context (3): semicolon body (`extern ... operator +(a,b);`), struct, missing-`static`
    (positive parse, binder concern).
  - Conversion operators (3): `implicit operator int(C c)`, `explicit operator C(int i)`, struct.
  - Indexers (5): class block accessors, interface semicolon accessors, multi-param, `value` in setter,
    struct.
  - Events (6): simple `;`, `{ add { } remove { } }`, interface simple, `value` in add/remove bodies,
    struct, non-delegate type (positive parse, binder concern).
  - Roslyn-derived + disambiguation (2): `explicit operator A(int i)` (Roslyn conversion operator),
    operator-vs-method mixed in one body.
- **NEGATIVE: 7**
  - Operator missing ret-type: `class C { operator + (C a) { } }` (D3).
  - Indexer missing `[params]`: `class C { public int this { get; } }`.
  - Generic operator (CS2): `class C { public static bool operator <T>(T a, T b) { ... } }` (D4).
  - Interface indexer with a body: `interface I { int this[int i] { get { return 0; } } }`.
  - Interface event with accessors: `interface I { event EventHandler Changed { add { } remove { } } }`.
  - Event accessor with wrong name: `class C { event EventHandler Changed { get { } } }` (add/remove only).
  - Conversion operator missing target type: `class C { public static implicit operator (C c) { ... } }`.

## Verification
- `dotnet build Nitra.sln --no-incremental` → **Build succeeded, 0 Warning(s), 0 Error(s)**.
- `dotnet test Tests/CSharpGrammarTests` (fresh build) → **Passed: 719, Failed: 0, Skipped: 3**
  (3 skips are pre-existing `RawString`/`StringLiteral` tests). New T2.1.4 tests: 41
  (`Cs1OperatorIndexerEventTests`), all green (verified via filtered run: `Passed: 41, Failed: 0`).
- `dotnet test Tests/ParserTests` → **Passed: 325, Failed: 0, Skipped: 2** (sanity; engine + CsNitra
  meta-grammar NOT changed — only the C# grammar text + tests + docs).

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs1.grammar` (unions + Operator/ConversionOperator/ConversionKind/
  OperatorSymbol/OperatorModifier + Indexer/InterfaceIndexer/IndexerModifier + Event/EventTail/
  EventAccessor/EventAccessorName/InterfaceEvent/EventModifier).
- `Tests/CSharpGrammarTests/Cs1OperatorIndexerEventTests.cs` (new; 41 tests).
- `docs/CSharpParserPlan-progressT2.1.4.md` (this file).
