# 6.2.1 (D2-full) — `RecoveryMetrics`: accept/rollback per strategy S0..S6 + specCache hits/misses

Sub-point 6.2.1 establishes the FULL `RecoveryMetrics` type and wires the two 6.2.1 counters:
**accept/rollback per strategy (S0..S6)** and **specCache hits/misses**. Time-by-phase and
hygiene-removals before/after are **6.2.2** (the hygiene fields are reserved below, not wired).
Counting is **observation-only** — no S0–S6 or recovery behavior changed. Per the plan
(`RecoveryImprovementPlan-checklist.md:73`): `6.2 D2 (full): counters by strategy — accept/rollback per
S (S0..S6); hygiene removals before/after B1; specCache hits/misses (B2); time by phase`.

## What D2-core (Wave 0) already had — reused, not re-wired

D2-core already exposed the **specCache hit/miss counters** at the lookup site. This sub-point does
NOT re-implement them — it surfaces them on the new `RecoveryMetrics` by **reading through the same
`SpeculativeCache`** (single source of truth):

- `SpeculativeCache.Hits`/`Misses` (`ExtensibleParser/Recovery/SpeculativeCache.cs:11-13`) — the
  counters are incremented **at the specCache lookup site**, `SpeculativeCache.Speculative`
  (`SpeculativeCache.cs:18-26`): `_hits++` on a cache hit (compute not called), `_misses++` on a miss
  (compute called + stored). This is the B2/5a.1 wiring — unchanged.
- `Parser.SpecCache` / `SpecCacheHits` / `SpecCacheMisses`
  (`ExtensibleParser/Parser.Recovery.cs:167-169`) — the existing public surface that reads through
  `_specCache`. Unchanged (existing `SpecCacheFieldTests`/`SpecCacheSharedTests` still pass).
- The other D2-core counters on `Parser` (`RecoveryPasses`, `EngineGenerateCalls`, `HygieneRemovals`,
  `S2ScanPositions`, `S3ScanPositions`) are unrelated to this sub-point and untouched.

So the **new** work is: (a) the `RecoveryMetrics` type, (b) the per-strategy accept/rollback counters
wired in the recovery loop, and (c) exposing both on `Parser.Metrics`.

## The `RecoveryMetrics` type (new)

New file: `ExtensibleParser/Recovery/RecoveryMetrics.cs`. A `sealed class` with a primary constructor
taking the shared `SpeculativeCache` (so its hit/miss counters are read through, not duplicated):

```csharp
public sealed class RecoveryMetrics(SpeculativeCache specCache)
{
    private readonly int[] _accept   = new int[7];   // indexed by rank 0..6 (S0..S6)
    private readonly int[] _rollback = new int[7];

    public int SpecCacheHits   => _specCache.Hits;    // read-through (single source of truth)
    public int SpecCacheMisses => _specCache.Misses;

    // 6.2.2 reserved (fields now, wired in 6.2.2): hygiene removals before/after B1.
    public int HygieneRemovalsBefore { get; internal set; }
    public int HygieneRemovalsAfter  { get; internal set; }

    public void NoteAccept(int rank);       // rank 0..6 == strategy index
    public void NoteRollback(int rank);
    public int AcceptCount(RecoveryStrategy s);  public int AcceptCount(int rank);
    public int RollbackCount(RecoveryStrategy s); public int RollbackCount(int rank);
    public int TotalAccept;   public int TotalRollback;
    public void Reset();
}
```

Key design choices:
- **Per-strategy key = the candidate's `Rank` (0..6).** `RecoveryCandidate.Rank` is documented as the
  strategy rank (`RecoveryCandidate.cs:5` "рангом стратегии (S0..S5)"), and the tier-budget logic
  already treats rank 0=S0, 1=S1, 2=S2, 3/4/5=S3S6, 6=S6. `S1b` is part of S1 (rank 1), so it counts
  under S1. The public query API is keyed by the `RecoveryStrategy` enum (`S0`..`S6` = `1<<0`..`1<<6`)
  for test readability; `Index(strategy)` maps a single flag to its 0..6 array slot.
