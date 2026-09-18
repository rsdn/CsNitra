# T4.1 — multiple `#else` / `#elif` after `#else` diagnostics

## What I did

Completed the structural preprocessor diagnostics to match Roslyn by adding the one missing
structural case: **multiple `#else` in one `#if` block** and the related **`#elif` after
`#else`**. Existing diagnostics (T2.3) and blanking behavior (T2.2) are unchanged — this is
purely additive.

### Files modified
1. `Parsers/CSharp/CsPreprocessor/DirectiveStack.cs`
2. `Parsers/CSharp/CsPreprocessor/PreprocessorInterpreter.cs`

## New diagnostic codes / messages

| Constant | Code | Message | Severity | Condition |
|---|---|---|---|---|
| `CodeMultipleElse` | **CS1034** | `Multiple '#else' directives` | Error | `#else` on an open `#if` frame that already has an `#else` |
| `CodeElifAfterElse` | **CS1035** | `'#elif' directive after '#else'` | Error | `#elif` on an open `#if` frame that already has an `#else` |

- **CS1034** matches Roslyn's code for "multiple `#else` directives".
- **CS1035** is our stable code for "`#elif` after `#else`". Roslyn treats this as an error but
  does not expose a dedicated well-known public code we could verify 1:1, so per the task we use
  a distinct stable code and document it here. It is distinct from CS1034 and from all codes we
  already emit (CS1023/1024/1025/1028/1029/1030/1034).

## How `HasElse` is tracked

- Added a settable `HasElse` flag to the private `IfFrame` record.
- `DirectiveStack.Else()`: when it is a valid `#else` (i.e. the `#if` stack is non-empty — the
  same guard that already returns `false` on an empty stack), it now sets
  `frame.HasElse = true`. This happens **regardless of active/inactive** — a `#else` in an
  inactive region still counts as "this frame has seen an `#else`" (matching Roslyn: the
  structural check is about the directive sequence, not the branch activity).
- Exposed `public bool CurrentFrameHasElse => _ifStack.Count > 0 && _ifStack.Peek().HasElse;`.
  This reads the **top** (current/innermost) frame, so a nested `#if`'s `#else` does not leak
  into the outer frame (each frame has its own `HasElse`).

## `PreprocessorInterpreter.ProcessDirective` changes

`Else` case:
- `!_stack.HasUnfinishedIf` → stray `#else` (CS1025, unchanged).
- else if `_stack.CurrentFrameHasElse` → multiple `#else` (CS1034).
- else → valid, call `_stack.Else()`.

`Elif` case:
- `!_stack.HasUnfinishedIf` → stray `#elif` (CS1028, unchanged).
- else if `_stack.CurrentFrameHasElse` → `#elif` after `#else` (CS1035).
- else → valid, call `_stack.Elif(EvaluateCondition(directive))`.

In both cases, when a structural error is emitted the corresponding `stack.Else()/stack.Elif()`
is **not** called, keeping the frame state consistent (the invalid directive is ignored for
state purposes, exactly as the existing stray cases already behaved). The `EndIf` / `_openIfs`
logic is unchanged.

## Key decisions
- `HasElse` is per-frame and set inside `Else()` (single source of truth), not duplicated in the
  visitor. The visitor only *reads* `CurrentFrameHasElse`.
- The check order is: stray first (no open `#if`), then multiple-else, then valid. This keeps the
  existing stray diagnostics byte-for-byte identical.
- Chose CS1035 (documented) for `#elif` after `#else` since Roslyn has no verifiable dedicated
  public code for it.

## Deviations / open questions
- None from the task spec. One open question: whether the user wants `#elif` after `#else` to
  carry a specific Roslyn code rather than our stable CS1035. CS1035 is documented and stable.

## Final state
- Build: `dotnet build Parsers/CSharp/CsPreprocessor/CsPreprocessor.csproj` → 0 errors, 0 warnings.
- Existing suite: `dotnet test Tests/CsPreprocessorTests` → **78 passed, 0 failed** (confirms the
  additive change does not break T2.3 diagnostics or `DirectiveStack` behavior).
