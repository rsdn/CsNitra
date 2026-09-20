# 6.3.1 (A4-6) — `GrammarDiagnostics`: grammar-quality channel type + collection hooks

Sub-point 6.3.1 establishes the public `GrammarDiagnostics` type — a **separate** public type from
`RecoveryMetrics` (per-recovery-session metrics) and `RecoveryDiagnostics` (per-input error
diagnostics) — and wires the **observation-only collection** for the four grammar-author feedback
signals. The quality analysis that turns the raw counts into author-facing findings is **6.3.2**
(not done here). Nothing here feeds recovery behavior — S0–S6 and recovery behavior are unchanged.

Per the plan (`RecoveryImprovementPlan-checklist.md:75`): `6.3 A4-6: grammar quality channel — public
GrammarDiagnostics (separate from RecoveryDiagnostics): anchor used N times / never; all recovery
points of a rule identical; Recoverable:false region never fires on corpus; S2 anchor redundant.
Generated on D1 corpus.`

## The four signals and what data is/isn't available

All four signals have available underlying data (no stop-if triggered). The collection is
**observation-only** — a `Note*` call at each relevant site; no recovery decision reads it.

| # | Signal (author feedback) | Data source (available) | Collection site |
|---|---|---|---|
| 1 | anchor used N times / never used | S2 resync candidate generated for an anchor (T1 full-validation or T2 can-start), keyed by the anchor's rule name | `RecoveryEngine.cs:238` (`AddResyncCandidate`) |
| 2 | all recovery points of a rule identical | the snapshot top frame's `Location` (grammar-relative: `RuleFrameLocation(AltIndex)` / `SeqFrameLocation(ElementIndex)` / `LoopFrameLocation(LoopKind, Iteration)` / `PostfixFrameLocation(PostfixIndex)`) at an accepted recovery, keyed by rule name | `Parser.Recovery.cs:623,636,647` |
| 3 | a `Recoverable:false` region never fires on the corpus | the C1 strict gate (`RecoveryEngine.Generate`), per distinct strict rule name on the stack | `RecoveryEngine.cs:27-34` |
| 4 | an S2 anchor is redundant (always the same one) | the set of distinct anchors used for S2 resync = the keys of signal-1's count map | `RecoveryEngine.cs:238` (same as #1) |

Notes on interpretation:
- **#1 and #4 share the anchor-usage data.** "Used" = an S2 resync candidate was generated for that
  anchor (observed at candidate **generation**, `AddResyncCandidate`, where the anchor identity is
  explicit). This slightly over-counts relative to "accepted" (a generated candidate may be rolled
  back), but it is the clean observation point for "S2 chose this anchor as a resync target" and is
  sufficient for the redundancy signal ("always the same one" == `UsedAnchors.Count == 1`).
- **#2 recovery point = the top frame's `FrameLocation`** (the rule that was failing at the recovery
  point and where in that rule). It is grammar-relative and stable across inputs — the right basis for
  "all identical". `LoopFrameLocation` carries `Iteration` (which varies), so 6.3.2 may want to
  normalize loop points; the raw `FrameLocation` is collected here and 6.3.2 decides the granularity.
  Recorded at **acceptance** (the two `NoteAccept` sites), so it counts rules that actually recovered,
  once per recovery event (the loop breaks on the first accepted candidate).
- **#3 a strict region "fires"** when `Generate` detects `strict` (a failure inside a
  `Recoverable=false` frame, S0 made no progress → S1..S5 suppressed, only S6 emitted). Counted per
  **distinct** strict rule name on the snapshot stack (recursion of the same strict rule counts once
  per firing). `Recoverable=false` frames are skipped elsewhere in the engine (`GenerateS1`/`NearestOptions`/
  `GetMaxSkip`), so this is the single point where a strict region's suppression actually takes effect.

## The `GrammarDiagnostics` type (new)

New file: `ExtensibleParser/Recovery/GrammarDiagnostics.cs`. A `sealed class` with three internal
dictionaries and a small public read surface (mirrors the `RecoveryMetrics` shape — `Note*` writers +
read properties + `Reset`):

