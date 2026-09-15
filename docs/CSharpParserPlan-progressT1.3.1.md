# T1.3.1 — C# 1.0 Grammar (compilation unit, usings/externs, namespaces, type headers) — Progress

## Status: done (build + all test suites green; see Verification log)

## Task
Replace the placeholder `Parsers/CSharp/CSharpGrammar/Cs1.grammar` with the real C# 1.0 grammar:
compilation unit, `using`/`extern alias` directives, block-form namespaces, and namespace-member
type declarations (headers without bodies; class/struct/interface bodies empty — members come in T2.1;
enum members ARE part of the declaration).

## Key engine facts established (verified in code)

- `Parser.Parse` succeeds only on full input consumption (`ExtensibleParser/Parser.cs:205`); no EOF
  terminal needed.
- **`ParseTerminal` skips trailing trivia after EVERY terminal match** (`Parser.cs:686-689`). This has
  a big consequence: a predicate placed after a literal (`"kw" !Something`) evaluates `!Something` at
  the start of the NEXT token, not at the character right after the keyword. See "keyword design" below.
- Longest-match across alternatives; equal-length ties are an error. At a given position at most ONE
  whole-word keyword can match (a keyword cannot be a whole-word prefix of a longer keyword), so
  keyword rules can never tie.
- `!` predicate (`ParseNotPredicate`, `Parser.cs:520-527`): `Speculative(() => ParseAlternative(...))`
  — succeeds iff the predicate rule FAILS. `Speculative` (recovery build, `Parser.Recovery.cs:360-377`)
  suppresses side effects (snapshots/ErrorPos/_expected) and restores them afterwards — predicates are
  cleanly isolated. The predicate rule is parsed as a normal rule: for `Ref("ReservedKeyword")` all
  alternatives are tried and ANY success fails the `!`.
- SeparatedList `(Elem; Sep)*` with NO modifier = `SeparatorEndBehavior.Forbidden` (trailing separator
  → whole list FAILS); `: ?` = Optional (trailing separator is CONSUMED, list ends after it); `: !` =
  Required (`RuleGenerator.cs:105-117`, `Parser.cs:797-934`). Count `+` = `CanBeEmpty=false`.
- ZeroOrMany/OneOrMany have epsilon guards (zero-progress stops the loop) — every loop element must
  consume ≥1 char.
- Meta-grammar (self-grammar) has NO inline choice (`|` only at rule level) — the interleaved
  namespace-body loop must be a named rule with anonymous rule-ref alternatives.
- Meta-grammar precedence (`CsNitraTypeChecker.cs:56`): first name in `precedence` list binds tightest:
  Primary=6, Postfix=5, Predicate=4, Naming=3, Optional=2, Sequence=1. Hence `!A B` =
  `Seq(NotPredicate(A), B)` (the `!` operand is parsed at Predicate level, so the following `B` is NOT
  swallowed into the predicate); `X=Y?` = `Optional(Named(X, Y))`; multiple captures in one sequence
  work (`A=B C=D` → `Seq(Named(A,B), Named(C,D))` — same pattern the self-grammar uses).
- Recovery is ON by default (`Directory.Build.props:6-12`, `EnableRecovery` defaults to true) —
  `Parser.RecoveryDiagnostics` is populated by the recovery loop (`Parser.Recovery.cs:261-357`).
- Terminals are resolved in grammar text by `terminal.Kind` (`TypeCheckingContext.cs:22-27`) —
  hand-written terminals with a matching Kind string are referenceable from grammar text (same pattern
  as `Trivia`).
- The DFA engine's `\w` = `char.IsLetterOrDigit(c) || c == '_'` (Unicode-aware,
  `Regex/Regex/RegexAst.cs:68`) — whole-word keyword checks must use exactly this class.

## Keyword design (the main design decision)

**Problem.** Keyword literals in this parser are PREFIX-matching (`Literal.TryMatch` = `StartsWith`),
and `ParseTerminal` skips trailing trivia after a match. Two naive designs both fail:

