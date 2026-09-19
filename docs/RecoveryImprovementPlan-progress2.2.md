# Recovery Improvement — Progress 2.2 (A2): budgets by tiers, not candidates

**Scope:** replace the per-position *candidate* budget (`MaxRecoveryAttemptsPerPosition`, a single
number counting every candidate tried at a recovery point) with per-**tier** sub-budgets, so that a
tier with many candidates (S1 — insertions) cannot starve the later tiers (S2/S3/S6). An "attempt"
is scoped per tier; S6 (rank 6) stays the guaranteed fallback (exempt, per 1.3.2).

## The tier-budget logic (before / after)

### Before — one shared per-position candidate budget (`Parser.Recovery.cs`, `TryCandidate`)

```csharp
if (attempts.Contains(candidate.Id))
    return false;                       // already tried at this point
attempts.Add(candidate.Id);
// S6 (rank 6) always tried; others skipped once the shared budget is exhausted
if (attempts.Count > MaxRecoveryAttemptsPerPosition && candidate.Rank != 6)
    return false;
```

- `_attempts` was `Dictionary<int, HashSet<string>>` — the set of candidate **ids** tried at point `e`.
- `MaxRecoveryAttemptsPerPosition` (default 3) counted **all** candidates regardless of tier.
- Consequence: S1 emits up to `|FollowSet|+2` ≈ 9–12 insertion candidates; with a shared budget of 3
  only `S0 + 2×S1` are tried and S2/S3/S5 (rank 2/3/5) are **starved** (never reached).

### After — per-tier sub-budgets (`Parser.Recovery.cs`, `TryCandidate`)

```csharp
if (attempts.TriedIds.Contains(candidate.Id))
    return false;                       // already tried at this point
attempts.TriedIds.Add(candidate.Id);
// S0 (rank 0) baseline re-parse and S6 (rank 6) guaranteed bottom (1.3.2) are EXEMPT.
// Everything else is gated by its own tier's sub-budget, so S1's many insertions
// cannot starve S2/S3/S6.
if (candidate.Rank is not 0 and not 6)
{
    var count  = candidate.Rank switch { 1 => attempts.S1, 2 => attempts.S2, _ => attempts.S3S6 };
    var budget = candidate.Rank switch { 1 => S1TierBudget, 2 => S2TierBudget, _ => S3S6TierBudget };
    if (count >= budget)
        return false;                   // this tier's sub-budget exhausted — next candidate, loop continues
}
switch (candidate.Rank)
{
    case 1: attempts.S1++; break;
    case 2: attempts.S2++; break;
    case 3: case 4: case 5: attempts.S3S6++; break;
}
```

- `_attempts` is now `Dictionary<int, TierBudget>`; `TierBudget` holds the dedup set (`TriedIds`) plus
  one counter per tier (`S1`, `S2`, `S3S6`).
- Tier grouping by rank: **S1** = rank 1; **S2** = rank 2; **S3/S6** = ranks 3, 4, 5.
- **S0** (rank 0, baseline re-parse) and **S6** (rank 6, guaranteed bottom) are exempt — always tried.
- A candidate is counted against its tier only when it is actually attempted (passes the gate).

## The sub-budget values

Constants / instance properties on `Parser` (A3 `RecoveryProfile` is Wave 4.1 — intentionally NOT done here):

| Property | Default | Meaning |
|---|---|---|
| `S1TierBudget` | 4 | number of S1 insertion candidates allowed at a point |
| `S2TierBudget` | 2 | S2 (resync) own sub-budget, **separate** from S3/S6 |
| `S3S6TierBudget` | 2 | S3/S4/S5 (panic/bottom) sub-budget, **separate** from S1/S2 |
| S6 (rank 6) | — | exempt, always tried (guaranteed fallback, 1.3.2) |

`MaxRecoveryAttemptsPerPosition` is retained (see Deviations) but no longer gates candidate selection.

