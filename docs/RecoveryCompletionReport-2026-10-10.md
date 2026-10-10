# Error recovery implementation and validation — 2026-10-10

Sequential implementation of the approved [completion plan](superpowers/plans/2026-10-10-error-recovery-completion.md).
Starting revision: `6e3916d8cfe72f3063c0e8a7d764f97fb555f1a4` (`grammar`). Implementation and measurements were prepared in the isolated
`csnitra-recovery` worktree for pull-request review; the original checkout and Nitrogen were not modified by implementation.

## Delivered behavior

| Area | Change and verification |
|---|---|
| Safety | Available-stack depth test replaces an invalid exact-400 expectation; deep damage retains full source and one stack diagnostic. Compiler/probe/session reuse tests pass. |
| Exact context | Sequence frames carry the actual `Seq` occurrence. Nested wrappers and recursive rules no longer select another alternative's suffix in expected/FOLLOW/S3/S6 sets. Synthetic legacy snapshots retain fallback. |
| Deduplication | Ordered sets use `HashSet<Terminal>` membership with `TerminalComparer`, preserving public lists and order. No general speedup claimed. |
| Boundaries | Shared S3/S6 scanner handles nesting, custom longest openers, multiple closers, trivia and explicit opaque regions. Zero-width events and empty EOF skips cannot become repairs. |
| C# lexical extents | Complete escaped/verbatim/raw/interpolated strings and characters hide internal delimiters. Lexical family is selected before probing; malformed raw/verbatim extents fall back to EOF. Host rule replacements and TDOPP rebuilds refresh the isolated grammar. |
| Diagnostics | Canonical missing-token placement happens before side-table attachment, after source-span matching. Public/root/subtree queries share attached instances. Implicit `OftenMissed` insertions can no longer appear clean. |
| Depth | Host/probe maxima and firing counts, initial/effective limits, and session resets expose the existing shrinking policy. This is instrumentation, not a claimed fix of historical F2 recursion. |
| Phases/rollback | Normal/Recovery ownership restores on exceptions and nested predicates. Rejected candidates restore patches, failure context and partial state. Normal capture is retained to preserve dynamic grammar behavior. |
| Local garbage | At a fresh Ref-owned loop item, a consuming scan uses that item's FIRST vocabulary, with a 64-character bound and opaque/bracket veto. Author anchors/soft starts disable the shortcut. Local repair precedes insertion candidates after S0, preventing an invented extra declaration. |
| Dense damage | Optional work/distance clustering suppresses expensive candidate generation nearby, resets across intact items or gaps, and defers at recognized item prefixes. Default off; no linear-complexity claim. |
| Consumers | CsNitra grammar AST, JSON, DOT and simplified C++ wrappers use compiler bail, avoiding AST casts or clean semantic results from incomplete syntax. C# exposes the recovery-capable parser/tree contract. |

No FIRST pruning or additional quiet-zone diagnostic suppression shipped. Broad FIRST filtering reproduced
four of eleven context regressions. A sound leading-literal subset passed tree/test checks but measured
120.484 ms (116.549–123.952) without it versus 118.630 ms (115.625–120.778) with it on 100 C# classes;
overlapping distributions did not justify retaining the prototype. Independent missing-token diagnostics remain visible.

A final read-only review found three scanner defects during implementation: opaque tokens could not serve
as anchors, malformed raw strings matched shorter regular prefixes, and opacity used stale grammar state.
Each received a failing regression and a fix; the follow-up static review found none of those defects remaining.

## Verification

Environment: macOS ARM64, .NET SDK 8.0.410, GraphViz 16.1.0. Root solution restore includes all 19 projects.
Use a restored solution: `--no-restore` without generated test-package imports can silently omit previously
unrestored test projects. The initial Release invocation covered only parser suites; the final gate follows
full `dotnet restore Nitra.sln` and explicitly verifies Regex/Workflow test totals too.

| Suite | Initial Debug observation | Final Debug | Final Release |
|---|---:|---:|---:|
| ParserTests | 445 passed / 1 failed / 2 skipped | 493 / 0 / 2 | 493 / 0 / 2 |
| CSharpGrammarTests | 1,524 / 0 / 3 | 1,539 / 0 / 3 | 1,539 / 0 / 3 |
| CsPreprocessorTests | 128 / 0 / 0 | 128 / 0 / 0 | 128 / 0 / 0 |
| RegexTests | not run | not required separately | 9 / 0 / 0 |
| WiWorkflowTests | not run | not required separately | 1 / 0 / 0 |

Final restored Release gate: **2,170 passed, zero failed, five skipped**, exit 0. Full solution Release build
also exits 0 with zero warnings/errors, including workflow and interop generators/consumers.

The five existing skips remain: `ShouldReportErrorForUndefinedRuleReference`,
`RequiredSubruleNamesAreNotSpecified`, `RawStringLiteral_CloseRunPrefix_MatchLengthSemantics`,
`StringLiteral_RawNewline_Fails`, `VerbatimStringLiteral_ExactBoundary_MatchesPrefixOnly`.
They are not newly asserted behavior. The workflow test is still a placeholder, not broad workflow acceptance.

Structural tests assert later declaration identities, an intact item after a damaged separated item,
an outer declaration after nested damage, exact terminal-leaf source partition, independent diagnostics,
and strict coarse fallback. The 4,000-unmatched-opener case terminates with preserved source.
These checks are stronger than an EOF-only assertion, but do not prove optimal recovery for arbitrary grammars.

Commands (from repository root, serialized builds):

```sh
dotnet restore Nitra.sln
dotnet test Tests/ParserTests --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test Tests/CSharpGrammarTests --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test Tests/CsPreprocessorTests --no-restore -m:1 -p:UseSharedCompilation=false
dotnet test Nitra.sln --configuration Release --no-restore -m:1 -p:UseSharedCompilation=false
dotnet build Nitra.sln --configuration Release --no-restore -m:1 -p:UseSharedCompilation=false
```

