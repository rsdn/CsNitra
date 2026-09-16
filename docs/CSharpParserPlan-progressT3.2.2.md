# T3.2.2 — C# 3.0 object/collection initializers + anonymous types

Status: done.

## Goal
Add C# 3.0 object initializers, collection initializers, and anonymous types to the EXISTING
`Parsers/CSharp/CSharpGrammar/Cs3.grammar`:
1. **Object initializers**: `new C { X = 5, Y = 6 }`, `new C(1, 2) { X = 5 }` (with args).
2. **Collection initializers**: `new List<int> { 1, 2, 3 }`, `new List<C> { new C { X = 1 }, ... }` (nested).
3. **Anonymous types**: `new { X = 5, Y = 6 }` (a `new` followed DIRECTLY by `{ ... }`, no type name).

CS3 only.

## Baseline (before changes)
- `dotnet build Nitra.sln --no-incremental` → success, 0 errors / 0 warnings.
- `dotnet test Tests/CSharpGrammarTests` (fresh build) → **888 total / 885 passed / 0 failed / 3 skipped** (per T3.2.1).
- `dotnet test Tests/ParserTests` (regression) → **327 total / 325 passed / 0 failed / 2 skipped** (per T3.2.1).

## The `new` expression structure (Cs1) — why `new C { ... }` rejects at Cs1
- `Primary` (Cs1:675) includes `NewExpr = "new" NewBody` (Cs1:709).
- `NewBody` (Cs1:735): `ArrayCreation = NewBaseType NewArrayRank ArrayInitializer?` |
  `ObjectCreation = NewBaseType ArgumentList`. It REQUIRES `[` (array rank) or `(` (argument list)
  after the `NewBaseType`. So `new C { ... }` (a `{` after the type) REJECTS at Cs1 — verified in T2.3.1.
- `NewBaseType` (Cs1:740): `PredefinedType | QualifiedName`. For `new List<int>`, `QualifiedName` =
  `TypeName` (merged Cs1+Cs2, greedily `List<int>` via the Cs2 `TypeArgumentList` alternative) +
  `NamespaceSegment*` (zero) → `NewBaseType` matches `List<int>`. So the collection-initializer type
  works without touching `NewBaseType`.

## `=` vs `==` disambiguation (the critical finding)
- A grammar `Literal` is a **StartsWith** match (`Rules.cs:76`): `Literal("=")` **WOULD** match the
  first `=` of `==`. So a bare `Identifier "=" Expression` would mis-read `x == 5` (consuming `x =`
  and leaving `= 5`, which is not an `Expression` → it backtracks, but the disambiguation must be
  explicit, not accidental).
- **Roslyn** disambiguates with `IsNamedAssignment` (LanguageParser.cs:13368):
  `IsTrueIdentifier() && PeekToken(1).Kind == EqualsToken`. Because Roslyn tokenizes `==` as ONE token
  (`EqualsEqualsToken`), `PeekToken(1)` after `x` in `x == 5` is `EqualsEqualsToken` (not `EqualsToken`)
  → NOT a named assignment → parsed as an expression.
- The **`!("==")` lookahead** replicates this exactly:
  - `X = 5` → `!("==")` passes (next is `= `), `"="` matches → **MemberAssignment**.
  - `x == 5` → `!("==")` fails (next is `==`) → MemberAssignment fails → **Expression**.
- **The equal-length tie**: for `X = 5` (a single `=`), BOTH `MemberAssignment` and `Expression` (via
  the `Assign` operator, Cs1:672) match at the SAME length. Empirically the engine's union tie-break for
  equal-length Success/Success is **FIRST-ALTERNATIVE-WINS** (`Parser.cs:299-316`: `postNewPos > maxPos`
  updates; on `==` only a Partial→Success upgrade updates, so the first Success is kept) — NOT a hard
  error (contrary to the AGENTS.md simplification). Confirmed by a temporary build with the bare
  `| Expression` alternative: `new C { X = 5, Y = 6 }` still parses (MemberAssignment, declared first,
  wins the tie). However, per the task's design principle (avoid equal-length ties; resolve with
  lookahead), the `Expression` alternative is **guarded** with `!(Identifier !("==") "=")` so the two
  alternatives are **MUTUALLY EXCLUSIVE** (never match at the same length) — robust, no reliance on the
  tie-break.

## Cs3.grammar rules (exact, appended after the T3.2.1 lambda rules)
```
NewBody =
    | ObjectCreationWithInitializer = NewBaseType ArgumentList? Initializer
    | AnonymousType = AnonymousInitializer;

Initializer = "{" (InitializerElement; ",")* "}";

InitializerElement =
    | MemberAssignment = Identifier !("==") "=" Expression
    | NotMemberAssignment = !(Identifier !("==") "=") Expression;

AnonymousInitializer = "{" (InitializerElement; ",")+ "}";
```
- `NewBody` is RE-DECLARED (append, the same pattern as the `Primary` re-declaration above and Cs2's
  `AnonymousMethod`). Each re-declared alternative REQUIRES the new construct (REQUIRED-new-construct):
  `ObjectCreationWithInitializer` REQUIRES the `Initializer`; `AnonymousType` REQUIRES a `{` directly
  after `new` (no type name).
