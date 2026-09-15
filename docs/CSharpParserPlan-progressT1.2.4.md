# T1.2.4 Progress — Verbatim family (`@"..."` + verbatim-interpolated `$@"..."` / `@$"..."`)

## Roslyn anchors (read, not web)

### Prefix dispatch (which orderings are legal)

- `Parser/Lexer.cs:610-632` — `case '@'` → `TryScanAtStringToken`; `Lexer.cs:634-645` —
  `case '$'` → `TryScanInterpolatedString`.
- `Lexer.cs:736-760` (`TryScanAtStringToken`): run of `@`s, then `"` → verbatim string
  (`:746-751`); then `$` → interpolated string (`:752-757`). So `@$"..."` is lexed by the
  interpolated path, not the verbatim-literal path.
- `Lexer.cs:762-778` (`TryScanInterpolatedString`): `$` followed by `$`/`@`/`"` →
  interpolated; `$$` = raw interpolated (T1.2.5).
- `Lexer_StringLiteral.cs:424-436` (`ScanOpenQuote`): exactly `('$','@','"')` or
  `('@','$','"')` → `InterpolatedStringKind.Verbatim`, `startingDollarSignCount = 1`,
  `startingQuoteCount = 1`. **Both `$@` and `@$` orders are legal**; `@` alone + `"` is
  plain verbatim.
- Multiple `@` is illegal: `Test/Syntax/Parsing/RawInterpolatedStringLiteralParsingTests.cs:1437-1444`
  (`@@$""` → `ERR_IllegalAtSequence`).

### Verbatim content rules (plain `@"..."`)

- `Lexer_StringLiteral.cs:192-252` (`ScanVerbatimStringLiteral`):
  - `""` doubling — content quote, `:214-227`.
  - **No escapes at all** — backslash is plain content (`:237-238` just appends).
  - **Newlines allowed** — the loop has no newline check.
  - Unterminated at EOF → `ERR_UnterminatedStringLit` (`:229-235`).
  - No brace handling: `{`/`}` are plain content (no interpolation, no doubling needed).
- Test corroboration: `LexicalTests.cs:1004-1015` (`@"literal"` → no error),
  `:1019-1030` (`TestMultiLineVerbatimStringLiteral` — CRLF inside, no error),
  `:1066-1078` (`TestUnterminatedVerbatimStringLiteral`), `:1184-1198`
  (`TestVerbatimStringLiteralWithEscape` — `@"\e"` value is the literal 2 chars `\e`).

### Verbatim-interpolated content (`$@"` / `@$"`)

- Same `ScanOpenQuote` branch (kind=Verbatim, dollarCount=1) → shared content loop
  `Lexer_StringLiteral.cs:653-717` (`ScanInterpolatedStringLiteralContents`):
  - `"` → `IsEndDelimiterOtherwiseConsume` `:769-797`: in Verbatim, `""` is consumed as
    content (doubled quote); a single `"` terminates.
  - `}` → `HandleCloseBraceInContent` `:818-834`: single `}` in content →
    `ERR_UnescapedCurly` (error); `}}` is a literal `}`.
  - `{` → `HandleOpenBraceInNormalOrVerbatimContent` `:867-894`: `{{` is a literal `{`;
    otherwise opens a hole; missing closing `}` → `ERR_UnclosedExpressionHole`.
  - `\` → escape processing only for `kind == Normal` (`:693-707`); Verbatim just
    advances one char (backslash is literal content).
  - Newlines: allowed for Verbatim kind (`IsAtEnd(kind)` `:361-364`); forbidden in the
    Normal text portion (C# 10: `ERR_NewlinesAreNotAllowedInsideANonVerbatimInterpolatedString`,
    `LexicalErrorTests.cs:1008-2110` series).
- Hole scanning is shared with the regular family — `ScanInterpolatedStringLiteralHoleBalancedText`
  `:1022-1141`: newlines always allowed inside holes (`:1029-1035`); nested `@""`/`@$""`
  via `TryScanAtStringToken` (`:1093-1109`); nested regular string/char via
  `ScanStringLiteral` (`:1075-1092`, `:1150-1154`); first top-level `:` → format
  specifier (`:1054-1064`, `ScanFormatSpecifier` `:959-1017` — verbatim `""` inside a
  format, `{` is an error, ends at `}`/unterminated).
- Test corroboration: `ExpressionParsingTests.cs:203-214` (`@$"hello"` legal; C# 8+),
  `:259-270` (nested `@$"` inside a `$@"` hole), `RawInterpolatedStringLiteralParsingTests.cs:863-905`
  (`OuterVerbatimMiddleVerbatimInner*` — `@$"` nested inside `$@` holes, all compile clean).

