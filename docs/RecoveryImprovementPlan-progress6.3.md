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

---

# 6.3.2 (A4-6) — grammar-quality FINDINGS + the D1-corpus test

Sub-point 6.3.2 turns the raw corpus-level counts collected in 6.3.1 into **author-facing findings** and
adds the corpus-level (negative-signal) test. It adds analysis only — S0–S6 recovery behavior and the
6.3.1 collection hooks are unchanged, and no `.csproj` is touched.

## The findings API (new, in `GrammarDiagnostics.cs`)

Two new public types and the analysis surface on `GrammarDiagnostics`:

```csharp
public enum GrammarFindingKind { AnchorNeverUsed, AnchorHeavilyUsed, RuleRecoveryPointsIdentical,
                                 StrictRegionNeverFires, S2AnchorRedundant }

public sealed record GrammarFinding(GrammarFindingKind Kind, string Name, int Count); // + ToString()

// on GrammarDiagnostics:
public const int DefaultHeavilyUsedThreshold = 10;
public IReadOnlyList<GrammarFinding> GetFindings(
    IEnumerable<string> declaredAnchors, IEnumerable<string> declaredStrictRegions,
    int heavilyUsedThreshold = DefaultHeavilyUsedThreshold);
public IReadOnlyList<GrammarFinding> GetFindings(Parser parser, int heavilyUsedThreshold = DefaultHeavilyUsedThreshold);
public static (IReadOnlyCollection<string> Anchors, IReadOnlyCollection<string> StrictRegions) DeclaredSets(Parser parser);
```

### The 5 finding kinds

| # | Kind | Condition | `Name` / `Count` |
|---|---|---|---|
| 1 | `AnchorNeverUsed` | a **declared** anchor with usage 0 over the corpus | anchor name / 0 |
| 2 | `AnchorHeavilyUsed` | an anchor used **above** the threshold (default 10, tunable) | anchor name / usage count |
| 3 | `RuleRecoveryPointsIdentical` | a recovered rule with exactly **1** distinct recovery point | rule name / 1 |
| 4 | `StrictRegionNeverFires` | a **declared** `Recoverable:false` region with 0 firings over the corpus | region name / 0 |
| 5 | `S2AnchorRedundant` | only **one** distinct S2 anchor is ever used (`UsedAnchors.Count == 1`) | the single anchor / its count |

- **Threshold (finding #2)**: `DefaultHeavilyUsedThreshold = 10` — an anchor used **more than** 10 times
  over the accumulated corpus is flagged. It is a corpus-size-dependent heuristic (a bigger corpus uses
  its anchors more) and is exposed as a `GetFindings` parameter so it is overridable per run.
- Findings #1 and #4 are the **negative** signals and need the declared set as a baseline (a declared
  name absent from the usage/firing map has count 0). #3 reads the per-rule recovery-point map directly.
  #5 reads the used-anchor set directly.

### How the declared set is obtained (`DeclaredSets`)

`DeclaredSets(parser)` walks `parser.Rules` (rule name → alternatives) and, for every `RecoveryRule` found
at any depth (`alt.GetSubRules<RecoveryRule>()`):
- **Anchors**: each `Ref` in `Options.Ancors` contributes `Ref.RuleName` — the same name
  `NoteAnchorUse` records, so a declared anchor is "used" iff that name is in the usage map.