- **specCache hits/misses are read through the `SpeculativeCache`** (not re-counted), so they are
  always consistent with `Parser.SpecCacheHits/Misses` (the test asserts the two surfaces agree).
- **Reserved 6.2.2 fields**: only `HygieneRemovalsBefore`/`HygieneRemovalsAfter` are added now
  (well-defined; wired in 6.2.2). **Per-phase time is deferred entirely to 6.2.2** (its exact shape —
  main parse / generation / re-parses — is a 6.2.2 decision), so no placeholder field is added here.

## Wiring points (all in `ExtensibleParser/Parser.Recovery.cs`, observation-only)

- **Public exposure**: `Parser.Metrics` property — `Parser.Recovery.cs:173`:
  `public RecoveryMetrics Metrics => _metrics ??= new(_specCache);`
- **Field**: `Parser.Recovery.cs:242`: `private RecoveryMetrics? _metrics;` (non-`readonly`, null until
  first creation — see Deviations: a field initializer cannot reference `_specCache`, CS0236).
- **Per-session reset**: `Parser.Recovery.cs:489-490` — at the start of `Recover`, alongside
  `_specCache.Reset()`: `var metrics = _metrics ??= new(_specCache); metrics.Reset();`. The local
  `metrics` is captured by the `TryCandidate` local function.
- **Accept / rollback per S** — inside the `TryCandidate(RecoveryCandidate)` local function (the single
  point where a candidate is applied, re-parsed, and either accepted or rolled back):
  - **Accept (full recovery, Success@EOF)**: `Parser.Recovery.cs:609` —
    `metrics.NoteAccept(candidate.Rank);` in the `next.TryGetSuccess(...) && end2 == input.Length` branch.
  - **Accept (progress, `e2 > e`)**: `Parser.Recovery.cs:619` —
    `metrics.NoteAccept(candidate.Rank);` in the `e2 > e` branch.
  - **Rollback (no progress)**: `Parser.Recovery.cs:622` — `metrics.NoteRollback(candidate.Rank);`
    immediately before `RollbackPatches(log);`.

  The "already tried" and "tier sub-budget exhausted" early returns (`TryCandidate`, before
  `ApplyPatches`) are intentionally NOT counted as rollbacks — no patches were applied there, so there
  is nothing to roll back. Only a candidate that was applied, re-parsed, and produced no progress
  (`RollbackPatches` called) is a rollback. The S6 hard-limit path (`ForceS6`) goes through the same
  `TryCandidate`, so it is counted too.
- **specCache hit/miss site**: unchanged — `SpeculativeCache.Speculative`
  (`ExtensibleParser/Recovery/SpeculativeCache.cs:18-26`). `RecoveryMetrics.SpecCacheHits/Misses` read
  through it, so no new increment was added at the lookup site (D2-core already does it).

## Test — `Tests/ParserTests/Recovery/RecoveryMetricsTests.cs` (new, 2 tests)

Grammar mirrors `SpecCacheSharedTests` (`Module := '{' ZeroOrMany(Stmt) '}'`, `Stmt := Ident ':' Expr
';'`, TDOPP `Expr` with six operators) so S2 resync drives the shared specCache (`First(Stmt) = {Ident}`
→ `Speculative` at each Ident after the garbage). Fresh `Parser` per test (method-level parallel).

