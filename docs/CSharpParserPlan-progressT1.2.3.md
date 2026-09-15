# T1.2.3 Progress — String literal core (regular family)

## Architecture decision: custom `Terminal` with hand-written recursive `TryMatch`

Chosen over grammar rules. Rationale:

1. **Token boundary is a lexical concern.** Roslyn's own architecture makes this exact
   choice: the lexer scans the *entire* interpolated string token (including all holes,
   nested strings, comments) before the parser sees it; the parser later re-parses the
   token's interior from its text (`LanguageParser_InterpolatedString.cs:126-190`,
   `Parser/Lexer_StringLiteral.cs:254-296`). If holes were grammar rules, the hole body
   ("almost any C# source") would have to be *parsed by the grammar* for the outer string
   rule to succeed — but Cs1.grammar has no expression grammar yet, and even with one,
   longest-match over `Alt` would make the outer literal fail whenever the hole's
   expression is not yet parsable, even though the literal is lexically well-formed.
2. **Balanced-brace scanning with nested literals is inherently a scanner**, not a rule
   composition: `{` inside a hole must not start a grammar rule; it must start a
   *balance count* that skips strings/chars/comments. That is exactly what Roslyn's
   `ScanInterpolatedStringLiteralHoleBalancedText` does, and it ports 1:1 into a
   recursive method.
3. `Terminal.TryMatch` gives the precise `-1` / `chars-consumed` contract the parser
   engine expects (`ExtensibleParser/Rules.cs:50`), same as the existing `TriviaTerminal`.

Names referenced by `Cs1.grammar` keep working: the new plain-string terminal keeps
`Kind == "StringLiteral"` (grammar resolves terminals by `Kind`, see
`CsNitraGrammar/TypeChecking/TypeCheckingContext.cs:22-25`). `VerbatimStringLiteral`
untouched (still the regex terminal; T1.2.4 rewires it to the core).

## Core shape

New file `Parsers/CSharp/CSharpGrammar/StringLiteralScanner.cs` — `internal static class
StringLiteralScanner`, mirroring Roslyn's `InterpolatedOrRawStringScanner`
(`Lexer_StringLiteral.cs:344-1170`) structure:

- `StringKind` enum: `Normal`, `Verbatim`, `SingleLineRaw`, `MultiLineRaw`
  (mirrors `Lexer.InterpolatedStringKind`, `Lexer_StringLiteral.cs:319-337`). Raw values
  are reserved; no code path constructs them yet.
- Entry points (public to the assembly, used by the terminals and by T1.2.4/T1.2.5):
  - `TryScanPlainString(input, pos)` — pos at `"`; dispatches `"""` to the raw hook.
  - `TryScanInterpolatedString(input, pos)` — pos at `$`; handles `$"`, `$@"`; `$$` → raw hook.
  - `TryScanVerbatimString(input, pos)` — pos at `@`; `@"`. T1.2.4 rewires the
    `VerbatimStringLiteral` terminal here.
  - `TryScanAtString(input, pos)` — pos at `@`; `@"` or `@$"` (used by hole scanning).
- Shared recursive core (private):
  - `TryScanStringContents(input, pos, kind, dollarCount, quoteCount)` — the one content
    loop for all families; `quoteCount` is carried for raw (T1.2.5) and is 1 now.
  - `TryScanHoleBalancedText(input, pos, endingChar, isHole, kind, dollarCount, quoteCount)`
    — Roslyn `ScanInterpolatedStringLiteralHoleBalancedText` (`:1022-1141`) ported; used
    for holes (`'}`, isHole=true) and for `()[]{}` bracket pairs inside holes.
  - `TryScanBracketed` — Roslyn `ScanInterpolatedStringLiteralHoleBracketed` (`:1156-1169`).
  - `TryScanFormatSpecifier` — Roslyn `ScanFormatSpecifier` (`:959-1017`).
  - `TryScanEscape` / `TryScanHexEscape` — Roslyn `ScanEscapeSequence` +
    `ScanUnicodeEscape` ported (see anchors below).
  - `TryScanNestedLiteral`, `TryScanCharLiteral` — string/char literals inside holes.
  - `TryScanBlockComment` — nested `/* */` (same shape as `TriviaTerminal`).
