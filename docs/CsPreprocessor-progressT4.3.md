# T4.3 — Parse `#line` arguments and record them

## Scope
In `PreprocessorInterpreter.ProcessDirective`, add a `case "LineDir":` that parses the `#line`
arguments and records an active directive into `PreprocessResult.LineDirectives` (D5: display
remap only — does NOT affect offsets). This is **additive**: blanking/text (T2.2) and existing
diagnostics (T2.3/T4.1) are untouched. All changes are localized to
`Parsers/CSharp/CsPreprocessor/PreprocessorInterpreter.cs`.

## What changed
File modified: `Parsers/CSharp/CsPreprocessor/PreprocessorInterpreter.cs`
- Added `using System.Globalization;` (for culture-invariant integer parsing).
- Added `case "LineDir":` in the `ProcessDirective` switch.
- Added three private helpers: `ProcessLineDirective`, `TokenizeLineDirective`, `ComputeOriginalLine`.
- No other file was modified. No committed tests written (a separate test subagent does that).

## Active-only decision
The `#line` case is gated on `_stack.IsActive`:
```csharp
case "LineDir":
    if (_stack.IsActive)
        ProcessLineDirective(directive, directiveLine.StartPos);
    break;
```
An inactive `#line` is a **no-op** (nothing recorded). This is consistent with `#error`/`#warning`
(also gated on `_stack.IsActive`) and with D4/Roslyn semantics ("`#line` in an inactive region —
no effect"). Verified: `#if A\n#line 42\n#endif\n` with `A` undefined → `LineDirectives.Count == 0`.

## Parsing rules
The `LineDir` grammar is `LineDir = "line" LineEnd;`, so the arguments (rest of the line after
`#line`) live in the `LineEnd` child. `ProcessLineDirective` reads
`directive.Elements.OfType<TerminalNode>().FirstOrDefault(t => t.Kind == "LineEnd")?.ToString(source)`
(the same pattern as `GetMessageText`) and `.Trim()`s it. The trimmed text is then parsed:

| Trimmed text | Result |
|---|---|
| `default` (whole text) | `State=Default`, `MappedLine=null`, `FilePath=null` |
| `hidden` (whole text) | `State=Hidden`, `MappedLine=null`, `FilePath=null` |
| leading integer `N` | `MappedLine=N`; `FilePath` = quoted string if present else `null`; `State` = `Hidden` if a `hidden` token is present else `Remapped` |
| anything else | **malformed → no-op** (nothing recorded) |

Valid leading-integer forms: `N`, `N "file"`, `N hidden`, `N "file" hidden` (file/hidden order
free). A quoted string may contain spaces (e.g. `#line 1 "my file.cs"`).

### Tokenizer (`TokenizeLineDirective`)
A small local tokenizer splits the trimmed text into `(Value, IsQuoted)` tokens:
- whitespace is a delimiter and skipped;
- a `"` starts a quoted string that runs to the next `"` (surrounding quotes stripped, inner
  spaces preserved); an **unterminated** quote returns `null` (malformed);
- otherwise a bare token runs to the next whitespace or `"`.

After tokenizing:
- token 0 must parse as an integer (`int.TryParse` with `NumberStyles.Integer` +
  `CultureInfo.InvariantCulture`); otherwise no-op.
- remaining tokens: at most one quoted string (→ `FilePath`) and at most one bare `hidden`
  (→ `State=Hidden`), in any order. A second quoted string, a second `hidden`, or any other bare
  token → malformed → no-op.

### Malformed / unrecognized → no-op
Any input that does not match one of the forms above records **nothing** (no `LineDirective`
added, no diagnostic). Examples that are no-ops: `#line` (empty), `#line "file"` (no leading
integer), `#line 42 "a" "b"`, `#line 42 hidden hidden`, `#line 42 foo`, `#line 42 "unterminated`.

## How `OriginalLine` is computed
`OriginalLine` = the 1-based original source line of the `#line` directive
= (number of `\n` in `source[..directiveLine.StartPos]`) + 1:
```csharp
private static int ComputeOriginalLine(string source, int startPos)
{
    var line = 1;
    for (var i = 0; i < startPos; i++)
        if (source[i] == '\n')
            line++;
    return line;
}
```
`directiveLine.StartPos` is the start of the `DirectiveLine` node (`Ws* "#" Directive`). Since
`Ws*` only matches spaces/tabs (never newlines), the whole `DirectiveLine` sits on a single line,
so the line number at its start equals the directive's line. Newlines are preserved by blanking
(T2.2), so counting `\n` in the (still original) `source` gives the original line number.

`LineDirective` records are added in source order — the visitor already processes lines in order.

## Key decisions
- **Reuse the `LineEnd`-text pattern** already used by `GetMessageText` (`ToString(source)` on the
  `LineEnd` terminal) — keeps the approach consistent with the rest of the visitor.
- **Invariant-culture integer parse** so a non-ASCII-digit culture cannot misparse a line number.
- **Strict-ish but simple** token validation: after the integer, only `hidden` and one quoted
  string are allowed; anything else is a no-op. Chosen over a fully permissive parser to keep the
  "unrecognized → no-op" guarantee obvious and crash-free.
- **`#line "file"` (file-only, no number) is a no-op** here. The full C# spec allows a
  file-only `#line`, but the task spec lists only `default`, `hidden`, and leading-integer forms,
  so file-only is treated as unrecognized. (Deliberate, per task.)
- **Negative / `+`-prefixed integers** (e.g. `#line -5`) parse and are recorded as-is
  (`NumberStyles.Integer` allows a leading sign). Not rejected; harmless and crash-free.

## Build
```
dotnet build Parsers/CSharp/CsPreprocessor/CsPreprocessor.csproj
```
→ Build succeeded, 0 errors. (One compile iteration: `text[i + 1..j]` needed parentheses —
`text[(i + 1)..j]` — because the range operator parsed as `i + (1..j)`.)

## Sanity check (throwaway, deleted)
Ran `Preprocessor.Run` on a temp console project and printed `LineDirectives`:
```
=== 1 ===  #line 42\n
  LineDirective(OriginalLine:1, MappedLine:42, FilePath:null, State:Remapped)
=== 2 ===  #line 42 "file.cs"\n
  LineDirective(OriginalLine:1, MappedLine:42, FilePath:"file.cs", State:Remapped)
=== 3 ===  #line default\n
  LineDirective(OriginalLine:1, MappedLine:null, FilePath:null, State:Default)
=== 4 ===  #line hidden\n
  LineDirective(OriginalLine:1, MappedLine:null, FilePath:null, State:Hidden)
=== 5 ===  #line 7 hidden\n
  LineDirective(OriginalLine:1, MappedLine:7, FilePath:null, State:Hidden)
=== 6 ===  #if A\n#line 42\n#endif\n   (A undefined)
  LineDirectives.Count = 0
=== 7 ===  int x;\n#line 99 "f.cs"\n
  LineDirective(OriginalLine:2, MappedLine:99, FilePath:"f.cs", State:Remapped)
```
All 7 cases match the expected output exactly. Temp project removed after the check.

## Final state
- `case "LineDir":` records active `#line` directives per the rules above; inactive → no-op.
- `LineDirective(OriginalLine, MappedLine, FilePath, State)` is appended to `_lineDirectives` in
  source order.
- No changes to blanking/text or diagnostics. No committed tests.

## Open questions / deviations
- `#line "file"` (file-only, no number) is a no-op (see Key decisions) — deviation from the full
  C# spec, aligned to the task spec. Flagging in case the test subagent expects it to be recorded.
- `RemappedSpan` state (from the `LineDirectiveState` enum) is not produced by this parser — the
  task's forms do not include a `(l,c)-(l2,c2)` span. Left unused for now.

## Tests (T4.3)
New file: `Tests/CsPreprocessorTests/LineDirectiveTests.cs` (`[TestClass]`, `sealed`, file-scoped
`namespace CsPreprocessorTests`). Helper `Run(source, params string[] symbols) => Preprocessor.Run(...)`
and `ExpectSingle(result)` (asserts exactly 1 `LineDirective`, returns it). Each test drives
`Preprocessor.Run(source, symbols)` and asserts on `PreprocessResult.LineDirectives` (count + each
record's `OriginalLine`/`MappedLine`/`FilePath`/`State`). Tests are stateless/thread-safe
(method-level parallelism).

| Test | Source | Asserts |
|---|---|---|
| `Line_NumberOnly` | `#line 42\n` | 1 dir: `OriginalLine:1, MappedLine:42, FilePath:null, State:Remapped` |
| `Line_NumberAndFile` | `#line 42 "file.cs"\n` | `OriginalLine:1, MappedLine:42, FilePath:"file.cs", State:Remapped` |
| `Line_Default` | `#line default\n` | `OriginalLine:1, MappedLine:null, FilePath:null, State:Default` |
| `Line_Hidden` | `#line hidden\n` | `OriginalLine:1, MappedLine:null, FilePath:null, State:Hidden` |
| `Line_NumberHidden` | `#line 7 hidden\n` | `OriginalLine:1, MappedLine:7, FilePath:null, State:Hidden` |
| `Line_NumberFileHidden` | `#line 7 "f.cs" hidden\n` | `OriginalLine:1, MappedLine:7, FilePath:"f.cs", State:Hidden` |
| `Line_QuotedPathWithSpaces` | `#line 3 "my file.cs"\n` | `OriginalLine:1, MappedLine:3, FilePath:"my file.cs", State:Remapped` |
| `Line_OriginalLineOnLaterLine` | `int x;\nint y;\n#line 99 "f.cs"\n` | `OriginalLine:3, MappedLine:99, FilePath:"f.cs", State:Remapped` |
| `Line_Multiple_InOrder` | `#line 10\n#line 20\n` | 2 dirs in order: `OriginalLine:1, MappedLine:10` then `OriginalLine:2, MappedLine:20` (both `Remapped`, `FilePath:null`) |
| `Line_Inactive_NotRecorded` | `#if A\n#line 42\n#endif\n` (A undefined) | `LineDirectives.Count == 0` |
| `Line_Malformed_NotRecorded` | `#line\n` and `#line abc\n` | `LineDirectives.Count == 0` for each |
| `Line_FileOnly_NoOp` | `#line "file.cs"\n` | `LineDirectives.Count == 0` (documented deviation: file-only, no number, is a no-op) |

### Test result
```
dotnet test Tests/CsPreprocessorTests/CsPreprocessorTests.csproj
  Total: 111 · Passed: 111 · Failed: 0
LineDirectiveTests (12 new): 12 passed / 0 failed
```
All existing + new tests pass. No production bug found.
