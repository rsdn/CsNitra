# Progress: declarative [Regex] conversion of 3 C# string terminals

Task: replace imperative terminals in `Parsers/CSharp/CSharpGrammar/CSharpTerminals.cs`:
- `InterpolatedRegularText` -> `[Regex]` value `[^\{\}\\"]+`
- `InterpolatedVerbatimText` -> `[Regex]` value `[^\{\}"]+`
- `RawFormatText` -> `[Regex]` value `[^}]*`

## Status: DONE

- [x] Read CSharpTerminals.cs, TerminalGenerator, csproj
- [x] Verified `TryScanNonBraceQuoteRun` has no users outside CSharpTerminals.cs
- [x] Added 3 declarative declarations after `InterpolatedRawText` (now lines 37-47)
- [x] Removed 3 factories, 3 fields, 3 records, `TryScanNonBraceQuoteRun`
- [x] Byte-level verification of the 3 attribute literals (char codes)
- [x] `dotnet build CSharpGrammar.csproj --no-incremental` — 0 errors, 0 warnings
- [x] EmitCompilerGeneratedFiles build (gencheck) — verified 3 patterns in
      `CSharpGrammar.CSharpTerminals.g.cs`, byte-exact:
      - `InterpolatedRegularTextMatcher`: `[^\{\}\\"]+` (11 chars)
      - `InterpolatedVerbatimTextMatcher`: `[^\{\}"]+` (9 chars)
      - `RawFormatTextMatcher`: `[^}]*` (5 chars)
- [x] Deleted `gencheck` from CSharpGrammar AND from CsNitraGrammar
      (the -p: global properties flowed to the referenced project too)
- [x] `dotnet build Tests/CSharpGrammarTests.csproj --no-incremental` — 0 errors
- [x] `dotnet test CSharpGrammarTests --no-build` — 279/279 passed

## Problems / notes

1. First edit of the InterpolatedRegularText attribute dropped the char-class
   `]` and collapsed `""` to `"` (edit-tool quote handling). Caught by the
   mandatory byte-level check; fixed via exact char-code replacement
   (PowerShell, no BOM, CRLF preserved). Lesson: the verbatim source for
   value `[^\{\}\\"]+` is `@"[^\{\}\\"]+"` (two backslashes + doubled quote),
   NOT `@"[^\{\}\\\\"]+"` (4 backslashes) as the task snippet suggested —
   the authoritative spec is the value string.
2. Behavioral nuance (per task spec, accepted): `[^}]*` matches empty (0) at
   ANY position, while the old imperative `RawFormatTextTerminal` returned -1
   when no `}` existed in the remainder. In the grammar RawFormatText always
   follows `:` inside a hole, so a real `}` (or end-of-hole error) follows;
   all 279 parse tests (incl. malformed-input cases) stay green.
3. Pre-existing working-tree changes (not mine, untouched):
   `Tests/CSharpGrammarTests/CSharpGrammarTests.csproj` (missing trailing
   newline), `DeclarativeStringTerminals-checklist.md` (untracked).

## Тесты P1

Full-suite verification, 2026-09-16, from repo root:
`dotnet test Nitra.sln --no-restore` (build inside, no restore needed).

**Result: GREEN — 0 failures.**

| Project | Passed | Failed | Skipped | Total |
|---|---|---|---|---|
| WiWorkflowTests | 1 | 0 | 0 | 1 |
| ParserTests | 325 | 0 | 2 | 327 |
| CSharpGrammarTests | 279 | 0 | 0 | 279 |
| RegexTests | 9 | 0 | 0 | 9 |
| **Total** | **614** | **0** | **2** | **616** |

- Skipped (pre-existing, unrelated to the change): `GrammarValidationTests.ShouldReportErrorForUndefinedRuleReference` and `GrammarValidationTests.RequiredSubruleNamesAreNotSpecified` — both carry `[Ignore("WIP")]` (Tests/ParserTests/CsNitra/GrammarValidationTests.cs:11,43).
- No failures, so no failure analysis. The accepted semantic nuance (note 2: `RawFormatText` = `[^}]*` allows a 0-length match where the old imperative terminal returned -1) stays covered — all 279 CSharpGrammarTests parse tests, incl. malformed-input cases, are green.
- Build noise, no impact: MSB3026 copy-retry warnings for `testhost.dll` / `Microsoft.TestPlatform.CoreUtilities.dll` — parallel testhost processes locked the shared central `bin\Debug\net8.0` output; MSBuild retried and succeeded.
- `git status` check: only the expected entries — M `Parsers/CSharp/CSharpGrammar/CSharpTerminals.cs`, M `Tests/CSharpGrammarTests/CSharpGrammarTests.csproj` (pre-existing trailing newline), untracked `DeclarativeStringTerminals-checklist.md` and `DeclarativeStringTerminals-progress1.md`. Nothing unexpected.
