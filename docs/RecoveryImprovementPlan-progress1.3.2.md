# 1.3.2 — S6 guaranteed bottom

Sub-point 1.3.2: make S6 (rank 6) the guaranteed progress bottom and raise the
iteration limit so D1.1 (120 small errors) finishes with `Success@EOF`.

## Change 1 — S6 exempt from the per-point budget

File: `ExtensibleParser/Parser.Recovery.cs`, inside `Recover` → local function `TryCandidate`.

The candidate-loop return convention (verified by reading the loop):
- `return false` → "skip this candidate, continue the loop" (same as the
  already-tried `attempts.Contains` guard above it).
- `return true` → "stop the candidate loop" (acceptance / progress / budget-exhausted).

Old branch (budget exhausted → stop the loop, so S6 was unreachable once the
per-point budget was spent):

```csharp
attempts.Add(candidate.Id);
if (attempts.Count > MaxRecoveryAttemptsPerPosition)
    return true; // лимит попыток на точке исчерпан — стоп (не акцепт: recoveredThisIteration не тронут)
```

New branch (budget exhausted → SKIP non-S6, continue; S6 always falls through to be tried):

```csharp
attempts.Add(candidate.Id);
// 1.3.2: S6 (ранг 6) — гарантированное дно, всегда пробуем даже при исчерпанном бюджите;
// остальные кандидаты пропускаются (следующий кандидат), а не стопят цикл.
if (attempts.Count > MaxRecoveryAttemptsPerPosition && candidate.Rank != 6)
    return false;
```

`MaxRecoveryAttemptsPerPosition` is unchanged (default 3).

## Change 2 — Raise MaxRecoveryIterations

File: `ExtensibleParser/Parser.Recovery.cs`, field initializer.

```csharp
// old
public int MaxRecoveryIterations { get; set; } = 64;
// new
public int MaxRecoveryIterations { get; set; } = 1000;
```

64 < 120, so D1.1 (120 small errors, one fixed per iteration) could not reach EOF.
1000 gives ample headroom.

## D1.1 assertion update

File: `Tests/ParserTests/Recovery/RecoveryCorpusTests.cs`, `Test_D1_1_ManySmallErrors`.

Old (Wave-0 baseline, tolerated the not-finished state):

```csharp
Assert.IsTrue(m.SuccessAtEof || m.Passes <= 2 * n, $"D1.1 passes={m.Passes} success@EOF={m.SuccessAtEof}");
```

New (asserts the new contract: D1.1 finishes with Success@EOF):

```csharp
Assert.IsTrue(m.SuccessAtEof, $"D1.1 not recovered to EOF\n{ReportLine(m)}");
```

The D2 counters (`RecoveryPasses` / `EngineGenerateCalls`) remain visible in the
`ReportLine` (Trace) as before. The test asserts only `SuccessAtEof` (no specific
pass count), so no pass-count value needed updating.

## Files changed

- `ExtensibleParser/Parser.Recovery.cs` — S6 (rank 6) exempt from the per-point budget in
  `TryCandidate` (non-S6 skipped when budget exhausted, S6 always tried); `MaxRecoveryIterations`
  default raised 64 → 1000.
- `Tests/ParserTests/Recovery/RecoveryCorpusTests.cs` — D1.1 assertion updated from
  `SuccessAtEof || Passes <= 2n` to `SuccessAtEof` (new contract).

`RecoveryEngine.cs` (candidate generation) was NOT modified.

## D1.1 result

- `Test_D1_1_ManySmallErrors` → **Success@EOF = true** (passes).
- Full `RecoveryCorpusTests` class: **8/8 passed** (D1.1..D1.7 + corpus report).

## I6 (zero cost on correct code) — preserved

`Test_ValidInput_NoRecovery` (clean input `int f() { int x; int y; }`) still asserts
`Success@EOF`, `ErrorInfo == null`, and `RecoveryDiagnostics.Count == 0` — passes.
The changes are inside the recovery loop body, which is only reached after a failed
first pass; the clean-input path returns on the first iteration before any of the
changed code runs. The explicit `MaxRecoveryIterations` tests (values 4 and 0) are
unaffected by the default change and still pass.

## Full-suite counts (Tests/ParserTests)

Stable across 8 consecutive runs (the very first run under the MCP harness showed 1
flaky failure that did not recur in any of the subsequent 7 runs):

- **Passed: 338 · Failed: 0 · Skipped: 2 · Total: 340**
- The 2 skipped are pre-existing `[Ignore("WIP")]` in `GrammarValidationTests.cs`
  (unrelated to this change).

## Deviations

- The task suggested an English inline comment for the budget branch; the codebase uses
  Russian comments, so the comment was written in Russian. The condition and the
  `return false` (skip/continue) convention are exactly as specified.
- No other deviations. `#if RECOVERY`, Allman braces, and 4-space indent preserved.
