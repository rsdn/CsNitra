# T2.1 progress — `DirectiveStack` (pure preprocessor state)

Status: DONE (build clean 0W/0E; throwaway sanity check passes on 7 scenarios incl. the 4
required + 3 extra; throwaway project deleted before finishing).

## Scope (from plan T2.1)
A standalone, tree-free, grammar-free data structure `DirectiveStack` that models the
preprocessor's running state: the **active/inactive** status of the current region and the
**symbol table** (from `#define`/`#undef` + command-line symbols). It must be testable in
isolation: a sequence of operations → verifiable state. Mirrors Roslyn's
`src/Compilers/CSharp/Portable/Parser/Directives.cs` `DirectiveStack` (semantics from the plan's
"Семантика (D4)" section).

## File created
- `Parsers/CSharp/CsPreprocessor/DirectiveStack.cs` — `public sealed class DirectiveStack` +
  two private nested `record`s (`IfFrame`, `DefineOp`). No other file touched.

## Public API (exactly as suggested by the task)
```csharp
public sealed class DirectiveStack
{
    public DirectiveStack(IReadOnlyCollection<string> commandLineSymbols);
    public bool IsActive { get; }
    public bool HasUnfinishedIf { get; }
    public bool If(bool condition);
    public bool Elif(bool condition);
    public bool Else();
    public void EndIf();
    public void Define(string symbol);
    public void Undef(string symbol);
    public bool IsDefined(string symbol);
}
```

## Internal state design
Four fields (all `_camelCase`), no parse tree, no grammar:

| Field | Type | Meaning |
|---|---|---|
| `_commandLineSymbols` | `HashSet<string>` (Ordinal) | `/define` command-line symbols; the `IsDefined` fallback when a symbol was never touched in source. |
| `_ifStack` | `Stack<IfFrame>` | One frame per open `#if`. LIFO; top = closest `#if`. |
| `_defineOps` | `List<DefineOp>` | Applied `#define`/`#undef` ops in source order. **Only ops from active regions are appended** (see below), so the list is the effective symbol history. |
| `_isActive` | `bool` | Current region active state. Starts `true`; the single source of truth for "am I active now". |

Records:
- `IfFrame` (non-positional `sealed record`): `EndIsActive` (`init`, the enclosing `#if`'s
  remembered active state — never changes) + `PreviousBranchTaken` (`set`, whether any earlier
  section of this `#if` chain already took its branch — mutated by `Elif`/`Else`).
- `DefineOp` (positional `sealed record`): `Symbol` + `IsDefined`. Immutable.

Why a field `_isActive` rather than deriving it from the stack: every op that changes activity
(`If`/`Elif`/`Else`/`EndIf`) also pushes/pops the matching frame, so field and stack are always
consistent by construction, and a single `bool` read is the cleanest `IsActive`.

## How each rule is implemented (1–2 lines each)
- **`IsActive`** — returns the `_isActive` field (starts `true`).
- **`If(condition)`** — `branchTaken = _isActive && condition`; push `new IfFrame(_isActive, branchTaken)`
  (remember enclosing active state + that this branch was taken); `_isActive = branchTaken`; return `branchTaken`.
- **`Elif(condition)`** — top frame `f`; `branchTaken = f.EndIsActive && condition && !f.PreviousBranchTaken`;
  `f.PreviousBranchTaken |= branchTaken`; `_isActive = branchTaken`; return `branchTaken`.
- **`Else()`** — top frame `f`; `branchTaken = f.EndIsActive && !f.PreviousBranchTaken`;
  `f.PreviousBranchTaken |= branchTaken`; `_isActive = branchTaken`; return `branchTaken`.
- **`EndIf()`** — pop top frame `f`; `_isActive = f.EndIsActive` (restore the enclosing active state).
- **`Define(symbol)`** — `if (_isActive) _defineOps.Add(new DefineOp(symbol, IsDefined: true))` (most-recent op wins).
- **`Undef(symbol)`** — `if (_isActive) _defineOps.Add(new DefineOp(symbol, IsDefined: false))`.
- **`IsDefined(symbol)`** — walk `_defineOps` from most recent to oldest; first op with `symbol` returns its
  `IsDefined`; if none, fall back to `_commandLineSymbols.Contains(symbol)`.
- **`HasUnfinishedIf`** — `_ifStack.Count > 0` (an open `#if` with no matching `#endif`).

## Key decisions / invariants preserved
- **Inactive-region `#define`/`#undef` has NO effect** — guarded by `if (_isActive)` before appending, so an
  op is only recorded when its region is active. This is the one simplification vs Roslyn: because inactive
  ops are never recorded, `IsDefined` does NOT need Roslyn's "skip-back over `#elif`/`#else` to the matching
  `#if`" logic — the list already contains only the effective (taken-path) ops.
