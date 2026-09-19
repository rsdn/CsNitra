# 1.3.4 — Unrecovered emission (S6 is the fallback)

Sub-point 1.3.4 (A4-2 "report and continue"): when **S6 (rank 6) is the fallback** — i.e. S6 is the
candidate that gives progress and no repair candidate (S0–S5) did — emit
`RecoveryDiagnostic(Kind.Unrecovered, E, …)` at the recovery point `E` so the consumer knows the
error at `E` was not truly repaired (only absorbed to EOF).

## The gate (NOT a budget heuristic)

The reverted previous attempt gated on
`attempts.Count > MaxRecoveryAttemptsPerPosition && candidate.Rank == 6` — a budget heuristic tuned
to a contrived `aX` test that needed 4+ S1 insertions to exhaust the budget of 3. That was wrong.

The correct gate is simply **`candidate.Rank == 6`** (S6 is the accepted candidate). Justification:
the `Recover` loop tries candidates in deterministic order (S0 first, then the engine's sorted
S1–S6) and **breaks on the first candidate that gives progress** (Success@EOF or `e2 > e`).
Therefore, if the accepted candidate is S6, no repair candidate (S0–S5) gave progress at this
recovery point — S6 is, by construction, the fallback. No budget term is involved.

## Emission location

File: `ExtensibleParser/Parser.Recovery.cs`, inside `Recover` → local function `TryCandidate`.

`TryCandidate` has two acceptance branches — Success@EOF and the `e2 > e` progress branch. Both
accept the candidate (set `result`, `recoveredThisIteration = true`, `return true`). A small local
helper `AddUnrecoveredIfS6()` was added and called in **both** branches, right after
`_recoveryDiagnostics.AddRange(candidate.Diagnostics);`:

```csharp
// 1.3.4: S6 (ранг 6) — дно, а не восстановление: ошибка в точке e не «починена», а лишь
// поглощена до EOF (абсорбер). Отчёт и продолжение (A4-2): отметка Unrecovered в точке
// восстановления e. Gate — candidate.Rank == 6 (S6 — принятый кандидат): цикл пробует
// кандидатов по порядку и выходит при первом дающем прогресс, поэтому если принят S6 —
// ни один ремонтный (S0–S5) прогресса не дал. НЕ бюджетная эвристика (attempts.Count).
void AddUnrecoveredIfS6()
{
    if (candidate.Rank != 6)
        return;
    _recoveryDiagnostics.Add(new RecoveryDiagnostic(e, e, RecoveryKind.Unrecovered, $"error at {e} not recovered (absorbed to EOF)", null, startRule));
}

if (next.TryGetSuccess(out _, out var end2) && end2 == input.Length)
{
    _recoveryDiagnostics.AddRange(candidate.Diagnostics);
    AddUnrecoveredIfS6();
    result = next;
    recoveredThisIteration = true;
    return true; // полностью восстановлено
}
if (e2 > e)
{
    _recoveryDiagnostics.AddRange(candidate.Diagnostics);
    AddUnrecoveredIfS6();
    result = next;
    recoveredThisIteration = true;
    return true; // I1: прогресс — к следующему итеративному проходу
}
```

`candidate.Rank == 6` identifies S6 uniquely — only `RecoveryEngine.GenerateS6` emits rank-6
candidates (S0=0, S1=0/1, S2=2, S3=3, S4=4, S5=5). A repair candidate (S0–S5) accepted therefore
never emits `Unrecovered`. `#if RECOVERY`, Allman braces, `=>`-style, 4-space indent preserved.

## Diagnostic shape

`new RecoveryDiagnostic(e, e, RecoveryKind.Unrecovered, $"error at {e} not recovered (absorbed to EOF)", null, startRule)`

| Field | Value |
|---|---|
| `Kind` | `RecoveryKind.Unrecovered` |
| `StartPos` / `EndPos` | `e` / `e` — a point diagnostic at the recovery point `E` |
| `Message` | `error at {e} not recovered (absorbed to EOF)` |
| `Terminal` | `null` |
| `RuleName` | `startRule` (the recovery context) |

Added to the parser's `RecoveryDiagnostics` list (`_recoveryDiagnostics`) alongside the candidate's
own diagnostics (the S6 `Skipped` "bottom skip to …" diagnostic still fires as before).

## Test — `Tests/ParserTests/Recovery/UnrecoveredTests.cs` (4 tests)

