# T1.1 progress — Preprocessor.grammar (file/line structure that tiles the whole source)

Status: DONE

## Scope (from plan T1.1)
`Preprocessor.grammar`: `PreprocessorFile = Line*`, `Line = DirectiveLine | CodeLine`;
hand-written `CodeLine`. The grammar must parse a file that is a mix of code and
directive lines; the top node covers the ENTIRE source `[0, source.Length)`.
Directive parsing is a PLACEHOLDER (T1.2 makes it real).

## Steps
- [x] Read plan, AGENTS.md, CSharpParser.Build, CSharpTerminals, Rules.cs, Parser.cs trivia,
      Cs1.grammar, EmbeddedGrammar.Load, ParserExtensions.BuildFromAst, RuleGenerator,
      CsNitraParser (meta-grammar), Result/SyntaxTree, existing smoke test.
- [x] Create `PreprocessorTerminals.cs` (NoOpTrivia, CodeLine, DirectiveLine).
- [x] Create `Preprocessor.grammar` (EmbeddedResource).
- [x] Add `<EmbeddedResource Include="Preprocessor.grammar" />` to csproj.
- [x] Wire `Preprocessor.BuildParser()` + use it in `Run`.
- [x] Build → 0 errors.
- [x] Throwaway sanity check (tile `[0,len)`, code vs directive split) → deleted.

## Key design decisions
- **No-op trivia**: `NoOpTrivia.TryMatch` always returns `0` (never -1). Satisfies the
  parse-start `Guard.IsTrue(triviaLength >= 0)` (Parser.cs:210) and disables the engine's
  automatic whitespace/comment skip (post-terminal at Parser.cs:775-777 is a no-op because
  `triviaLength > 0` is false). The GRAMMAR owns all whitespace/line structure.
- **Line-oriented tiling**: `CodeLine`/`DirectiveLine` each consume a WHOLE line
  (through the terminating `\n` inclusive, or to EOF for the last line). CRLF handled:
  the `\r` is ordinary line content, `\n` is the terminator. A lone `\r` (old-Mac) is
  treated as content (NOT a terminator) — out of scope (see open questions).
- **Directive vs code**: determined by the FIRST non-whitespace char of the line.
  `IsDirectiveStart` scans from line start: `\n`/`\r` (terminator reached, no `#`) → code;
  `#` → directive; other non-whitespace → code; horizontal whitespace (space/tab) → continue.
  An empty / whitespace-only line → code. The two terminals are mutually exclusive, so the
  `Line = | DirectiveLine | CodeLine` alternative can never tie (no equal-length ambiguity).
- **EOF safety**: at EOF, `CodeLine.TryMatch` returns 0 (matches empty). `ParseZeroOrMany`
  has a zero-width guard (Parser.cs:653 `if (newPos == currentPos) break;`), so `Line*`
  stops — no infinite loop. Empty file → top node `[0,0)`.
- **Grammar syntax**: CsNitra `Rule` alternatives REQUIRE a leading `|`
  (CsNitraParser.cs:64-70 `Alternative = | ...`). So `Line = | DirectiveLine | CodeLine;`.
  `PreprocessorFile = Line*;` is a `SimpleRule` (`Identifier "=" RuleExpression ";"`).
- **Terminal resolution**: grammar identifiers resolve to `Terminal` objects by `Kind`
  (RuleGenerator.cs:96 `globalScope.FindTerminal(refName)`). So the terminals' `Kind`
  must equal the grammar names `CodeLine` / `DirectiveLine`.
- **Thread-safety**: `Parser.Parse` mutates instance state (`_memo`, `_terminalCache`,
  `ErrorInfo`), so a `Parser` is NOT safe for concurrent `Parse`. Tests run method-level
  parallel. Therefore `BuildParser()` returns a FRESH `Parser` per call (no shared cache).
  Cost is negligible for this tiny grammar.
- **`BuildParser` visibility**: made `public static` (repo has NO InternalsVisibleTo;
  convention is public test hooks — see RecoverySystemChecklist). Lets the test subagent
  and the throwaway sanity check inspect the parse tree directly.

