# T3.1.3 — C# 2.0 `partial` (class/struct/method) + `sealed override`

Status: done.

## Goal
Add C# 2.0 `partial` (on class/struct and on partial methods) and `sealed override` to the existing
`Parsers/CSharp/CSharpGrammar/Cs2.grammar`. CS2 only (with the `sealed override` version finding
documented below).

## Cs2.grammar rules (T3.1.3 section)

### `partial` — version-purity via modifier re-declaration (NOT reservation)
`partial` is a **contextual keyword** in Roslyn (an `IdentifierToken` whose `ContextualKind` is
`PartialKeyword`; `LanguageParser.cs:1324-1328` maps it to `DeclarationModifiers.Partial` in the
generic `ParseModifiers` loop). It is therefore **NOT** added to `ReservedKeyword` — this is the
T1.3.1 decision (progressT1.3.1.md:70-75): modern Roslyn treats it as contextual, mcs's ISO_1 mode
demotes it to an identifier, and it is merge-friendly (append-only merging cannot remove an
alternative from `ReservedKeyword`).

Instead the relevant Cs1 modifier unions are re-declared in Cs2 to **APPEND** `"partial"` (T0.3 merge
semantics — re-declaring a rule name adds alternatives):

```
ClassModifier  = | "partial";
StructModifier = | "partial";
MethodModifier = | "partial";
```

The Cs1 `ClassDeclaration` / `StructDeclaration` / `Method` rules use these merged modifier unions,
so **no re-declaration of the declarations is needed** — at v2 the merged modifiers accept `partial`
and the Cs1 declaration/method alternatives match. `"partial"` is a `WordLiteral`
(RuleGenerator.cs:77-79: a valid-identifier literal becomes `EP.WordLiteral`), so it matches
whole-word (`partial`, not `partialX`).

`partial` on interface (CS8) / enum is out of scope for this sub-point (the task scopes `partial` to
class/struct/method).

### `sealed override` — CS1 feature, already covered by Cs1 (no Cs2 change)
`sealed` is a **hard keyword** in Roslyn (`SyntaxKindFacts.cs:974-975` maps `"sealed"` to
`SealedKeyword` directly; there is no contextual path for it). It is **already** a Cs1 `MethodModifier`
(Cs1.grammar:299). There is **no `CSharpVersion` gate** for sealing an override method in Roslyn — it
is a C# 1.0 feature. So `sealed override void M() { }` **already parses at v1** (and v2) via the Cs1
`Method` alternative. No Cs2 change is required; this is documented as a CS1 feature already covered
by Cs1 (no gap).

## Partial methods — declaration vs implementation mutual exclusivity
A partial method has two forms:
- **Declaration**: `partial void M(int x);` — return type `void`, NO body, ends with `;`.
- **Implementation**: `partial void M(int x) { ... }` — WITH a body.

The Cs1 `Method` rule (Cs1.grammar:265) is `Attributes? MethodModifier* Type TypeName "(" ParameterList?
")" MethodBody;` and its `MethodBody` (Cs1.grammar:267) is the named union `| Block | ";"`. So once
`partial` is a `MethodModifier`, **both** forms parse via the Cs1 `Method` alternative — the `;` (declaration)
and `{` (implementation) bodies disambiguate *inside* `MethodBody` (different leading tokens, so no
equal-length tie). No re-declaration of `Method` is needed.