1. **`Test_MultiIteration_AcceptRollbackPerStrategy_AndSpecCache`** — input
   `"{ a: 1+ ### b; c: 2+ ### d; }"` (two errors → two recovery iterations). Each error is followed by
   an Ident, and S3 (panic to `;`) is the repair (accepted once per error); S0 and the S1 insert
   candidates are tried first and rolled back. The `(Stmt,'d')` speculative lookup is cached across the
   two iterations (the cache lives for the whole `Recover`, not per iteration) → at least one hit.
   Asserts: `Success@EOF`; `TotalAccept >= 1`; `AcceptCount(S3) >= 1` (the winning strategy);
   `TotalRollback >= 1`; `SpecCacheMisses > 0`; `SpecCacheHits > 0`; per-strategy accept counts sum to
   `TotalAccept`; `TotalAccept <= RecoveryPasses`; and `Metrics.SpecCacheHits/Misses ==
   Parser.SpecCacheHits/Misses` (the read-through agrees with the D2-core surface).
2. **`Test_Metrics_Reset_BetweenParses`** — a clean parse (`"{ a: 1; }"`) leaves `TotalAccept == 0` and
   `TotalRollback == 0`; the next dirty parse (`"{ a: 1+ ### b; }"`) resets and accumulates fresh
   (`TotalAccept >= 1`), pinning the per-`Recover` reset.

## Test results (one-shot, from `C:\RSDN\CsNitra`)

- `dotnet test Tests/ParserTests` — **Total: 411 · Passed: 409 · Failed: 0 · Skipped: 2** (the 2 skipped
  are the pre-existing `[Ignore("WIP")]`; +2 vs the 6.1.2c baseline of 409 is exactly the 2 new
  `RecoveryMetricsTests`).
- `dotnet test Tests/CSharpGrammarTests` — **Total: 1527 · Passed: 1524 · Failed: 0 · Skipped: 3** (no regression).
- `dotnet test Tests/CsPreprocessorTests` — **Total: 128 · Passed: 128 · Failed: 0** (no regression).

## Files changed

- `ExtensibleParser/Recovery/RecoveryMetrics.cs` — **new**: the `RecoveryMetrics` type (per-strategy
  accept/rollback arrays + read-through specCache counters + reserved 6.2.2 hygiene fields + `Reset`).
- `ExtensibleParser/Parser.Recovery.cs` — **modified**: `Metrics` property (`:173`), `_metrics` field
  (`:242`), per-`Recover` reset (`:489-490`), and the three accept/rollback note calls in
  `TryCandidate` (`:609`, `:619`, `:622`).
- `Tests/ParserTests/Recovery/RecoveryMetricsTests.cs` — **new**: 2 tests.
- `docs/RecoveryImprovementPlan-progress6.2.md` — this file.

No `RecoveryEngine.cs` change, no `.csproj` change (net-zero — see Deviations), no S0–S6 / recovery
behavior change, `SpeculativeCache` and the D2-core counters untouched, no stop-if triggered.
**Not committed.**

## Deviations

- **CS0236 — field initializer cannot reference `_specCache`.** `Parser` uses a primary constructor
  (no body), and C# forbids a field initializer from referencing another instance field/method/property
  (even inside a lambda/method-group — CS0236 covers "field, method, or property"). So `_metrics` is a
  non-`readonly` nullable created lazily: `Metrics => _metrics ??= new(_specCache)` and
  `var metrics = _metrics ??= new(_specCache); metrics.Reset();` at the start of `Recover` (a local
  captured by `TryCandidate`). Both creation sites are in property/method bodies, where referencing
  `_specCache` is legal.
- **csproj build fix (required), net-zero vs HEAD.** The same external process documented in the
  6.1.x progress files added duplicate `<Compile Include="Recovery\RecoveryMetrics.cs" />` to
  `ExtensibleParser.csproj` and `<Compile Include="Recovery\PublicDerivedListTests.cs" />` +
  `<Compile Include="Recovery\RecoveryMetricsTests.cs" />` to `ParserTests.csproj` (NETSDK1022:
  Duplicate 'Compile' items — SDK default globbing already includes them). All three lines were removed
  and `git diff -- "*.csproj"` is empty (net-zero); `PublicDerivedListTests.cs` also had a BOM added by
  the same process and was restored via `git checkout`. The "do not modify any .csproj" rule is honored
  in net effect, exactly per the established pattern.