## Measurements

Debug, same machine/SDK, fresh parser per parse, deterministic `RecoveryProfile.Test`, one warm-up and five
measured runs per case; median and min–max, allocated bytes on the calling thread. Parsing excludes grammar
construction. These are local experiments, not CI latency thresholds or worst-case complexity proofs.
Starting-revision comparisons use a separate source archive. Clustering and local-repair comparisons toggle
one profile option on the same implementation. The latter isolate policy effects, not the whole revision.

| Whole revision / MiniC corpus | Starting median ms (range) | Current median ms (range) | Allocation bytes before → after | Diagnostics before → after |
|---|---:|---:|---:|---:|
| Clean 500 functions | 32.770 (31.938–38.859) | 33.416 (32.840–37.082) | 37,144,128 → 37,448,472 | 0 → 0 |
| D1.2: 500 clean functions, missing separator in final function | 48.893 (46.727–50.263) | 50.356 (47.980–52.333) | 38,931,896 → 39,241,808 | 0 → 1 |
| D1.6: 50 functions, missing separator every fifth | 6.142 (5.788–7.307) | 5.959 (5.776–6.388) | 4,843,664 → 4,882,064 | 0 → 10 |

Clean median time increases 2.0%, with overlapping ranges and 0.8% more measured allocations; no
repeatable >5% clean-input regression is established. D1.2 medians differ by 3.0%, also with overlapping ranges.
A prior sample ran amid other build/test work and was excluded from this isolated comparison.

Missing-separator diagnostics now report existing recovery that the old `OftenMissed` path hid. That semantic
work must not be mistaken for equivalent old/new diagnostic output. All three cases reach EOF on both revisions.

| Opt-in clustering corpus | Off median ms (range) | On median ms (range) | Allocation bytes off → on | Retained real tokens / diagnostics (both) |
|---|---:|---:|---:|---:|
| 20 damaged items | 2.977 (2.741–3.360) | 2.008 (1.915–2.637) | 2,728,352 → 2,120,592 | 20 / 20 |
| 40 damaged items | 9.295 (9.147–10.366) | 6.790 (6.730–7.156) | 8,778,752 → 6,607,472 | 40 / 40 |
| 80 damaged items | 39.806 (35.913–45.375) | 26.009 (21.463–27.242) | 30,987,960 → 22,848,040 | 80 / 80 |

Corpus: repeat `a $ ; `, `Item = Seq(a,b,;)`, `Module = ZeroOrMany(Ref(Item))`, local repair off,
clustering threshold 1 for the experiment. Accepted strategies are S3 throughout; broad generation calls remain
20/40/80 and probe misses remain 19/39/79. Clustering reduces work within generation, not every boundary probe.
Scaling is still superlinear. Production default threshold is 16; these timings do not establish its benefit.

Local corpus: repeat `a; ### b; ` 80 times, `Item = Seq(a,;) | Seq(b,;)`, repeating module, clustering off.

| Local repair | Median ms (range) | Allocation bytes | Real tokens | Diagnostics | Broad generation / probe misses |
|---|---:|---:|---:|---:|---:|
| Off | 91.717 (87.313–119.669) | 145,342,920 | 160 | 160 | 160 / 159 |
| On | 18.876 (17.960–19.173) | 31,663,328 | 160 | 80 | 0 / 0 |

Off accepts 80 S1 and 80 S3 repairs; on accepts 80 local rank-2 repairs. Later real tokens remain intact,
while the extra invented declaration/insertion cascade disappears. Absorbers cover each damaged interval and
trivia through the next item boundary; display diagnostics identify the first dirty word instead of the entire
absorber. Coverage and retained declaration count have independent regression assertions.

Earlier isolated A7 and A6 D1.2/D1.6 measurements had overlapping/noisy timing differences and no allocation
change on those paths; neither is presented as a measured general performance win.

## Remaining acceptance and deferred work

1. **Linux/Windows acceptance is pending.** The existing CI matrix installs GraphViz and runs Release on
   Linux, Windows and macOS. PR preparation enables the same matrix for PRs targeting `grammar`;
   cross-platform results must be recorded after the published PR runs. macOS stack guards passing
   does not establish the other platforms' stack behavior.
2. **Historical F2 exact reproduction is pending.** The old note reports 37 characters and 514 firings but
   contains no input/grammar. New metrics are available; an exact regression and a recursion-cause fix require
   the original case or a newly reproducible equivalent. No claim that instrumentation fixes the symptom.
3. **Broad loop ownership is still conservative.** Local repair/clustering use actual `LoopFrameLocation.Element`;
   legacy `DeriveLoopAnchors`/`FindEnclosingLoopElement` can still search loops in containing alternatives.
   Exact sequence suffix ownership is fixed, not every legacy loop inference. A separate regression should
   justify narrowing those helpers without dropping existing authored/derived anchors.
4. **Normal-path optimization remains deferred.** Complete normal snapshots were retained. No instrumented
   replay or broad FIRST filter was kept; dynamic grammar side effects require a separate correctness design.
5. **Optional backlog remains:** real compiler two-stage integration, multiple S3 stops, span/range input,
   code-generation consumer preparation and persistent subtree reuse/diagnostic lifetime. No global chart or
   hard time deadline was introduced. The historical external watcher/duplicate-Compile issue did not reproduce.

Clustering is intentionally opt-in, and the historical linear-scaling aspiration is not met. These limits mean
this is a completed local implementation/evaluation slice with explicit acceptance gaps, not closure of every
research or cross-platform requirement. Current author-facing contracts are in [the guide](RecoveryAuthorGuide.md).
