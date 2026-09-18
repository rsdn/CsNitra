# T1.2.1 progress — declarative `DirectiveLine` + the simple directives

Status: DONE (build clean; sanity check passes; 4 T1.1 tests intentionally left failing — see "Test impact").

## Scope (from plan T1.2.1)
Replace the hand-written `DirectiveLine` terminal with a **declarative** rule, and add the simple
directives (`else`/`endif`/`define`/`undef` + a `BadDirective` catch-all). Keep `CodeLine` and
`NoOpTrivia` as-is. Each directive must produce a node whose `Kind` is the rule name
(`Else`/`EndIf`/`Define`/`Undef`/`BadDirective`) with the symbol content reachable. The parser must
still tile the whole source and `#else` must not error (no equal-length conflict).

## Steps
- [x] Read plan, AGENTS.md, T1.1 progress, current grammar/terminals/Preprocessor.cs, `Rules.cs`,
      the CsNitra meta-grammar (`CsNitraParser.cs`, `CsNitraTerminals.cs`, `CsNitraVisitor.cs`,
      `AstSimplifier.cs`, `RuleGenerator.cs`, `Naming.cs`), `Parser.cs` (Seq/predicate handling).
- [x] Rewrite `Preprocessor.grammar` with the declarative `DirectiveLine` + directive rules.
- [x] Add `Ws`/`Symbol`/`LineEnd` terminals; remove the hand-written `DirectiveLine` terminal;
      update `PreprocessorTerminals.GetAll()`.
- [x] Build → 0 W / 0 E.
- [x] Throwaway sanity check (kinds + tiling + `#else` no-error + whole-word edge cases) → deleted.

## Key design decisions

### Literal / keyword matching is automatically whole-word
`RuleGenerator.cs:77-79` turns any grammar literal whose value is a valid identifier into a
`WordLiteral` (whole-word: the char after the match must not be letter/digit/`_`). `else`, `endif`,
`define`, `undef` are all valid identifiers, so `"else"`/`"endif"`/`"define"`/`"undef"` in the grammar
become `WordLiteral`s. This is what makes `KnownKeyword` a set of **whole-word** keyword matches and
is essential to the equal-length fix.

### `Ws` is a hand-written terminal (not a grammar rule)
The task allowed `Ws = ' ' | '\t'` in the grammar, but the CsNitra meta-grammar's string-unescape
(`CsNitraVisitor.UnescapeString`) only handles `\"`, `\'`, `\\` — it does **not** decode `\t`. A tab
would have to be a literal tab byte in the `.grammar` file (fragile/unreadable). So `Ws` is a
hand-written terminal matching exactly one space or tab (NOT newline). The grammar still references
it declaratively as `Ws*` / `Ws+`.

### `LineEnd` reuses the existing `LineSupport.LineLength`
`LineEnd.TryMatch(input, startPos) = LineSupport.LineLength(input, startPos)`: scan to the next `\n`
(inclusive of the `\n`, and its preceding `\r` for CRLF) or to EOF. Called from the position AFTER the
directive body, it consumes the rest of the line (trailing spaces/comments). At EOF it returns 0
(matches empty) — safe.

### `Symbol` is a hand-written identifier terminal
First char letter/`_`, rest letter/digit/`_`. Used by `Define`/`Undef`/`BadDirective`. All new
terminals are `Injectable => false` (consistent with `CodeLine`/`NoOpTrivia`; recovery must not
conjure a zero-width line/symbol token).

## Grammar text (final `Preprocessor.grammar`)
```
PreprocessorFile = Line*;

Line =
    | DirectiveLine
    | CodeLine;

DirectiveLine = Ws* "#" Directive;

Directive =
    | Else
    | EndIf
    | Define
    | Undef
    | BadDirective;

Else = "else" LineEnd;

EndIf = "endif" LineEnd;

Define = "define" Ws+ Symbol LineEnd;

Undef = "undef" Ws+ Symbol LineEnd;

BadDirective = !KnownKeyword Symbol LineEnd;

KnownKeyword =
    | "else"
    | "endif"
    | "define"
    | "undef";
```
Note: double-quoted literals (the meta-grammar `Literal` terminal accepts both `'…'` and `"…"`;
`"…"` matches the existing `Cs1.grammar` convention). Alternatives use the leading `|` form that the
CsNitra meta-grammar requires (`Alternative = | …`).