- **Strict regions**: a `RecoveryRule` with `Options.Recoverable == false` contributes the enclosing
  `parser.Rules` key — the same name `NoteStrictRegionFiring` records (for a top-level prefix the stack
  frame's `RuleName` is exactly that key).

This is the declared-set baseline the "never used" / "never fires" findings compare against. The
convenience overload `GetFindings(parser)` calls `DeclaredSets(parser)` then the core `GetFindings`, so
the author-facing call is `parser.GrammarDiagnostics.GetFindings(parser)`.

## Tests — `Tests/ParserTests/Recovery/GrammarDiagnosticsTests.cs` (+3, now 6)

1. **`Test_GetFindings_NegativeAndRedundancyKinds`** (controlled, no parser) — populates a
   `GrammarDiagnostics` directly via the `Note*` hooks (`Item` used 15×, `AltItem` declared-but-never-used,
   `Strict` declared-but-never-firing) and asserts: `AnchorNeverUsed(AltItem)` present and `AnchorNeverUsed(Item)`
   absent; `AnchorHeavilyUsed(Item, 15)`; `StrictRegionNeverFires(Strict)`; `S2AnchorRedundant(Item)`; and no
   `RuleRecoveryPointsIdentical` (no recovery points recorded).
2. **`Test_GetFindings_RecoveryPointsIdentical`** (controlled) — a rule with 1 distinct recovery point
   (`Item`) is reported `RuleRecoveryPointsIdentical`; a rule with 2 (`Other`) is not. Also guards the
   `S2AnchorRedundant` gate: with two used anchors the set is **not** redundant.
3. **`Test_Findings_CorpusSlice_NegativeSignals`** (the corpus test) — builds the quality grammar
   (`NewQualityCorpusParser`) and runs a **representative slice of the D1 corpus** (the many-small-errors
   scenario, like D1.1/D1.6): two inputs of `GenManyItemErrors(12)` = `int a1; ### int a2; ### ... int a12;`
   (12 well-formed `int aN;` items separated by `###` garbage; each `###` forces one S2 resync to the next
   `Item`), parsed against the **same** `Parser` so `GrammarDiagnostics` accumulates over the corpus.

   The quality grammar declares: `Item` (the used S2 anchor / resync target), `AltItem` (a declared anchor
   the inputs never use — no `alt` token), `Strict` (a reachable `Recoverable:false` region the inputs never
   fire — no `strict` token), and `Module` = `RecoveryRule(ZeroOrMany(Stmt), Anchors = [Item, AltItem])`.

   Asserted findings (via `GetFindings(parser)`): **`AnchorNeverUsed(AltItem)`** (the deliberately-declared-
   but-never-used anchor) and **`StrictRegionNeverFires(Strict)`** (the never-firing strict region) — the two
   negative signals — plus `AnchorHeavilyUsed(Item)` (used on every resync, over the threshold) and
   `S2AnchorRedundant(Item)` (the only S2 anchor ever used). Also asserts `AnchorUsage("Item") > 0` (sanity)
   and that `AnchorNeverUsed(Item)` is absent.

   Note on the slice: the D1 corpus's generators/terminals are `private` to `RecoveryCorpusTests`, so the
   slice is a self-contained analog (same *shape* — many small errors in a loop, the D1.1/D1.6 scenario)
   with the declared anchors + strict region added. This keeps the change within `GrammarDiagnostics.cs` +
   the test file (no edit to `RecoveryCorpusTests.cs`, which is out of scope per the stop-if).

## Test results (one-shot, from `C:\RSDN\CsNitra`)

- `dotnet test Tests/ParserTests` — **Total: 418 · Passed: 416 · Failed: 0 · Skipped: 2** (the 2 skipped
  are the pre-existing `[Ignore("WIP")]`; +3 vs the 6.3.1 result of 415 is exactly the 3 new findings tests).
- `dotnet test Tests/CSharpGrammarTests` — **Total: 1527 · Passed: 1524 · Failed: 0 · Skipped: 3** (identical to the 6.3.1 baseline — no regression).
- `dotnet test Tests/CsPreprocessorTests` — **Total: 128 · Passed: 128 · Failed: 0** (identical — no regression).

## Files changed (6.3.2)

- `ExtensibleParser/Recovery/GrammarDiagnostics.cs` — **modified**: added `GrammarFindingKind`,
  `GrammarFinding`, `DefaultHeavilyUsedThreshold`, `GetFindings` (×2 overloads), and `DeclaredSets`.
- `Tests/ParserTests/Recovery/GrammarDiagnosticsTests.cs` — **modified**: added `NewQualityCorpusParser`,
  `GenManyItemErrors`, and the 3 findings tests.
- `docs/RecoveryImprovementPlan-progress6.3.md` — this file.

No S0–S6 / recovery behavior change, no 6.3.1 collection change, no `.csproj` change, no stop-if triggered
(declared set is enumerable from `parser.Rules`; the D1 slice runs in well under a second). **Not committed.**
