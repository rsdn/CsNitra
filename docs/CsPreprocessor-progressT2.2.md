# T2.2 progress — `PreprocessorInterpreter` (visitor) that builds the preprocessed `Text`

Status: IN PROGRESS

## Scope (from plan T2.2)
Create `Parsers/CSharp/CsPreprocessor/PreprocessorInterpreter.cs`: an `ISyntaxVisitor` that walks
the parse tree in source order, drives a `DirectiveStack`, and builds the **preprocessed text**
using **same-length blanking** (D2). Wire it into `Preprocessor.Run`.

Core invariant (D2): `Text.Length == source.Length`; blanked chars → `' '`, `\n`/`\r` kept.

## Steps
- [x] Read plan, DirectiveStack, Preprocessor, PreprocessResult, PreprocessorTerminals, grammar.
- [x] Read SyntaxTree (ISyntaxVisitor + node types), JsonVisitor, T1.3 progress, condition tests.
- [x] Write `PreprocessorInterpreter.cs`.
- [x] Wire into `Preprocessor.Run`.
- [x] Build → 0 errors / 0 warnings (repo C# 13); also warning-free under C# 14 (see Deviations).
- [x] Throwaway sanity check (temp console project in temp dir, deleted after running).
- [x] Ran existing committed `CsPreprocessorTests` → 42/42 pass (no regression).
- [x] Update this file with final state.

## Visitor structure (design)
- `sealed class PreprocessorInterpreter(string source, IEnumerable<string> commandLineSymbols) : ISyntaxVisitor`
  (primary constructor; `source` captured as a field, used for `ToString(source)`).
- Fields: `_text = source.ToCharArray()`, `_stack = new DirectiveStack(commandLineSymbols.ToList())`,
  `_diagnostics` (List, empty for now — T2.3), `_lineDirectives` (List, empty for now — T4.3).
- `PreprocessResult? Result` — set at the end of the top-node traversal.

### Traversal
- `Visit(SeqNode node)`: if `node.Kind != "PreprocessorFile"` → no-op. Else iterate `node.Elements`
  (the lines) in source order, call `ProcessLine(line)`, then set `Result`.
- `ProcessLine(line)`:
  - `TerminalNode { Kind: "CodeLine" }` → if `!_stack.IsActive`, `Blank(line.StartPos, line.EndPos)`.
  - `SeqNode { Kind: "DirectiveLine" }` → `ProcessDirective(line)`, then `Blank(line.StartPos, line.EndPos)`.
  - (other line kinds ignored — shouldn't occur for `Line = DirectiveLine | CodeLine`).
- `ProcessDirective(directiveLine)`: `FindDirective` (direct child whose Kind is a directive kind),
  then switch:
  - `If` → `_stack.If(EvaluateCondition(directive))`
  - `Elif` → `_stack.Elif(EvaluateCondition(directive))`
  - `Else` → `_stack.Else()`
  - `EndIf` → `_stack.EndIf()`
  - `Define` → `_stack.Define(GetSymbolText(directive))`
  - `Undef` → `_stack.Undef(GetSymbolText(directive))`
  - `Error`/`Warning`/`LineDir`/`Region`/`EndRegion`/`Pragma`/`Nullable`/`Shebang`/`BadDirective`
    → recognize, no-op (T2.3/T4.x). Line still blanked by the caller.
- `FindDirective`: first `directiveLine.Elements` child whose Kind is in the directive-kind set.
- `GetSymbolText`: the `Symbol` TerminalNode child's `ToString(source)`.

### Condition evaluation (recursive, NOT via the visitor)
- `EvaluateCondition(directive)`: the unique child whose Kind is not in {If, Elif, Ws, LineEnd},
  then `Evaluate(condition)`.
- `Evaluate(node)`:
  - `TerminalNode { Kind: "Symbol" }` → text `true`→true, `false`→false, else `_stack.IsDefined(text)`.
  - `SeqNode` with `nonWs = Elements.Where(e => e.Kind != "Ws")`:
    - `Paren` → `Evaluate(nonWs.Single(e => e.Kind is not ("OpenParen" or "CloseParen")))`.
    - `CondNot` → `!Evaluate(nonWs[^1])`.
    - `CondAnd` → `Evaluate(nonWs[0]) && Evaluate(nonWs[^1])`.
    - `CondOr` → `Evaluate(nonWs[0]) || Evaluate(nonWs[^1])`.
    - `CondEq` → `Evaluate(nonWs[0]) == Evaluate(nonWs[^1])`.
    - `CondNotEq` → `Evaluate(nonWs[0]) != Evaluate(nonWs[^1])`.
  (Matches the T1.3 `Describe` helper shape: binary `[left, op, right]`, unary `[op, operand]`.)

### Blanking
- `Blank(start, end)`: for `i in [start, end)`, if `_text[i]` is not `\n`/`\r` → `' '`.
- CodeLine span includes the trailing `\n` (CodeLine terminal `ContentLength` = full line incl. `\n`);
  DirectiveLine span covers Ws + `#` + Directive (incl. `LineEnd` newline). Newlines preserved →
  `Text.Length == source.Length`, identity offset mapping.

## Key decisions
- **Top-node driven, not a generic recursive walk.** Only `topNode.Accept(interpreter)` is called.
  `Visit(SeqNode)` special-cases `Kind == "PreprocessorFile"` and iterates `node.Elements` (the
  lines) in source order; the other `Visit` overloads are no-ops. This keeps active/inactive state
  strictly sequential (top→bottom) as D4 requires, and avoids any risk of the generic visitor
  re-entering directive/condition subtrees out of order.
- **Directive dispatch by Kind set.** `FindDirective` returns the first direct child of the
  `DirectiveLine` whose Kind is in the 15-kind directive set (all directive nodes are `SeqNode`s).
  A small `IsDirectiveKind` helper holds the `or`-pattern. Non-branching/no-op directives
  (`Error`/`Warning`/`LineDir`/`Region`/`EndRegion`/`Pragma`/`Nullable`/`Shebang`/`BadDirective`)
  fall to the `default:` no-op — recognized but no effect yet (T2.3/T4.x); the line is still blanked
  by the caller regardless.
- **Condition evaluated recursively, NOT through the visitor.** `EvaluateCondition` picks the
  unique child whose Kind ∉ {If, Elif, Ws, LineEnd} (the `If`/`Elif` keyword literal, `Ws*`, and
  `LineEnd` are the other three), then `Evaluate` recurses on the TDOPP tree, filtering `Ws` out and
  using `nonWs[0]`/`nonWs[^1]` for binary operands and `nonWs[^1]` for the unary operand. This mirrors
  the T1.3 `Describe` helper shape exactly (verified against the committed `PreprocessorConditionTests`).
- **Blanking keeps `\n`/`\r`.** `Blank(start, end)` sets each non-newline char in the span to `' '`.
  Because both the `CodeLine` terminal span and the `DirectiveLine` span include their trailing
  newline, and newlines are preserved, `Text.Length == source.Length` and line/column positions are
  identity-mapped (D2). Active code lines are left untouched entirely.
- **`Result` nullable, set at end of top traversal.** `Run` guards `topNode is SeqNode` before
  `Accept`, then reads `interpreter.Result!` (non-null because a successful `PreprocessorFile` parse
  always yields a `PreprocessorFile` `SeqNode`).

## Deviations
- **`Blank` uses the positive pattern form to dodge a C# 14 warning.** The natural form
  `if (_text[i] is not '\n' or '\r')` triggers `CS9336: The pattern is redundant` under the .NET 10 /
  C# 14 compiler (the throwaway temp project, being outside the repo `global.json`, rolled forward to
  SDK 10.0.401 and surfaced it; the repo build on SDK 8.0.411 / C# 13 does not). Restructured to
  `var c = _text[i]; if (c is '\n' or '\r') continue; _text[i] = ' ';` — this both removes the warning
  under every compiler version and matches the AGENTS.md convention (`c is '\r' or '\n'`, positive form).
- **`FindDirective` returns `SeqNode?` (not `ISyntaxNode?`).** All 15 directive nodes are `SeqNode`s,
  so returning the concrete type lets `ProcessDirective`/`EvaluateCondition`/`GetSymbolText` take a
  `SeqNode` directly (avoids a cast).

## Final state
- `Parsers/CSharp/CsPreprocessor/PreprocessorInterpreter.cs` — **new**. `sealed`
  `PreprocessorInterpreter(string source, IEnumerable<string> commandLineSymbols) : ISyntaxVisitor`,
  file-scoped `namespace CsPreprocessor`, primary constructor, `_camelCase` fields
  (`_text`/`_stack`/`_diagnostics`/`_lineDirectives`), Allman braces, `sealed`. Methods:
  `Visit(SeqNode)` (top-node driver), `ProcessLine`, `ProcessDirective`, `FindDirective`,
  `IsDirectiveKind`, `GetSymbolText`, `EvaluateCondition`, `Evaluate`, `Blank`. `_diagnostics` and
  `_lineDirectives` are empty lists for now (T2.3 / T4.3 fill them).
- `Parsers/CSharp/CsPreprocessor/Preprocessor.cs` — **modified** `Run`: build parser →
  `Parse(source, "PreprocessorFile", out _)` → on success + `topNode is SeqNode`,
  `topNode.Accept(new PreprocessorInterpreter(source, commandLineSymbols))` and return
  `interpreter.Result!`; on parse failure return `new(source, empty, empty)` (source unchanged).
- No changes to `Preprocessor.grammar`, `PreprocessorTerminals.cs`, `DirectiveStack.cs`,
  `PreprocessResult.cs`, `Diagnostic.cs`, or `LineDirective.cs`.

## Verify
- Build: `dotnet build Parsers/CSharp/CsPreprocessor/CsPreprocessor.csproj` → **0 Warning / 0 Error**
  (repo SDK 8.0.411 / C# 13). Also warning-free under C# 14 after the `Blank` restructure.
- Existing committed suite: `dotnet test Tests/CsPreprocessorTests/CsPreprocessorTests.csproj` →
  **Total: 42, Passed: 42, Failed: 0** (no regression).

### Sanity check (throwaway temp console project, deleted after running)
`Preprocessor.Run` on the plan's inputs — **every case: `Text.Length == source.Length` (equal=True)**:

1. `#define FOO\nint a;\n#if FOO\nint b;\n#else\nint c;\n#endif\nint d;\n` (no cmdline), len 61:
   `"           \nint a;\n       \nint b;\n     \n      \n      \nint d;\n"`
   → `#define FOO` blanked, `int a;` kept, `#if FOO` blanked, `int b;` kept (FOO defined),
   `#else` blanked, `int c;` blanked (else not taken), `#endif` blanked, `int d;` kept.
2. `#define FOO\nint a;\n#if BAR\nint b;\n#else\nint c;\n#endif\nint d;\n` (no cmdline), len 61:
   `"           \nint a;\n       \n      \n     \nint c;\n      \nint d;\n"`
   → `int b;` blanked (BAR undefined), `int c;` kept (else taken).
3. `int x;\n` (no directives), len 7 → `"int x;\n"` (Text == source).
4. `""` (empty input), len 0 → `""` (source unchanged; parse yields an empty `PreprocessorFile`).
5. `#if DEBUG\nint x;\n#endif\n` with `["DEBUG"]`, len 24 → `"         \nint x;\n      \n"` (kept).
6. `#if DEBUG\nint x;\n#endif\n` with `[]`, len 24 → `"         \n      \n      \n"` (blanked).

## Open questions / notes for later sub-points
- **T2.3 (diagnostics):** `_diagnostics` is plumbed through but empty. `#error`/`#warning` should
  emit a `Diagnostic` (only when active — the `default:` no-op in `ProcessDirective` is where the
  active-only gating + message capture belongs); structural errors (unmatched `#if`/`#endif`/`#else`,
  bad placement) still need `DirectiveStack.HasUnfinishedIf` / stack-underflow checks at EOF.
- **T4.3 (`#line`):** `_lineDirectives` is plumbed but empty; `LineDir` handling goes in the
  `default:` branch (capture into `LineDirectives`, no effect on `Text`).
- **`#define`/`#undef` in inactive regions:** already correct — `DirectiveStack.Define`/`Undef` are
  internally gated on `_isActive`, so no extra work in the visitor.
- **Multi-line strings/comments (T4.4):** a `#` inside a `/* */` or `"""` at line-start is NOT a
  directive to the real C# lexer, but our line-based grammar would treat it as a `DirectiveLine`.
  The visitor currently trusts the grammar's line split; T4.4 will need active-region string/comment
   state tracking (a real deviation from pure top-down line processing).

## Tests (T2.2)

Added `Tests/CsPreprocessorTests/PreprocessorInterpreterTests.cs` (`[TestClass]`, `sealed`).
Tests the `Preprocessor.Run(source, symbols) → PreprocessResult` contract for the **interpreter's
same-length blanking** (D2): directive lines + inactive code lines blanked to spaces (newlines
preserved), active code lines kept verbatim. All sources use LF newlines.

Helpers (keep tests tidy):
- `Run(source, params string[] symbols) => Preprocessor.Run(source, symbols)`.
- `SameLength(source, result)` — asserts `result.Text.Length == source.Length` **and** the count of
  `\n` in `Text` equals the count in `source` (newlines preserved).
- `AssertLineKept(source, text, lineIndex)` — the line (split on `\n`) is kept **verbatim**
  (`text` line == `source` line).
- `AssertLineBlanked(source, text, lineIndex)` — the line has the **same length** as the source line
  and **every char is `' '`** (the split element contains no newline, so "every non-newline char in
  the line span is space" == "every char is space").

Tests + assertions:
1. `No_Directives_Passthrough` — `Run("int x = 1;\n")` → `Text == source` (passthrough).
2. `Directive_Line_Blanked_ActiveCode_Kept` — `#define FOO\nint a;\n` → line 0 (`#define FOO`)
   blanked, line 1 (`int a;`) kept verbatim, `SameLength`.
3. `If_True_KeepsBody` / `If_False_BlanksBody` — `#define A\n#if A\nint keep;\n#endif\n` → `int keep;`
   kept; `#if A\nint drop;\n#endif\n` (A undefined) → `int drop;` blanked.
4. `Define_Drives_If_SymbolState` — `#if A\nint x;\n#endif\n#define A\n#if A\nint y;\n#endif\n` →
   `int x;` blanked (A undefined at first `#if`), `int y;` kept (A defined by the second `#if`).
5. `CommandLine_Symbols` — `#if DEBUG\nint x;\n#endif\n` with `DEBUG` → `int x;` kept; without →
   `int x;` blanked.
6. `Else_FirstTrueBranchWins` — A undefined (`#if A\nint x;\n#else\nint y;\n#endif\n`) → `int x;`
   blanked, `int y;` kept; A defined (`#define A` prepended) → `int x;` kept, `int y;` blanked.
7. `Nested_If` — A defined (`#define A\n#if A\n#if A\nint x;\n#endif\n#endif\n`) → `int x;` kept;
   A undefined (`#if A\n#if A\nint x;\n#endif\n#endif\n`) → `int x;` blanked.
8. `Undef_RemovesSymbol` — `#define A\n#undef A\n#if A\nint x;\n#endif\n` → `int x;` blanked (A
   undefined after `#undef`).
9. `SameLength_And_NewlinePreservation` (invariant) — for all 11 sources of cases 2–8 (both symbol
   variants), asserts `SameLength` (length + newline count preserved).
10. `Blanked_Chars_AreSpaces` (invariant) — for a set of known-blanked lines (directive + inactive
    code across cases 2/3b/4/8), asserts every char in the line span is `' '`.

All tests are stateless/thread-safe (each `Run` builds a fresh parser + interpreter; helpers are
`static`), compatible with method-level parallelism.

### Result
`dotnet test Tests/CsPreprocessorTests/CsPreprocessorTests.csproj` → **Total: 53, Passed: 53,
Failed: 0, Skipped: 0** (42 pre-existing + 11 new; no regression). No production bug found — the
interpreter's same-length blanking matches the D2 contract on every case.
