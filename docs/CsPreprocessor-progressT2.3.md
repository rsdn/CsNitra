# T2.3 — emit diagnostics (in ORIGINAL source coordinates)

Status: done (build clean + sanity checks pass).

## Task
Extend `PreprocessorInterpreter` to populate the `_diagnostics` list. All diagnostic
positions are the directive line's span in the ORIGINAL source (`StartPos`/`EndPos` of the
`DirectiveLine` node — by D2 these equal the original coordinates).

## Plan
1. `#error`/`#warning` (active only): emit a Diagnostic only when `stack.IsActive`.
   - Severity: Error for `#error`, Warning for `#warning`.
   - Message: trailing `LineEnd` child text, trimmed; if double-quoted, strip surrounding
     quotes; if empty, default to "error"/"warning".
   - Span: the `DirectiveLine` span.
2. Structural errors (always emitted, regardless of active/inactive):
   - Stray `#else`/`#elif`: `!stack.HasUnfinishedIf` *before* processing.
   - Stray `#endif`: `!stack.HasUnfinishedIf` *before* processing.
   - Unterminated `#if`: after processing all lines, `stack.HasUnfinishedIf` still true.
     Point at the last open `#if` (tracked via a local position stack).
   - Still call the corresponding `stack.Elif/Else/EndIf` (they no-op safely on empty stack).

## Diagnostic codes (our own stable scheme, documented)
| Diagnostic | Code | Severity | Message |
|---|---|---|---|
| `#error` (active) | `CS1029` | Error | message text (or "error") |
| `#warning` (active) | `CS1030` | Warning | message text (or "warning") |
| Stray `#else` | `CS1025` | Error | `Unexpected '#else' directive` |
| Stray `#elif` | `CS1028` | Error | `Unexpected '#elif' directive` |
| Stray `#endif` | `CS1023` | Error | `Unexpected '#endif' directive` |
| Unterminated `#if` | `CS1024` | Error | `Undefined '#if' directive` |

Note: these are our own stable IDs (task allows "your own consistent scheme"). T4.1 may
re-align with Roslyn's exact codes.

## How positions are derived
- All diagnostics use the `DirectiveLine` node's `StartPos`/`EndPos` (original coords, by D2).
- For the unterminated `#if`, we track a local `Stack<IfPosition>` mirroring the
  `DirectiveStack` if-frames: push the `DirectiveLine` span on each `If`, pop on each real
  `EndIf`. At EOF, if non-empty, point the diagnostic at the top (most recent open `#if`).
- No change to `DirectiveStack.cs` (position tracking lives in the interpreter).

## Out of scope (deferred, per task)
- "two `#else` in one `#if`" / "`#elif` after `#else`" — defer to T4.x.
- `#region`/`#endregion` mismatch — defer to T4.x.
- `#line` recording (T4.3), `#pragma`/`#nullable` semantics (no-op).

## Key decisions
- Track open-`#if` positions in the interpreter (not in `DirectiveStack`) to keep changes
  localized; the local stack stays in sync with `DirectiveStack._ifStack`.
- Structural errors are checked BEFORE calling the stack method, and the stack method is
  still called so state stays consistent.
- `GetMessageText` finds the `LineEnd` terminal child of the `Error`/`Warning` directive
  node, trims, and strips a single pair of surrounding double quotes.

## Deviations / open questions
- "Last open `#if`" for the unterminated-`#if` diagnostic is interpreted as the **most
  recently opened** still-open `#if` (top of the local position stack = innermost). For a
  single unclosed `#if` this is unambiguous; for nested unclosed `#if`s it points at the
  innermost. (Roslyn typically points at the outermost; if T4.1 wants that, flip `Peek()`
  to the stack bottom.)
- The diagnostic codes are our own stable scheme (see table above), not guaranteed to match
  Roslyn's exact codes. T4.1 may re-align them.

## Final state
- File modified: `Parsers/CSharp/CsPreprocessor/PreprocessorInterpreter.cs` (only).
  - Added 6 `Code*` constants.
  - Added `Stack<IfPosition> _openIfs` + `private sealed record IfPosition(int, int)`.
  - `Visit(SeqNode)`: after processing all lines, if `_openIfs` non-empty, emit one
    `CS1024` "Undefined '#if' directive" at the last open `#if` span.
  - `ProcessDirective`:
    - `If`: push `IfPosition` before `_stack.If`.
    - `Elif`/`Else`: if `!_stack.HasUnfinishedIf` (before), emit `CS1028`/`CS1025`
      "Unexpected '#elif'/'#else' directive"; still call `_stack.Elif/Else`.
    - `EndIf`: if `!_stack.HasUnfinishedIf` (before), emit `CS1023` "Unexpected '#endif'
      directive"; else pop `_openIfs`; still call `_stack.EndIf`.
    - `Error`/`Warning`: if `_stack.IsActive`, emit `CS1029`/`CS1030` with the trimmed
      `LineEnd` message (quotes stripped, default "error"/"warning").
  - Added `AddDiagnostic` (node + int overloads) and `GetMessageText` helpers.