- **Only the first true branch is active** — `PreviousBranchTaken` is set by the `#if` itself (initial value)
  and latched (`|=`) by any taken `#elif`/`#else`; once true, later sections compute `!previousBranchTaken == false`.
- **`#define` in a taken branch persists after `#endif`; in a not-taken branch it does not** — falls out of the
  `if (_isActive)` guard (taken → active → recorded; not-taken → inactive → not recorded). The list is global
  (not scoped to the block), so a taken-branch define outlives the `#endif`.
- **Case-sensitive symbols** — `StringComparer.Ordinal` for the command-line set and ordinal `==` in the
  `IsDefined` walk (C# symbols are case-sensitive, unlike C).
- **Robustness for malformed sequences** — `Elif`/`Else`/`EndIf` with an empty `_ifStack` (a stray
  `#elif`/`#else`/`#endif`) are no-ops (return `false` / do nothing) rather than throwing. Structural
  diagnostics for these belong to T2.3/T4.x; the pure-logic layer must not crash on them.

## Deviations from the task's suggested shape
- **None to the public API** — implemented exactly as suggested.
- **`IfFrame` is a non-positional record with an `init` + a `set` property** (not a positional record) because
  `PreviousBranchTaken` must be mutated in place while `EndIsActive` is fixed. Mutation happens through
  `Stack<IfFrame>.Peek()` (reference type → mutating the peeked frame updates the stack top). `init` works here
  because `Shared/NetStandard2_0Support.cs` polyfills `System.Runtime.CompilerServices.IsExternalInit`
  (required for `init` on netstandard2.0).
- **Ordered list (`List<DefineOp>`) for the symbol table, not a `Dictionary`** — matches the task's
  "ordered list of applied define/undef operations" suggestion; the reverse walk makes "most-recent wins"
  explicit and trivially testable. O(n) per `IsDefined` is fine for a preprocessor (n = applied ops, called
  once per identifier in a condition).

## Verify
- Build: `dotnet build Parsers/CSharp/CsPreprocessor/CsPreprocessor.csproj` → **0 Warning / 0 Error**.
- Sanity check (throwaway net8.0 console project in the temp dir referencing the csproj, **deleted after**):
  7 scenarios, all observed values matched expectations:
  - S1 `Define("A")` + `If(IsDefined("A"))` → ifTaken=True, active=True; `EndIf` → active=True.
  - S2 `If(false)` → ifTaken=False; `Define("B")` (inactive, no effect); `Else()` → elseTaken=True;
    `Define("C")`; `EndIf` → isC=True, isB=False, active=True.
  - S3 nested `If(true){If(true){Define("X")}}` → isX=True, active=True.
  - S4 `If(true)` → `Elif(true)` → elifTaken=False (previous taken) → `Else()` → elseTaken=False →
    `EndIf` → active=True.
  - S5 cmdline fallback: `IsDefined("CMD")`=True, `IsDefined("NOPE")`=False; `Define("A");Undef("A")` →
    isA=False; `Define("A")` again → isA=True (most-recent wins).
  - S6 (extra) `If(false){ If(true){ Define("Y") } } else { Define("Z") }` → innerTaken=False, isY=False
    (inner define in inactive region, no effect), elseTaken=True, isZ=True, active=True.
  - S7 (extra) `HasUnfinishedIf`: False before, True after `If`, True after nested `If`, True after one
    `EndIf`, False after all `EndIf`.

## Final state
- `Parsers/CSharp/CsPreprocessor/DirectiveStack.cs` — new, compiles clean, sanity-checked.
- No other production file changed. No committed tests added (separate test subagent).

## Problems / open questions
- **`HasUnfinishedIf` scope** — implemented as "an open `#if` frame exists" (`_ifStack.Count > 0`), which is the
  natural reading of "an `#if` without a matching `#endif`". A *stray* `#elif`/`#else` with no `#if` is a
  different structural error and is NOT reflected in `HasUnfinishedIf` (those ops are no-ops). If the later
  diagnostic (T2.3) needs to distinguish "stray `#else`" from "unclosed `#if`", the interpreter can detect it
  independently (it knows it called `Else()`/`Elif()` on an empty stack) — no change needed here, but flagging
  in case the test subagent expects `HasUnfinishedIf` to also cover stray `#else`.
- **`Elif`/`Else`/`EndIf` on empty stack are silent no-ops** (return `false` / do nothing). This keeps the pure
  logic crash-free; actual structural diagnostics are T2.3/T4.x. Confirm the test subagent drives only well-formed
  sequences for the branch-taken assertions (or expects these no-op semantics for malformed ones).

## Tests (T2.1)

File added: `Tests/CsPreprocessorTests/DirectiveStackTests.cs` (`[TestClass]`, `public sealed class
DirectiveStackTests`). Pure-logic, tree-free; each test builds a fresh `DirectiveStack` via a small helper
`Create(params string[] commandLineSymbols) => new(commandLineSymbols)` (target-typed `new`, `string[]` →
`IReadOnlyCollection<string>`). No shared state — safe under the project's method-level parallelism. Covers all 12
required scenarios; asserts `IsActive`, `IsDefined`, `HasUnfinishedIf`, and the branch-taken return values.

| # | Test method | Asserts |
|---|---|---|
| 1 | `InitialState_Active_NoUnfinishedIf_CmdlineFallback` | Fresh stack (cmdline `["CMD"]`): `IsActive` true, `HasUnfinishedIf` false; `IsDefined("CMD")` true (cmdline fallback), `IsDefined("NOPE")` false. |
| 2a | `IfTrue_Active_HasUnfinishedIf_EndIfRestoresActive` | `If(true)` → returns true, `IsActive` true, `HasUnfinishedIf` true; `EndIf` → `IsActive` true, `HasUnfinishedIf` false. |
| 2b | `IfFalse_Inactive_EndIfRestoresActive` | `If(false)` → returns false, `IsActive` false, `HasUnfinishedIf` true; `EndIf` → `IsActive` true, `HasUnfinishedIf` false. |
| 3 | `IsDefined_DrivesIf` | `Define("A")` then `If(IsDefined("A"))` → true. |
| 4a | `FirstTrueBranchWins_FalseIf_TrueElif_FalseElse` | `If(false)` → `Elif(true)` → true (IsActive true); `Else()` → false (previous taken); `EndIf` → IsActive true. |
| 4b | `FirstTrueBranchWins_TrueIf_FalseElif_FalseElse` | `If(true)` → `Elif(true)` → false (previous taken); `Else()` → false; `EndIf` → IsActive true. |
| 5 | `Define_Undef_MostRecentWins` | `Define("A")` → `IsDefined("A")` true; `Undef("A")` → false; `Define("A")` → true. |
| 6 | `Define_InInactiveRegion_HasNoEffect` | `If(false)` → `Define("B")` (inactive) → `Else()` (active) → `EndIf` → `IsDefined("B")` false. |
| 7 | `Undef_InInactiveRegion_HasNoEffect` | `Define("A")` → `If(false)` → `Undef("A")` (inactive) → `EndIf` → `IsDefined("A")` still true. |
| 8 | `Define_InTakenBranch_PersistsAfterEndIf` | `If(true)` → `Define("B")` → `EndIf` → `IsDefined("B")` true. |
| 9 | `NestedIf_Active_DefinePersists_AfterEndIfs` | `If(true)` → `If(true)` → `Define("X")` → `EndIf` → `EndIf` → `IsDefined("X")` true, `IsActive` true. |
| 10 | `NestedIf_InactiveOuter_InnerDefineNoEffect_OuterElseActive` | `If(false)` → `If(true)` (inner) → inner taken false, `IsActive` false → `Define("Y")` (no effect) → `EndIf` → `Else()` (outer else, active) → `Define("Z")` → `EndIf` → `IsDefined("Y")` false, `IsDefined("Z")` true. |
| 11 | `HasUnfinishedIf_AcrossPushPop` | false → (`If`) true → (`If`) true → (`EndIf`) true → (`EndIf`) false. |
| 12 | `StrayElseElifEndIf_OnEmptyStack_AreSafeNoOps` | On a fresh stack, `Else()`, `Elif(true)`, `EndIf()` do not throw; `Else`/`Elif` return false; `IsActive` stays true; `HasUnfinishedIf` stays false. |

Notes:
- Test 12 drives the documented no-op semantics for a stray `#else`/`#elif`/`#endif` on an empty stack (the
  "silent no-op" behaviour flagged in the progress file's "Problems / open questions"). It asserts the calls are
  exception-free and that `IsActive` is preserved, without asserting any structural diagnostic (that is T2.3/T4.x).
- No production bug was found: every assertion matched the implementation's behaviour on the first run.

Test result: `dotnet test Tests/CsPreprocessorTests/CsPreprocessorTests.csproj` → **42 passed / 0 failed**
(14 new in `DirectiveStackTests` + 28 pre-existing). `dotnet test --filter` on `DirectiveStackTests` alone →
**14 passed / 0 failed**.