- **Per-phase time deferred to 6.2.2.** Only the well-defined `HygieneRemovalsBefore/After` fields are
  reserved now; the per-phase time counters (main parse / generation / re-parses) are left to 6.2.2,
  which decides their exact shape.
- Not committed.

---

# 6.2.2 (D2-full) — time-by-phase + hygiene-removals counters + braking-scenario acceptance test

Wires the remaining two D2-full counter groups and adds the braking-scenario acceptance test.
Everything is **observation-only**: one `Stopwatch.StartNew()` per parse/generation call (a small
number of calls per session — iterations + candidates, NOT per token), no S0–S6 or recovery behavior
changed, no `.csproj` change.

## The three phases — structure and delimitation

The recovery session is one `Recover` call (`Parser.Recovery.cs:479`); its loop has exactly three
kinds of expensive work, all cleanly delimited:

| Phase | Site | Why it is clean |
|---|---|---|
| (a) MAIN parse | `Parser.Recovery.cs:519-524` — the loop's `ParseRule`, `iter == 0` | The first loop iteration IS the initial parse before recovery (`Recover` is the single session entry, `Parser.cs:266`). `iter == 0 ? NoteMainParse : NoteReparse`. |
| (b) GENERATION | `Parser.Recovery.cs:656-658` (normal lazy `Generate`) and `:642-644` (the `ForceS6` hard-limit `Generate`) | `RecoveryEngine.Generate` returns a `List<RecoveryCandidate>` **eagerly** (`RecoveryEngine.cs:10`) — the speculative-parse work all happens inside the call, so wall-clock around the call is exact. The `foreach` that follows consumes the ready list (interleaved with `TryCandidate`, whose re-parses go to phase c). |
| (c) RE-PARSES | `Parser.Recovery.cs:592-594` (the `TryCandidate` candidate re-parse) and `:519-524` (the main-loop `ParseRule` for `iter >= 1`) | Every `ParseRule` after the first is a re-parse: the candidate re-parses in `TryCandidate` (the task's example) PLUS the iterative main-loop passes after a candidate was accepted (a full re-parse of the start rule from `currentStartPos`). Both accumulate into `ReparseTime`. |

**Interpretation note (re-parses):** the task names "the candidate re-parse in the loop, e.g.
`TryCandidate`'s re-parse". The `iter >= 1` main-loop passes are also re-parses of the start rule and
carry most of the multi-iteration cost, so they are counted in `ReparseTime` too (not dropped) —
otherwise a multi-error scenario would show a large unmeasured share.

## Metrics fields added (`ExtensibleParser/Recovery/RecoveryMetrics.cs`)

- `MainParseTime` / `GenerationTime` / `ReparseTime` (`TimeSpan`, `internal set`) —
  `RecoveryMetrics.cs:44-48`; accumulate across the session via `NoteMainParse` / `NoteGeneration` /
  `NoteReparse` (`:50-54`); zeroed in `Reset()` (`:84-86`) alongside the 6.2.1 counters.
- Wall clock = `Stopwatch.StartNew()` per call, `Elapsed` noted after the call. No reuse of
  `_recoveryStopwatch` (that one drives the B4 `TimeBudget` degradation ladder — separate concern).

## Hygiene-removals wiring + the "before/after" interpretation

- **Where hygiene removes entries:** `HygieneCore` (`Parser.Recovery.cs:853-872`) — exact (post-B1)
  hygiene; it removes via `RemoveMemo` + the existing per-session `HygieneRemovals++`
  (`:868`, hook from 2.1/B1, reset per `Recover` at `:485`). No new removal site was added.
- **Interpretation of "before/after B1":** the plan's wording (`RecoveryImprovementPlan.md:73` —
  «число удалений memo до/после (D2)») compares the removal count under the PRE-B1 (imprecise) vs
  POST-B1 (exact) hygiene **implementations**. B1 has already landed (Wave 2, commit f65ff66), so only
  the exact hygiene exists at runtime — there is a **single live counter**, and "before/after" does not
  map to two live counters. Hence: the per-session count is recorded in **`HygieneRemovalsAfter`**
  (the post-B1/exact-hygiene field); **`HygieneRemovalsBefore` stays 0** (the pre-B1 baseline has no
  runtime referent — it is a historical comparison point, not a live counter).
