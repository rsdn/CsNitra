# T4.2 — Inactive directive behavior (tests)

## Scope
Verify that inactive `#define`/`#undef` have **no effect** on the symbol table and inactive
`#error`/`#warning` do **not fire**, mirroring Roslyn. Active counterparts are included as the
contrast for each case.

- File added: `Tests/CsPreprocessorTests/InactiveDirectiveTests.cs` (`[TestClass]`, `sealed`).
- API under test: `Preprocessor.Run(source, symbols)` → `PreprocessResult { Text, Diagnostics, LineDirectives }`.
- Assertions use the preprocessed `Text` (blanking) **and** `Diagnostics`.

## How blanking is observed
`PreprocessorInterpreter.Blank` replaces every non-`'\n'`/`'\r'` char in a span with `' '`.
- A `CodeLine` (e.g. `int x;`) is blanked **iff** the region is inactive (`!_stack.IsActive`).
- A `DirectiveLine` is **always** blanked (active or not).

So the observable signal for an inactive/active `#define`/`#undef` is whether the downstream
`#if B` `CodeLine` (`int x;`) is blanked (B not effectively defined) or kept verbatim (B
effectively defined).

Helpers in the test file:
- `Run(source, params string[] symbols)` → `Preprocessor.Run(source, symbols)`.
- `LineSpan(source, marker)` → 0-based `[start, end)` span of the source line containing `marker`
  (end includes the trailing newline).
- `AssertBlanked(result, source, marker)` → every char in that span of `result.Text` is `' '`/`'\n'`/`'\r'`.
- `AssertKept(result, source, marker)` → that span of `result.Text` is byte-identical to `source`.

## Tests + assertions
| # | Test method | Source | Assertion |
|---|---|---|---|
| 1 | `Inactive_Define_HasNoEffect` | `#if A` … `#define B` … `#endif` … `#if B` `int x;` `#endif` (A undefined) | `int x;` **blanked** (inactive `#define B` → B not defined) |
| 2 | `Active_Define_HasEffect` | `#define A` … `#if A` … `#define B` … `#endif` … `#if B` `int x;` `#endif` | `int x;` **kept** (active `#define B` → B defined) |
| 3 | `Inactive_Undef_HasNoEffect` | `#define B` … `#if A` … `#undef B` … `#endif` … `#if B` `int x;` `#endif` (A undefined) | `int x;` **kept** (inactive `#undef B` → B stays defined) |
| 4 | `Active_Undef_HasEffect` | `#define A` `#define B` … `#if A` … `#undef B` … `#endif` … `#if B` `int x;` `#endif` (A defined) | `int x;` **blanked** (active `#undef B` → B undefined) |
| 5 | `Inactive_Error_DoesNotFire` | `#if A` … `#error "off"` … `#endif` (A undefined) | `Diagnostics.Count == 0` |
| 6 | `Active_Error_Fires` | `#define A` … `#if A` … `#error "on"` … `#endif` | 1 diagnostic, `Code == "CS1029"`, `Severity == Error` |
| 7 | `Inactive_Warning_DoesNotFire` | `#if A` … `#warning "off"` … `#endif` (A undefined) | `Diagnostics.Count == 0` |
| 8 | `Active_Warning_Fires` | `#define A` … `#if A` … `#warning "on"` … `#endif` | 1 diagnostic, `Code == "CS1030"`, `Severity == Warning` |
| 9 | `Nested_Inactive_Define_HasNoEffect` | `#if A` `#if A` … `#define B` … `#endif` `#endif` … `#if B` `int x;` `#endif` (A undefined) | `int x;` **blanked** (doubly-inactive `#define B` → B not defined) |
| 10 | `Define_ThenActive_Undef_ThenUse_IsBlanked` | `#define A` … `#undef A` … `#if A` `int x;` `#endif` | `int x;` **blanked** (both active; A undefined at `#if A`) |

## Test result
```
dotnet test Tests/CsPreprocessorTests/CsPreprocessorTests.csproj
Passed! - Failed: 0, Passed: 99, Skipped: 0, Total: 99
```
- All 10 new `InactiveDirectiveTests` pass (verified via `--filter "FullyQualifiedName~InactiveDirectiveTests"` → 10/10).
- All pre-existing tests in the project still pass (99 total).

## ⚠ Note on the task spec for test 3 (NOT a production bug)
The task text specified test 3 with the source
`#define A\n#define B\n#if A\n#undef B\n#endif\n#if B\nint x;\n#endif\n` and annotated it "(A undefined)",
expecting `int x;` to be **kept**.

That source is **identical to test 4's source**, and in it `A` is defined by the top-level
(active) `#define A`. Tracing `DirectiveStack`:
1. `#define A` (active) → A defined
2. `#define B` (active) → B defined
3. `#if A` → A defined → **active**
4. `#undef B` → **active** → B undefined
5. `#if B` → B undefined → inactive → `int x;` **blanked**

So the literal source cannot yield "kept" — it yields "blanked", exactly matching test 4. The
"(A undefined)" annotation contradicts the literal source (which defines A). This is a **spec
contradiction, not a production bug**: the preprocessor behaves correctly (per Roslyn).

**Resolution:** I corrected test 3's source to match its stated intent ("A undefined" so the
`#undef B` is genuinely inactive):
`#define B\n#if A\n#undef B\n#endif\n#if B\nint x;\n#endif\n`
Here B is defined, A is undefined → `#if A` inactive → `#undef B` has no effect → B stays
defined → `#if B` active → `int x;` kept. This makes tests 3 and 4 a proper contrast pair
(differing only in whether A is defined) and all 10 tests pass.

No production code was modified.