- `Initializer` is a `SeparatedList` with the default (Forbidden) trailing-separator behavior, so a
  trailing comma (`new C { X = 5, }`) is rejected. Empty `{ }` is allowed (`*`).
- `AnonymousInitializer` uses `+` (at least one element) → `new { }` is rejected (see empty-init below).

## Mutual-exclusivity hand-traces (no equal-length tie)

### Object initializer vs object creation vs anonymous type (inside the merged `NewBody`)
- `new C { X = 5 }`: Cs1 `ArrayCreation` fails (no `[`); Cs1 `ObjectCreation` fails (no `(`);
  Cs3 `ObjectCreationWithInitializer` → `NewBaseType=C`, `ArgumentList?` empty, `Initializer={ X = 5 }`
  → matches `C { X = 5 }`; Cs3 `AnonymousType` fails (no `{` first). → **ObjectCreationWithInitializer**.
- `new C(1) { X = 5 }`: Cs1 `ObjectCreation` → `C(1)` (len 4); Cs3 `ObjectCreationWithInitializer` →
  `C(1) { X = 5 }` (len 14). Longest-match → **Cs3** (longer). No tie.
- `new C(1)` (no initializer): Cs1 `ObjectCreation` → `C(1)`; Cs3 `ObjectCreationWithInitializer` fails
  (no `{`). → **Cs1 only**. No tie.
- `new { X = 5 }`: every `NewBaseType`-based alternative fails (`{` first, not a type name);
  Cs3 `AnonymousType` → `{ X = 5 }`. → **AnonymousType**.

### MemberAssignment vs Expression (inside `InitializerElement`)
- `X = 5` (single `=`): `MemberAssignment` matches; `NotMemberAssignment` FAILS (the
  `!(Identifier !("==") "=")` guard: it IS a member-assignment start). → **MemberAssignment only**. No tie.
- `x == 5` (double `=`): `MemberAssignment` fails (`!("==")`); `NotMemberAssignment` matches (not a
  member-assignment start). → **Expression only**. No tie.
- `5` (no `=`): `MemberAssignment` fails (no identifier); `NotMemberAssignment` matches. →
  **Expression only**. No tie.

## Empty-initializer findings
- `new C { }` (empty object initializer): **VALID**. `Initializer = "{" (InitializerElement; ",")* "}"`
  allows zero elements (`*`). Roslyn `ParseObjectOrObjectCollectionInitializer` accepts an empty list.
