# T1.2.2 progress — add the remaining directive kinds

Status: DONE (build clean 0W/0E; sanity check passes; all 10 existing committed tests still pass;
throwaway sanity test deleted before finishing).

## Scope (from plan T1.2.2)
Extend the `Directive` alternatives and `KnownKeyword` to cover ALL C# preprocessor directives.
The `#if`/`#elif` condition is a **placeholder** in this task (T1.3 makes it a real TDOPP
expression) — for now the condition is just "the rest of the line" (the trailing `LineEnd`).
Add `If`/`Elif`/`Error`/`Warning`/`LineDir`/`Region`/`EndRegion`/`Pragma`/`Nullable`/`Shebang`,
each ending in `LineEnd` (so each is a real multi-element node and consumes the whole line), and
keep `BadDirective` last. Every new whole-word keyword is added to `KnownKeyword` so
`BadDirective = !KnownKeyword Symbol LineEnd` stays the only matcher for unknown `#`-lines.

## Steps
- [x] Read plan, T1.2.1 progress, current grammar/terminals/Preprocessor.cs, and the meta-grammar
      (`RuleGenerator.cs`, `CsNitraParser.cs`, `CsNitraTerminals.cs`, `CsNitraVisitor.cs`,
      `Naming.cs`) to confirm how `"!"` is tokenized and how `WordLiteral`/`Seq` collapse behave.
- [x] Rewrite `Preprocessor.grammar` with the 10 new directive rules + expanded `Directive` and
      `KnownKeyword`.
- [x] Confirmed **no terminal changes** are needed (all new rules use only existing `Ws`/`Symbol`/
      `LineEnd` + grammar literals).
- [x] Build → 0 Warning / 0 Error.
- [x] Throwaway sanity check (kinds + tiling + shebang + whole-word glued edge cases) → deleted.
- [x] Ran the existing committed `CsPreprocessorTests` suite → 10/10 pass (no regression).

## Key design decisions

### `Shebang = "!" LineEnd` — the quoted `"!"` is a literal, not the negative predicate
Confirmed via the meta-grammar: the `Literal` terminal (`CsNitraTerminals.Literal`) matches a
quoted `"…"`/`'…'` string, and `CsNitraVisitor.UnescapeString` strips the quotes leaving the value
`!`. `Naming.IsValidIdentifier("!")` is `false`, so `RuleGenerator.cs:77-79` emits a plain
`EP.Literal("!", …)` (NOT a `WordLiteral`). This is distinct from the unquoted `!` that the
`NotPredicate` rule (`CsNitraParser.cs:121`) uses for `!KnownKeyword`. So `Shebang` is the only
matcher for `#!…` lines. `BadDirective` cannot match `#!…` anyway because its `Symbol` terminal
requires a letter/`_` first char (and `!` is not one). Per the task, `!`/shebang is deliberately
**not** added to `KnownKeyword` (it is not a keyword).

### Whole-word keywords → `KnownKeyword`
`RuleGenerator.cs:77-79` turns any grammar literal whose value is a valid identifier into a
`WordLiteral` (whole-word: the char after the match must not be letter/digit/`_`). All ten new
keywords (`if`/`elif`/`error`/`warning`/`line`/`region`/`endregion`/`pragma`/`nullable`) are valid
identifiers, so each becomes a `WordLiteral`. Adding each to `KnownKeyword` keeps the
`!KnownKeyword` guard correct: on `#if …`, `KnownKeyword` matches the `if` word → `!KnownKeyword`
fails → `BadDirective` fails → only `If` matches. No equal-length tie.

### `Ws*` after `if`/`elif` (not `Ws+`)
`If = "if" Ws* LineEnd` and `Elif = "elif" Ws* LineEnd` use `Ws*` so BOTH `#if FOO` and `#if(FOO)`
are recognized (zero or more spaces between the keyword and the condition). The condition is the
trailing `LineEnd` child (the placeholder for T1.3). The shape is a 3-element `Seq`
(`"if"`, `Ws*`, `LineEnd`) — easy to edit for the T1.3 swap to `"if" Ws* Condition LineEnd`.

### All other new directives are `"keyword" LineEnd`
`Error`/`Warning`/`LineDir`/`Region`/`EndRegion`/`Pragma`/`Nullable` are a 2-element `Seq`
(`"keyword"`, `LineEnd`); the message/target/rest is the trailing `LineEnd` child. `Shebang` is
`"!" LineEnd` (2 elements). Every rule ends in `LineEnd`, so (a) each directive is a real
multi-element `SeqNode` with `Kind` = the rule name (no single-element collapse — see T1.2.1
deviation), and (b) each directive consumes the whole line.

