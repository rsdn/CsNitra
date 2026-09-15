# T1.2.5 Progress — Raw strings (CS11): plain raw `"""..."""` + raw-interpolated `$..."""..."""`

## Status: in progress

## Baseline (pre-change)

- `dotnet build Nitra.sln` — 0 warnings / 0 errors.
- `Tests/CSharpGrammarTests` — 69 passed / 0 failed.
- `Tests/ParserTests` — 310 passed / 2 skipped (incl. uncommitted T1.3.1 files).
- Boundary tests carrying the "until T1.2.5" marker (currently PASSING while the stubs
  return -1; to be updated to the new split):
  - `CSharpTerminalsTests.cs:231-235` — `StringLiteral_RawStringQuotes_FailsUntilT125`
    (`StringLiteral` on `"""x"""` → -1).
  - `CSharpTerminalsTests.cs:425-429` — `InterpolatedStringLiteral_RawInterpolated_FailsUntilT125`
    (`InterpolatedStringLiteral` on `$$"""x"""` → -1).

## Roslyn research (checkout only, no web)

### Prefix dispatch

- `Parser/Lexer.cs:762-778` (`TryScanInterpolatedString`): `$` followed by `$`/`@`/`"` →
  `ScanInterpolatedStringLiteral`. So `$$`, `$$$...` all enter the interpolated scanner;
  the raw-vs-regular decision happens in `ScanOpenQuote`.
- `Parser/Lexer.cs:19-33` (`ScanStringLiteral`): input starting with 3+ `"` →
  `ScanRawStringLiteral` (plain raw path).
- `Lexer_StringLiteral.cs:415-520` (`ScanOpenQuote`):
  - `('$','@','"')` / `('@','$','"')` → Verbatim, D=1, N=1 (`:424-436`).
  - `('$','"',not '"',_)` / `('$','"','"',not '"')` → Normal, D=1, N=1 (`:438-449`) —
    i.e. `$"` but explicitly NOT `$"""`.
  - otherwise (`:451-519`): consume `@`-run (prefix), `$`-run, `@`-run (suffix), `"`-run:
    - N == 0 → error `ERR_StringMustStartWithQuoteCharacter`, stop (`:476-485`).
    - any `@` → error `ERR_IllegalAtSequence` (verbatim/raw mix) (`:489-492`).
    - N < 3 → error `ERR_NotEnoughQuotesForRawString`, **continue** scanning with that N
      (`:494-498`) — e.g. `$$"x"` lexes as a broken SingleLineRaw token, N=1.
    - single/multi decision (`:500-517`): consume whitespace; if next is a newline →
      MultiLineRaw (the newline is consumed as part of the open-quote section); else →
      SingleLineRaw and **reset to right after the quotes** (whitespace after the opening
      quotes is content for single-line).
- Whitespace = `Parser/CharacterInfo.cs:116-143` (`IsWhitespace`): ` `, `\t`, `\v`, `\f`,
  `\u00A0`, `\uFEFF`, `\u001A`, or (ch > 255 && SpaceSeparator). Newline =
  `CharacterInfo.cs:149-163` (`IsNewLine`): `\r`, `\n`, `\u0085`, `\u2028`, `\u2029`.

### Plain raw scan (`Parser/Lexer_RawStringLiteral.cs`)

- `ScanRawStringLiteral` `:51-99`: consume opening `"`-run (N, asserted ≥ 3), consume
  whitespace, newline → `ScanMultiLineRawStringLiteral`, else
  `ScanSingleLineRawStringLiteral`. No errors reported by the lexer — all errors are
  deferred to the parser re-scan.
- Single-line `:101-131`: scan to newline/EOF (unterminated → parser error) or to a
  `"`-run; **run < N → content; run ≥ N → string ends, the ENTIRE run is consumed as the
  close** (`:121-129`; excess quotes = parser error CS8998, token still contains them).
- Multi-line `:133-178`: per line — advance past newline, consume whitespace; if the
  line's `"`-run ≥ N → closing line, literal ends (entire run consumed, excess = CS8998,
  `:149-150`). Otherwise a content line: scan to newline/EOF; a `"`-run ≥ N **inside** a
  content line also terminates the literal with the entire run consumed
  (`:165-171`, parser error CS8999 `ERR_RawStringDelimiterOnOwnLine`).