General, **not** budget-tuned. Terminal class `UnrecoveredTerminals` (`Number` `\d+`, `Trivia` `\s*`).

### 1. `Test_S6_Fallback_EmitsUnrecovered` (and `…_UnrecoveredAtRecoveryPoint`, `…_IndependentOfBudget`)

Grammar: `Module := Seq(Expr, Expr)`, `Expr := Number`. Input: `"12 34 ###"`.

Why S6 is the fallback **independent of budget**:
- First pass parses `Module` = two `Number`s ("12", "34") and succeeds below EOF; the trailing
  `"###"` matches no expected terminal. The pass ends in `Success` **with no mismatch**, so
  `snapshot == null`.
- With `snapshot == null`, `RecoveryEngine.Generate` emits **only S6** (S1–S4 need a snapshot;
  S5 returns early on `snapshot == null`; S6 is emitted because `parseEnd < EOF`). S0 (reparse as-is)
  gives no progress. So S6 is the *only* candidate and hence the fallback.
- Attempts at the point: S0 + S6 = 2, which is ≤ the default budget of 3 — so S6 is accepted even
  with the default budget, and identically with a larger one. The result does **not** depend on
  `MaxRecoveryAttemptsPerPosition`.

Assertions:
- Parse reaches `Success@EOF` (`end == input.Length`), `ErrorInfo == null`.
- `RecoveryDiagnostics` has ≥1 `Unrecovered`.
- The `Unrecovered` diagnostic is at the recovery point `E = input.IndexOf("###")` (the boundary
  between the valid prefix and the trailing garbage).
- The budget-independence test repeats the above for `MaxRecoveryAttemptsPerPosition` ∈ {3, 16, 1000}.

### 2. `Test_RepairCandidate_Accepted_NoUnrecovered`

Grammar: `Module := Seq(Literal("a"), Literal("b"), Literal("c"))`. Input: `"a c"` (missing `"b"`).

A mismatch occurs at the `"b"` slot → S1 (rank 1) inserts `"b"` → `Success@EOF`. S1 (a repair
candidate, not S6) is the accepted candidate, so **no** `Unrecovered` is emitted.

Assertions:
- Parse reaches `Success@EOF`, `ErrorInfo == null`.
- ≥1 `Inserted`/`Skipped` diagnostic is present (confirms a repair candidate was actually accepted —
  the test is not a trivial clean success).
- `RecoveryDiagnostics` has **0** `Unrecovered`.

## Files changed

- `ExtensibleParser/Parser.Recovery.cs` — added `AddUnrecoveredIfS6()` local helper in `TryCandidate`
  and call it in both acceptance branches (Success@EOF and `e2 > e` progress); emits
  `RecoveryDiagnostic(Kind.Unrecovered, e, …)` gated on `candidate.Rank == 6`.
- `Tests/ParserTests/Recovery/UnrecoveredTests.cs` — new: 4 tests (S6-fallback → Unrecovered at E,
  budget-independent; S1 repair accepted → no Unrecovered).
- `docs/RecoveryImprovementPlan-checklist.md` — 1.3.4 `[~]` → `[✅]` with the corrected gate wording
  (was pre-marked `[~]` before this task; the "budget exhaustion" phrasing was corrected to
  "S6 is the accepted fallback candidate").

`RecoveryEngine.cs` was **NOT** modified.

## Test results

- `dotnet build Tests/ParserTests/ParserTests.csproj` — **0 errors, 0 warnings**.
- `dotnet test Tests/ParserTests/ParserTests.csproj` — **Total: 344 · Passed: 342 · Failed: 0 · Skipped: 2**.
  - The 2 skipped are pre-existing `[Ignore("WIP")]` in `GrammarValidationTests.cs` (unrelated).
  - Baseline before this change was 340 total / 338 passed / 2 skipped; the +4 passed / +4 total is
    exactly the 4 new `UnrecoveredTests`.

## Deviations

- The checklist line 1.3.4 was already marked `[~]` (in progress) in the working tree before this
  task started (not by this change). It was advanced to `[✅]` and its description corrected from
  "on per-point budget exhaustion" to the actual gate ("S6 is the accepted fallback candidate"),
  consistent with the other completed sub-points.
- The diagnostic is a **point** diagnostic at `E` (`StartPos == EndPos == e`), matching the task's
  "carries position E". The S6 `Skipped` "bottom skip to …" diagnostic (spanning the absorber region)
  is still emitted separately and unchanged.
- Not committed.