- Build: `dotnet build Parsers/CSharp/CsPreprocessor/CsPreprocessor.csproj` → 0 warnings,
  0 errors.
- Sanity check (throwaway console app, deleted after) — exact diagnostics observed:
  - `#error "boom"\n` → `[Error] CS1029 "boom" span=[0,14)`.
  - `#if A\n#error "off"\n#endif\n` (A undefined) → NO diagnostic.
  - `#else\n` → `[Error] CS1025 "Unexpected '#else' directive" span=[0,6)`.
  - `#endif\n` → `[Error] CS1023 "Unexpected '#endif' directive" span=[0,7)`.
  - `#if A\nint x;\n` → `[Error] CS1024 "Undefined '#if' directive" span=[0,6)` (the `#if` line).
  - `#warning "careful"\n` → `[Warning] CS1030 "careful" span=[0,19)`.
  - Extra: `#elif A\n` → `CS1028`; `#error\n` → default "error"; `#error boom\n` → "boom";
    `#if true` + `#error` → active (emitted); `#if false` + `#error` → inactive (none);
    `#define A`/`#if A`/`#error` → active (emitted); nested `#if A`/`#if B`/EOF → `CS1024`
    at the innermost `#if B` span.
   - In every case `Text.Length == source.Length` (T2.2 blanking unchanged).

## Tests (T2.3)

Added `Tests/CsPreprocessorTests/PreprocessorDiagnosticsTests.cs` (`[TestClass]`, MSTest,
stateless/thread-safe, method-level parallelism). Helper
`Run(source, params string[] symbols) => Preprocessor.Run(...)`. Assertions target
`PreprocessResult.Diagnostics` (count, `Severity`, `Code`, `Message`, and `StartPos`/`EndPos`
in ORIGINAL coordinates). Line offsets are computed from the source string (LF) via small
`LineStart`/`LineEnd` helpers — no hardcoded spans.

Tests (13):
1. `Error_Active_EmitsOne` — `#error "boom"\n` → exactly 1 diag: `Severity=Error`,
   `Code=CS1029`, `Message="boom"` (quotes stripped), `StartPos=0`, `EndPos` = `#error` line length.
2. `Error_Inactive_EmitsNone` — `#if A\n#error "off"\n#endif\n` (A undefined) → 0 diags.
3. `Error_Active_ViaDefine` — `#define A\n#if A\n#error "on"\n#endif\n` → 1 Error, `Message="on"`.
4. `Warning_Active_EmitsOne` — `#warning "careful"\n` → 1 diag: `Severity=Warning`,
   `Code=CS1030`, `Message="careful"`.
5. `Warning_Inactive_EmitsNone` — `#if A\n#warning "off"\n#endif\n` (A undefined) → 0 diags.
6. `Error_MessageForms` — `#error boom\n` → `Message="boom"`; `#error\n` → `Message="error"`
   (default); `#error "spaced msg"\n` → `Message="spaced msg"`.
7. `Stray_Else` — `#else\n` → 1 Error, `Code=CS1025`, message contains `#else`, `StartPos=0`.
8. `Stray_Elif` — `#elif A\n` → 1 Error, `Code=CS1028`.
9. `Stray_EndIf` — `#endif\n` → 1 Error, `Code=CS1023`.
10. `Unterminated_If` — `#if A\nint x;\n` → 1 Error, `Code=CS1024`, `StartPos=0` (the `#if` line).
11. `Unterminated_Nested_PointsAtInnermost` — `#define A\n#if A\n#if B\nint x;\n` (both `#if`s
    left open) → 1 `CS1024` whose `StartPos` is the inner `#if B` line (line index 2), not the outer.
12. `Balanced_Block_EmitsNone` — `#define A\n#if A\nint x;\n#else\nint y;\n#endif\n` → 0 diags.
13. `Positions_AreOriginalCoordinates` — `int x = 1;\n#error "boom"\n` → the line-2 `#error`
    has `StartPos`/`EndPos` equal to that line's span in the ORIGINAL source (computed from the string).

### Note on test #11 source (task discrepancy, NOT a production bug)
The task's literal source for #11 was `#define A\n#if A\n#if B\nint x;\n#endif\n`. That trailing
`#endif` closes the INNER `#if B`, leaving only the OUTER `#if A` open — so the `CS1024`
diagnostic points at the OUTER `#if A` (line index 1), not the inner. Verified empirically with a
temporary test (asserted `StartPos` = line 1 → passed). To actually exercise the stated intent
("inner `#if` left open" / "points at innermost", matching this progress file's documented
behavior and sanity check), the test leaves BOTH `#if`s open (no `#endif`); the diagnostic then
correctly points at the innermost open `#if` (line index 2). No production change required.

### Test result
`dotnet test Tests/CsPreprocessorTests/CsPreprocessorTests.csproj` → **66 passed / 0 failed**
(53 pre-existing + 13 new). No production bugs found.