- Sanity check (internal throwaway console project, deleted after running). Exact observed output:
  ```
  === 1 multiple #else ===        (#if A / #else / #else / #endif, A defined)
    Error CS1034: Multiple '#else' directives  [start=12 end=18 line=3]
  === 2 elif after else ===       (#if A / #else / #elif B / #endif, A defined)
    Error CS1035: '#elif' directive after '#else'  [start=12 end=20 line=3]
  === 3 single valid #else ===    (#define A / #if A / #else / #endif)
    (no diagnostics)
  === 4 nested #if ===            (#define A / #if A / #if A / #else / #endif / #endif)
    (no diagnostics)
  ```
   All four scenarios behaved as specified: CS1034 fires at the second `#else` (line 3) with no
   stray/other diagnostics; CS1035 fires at the `#elif` (line 3); the single-`#else` and nested
   cases produce no structural diagnostic.

## Tests (T4.1)

New test file: `Tests/CsPreprocessorTests/StructuralDirectiveTests.cs` (`[TestClass]`, MSTest,
stateless/thread-safe — method-level parallelism). Helper `Run(source, params string[] symbols)`
wraps `Preprocessor.Run`; `ExpectSingle` asserts exactly one diagnostic; `LineStart(source, i)`
computes the 0-based offset of line `i` in ORIGINAL coordinates. All assertions are on
`PreprocessResult.Diagnostics` (count, `Code`, `Severity`, `StartPos`).

| # | Test | Source | Asserts |
|---|---|---|---|
| 1 | `Multiple_Else_CS1034` | `#define A\n#if A\n#else\n#else\n#endif\n` | exactly 1 diagnostic: `Code="CS1034"`, `Severity=Error`, `StartPos`=line 3 (second `#else`) |
| 2 | `Elif_After_Else_CS1035` | `#define A\n#if A\n#else\n#elif B\n#endif\n` | exactly 1: `Code="CS1035"`, `Severity=Error`, `StartPos`=line 3 (`#elif`) |
| 3 | `Single_Valid_Else_NoDiagnostic` | `#define A\n#if A\n#else\n#endif\n` | 0 diagnostics |
| 4 | `Nested_Inner_Else_DoesNotLeak_NoDiagnostic` | `#define A\n#if A\n#if A\n#else\n#endif\n#endif\n` | 0 diagnostics (inner `#else` does not leak to outer) |
| 5 | `Nested_Inner_Multiple_Else_CS1034_AtInner` | `#define A\n#if A\n#if A\n#else\n#else\n#endif\n#endif\n` | exactly 1: `Code="CS1034"`, `StartPos`=line 4 (inner second `#else`, not the outer) |
| 6 | `Stray_Else_CS1025` | `#else\n` | exactly 1: `Code="CS1025"`, `Severity=Error`, `StartPos`=0 |
| 7 | `Stray_Elif_CS1028` | `#elif A\n` | exactly 1: `Code="CS1028"`, `Severity=Error`, `StartPos`=0 |
| 8 | `Stray_EndIf_CS1023` | `#endif\n` | exactly 1: `Code="CS1023"`, `Severity=Error`, `StartPos`=0 |
| 9 | `Unterminated_If_CS1024` | `#if A\nint x;\n` | exactly 1: `Code="CS1024"`, `Severity=Error`, `StartPos`=0 (the `#if` line) |
| 10 | `Balanced_Nested_Block_NoDiagnostic` | `#define A\n#if A\n#if A\nint x;\n#else\nint y;\n#endif\n#else\nint z;\n#endif\n` | 0 diagnostics (well-formed 2-level nested block) |
| 11 | `Inactive_Region_Multiple_Else_Still_CS1034` | `#if A\n#else\n#else\n#endif\n` (A undefined → region inactive) | exactly 1: `Code="CS1034"`, `StartPos`=line 2 (a `#else` in an inactive region still counts) |

### Test result

- `dotnet test Tests/CsPreprocessorTests/CsPreprocessorTests.csproj` → **89 passed, 0 failed**
  (78 pre-existing + 11 new).
- New class in isolation: `--filter "FullyQualifiedName~StructuralDirectiveTests"` → **11 passed, 0 failed**.

No production bug found — every structural diagnostic (the T2.3 set plus the T4.1 additions)
matches the expected code, severity, and original-coordinate position, including the nested-frame
isolation and the inactive-region `#else` counting.