## Terminal `TryMatch` summaries
- `Ws.TryMatch` → `1` iff `input[startPos]` is `' '` or `'\t'`; else `-1`.
- `Symbol.TryMatch` → `-1` if no valid identifier start; else the identifier length (letter/`_` first,
  letter/digit/`_` after).
- `LineEnd.TryMatch` → `LineSupport.LineLength(input, startPos)`: distance to next `\n` (inclusive) or
  to EOF (0 at EOF).
- `CodeLine` / `NoOpTrivia` unchanged.

## How the equal-length conflict is avoided
Every directive keyword is itself a valid identifier, so a naive `BadDirective = Symbol` would match
the SAME span as e.g. `Else` on `#else` → equal length → parse error → tiling breaks. The fix:
`BadDirective = !KnownKeyword Symbol …`, where `KnownKeyword` lists every known directive keyword as a
**whole-word** `WordLiteral`. The `!` negative predicate makes `BadDirective` fail whenever a known
keyword is present, so only the specific directive matches. Verified edge cases (no error, correct
kind): `#else`→`Else`, `#defineX`→`BadDirective`, `#elseX`→`BadDirective`, `#define else`→`Define`
(symbol `else`), `#endif`→`EndIf`. Because a specific directive's keyword is always in `KnownKeyword`,
`BadDirective` can never co-match a specific directive → no equal-length tie anywhere in `Directive`.

## DEVIATION — `LineEnd` moved into each directive (required by the parser's Seq collapse)
**Problem found:** the target grammar's `DirectiveLine = Ws* '#' Directive LineEnd` with
`BadDirective = !KnownKeyword Symbol` does NOT produce a `BadDirective` node. `Parser.cs:853` skips
`PredicateNode`s (the `!KnownKeyword` lookahead) and `Parser.cs:867` collapses any `Seq` with a single
remaining element to that element. So `BadDirective` (`[!KnownKeyword, Symbol]` → `[Symbol]`) collapsed
to the bare `Symbol` terminal — the `#bogus` line produced `Kind="Symbol"`, not `Kind="BadDirective"`.
The task explicitly requires a node with `Kind="BadDirective"` ("not a generic terminal").

**Fix:** moved `LineEnd` from `DirectiveLine` into each directive (each directive now self-terminates
its line). `BadDirective = !KnownKeyword Symbol LineEnd` now has two non-predicate elements
(`Symbol`, `LineEnd`) → no collapse → a real `SeqNode` with `Kind="BadDirective"`. `DirectiveLine`
became `Ws* "#" Directive`. `Else`/`EndIf`/`Define`/`Undef` also gain the trailing `LineEnd` (they
become `SeqNode`s with `Kind` = rule name, which the task allows — "the exact child structure is up to
you"). This keeps the design fully declarative and keeps the `KnownKeyword` rule live (it must stay in
sync with the `Directive` alternatives as T1.2.2 adds directives).

**Rejected alternative:** a hand-written `BadDirective` terminal (matches a non-keyword identifier)
would also yield the right `Kind` and avoid the tie, but it drops the declarative `!KnownKeyword` guard
and leaves `KnownKeyword` unused — against the task's explicit "keep them in sync" intent.

**Equal-length re-check after the move:** each `Directive` alternative now ends in `LineEnd` (all reach
end-of-line), but the `!KnownKeyword` guard + specific keywords guarantee exactly ONE alternative
matches any given line, so no two alternatives tie.

## Node shape produced (verified)
For `#define FOO\n` (a `DirectiveLine` `SeqNode`, `Kind="DirectiveLine"`):
```
DirectiveLine
  Ws            (ZeroOrMany, 0 matches)
  #             (Literal)
  Define        (SeqNode, Kind="Define")
    define      (WordLiteral; inferred Kind "Define")
    Ws          (OneOrMany → Ws terminal)
    Symbol      (terminal, "FOO")   ← symbol reachable
    LineEnd     (terminal, "\n")
```
`#else`→`Else` (`SeqNode`), `#endif`→`EndIf` (`SeqNode`), `#bogus`→`BadDirective` (`SeqNode` with
`Symbol` child). `CodeLine` lines remain `TerminalNode`s.