- The parser re-scans plain raw tokens through the SAME interpolated scanner:
  `LanguageParser_InterpolatedString.cs:16-30` (`ParseRawStringToken` →
  `ScanInterpolatedOrRawStringLiteralTop`, `:165-173`). Consequence: every interpolated
  rule below applies to plain raw strings too (with D=0, so no brace rules).

### Shared content loop (raw branches) — `Lexer_StringLiteral.cs:653-717`

`ScanInterpolatedStringLiteralContents(kind, D, N)`:

- Empty multi-line check FIRST (`CheckForIllegalEmptyMultiLineRawStringLiteral`
  `:719-739`): for MultiLineRaw, at content start — consume whitespace, consume `"`-run;
  if run ≥ N → error CS9002 `ERR_RawStringMustContainContent` ("Multi-line raw string
  literals must contain at least one line of content"). So `"""\n"""` and `"""\n  """`
  are illegal, while `"""\n\n"""` (one empty content line) and `"""\n  \n"""` are legal
  (corroborated by `LexicalAndXml/RawStringLiteralLexingTests.cs:53,54,57,58,61,62,67,68,117`).
  Corroboration that it applies to PLAIN raw too: `RawStringLiteralLexingTests.cs:53`
  (`"""\n"""` → CS9002) and `RawInterpolatedStringLiteralCompilingTests.cs:636-649`
  (`$"""\n"""` → CS9002).
- Loop top (`:665-676`): `IsAtEnd(kind)` (newline for non-multiline, or EOF) → return
  (unterminated, error at end-scan). Then `IsAtEndOfMultiLineRawLiteral` (`:741-762`):
  for MultiLineRaw, if current char is a newline — lookahead newline + whitespace +
  `"`-run ≥ N → this is the legitimate close (position reset; the end-scan consumes it).
- `"` (`IsEndDelimiterOtherwiseConsume` `:769-816`): raw kinds — consume the `"`-run;
  run ≥ N → reset and return (string ends; the end-scan consumes the entire run);
  run < N → content (consumed).
- `\\` (`:692-707`): escapes only for kind Normal; raw kinds: backslash is plain content.
- `{` / `}` when interpolated — see brace rules below. (For plain raw the re-scan runs
  with `_isInterpolatedString=false`, so `{`/`}` are plain content — `:686-691` guard.)
- Everything else: content.

### Raw-interpolated brace rules (the crux)

`HandleOpenBraceInContent`/`HandleCloseBraceInContent` (`Lexer_StringLiteral.cs:818-865`)
dispatch to the raw variants:

- **Close brace in content** (`HandleCloseBraceInContent` `:835-852`): consume the
  `}`-run M. **M < D → plain content. M ≥ D → error CS9007
  `ERR_TooManyCloseBracesForRawString`** (whole run flagged; still consumed as content).
- **Open brace in content** (`HandleOpenBraceInRawContent` `:896-957`): consume the
  `{`-run K:
  - **K < D → plain content** (`:907-911`).
  - **K ≥ 2D → error CS9006 `ERR_TooManyOpenBracesForRawString`** on the first K−D
    braces (`:914-922`) — the hole still starts at the LAST D braces.
  - **D ≤ K < 2D → hole**: first K−D braces are literal content, last D open the hole
    (comment `:900-904`: "up to 2*N-1 open (or close) braces ... the inner N braces start
    the interpolation").
  - Hole body = the SAME `ScanInterpolatedStringLiteralHoleBalancedText` (`:1022-1141`)
    as regular/verbatim holes: newlines always allowed, nested strings (incl. raw — via
    `ScanStringLiteral` `:1075-1092`), `#` = error, balanced `()`/`[]`/`{}`, first
    top-level `:` → `ScanFormatSpecifier` (`:959-1017`; applies to raw holes too — the
    escape branch is Normal-only, a raw `"` inside a format ends it early).
  - Hole close (`:927-951`): consume the `}`-run M after the body:
    - M == 0 → error CS1054 `ERR_UnclosedExpressionHole`.
    - 0 < M < D → error CS9005 `ERR_NotEnoughCloseBracesForRawString`.
    - M ≥ D → consume exactly D; the surplus M−D is re-processed by the content loop,
      where a run ≥ D is CS9007. Net: **a well-formed hole close is D ≤ M < 2D**;
      M ≥ 2D → error.

Summary table (D = opening `$` count, N = opening `"` count):

| construct in content | D=1 | D=2 | general |
|---|---|---|---|
| `{`-run K < D | `` (none: K=0) | `{` | content |
| `{`-run D ≤ K < 2D | `{` | `{{`, `{{{` | hole (K−D literal + D open) |
| `{`-run K ≥ 2D | `{{` | `{{{{` | ERROR CS9006 |
| `}`-run M < D | `` | `}` | content |
| `}`-run M ≥ D (content) | `}` | `}}` | ERROR CS9007 |
| hole close run M | D ≤ M < 2D | D ≤ M < 2D | D ≤ M < 2D valid; else error |

Test corroboration (`Syntax/Parsing/RawInterpolatedStringLiteralParsingTests.cs`):
`$"""{0}}}"` CS9007 (`:192-207`), `$$"""{{{{0}}}}"` CS9006 (`:174-189`),
`$$"""{{{0}}}}"` CS9007 (`:210-225`), `$$"""{0}}"` CS9007 (`:228-243` — the `}}` run = D
in content), `$$"""{{{0}"` CS9005 (`:246-261` — hole close run 1 < D), `$$"""{{0}}"`
legal (`:264-276`), `$$"""{{{0}}}"` legal (`:278-291` — K=3: 1 literal + hole `{{0}}`,
close run 3: D consumed + 1 literal), `$"""{{0}}"` CS9006 (`:156-171` — D=1: `{{` is NOT
a `{{`-escape, it is an error), `"""a""""`-style excess quotes CS8998 (`:30-63`),
delimiter-in-content-line CS9000 (`LexicalAndXml/RawStringLiteralLexingTests.cs:73-74,101-102`).