## Decisions

1. **`@$"..."` is matched by the `InterpolatedStringLiteral` terminal, NOT by
   `VerbatimStringLiteral`.**
   - Semantically it is an interpolated string expression (it has holes). Roslyn's
     `ScanOpenQuote` classifies both `($"`+`@` orders as `InterpolatedStringKind.Verbatim`
     (`Lexer_StringLiteral.cs:424-436`) and the token kind is `InterpolatedStringToken`
     (`:294`).
   - Grammar safety: `Constant` in `Cs1.grammar:117-125` lists `StringLiteral |
     VerbatimStringLiteral` (no `InterpolatedStringLiteral`). If `VerbatimStringLiteral`
     matched `@$"..."`, hole-bearing constants would become valid `Constant`s.
   - This revises the tentative note in the T1.2.3 log ("T1.2.4 will make
     `VerbatimStringLiteral` accept both `@"` and `@$"`"); the task allows either
     placement ("via `InterpolatedStringLiteral` or a shared entry — document the
     choice"). No longest-match tie results: `@"` matches only `VerbatimStringLiteral`,
     `@$"`/`$@"` match only `InterpolatedStringLiteral`.
2. **Core change is minimal (no restructuring)**: new public entry
   `TryScanAtInterpolatedString(input, pos)` (pos at `@`, requires exactly `@$"`, scans
   Verbatim kind with dollarCount=1). `TryScanAtString`'s `$` branch now *delegates* to
   it (previously duplicated those 3 lines) — used by hole scanning, behavior unchanged.
   The Verbatim-kind content path was already complete in T1.2.3 (it powers `$@"`/`@$"`
   inside holes today), so no content-loop changes were needed.

## Implementation log

- `Parsers/CSharp/CSharpGrammar/StringLiteralScanner.cs`:
  - New public entry `TryScanAtInterpolatedString(input, pos)` — pos at `@`, requires
    exactly `@$"`, scans `StringKind.Verbatim` with `dollarCount: 1, quoteCount: 1`
    (identical parameters to the existing `$@"` branch of `TryScanInterpolatedString`).
  - `TryScanAtString`'s `$` branch now delegates to `TryScanAtInterpolatedString`
    (previously duplicated those 3 lines). Hole scanning behavior unchanged.
  - No content-loop changes: the Verbatim-kind path was already complete in T1.2.3.
- `Parsers/CSharp/CSharpGrammar/CSharpTerminals.cs`:
  - `VerbatimStringLiteral()` — dropped the `[Regex(""" @"(""|[^"])*" """)]` declaration;
    now returns a manual `VerbatimStringLiteralTerminal` (Kind `"VerbatimStringLiteral"`,
    unchanged) whose `TryMatch` calls `StringLiteralScanner.TryScanVerbatimString`.
  - `InterpolatedStringLiteralTerminal.TryMatch` — tries `TryScanInterpolatedString`
    (`$"`/`$@"`/`$$`→raw-stub) first, then falls back to `TryScanAtInterpolatedString`
    (`@$"`). This is the `@$` fix; the terminal keeps Kind `"InterpolatedStringLiteral"`.
  - `GetAll()` unchanged (both terminals already listed).
- `Tests/CSharpGrammarTests/CSharpTerminalsTests.cs`:
  - Removed the 2 regex-era verbatim tests and `InterpolatedStringLiteral_AtPrefixOrder_FailsUntilT124`.
  - Added 8 `VerbatimStringLiteral_*` methods + 5 `InterpolatedStringLiteral_Verbatim*` /
    `AtPrefixOrder` methods, extended both `StartPos_*` methods. All lengths verified
    against the unescaped test-string content (scripted quote-pair/length check) and
    against a throwaway harness calling the terminal directly.

## Test results (final)

- `dotnet build Nitra.sln` — **0 warnings, 0 errors**.
- `Tests/CSharpGrammarTests` — **69 passed / 0 failed** (baseline 59; +8 verbatim methods,
  +4 net verbatim-interpolated methods, 2 extended StartPos methods).
  - VerbatimStringLiteral: QuotedText (`@""`/`@"abc"`/`@"literal"`), DoubledQuotes
    (`@"a""b"`=7, `@""""`=5, `@"a"""`=6, `@"x"`=4), BackslashIsLiteralContent
    (`@"back\slash"`=13, `@"\e"`=5), NewlinesAllowed (LF; CRLF `@"multi line\r\nliteral"`=22),
    BracesArePlainContent (`@"{x}"`, `@"a}b"`), ExactBoundary (`@"a" + 1`→4),
    Unterminated_Fails (`@"`, `@"literal`, `@"""`), NonStringAfterAt_Fails (`@$x`, `@$`, `@x`).
  - Verbatim-interpolated (both orders): AtPrefixOrder (`@$""`=4, `@$"x{y}"`=8,
    `@$"{{x}}"`=9, `@$"a {x} b"`=11, `@$"{"}"}`=8, `@$"}}"`=6),
    VerbatimHolesAndBraces (`$@""`=4, `$@"abc"`=7, `$@"{x}"`=7, `$@"{{x}}"`=9,
    `$@"a {x} b"`=11, `$@"{"}"}`=8, `$@"}}"`=6, `$@"{x:0}"`=9, `$@"{x:}"`=8),
    VerbatimNewlinesAndBackslash (`$@"a\nb"`=8, `$@"a\rb"`=8 — CR allowed in verbatim,
    contrast with Normal family, `$@"\{x}"`=8 — backslash literal yet `{` still opens a
    hole, `$@"}\"`=5),
    VerbatimNestedLiteralsInHole (`$@"{s = "a""b"}"`=16, `$@"{ @"a}b" }"`=15 — `}` is
    plain content inside a nested verbatim, `$@"{ @$"a{b}" }"`=16,
    `$@"{@$"{$"{0}"}"}"`=18 — the exact source of Roslyn's
    `OuterVerbatimMiddleVerbatimInnerNormal` test),
    VerbatimInvalidBraceOrHole_Fails (unterminated holes both orders, stray `}` both
    orders, `}` + trailing content, unterminated string both orders, single `"` inside a
    verbatim format specifier `$@"{x:"a"}"`).
  - StartPos: `x @"ab"`→5 (startPos 2), `x @$"{a}"`→7 (startPos 2), misaligned
    `a@"ab"`→-1.
- `Tests/ParserTests` — **310 passed / 2 skipped** (identical to baseline; the uncommitted
  T1.3.1 files stay green).
- Uncommitted T1.3.1 files (`Cs1.grammar`, `CsNitraVisitor.cs`, `CSharpParserTests.cs`,
  checklist, progressT1.3.1) untouched.

## Roslyn cross-checks used (test ↔ Roslyn test)

- `@"literal"`=10, `@"multi line\r\nliteral"`=22, `@"literal`→-1, `@"\e"`=5 ↔
  `LexicalTests.cs:1004-1015` / `:1019-1030` / `:1066-1078` / `:1184-1198`.
- `@$"hello"` legal (C# 8+), nested `@$"` in a `$@"` hole ↔ `ExpressionParsingTests.cs:203-214`
  and `:259-270`.
- `$@"{@$"{$"{0}"}"}"`=18 ↔ `RawInterpolatedStringLiteralParsingTests.cs:863-875`
  (`OuterVerbatimMiddleVerbatimInnerNormal`, same source text).
- Stray single `}` in verbatim-interpolated content → error ↔ `HandleCloseBraceInContent`
  (`Lexer_StringLiteral.cs:818-834`, `ERR_UnescapedCurly`); `}}` literal.
- `@@$""` illegal (`ERR_IllegalAtSequence`) ↔ `RawInterpolatedStringLiteralParsingTests.cs:1437-1444`
  — our entry requires exactly one `@`, so `@@$"` → -1 (fail-fast, same accept/reject).
- Newline contrast: verbatim content allows LF/CR; Normal interpolated text portion does not
  (CS8967 series, `LexicalErrorTests.cs:1008+`) — covered by `$@"a\nb"` passing while the
  existing `$"a\nb"` fails.

## Roslyn surprises

1. **`@"""` (4 chars) is unterminated, not a string with one doubled quote.** The doubled
   `""` consumes both quotes as content, leaving no close quote
   (`ScanVerbatimStringLiteral` `:214-227`). A string with one doubled quote is `@""""`
   (5 chars) → 5. (Task's minimum list had `@"""` → 5; the 4-char input can't match 5.)
2. **The task's `$@"{"}"}` is 9 chars**: hole = nested empty regular string `""`, `}`
   closes the hole, `"` closes the string, final `}` is trailing content → match = 8.
   The 8-char string `$@"{"}"` is a different (unterminated) input.
3. **`""` inside a hole is NOT a doubled quote**: nested strings in holes are always
   regular strings (`ScanStringLiteral`, `:1075-1092`, `:1150-1154`), so `$@"{s = "a""b"}"`
   contains two adjacent regular strings `"a"` and `"b"` — lexically fine, match = 16.
   Verbatim `""` doubling applies only to the string's own text portion and format
   specifier (`IsEndDelimiterOtherwiseConsume` `:788-796`, `ScanFormatSpecifier` `:987-997`).
4. **`@$"..."` is lexed by the interpolated path**: `TryScanAtStringToken` (`Lexer.cs:752-757`)
   routes `@...$"` to `ScanInterpolatedStringLiteral`; `ScanOpenQuote` classifies both
   orders as `InterpolatedStringKind.Verbatim` (`:424-436`) and the token kind is
   `InterpolatedStringToken` (`:294`). Hence the `InterpolatedStringLiteral` placement.
5. **Backslash before `{` in verbatim-interpolated**: `\` is literal content (escape case
   is `Normal`-only, `:692-707`), so `$@"\{x}"` is 8 — the `{` still opens a hole.

## Deviations (final)

1. `@$"..."` matched by `InterpolatedStringLiteral`, not `VerbatimStringLiteral`
   (decision #1 above; revises the T1.2.3 tentative note — task allows either).
2. Task's `@"back\slash"` is annotated "12 chars" but is 13; the scanner (13) is correct
   per Roslyn (backslash literal, `ScanVerbatimStringLiteral` `:237-238`).
3. Task's `@"""` → 5 is impossible for a 4-char input; covered as `@"""` → -1 plus
   `@""""` → 5 (surprise #1).
4. Fail-fast on first error → -1 (inherited from T1.2.3): stray `}`, unterminated holes,
   single `"` in a verbatim format specifier, `@@$"` all → -1 where Roslyn would record
   an error and keep scanning. Same accept/reject outcomes.

## Status

- [x] Research (anchors above)
- [x] Baseline green (59 + 310 pre-change)
- [x] Core entry + terminal rewiring
- [x] Tests (69 + 310 post-change)
- [x] Full build 0 warnings; CSharpGrammarTests + ParserTests green
- [x] T1.3.1 uncommitted files untouched