## Grammar text (Preprocessor.grammar)
```
PreprocessorFile = Line*;

Line =
    | DirectiveLine
    | CodeLine;
```
Start rule passed to `Parse` = `PreprocessorFile`. The leading `|` on each `Line`
alternative is REQUIRED by the CsNitra meta-grammar (`Alternative = | ...`).

## Terminal TryMatch logic
- `NoOpTrivia.TryMatch(input, startPos) => 0` — always matches empty, never -1. Disables
  the engine's auto whitespace/comment skip; grammar owns all whitespace/line structure.
- `CodeLine.TryMatch` — `IsDirectiveStart ? -1 : LineLength`. Matches a whole line whose
  first non-whitespace char is NOT `#` (empty/ws-only lines count as code).
- `DirectiveLine.TryMatch` — `IsDirectiveStart ? LineLength : -1`. PLACEHOLDER (T1.2
  replaces with a declarative directive grammar); matches a whole line whose first
  non-whitespace char IS `#`.
- `LineSupport.IsDirectiveStart(input, startPos)` — scan from `startPos`:
  `c is '\n' or '\r'` → `false` (terminator reached, no `#`); `c == '#'` → `true`;
  `!char.IsWhiteSpace(c)` → `false` (some other first token); horizontal ws → continue.
  Returns `false` at EOF.
- `LineSupport.LineLength(input, startPos)` — scan for the first `'\n'`; return
  `pos - startPos + 1` (include the `\n`); if none, `input.Length - startPos` (to EOF).
  CRLF: `\r` is ordinary content, `\n` is the terminator, so `\r\n` lines tile correctly.
- All three terminals: `Injectable => false` (recovery must not conjure a zero-width
  line/trivia token).

## Deviations / open questions
- **Empty file does not produce a top node** (documented, not a grammar bug): the PEG
  engine's `ParseRule` rejects a zero-width (epsilon) match for the START rule at a
  non-recovery position (Parser.cs:299-315 — `postNewPos > maxPos` is the only non-recovery
  accept path; the epsilon accept at :310 requires `isRecoveryPos`). So `Line*` correctly
  yields an empty success, but the wrapping `ParseRule("PreprocessorFile")` drops it →
  `Parse("")` returns `Failure`. This is the SAME behavior as CSharpGrammar (`Grammar =
  CompilationUnit` on empty input). It is a fundamental engine property, not fixable by the
  grammar. The public `Run` API still handles empty input by returning the (empty) source
  text (it ignores the parse result). A test for empty-file tiling via `BuildParser` will
  see `Failure` — the test subagent should assert `Run("")` → `Text == ""` instead.