Minor cosmetic note (not a bug): the keyword literal inside each directive gets an *inferred* `Kind`
from CsNitra name inference (`"define"`→`Define`, `"endif"`→`Endif`). The directive node itself always
has the correct rule-name `Kind`; the later visitor should key off the directive `SeqNode` `Kind`, not
the inner literal.

## Test impact (expected — do NOT fix here)
The T1.1 test `Tests/CsPreprocessorTests/PreprocessorGrammarTests.cs` asserts each line node is a
`TerminalNode` (`ParseAndTile`, line 77). Directive lines are now `SeqNode`s (`Kind="DirectiveLine"`),
so the 4 tests that contain directive lines now FAIL with
"Expected a line TerminalNode, got SeqNode (Kind=DirectiveLine)":
`MixedLfFile_TilesWholeSource`, `CrlfFile_TilesWholeSource`,
`LastLineWithoutTrailingNewline_TilesWholeSource`, `BlankAndWhitespaceOnlyLines_AreCode`.
Per the task, the test subagent updates these. `Run_EmptySource_ReturnsEmptyText` and the pre-existing
smoke test still pass. (Suite result: Failed 4, Passed 2 — all 4 failures are this expected shape change.)

## Deviations / open questions
- **`LineEnd` placement** (the main deviation) — see "DEVIATION" above. Required to satisfy the
  `Kind="BadDirective"` node requirement given the engine's single-element Seq collapse.
- **Malformed directive lines are not handled** (out of scope — T4.x / error recovery):
  - `#undef\n` (keyword with no symbol) → parse **fails** (`Undef` needs `Ws+ Symbol`; `BadDirective`
    guarded out because `undef` is known).
  - `#\n` (bare `#`) → the (mid-implementation) **error-recovery** engine injects a zero-width
    `Else` (`recovery=True`), so it "succeeds" with a recovery node. Not a grammar bug; recovery is a
    separate workstream.
  Both are genuinely malformed C# (e.g. CS1024) and are covered by later phases.
- **`KnownKeyword` must stay in sync** with the `Directive` alternatives as T1.2.2 adds directives
  (`if`/`elif`/`error`/`warning`/`line`/`region`/`endregion`/`pragma`/`nullable`/`shebang`). Each new
  keyword directive adds an alternative to `Directive` AND a whole-word literal to `KnownKeyword`.

## Final state
- `Parsers/CSharp/CsPreprocessor/Preprocessor.grammar` — declarative `DirectiveLine` + `Directive` +
  `Else`/`EndIf`/`Define`/`Undef`/`BadDirective`/`KnownKeyword` (EmbeddedResource).
- `Parsers/CSharp/CsPreprocessor/PreprocessorTerminals.cs` — added `Ws`, `Symbol`, `LineEnd`
  (all `Injectable => false`); removed the hand-written `DirectiveLine` terminal; `CodeLine` and
  `NoOpTrivia` unchanged; `GetAll()` registers `NoOpTrivia`, `CodeLine`, `Ws`, `Symbol`, `LineEnd`.
- `Parsers/CSharp/CsPreprocessor/Preprocessor.cs` — **unchanged** (still `new Parser(NoOpTrivia())` +
  `BuildFromAst(…, GetAll())` + `BuildTdoppRules()`); public `Run(string, IEnumerable<string>)`
  signature preserved (still returns source unchanged — interpreter is T2.x).

## Verify
- Build: `dotnet build Parsers/CSharp/CsPreprocessor/CsPreprocessor.csproj` → **0 Warning / 0 Error**.
- Sanity check (throwaway, deleted): input
  `int a;\n#define FOO\n#undef FOO\n#else\n#endif\n#bogus\nint b;\n` (len 57) →
  1) parse **succeeds**; 2) top `PreprocessorFile [0,57)` tiles the whole source (contiguous
  0→7→19→30→36→43→50→57); 3) directive lines produce `Kind`s `Define`/`Undef`/`Else`/`EndIf`/
  `BadDirective` (each a `SeqNode` under a `DirectiveLine` `SeqNode`, symbol reachable); 4) `#else`
  does **not** error (the `!KnownKeyword` guard made `BadDirective` fail). Extra whole-word cases
  (`#defineX`, `#elseX`, `#define else`) and CRLF / no-trailing-newline all tile correctly with the
  right kinds.