- **Wiring point:** `Parser.Recovery.cs:681-685` — `Metrics.HygieneRemovalsAfter = HygieneRemovals;`
  at the top of `FinishRecovery`, the **single session exit point** (every `Recover` return — `:522`,
  `:652`, `:658` — goes through it, and it is only called from `Recover`). The counter is per-`Recover`
  (reset at `:485`), so the value captured at session end is exactly this session's removals.

## Braking-scenario acceptance test

`Tests/ParserTests/Recovery/RecoveryMetricsTests.cs` —
`Test_BrakingScenario_ManyErrors_PhaseTimesAndHygieneVisible` (3rd test in the class):

- Same grammar as the 6.2.1 tests (`Module/Stmt/Expr`, TDOPP `Expr`); input
  `"{ a: 1+ ### b; c: 2+ ### d; e: 3+ ### f; g: 4+ ### h; }"` — **4 errors → 4 recovery iterations**
  (D1.1-style many-errors stress). Each iteration: S0 re-parse (no progress → rollback) → one
  `Generate` call → candidate re-parses until S3 (panic to `;`) is accepted; `HygieneCore` runs in
  every `ApplyPatches`.
- Asserts the "braking" is **visible in the metrics, not a timeout**:
  `Success@EOF`; `MainParseTime > 0` (a); `EngineGenerateCalls > 0` AND `GenerationTime > 0` (b);
  `ReparseTime > 0` (c); `HygieneRemovalsAfter > 0` (hygiene ran and removed entries); and
  `HygieneRemovalsAfter == parser.HygieneRemovals` (the metric agrees with the D2-core hook).
- Non-zero assertions (not magnitudes) — wall-clock magnitudes are machine-dependent; the point is
  observability: a slow scenario shows up in the counters, not as a hang.

## Test results (one-shot, from `C:\RSDN\CsNitra`)

- `dotnet test Tests/ParserTests` — **Total: 412 · Passed: 410 · Failed: 0 · Skipped: 2** (the 2
  skipped are the pre-existing `[Ignore("WIP")]`; +1 vs the 6.2.1 baseline of 411 is exactly the new
  braking test).
- `dotnet test Tests/CSharpGrammarTests` — **Total: 1527 · Passed: 1524 · Failed: 0 · Skipped: 3** (identical to the 6.2.1 baseline — no regression).
- `dotnet test Tests/CsPreprocessorTests` — **Total: 128 · Passed: 128 · Failed: 0** (identical — no regression).

## Files changed (6.2.2)

- `ExtensibleParser/Recovery/RecoveryMetrics.cs` — **modified**: time-by-phase fields + `Note*`
  methods + `Reset` extension; header/field comments for the hygiene interpretation.
- `ExtensibleParser/Parser.Recovery.cs` — **modified**: the three phase-timing sites (`:519-524`
  main/iterative, `:592-594` candidate re-parse, `:642-644` + `:656-658` generation) + the
  hygiene-removals record in `FinishRecovery` (`:681-685`).
- `Tests/ParserTests/Recovery/RecoveryMetricsTests.cs` — **modified**: +1 braking-scenario test
  (3 tests total).
- `docs/RecoveryImprovementPlan-progress6.2.md` — this file.

No `RecoveryEngine.cs` change, no `.csproj` change, no S0–S6 / recovery behavior change
(`HygieneCore`, `SpeculativeCache`, the D2-core counters untouched), no stop-if triggered.
**Not committed.** (Note: `docs/RecoveryImprovementPlan-checklist.md` carried a pre-existing
uncommitted working-tree edit from the 6.2.1 session — left as-is, not part of 6.2.2.)