- **Lone `\r` (old-Mac) line terminator**: treated as ordinary content, NOT a terminator.
  Only `\n` terminates a line (CRLF's `\r` is content). Old-Mac-only files would not tile
  per-line. Out of scope (T4.5 covers CRLF/LF; old-Mac is not a realistic C# source case).
- **`BuildParser` made `public static`** (not private): repo has no `InternalsVisibleTo`;
  convention is public test hooks. Lets the test subagent + future T2.x interpreter access
  the parser/tree directly.
- **`Run` builds a fresh `Parser` per call** (no cache): `Parser.Parse` mutates instance
  state (`_memo`/`_terminalCache`/`ErrorInfo`) → not safe for concurrent `Parse`; tests run
  method-level parallel. Cost is negligible for this 2-rule grammar.

## Final state
- `Parsers/CSharp/CsPreprocessor/Preprocessor.grammar` — `PreprocessorFile = Line*;` +
  `Line = | DirectiveLine | CodeLine;` (EmbeddedResource).
- `Parsers/CSharp/CsPreprocessor/PreprocessorTerminals.cs` — `NoOpTrivia`, `CodeLine`,
  `DirectiveLine` (all `Injectable => false`); shared `LineSupport` helpers.
- `Parsers/CSharp/CsPreprocessor/Preprocessor.cs` — `public static Parser BuildParser()`
  (CsNitraParser → `new Parser(NoOpTrivia)` → `BuildFromAst` → `BuildTdoppRules`);
  `Run` builds+parses then returns the source text (stub interpreter, T2.x).
- `Parsers/CSharp/CsPreprocessor/CsPreprocessor.csproj` — added the EmbeddedResource.
- Build: `dotnet build Parsers/CSharp/CsPreprocessor/CsPreprocessor.csproj` → 0 W / 0 E.
- Sanity check (throwaway, deleted): mixed LF input tiles `[0,len)` as
  Code/Directive/Code/Directive/Code/Directive; CRLF tiles; no-trailing-newline tiles;
  `Run("")` → `""`; `Run("int x = 1;")` → unchanged (existing smoke test still green).

## Tests (T1.1)

Status: DONE — all green.

### Test file added
`Tests/CsPreprocessorTests/PreprocessorGrammarTests.cs` (`[TestClass]`, 5 `[TestMethod]`).
Each test builds a fresh `Parser` via `public static Preprocessor.BuildParser()` (no shared
instance — method-level parallelism). A shared helper `ParseAndTile(source)` parses, asserts
success + `top.Kind == "PreprocessorFile"` + `top.StartPos == 0` + `top.EndPos == source.Length`
+ `newPos == source.Length`, extracts the ordered list of `(kind, start, end)` per line, and
asserts **contiguity** (first starts at 0; each line's `EndPos` == next line's `StartPos`; last
line's `EndPos` == `source.Length`). A second helper `AssertKinds` checks the ordered kinds.

### Actual parse-tree shape (verified empirically, corrected the task's assumption)
The top node is a `SeqNode` with `Kind == "PreprocessorFile"`. Its `.Elements` are the line
nodes **directly** — each is a `TerminalNode` whose `Kind` is `"CodeLine"` or `"DirectiveLine"`
(`StartPos`/`EndPos` = the line span). There is **no** intermediate `SeqNode` with
`Kind == "Line"`: the `Line = | DirectiveLine | CodeLine` rule's two terminal alternatives
collapse to the winning terminal node (TDOPP inlining), so the `Line` name is not attached to a
node. This is NOT a production bug — T1.1's contract (top covers `[0, source.Length)`, lines
tile the whole source, correct code/directive split) is fully satisfied. Tests assert against
this real shape.

### Tests + assertions
1. `MixedLfFile_TilesWholeSource` — LF input (6 lines: code/define/code/if/code/endif). Asserts
   parse success, top `[0, source.Length)`, full tiling, and kinds
   `Code, Directive, Code, Directive, Code, Directive`.
2. `CrlfFile_TilesWholeSource` — same content with `\r\n`. Asserts identical tiling + same kinds
   (CRLF's `\r` is content, `\n` terminates the line).
3. `LastLineWithoutTrailingNewline_TilesWholeSource` — `int a = 1;\n#define FOO` (ends at `FOO`,
   no trailing `\n`). Asserts parse success, `top.EndPos == source.Length`, tiling, kinds
   `Code, Directive`.
4. `BlankAndWhitespaceOnlyLines_AreCode` — input with a blank line and a whitespace-only (3-space)
   line mixed with code/directives. Asserts the blank + ws lines are classified `CodeLine`
   (not `DirectiveLine`) and tiling holds: `Code, Code, Code, Directive, Code`.
5. `Run_EmptySource_ReturnsEmptyText` — `Preprocessor.Run("", Array.Empty<string>())`. Asserts
   `Text == ""`, `Diagnostics.Count == 0`, `LineDirectives.Count == 0`. (Does NOT assert
   `Parse("")` succeeds — the engine rejects a zero-width top rule; known engine property, not a
   bug, so it is exercised only through `Run`.)

### Result
`dotnet test Tests/CsPreprocessorTests/CsPreprocessorTests.csproj` →
**Passed: 6, Failed: 0, Skipped: 0** (5 new grammar tests + 1 pre-existing smoke test).

### Production bug found
None. No test failed; no production code was modified.