- **Extension points for T1.2.5 (raw)**: `TryScanRawString(input, pos)` (pos at first of
  ≥3 quotes) and `TryScanRawInterpolatedString(input, pos)` (pos at first of ≥2 `$`),
  both currently `=> -1`; plus the raw branches inside `TryScanStringContents`
  (`"` run / `{` / `}` cases when kind is a raw kind). T1.2.5 fills these without
  touching the Normal/Verbatim paths. **T1.2.4 (verbatim)**: the core already fully
  supports kind=Verbatim (used today for `@$"`/`$@"` inside holes); the terminal rewiring
  is a one-line change to `VerbatimStringLiteral()`.

## Fail-fast vs Roslyn recovery

Roslyn records the first error and *keeps scanning* (with `RecoveringFromRunawayLexing`
short-cuts, `Lexer_StringLiteral.cs:1148`, `:777-780`) to find the closing quote and emit
one token. `TryMatch` has no diagnostics channel, so the core **fails fast: first error
→ -1**. Same accept/reject outcome for all well-formed/ill-formed literals; only error
*reporting* differs (parser-level, not our concern). Documented deviation.

## Roslyn anchors (read, not web)

- Prefix dispatch: `Parser/Lexer.cs:610-632` (`case '@'` → `TryScanAtStringToken`
  `:736-760`), `Lexer.cs:634-645` (`case '$'` → `TryScanInterpolatedString` `:762-778`:
  next char must be `$`/`@`/`"`), `Lexer.cs:442` (`"` → `ScanStringLiteral`),
  raw dispatch on `"""` in `Lexer_StringLiteral.cs:19-33`.
