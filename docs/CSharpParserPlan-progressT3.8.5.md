# T3.8.5 — NRT annotations `?` / `!` on type names

## Status
DONE — build green, CSharpGrammarTests green (1302/1302), ParserTests green (325/325).

## Roslyn syntax found
- `?` (nullable) is parsed in the `ParseType` postfix loop
  (`C:\RSDN\roslyn\src\Compilers\CSharp\Portable\Parser\LanguageParser.cs:7614-7676`).
  The `QuestionToken` case (7618-7629) calls `TryEatNullableQualifierIfApplicable` (7682) and,
  on success, wraps the type in `NullableType` (7623, `_syntaxFactory.NullableType(type, question)`).
  `NullableTypeSyntax` is the only NRT-related type node
  (`Generated/CSharpSyntaxGenerator/.../Syntax.xml.Syntax.Generated.cs:797`).
- `!` (non-null) has **NO** dedicated syntax node: there is no `NotNullTypeSyntax` / `NotNullType`
  kind anywhere in the syntax tree (searched `src/Compilers/CSharp`). Roslyn represents the
  non-null annotation as a `NullableAnnotation` (flow-state metadata), not as a distinct node.
  Syntactically the `!` is a one-char suffix on a type name, exactly like `?`.
- Conclusion for the grammar: both `?` and `!` are 1-char type-suffix LITERALS (the same
  `StartsWith` mechanism as the `.` literal), applied in the `Type` postfix loop.

## Was the `?` suffix already supported?
NOT at any version. The `Type` rule (Cs1.grammar:508-512) has only `PredefinedType |
QualifiedName | PointerType (*) | ArrayType ([])` — no `?` and no `!`. The Cs1 comment
(Cs1.grammar:503-504) explicitly says "Nullable `?` (CS2) и NRT (CS8) не входят в C# 1.0".
Pre-existing tests confirm: `Cs1TypeTests.Invalid_NullableValueType_Fails`
(`delegate goo? D();` fails at v1) and `Cs1TypeTests.Invalid_NrtAnnotation_Fails`
(`delegate string? D();` fails at v1).

**Empirical baseline (scratch run, before the change):** `int?` / `string?` / `string!` all
REJECT at v1, v7, and v8. So the `?` suffix was NOT previously modeled anywhere; both `?` and
`!` had to be added. (The task's hypothesis that `?` was "probably already supported" was false.)

## Rule written
(Cs8.grammar, appended after the T3.8.4 `CoalesceAssign` section)

```
Type =
    | NullableType = Type : TypeNullable "?"
    | NotNullType = Type : TypeNotNull "!";

precedence TypeNullable, TypeNotNull, TypeArray;
```

- `NullableType` / `NotNullType` are `Type` TDOPP **postfixes** (first element is a self-`ReqRef`
  to `Type`, Parser.cs:137-146). The postfix precedence comes from the `: Level` on the self-Ref
  (Parser.cs:144). The `?` / `!` are 1-char grammar LITERALS (NOT terminals; `CSharpTerminals.cs`
  untouched).
- Two NEW precedence levels `TypeNullable` / `TypeNotNull` are added via a `precedence` statement
  merged at the `TypeArray` anchor (CsNitraTypeChecker.cs:67-122). Only NEW names appear before the
  anchor, so the merge succeeds; the final order is … Assignment, TypeNullable(5), TypeNotNull(4),
  TypeArray(3), TypePointer(2), Comma(1). All > 0.
- Why any level > 0 works: the `Type` postfix loop (Parser.cs:337-416) does NOT update
  `minPrecedence` in-loop, so every postfix with precedence > the entry `minPrecedence` (0 for a
  plain `Type` Ref) applies, in INPUT ORDER, until none match. Hence `?`/`!` compose in any order
  with `*`/`[]` (`int?[]`, `int[]?`, `int?*`).

## Code iterations
1. **Iteration 1 (baseline probe):** wrote a scratch test (`ScratchNrtBaselineTests.cs`, deleted
   after use) probing `int?`/`string?`/`string!` at v1/v7/v8. First build failed on `Output`
   (not in scope in MSTest) → switched to `Console.WriteLine`. Result: all REJECT at v1/v7/v8.
   Confirmed `?` was never modeled → both `?` and `!` must be added in Cs8.grammar.
2. **Iteration 2 (grammar):** added the `Type` re-declaration + `precedence` statement to
   Cs8.grammar. Re-ran the scratch probe: v1/v7 still REJECT (no regression, version-purity holds),
   v8 now PARSE for all three. Correct on the first try — no further iteration needed.
3. **Iteration 3 (tests):** deleted the scratch test, wrote the final
   `Cs8NrtAnnotationsTests.cs` (CRLF + UTF-8 BOM), all 24 tests green on the first run.

## Version-purity results
- v1: `int?` / `string?` / `string!` → REJECT (unchanged; matches pre-existing Cs1TypeTests).
- v7: `int?` / `string?` / `string!` → REJECT (the Cs8 `Type` re-declaration is absent).
- v8: `int?` / `string?` / `string!` → PARSE.
- No-regression: `!x` (logical NOT) and `a ? 1 : 2` (ternary) still parse at v7 (and v8) — the NRT
  suffixes are `Type` postfixes, the NOT/ternary are `Expression` operators (different TDOPP rules).

## Tests (pos/neg)
`Tests/CSharpGrammarTests/Cs8NrtAnnotationsTests.cs` — 24 tests, all pass.
- Positive (v8), 17: `string?`, `string!`, `int?`, `int!`, `MyClass?`, `MyClass!`; qualified
  `N.M?`/`N.M!`; combos `int?[]`, `int[]?`, `string?[]`, `int?*`; contexts return/parameter/local;
  disambiguation `!x` (NOT) and `a ? 1 : 2` (ternary) at v8.
- Positive (v7, no-regression), 2: `!x` and `a ? 1 : 2` still parse at v7.
- Negative (v7, version-purity `!`), 3: `string!`, `int!`, `MyClass!` REJECT at v7.
- Negative (v7, version-purity `?`), 2: `string?`, `int?` REJECT at v7 (CS8-only in this grammar).

## Verification
- `dotnet build Nitra.sln --no-incremental` → **Build succeeded, 0 Warning(s), 0 Error(s)**.
- `dotnet test Tests/CSharpGrammarTests` → **Passed: 1302, Failed: 0, Skipped: 3, Total: 1305**.
- `dotnet test Tests/ParserTests` → **Passed: 325, Failed: 0, Skipped: 2, Total: 327**.

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs8.grammar` — added the `Type` re-declaration (`NullableType` /
  `NotNullType` postfixes) + the `precedence TypeNullable, TypeNotNull, TypeArray;` statement.
- `Tests/CSharpGrammarTests/Cs8NrtAnnotationsTests.cs` — NEW (CRLF + UTF-8 BOM), 24 tests.
- `docs/CSharpParserPlan-progressT3.8.5.md` — this progress file (NEW).