- `new { }` (empty anonymous type): **NOT valid in CS3**. Roslyn's PARSER accepts the empty list
  (`requireOneElement: false`, LanguageParser.cs:13338) but the BINDER rejects it (CS0659 "Anonymous
  type must have at least one member"). We enforce that binder rule at the parse level:
  `AnonymousInitializer = "{" (InitializerElement; ",")+ "}"` requires at least one element (`+`).

## Roslyn references (C:\RSDN\roslyn, main, src/Compilers/CSharp/Portable/Parser/LanguageParser.cs)
- `ParseNewExpression` (13245): dispatches `IsAnonymousType` (13318, `new` + `{`) to
  `ParseAnonymousTypeExpression` (13323); otherwise `ParseArrayOrObjectCreationExpression` (13383).
- `ParseArrayOrObjectCreationExpression` (13383): optional argument list (13406) + optional
  object/collection initializer (13411-13414); if neither an argument list nor an initializer is present
  → `ERR_BadNewExpr` (CS1526, 13417-13423).
- `IsNamedAssignment` (13368): `IsTrueIdentifier() && PeekToken(1).Kind == EqualsToken` (the `=`/`==`
  signal).
- `ParseAnonymousTypeMemberInitializer` (13348):
  `AnonymousObjectMemberDeclarator(IsNamedAssignment() ? ParseNameEquals() : null, ParseExpressionCore())`.
- Syntax tests (src/Compilers/CSharp/Test/Syntax/Parsing/ExpressionParsingTests.cs): :1209
  (`new a() { }`), :1237 (`new a { }`), :1260 (`new a { b }`), :1284 (`new a { b, c, d }`), :1310
  (`new a { B = b }`), :1334 (`new a { B = { X = x } }`), :1932 (`new {a, b}`).

## Version-purity results
- `class C { int X; void M() { var c = new C { X = 5 }; } }` → **rejects at v2** / **parses at v3**.
- `class C { void M() { var l = new List<int> { 1, 2, 3 }; } }` → **rejects at v2** / **parses at v3**.
- `class C { void M() { var a = new { X = 5 }; } }` → **rejects at v2** / **parses at v3**.
- At v2 `NewBody` has only the Cs1 alternatives (`ArrayCreation`, `ObjectCreation`): `new C { ... }`
  fails (no `[`/`(` after the type) and `new { ... }` fails (no anonymous-type alternative). All
  pre-existing Cs1/Cs2/Cs6/Cs11 tests stay green (no Cs1/Cs2 modification).

## Tests
`Tests/CSharpGrammarTests/Cs3InitializerTests.cs` (CRLF + UTF-8 BOM) — **18 tests, all green**.
- POSITIVE (v3, 7): `ObjectInitializer_TwoMembers_Succeeds` (`new C { X = 5, Y = 6 }`),
  `ObjectInitializer_WithArgs_Succeeds` (`new C(1) { X = 5 }`), `ObjectInitializer_Empty_Succeeds`
  (`new C { }`), `CollectionInitializer_Ints_Succeeds` (`new List<int> { 1, 2, 3 }`),
  `CollectionInitializer_NestedObjectCreations_Succeeds` (`new List<C> { new C { X = 1 }, new C { X = 2 } }`),
  `CollectionInitializer_ComparisonElement_Succeeds` (`new List<int> { x == 5 ? 1 : 2 }` — a comparison,
  NOT a member assignment), `AnonymousType_TwoMembers_Succeeds` (`new { X = 5, Y = 6 }`).
- POSITIVE (v3, Roslyn-derived, 4): `Roslyn_NewWithEmptyInitializer_Succeeds` (`new C() { }`,
  ExpressionParsingTests.cs:1209), `Roslyn_NewWithNoArgumentsAndInitializers_Succeeds`
  (`new List<int> { 1, 2, 3 }`, :1284), `Roslyn_NewWithNoArgumentsAndAssignmentInitializer_Succeeds`
  (`new C { B = 5 }`, :1310), `Roslyn_AnonymousObjectCreation_Succeeds` (`new { X = 5, Y = 6 }`, :1932).
- VERSION-PURITY (v2, 3): `ObjectInitializer_RejectedAtV2`, `CollectionInitializer_RejectedAtV2`,
  `AnonymousType_RejectedAtV2`.
- NEGATIVE (v3, malformed, 4): `ObjectInitializer_MissingValue_Rejected` (`new C { X = }`),
  `AnonymousType_Empty_Rejected` (`new { }`), `ObjectInitializer_TrailingComma_Rejected`
  (`new C { X = 5, }`), `AnonymousType_Unclosed_Rejected` (`new { X = 5`).

## Verification
- [x] `dotnet build Nitra.sln --no-incremental` → **success, 0 errors / 0 warnings**.
- [x] `dotnet test Tests/CSharpGrammarTests` (fresh build) → **906 total / 903 passed / 0 failed /
  3 skipped**. Baseline before T3.2.2: 888 total / 885 passed / 3 skipped. Delta = **+18** (all new
  `Cs3InitializerTests`). Filtered run of `Cs3InitializerTests` → **18 passed / 0 failed**. All
  pre-existing Cs1 / Cs2 / Cs6 / Cs11 tests remain green.
- [x] `dotnet test Tests/ParserTests` (regression; engine + CsNitra meta-grammar unchanged) →
  **327 total / 325 passed / 0 failed / 2 skipped** — matches baseline.

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs3.grammar` (added the T3.2.2 rules: re-declared `NewBody`,
  `Initializer`, `InitializerElement`, `AnonymousInitializer`).
- `Tests/CSharpGrammarTests/Cs3InitializerTests.cs` (new, 18 tests).
- `Tests/CSharpGrammarTests/CSharpGrammarTests.csproj` (removed a pre-staged redundant `<Compile
  Include="Cs3InitializerTests.cs" />` that caused a `NETSDK1022` duplicate-`Compile`-item build error —
  the SDK auto-includes all `.cs` files; same fix as T3.2.1 for the lambda tests).
- `docs/CSharpParserPlan-checklist.md` (T3.2.2 `[~]` → `[✅]`).
- `docs/CSharpParserPlan-progressT3.2.2.md` (this file).

## Boundary decisions / deviations
- **`new a { B = { X = x } }` (nested initializer as a member-assignment RHS) is NOT supported**
  (ExpressionParsingTests.cs:1334). The member-assignment RHS is an `Expression`, and a bare
  `{ X = x }` is not an `Expression`. This is a more advanced nested-initializer form not required by
  the task; the required nested case — a collection initializer with object creations as elements
  (`new List<C> { new C { X = 1 }, new C { X = 2 } }`) — IS supported (each element is a `NewExpr`).
- **`new { }` (empty anonymous type) is rejected at the parse level** (enforcing the binder CS0659 rule),
  a deliberate deviation from Roslyn's parser, which accepts the empty member list syntactically.
- **Comma inside the initializer element list**: the TDOPP `Comma` operator (bp 1) is applicable at
  minPrecedence 0, so a multi-element initializer like `{ X = 5, Y = 6 }` is parsed as a single element
  whose RHS is a comma expression. This is the EXISTING grammar behavior for any top-level `Expression`
  in a list (e.g. `new Foo(1, 2)` already parses `1, 2` as one comma expression, T2.3.1); the whole
  input is still consumed, so the positive tests pass.
- **CS3 only**: no target-typed `new()` (CS9), no collection expressions `[1, 2, 3]` (CS12), no `with`
  elements (CS12). The Roslyn `ImplicitObjectCreationParsingTests` (`new(){}`, `new(1,2){x=y}`) are CS9
  target-typed forms and were deliberately NOT ported.
