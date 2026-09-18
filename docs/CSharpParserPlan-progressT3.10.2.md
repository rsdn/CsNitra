# T3.10.2 — C# 10.0 `global using` directive

Status: DONE (build + all tests green; not committed — orchestrator commits)

## Task
Extend the grammar with the C# 10.0 `global using` directive:
```csharp
global using System;
global using static System.Console;
```
A `global using` is a using directive with a `global` prefix (applies to the entire assembly).

## Roslyn syntax found
- `ParseUsingDirective` — `C:\RSDN\roslyn\src\Compilers\CSharp\Portable\Parser\LanguageParser.cs:942-1010`:
  - `globalToken` (949-951): `CurrentToken.ContextualKind == GlobalKeyword ? EatToken() : null`.
  - `usingToken` (955), `staticToken` (956, `TryEatToken(StaticKeyword)`), `unsafeToken` (957).
  - `alias` (967, `IsNamedAssignment() ? ParseNameEquals() : null`).
  - `type` (1000): `alias == null ? ParseQualifiedName() : ParseType()`.
  - `semicolon` (1006).
  - Node: `_syntaxFactory.UsingDirective(globalToken, usingToken, staticToken, unsafeToken, alias, type, semicolon)` (1009).
- `UsingDirectiveSyntax` — `C:\RSDN\roslyn\src\Compilers\CSharp\Portable\Syntax\UsingDirectiveSyntax.cs:21-22` (and
  `Syntax.xml.Syntax.Generated.cs:9518`): `GlobalKeyword UsingKeyword StaticKeyword? UnsafeKeyword? Alias? NamespaceOrType SemicolonToken`.
- Version gate (binder/semantic, C# 10.0): `MessageID.IDS_FeatureGlobalUsing` checked in
  `Declarations/DeclarationTreeBuilder.cs:484-485` (`usingDirective.GlobalKeyword` non-default).
- `SyntaxKind.GlobalKeyword` — `Syntax/SyntaxKind.cs:341`.

## Grammar context (existing)
- `UsingDirective` (Cs1.grammar:11-13): `AliasUsingDirective = "using" TypeName "=" QualifiedName ";"`
  | `OpenUsingDirective = "using" QualifiedName ";"`. (No `static` form — pre-existing gap.)
- `NamespaceMember` (Cs1.grammar:5-9) includes `UsingDirective`.
- `QualifiedName = TypeName NamespaceSegment*` (Cs1.grammar:21); `TypeName = !ReservedKeyword Identifier` (Cs1.grammar:23).
- `static` is a reserved keyword (Cs1.grammar:619); `global` is NOT reserved (absent from Cs1.grammar:556-637).
- Cs10.grammar already appends `FileScopedNamespaceDeclaration` to `NamespaceMember` (T3.10.1).

## Rule written (Cs10.grammar)
Append `GlobalUsingDirective` to the `NamespaceMember` union (T0.3 merge) and define:
```
GlobalUsingDirective =
    | GlobalAliasUsingDirective = "global" "using" TypeName "=" QualifiedName ";"
    | GlobalStaticUsingDirective = "global" "using" "static" QualifiedName ";"
    | GlobalOpenUsingDirective = "global" "using" QualifiedName ";";
```
Mirrors Roslyn's `[global] using [static] [alias] name ;` (unsafe omitted — out of scope, matches the
existing non-global UsingDirective which also omits it).

Disambiguation (longest-match, mutually exclusive on the token after `using`):
- `=` after a TypeName -> alias; `static` (reserved) -> static; `;` after a QualifiedName -> open.
- `static` is reserved so `TypeName`/`QualifiedName` cannot consume it -> the open/alias alternatives
  fail on `global using static ...` and only the static alternative matches (no equal-length tie).
- vs `UsingDirective` (Cs1): starts with `"using"`, this starts with `"global"` -> different first token.

## Code iterations
- Iteration 1: appended `GlobalUsingDirective` to the `NamespaceMember` union (Cs10.grammar) and
  defined it with three alternatives (alias / static / open), mirroring the Cs1 `UsingDirective`
  (Cs1.grammar:11-13) with a leading `"global"` and an added `"static"` form.
  - Build: 0 errors. New test filter (`Cs10GlobalUsingTests`): 10/10 passed on the first run —
    no grammar iteration needed.
- Note: the grammar is parsed at RUNTIME (embedded resource, `CSharpParser.Build`), so the build
  cannot catch a grammar error; the tests are the check. The version-purity negatives (v9 REJECTS)
  prove the `GlobalUsingDirective` rule — not a top-level statement path — is what matches at v10:
  if a `Statement` could consume `global using ...`, v9 (which has the same statements) would also
  ACCEPT it, but the v9 negatives pass (rejected).

## Version-purity results
- v9 REJECTS `global using System;` (GlobalUsingSimple_RejectedAtV9) — PASS.
- v9 REJECTS `global using static System.Console;` (GlobalUsingStatic_RejectedAtV9) — PASS.
- v10 ACCEPTS both (GlobalUsingSimple_Succeeds, GlobalUsingStatic_Succeeds) — PASS.

## Tests (Cs10GlobalUsingTests.cs) — 10 total: 8 positive (v10), 2 negative (v9)
Positive (CreateParser(10)):
- GlobalUsingSimple_Succeeds — `global using System;`
- GlobalUsingStatic_Succeeds — `global using static System.Console;`
- GlobalUsingDottedName_Succeeds — `global using A.B.C;`
- GlobalUsingStaticDottedName_Succeeds — `global using static A.B.C.D;`
- GlobalUsingFollowedByType_Succeeds — `global using System; class C { }`
- GlobalUsingMultiple_Succeeds — `global using System; global using static System.Console;`
- GlobalUsingAlias_Succeeds — `global using X = System.Console;`
- NonGlobalUsing_StillSucceeds — `using System;` (regression, Cs1 UsingDirective untouched)
Negative (CreateParser(9), version-purity):
- GlobalUsingSimple_RejectedAtV9 — `global using System;`
- GlobalUsingStatic_RejectedAtV9 — `global using static System.Console;`

## Verification
- `dotnet build Nitra.sln --no-incremental` -> 0 Warning(s), 0 Error(s).
- `dotnet test Tests/CSharpGrammarTests` (fresh build) -> Passed: 1396, Failed: 0, Skipped: 3 (pre-existing), Total: 1399.
- `dotnet test Tests/ParserTests` (regression) -> Passed: 325, Failed: 0, Skipped: 2 (pre-existing), Total: 327.

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs10.grammar`
- `Tests/CSharpGrammarTests/Cs10GlobalUsingTests.cs` (new)
- `docs/CSharpParserPlan-progressT3.10.2.md` (this file)