## Grammar text (final `Preprocessor.grammar`)
```
PreprocessorFile = Line*;

Line =
    | DirectiveLine
    | CodeLine;

DirectiveLine = Ws* "#" Directive;

Directive =
    | If
    | Elif
    | Else
    | EndIf
    | Define
    | Undef
    | Error
    | Warning
    | LineDir
    | Region
    | EndRegion
    | Pragma
    | Nullable
    | Shebang
    | BadDirective;

If = "if" Ws* LineEnd;

Elif = "elif" Ws* LineEnd;

Else = "else" LineEnd;

EndIf = "endif" LineEnd;

Define = "define" Ws+ Symbol LineEnd;

Undef = "undef" Ws+ Symbol LineEnd;

Error = "error" LineEnd;

Warning = "warning" LineEnd;

LineDir = "line" LineEnd;

Region = "region" LineEnd;

EndRegion = "endregion" LineEnd;

Pragma = "pragma" LineEnd;

Nullable = "nullable" LineEnd;

Shebang = "!" LineEnd;

BadDirective = !KnownKeyword Symbol LineEnd;

KnownKeyword =
    | "if"
    | "elif"
    | "else"
    | "endif"
    | "define"
    | "undef"
    | "error"
    | "warning"
    | "line"
    | "region"
    | "endregion"
    | "pragma"
    | "nullable";
```
Note: `LineDir` is the rule name (NOT `Line` — that name is already taken by the file/line rule
`Line`). `Shebang` uses the quoted literal `"!"`. Alternatives use the leading `|` form the meta-
grammar requires.

## Terminal changes
**None.** All ten new rules reference only the existing terminals `Ws`, `Symbol`, `LineEnd`
(`PreprocessorTerminals.cs` untouched) plus grammar literals. `GetAll()` unchanged.

## How the equal-length conflict is avoided
Identical mechanism to T1.2.1, now with more keywords. Each directive keyword is itself a valid
identifier, so a bare `BadDirective = Symbol` would co-match the SAME span as e.g. `If` on `#if`
→ equal length → parse error. The fix: `BadDirective = !KnownKeyword Symbol LineEnd`, where
`KnownKeyword` lists EVERY known directive keyword as a whole-word `WordLiteral`. Because each
specific directive's keyword is in `KnownKeyword`, `BadDirective` fails whenever a known keyword
is present, so exactly ONE `Directive` alternative matches any given line → no tie anywhere.
Verified glued forms (`#ifX`, `#errorX`, `#lineX`, `#nullableX`) → `BadDirective` (whole-word
`if`/`error`/`line`/`nullable` fail when glued to a letter), and valid forms never hit
`BadDirective`.

## Node shape produced (verified via throwaway dump)
Each directive line is a `DirectiveLine` `SeqNode` whose last direct child is the directive
`SeqNode` with `Kind` = the rule name. The content is the trailing `LineEnd` child. Confirmed
`Kind`s: `If`/`Elif`/`Else`/`EndIf`/`Error`/`Warning`/`LineDir`/`Region`/`EndRegion`/`Pragma`/
`Nullable`/`Shebang` (and `BadDirective` for unknown/glued). Minor cosmetic note (same as T1.2.1,
not a bug): the inner keyword literal carries an *inferred* `Kind` (e.g. `"nullable"`→`Nullable`,
`"!"`→`Not`); the directive `SeqNode` itself always has the correct rule-name `Kind`. A later
visitor should key off the directive `SeqNode` `Kind`, not the inner literal.

## Deviations / open questions
- **None from the task.** All ten directives added exactly as specified; `KnownKeyword` kept in
  sync; `LineEnd` inside each directive; `Ws*` on `if`/`elif`; `BadDirective` last; `!`/shebang
  not in `KnownKeyword`.
- **`If`/`Elif` condition is a placeholder** (`LineEnd`), per the task — T1.3 will change it to
  `"if" Ws* Condition LineEnd` (a real TDOPP `Condition`). The current shape is chosen to make
  that swap a one-line edit.
- **`#line`/`#pragma`/`#nullable`/`#region` bodies are not parsed** — they are the whole rest of
  the line in the trailing `LineEnd`. Structured parsing of `#line N "file"` / `#line default` /
  `#line hidden`, `#pragma …`, `#nullable enable|disable`, and `#region` text is out of scope here
  (later phases / T4.x). The node `Kind` is correct and the content is reachable via `LineEnd`.
- **Malformed directive lines remain unhandled** (T4.x / error recovery), same as T1.2.1: e.g.
  `#undef` with no symbol, bare `#`.

## Final state
- `Parsers/CSharp/CsPreprocessor/Preprocessor.grammar` — now defines all 14 directive rules
  (`If`/`Elif`/`Else`/`EndIf`/`Define`/`Undef`/`Error`/`Warning`/`LineDir`/`Region`/`EndRegion`/
  `Pragma`/`Nullable`/`Shebang`) + `BadDirective` + expanded `KnownKeyword` (13 whole-word
  keywords). EmbeddedResource.
- `Parsers/CSharp/CsPreprocessor/PreprocessorTerminals.cs` — **unchanged**.
- `Parsers/CSharp/CsPreprocessor/Preprocessor.cs` — **unchanged** (still `new Parser(NoOpTrivia())`
  + `BuildFromAst(…, GetAll())` + `BuildTdoppRules()`); public `Run(string, IEnumerable<string>)`
  preserved (still returns source unchanged — interpreter is T2.x).

