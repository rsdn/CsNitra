# CsPreprocessor T4.4.1 — Foundation: Reuse the Main Parser's Trivia

Status: **done** (foundation only). Build 0 errors, all 111 existing `CsPreprocessorTests` pass.

## Problem

`CsPreprocessor` classified a line as a directive when its first non-whitespace char was `#`.
That is wrong when the `#` sits inside a multi-line `/* ... */` comment (or, for the next
sub-point, a multi-line string): such a `#` is **not** a directive. A hand-written comment
scanner was rejected because it would duplicate the main C# parser's logic. The fix must
**reuse** the main parser's rules — comments/whitespace from `CSharpTerminals.Trivia()`,
string literals from the grammar rules.

## Architecture Decision

The preprocessor `Parser` is now built with a trivia terminal that **reuses the main C#
parser's comment/whitespace scanner** (`CSharpTerminals.Trivia()`), so the engine skips
comments/whitespace and a `#` inside a comment is no longer seen as a directive-start.

The raw `CSharpTerminals.Trivia()` treats newlines as ordinary whitespace. The preprocessor
grammar is **line-based** (each `Line` must tile `[0, len)` for same-length blanking, D2), so
a trivia that swallows newlines would merge blank/whitespace-only lines into the previous
line's trailing trivia and break tiling. Therefore the engine's trivia is a thin wrapper,
`PreprocessorTriviaTerminal`, that:

- **delegates comment matching to `CSharpTerminals.Trivia()`** (so `//` and nested `/* */`
  are handled by the main parser — no hand-rolled comment scanning), and lets comments span
  multiple lines (this is what hides a `#` inside a multi-line comment);
- **keeps whitespace on the current line** (stops at the first `\n`) so blank lines stay
  distinct and lines still tile `[0, len)`.

`NoOpTrivia` is kept available but no longer used for the engine's trivia.

## Project-Reference Change

`CsPreprocessor.csproj` → `CSharpGrammar.csproj`. This reference was **already present** in
the csproj (verified); no change was needed. Direction is clean: the test project already
references both, and `CsPreprocessor` now consumes `CSharpTerminals`.

## Grammar Adjustment (`Preprocessor.grammar`)

Minimal, to keep the build green and lines tiling `[0, len)` under the new trivia:

- **Removed all `Ws*` / `Ws+` tokens** from `DirectiveLine`, `If`, `Elif`, `Define`, `Undef`,
  and the `Condition`/`CondAtom` rules. The engine now auto-skips whitespace as trailing
  trivia after each terminal, so `Ws+` would fail (the space was already consumed) and `Ws*`
  was redundant. The interpreter's `Evaluate` already filters `Ws` elements, so it is
  unaffected.
- **`LineEnd` terminal redefined** (`PreprocessorTerminals.cs`) to match the rest of the line
  **including its line ending** (the `\n`), mirroring `CodeLine`'s `LineLength`. Because the
  engine's trivia stops at `\n`, the newline is left for `LineEnd`/`CodeLine` to consume, so
  each directive line's node still covers the full line and lines tile `[0, len)`. For a
  message (`#error "boom"`), the `LineEnd` content is the message plus the line ending; the
  interpreter already trims it (`GetMessageText` / `ProcessLineDirective`).

`Ws` is kept in `GetAll()` (available but no longer referenced by the grammar).

## Key Decisions

1. **Reuse, don't duplicate**: comment scanning is delegated to `CSharpTerminals.Trivia()`;
   only a line-boundary guard is added for whitespace.
2. **Line tiling is a hard invariant** (`PreprocessorGrammarTests` asserts `top.StartPos == 0`,
   `top.EndPos == len`, and contiguous line tiling). The trivia wrapper exists to preserve it.
3. **`LineEnd` owns the line ending** so a directive line's node spans the whole line, keeping
   the blanking (D2) ranges correct.
4. **CRLF**: the wrapper stops at `\n` only and consumes `\r` as whitespace, consistent with
   `CodeLine`'s `LineLength` (which also stops at `\n` only). Verified by the CRLF tiling tests.

## Deviations

- The task said to build the `Parser` with `CSharpTerminals.Trivia()` **directly**. Using it
  directly breaks `BlankAndWhitespaceOnlyLines_AreCode` (the C# trivia swallows the blank and
  whitespace-only lines as trailing trivia → 3 lines instead of 5). To satisfy the hard
  constraint "all existing tests keep passing / lines still tile `[0,len)`", the engine's
  trivia is `PreprocessorTerminals.Trivia()` — a wrapper that **reuses** `CSharpTerminals.Trivia()`
  for comments but bounds whitespace to the current line. This is the minimal deviation that
  keeps both the tiling invariant and the "reuse the main parser's trivia" rule.

## What T4.4.3 (String Literals) Should Build On

- **Hook point**: `PreprocessorTriviaTerminal.TryMatch` (`PreprocessorTerminals.cs`). It is the
  single place where the engine decides what to skip before a line is classified. T4.4.3 should
  extend it to also recognize **string literals** (reuse the main parser's string rules from
  `CSharpTerminals` — `StringText`, `NonQuoteText`, `RawString`, `CharLiteral`, escapes, etc.)
  so a `#` inside a (multi-line) string is not seen as a directive-start.
- **Why the trivia terminal and not `CodeLine.IsDirectiveStart`**: `IsDirectiveStart` only sees
  the current line, but a multi-line string spans several lines, so the "inside a string" state
  must be tracked across line boundaries. The trivia terminal is the natural place to consume a
  string literal (like it already consumes a comment) so the `#` on a continuation line is never
  reached by the directive test.
- **Caveat to resolve in T4.4.3**: string literals are tokens, not C# trivia, so the wrapper
  must consume them deliberately (not as "trivia"). Mirror the comment branch: detect a string
  start, delegate the match to the main parser's string rule, advance past it, and continue.
- The `LineEnd`/`CodeLine` line-ending ownership and the removal of `Ws*`/`Ws+` remain valid and
  need no change for strings.

## Final State

- `Parsers/CSharp/CsPreprocessor/CsPreprocessor.csproj` — reference to `CSharpGrammar` already
  present (no change).
- `Parsers/CSharp/CsPreprocessor/Preprocessor.cs` — `BuildParser` now uses
  `PreprocessorTerminals.Trivia()` (reuses `CSharpTerminals.Trivia()`).
- `Parsers/CSharp/CsPreprocessor/PreprocessorTerminals.cs` — added `PreprocessorTriviaTerminal`
  + `Trivia()` accessor; redefined `LineEndTerminal`.
- `Parsers/CSharp/CsPreprocessor/Preprocessor.grammar` — removed `Ws*`/`Ws+`.
- Verified: a `#` inside a `/* ... */` block comment and inside a `//` line comment is **not**
  treated as a directive (checked with a throwaway test: same-length output, real directives
  still active, no spurious CS1024). That throwaway test was removed to keep the change set
  minimal; T4.4.3 should add a permanent regression test for comments + strings.