Hand-traces (v2, Cs1 `Method` alternative; `MethodModifier` is the merged union with `partial`):
- `partial void M();` (declaration): `MethodModifier*`=`partial`, `Type=void`, `TypeName=M`, `()`,
  `MethodBody`;` → **match** (semicolon body). The Cs2 re-declared `Method` (T3.1.1.2, requires
  `ConstraintClause+`) fails at `;`. → Cs1 `Method` only, no tie.
- `partial void M() { }` (implementation): `MethodModifier*`=`partial`, `Type=void`, `TypeName=M`, `()`,
  `MethodBody={ }` → **match** (block body). → Cs1 `Method` only, no tie.
- `void M() { }` (normal, no partial): `MethodModifier*`=empty, `Type=void`, `TypeName=M`, `()`,
  `MethodBody={ }` → **match**. → no tie.

The declaration (`;`) and implementation (`{`) are different inputs and, within `MethodBody`, start with
different tokens, so they never tie.

**Void-only return type (CS2)**: a CS2 partial method must return `void`. This is a **binder**
restriction (Roslyn CS0759 / CS8023), **not** a parse restriction. The parser accepts `partial int M();`
syntactically (it is a valid `Method` declaration: `Type=int`, `;` body); the binder rejects it for CS2.
This is the parser-vs-binder split used throughout the project.

## Version-purity results
- `partial class C { }` → **rejects at v1** (partial not a class modifier; cannot start a declaration)
  / **parses at v2** (merged ClassModifier). ✓
- `partial struct S { }` → **rejects at v1** / **parses at v2** (merged StructModifier). ✓
- `class C { partial void M(); }` (declaration) → **rejects at v1** / **parses at v2** (merged
  MethodModifier). ✓
- `class C { partial void M() { } }` (implementation) → **rejects at v1** / **parses at v2**. ✓
- `sealed override void M() { }` → **parses at v1** and **v2** (sealed is a Cs1 MethodModifier; CS1
  feature, no version gate). ✓ (no Cs1 regression)

## Tests
`Tests/CSharpGrammarTests/Cs2PartialSealedTests.cs` (CRLF + UTF-8 BOM) — **20 tests, all green**.
- POSITIVE (v2, partial, 5): `PartialClass_Succeeds` (`partial class C { }`), `PartialStruct_Succeeds`
  (`partial struct S { }`), `PartialMethodDeclaration_Succeeds` (`class C { partial void M(); }`),
  `PartialMethodImplementation_Succeeds` (`class C { partial void M() { } }`),
  `PartialMethodDeclaration_WithParams_Succeeds` (`class C { partial void M(int x, string y); }`).
- POSITIVE (v2, sealed override / combined, 2): `SealedOverride_Succeeds`
  (`class Base { virtual void M() { } } class C : Base { sealed override void M() { } }`),
  `SealedPartialClass_Succeeds` (`sealed partial class C { }` — both are ClassModifiers, any order; valid).
- POSITIVE (v2, Roslyn-derived, 2): `PartialMethodDeclaration_Roslyn_Succeeds`
  (`partial class P { partial void M(); }`, RoundTrippingTests.cs:1579 PartialMethodWithLanguageVersion2),
  `NonVoidPartialMethod_Parses_Roslyn` (`class C { partial int M(); }`, ParserErrorMessageTests.cs:5502
  PartialMethodsVersionThree — the CS2 void-only restriction is a **binder** CS8023, a parse-level success).
- POSITIVE (v2, binder-concern forms the parser accepts, 3): `SealedWithoutOverride_Parses`
  (`class C { sealed void M() { } }` — binder CS0106), `OverrideSealedOrder_Parses`
  (`class Base { virtual void M() { } } class C : Base { override sealed void M() { } }` — Roslyn's
  ParseModifiers is a generic any-order loop; ordering is a binder concern),
  `PartialMethodDeclaration_TriviaBeforeSemicolon_Parses` (`class C { partial void M() ; }` — whitespace
  before `;` is trivia, skipped after every terminal, so the "wrong spacing" form parses identically).
- POSITIVE (v1, sealed override is a C# 1.0 feature, 1): `SealedOverride_ParsesAtV1`
  (`class Base { virtual void M() { } } class C : Base { sealed override void M() { } }` parses at v1 —
  `sealed` is already a Cs1 MethodModifier; documents the CS1 version finding).
- NEGATIVE (v1, version-purity, 4): `PartialClass_RejectedAtV1` (`partial class C { }`),
  `PartialStruct_RejectedAtV1` (`partial struct S { }`), `PartialMethodDeclaration_RejectedAtV1`
  (`class C { partial void M(); }`), `PartialMethodImplementation_RejectedAtV1`
  (`class C { partial void M() { } }`).
- NEGATIVE (v2, malformed, genuinely reject, 3): `PartialClass_MissingBody_Rejected`
  (`partial class C`), `PartialMethod_MissingParameterList_Rejected` (`partial void M`),
  `PartialMethod_MissingBody_Rejected` (`class C { partial void M() }`).

Note on the task's "malformed" list: `partial void M() ;` (spacing), `partial int M();` (non-void),
`sealed void M() { }` (sealed w/o override), and `override sealed void M() { }` (order) all **parse**
at the syntax level — each is a binder concern, not a parse error (parser-vs-binder split). They are
therefore covered as POSITIVE tests (the three binder-concern forms above + the Roslyn non-void case),
not as negatives. The genuinely-malformed negatives are the three that reject at the parse level.

## Roslyn references (C:\RSDN\roslyn, main)
- `ParseModifiers` (LanguageParser.cs:1347) → `GetModifierExcludingScoped` (1286-1344):
  `SyntaxKind.PartialKeyword` → `DeclarationModifiers.Partial` (1318-1319 hard path; 1324-1328
  contextual path via `IdentifierToken` + `contextualKind == PartialKeyword`).
- `IsCurrentTokenDefinitelyPartialModifier` (LanguageParser.cs:1637) — gates `partial` as a modifier on
  a following type/member-declaration start (contextual disambiguation; `partial` may otherwise be a
  type/member name, see comment at 13846-13848).
- Syntax tests: `RoundTrippingTests.cs:1579` `PartialMethodWithLanguageVersion2`
  (`partial class P { partial void M(); }` parses cleanly at `LanguageVersion.CSharp2`);
  `ParserErrorMessageTests.cs:5502` `PartialMethodsVersionThree` (`partial int Goo() { }` — the
  non-void form; at CSharp2 the void-only restriction is a **binder** CS8023, a parse-level success).
- `sealed`: `SyntaxKindFacts.cs:974-975` (`"sealed"` → `SealedKeyword`, hard keyword); no `CSharpVersion`
  gate for sealing an override method (CS1 feature).

## Verification
- [x] `dotnet build Nitra.sln --no-incremental` → **success, 0 errors / 0 warnings**.
- [x] `dotnet test Tests/CSharpGrammarTests` (fresh build) → **864 passed / 0 failed / 3 skipped** (total 867).
  - Baseline before T3.1.3: 844 passed / 3 skipped (total 847), per T3.1.2. Delta = **+20** (all new
    `Cs2PartialSealedTests`).
  - Filtered run of `Cs2PartialSealedTests` → **20 passed / 0 failed**.
  - All pre-existing Cs1 / Cs2 (T3.1.1.1 + T3.1.1.2 + T3.1.2) / Cs6 / Cs11 tests remain green.
- [x] `dotnet test Tests/ParserTests` (regression; engine + CsNitra meta-grammar unchanged) →
  **325 passed / 0 failed / 2 skipped** (total 327) — matches baseline.

## Boundary decisions / deviations
- **`partial` version-purity via modifier re-declaration, NOT reservation**: `partial` is a contextual
  keyword (Roslyn `IdentifierToken` + `ContextualKind == PartialKeyword`; LanguageParser.cs:1324-1328),
  so it is NOT added to `ReservedKeyword` (the T1.3.1 decision, progressT1.3.1.md:70-75). It is APPENDED
  to the Cs1 `ClassModifier` / `StructModifier` / `MethodModifier` unions in Cs2 (T0.3 merge). The Cs1
  `ClassDeclaration` / `StructDeclaration` / `Method` use those merged unions, so no re-declaration of
  the declarations is needed. Consequence: at v1 `partial` is a plain identifier (not reserved, not a
  modifier) so the partial forms REJECT; at v2 the merged modifiers accept `partial` so they PARSE.
- **No re-declaration of `Method` for partial methods**: the Cs1 `Method` (Cs1:265) already allows both
  a `;` body and a `{ ... }` body via `MethodBody = | Block | ";"` (Cs1:267). So once `partial` is a
  `MethodModifier`, both the declaration (`;`) and implementation (`{ }`) parse via the Cs1 `Method`
  alternative; the `;`/`{` bodies disambiguate inside `MethodBody` (different leading tokens, no tie).
- **CS2 void-only partial-method return type is a binder concern**: the parser accepts `partial int M();`
  syntactically (a valid `Method` declaration); the void-only restriction (Roslyn CS0759/CS8023) is a
  binder diagnostic. Parser-vs-binder split, consistent throughout.
- **`sealed override` is a C# 1.0 feature already covered by Cs1 (no Cs2 change, no gap)**: `sealed` is a
  hard keyword (SyntaxKindFacts.cs:974-975) and is already a Cs1 `MethodModifier` (Cs1:299). There is no
  `CSharpVersion` gate for sealing an override method in Roslyn. So `sealed override void M() { }` already
  parses at v1 (and v2). The plan's placement of `sealed override` under T3.1.3 (CS2) is therefore a
  documentation-only item: no grammar change was required, and no Cs1 test is affected.
- **The task's "malformed" v2 cases all parse** (binder concerns, not parse errors): `partial void M() ;`
  (whitespace is trivia), `partial int M();` (non-void, binder CS0759/CS8023), `sealed void M() { }`
  (sealed w/o override, binder CS0106), `override sealed void M() { }` (ordering, Roslyn ParseModifiers is
  any-order). Covered as POSITIVE tests; the genuinely-malformed negatives are the three that reject at the
  parse level (missing body / parameter list).
- **csproj working-tree revert (setup fix)**: the working tree contained an uncommitted modification to
  `Tests/CSharpGrammarTests/CSharpGrammarTests.csproj` that added an explicit `<Compile Include>` ItemGroup
  (for the Cs2 test files, incl. this sub-point's new file). That conflicts with the SDK default compile
  globbing and produced `NETSDK1022: Duplicate 'Compile' items` when building the test project directly.
  It was reverted to the committed (HEAD) form, which relies on the default globbing and already picks up
  the new test file. No committed content changed; the revert restores the pre-existing correct state.

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs2.grammar` (added T3.1.3 section).
- `Tests/CSharpGrammarTests/Cs2PartialSealedTests.cs` (new, 20 tests).
- `docs/CSharpParserPlan-checklist.md` (T3.1.3 `[~]` → `[✅]`).
- `docs/CSharpParserPlan-progressT3.1.3.md` (this file).
- (working-tree fix, no committed change) `Tests/CSharpGrammarTests/CSharpGrammarTests.csproj` reverted to
  the HEAD form (removed the uncommitted duplicate-`Compile` ItemGroup).