## Verify
- Build: `dotnet build Parsers/CSharp/CsPreprocessor/CsPreprocessor.csproj` → **0 Warning / 0 Error**.
- Existing committed suite: `dotnet test Tests/CsPreprocessorTests/CsPreprocessorTests.csproj` →
  **Passed: 10, Failed: 0** (no regression from the grammar change).
- Sanity check (throwaway `T122SanityCheck`, deleted after running):
  - **Main input** (14 lines, len 180): parse **succeeds**, top `PreprocessorFile [0,180)`,
    `newPos=180`, contiguous tiling `0→10→17→31→38→44→51→58→72→91→111→123→134→163→180`. Directive
    node `Kind`s in order: `If, Elif, Else, EndIf, Error, Warning, LineDir, Region, EndRegion,
    Pragma, Nullable` — all correct, **no** `BadDirective`, **no** equal-length-match error.
  - **Shebang input** `#!shebang\nint x;\n` (len 17): top `[0,17)`; line[0] → `Shebang`,
    line[1] → `CodeLine`.
  - **Whole-word edge input** `#ifX\n#errorX\n#lineX\n#nullableX\nint z;\n` (len 38): the four
    glued forms each → `BadDirective` (whole-word keyword matching intact), `int z;` → `CodeLine`.

## Tests (T1.2.2)

Added `Tests/CsPreprocessorTests/PreprocessorAllDirectivesTests.cs` (`[TestClass]`, `sealed`). Each
test builds a fresh parser via `Preprocessor.BuildParser()` (no shared parser instance —
thread-safe under method-level parallelism). Helpers mirror `PreprocessorDirectiveTests`
(`ParseTop`, `GetDirectiveLines`, `GetDirective`, `AssertTiling`, `EffectiveKind`, `AssertKinds`),
with `GetDirective` extended to the **full** directive-kind set
(`If/Elif/Else/EndIf/Define/Undef/Error/Warning/LineDir/Region/EndRegion/Pragma/Nullable/Shebang/BadDirective`)
and a new `AssertDirectiveKinds`/`GetDirectiveKinds` pair. `ParseTop` asserts `IsSuccess`,
`TryGetSuccess`, top node `PreprocessorFile [0, source.Length)`, `newPos == source.Length`, and
contiguous tiling of all line nodes.

### Tests

1. **`AllDirectiveKinds_ProduceTheRightNodeKind`** — a file wrapped with `int a;` / `int b;`
   containing one line each of `#if DEBUG`, `#elif RELEASE`, `#else`, `#endif`, `#define X`,
   `#undef X`, `#error "boom"`, `#warning "w"`, `#line 42 "Other.cs"`, `#region Top`, `#endregion`,
   `#pragma warning disable 0162`, `#nullable enable`. Asserts the 15 line Kinds are
   `CodeLine, DirectiveLine×13, CodeLine`; the 13 directive node Kinds in order are
   `If, Elif, Else, EndIf, Define, Undef, Error, Warning, LineDir, Region, EndRegion, Pragma,
   Nullable`; and **none** is `BadDirective`. Tiling holds (via `ParseTop`).
2. **`Shebang_IsRecognized`** — input `#!usr/bin/env dotnet\nint x;\n`. Asserts line Kinds are
   `DirectiveLine, CodeLine`; the first line's directive node Kind is `Shebang`. Tiling holds.
3. **`GluedKeywords_AreBadDirective`** — a wrapped file with `#ifX`, `#errorX`, `#lineX`,
   `#nullableX`, `#regionX`. Asserts line Kinds are `CodeLine, DirectiveLine×5, CodeLine` and all
   5 directive node Kinds are `BadDirective` (whole-word keyword matching — the glued forms are NOT
   the specific directives). Tiling holds.
4. **`IfWithNoSpace_IsAnIfDirective`** — a wrapped file with `#if(FOO)` (no space after `if`).
   Asserts line Kinds are `CodeLine, DirectiveLine, CodeLine` and the directive node Kind is `If`
   (`Ws*` allows zero whitespace), not `BadDirective`. Tiling holds.
5. **`MixedAllDirectives_NoEqualLengthMatchError`** — a wrapped file mixing **all** of the above
   (13 valid directives + `#if(FOO)` + the 5 glued forms). Asserts the parse **succeeds**
   (`IsSuccess`/no equal-length-match exception, via `ParseTop`) and tiles, and the 19 directive
   node Kinds in order are `If, Elif, Else, EndIf, Define, Undef, Error, Warning, LineDir, Region,
   EndRegion, Pragma, Nullable, If, BadDirective, BadDirective, BadDirective, BadDirective,
   BadDirective`.

### Result

`dotnet test Tests/CsPreprocessorTests/CsPreprocessorTests.csproj` → **Total: 15, Passed: 15,
Failed: 0** (10 pre-existing + 5 new). **No production bug found.**