- Regular string body + escape set: `Lexer_StringLiteral.cs:14-116`
  (`ScanStringLiteral`), `:129-190` (`ScanEscapeSequence` — escape set `\' \" \\ \0 \a \b
  \f \n \r \t \v` plus `x/u/U`; `\e` is a C# 13 feature-gated extra, **excluded** here to
  match the task's escape list for this C# 1-era grammar), `Lexer.cs:4630-4725`
  (`ScanUnicodeEscape`: `\x` 1–4 hex, `\u` exactly 4, `\U` exactly 8 and ≤ 0x0010FFFF),
  `Lexer.cs:4839-4844` (illegal-escape diagnostic), `CharacterInfo.cs:149-163`
  (`IsNewLine`: `\r \n \u0085 \u2028 \u2029`).
- Interpolated scan: `Lexer_StringLiteral.cs:380-404` (`ScanStringLiteralTop`),
  `:415-520` (`ScanOpenQuote` — `($"`/`$@"`/`@$"` prefix forms), `:653-717`
  (`ScanInterpolatedStringLiteralContents` — `"`/`{`/`}`/`\` cases, newline restriction
  via `IsAtEnd(kind)` `:361-372`), `:769-816` (`IsEndDelimiterOtherwiseConsume` — verbatim
  `""` doubling), `:818-853` (`HandleCloseBraceInContent` — `}` must be doubled,
  else ERR_UnescapedCurly), `:867-894` (`HandleOpenBraceInNormalOrVerbatimContent` — `{{`
  literal, else hole; unclosed hole → ERR_UnclosedExpressionHole),
  `:959-1017` (`ScanFormatSpecifier` — first top-level `:`; `{` is an error inside a
  format; ends at `}`/`"`), `:1019-1141` (hole balanced text — `#` error, `$`/`@` nested
  strings, `"`/`'` nested literals, `//`+`/* */` comments, `{(`[` bracket pairs, stray
  `)`/`]` at hole top level are errors, newlines always allowed in holes),
  `:1150-1154` (nested string = full `ScanStringLiteral`, so `"""` inside a hole is a raw
  string).
- Raw strings (for extension design only, not implemented): `Lexer_RawStringLiteral.cs:51-178`,
  `Lexer_StringLiteral.cs:476-517` (open quote: quote run ≥3, multi-line decision),
  `:567-651` (close: quote run ≥ starting count), `:798-816` (content quote runs),
  `:896-957` (raw hole braces: N-dollar rule).
- Test case mining: `Test/Syntax/LexicalAndXml/LexicalTests.cs:1098-1214` (unicode
  escapes, `\u12` illegal), `Test/Syntax/LexicalAndXml/LexicalErrorTests.cs:1008-1898`
  (verbatim strings / comments / holes spanning lines — `}` inside `/* */` does NOT close
  the hole; `@" "` inside a hole closes at first non-doubled `"`),
  `Test/Syntax/Parsing/RawInterpolatedStringLiteralParsingTests.cs:309-679`
  (`{'}'}`, `{"}"}`, `{@"}"}`, nested `$`/`$@` strings in holes all lex fine).

## Decisions / deviations (log)

1. **Fail-fast** instead of Roslyn's scan-through-recovery (see above).
2. **`\e` excluded** (C# 13 feature-gated in Roslyn; not in the task's escape set).
3. **`\U` range check kept**: code point > 0x0010FFFF → invalid (Roslyn
   `Lexer.cs:4672-4678`).
4. **`#` inside a hole → -1** (Roslyn `ERR_SyntaxError`, `Lexer_StringLiteral.cs:1039-1043`).
5. **Stray `)` / `]` at hole top level → -1** (Roslyn `:1065-1074`).
6. **Escaped curly → -1**: `\u007B`/`\x7D`/`\U0000007B` etc. inside an interpolated
   string (Roslyn `ERR_EscapedCurly`, `:699-702`, `:982-985`).
7. **Razor comments (`@*`) not handled** in holes — `@` falls back to plain char
   (Roslyn special-cases razor; out of scope).
8. **`$$"..."` (raw interpolated) and `"""..."""` (raw) → -1** until T1.2.5 (stubs).
9. UTF8 string suffix (`"..."u8`, C# 11) not modeled — out of era.

## Implementation log

- Core: `Parsers/CSharp/CSharpGrammar/StringLiteralScanner.cs` (new, `internal static class`,
  ~500 lines). Terminals: `CSharpTerminals.StringLiteral()` now returns a custom
  `StringLiteralTerminal` (Kind `"StringLiteral"`, unchanged for `Cs1.grammar`); new
  `CSharpTerminals.InterpolatedStringLiteral()` (Kind `"InterpolatedStringLiteral"`) added to
  `GetAll()`. `VerbatimStringLiteral` untouched.
- **Bug found during verification (would have shipped)**: the scanner entry points initially
  returned the absolute end position; the `Terminal.TryMatch` contract is *relative* chars
  consumed. All `startPos=0` tests passed, but the grammar sanity check
  (`[Attr("x")] class C { }`, string at pos 6) failed at pos 15. Fixed with a `ToLength`
  conversion at the 4 public entry points; the hole scanner advances by `pos += end`.
  Added `StartPos_StringLiterals_MatchFromThatPosition` as regression coverage.
- **Second convention bug**: `TryScanNestedLiteral` mixed absolute (char literal) and
  relative (plain string) returns — fixed via `ToLength` on the char path.
- **API boundary decided**: `InterpolatedStringLiteral` matches only input starting with `$`
  (per task spec: "the `$` prefix char is part of the match"). `@$"..."` (at-prefix order)
  therefore returns -1 from this terminal; it belongs to the verbatim family — T1.2.4 will
  make `VerbatimStringLiteral` accept both `@"` and `@$"`. Core already supports it
  (`TryScanAtString`, used by hole scanning today). Documented in test
  `InterpolatedStringLiteral_AtPrefixOrder_FailsUntilT124`.

## Test results (final)

- `dotnet build Nitra.sln` — 0 warnings, 0 errors.
- `Tests/CSharpGrammarTests` — **59 passed / 0 failed** (baseline was 41; +17 terminal tests,
  +1 parser test).
  - StringLiteral: 7 methods — QuotedText (all plain forms), SimpleEscapes (all 11 C# escapes),
    UnicodeEscapes (`\x` 1–4, `\u` 4, `\U` 8 incl. surrogate pair + 0x10FFFF boundary),
    InvalidEscape (`\$ \q \e \x \xZZ \u \u12 \u12G4 \U \U1234567 \U12345678`),
    UnterminatedOrRawNewline (incl. lone `\` at EOF, `\x` at EOF, LF/CR),
    RawStringQuotes_FailsUntilT125 (`"""x"""` → -1), AtInterpolatedPrefix_Fails.
  - InterpolatedStringLiteral: 11 methods — PrefixForms (`$""`/`$"abc"`/`$@"…"`),
    AtPrefixOrder_FailsUntilT124 (`@$"` → -1), HolesAndEscapedBraces (`{{x}}`, `{{{some}}}`,
    nested braces, newlines in holes, line/block comments, `}` inside `/* */` ignored),
    NestedLiteralsInHole (`{s = "}"}"`, `{c = '}'}"`, `{ @"a b" }`, `{ $"a{b}c" }`,
    `{ $@"x{y}z" }`, `$@"a""b"`), EscapesInContent, FormatSpecifier (`{x:0}`, `{x:}`),
    InvalidHoleOrBrace (unescaped `}`, unclosed holes incl. task cases `{"inner "}"` and
    `{"a" + {b}"`, `#` in hole, stray `)`/`]`, `{` in format, unterminated comment),
    EscapedCurly (`\u007B`/`\x7D`/`\U0000007B`), UnterminatedOrRawNewline,
    RawInterpolated_FailsUntilT125 (`$$"x"""` → -1), BadPrefix.
  - StartPos: `StartPos_StringLiterals_MatchFromThatPosition` (startPos=2 for both families),
    `StartPos_MisalignedStart_Fails` extended to both families.
- `Tests/ParserTests` — **310 passed / 2 skipped** (identical to baseline).
- Full solution: WiWorkflowTests 1 passed, RegexTests 9 passed.
- Grammar sanity: `CSharpParser` + embedded `Cs1.grammar` parses
  `[Attr("x")] class C { }` to end of input with no errors — kept as permanent regression
  test `CSharpParserTests.Parse_AttributeWithStringConstant_Succeeds` (deviation from
  "throwaway", see below).

## Roslyn semantics surprises worth knowing

1. `$"{"a" + {b}"` (task: "weird but balanced") is **not** valid: the final `"` opens a nested
   string that runs to EOF → ERR_UnclosedExpressionHole → our -1.
2. A stray `)` or `]` at hole top level is a lexer error in Roslyn
   (`Lexer_StringLiteral.cs:1065-1074`), not just a parser error — ported as -1.
3. `{` inside a format specifier is a lexer error (`:998-1003`) — `$"{x:{}}"` → -1.
4. `}` inside a `/* */` comment in a hole does NOT close the hole (comment is consumed whole,
   `:1110-1122`) — ported; unterminated comment → -1.
5. Roslyn's `\U` escape additionally rejects code points > 0x0010FFFF (`Lexer.cs:4672-4678`) —
   ported (`"\U12345678"` → -1).
6. Newlines are forbidden in the *text* portion of a Normal interpolated string but always
   allowed *inside holes* and in format specifiers (`:361-372`, `:1031`, `:1008`) — ported.
7. Roslyn keeps scanning after the first hole/escape error (recovery via
   `RecoveringFromRunawayLexing`); we fail fast — same -1/accept outcome, see decision #1.

## Deviations (final)

1. Fail-fast instead of Roslyn recovery scanning (documented above, decision #1).
2. `\e` escape excluded (C# 13 feature-gated in Roslyn; not in task's C# 1-era set).
3. Razor comments (`@*`) in holes not handled — `@` falls back to plain char.
4. `$$"…"` / `"""…"""` → -1 until T1.2.5 (stubs in place, marked in code).
5. `@$"…"` → -1 from `InterpolatedStringLiteral` (at-prefix order is the verbatim family,
   T1.2.4) — core already handles it via `TryScanAtString`.
6. UTF8 string suffix (`"…"u8`) not modeled (C# 11, out of era).
7. Sanity check kept as a permanent test instead of throwaway (one method, protects the
   `StringLiteral` wiring through `Cs1.grammar`'s `Constant` rule).

## Status

- [x] Research (anchors above)
- [x] Baseline build green (41 + 310 passed pre-change)
- [x] Implement core + terminals
- [x] Tests (59 + 310 passed post-change)
- [x] Grammar sanity check (kept as permanent test)
- [x] Full build + test green, 0 new warnings