```csharp
public sealed class GrammarDiagnostics
{
    private readonly Dictionary<string, int>                 _anchorUsage       = new(StringComparer.Ordinal); // #1/#4: anchor rule name -> count
    private readonly Dictionary<string, HashSet<FrameLocation>> _recoveryPoints = new(StringComparer.Ordinal); // #2: rule name -> distinct recovery points
    private readonly Dictionary<string, int>                 _strictRegionFirings = new(StringComparer.Ordinal); // #3: strict rule name -> firing count

    // writers (observation hooks)
    public void NoteAnchorUse(string anchorRuleName);                    // #1/#4
    public void NoteRecoveryPoint(string ruleName, FrameLocation loc);   // #2
    public void NoteStrictRegionFiring(string ruleName);                 // #3

    // read surface (consumed by 6.3.2)
    public int  AnchorUsage(string anchorRuleName);          // #1 (0 if never used)
    public IReadOnlyDictionary<string,int> AnchorUsageAll;   // #1 (used anchors + counts)
    public IReadOnlyCollection<string>     UsedAnchors;      // #4 ("always the same one" == Count == 1)
    public int  DistinctRecoveryPoints(string ruleName);     // #2 ("all identical" == 1)
    public IReadOnlyCollection<FrameLocation> RecoveryPoints(string ruleName); // #2 (the points)
    public IReadOnlyCollection<string>     RecoveredRules;   // #2 (rules that recovered)
    public int  TotalDistinctRecoveryPoints;                  // #2
    public int  StrictRegionFirings(string ruleName);         // #3 (0 if never fired)
    public IReadOnlyDictionary<string,int> StrictRegionFiringsAll; // #3

    public void Reset();
}
```

Key design choice — **accumulation scope**: unlike `RecoveryMetrics` (reset per `Recover`/`Parse`),
`GrammarDiagnostics` **accumulates across parses for the lifetime of the `Parser`** and is NOT reset in
`Recover`. The four signals are corpus-level ("never used", "never fires", "always the same one") and
only make sense over the whole corpus of parses, not a single one. `Reset()` clears it for a fresh run
(the negative "never" signals for 6.3.2 read `count == 0` / absent-key over the accumulated corpus).

## Public exposure + collection sites (all observation-only)

- **Public exposure**: `Parser.GrammarDiagnostics` — `Parser.Recovery.cs:179`:
  `public GrammarDiagnostics GrammarDiagnostics => _grammarDiagnostics ??= new();`
- **Field**: `Parser.Recovery.cs:252`: `private GrammarDiagnostics? _grammarDiagnostics;` (non-`readonly`,
  null until first creation; deliberately **not** reset in `Recover` — corpus accumulation).
- **Signal #1/#4 (anchor usage)**: `RecoveryEngine.cs:238` — first line of the `AddResyncCandidate`
  local function in `GenerateS2` (called for both T1 and T2 resync candidates):
  `parser.GrammarDiagnostics.NoteAnchorUse(anchorName);`
- **Signal #3 (strict-region firing)**: `RecoveryEngine.cs:27-34` — inside the existing `if (strict)`
  block of `Generate` (where S1..S5 are suppressed, C1): one `NoteStrictRegionFiring(ruleName)` per
  distinct `Recoverable=false` rule on the snapshot stack.
- **Signal #2 (recovery points per rule)**: `Parser.Recovery.cs:623` — a `NoteRecoveryPoint()` local
  function in `TryCandidate` that records the snapshot top frame's `(RuleName, Location)`; called at
  the two acceptance sites `Parser.Recovery.cs:636` (Success@EOF) and `:647` (progress `e2 > e`), right
  before each existing `metrics.NoteAccept(...)`. A null/empty snapshot (e.g. S6 with no snapshot) has
  no rule to attribute and is skipped.

## Test — `Tests/ParserTests/Recovery/GrammarDiagnosticsTests.cs` (new, 3 tests)

Stateless: each test builds its own `Parser` (method-level parallel). Asserts the **positive**
collection works (the negative "never used"/"never fires" signals need a corpus run — 6.3.2).

