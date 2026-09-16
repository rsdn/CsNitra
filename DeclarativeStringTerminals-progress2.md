# Progress: DeclarativeStringTerminals P2 — StringLiteral/VerbatimStringLiteral as grammar rules

Task: replace imperative terminals `StringLiteral` / `VerbatimStringLiteral` in
`Parsers/CSharp/CSharpGrammar/CSharpTerminals.cs` with Cs1.grammar rules + new `[Regex]`
terminals (`StringEscape`, `StringText`, `NonQuoteText`).

## Status: DONE

- [x] Read CSharpTerminals.cs, Cs1.grammar, Cs6.grammar, StringLiteralScanner.cs,
      RuleGenerator, TypeChecking (Scope/Context/Resolver/Visitor), TerminalGenerator,
      RegexParser/NfaBuilder/DfaInterpreter
- [x] Engine capability probes (scratch apps in Temp\opencode: regexcheck, roslynprobe)
- [x] Cs1.grammar: 4 rules inserted after `UnarySign`, verbatim per task text, CRLF kept
- [x] CSharpTerminals.cs: +3 [Regex] terminals, -2 factories, -2 fields, -2 records,
      GetAll(): -StringLiteral()/VerbatimStringLiteral() (forced by compilation — see Notes),
      +StringEscape()/StringText()/NonQuoteText()
- [x] Byte-level verification of all 3 attribute strings (PowerShell, char codes): ALL EXACT
- [x] `dotnet build CSharpGrammar.csproj --no-incremental` — **0 errors, 0 warnings**
- [x] gencheck build — 3 new `// Code generated for regular expression:` comments verified
      byte-exact (187/11/5 chars); StringText matcher condition verified
      `!(c == '"' || c == '\\' || c == '\n' || c == '\r') && c != '\0'`
- [x] All gencheck folders deleted recursively from repo root (5 folders: CSharpGrammar,
      CsNitraGrammar, ExtensibleParser, Regex/Regex, TerminalGenerator)
- [x] `dotnet build Tests/CSharpGrammarTests.csproj --no-incremental` — FAILED as expected:
      19× CS0117, all in `Tests/CSharpGrammarTests/CSharpTerminalsTests.cs`:
      - `CSharpTerminals` does not contain a definition for `StringLiteral` — lines 155, 175,
        217, 250, 292, 320, 332, 702, 716 (tests StringLiteral_QuotedText_Matches,
        StringLiteral_SimpleEscapes_Matches, StringLiteral_UnicodeEscapes_Matches,
        StringLiteral_InvalidEscape_Fails, StringLiteral_UnterminatedOrRawNewline_Fails,
        StringLiteral_RawStringQuotes_Fails, StringLiteral_AtInterpolatedPrefix_Fails,
        StartPos_StringLiterals_MatchFromThatPosition, StartPos_MisalignedStart_Fails)
      - `CSharpTerminals` does not contain a definition for `VerbatimStringLiteral` — lines
        524, 538, 554, 566, 580, 591, 598, 610, 705, 719 (tests VerbatimStringLiteral_* ×8,
        StartPos_* ×2)
      NOT fixed (per task — next subagent rewrites the tests).

## Engine findings (scratch probes, verified empirically 2026-09-16)

1. **`(?:` is NOT supported by RegexParser.** `parseGroup` only handles `(`...`)`; in `(?:`
   the `?`/`:` parse as literal chars glued to the first alternative → StringEscape would
   reject ALL plain escapes (`\"`, `\'`, `\\`, `\0`, `\a`..`\v` → -1) and the `\U` 0x0..0xFFFF
   range. Plain groups `(...)` work (precedent: `CharLiteral` = `"'([^'\n\\]|\\.)'"`).
   => StringEscape uses plain groups.