1. **Bare-literal keywords** (`"class"`, `"int"`, ...):
   - FALSE POSITIVE: `classX { }` parses as `class X` (literal `"class"` matches the prefix of
     `classX`, then the name is `X`).
   - FALSE NEGATIVE: `class intX { }` (a perfectly valid C# 1.0 class name) is rejected, because the
     `ReservedKeyword` set (bare literals) matches the `int` prefix inside `intX` and
     `TypeName = !ReservedKeyword Identifier` fails.
2. **`"kw" !IdentifierPart` whole-word literal rules**: BROKEN by the trailing-trivia skip — the
   predicate sees the first char of the NEXT token, so `public sealed class C` fails (after `"public"`
   + skipped space, `!IdentifierPart` sees `s` of `sealed` and rejects the `public` keyword).

**Solution: hand-written whole-word keyword terminals.** `CSharpTerminals` gets a private
`KeywordTerminal` record + 82 cached factory methods (`KwAbstract()` ... `KwAlias()`).
`KeywordTerminal.TryMatch` matches the word AND rejects the match when the very next character
(no trivia skip — the check happens inside `TryMatch`) is an identifier-part char
(`char.IsLetterOrDigit(c) || c == '_'`, exactly the DFA's `\w`). This gives Roslyn-lexer semantics:
`classX` is one identifier token, the keyword `class` never matches it; `class X` matches `class`.

- 81 reserved C# 1.0 keywords (Roslyn `SyntaxKindFacts.GetKeywordKind`, `SyntaxKindFacts.cs:880-1048`)
  + `alias` (contextual, used only by `extern alias`).
- `partial` is NOT in the reserved set: modern Roslyn treats it as contextual, and mcs's ISO_1 (C# 1.0)
  mode also demotes it to identifier unless followed by class/struct/interface/void
  (`mono/mcs/cs-tokenizer.cs`, `GetKeyword` `Token.PARTIAL` case). Keeping it out of `ReservedKeyword`
  is also merge-friendly: Cs2 needs `partial` as a modifier, and append-only merging cannot remove an
  alternative from `ReservedKeyword`. (The original C# 1.0 spec's treatment of `partial` could not be
  verified from a reachable spec copy; compiler behavior + merge-friendliness decided it.)
- Consequences: `TypeName = !ReservedKeyword Identifier` now has NO prefix-matching inaccuracy —
  `intX`/`className` are valid names, `int`/`string` are rejected as names, and `classX { }` is
  rejected (no more false positives).

## C# 1.0 boundary decisions

- **`static` is NOT a C# 1.0 class modifier.** Roslyn gates `static class` as a C# 2 feature
  (`Errors/MessageID.cs:723-737` — `IDS_FeatureStaticClasses` → `LanguageVersion.CSharp2`,
  `Declarations/DeclarationTreeBuilder.cs:794-797`), and the task's `RoslynGrammarMap.md` feature
  table lists `StaticClasses` under C# 2. `ClassModifier` in Cs1 = access + `abstract` + `sealed`.
  NOTE: my prior memory was that static classes were C# 1.0 (ECMA-334 3rd ed. §12.2.1); the spec copy
  was not reachable for verification. Followed Roslyn (the task's designated reference). If the spec is
  found to include it, T1.4 just appends `| Static = KwStatic` to `ClassModifier`.
- Modifier sets per kind (C# 1.0 spec forms; Roslyn's parser accepts the union and the binder
  enforces combos — the grammar mirrors the spec's per-kind sets):
  - class: access, `abstract`, `sealed`
  - struct: access, `new`
  - interface: access, `abstract`
  - enum: access
  - delegate: access
- **Enum trailing comma**: allowed in C# 1.0 (spec `enum-member-list: enum-member , enum-member-list ,?`),
  Roslyn allows `;` too (`ParseEnumDeclaration`, `allowSemicolonAsSeparator: true`,
  `LanguageParser.cs:5924`). Cs1 uses `(EnumMember; "," : ?)+` — trailing comma consumed; semicolon
  separator NOT supported (documented gap). Empty enum body IS allowed (`enum E { }` — spec
  `enum-member-list?`), hence `EnumMemberList?` in `EnumDeclaration`.
  Same trailing-comma allowance applies to `ParameterList` and `AttributeArgumentList`
  (spec `parameter-list: parameter ,*`, `attribute-argument-list: ( attribute-argument ,* )`).
- **Named attribute arguments use `=`** (C# 1.0 form: `Name = value`); the `Name:` form is C# 2.0.
- **Attribute targets** (`field:` etc.) — C# 2.0, excluded.
- **Delegate parameters**: `attributes? parameter-modifier* type identifier`, where
  `parameter-modifier = ref | out | params` — all three are C# 1.0 (spec §11.6.1 lists exactly these
  three). (An earlier draft of this note said "no out"; corrected — `out` parameters are core C# 1.0.)
  `Parameter = Attributes? ParameterModifier* Type TypeName;` — the parameter NAME goes through the
  keyword-guarded `TypeName` (a parameter cannot be named `class`, etc.).
- **Attribute argument constants**: minimal set per task — `true`/`false`, decimal/hex integers +
  suffix, string/verbatim/char literals, unary `+`/`-`. No real literals (T1.4 can add them — the
  `DecimalRealLiteral`/`Exponent`/`RealSuffix` terminals exist), no identifier/const-identifier
  arguments, no `new`, no casts.
- **`Type = Identifier`** is provisional (T1.4 re-declares with predefined/qualified/array/pointer
  alternatives). Because it is a plain `Identifier` reference, `enum E : int` parses (the DFA matches
  the string `int`) while `class int { }` does NOT (class names go through the guarded `TypeName`).
  Base lists with dotted names (`class C : A.B { }`) do not parse until T1.4 — use identifier-named
  bases in T1.3 tests (per task).
- **No global (`[assembly:...]`) attributes** at compilation-unit level (Roslyn
  `ParseNamespaceBodyWorker:687` handles them; out of task scope — gap for a later task).
- **No ordering enforcement** (externs→usings→members): the body loop is a single
  `NamespaceMember*` over `UsingDirective | ExternAliasDirective | NamespaceDeclaration |
  TypeDeclaration` (mirrors Roslyn's one interleaved loop, `ParseNamespaceBodyWorker:584-764`);
  Roslyn's externs-then-usings ordering is a binder-level diagnostic. Deviation from the task's
  "NamespaceMemberDeclaration: TypeDeclaration (only these exist at namespace level)" — nested
  namespaces (`namespace A { namespace B { } }`) are valid C# 1.0 and must parse, so
  `NamespaceDeclaration` is also an alternative of `NamespaceMember`.
- **Bodies empty**: `ClassBody = "{" "}"`, `StructBody`, `InterfaceBody` are separate named rules so
  T2.1 can re-declare them (merge appends the memberful alternative; longest match picks it).
- **File-scoped namespace** (`;` form) — C# 10, excluded; `NamespaceDeclaration` is block-form only
  (T1.4+ re-declares to add the file-scoped alternative).
- **`params`** accepted before a parameter type (valid C# 1.0 for methods; harmless here since
  `Type = Identifier` cannot yet form arrays).

## Provisional rules to re-declare in later tasks (merge = append alternatives)

| Rule (Cs1.grammar) | Later re-declaration |
|---|---|
| `Type = Identifier;` | T1.4: `+` predefined types (KwBool...KwVoid), qualified names, arrays `[...]`, pointers `*`, nullable `?` |
| `ClassBody` / `StructBody` / `InterfaceBody` = `"{" "}"` | T2.1: `"{" Member* "}"` |
| `ClassModifier` | Cs2: `+ Partial`; Cs2: `+ Static` (if C# 1.0 spec is found to include it) |
| `EnumMemberValue = "=" Constant` | T2.x: full constant-expression (casts, identifiers, `+`/`-`/`~`/`!` on constants) |
| `Constant` | T1.4/T2.x: real literals, identifier constants, `new`, type names (typeof) |
| `NamespaceDeclaration` | Cs10: `+` file-scoped form |
| `UsingDirective` | Cs6: `+ static`; Cs10: `+ global` |
| `ReservedKeyword` | grows only (never shrinks — all 81 stay reserved in every later version) |
| `NamespaceMember` | T2.x if new namespace-level forms appear (none expected before Cs10) |

## Engine findings (hit during implementation)

### 1. `CsNitraVisitor` did not handle separator modifiers (`: ?` / `: !`) — FIXED

The meta-grammar already supports `(Elem; Sep : ?)+` / `: !` in grammar text, and `RuleGenerator`
already maps the modifier to `SeparatorEndBehavior`. But the parse-tree→AST bridge (`CsNitraVisitor`)
had no case for the `SeparatorModifier` SeqNode the meta-grammar produces
(`Optional(Seq(":", Modifier))` → `SomeNode(SeqNode "SeparatorModifier")`), so ANY grammar text using
a separator modifier threw `Unknown SeqNode kind: SeparatorModifier`. The C# 1.0 grammar needs `: ?`
(enum members, parameters, attribute arguments all allow trailing commas), so this was a hard blocker.

Fix (minimal): `CsNitraVisitor.Visit(SeqNode)` case `"SeparatorModifier"` → `ProcessSeparatorModifier`
returns the modifier `Literal` (`?`/`!`), which the existing `Some<Literal>` arm of
`ProcessSeparatedListExpression` already consumes. Verified: ParserTests (metacircular + recovery)
still fully green; C# 1.0 separated lists now build.

### 2. Recovery: what the current engine actually recovers for this grammar

Empirically probed (temporary diagnostic tests, since removed):

| Input | Result |
|---|---|
| `class C {` (missing `}`) | RECOVERED: `S1` inserts `}` at EOF → full success, 1 diagnostic (`Inserted@9`), `ErrorInfo` null |
| `namespace N { using System;` (missing `}`) | RECOVERED: same mechanism (`Inserted@27`) |
| `class { }` (missing class name) | NOT recovered: `FatalError`, 0 diagnostics |
| `using System` (missing `;`) | NOT recovered: `FatalError`, 0 diagnostics |
| `using System; using Collections` (trailing incomplete directive) | NOT recovered: partial success, `FatalError` |
| valid programs (multi-declaration, realistic sample) | clean full-consumption success, 0 diagnostics |

Why the missing-NAME case fails: the S1 candidate injects the missing identifier as a zero-width
match at the recovery point, but `ParseRule` only accepts an epsilon (zero-progress) success at a
recovery position for recoverable `RecoveryRule`s (`AcceptEpsilonMatch`); `TypeName` is a plain
`Seq`, so the injected identifier is rejected → no candidate makes progress. The missing-`}` case
works because the injected `}` completes a Seq element and the outer rules make real progress.
Consequence: the smoke test uses `class C {` (missing brace), not a missing name. Deeper
"missing identifier" recovery (e.g. via `OftenMissed`/RecoveryRule affordances in the C# grammar)
is engine/T4.1 territory, not T1.3.1.

Also noted: a rule whose entire body is a `ZeroOrMany` (like `CompilationUnit = NamespaceMember*;`)
returns Failure (not success) on empty input — `ParseRule` discards zero-progress successes outside
recovery positions. Empty C# files therefore do not parse in this engine; not a T1.3.1 concern,
flagged for the engine owners.

## Files

- `Parsers/CSharp/CSharpGrammar/Cs1.grammar` — replaced (main artifact: full C# 1.0 grammar).
- `Parsers/CSharp/CSharpGrammar/CSharpTerminals.cs` — added `KeywordTerminal` record + 82 `Kw*`
  factory methods (whole-word keyword terminals) + `GetAll()` (full terminal list for parser builds).
- `Parsers/CsNitra/CsNitraGrammar/CsNitraVisitor.cs` — engine fix: `SeparatorModifier` case
  (see Engine findings §1).
- `Tests/CSharpGrammarTests/CSharpParserTests.cs` — 4 smoke tests rewritten for the real grammar
  (same shape: success / multi-declaration / malformed→recovery diagnostics / ctor-unknown-terminal;
  malformed input = `class C {`).
- `Tests/CSharpGrammarTests/CSharpParserMultiTextTests.cs`, `SmokeTestTerminals.cs` — expected
  unaffected (they use their own inline grammar texts); verified (37/37 green).

## Verification log

- `dotnet build Nitra.sln --no-incremental` — 0 warnings, 0 errors.
- `dotnet test Tests/CSharpGrammarTests` — 41/41 passed (4 parser smoke + 37 terminal/multi-text).
- `dotnet test Tests/ParserTests` — 310/310 passed, 2 skipped (metacircular + recovery suites green
  after the `CsNitraVisitor` change).
- `dotnet test Tests/RegexTests` — 9/9 passed. `dotnet test Tests/WiWorkflowTests` — 1/1 passed.
- Runtime sanity parse (temporary test, since removed): realistic C# 1.0 program (comment, using +
  alias using, extern alias, nested namespaces, attributes incl. string argument, abstract/sealed
  classes with base list, struct, interface, enum with trailing comma + `: int` base + negative
  constant, empty enum, delegate, internal nested class) → full consumption (end == input.Length),
  0 recovery diagnostics.

## Grammar quality rework (second pass, plan-owner review fixes)

All fixes verified against the Roslyn checkout (`C:\RSDN\roslyn`, main branch, HEAD 9f220ee5d10).
File:line anchors below are into that checkout.

### Per-defect outcomes

1. **`KwOut` in `ParameterModifier` — NOT removed (review premise refuted by Roslyn).**
   `out` parameters are C# 1.0, not C# 2.0.
   - Roslyn parses `out` as an ordinary parameter modifier with NO language-version gate:
     `LanguageParser.cs:4999-5013` (`IsParameterModifierExcludingScoped` = this|ref|out|in|params|readonly,
     consumed unconditionally in `ParseParameterModifiers` 5015-5027).
   - The C# 2 feature table has NO out-params entry: `Errors/MessageID.cs:723-737` (the whole
     `LanguageVersion.CSharp2` block: generics, anon delegates, global namespace, fixed buffer,
     static classes, partial types, property accessor mods, extern alias, iterators, default,
     nullable, pragma, switch-on-bool). The only out-related gate is `IDS_FeatureOutVar`
     (`MessageID.cs:136`, `:678` → C# 7, for `out var`, not `out` params).
   - The previous note in this file (line ~107, "out parameters are core C# 1.0") was already correct;
     the review's "C# 2.0" attribution appears to have confused `out` params (C# 1.0) with `out var`
     (C# 7) or the C# 2 feature table's `IDS_FeatureExternAlias` neighbor.
   - Result: `ParameterModifier = | KwRef | KwOut | KwParams;` unchanged.

2. **`AttributeList` closing `]` — FIXED (required), trailing comma kept.**
   `LanguageParser.cs:1103-1141` (`TryParseAttributeDeclaration`): `ParseCommaSeparatedSyntaxList`
   with `allowTrailingSeparator: true` (line 1128) then a REQUIRED `EatToken(CloseBracketToken)`
   (line 1132). So `[A, B` is not an attribute list and `[A, B,]` is.
   - Now: `AttributeList = "[" (Attribute; "," : ?)+ "]";`
   - Verified behavior: `[A class Z { }` now FAILS to parse (previously the optional `]` let `[A`
     be swallowed as an attribute list and `class Z { }` parse after it).

3. **`DelegateDeclaration` final `;` — FIXED (required).**
   `LanguageParser.cs:5835-5866` (`ParseDelegateDeclaration`): ends with
   `this.EatToken(SyntaxKind.SemicolonToken)` (line 5865) — required.
   - Now: `DelegateDeclaration = Attributes? DelegateModifier* KwDelegate Type TypeName "(" ParameterList? ")" ";";`
   - Verified: `delegate void D();` / `D(int x)` parse; `delegate void D(int x)` (no `;`) fails.

4. **`StructModifier` `KwNew` — FIXED (removed), plus two more per-kind set corrections.**
   Roslyn's per-kind type-modifier table (binder; the parser accepts all modifier keywords generically
   via `ParseModifiers` `LanguageParser.cs:1347-1484` + `GetModifierExcludingScoped` 1286-1345, and the
   binder enforces per-kind sets):
   - `SourceMemberContainerSymbol.cs:293-355` (`MakeModifiers`):
     - base for all kinds: `AccessibilityMask | File` (307; `File` = C# 11, gated
       `ModifierUtils.cs:114` `IDS_FeatureFileTypes`);
     - **nested types only**: `+ New` (315) — `new` is a nested-type modifier, invalid at namespace
       level → removed `KwNew` from `StructModifier`;
     - class: `+ Partial | Sealed | Abstract | Unsafe | Safe | Closed` (+ `Static` 336) (331-337);
     - struct: `+ Partial | ReadOnly | Unsafe | Safe` (+ `Ref` 345) (341-346);
     - interface: `+ Partial | Unsafe | Safe` (350);
     - delegate: `+ Unsafe | Safe` (353);
     - enum: no additions (access only) — `unsafe enum` is invalid;
   - Version gates for the additions (all C# 2+ → excluded from Cs1): `Partial` C# 2
     (`MessageID.cs:729`), `Static` class C# 2 (`MessageID.cs:728`), `Ref` struct C# 7.2
     (`MessageID.cs:657`), `ReadOnly` struct C# 7.2 (`MessageID.cs:658`), `Safe` C# 13
     (`MessageID.cs:497` `IDS_FeatureUnsafeEvolution`, `ModifierUtils.cs:118-123`), `Closed` C# 14
     (`ModifierUtils.cs:115`), `File` C# 11 (`ModifierUtils.cs:114`).
   - `abstract` on interface is INVALID: `Abstract` is absent from the interface allowed-set
     (`SourceMemberContainerSymbol.cs:349-351`); invalid modifiers report CS0106
     `ERR_BadMemberFlag` via `ModifierUtils.cs:75-106`. → removed `KwAbstract` from `InterfaceModifier`.
     (Spec/impl divergence: the spec grammar listed `abstract` as an interface modifier; the compiler
     has never accepted it. Roslyn is the designated reference.)
   - `protected`/`private` (and `protected internal`) on namespace-level types are INVALID:
     `Test/Symbol/Symbols/Source/AccessTests.cs:78-99` (`Access_3_5_1_d`) — quotes the spec corollary
     "Types declared in a compilation unit or namespace cannot have private, protected, or protected
     internal accessibility" and expects 6 declaration diagnostics for 6 such declarations.
     → all five modifier sets restricted to `KwPublic | KwInternal` (T2.1 re-adds
     `KwProtected | KwPrivate | KwNew` for nested types — append-only merge).
   - Resulting C# 1.0 modifier sets (namespace-level declarations):

     | Kind | Modifiers (Cs1.grammar) |
     |---|---|
     | class | `KwPublic | KwInternal | KwAbstract | KwSealed | KwUnsafe` |
     | struct | `KwPublic | KwInternal | KwUnsafe` |
     | interface | `KwPublic | KwInternal | KwUnsafe` |
     | enum | `KwPublic | KwInternal` |
     | delegate | `KwPublic | KwInternal | KwUnsafe` |

5. **`unsafe` modifier — FIXED (added where Roslyn + C# 1.0 allow it: class, struct, interface, delegate).**
   - Allowed kinds: `SourceMemberContainerSymbol.cs:331` (class), `:341` (struct), `:350` (interface),
     `:353` (delegate); NOT for enum (absent from the switch).
   - Version: NO feature gate — `CheckUnsafeModifier` only checks the `/unsafe` compilation flag
     (`Symbols/SymbolExtensions.cs:287-296`); the feature table has no unsafe-type entry (only
     `IDS_FeatureRefUnsafeInIteratorAsync` C# 8 `MessageID.cs:526` and `IDS_FeatureUnsafeEvolution`
     C# 13 `MessageID.cs:497`, both unrelated). → C# 1.0 baseline.
   - `unsafe interface` / `unsafe delegate` included per Roslyn (no gate); the C# 1.0 spec's per-kind
     grammar could not be re-verified from a reachable spec copy (web excluded) — Roslyn followed as
     designated reference.

6. **Re-verification round (defect 6 of the review) — outcomes:**
   - `";"?` after class/struct/interface bodies and after namespace block — **CONFIRMED legal, kept.**
     Roslyn optionally consumes a trailing `;` after the closing brace: `TryEatToken(SemicolonToken)`
     at `LanguageParser.cs:1913` (class/struct/interface, in `ParseMainTypeDeclaration`) and
     `:321` (namespace, in `ParseNamespaceDeclarationCore`); no binder diagnostic for it (the
     body-less `class C;` form is the error case, handled at
     `Binder/BinderFactory.BinderFactoryVisitor.cs:1416`). Verified: `class C { };` and
     `namespace N { class C { } };` parse.
   - `EnumMemberList = (EnumMember; "," : ?)+` trailing comma — **CONFIRMED legal, kept.**
     `LanguageParser.cs:5916-5924` (`ParseEnumDeclaration`): `allowTrailingSeparator: true` (5922).
     (Roslyn also tolerates `;` separators, 5923 `allowSemicolonAsSeparator: true` — resilience with a
     semantic error, not grammar; documented gap stands.)
   - `BaseList = ":" (Type; ",")+` trailing comma — **CONFIRMED illegal, already correct (no change).**
     `LanguageParser.cs:2170-2232` (`ParseBaseList`): every comma must be followed by a type
     (2191-2195); there is no trailing-separator path (unlike the generic
     `ParseCommaSeparatedSyntaxList` lists). `class C : B, I1, { }` fails.
   - `EnumBaseList = ":" Type` — **CONFIRMED (single type, C# 1.0).**
     `LanguageParser.cs:5884-5894` (`ParseEnumDeclaration`): `:` + exactly one `ParseType()`.
   - `Parameter` with `ref`/`out`/`params` — **CONFIRMED all three C# 1.0** (see defect 1 anchors).
     C# 7.2 additions (`in`, `readonly`, `this` params — `LanguageParser.cs:4999-5013`) stay out;
     their gates: `IDS_FeatureReadOnlyReferences` C# 7.2 (`MessageID.cs:656`),
     `IDS_FeatureThisParameter` (C# 7.2).
   - BUT two parameter/argument LIST bugs found and fixed (the review's `: ?` notes were inverted for
     these):
     - **`ParameterList` trailing comma is ILLEGAL**: `LanguageParser.cs:4844-4852`
       (`ParseParameterList`): `allowTrailingSeparator: false` (4850).
       Now: `ParameterList = (Parameter; ",")+;` (was `: ?`). `delegate void D(int x,);` fails.
     - **`AttributeArgumentList` trailing comma is ILLEGAL and the list may be EMPTY**:
       `LanguageParser.cs:1193-1246` (`ParseAttributeArgumentList`): `allowTrailingSeparator: false`
       (1212), `requireOneElement: false` (1213).
       Now: `AttributeArgumentList = "(" (AttributeArgument; ",")* ")";` (was `: ?` + `+`).
       `[A(1,)]` fails; `[A()]` parses.
       (Named arguments: Roslyn parses both `Name = value` (1264-1267) and `Name: value`
       (1270-1273); the `Name:` form is C# 2.0 — grammar keeps only `TypeName "=" Constant`,
       confirmed correct for C# 1.0.)
   - `EnumMember` / `Constant` — **one fix + one documented gap:**
     - `ParseEnumMemberDeclaration` (`LanguageParser.cs:5952-5973`) parses member ATTRIBUTES first
       (5959) — C# 1.0 spec form is `[attributes] identifier (= constant-expression)?`.
       Now: `EnumMember = Attributes? TypeName EnumMemberValue?;` (was without `Attributes?`).
     - The value is a full expression in Roslyn (5962-5969); the minimal `Constant` set stands
       (bool, string, verbatim string, char, unary `+`/`-`, decimal/hex int + suffix — all C# 1.0
       forms; no real literals, no `~`/`!`/casts/identifiers — T2.x gaps as documented above).
     - **Octal integer literals** were C# 1.0 (terminal `OctalIntegerLiteral = 0[0-7]+` exists,
       `CSharpTerminals.cs:18-19`) but are deliberately NOT added to `Constant`: `DecimalIntegerLiteral
       = [0-9]+` matches the same leading-zero text, and the engine treats an equal-length alternative
       tie as an error — adding the octal alternative would break parsing of `0123`-style literals.
       Fixing it requires reworking the literal terminal regexes (terminal file is committed / out of
       scope here) — flagged for T1.4.
   - `TypeName = !ReservedKeyword Identifier` + `ReservedKeyword` — **one fix:**
     - The list is EXACTLY Roslyn's modern reserved set = enum range `BoolKeyword..ImplicitKeyword`
       (`Syntax/SyntaxKind.cs:170-331`, 81 keywords; `SyntaxKindFacts.cs:18-25/40-43`
       `GetReservedKeywordKinds`/`IsReservedKeyword`). `var`/`dynamic`/`yield`/`partial` are NOT in it
       (contextual, `SyntaxKind.cs:333-339`) — confirmed absent from the grammar list.
     - `alias` IS contextual in Roslyn (`SyntaxKind.cs:338-339`) and only serves `extern alias`
       (C# 2.0) → **removed `KwAlias` from `ReservedKeyword`** (in C# 1.0 `alias` was a plain
       identifier). The remaining 81 are all C# 1.0 keywords, incl. `in` (reserved, unused until C#
       7.2), `checked`/`unchecked`, `fixed`, `stackalloc`, `sizeof`, `explicit`/`implicit`
       (conversion operators), and the four `__` unsafe keywords — no version gate exists for any of
       them in Roslyn (no feature-table entry), consistent with C# 1.0 origin.

### Additional finding (version purity, outside the numbered defects) — FIXED

- **`extern alias` is C# 2.0, not C# 1.0**: `IDS_FeatureExternAlias` → `LanguageVersion.CSharp2`
  (`Errors/MessageID.cs:731`). Removed `ExternAliasDirective = KwExtern KwAlias TypeName ";";` and its
  `NamespaceMember` alternative from Cs1.grammar; dropped the `extern alias Foo;` line from
  `Parse_MultipleDeclarations_Succeeds`. The Cs2 stage re-adds the directive (append-only). Note this
  deviates from the original T1.3.1 task title ("usings/externs") in favor of the plan owner's hard
  rule 2 (C# 1.0 version purity).

### Files changed in this pass

- `Parsers/CSharp/CSharpGrammar/Cs1.grammar` — all fixes above.
- `Tests/CSharpGrammarTests/CSharpParserTests.cs` — removed `extern alias Foo;` from the
  multi-declaration smoke test (C# 2.0 construct).
- No other files touched (`CsNitraVisitor.cs` / `Nitra.sln` left as-is per plan owner).

### Verification log (this pass)

- `dotnet build Nitra.sln --no-incremental` — 0 warnings, 0 errors.
- `dotnet test Tests/CSharpGrammarTests --no-build` — 84/84 passed.
- `dotnet test Tests/ParserTests --no-build` — 310/310 passed, 2 skipped.
  (One flaky timing failure observed in `Recovery.RecoveryPerfTests.Test_Recovery_Performance_Scaling`
  — machine-load dependent `time(N=10) < 3*time(N=5)` assertion, passes on rerun, unrelated to the
  grammar.)
- Sanity parse (temporary `T131SanityProbe` test, since removed — 29 cases, all as expected):
  `class C { };` ok; `namespace N { class C { } };` ok; `[A, B] class X { }` ok; `[A, B,] class Y { }`
  ok; `[A class Z { }` FAILS; `delegate void D();` ok; `delegate void D(int x);` ok;
  `delegate void D(int x)` FAILS (no `;`); `delegate void D(int x,);` FAILS (trailing comma);
  `[A(1,)] class C { }` FAILS; `[A()] class C { }` ok; `unsafe class U { }` ok; `unsafe struct S { }`
  ok; `unsafe interface I { }` ok; `unsafe delegate void D();` ok; `unsafe enum E { A }` FAILS;
  `struct St { }` ok; `enum E : int { A, B, }` ok; `class C : B, I1 { }` ok;
  `class C : B, I1, { }` FAILS; `new struct St { }` FAILS; `abstract interface I { }` FAILS;
  `private class C { }` FAILS; `protected class C { }` FAILS; `static class C { }` FAILS;
   `delegate void D(out int x);` ok; `extern alias Foo;` FAILS; `enum E { [A] B, C = 2 }` ok.

## T1.3.1.2 — Kw* → native literals (third pass, plan-owner rework)

The plan owner rejected the 82 `Kw*` keyword terminals as boilerplate (hard rule 2): keywords in
grammar TEXTS must be native string literals, which get whole-word semantics automatically via
`WordLiteral` (T1.3.1.1, `RuleGenerator.cs:74-76`, `ExtensibleParser/Rules.cs:91-106`). This pass
completes that: `Cs1.grammar` rewritten on native literals, `CSharpTerminals.cs` stripped of all
`Kw*` machinery, one regression test added. All semantics re-verified against the Roslyn checkout
(`C:\RSDN\roslyn`, main, same HEAD as the second pass). Anchors below are into that checkout.

### Framework enabling change (new, recorded): anonymous literal alternatives in grammar texts

The meta-grammar did NOT support `X = | "a" | "b";` — `Alternative` only had
`NamedAlternative = "|" Identifier "=" RuleExpression` and `AnonymousAlternative = "|" QualifiedIdentifier`
(`CsNitraParser.cs:64-69`); a literal token after `|` failed with "Expected: Identifier" (verified
empirically before the change). Since the whole point of T1.3.1.1/2 is keyword lists as literal
alternatives, the meta-grammar was extended (append-only, all consumers stay green):

- `Parsers/CsNitra/CsNitraGrammar/CsNitraParser.cs` — third `Alternative` form:
  `AnonymousLiteral = "|" Literal` (a `Seq` of the `|` literal and the meta `Literal` terminal).
- `Parsers/CsNitra/CsNitraGrammar/Ast/CsNitra.Ast.cs` — new
  `AnonymousLiteralAlternativeAst(Literal Pipe, LiteralAst Literal, ...)` record;
  `Ast/CsNitra.Ast.Visitor.cs` — `Visit(AnonymousLiteralAlternativeAst)` in `IAstVisitor`/`AstVisitor`
  + `Accept` block.
- `Parsers/CsNitra/CsNitraGrammar/CsNitraVisitor.cs` — `"AnonymousLiteral"` SeqNode case →
  `ProcessAnonymousLiteralAlternative` (same shape as the existing `AnonymousAlternative` case;
  the `SeparatorModifier` case from the second pass is untouched).
- `Parsers/CsNitra/CsNitraGrammar/RuleGenerator.cs` — `AnonymousLiteralAlternativeAst` arm →
  `GenerateExpression(anonLiteral.Literal)` (i.e. `WordLiteral` for identifier-like values, plain
  `Literal` for punctuation — same as any other literal in grammar text).
- `AstSimplifier.TransformAlternative` needed NO change (default `_ => ast` pass-through).
- `Tests/ParserTests/CsNitra/CsNitraGrammarText.cs` — the text meta-grammar (used by
  `RuleGeneratorTests.GeneratedParserShouldParseGrammarSameAsManualParser`) got the matching line
  `| AnonymousLiteral = "|" Literal;` to stay in sync with the C# rules.
- Metacircular (`ShouldParseItself`), `RuleGeneratorTests`, `GrammarMergeTests` all re-verified green.

### Cs1.grammar (main artifact) — full rewrite on native literals

- All 82 `Kw*` references replaced with native literals (`KwUsing` → `"using"`, `KwClass` → `"class"`,
  `KwTrue` → `"true"`, …), incl. `ReservedKeyword` as a list of 81 literals. No `!`-predicate
  keyword guards anywhere (the `TypeName = !ReservedKeyword Identifier` guard stays — it is the
  Roslyn-faithful mechanism, now over whole-word literals).
- **`extern alias` RESTORED** (plan-owner defect): `ExternAliasDirective = "extern" "alias" TypeName ";";`
  + `| ExternAliasDirective` alternative in `NamespaceMember`.
  - Parser: `LanguageParser.cs:918-932` (`ParseExternAliasDirective`: `extern` + contextual `alias`
    + identifier + REQUIRED `;`), lookahead `ScanExternAliasDirective` `:905-916`, body-loop dispatch
    `:633-658`. The PARSER has no version gate.
  - Binder gate: `DeclarationTreeBuilder.cs:492` checks `IDS_FeatureExternAlias` → `LanguageVersion.CSharp2`
    (`Errors/MessageID.cs:731`). So Roslyn's PARSER accepts `extern alias` at every version and the
    BINDER rejects it below C# 2 — the same parser-vs-binder split the plan owner applies to
    visibility modifiers. The grammar models the parser → directive included. VERSION NOTE: the task
    statement calls it C# 1.0; Roslyn's feature table says C# 2 — recorded as a deviation, followed
    the explicit restore instruction. (The second pass had removed it citing exactly that table entry.)
- **Modifier sets** (final, per-kind; Roslyn's PARSER accepts ALL modifier keywords generically for
  every kind — `ParseModifiers` `LanguageParser.cs:1347-1484` + `GetModifierExcludingScoped`
  `:1286-1345`, shared by class/struct/interface via `ParseMainTypeDeclaration` `:1789`/`:1765-1778`
  and by delegate/enum `:5835`/`:5868` — per-kind legality is binder territory,
  `SourceMemberContainerSymbol.MakeModifiers` `:293-355`; the C# 1.0 per-kind sets below follow the
  plan owner's expected starting point + version filtering):

  | Kind | Cs1.grammar set | Roslyn anchors |
  |---|---|---|
  | class | `public private protected internal abstract sealed unsafe` | binder `SourceMemberContainerSymbol.cs:331-337` (class: Partial/Sealed/Abstract/Unsafe/Safe/Closed + Static); `static` C# 2 (`MessageID.cs:728`), `partial` C# 2 (`:729`) → both out |
  | struct | `public private protected internal abstract unsafe` | binder `:340-348` (struct: Partial/ReadOnly/Unsafe/Safe + Ref — NO Abstract in MODERN Roslyn: `ParserRegressionTests.cs:57` expects CS0106 for `abstract struct`); `readonly`/`ref` structs C# 7.2 (`MessageID.cs:658/657`) → out. `abstract struct` KEPT per plan owner ("legal in C# 1.0") — C# 1.0 spec `struct-modifier` included `abstract`; modern Roslyn parser accepts it too (`ParserRegressionTests.cs:40-41` parses `partial abstract struct S {}` with no syntax error) |
  | interface | `public private protected internal unsafe` | binder `:349-351` (interface: Partial/Unsafe/Safe — Abstract ABSENT → `abstract interface` invalid, CS0106 via `ModifierUtils.cs:75-106`); `unsafe` on interface CONFIRMED (`:350`) |
  | enum | `public private protected internal` | binder switch `:327-355` adds nothing for enum (no `unsafe` — `unsafe enum` invalid) |
  | delegate | `public private protected internal unsafe` | binder `:352-354` (delegate: Unsafe/Safe); `unsafe` on delegate CONFIRMED (`:353`) |

  - `unsafe` on types has NO feature-table entry (no `IDS_Feature*Unsafe*` for type modifiers —
    only `IDS_FeatureRefUnsafeInIteratorAsync` C# 8 `MessageID.cs:526` and
    `IDS_FeatureUnsafeEvolution` C# 13 `:497`, both unrelated) → C# 1.0 baseline;
    `CheckUnsafeModifier` only checks the `/unsafe` compile flag (`SymbolExtensions.cs:287-296`).
  - `new` is a NESTED-type-only modifier (`SourceMemberContainerSymbol.cs:315`, added only when the
    container is not a namespace) → NOT in any Cs1 set (T2.1 adds it for nested types).
  - All four visibilities in every set: namespace-level `private`/`protected` are binder diagnostics
    (spec corollary, `Test/Symbol/Symbols/Source/AccessTests.cs:78-99`), but the PARSER accepts them
    (generic `ParseModifiers`) — per plan owner note the syntax grammar includes them; the second
    pass had wrongly stripped them to `public|internal` (reverted).
- **Trailing commas** (each re-verified against Roslyn's `allowTrailingSeparator` flags):

  | List | Trailing comma | Roslyn anchor | Grammar |
  |---|---|---|---|
  | attribute list `[A, B,]` | ALLOWED | `LanguageParser.cs:1128` (`TryParseAttributeDeclaration`, `allowTrailingSeparator: true`), required `]` at `:1132` | `AttributeList = "[" (Attribute; "," : ?)+ "]";` (unchanged) |
  | attribute arg list `[A(1,)]` | FORBIDDEN, empty OK | `LanguageParser.cs:1212` (`ParseAttributeArgumentList`, `allowTrailingSeparator: false`), `:1213` (`requireOneElement: false`) | `AttributeArgumentList = "(" (AttributeArgument; ",")* ")";` (unchanged) |
  | enum member list `{ A, B, }` | ALLOWED | `LanguageParser.cs:5922` (`ParseEnumDeclaration`, `allowTrailingSeparator: true`), `:5923` (`requireOneElement: false`); `;`-as-separator resilience `:5924` (not modeled — documented gap stands) | `EnumMemberList = (EnumMember; "," : ?)+;` (unchanged) |
  | parameter list `(int x,)` | FORBIDDEN | `LanguageParser.cs:4850` (`ParseParameterList`, `allowTrailingSeparator: false`) | `ParameterList = (Parameter; ",")+;` (unchanged) |
  | base list `: B, I1,` | FORBIDDEN | `LanguageParser.cs:2188-2196` (`ParseBaseList`): every comma must be followed by `ParseType()`, no trailing path | `BaseList = ":" (Type; ",")+;` (unchanged) |

- **Trailing `";"?` after type/namespace bodies — CONFIRMED legal, kept** (second-pass finding
  re-verified): `TryEatToken(SemicolonToken)` at `LanguageParser.cs:1913` (class/struct/interface,
  `ParseMainTypeDeclaration`), `:5928` (enum), `:321` (namespace). `class C { };` and
  `namespace N { class C { } };` parse (sanity-verified this pass).
- **`EnumBaseList = ":" Type`** — single type, re-confirmed `LanguageParser.cs:5884-5894`
  (`ParseEnumDeclaration`: `:` + exactly one `ParseType()`).
- **`DelegateDeclaration` ends with REQUIRED `";"`** — re-confirmed `LanguageParser.cs:5865`
  (`EatToken(SemicolonToken)`).
- **`ParameterModifier = "ref" | "out" | "params"`** — re-confirmed (second-pass finding stands):
  parser consumes `this|ref|out|in|params|readonly` unconditionally (`LanguageParser.cs:4999-5027`,
  `IsParameterModifierExcludingScoped`/`ParseParameterModifiers`); version filtering: `in`/`readonly`
  params C# 7.2 (feature block `MessageID.cs:652-661`, `IDS_FeatureReadOnlyReferences` `:656`),
  `this` params C# 7.2 → out; `out` has NO gate (the only out-entry is `IDS_FeatureOutVar` C# 7
  `:678` — that is `out var`, not `out` params) → C# 1.0; `ref`/`params` no gate → C# 1.0.
- **`UsingDirective`** — re-confirmed against `ParseUsingDirective` `LanguageParser.cs:942-1006`:
  `using` + (alias: `Identifier "="` via `ParseNameEquals` `:934-940`) + `ParseQualifiedName`
  `:1000` + REQUIRED `;` `:1006`. `static` (`:956`, C# 6) / `unsafe` (`:957`, C# 12) usings excluded
  (T1.4+/later stages re-declare). `global using` (C# 10) excluded.
- **Namespace body** — single interleaved `NamespaceMember*` loop mirrors Roslyn's
  `ParseNamespaceBodyWorker` one loop (`LanguageParser.cs:584-703`: namespace `:588`, extern alias
  `:633`, using `:660`, members default). Externs-then-usings ordering is a diagnostic
  (`ERR_ExternAfterElements` `:647`), not grammar → no ordering enforced (second-pass decision stands).

### ReservedKeyword — C# 1.0 set (81 words)

Derived from Roslyn: the reserved set is the enum range `BoolKeyword..ImplicitKeyword`
(`Syntax/SyntaxKind.cs:171-331`, 81 values; `SyntaxKindFacts.cs:18-25` `GetReservedKeywordKinds`,
`:40-43` `IsReservedKeyword`, `:880-1048` `GetKeywordKind`). Crucially, the LEXER's keyword
classification is VERSION-INDEPENDENT (`Parser/Lexer.cs:1840-1851` + `Parser/LexerCache.cs:25-31`:
`GetKeywordKind` first, then `GetContextualKeywordKind`, no `LanguageVersion` consulted), and
`ParseIdentifierToken` only accepts `IdentifierToken` (`LanguageParser.cs:6059-6090`) — so exactly
these 81 words are never type names in Roslyn at ANY language version, incl. C# 1.0. The list is
therefore identical to Roslyn's modern reserved set (= C# 1.0's 77 keywords + the 4 `__` unsafe
words; 77+4 = 81):

`abstract __arglist as base bool break byte case catch char checked class const continue decimal
default delegate do double else enum event explicit extern false finally fixed float for foreach
goto if implicit in int interface internal is lock long __makeref namespace new null object operator
out override params private protected public readonly ref __reftype __refvalue return sbyte sealed
short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked
unsafe ushort using virtual void volatile while`

Task-claim adjudication (the task said "verify, don't trust"):
- **`default` — KEPT** (task claimed "C# 7, NOT in Cs1"). The feature-table entries
  `IDS_FeatureDefaultLiteral` (`MessageID.cs:142`, `:665` → C# 7.1) and `IDS_FeatureDefault`
  (`:60`, `:733` → C# 2; used at `Binder_Expressions.cs:1632`) gate `default` as a LITERAL
  EXPRESSION — not the keyword. The keyword `default` is in the reserved range
  (`SyntaxKind.cs:229`), lexed as a keyword at every version → `class default { }` is a parse error
  in Roslyn even at `LanguageVersion.CSharp1`. (C# 1.0 also used `default:` switch labels.)
- **`in` — KEPT** (task claimed "C# 7 contextual, NOT a Cs1 keyword"). `in` is in the reserved range
  (`SyntaxKind.cs:287`) and is NOT in the contextual list (`SyntaxKindFacts.cs:1253-1312` — no
  `InKeyword` case); the C# 7.2 attribution is for the `in` PARAMETER feature (binder-gated,
  feature block `MessageID.cs:652-661`), not the keyword. Version-independent reservation →
  `class in { }` is a parse error at every Roslyn version.
- **`var`/`yield`/`partial`/`dynamic`/`async`/`await`/`nameof`/`when` — correctly ABSENT**: all
  contextual in Roslyn (`SyntaxKindFacts.cs:1253-1312`: `Yield :1257`, `Partial :1258`, `When :1289`,
  `Async :1287`, `Await :1288`, `NameOf :1286`, `Var :1291`; `dynamic` treated as contextual per
  `SyntaxKindExtensions.cs:48` + binder `Binder_Symbols.cs:897`) → valid identifiers → valid type
  names → not reserved.
- **`__arglist`/`__makeref`/`__reftype`/`__refvalue` — KEPT** (task: "WERE C# 1.0, verify"): in the
  reserved range (`SyntaxKind.cs:294-301` = 8366-8369), reserved at every version.
- **`params`/`stackalloc`/`implicit`/`explicit` — all four KEPT** (task: "contextual in C# 1.0,
  determine which may not be type names"): in Roslyn all four are in the reserved range
  (`Params :293` = 8365, `StackAlloc :267` = 8352, `Implicit :331` = 8384, `Explicit :329` = 8383),
  reserved at every version → NONE of them may be a type name → all four in `ReservedKeyword`.
- `alias` — correctly ABSENT: contextual in Roslyn (`SyntaxKind.cs:338-339`,
  `SyntaxKindFacts.cs:1272`) → a valid type name; only special inside `extern alias`
  (`ScanExternAliasDirective` `:913` checks `ContextualKind == AliasKeyword`). Second-pass removal
  stands.

### Files

- `Parsers/CSharp/CSharpGrammar/Cs1.grammar` — full rewrite on native literals (main artifact; CRLF).
- `Parsers/CSharp/CSharpGrammar/CSharpTerminals.cs` — DELETED: 82 `Kw*` factory methods, `Kw(...)`
  helper, `_keywords` dict, `_keywordLock`, `KeywordTerminal` record; `GetAll()` now 15 terminals
  (Trivia, Identifier, 8 number terminals, 5 string terminals, CharLiteral). Kept: everything the
  task listed. Grep of the repo: no other references to the Kw terminals (only
  `WordLiteralTests.cs:95` uses the string `"KwPublic"` as an explicit Kind argument — unrelated).
- `Parsers/CsNitra/CsNitraGrammar/CsNitraParser.cs` — +`AnonymousLiteral` alternative form (above).
- `Parsers/CsNitra/CsNitraGrammar/CsNitraVisitor.cs` — +`"AnonymousLiteral"` case (SeparatorModifier
  case from the second pass untouched).
- `Parsers/CsNitra/CsNitraGrammar/RuleGenerator.cs` — +`AnonymousLiteralAlternativeAst` arm.
- `Parsers/CsNitra/CsNitraGrammar/Ast/CsNitra.Ast.cs`, `Ast/CsNitra.Ast.Visitor.cs` — new
  `AnonymousLiteralAlternativeAst` node + visitor plumbing.
- `Tests/CSharpGrammarTests/CSharpParserTests.cs` — +1 regression test
  `Parse_KeywordPrefixIdentifier_FailsOrRecovers`: `usingx;` must not parse cleanly as `using x;`
  (WordLiteral whole-word end-to-end through the grammar text).
- `Tests/ParserTests/CsNitra/CsNitraGrammarText.cs` — text meta-grammar +`| AnonymousLiteral = "|" Literal;`
  (kept in sync with the C# rules for `RuleGeneratorTests`).
- `docs/CSharpParserPlan-checklist.md` — T1.3.1.2 → [✅].
- Hygiene: normalized CRLF (+ BOM where the committed files had it) in `Cs1.grammar`,
  `CSharpTerminals.cs`, `RuleGenerator.cs`, `CSharpParserTests.cs` (the interrupted agent had left
  the first two all-LF and `CSharpParserTests.cs` all-LF; `RuleGenerator.cs` was already
  mixed-CRLF at HEAD — now uniform).

### Verification log (this pass)

- `dotnet build Nitra.sln --no-incremental` — 0 warnings, 0 errors.
- `dotnet test Tests/CSharpGrammarTests --no-build` — 85/85 passed (84 prior + 1 regression).
- `dotnet test Tests/ParserTests --no-build` — 324 passed, 2 skipped (pre-existing
  `[Ignore("WIP")]` in `GrammarValidationTests`; metacircular `ShouldParseItself`,
  `RuleGeneratorTests`, `GrammarMergeTests` all green after the meta-grammar extension).
- `dotnet test Tests/RegexTests --no-build` — 9/9. `dotnet test Tests/WiWorkflowTests --no-build` — 1/1.
- Sanity (temporary `TempT1312SanityProbe`, 31 cases, all as expected, since removed):
  - POSITIVE (clean full-consumption parse, 0 recovery diagnostics): `extern alias MyAlias;`
    (restored); `using System; using A = B.C;`; `namespace N { public class C : B { } }`;
    `unsafe class U { }`; `unsafe struct S { }`; `abstract struct St { }` (kept per plan owner);
    `delegate void D();`; `enum E : int { A, B, }` (trailing comma); `[A, B,] class X { }`
    (trailing comma); `class C { };` (trailing `;`); `class publicity { }` (whole-word: `publicity`
    is a valid name); `private class C { }`; `protected class C { }` (parser-level visibilities);
    `unsafe interface I { }`; `unsafe delegate void D();`; `namespace N { class C { } };`;
    `interface I : J { };` (see deviation D5); `[A] delegate int F(string name, ref int x);`;
    `enum E { [A] B, C = 2 }`.
  - NEGATIVE (no clean parse — failure or recovery with diagnostics): `usingx;` (the regression
    case); `publicity class P { }`; `class int { }`; `class class { }`; `[C(1,)] class Y { }`
    (arg-list trailing comma); `delegate void D(int x,);` (param-list trailing comma);
    `class C : B, I1, { }` (base-list trailing comma); `unsafe enum E { A }`;
    `abstract interface I { }`; `static class C { }` (C# 2); `partial class C { }` (C# 2);
    `extern aliasx;` (whole-word `alias`).

### Deviations (this pass)

1. **`extern alias` restored despite Roslyn's C# 2 gate** — the task calls it C# 1.0 and the plan
   requires it; Roslyn's feature table gates it C# 2 (`MessageID.cs:731` via
   `DeclarationTreeBuilder.cs:492`) but its PARSER accepts it at every version
   (`LanguageParser.cs:918-932`). The grammar models the parser; the binder gate is out of scope.
   This REVERSES the second pass's removal (which cited the same table entry). If the plan owner
   re-imposes strict version purity, deleting `ExternAliasDirective` + its `NamespaceMember`
   alternative is the one-line rollback.
2. **`abstract struct` kept** — plan owner: "abstract struct IS legal in C# 1.0". Modern Roslyn's
   BINDER rejects it (`SourceMemberContainerSymbol.cs:340-348` has no `Abstract` for struct;
   `ParserRegressionTests.cs:57` expects CS0106) but its PARSER accepts it
   (`ParserRegressionTests.cs:40-41`), and the C# 1.0 spec `struct-modifier` included `abstract` —
   version purity with C# 1.0 as the target keeps it. (Second pass had dropped it from
   `StructModifier`.)
3. **`default` and `in` KEPT in `ReservedKeyword`** — the task's "C# 7" attribution was verified and
   refuted: both are in Roslyn's version-independent reserved range (`SyntaxKind.cs:229`/`:287`,
   lexer `Lexer.cs:1840-1851`), so Roslyn never allows them as type names at any version including
   C# 1.0; the C# 7/7.1/7.2 feature-table entries gate their USES (default literal, in-params), not
   their keyword status. See the ReservedKeyword section above for full anchors.
4. **Meta-grammar extended** (anonymous `| "literal"` alternatives) — required by the task's
   `ClassModifier = | "public" | ...;` shape; the pre-change meta-grammar could not parse it
   (empirically verified: "Expected: Identifier"). Append-only; all metacircular/merge tests green.
5. **`interface I : J { };` PARS** (listed under "negatives" in the task): an interface base list is
   core C# 1.0 (`ParseBaseList` `LanguageParser.cs:2170-2232`) and the trailing `;` is legal
   (`:1913`) — no Roslyn anchor supports rejecting it; treated as a positive sanity case.
6. Second-pass `public|internal`-only modifier sets reverted to all four visibilities per the plan
   owner's explicit note ("include all four visibilities per kind where Roslyn's PARSER accepts them"
   — it accepts all of them generically, `ParseModifiers` `LanguageParser.cs:1347-1484`; the
   namespace-level restriction is a binder diagnostic, `AccessTests.cs:78-99`).
