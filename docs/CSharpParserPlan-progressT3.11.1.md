# T3.11.1 — C# 11.0 interpolated raw string `$"""..."""`

Status: DONE (build + all tests green; not committed — orchestrator commits)

## Task
Extend the grammar with the interpolated raw string `$"""..."""` (C# 11.0). The interpolated
raw string is a raw string with a `$` prefix. Version purity: `CreateParser(10)` must REJECT it,
`CreateParser(11)` must accept it.

## Roslyn syntax found
- The interpolated raw string is scanned as a SINGLE token (`InterpolatedStringToken`) by
  `ScanInterpolatedOrRawStringLiteralTop` —
  `C:\RSDN\roslyn\src\Compilers\CSharp\Portable\Parser\Lexer_StringLiteral.cs:282-296`.
- `InterpolatedStringKind` enum (Lexer_StringLiteral.cs:319-337): `SingleLineRaw` / `MultiLineRaw`
  = "raw or raw-interpolated string that can start with at least one `$`, and then at least
  three `"`s". So the token form is `$`×D + `"`×N (D≥1, N≥3).
- The token is parsed into an `InterpolatedStringExpressionSyntax` by
  `ParseInterpolatedOrRawStringToken` —
  `C:\RSDN\roslyn\src\Compilers\CSharp\Portable\Parser\LanguageParser_InterpolatedString.cs:126-143`
  (`SyntaxFactory.InterpolatedStringExpression(getOpenQuote(), getContent(...), getCloseQuote())`).
- Non-interpolated raw string is a different token (`SingleLineRawStringLiteralToken` /
  `MultiLineRawStringLiteralToken`) scanned by `ScanRawStringLiteral` —
  `C:\RSDN\roslyn\src\Compilers\CSharp\Portable\Parser\Lexer_RawStringLiteral.cs:51-73`.

## Grammar context (existing)
- `RawStringLiteral = RawString;` (Cs11.grammar:19), connected to `Primary` (Cs11.grammar:23).
  `RawString` is a single `[Regex]` terminal matching the whole `"""..."""` (CSharpTerminals.cs:94-97).
- `RawInterpolatedStringLiteral = context("$"+, "\"\"\"" RawPart* "\"\"\"");` was in Cs6.grammar:43,
  connected to `Expression` (Cs6.grammar:57-60). Helpers `RawPart` (Cs6.grammar:45-51),
  `RawFormat` (Cs6.grammar:53). This was a version-purity bug: raw strings are C# 11, not C# 6,
  so `CreateParser(10)` (Cs1..Cs10) already accepted `$"""..."""`.

## Rule written (Cs11.grammar)
Moved `RawInterpolatedStringLiteral` (+ helpers `RawPart`, `RawFormat`) from Cs6.grammar to
Cs11.grammar, unchanged, and connected it to `Primary` (consistent with the non-interpolated
`RawStringLiteral`), not `Expression`:
```
RawInterpolatedStringLiteral = context("$"+, "\"\"\"" RawPart* "\"\"\"");

RawPart =
    | Hole = "{" RawHoleOpenBraces Expression RawFormat? "}"{n}
    | OpenBrace = RawOpenBraceLiteral
    | CloseBrace = RawCloseBraceLiteral
    | Quote1 = "\"" !"\""
    | Quote2 = "\"" "\"" !"\""
    | Text = InterpolatedRawText;

RawFormat = ":" RawFormatText;

Primary =
    | RawStringLiteral
    | RawInterpolatedStringLiteral;
```
Cs6.grammar's `Expression` re-declaration now lists only `InterpolatedStringLiteral` and
`VerbatimInterpolatedStringLiteral` (the raw form is C# 11). `InterpolatedStringTests.cs`
now merges Cs1+Cs6+Cs11 so its `Raw` (start rule `RawInterpolatedStringLiteral`) tests still
resolve (they exercise the same rule, now a Cs11 feature).

## Code iterations
- Iteration 1: added the new test file `Cs11InterpolatedRawStringTests.cs` (v11 positives + v10
  version-purity negatives) and ran it WITHOUT changing the grammar.
  - Result: v11 positives PASSED (2/2) but v10 negatives FAILED (2/2) — `CreateParser(10)` already
    ACCEPTED `$"""..."""` via the Cs6 `RawInterpolatedStringLiteral`. This confirmed the
    version-purity bug (raw interpolation living in Cs6.grammar).
- Iteration 2: moved `RawInterpolatedStringLiteral` (+ `RawPart`, `RawFormat`) from Cs6.grammar to
  Cs11.grammar, removed it from the Cs6 `Expression` re-declaration, connected it to `Primary` in
  Cs11.grammar, and updated `InterpolatedStringTests.cs` to merge Cs11.
  - Result: all 4 new tests passed on the first run (v11 accepts, v10 rejects). No further grammar
    iteration needed.
- Note: the grammar is parsed at RUNTIME (embedded resource, `CSharpParser.Build`), so the build
  cannot catch a grammar error; the tests are the check. The version-purity negatives (v10 REJECTS)
  prove the rule is a Cs11-only feature: if the Cs6 `Expression` alternative still matched, v10
  would also ACCEPT, but the v10 negatives pass (rejected).
- Encoding: my grammar edit introduced LF line endings into Cs11.grammar (originally CRLF); restored
  CRLF/no-BOM. New test file written as CRLF + UTF-8 BOM.

## Version-purity results
- v10 REJECTS `class C { string S(string name) => $"""Hello, {name}!"""; }` (Simple_RejectedAtV10) — PASS.
- v10 REJECTS the multi-line form (MultiLine_RejectedAtV10) — PASS.
- v11 ACCEPTS the simple form (Simple_Succeeds) — PASS.
- v11 ACCEPTS the multi-line form (MultiLine_Succeeds) — PASS.

## Tests (Cs11InterpolatedRawStringTests.cs) — 4 total: 2 positive (v11), 2 negative (v10)
Positive (CreateParser(11)):
- InterpolatedRawString_Simple_Succeeds — `class C { string S(string name) => $"""Hello, {name}!"""; }`
- InterpolatedRawString_MultiLine_Succeeds — multi-line `$"""\n    Hello, {name}!\n    """`
Negative (CreateParser(10), version-purity):
- InterpolatedRawString_Simple_RejectedAtV10
- InterpolatedRawString_MultiLine_RejectedAtV10

## Verification
- `dotnet build Nitra.sln --no-incremental` -> 0 Warning(s), 0 Error(s).
- `dotnet test Tests/CSharpGrammarTests` (fresh build) -> Passed: 1400, Failed: 0, Skipped: 3 (pre-existing), Total: 1403.
- `dotnet test Tests/ParserTests` (regression) -> Passed: 325, Failed: 0, Skipped: 2 (pre-existing), Total: 327.

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs11.grammar` (added `RawInterpolatedStringLiteral` + helpers, connected to `Primary`)
- `Parsers/CSharp/CSharpGrammar/Cs6.grammar` (removed `RawInterpolatedStringLiteral` + helpers, removed from `Expression`)
- `Tests/CSharpGrammarTests/Cs11InterpolatedRawStringTests.cs` (new)
- `Tests/CSharpGrammarTests/InterpolatedStringTests.cs` (merge Cs11 so `Raw` start rule still resolves)
- `docs/CSharpParserPlan-progressT3.11.1.md` (this file)