2. **A `\` before a raw LF inside a char class is an escape introducer, not a class member** —
   the task value-block's single-backslash StringText would drop the backslash from the
   excluded set (probe: `abc\d` matched through the negated class) → invalid escapes like
   `\q` would become accepted text (reject→accept regression). A literal backslash class
   member needs `\\` in the pattern value.
3. **The pattern value must be free of raw control chars.** TerminalGenerator interpolates the
   value into a `//` comment and into char literals (`c == '<char>'`). Roslyn treats
   U+0085, U+2028, U+2029 (and CR/LF) as LINE TERMINATORS (roslynprobe: all three terminate
   `//` comments and break char literals). The generator emits U+2028/U+2029 RAW (its
   `escapeChar` only escapes `char.IsControl`), so any pattern containing them (or CR/LF)
   yields uncompilable generated code. The engine has no `\uXXXX` escape →
   **U+0085/U+2028/U+2029 are inexpressible in a pattern value at all.**
   => StringText excludes only `"`, `\`, LF, CR; LF/CR written as regex escapes `\n`/`\r`
   (printable in the value).

## Pattern values actually implemented (resolved regex text, verified byte-exact in BOTH
the C# attributes and the generated comments)

- StringEscape (187 chars), C# source `"\\\\([\"'\\\\0abfnrtv]|x[0-9a-fA-F]+|u[0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f]|U00(0[0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f]|10[0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f]))"`:
  `\\(["'\\0abfnrtv]|x[0-9a-fA-F]+|u[0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f]|U00(0[0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f]|10[0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f]))`
  Semantic probe (21 cases): `\"` `\'` `\\` `\0` `\a` `\b` `\f` `\n` `\r` `\t` `\v` → 2;
  `\x1F` → 4; `\x123456` → 8 (unbounded, D4); `\u0041` → 6; `\u12` → -1; `\U0001F600` → 10;
  `\U0010FFFF` → 10; `\U00110000` → -1; `\U000FFFFF` → 10; `\q` → -1; lone `\` → -1. All correct.
- StringText (11 chars), C# source `"[^\"\\\\\\n\\r]+"`:
  value `[^"\\\n\r]+` (regex-level: `\\` = backslash member, `\n`/`\r` = LF/CR members).
  Class = { `"`, `\`, LF, CR }. Probe: `abc` → 3; stops at LF/CR/`"`/`\` (→ 2/2/3/3);
  U+0085/U+2028/U+2029 → matched as text (see deviation D5).
- NonQuoteText (5 chars), C# source `"[^\"]+"`: value `[^"]+`. Class = { `"` }.
  Probe: backslash/newlines/quotes handled as intended for verbatim content.

## Deviations (to be recorded in the plan checklist)

- **D5 (NEW)**: StringText excludes only `"`, `\`, LF, CR — NOT U+0085/U+2028/U+2029.
  Those three are inexpressible in a pattern value (engine has no `\u` escapes; raw chars
  break the generator's output — Roslyn line terminators in comment/char literals).
  Consequence: a plain string containing a raw U+0085/U+2028/U+2029 (e.g. `"ab<U+2028>cd"`)
  is ACCEPTED by the grammar, while the old imperative scanner rejected it (CS1010 class,
  `StringLiteralScanner.IsNewLine`). Same category as D2 (grammar more permissive on
  newline handling, valid code unaffected) — reject→accept only for the 3 rare Unicode
  newlines. Test-rewrite subagent should convert the corresponding "reject" unit cases to
  documentation tests (like D2's §6.3).
- **StringEscape `(?:` → `(`** (two places): engine-forced (finding 1); the task value block
  contradicted the task's own engine capability statement ("supports ()").
- **StringText value-block notation**: the task's "value block" (`[^"\\\n\r\u0085...]`,
  single backslash + raw U+0085/2028/2029) is engine-incompatible on two counts (findings 2, 3);
  the implemented value matches the block's regex-escape NOTATION for `\\`/`\n`/`\r` minus
  the inexpressible U+0085/2028/2029 (D5).

## Notes

- `RuleGenerator.GenerateRuleRefExpression` binds a ref to a TERMINAL when one exists in
  scope, else to a rule ref — so removing the 2 terminals from `GetAll()` is REQUIRED for
  `Constant`/`Primary` refs to bind to the new rules (type checker already prefers rules).
  The 2 `GetAll()` entries calling the deleted factories had to be removed (compilation);
  task's "НИЧЕГО не удалять из GetAll()" read as "nothing beyond the deleted factories'
  own entries".
- StringLiteralScanner.cs untouched per task. `TryScanPlainString`/`TryScanVerbatimString`
  are now unused (public static → no warning); they die with the file in P3/P4.
- Forward refs in grammar are fine (declarations collected first; runtime Ref by name).
  `UnarySign` was already forward-referenced by `Constant`.
- gencheck side effect: `-p:` global properties made referenced projects (CsNitraGrammar,
  ExtensibleParser, Regex, TerminalGenerator) emit gencheck folders too; if a gencheck build
  fails, its emitted .cs files get compiled by the NEXT build (duplicate-definition errors)
  — always delete gencheck folders after a failed gencheck build.
- Pre-existing working-tree change (not mine): `Tests/CSharpGrammarTests/CSharpGrammarTests.csproj`
  (missing trailing newline, present before P2).