### End scan — `Lexer_StringLiteral.cs:567-651`

- Single-line raw: next char not `"` → unterminated (CS8997); else consume the ENTIRE
  `"`-run (assert ≥ N), run > N → CS8998 on the excess (token keeps the whole run).
- Multi-line raw: EOF → CS8997; current char `"` (mid-content-line termination) →
  consume entire run → CS8999; else (legitimate close found by the lookahead) →
  advance past newline + whitespace + entire `"`-run, run > N → CS8998.

### Nesting (holes contain full C# of the same version)

`ScanInterpolatedStringLiteralHoleBalancedText` (`:1022-1141`): `$` →
`TryScanInterpolatedString` (which itself dispatches `$$`-raw), `"`/`'` →
`ScanStringLiteral` (which dispatches 3+-quote raw), `@` → `TryScanAtStringToken`.
Corroboration: `RawInterpolatedStringLiteralParsingTests.cs:448-477` (raw inside
regular-interpolated hole, single- and multi-line), `:500-590` (raw-interpolated inside
raw-interpolated hole, incl. `$$$"""{{{$"""{0}"""}}}"` at `:608-620`), `:668-680`
(closing `}` as a raw string inside a hole), `:848-905` (raw inside verbatim hole).

## Decisions

1. **Version-purity split (disjoint terminals, no longest-match ties):**
   - `StringLiteral` = plain-only. `TryScanPlainString` becomes plain-only: a 3+ opening
     `"`-run → -1 (was: dispatch to the raw stub).
   - `RawStringLiteral` (NEW) = raw-only: input starts with a 3+ `"`-run, no `$`.
   - `InterpolatedStringLiteral` = regular-only: `$"`, `$@"`, `@$"`.
     `TryScanInterpolatedString` no longer dispatches `$$` → -1 (was: raw stub).
   - `RawInterpolatedStringLiteral` (NEW) = raw-interpolated-only: 1+ `$` then 3+ `"`.
   - Split is disjoint by construction: after the `$`-run, a 1-quote run → regular
     interpolated; a 3+ quote run → raw-interpolated; 2 quotes → invalid for both.
     `StringLiteral` vs `RawStringLiteral` split by opening quote run (1 vs 3+).
   - Rationale: Roslyn has one `InterpolatedStringToken` for all `$`-forms, but our
     grammar needs per-version terminal sets and longest-match ties on equal lengths are
     errors. Keeping the families disjoint means Cs1.grammar (no raw, no interpolation)
     is unaffected: `StringLiteral` matches only plain strings.