## The regression test

`Tests/ParserTests/Recovery/TierBudgetTests.cs` → `Test_S1ManyCandidates_S3StillTried_NotStarved`.

- **Grammar:** `Module := '{' ZeroOrMany(Stmt) '}'`; `Stmt := Ident ':' Expr ';'`; `Expr` is a TDOPP
  rule with six operators (`+ - * / == !=`) so `FollowSet(Expr)` is large → **S1 generates many
  rank-1 insertion candidates**. The `{…}` wrapper is deliberate: after the garbage there is no
  `Ident`, so S2 (resync, `First(Stmt)={Ident}`) has no anchor, while a `;` terminator exists — making
  **S3 (panic) the only real repair**.
- **Input:** `{ a: 1+ ### ; }` — the mismatch is *inside* `Expr` (the right operand of `+` is missing);
  the top rule is `Expr` (large FollowSet), so S1 is crowded.
- **Assertion:** `RecoveryDiagnostics` contains an S3-originated `Skipped` diagnostic
  (`Message` contains `"skip to terminator"`), i.e. S3 was actually tried and accepted.
- **Verified to be a real regression:** temporarily restoring the old single-budget gate
  (`attempts.TriedIds.Count > MaxRecoveryAttemptsPerPosition`) makes the test **fail** (S3 starved —
  only `S6`/`Unrecovered` diagnostics, no `skip to terminator`); with the per-tier budgets it **passes**
  (S3 is reached and accepted).

## Files changed

| File | Reason (one line) |
|---|---|
| `ExtensibleParser/Parser.Recovery.cs` | Budget logic: per-tier sub-budgets (`S1TierBudget`/`S2TierBudget`/`S3S6TierBudget`) + `TierBudget` class; S0/S6 exempt. |
| `Tests/ParserTests/Recovery/TierBudgetTests.cs` | New regression test (S1 crowded → S3 still tried). |
| `Tests/ParserTests/Recovery/RecoveryRuleTests.cs` | `HasEpsilonOperand` made precise (see Deviation 2). |

## Test results

- `dotnet build Tests/ParserTests/ParserTests.csproj` — **0 errors, 0 warnings**.
- `dotnet test Tests/ParserTests/ParserTests.csproj` — **348 passed / 0 failed / 2 skipped** (Total 350).
  The 2 skipped are the pre-existing `[Ignore("WIP")]` tests. (Baseline before this sub-point was
  347/0/2; +1 is the new `TierBudgetTests` test.)

## Deviations

1. **`MaxRecoveryAttemptsPerPosition` retained as a legacy no-op.** Ten existing test files set it
   (`= 16`/`10`/`11`/`3`). Removing it would have touched all of them (large, unfocused diff), so the
   property is kept for API compatibility but is **no longer consulted** by the budget gate — the
   per-tier sub-budgets are the active mechanism. It is documented in-source as superseded by A2.
2. **`RecoveryRuleTests.HasEpsilonOperand` tightened.** Its old body matched *any* zero-length
   `IsRecovery` non-absorber terminal. A2 lets S3 be tried; in `Test_Recoverable_False_Disables_Epsilon_Acceptance`
   that path now yields a legitimate S1 insertion (`;`, Kind `";"`) which the old predicate wrongly
   flagged as the ε-match result. The predicate now also requires `Kind == "Error"` (the actual
   ε-acceptance of the `ErrorEmpty` rule), so it detects the ε-match and not S1/S4 zero-length repairs.
   The Recoverable=true half (which does produce the Kind-`"Error"` ε node) is unaffected.
3. **`ParserTests.csproj` duplicate include removed.** A prior session had added
   `<Compile Include="Recovery\TierBudgetTests.cs" />`; because the SDK auto-includes project `.cs`
   files, that produced `NETSDK1022` (duplicate Compile item) and broke the build. The line was removed;
   the `.csproj` is now identical to HEAD (no net diff).