1. **`Test_AnchorUsage_UsedAndNeverUsed_Collected`** — grammar with two author anchors: `Item` (the
   resync target) and `ItemAlt` (declared but never used — the input has no `alt` token, so its First
   never matches). Input `"int a; ### int b;"` → S2 resync to the next `Item`. Asserts `Success@EOF`;
   `AnchorUsage("Item") >= 1`; `UsedAnchors` contains `Item`; `AnchorUsage("ItemAlt") == 0` and `ItemAlt`
   is not in `UsedAnchors`; `TotalDistinctRecoveryPoints >= 1` and `RecoveredRules.Count >= 1` (signal #2).
2. **`Test_StrictRegionFiring_Collected`** — `S = a STRICT b`, `STRICT = RecoveryRule(x,
   Recoverable=false)`. Input `"acb"` (STRICT expects `x`, finds `c`) → a failure inside the strict
   region; S0 makes no progress → `Generate` detects strict → S1..S5 suppressed, only the S6 bottom.
   Asserts `StrictRegionFirings("STRICT") >= 1`, `StrictRegionFiringsAll` contains `STRICT`, and
   `Success@EOF` (the strict region still reaches EOF via the S6 guaranteed bottom, C1).
3. **`Test_AccumulatesAcrossParses_AndReset`** — two parses of the same dirty input **double** the
   anchor-usage count (corpus-level accumulation, not per-`Recover`); `Reset()` returns anchor usage,
   recovery points, and strict-region firings to 0.

## Test results (one-shot, from `C:\RSDN\CsNitra`)

- `dotnet test Tests/ParserTests` — **Total: 415 · Passed: 413 · Failed: 0 · Skipped: 2** (the 2 skipped
  are the pre-existing `[Ignore("WIP")]`; +3 vs the 6.2.2 baseline of 412 is exactly the 3 new
  `GrammarDiagnosticsTests`).
- `dotnet test Tests/CSharpGrammarTests` — **Total: 1527 · Passed: 1524 · Failed: 0 · Skipped: 3** (identical to the 6.2 baseline — no regression).
- `dotnet test Tests/CsPreprocessorTests` — **Total: 128 · Passed: 128 · Failed: 0** (identical — no regression).

## Files changed

- `ExtensibleParser/Recovery/GrammarDiagnostics.cs` — **new**: the `GrammarDiagnostics` type (three
  count/set maps + `Note*` writers + read surface + `Reset`).
- `ExtensibleParser/Recovery/RecoveryEngine.cs` — **modified**: signal #1/#4 in `AddResyncCandidate`
  (`:238`) and signal #3 in the `Generate` strict block (`:27-34`).
- `ExtensibleParser/Parser.Recovery.cs` — **modified**: `GrammarDiagnostics` property (`:179`),
  `_grammarDiagnostics` field (`:252`), and the signal #2 `NoteRecoveryPoint` local function + two
  acceptance-site calls (`:623`, `:636`, `:647`).
- `Tests/ParserTests/Recovery/GrammarDiagnosticsTests.cs` — **new**: 3 tests.
- `docs/RecoveryImprovementPlan-progress6.3.md` — this file.

No S0–S6 / recovery behavior change, `RecoveryMetrics`/`RecoveryDiagnostics`/`SpeculativeCache`
untouched, no stop-if triggered. **Not committed.** (Note: `docs/RecoveryImprovementPlan-checklist.md`
carried a pre-existing uncommitted working-tree edit from the 6.2.2 session — left as-is, not part of
6.3.1.)

## Deviations

- **csproj build fix (required), net-zero vs HEAD.** The same external tooling process documented in the
  6.1.x/6.2.x progress files and `RecoverySystemChecklist.md` auto-added duplicate
  `<Compile Include="Recovery\GrammarDiagnostics.cs" />` to `ExtensibleParser.csproj` and
  `<Compile Include="Recovery\GrammarDiagnosticsTests.cs" />` to `ParserTests.csproj` when the new
  `.cs` files were created (NETSDK1022: Duplicate 'Compile' items — the SDK default globbing already
  includes them). Both lines were removed via `git checkout` and `git diff -- "*.csproj"` is empty
  (net-zero vs HEAD). The "do not modify any .csproj" rule is honored in net effect, exactly per the
  established pattern (see `RecoverySystemChecklist.md:220,276,328,343,418` and the 6.2 progress file).
- **Signal #1/#4 "used" = candidate generation, not acceptance.** Observed at `AddResyncCandidate`
  (where the anchor identity is explicit). This is the clean observation point and is sufficient for the
  redundancy signal; 6.3.2 can refine to acceptance-based counting if needed (the acceptance site already
  exposes `candidate.TerminalKind` == the anchor name for S2).
- **Accumulation, not per-`Recover` reset.** Deliberate divergence from `RecoveryMetrics`: the signals
  are corpus-level, so `GrammarDiagnostics` accumulates across parses and is only cleared by an explicit
  `Reset()`.
- Not committed.
