# T0.3.2 Progress — tests for multi-file grammar merging

## Plan

- [x] Read existing patterns (RuleGeneratorTests, CsNitraTests, CSharpParserTests, T0.3.1 progress).
- [x] Share `GetGrammarText()` from `RuleGeneratorTests` into `CsNitraGrammarText` (minimal change).
- [x] `Tests/ParserTests/CsNitra/GrammarMergeTests.cs` — merge semantics + metacircular test.
- [x] `Tests/CSharpGrammarTests/CSharpParserMultiTextTests.cs` — CSharpParser shell level.
- [x] Build + run both test projects.

## Decisions

- Terminals for merge tests: reuse `MiniC.Terminals.Trivia()` (`\s*`) — no new `[TerminalMatcher]` class needed;
  grammars use literals only, so the `terminals` list passed to `BuildFromTexts` is empty.
- Metacircular extension rule: `Using` (re-declared in file2 with `| Unused = "zzz";`).
  Safe because `Using` is only tried at statement starts (`Grammar = Usings=Using* Statements=Statement*`),
  and the literal `zzz` never matches at any statement start in the concatenated grammar text.
  The test also asserts `parser.Rules["Using"]` has 3 alternatives with the 3rd being `Literal("zzz")`
  (the actual merge proof — the concatenated-text parse succeeds regardless of the merge).
- Precedence merge test: files must share at least one precedence name (`Mid`) —
  `TryMergePrecedenceList` in `CsNitraTypeChecker` requires an intersection, otherwise it reports
  "Cannot merge precedence list" error. Merged order: [Left, Mid, Right] → binding powers 3, 2, 1.
- Diagnostics test: file2 = `Rule2 = UnknownThing;` → type error "Symbol 'UnknownThing' not found",
  position prefix must be `Cs2.grammar:`, not `Cs1.grammar:`.

## Deviations discovered while writing tests

- CsNitra `Alternative` rule does NOT allow anonymous literals in piped statements:
  `Rule = | "a";` fails to parse ("Expected: Identifier" at `|`). Alternatives must be
  `| Name = Expr` (NamedAlternative) or `| RuleRef` (AnonymousAlternative).
  All test grammars use named alternatives (`| A = "a" | B = "b"`).
- `CsNitraParser.Parse<T>` returns `ParseResult` (`Success<T>`/`Failed` union), not `Result` —
  the metacircular manual-parse sanity check uses `is Success<GrammarAst>` pattern.

## Results

- `dotnet build Nitra.sln` — 0 warnings, 0 errors.
- `dotnet test Tests/ParserTests --no-build` — 310 passed, 0 failed, 2 skipped (pre-existing [Ignore]).
  8 new tests, all green:
  - `AppendAlternative_ReDeclaredRule_AllAlternativesParse`
  - `Merge_SimpleWithSimpleSameName_BothParse`
  - `Merge_PipedWithSimple_AllParseInFileOrder`
  - `Merge_ThreeFiles_AlternativesInFileOrder`
  - `PrecedenceAcrossFiles_MergedLists_TdoppBuildsAndParses`
  - `BuildFromTexts_UnknownTerminalInSecondFile_ErrorNamesSecondFile`
  - `BuildFromAst_SingleText_BehavesAsBefore`
  - `Metacircular_MultiFileGrammar_ExtendedRuleParsesConcatenatedGrammar`
- `dotnet test Tests/CSharpGrammarTests --no-build` — 6 passed, 0 failed.
  2 new tests, all green:
  - `Ctor_MultiText_AppendedAlternative_Parses`
  - `Ctor_MultiText_UnknownTerminalInSecondFile_ThrowsWithSecondFilePath`

## Files

- New: `Tests/ParserTests/CsNitra/CsNitraGrammarText.cs` (shared full CsNitra grammar text).
- New: `Tests/ParserTests/CsNitra/GrammarMergeTests.cs`.
- New: `Tests/CSharpGrammarTests/CSharpParserMultiTextTests.cs`.
- Modified: `Tests/ParserTests/CsNitra/RuleGeneratorTests.cs` — local `GetGrammarText()` removed,
  now calls `CsNitraGrammarText.GetGrammarText()` (content unchanged).
- New: this progress file.

Tests are stateless/thread-safe: each test builds its own `Parser` locally; no shared statics
mutated, no temp files.
