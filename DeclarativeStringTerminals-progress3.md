# Progress: DeclarativeStringTerminals P3 (rework) + P4 — raw string, formats, escape as [Regex]

Task: finish the hand-written terminal removal. P3 as first implemented (RawStringLiteral as a
rule over RawQuoteRun/RawQuoteContent/NonQuoteText) was reworked into a single [Regex] terminal
per the Nitra-style pattern; P4 (RegularFormatText/VerbatimFormatText/InterpolatedRegularEscape)
converted the same way; StringLiteralScanner.cs deleted.

## Status: DONE

- [x] RawString [Regex] terminal: `"""+("|""[^"]|[^"])+"""+` (multi-line raw string in the
      attribute — the value starts/ends with 3-quote runs, a 3-quote raw string cannot hold it)
- [x] Cs11.grammar: `RawStringLiteral = RawString;` (wrapper rule — Parser.Parse start rule must
      be a rule) + `Primary = | RawStringLiteral;` (ref binds to the rule)
- [x] InterpolatedRegularEscape [Regex] — same set as StringEscape (D7: hex value not checked)
- [x] RegularFormatText [Regex] `([^}"\\]|\\(...escape...))*` — run up to '}'
- [x] VerbatimFormatText [Regex] `([^}"]|"")*` — run up to '}', exact '""' pairs
- [x] CSharpTerminals.cs: -4 factories/fields/records (RawQuoteContent, InterpolatedRegularEscape,
      RegularFormatText, VerbatimFormatText) -1 [Regex] (RawQuoteRun) +3 [Regex] (RawString,
      InterpolatedRegularEscape, RegularFormatText, VerbatimFormatText); GetAll() updated
- [x] StringLiteralScanner.cs DELETED (all methods dead: TryScanPlain/Verbatim/Raw from P2/P3,
      TryScanEscape with the last escape terminals)
- [x] RawOpenBraceLiteral/RawCloseBraceLiteral/RawHoleOpenBraces KEPT (D1 — see below)
- [x] Tests: RawStringLiteralRuleTests (N=4 content run 3 flipped reject→accept — now
      scanner-consistent), InterpolatedStringTests (\u007B reject→D7 doc-test accept)
- [x] rawsmoke matrix: ALL EXPECTATIONS MET (17 parse / 11 fail / 6 info / 4 full-grammar)
- [x] `dotnet test Nitra.sln`: 616 passed, 5 skipped (pre-existing [Ignore]), 0 failed
- [x] `dotnet build Nitra.sln -c Release --no-incremental`: 0 errors

## Why the P3 rework (rule over parts → single match)

The first P3 design (`RawStringLiteral = RawQuoteRun RawLiteralPart* RawQuoteRun` with a
hand-written RawQuoteContent for maximal 1..2 quote runs) worked but kept a hand-written
terminal for the one inexpressible piece. The Nitra reference grammars
(c:\RSDN\nitra\Grammars\CSharp\...Literals.nitra) show the shape: a literal is ONE pattern with
character-level alternation (`(!NotAllowed Any)+`). Without lookahead the equivalent is the
quote-part alternation: every content quote is a `"` or a `""` followed by a non-quote, so a
3+ run can only be the closing. One [Regex] match:

- no intra-match trivia skip (a OneOrMany quote-literal source is broken by it — the
  post-terminal skip merges whitespace-separated runs, which is why `context("\""+, ...)`
  was abandoned);
- comments/newlines inside the literal stay content (single match);
- kills BOTH RawQuoteRun and RawQuoteContent;
- scanner-faithful on the N>=4 content run 3..N-1 case (old design rejected it, scanner 15 —
  now 15/15);
- `+` (1+ content) keeps `""""""` (6 quotes, empty body) rejected like the old scanner;
  9 quotes are accepted (open 3 + content 3 + close 3) — D3 note.

CsNitra has no Nitra raw-string implementation to copy (Nitra grammar stops at C# 7; repo-wide
grep for `"""`/RawString: no hits) — the pattern above is the lookahead-free equivalent of
Nitra's `(!"""+ Any)+` idiom.

## D1 proof (brace terminals stay hand-written)

Parser.cs:679: `var count = repeat.Count ?? ContextCount;` — the only repeat forms are an exact
`{N}` or the context count `{n}` (used by Cs6.grammar hole: `"}"{n}`). No range repeat
(1..D-1, D-1..2D-2, D..2D-1) exists in the grammar language, and D is dynamic (any `$` depth,
one parameterized rule). Expressing the three brace ranges would need a new engine feature
(repeat with context bounds) — Stage 5, out of scope ("парсер не меняем").

## Pattern values (resolved regex text)

- RawString: `"""+("|""[^"]|[^"])+"""+`
- InterpolatedRegularEscape: `\\(["'\\0abfnrtv]|x[0-9a-fA-F]+|u hex4|U00(0 hex5|10 hex4))`
  (identical to StringEscape)
- RegularFormatText: `([^}"\\]|\\(same escape))*`
- VerbatimFormatText: `([^}"]|"")*`

All values free of raw control chars and 3+ quote runs inside 3-quote raw strings (generator
interpolation safety, progress2 finding 3); RawString uses the 4-quote multi-line form.

## Rule-level semantics check (formats)

The old imperative terminals returned -1 on a lone `"` / invalid escape (whole run fails); the
regex terminals stop the run there (prefix match). In the hole rules (`... Format? "}"`) the
trailing `'}'` then fails at the same char, so rule-level accept/reject is identical; only the
failed-parse consumption differs (recovery shape). VerbatimFormatText pair-before-`'}'`
(`$@"{x, ""}"`) is accepted — the bare `""` part matches, then the rule's `'}'` does (a
`""[^}]`-style follower alternative would have broken this valid case).

## Deviations (recorded in the checklist)

- D7 (NEW): escapes resolving to '{'/'}' accepted (D4 category, reject→accept, invalid code
  only). Excluding the finite \u007B/\U0000007B forms was rejected: the \x family (1-4 hex
  digits, leading zeros) is the same code point and the enumeration would bloat the pattern 3x
  while leaving an asymmetric hole.
- D3 (extended): 9-quote raw string accepted (scanner: -1).
- D2 (clarified): for RawString the newline acceptance is [^"] inside one match, not trivia.
- Fixed vs old P3 design: N>=4 content run 3..N-1 now matches (was a reject deviation).

## Notes

- `RawString` terminal name vs rule `RawStringLiteral`: the wrapper rule keeps the test start
  rule and the Primary reference stable; parse tree = RawStringLiteral → RawString.
- rawsmoke prefix cases: `"""a""""b"""` now matches 12/12 (old design: 8/12 — the regex
  consumes past the first closing run). Prefix match-length semantics have no grammar/terminal
  equivalent either way — documented in the [Ignore]d test.