## Tests (T1.2.1)

Scope: tests only — touched `Tests/CsPreprocessorTests` (+ this progress file). No production code changed.
No production bug found.

### Verified node shape (by walking a real parse tree)
Top `PreprocessorFile` is a `SeqNode`; its `Elements` are the lines, in order:
- **Code line** → `TerminalNode` with `Kind == "CodeLine"`.
- **Directive line** → `SeqNode` with `Kind == "DirectiveLine"`; its children are
  `[Ws (SeqNode, Kind="Ws"), # (TerminalNode, Kind="#"), <Directive> (SeqNode)]`. The directive is the
  **last direct child**, a `SeqNode` whose `Kind` is `Define`/`Undef`/`Else`/`EndIf`/`BadDirective`.
  `Define`/`Undef`/`BadDirective` each contain a `Symbol` `TerminalNode` (the symbol text via
  `node.ToString(source)`); `Else`/`EndIf` have no `Symbol`.
- **Gotcha handled:** the inner keyword literal also carries an *inferred* `Kind`
  (`"define"`→`Define`, `"endif"`→`Endif`), so tests must locate the directive as a **direct child** of
  the `DirectiveLine` (a `SeqNode` whose `Kind` is a directive kind), not by searching the whole subtree.

### Fixed T1.1 tiling tests (`PreprocessorGrammarTests.cs`)
The `ParseAndTile` helper previously asserted every line is a `TerminalNode` — now false for directive
lines (they are `SeqNode`s). Added an `EffectiveKind(ISyntaxNode)` mapping:
`TerminalNode{Kind:"CodeLine"} → "CodeLine"`, `SeqNode{Kind:"DirectiveLine"} → "DirectiveLine"`,
anything else throws. The 4 tiling tests (`MixedLfFile_TilesWholeSource`, `CrlfFile_TilesWholeSource`,
`LastLineWithoutTrailingNewline_TilesWholeSource`, `BlankAndWhitespaceOnlyLines_AreCode`) keep their
intent (Code/Directive split + contiguity) and pass unchanged in assertion text.

### New T1.2.1 tests (`PreprocessorDirectiveTests.cs`, `[TestClass]`, fresh parser per test)
Each test parses via a fresh `Preprocessor.BuildParser()` and asserts the top node covers `[0, len)`
with contiguous lines (tiling invariant) via a shared `ParseTop`/`AssertTiling` helper.
- **`DirectiveKinds_AreProduced`** — input with `#define FOO`, `#undef FOO`, `#else`, `#endif`, `#bogus`
  (wrapped in code lines): asserts the 5 directive-node `Kind`s in order are
  `Define`/`Undef`/`Else`/`EndIf`/`BadDirective`, that the line sequence is
  `Code,Dir,Dir,Dir,Dir,Dir,Code`, and that the `Symbol` texts are `FOO`/`FOO`/—/—/`bogus`
  (`Else`/`EndIf` have no symbol).
- **`Else_DoesNotError`** — a file with `#else` parses **successfully** (`result.IsSuccess`, no
  equal-length-match error — the `!KnownKeyword` guard) and yields an `Else` node.
- **`GluedKeywords_AreBadDirective`** — `#defineX` and `#elseX` (keyword glued to extra chars) each
  produce a `BadDirective` node (whole-word keyword matching), **not** `Define`/`Else`, with symbols
  `defineX`/`elseX`.
- **`Tiling_HoldsForAllDirectiveKinds`** — a source containing all five directive kinds plus both glued
  forms: asserts the 10-line sequence, the top node `[0, len)`, and explicit pairwise contiguity of every
  line.

### Test result
`dotnet test Tests/CsPreprocessorTests/CsPreprocessorTests.csproj` → **Passed: 10, Failed: 0, Skipped: 0**.
Breakdown: 5 T1.1 grammar tests (4 fixed tiling + empty-source), 1 smoke test, 4 new T1.2.1 tests.