2. **Nested literals in holes**: `TryScanPlainString`'s old dispatch behavior moves to a
   private `TryScanPlainOrRawString` used by `TryScanNestedLiteral` (holes contain full
   C# of the same language version, so a raw string inside a hole must scan). The `$`
   case in `TryScanHoleBalancedText` tries regular interpolated first, then
   raw-interpolated (mirrors Roslyn `:1044-1053` where `TryScanInterpolatedString`
   itself dispatches `$$`).
3. **Error mapping — Roslyn error → -1 (fail-fast, per T1.2.3/T1.2.4 convention), EXCEPT
   where the close-quote run is found.** Concretely:
   - **Match (extent returned)**:
     - any `"`-run ≥ N terminates the string; the extent includes the ENTIRE run
       (port of `Lexer_RawStringLiteral.cs:121-129`, `Lexer_StringLiteral.cs:585-602,
       617-629, 632-648`). Excess close quotes (CS8998) and mid-content-line
       delimiters (CS8999) are semantic diagnostics our scanner does not emit — the
       token extent is well-defined, so the terminal matches.
       E.g. `"""a""""b"""` → 8 (string is `"""a""""`), `"""\nabc"""\n"""` → 10.
     - open brace run D ≤ K < 2D (hole, K−D literal braces); hole close run D ≤ M < 2D.
     - brace runs < D in content (plain content — `$$"""{x}"""` is a VALID hole-less
       string; the "1-brace hole in a 2-dollar string" is content, not an error).
   - **-1 (scan failure)**:
     - unterminated (newline/EOF before a ≥N close run) — CS8997 family.
     - **empty multi-line raw** (`"""\n"""`, `"""\n  """`) — CS9002. Decision: this IS a
       scan failure for us, unlike the excess-quote cases: it is a lexical-shape
       constraint (spec: "must contain at least one line of content"), Roslyn's own
       raw-interpolated scanner records it as a scanner error
       (`CheckForIllegalEmptyMultiLineRawStringLiteral`, `:719-739`), and matching it
       would silently accept invalid C#. Note empty single-line raw is impossible by
       construction (the opening run swallows adjacent closing quotes: `""""""` is N=6
       unterminated, not 3+3 empty).
     - open brace run K ≥ 2D (CS9006); close brace run ≥ D in content (CS9007);
       hole close run M == 0 (CS1054) or 0 < M < D (CS9005) or M ≥ 2D (CS9007 via the
       surplus). Roslyn keeps scanning and still closes the token; our port fails fast
       (same accept/reject outcome, per T1.2.3/T1.2.4 deviation #4).
     - raw-interpolated prefix with < 3 quotes (`$$"x"`, `$$$""`) — CS1005
       `ERR_NotEnoughQuotesForRawString`. Roslyn continues scanning with N=1/2; we
       fail fast (task checklist lists these as invalid).
4. **Backslash in raw content**: plain content (no escapes) — the existing
   `case '\\' when kind is Normal` guard already does this (raw falls to default).
5. **Braces in plain raw**: plain content — `dollarCount: 0` keeps the `{`/`}` cases
   from firing (matches the `_isInterpolatedString=false` re-scan guard
   `Lexer_StringLiteral.cs:686-691`).
6. **Whitespace**: ported exactly from `CharacterInfo.cs:116-143` (incl. `\v`, `\f`,
   NBSP, BOM, `^Z`, and Unicode SpaceSeparator via `CharUnicodeInfo`).

## Implementation log

- `Parsers/CSharp/CSharpGrammar/StringLiteralScanner.cs`:
  - `TryScanPlainString` — now PLAIN-ONLY: a 3+ opening quote run returns -1 (was:
    dispatch to the raw stub). Powers the `StringLiteral` terminal (Cs1.grammar).
  - `TryScanInterpolatedString` — no longer dispatches `$$` to the raw stub; returns -1
    for a `$` followed by `$`. NEW: a `$` followed by a 3+ quote run also returns -1
    (raw-interpolated) — this was a real bug caught by the harness: before the fix
    `$"""{x}"""` matched the regular terminal as a 3-char `$""` (Roslyn's `ScanOpenQuote`
    Normal pattern only matches a 1-2 quote run, `Lexer_StringLiteral.cs:438-449`).
  - New public `TryScanRawString(input, pos)` — pos at first `"`; requires a 3+ quote run;
    no dollars.
  - New public `TryScanRawInterpolatedString(input, pos)` — pos at first `$`; `$`-run D ≥ 1
    then quote run N ≥ 3.
  - New private `TryScanRawContents` — single/multi-line decision (whitespace after the
    opening quotes + newline → multi-line, opening newline consumed; else single-line with
    content starting right after the quotes — port of `ScanOpenQuote` `:500-517` reset
    behavior) and the empty-multi-line check (CS9002) before scanning.
  - `TryScanStringContents` raw branches filled (no restructuring):
    - multi-line close-line lookahead on a newline (newline + whitespace + quote run ≥ N →
      return after the ENTIRE run — port of `IsAtEndOfMultiLineRawLiteral` `:741-762` +
      end-scan `:632-648`);
    - `case '"'` raw: quote run < N = content, run ≥ N ends the string with the entire run
      (port of `IsEndDelimiterOtherwiseConsume` `:800-815` + `ScanSingleLineRawStringLiteral`
      `:121-129`);
    - `case '{'` raw (D > 0): open run K < D = content; K ≥ 2D = -1 (CS9006); D ≤ K < 2D =
      hole (first K−D braces literal, last D open); hole body via the existing
      `TryScanHoleBalancedText`; hole close run M: -1 if M < D (CS1054/CS9005) or M ≥ 2D
      (CS9007 via the surplus) — port of `HandleOpenBraceInRawContent` `:896-957`;
    - `case '}'` raw (D > 0): close run < D = content, run ≥ D = -1 (CS9007) — port of
      `HandleCloseBraceInContent` `:835-852`.
  - `TryScanHoleBalancedText` `$` case now falls back to `TryScanRawInterpolatedString`
    after `TryScanInterpolatedString` (mirrors Roslyn `:1044-1053` where the single
    `TryScanInterpolatedString` entry itself dispatches `$$`).
  - `TryScanNestedLiteral` now uses new private `TryScanPlainOrRawString` (old
    `TryScanPlainString` dispatch behavior) so raw strings nest inside holes.
  - New helpers: `CountRun`, `ConsumeWhitespace`/`IsRawWhitespace` (exact port of
    `CharacterInfo.cs:116-143`, incl. `\v`, `\f`, NBSP, BOM, `^Z`, Unicode SpaceSeparator
    via `CharUnicodeInfo`), `SkipNewLine` (CRLF-aware, per `AdvancePastNewLine`),
    `IsIllegalEmptyMultiLineRaw` (CS9002 check).
- `Parsers/CSharp/CSharpGrammar/CSharpTerminals.cs`:
  - New `RawStringLiteral()` / `RawInterpolatedStringLiteral()` factory methods + static
    fields + sealed records (kinds `"RawStringLiteral"` / `"RawInterpolatedStringLiteral"`);
    both added to `GetAll()` after `VerbatimStringLiteral()`.
  - `StringLiteral` / `InterpolatedStringLiteral` / `VerbatimStringLiteral` unchanged in
    shape; their scanner entries now implement the disjoint split (decision #1).
- `Tests/CSharpGrammarTests/CSharpTerminalsTests.cs`:
  - `StringLiteral_RawStringQuotes_FailsUntilT125` → `StringLiteral_RawStringQuotes_Fails`
    (still -1, now version-purity, extended with a 6-quote case).
  - `InterpolatedStringLiteral_RawInterpolated_FailsUntilT125` →
    `InterpolatedStringLiteral_RawInterpolatedPrefix_Fails` (still -1, extended with
    `$"""{x}"""` and `$$$"""x"""`).
  - 15 new methods: `RawStringLiteral_{QuotedText,MultiLine,QuoteRunRules}_Matches`,
    `RawStringLiteral_{UnterminatedOrEmpty,NonRawPrefix}_Fails`,
    `RawInterpolatedStringLiteral_{DollarCounts,LiteralBraceRuns,HoleBraceSurplus,
    FormatSpecifier,NestedLiteralsInHole,MultiLine}_Matches`,
    `RawInterpolatedStringLiteral_{BraceRunErrors,UnterminatedOrEmpty}_Fails`,
    `InterpolatedStringLiteral_NestedRawInHole_Matches`,
    `InterpolatedStringLiteral_NestedSingleLineRawWithNewline_Fails`.
  - Both `StartPos_*` methods extended with raw terminals.
  - Every expected length verified with a throwaway harness (84 terminal checks, all OK)
    against the runtime string, then removed.
  - NOTE: C# 11+ forbids `@"""...` source (verbatim prefix + raw string = CS9009 family),
    so plain-raw test inputs are written as regular escaped strings, not verbatim.

## Test results (final)

- `dotnet build Nitra.sln` — **0 warnings, 0 errors**.
- `Tests/CSharpGrammarTests` — **84 passed / 0 failed** (baseline 69; +15 new methods,
  2 boundary tests renamed/extended, 2 StartPos methods extended).
- `Tests/ParserTests` — **310 passed / 2 skipped** (identical to baseline; the uncommitted
  T1.3.1 files stay green — `GetAll()` now contains the two raw terminals, which the C# 1.0
  grammar simply does not reference).
- Uncommitted T1.3.1 files (`Cs1.grammar`, `CsNitraVisitor.cs`, `CSharpParserTests.cs`,
  checklist, progressT1.3.1) untouched.

## Roslyn surprises

1. **Empty raw strings are impossible/illegal in all forms.** Single-line: the opening
   quote run is maximal, so `""""""` is N=6 unterminated, not 3+3 empty (CS8997).
   Multi-line: zero content lines is CS9002. A raw string always has non-empty content.
2. **`$"""{x}"""` (1 dollar) is raw-interpolated, and `{{` in it is an ERROR (CS9006),
   not a `{{`-escape.** The task checklist's "`$"""{x}""" (1 dollar → 1-brace holes,
   `{{` literal)" is wrong per `HandleOpenBraceInRawContent` `:914-922` (K=2 ≥ 2D=2 →
   error); corroborated by `RawInterpolatedStringLiteralParsingTests.cs:156-171`
   (`$"""{{0}}"` → CS9006). There is NO doubled-brace escape in raw-interpolated content:
   a literal `{` requires D ≥ 2 and a single `{` in content.
3. **Plain raw tokens are re-scanned by the interpolated scanner at parse time**
   (`LanguageParser_InterpolatedString.cs:16-30,165-173`), which is how `"""\n"""` gets
   CS9002 even though `ScanRawStringLiteral` itself reports nothing.
4. **Excess close quotes stay in the token** (`""""""`-style runs are consumed whole,
   CS8998 on the excess; `Lexer_StringLiteral.cs:585-602,632-648`) — so our scanner
   returns the extent through the entire run instead of -1 (decision #3).
5. **A mid-content-line quote run ≥ N in a multi-line raw string ends the literal**
   (CS9000 `ERR_RawStringDelimiterOnOwnLine`, `:617-629`;
   `RawStringLiteralLexingTests.cs:73-74`) — same extent rule as (4).
6. **`$$"x"` (1-2 quotes after dollars) is a broken token in Roslyn** (CS1005
   `ERR_NotEnoughQuotesForRawString`, scanner continues with N=1/2,
   `Lexer_StringLiteral.cs:494-498`) — our scanner fails fast with -1 (decision #3).

## Deviations (final)

1. **Task example length**: "`RawStringLiteral().TryMatch('\"\"\"abc\"\"\"', 0)` → 8" is a
   miscount — `"""abc"""` is 9 characters; the scanner correctly returns 9 (harness
  -verified). `"""ab"""` is the 8-char form (covered by the StartPos test).
2. **Error cases where Roslyn still closes the token** (excess close quotes CS8998,
   mid-content-line delimiter CS9000) are MATCHES (extent through the whole quote run) in
   our scanner, while structural/shape errors (unterminated, empty multi-line, brace-run
   violations CS9004/9005/9006/9007, < 3 quotes after dollars) are -1. Rationale in
   decision #3: the close-quote extent is well-defined and the longest-match-friendly;
   the rest follow the T1.2.3/T1.2.4 fail-fast convention with the same accept/reject
   outcomes as Roslyn.
3. **CS9002 (empty multi-line) is a scan failure (-1) for us**, even though Roslyn
   lexes the token: it is a lexical-shape constraint and Roslyn's own raw-interpolated
   scanner records it as a scanner error (`:719-739`).
4. `$$"x"` / `$$$""` → -1 (fail-fast) where Roslyn records CS1005 and keeps scanning.

## Status

- [x] Research (anchors above)
- [x] Baseline green (69 + 310/2 pre-change)
- [x] Core raw entries + content-loop raw branches + terminal rewiring
- [x] Tests (84 + 310/2 post-change, harness-verified lengths)
- [x] Full build 0 warnings; CSharpGrammarTests + ParserTests green
- [x] T1.3.1 uncommitted files untouched
