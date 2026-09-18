# T4.5 — Line-structure edge cases (shebang, CRLF/LF, ws before `#`, `#` after a token)

## Goal
Verify the preprocessor's line-structure edge cases via `Preprocessor.Run(source, symbols)` →
`PreprocessResult { Text, Diagnostics, LineDirectives }`:
- **Shebang `#!`** at offset 0: a `#!...` line is a shebang directive (blanked, no symbol effect,
  no structural diagnostic).
- **CRLF vs LF**: both line endings work (same-length blanking D2 holds; newlines preserved).
- **Whitespace before `#`**: a line like `   #if A` (leading ws then `#`) is a directive.
- **`#` after a token in a line (bad placement)**: a line like `int x; #define FOO` is CODE, not a
  directive (the `#define` does not take effect).

## Status: DONE

## Test file
- `Tests/CsPreprocessorTests/LineStructureEdgeTests.cs` (`[TestClass]`, `sealed`).
- Helper `Run(source, params string[] symbols)`. Assertions on `Text` (blanking/keeping, same-length)
  and `Diagnostics`. Line spans computed from `source` (`LineBounds`, trailing `\n` inclusive, so it
  works for both LF and CRLF). Stateless/thread-safe (method-level parallelism).
- Shared assertion helpers:
  - `AssertSameLength(source, result)` — `Text.Length == source.Length`.
  - `AssertNewlinesPreserved(source, text)` — for every `i`, `(source[i]=='\r')==(text[i]=='\r')`
    and `(source[i]=='\n')==(text[i]=='\n')`.
  - `AssertLineKept(source, text, lineIndex)` — the line span is byte-identical in `Text` and `source`.
  - `AssertLineBlanked(source, text, lineIndex)` — the line span in `Text` is all `' '/'\n'/'\r'`, and
    the source line actually had blankable content (non-vacuous).

## Tests + assertions
1. **`Shebang_AtOffset0_Blanked_CodeKept`** — `Run("#!/usr/bin/env csi\nint x;\n")`:
   line 0 (`#!...`) blanked, line 1 (`int x;`) kept verbatim, `Text.Length == source.Length`,
   0 diagnostics.
2. **`Shebang_DoesNotDefineSymbols_RegionInactive`** — `Run("#!define FOO\n#if FOO\nint x;\n#endif\n")`:
   `#!define FOO` is a shebang (not `#define`), so `FOO` is NOT defined; line 2 (`int x;`) blanked
   (inactive region); 0 diagnostics.
3. **`CRLF_Newlines_Preserved`** — `Run("#define FOO\r\n#if FOO\r\nint x;\r\n#endif\r\n")`:
   line 2 (`int x;`) kept verbatim, `Text.Length == source.Length`, `\r\n` preserved at the same
   positions, 0 diagnostics.
4. **`Mixed_CRLF_And_LF_NewlinesPreserved`** — `Run("#define FOO\r\nint a;\nint b;\r\n")`:
   `Text.Length == source.Length`, both `\r\n` and `\n` preserved at their positions, lines 1
   (`int a;`) and 2 (`int b;`) kept verbatim.
5. **`Whitespace_BeforeHash_IsDirective`** — `Run("   #define FOO\n   #if FOO\nint x;\n   #endif\n")`:
   the indented directives are real directives (`FOO` defined), so line 2 (`int x;`) kept verbatim;
   lines 0/1/3 blanked; `Text.Length == source.Length`; 0 diagnostics.
6. **`Hash_AfterToken_IsCode_NotDirective`** — `Run("int x; #define FOO\n#if FOO\nint y;\n#endif\n")`:
   line 0 (`int x; #define FOO`) is CODE (kept verbatim, `#define` does NOT take effect), so `FOO`
   is NOT defined and line 2 (`int y;`) is blanked; 0 diagnostics.
7. **`SameLength_Invariant_AcrossAllInputs`** — for inputs 1–6, assert `Text.Length == source.Length`.

## Production bug found
None. All expected behaviors hold: the shebang is classified as a `Shebang` directive (blanked, no
symbol effect), CRLF/LF/mixed newlines are preserved at the same positions under the same-length
blanking, a `#` after leading whitespace is a directive, and a `#` after a token is a code line
(the embedded `#define` does not define the symbol).

## Test result
- `dotnet test Tests/CsPreprocessorTests/CsPreprocessorTests.csproj` (workdir `C:\RSDN\CsNitra`)
  → **124 passed / 0 failed** (existing + 7 new).
- New class only: `LineStructureEdgeTests` → **7 passed / 0 failed**.
