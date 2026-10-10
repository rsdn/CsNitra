# Progress 2.1 — Resolve conflict in ExtensibleParser/Recovery/RecoveryEngine.cs

## Intentions
1. Read the merged-with-markers working file and identify all 6 conflict regions.
2. Extract both full sides: `git show :2:...` (ours = PR hardening) and `git show :3:...` (theirs = grammar A8/A10/A11/R2).
3. Resolve each hunk per policy:
   - Base = grammar (theirs); layer PR upgrades where explicit.
   - GenerateS3/GenerateS6: keep grammar token-wise scan (`CollectGrammarTerminals`) + `IsCatchAllRegexTerminal` filter + `AddSuffixObligations`; take scan BOUNDARIES (bracket pairs, opaque regions) from PR shared scanner / `SeqFrameLocation.Sequence`.
   - Pure PR additions (scanner plumbing, `_phase`, rollback, `LocalGarbage`) → keep.
   - Pure grammar additions (`IsCatchAllRegexTerminal`, `AddSuffixObligations`, token scan) → keep.
   - Unreconcilable → keep grammar side, record in report.
4. Consistency check: grep ExtensibleParser/ for kept symbols. Reconcile to existing names.
5. Acceptance: no conflict markers; `dotnet build ExtensibleParser/ExtensibleParser.csproj` → 0 errors.

## Key finding (drove the S3/S6 decision)
- PR side (stage 2) replaced both inline scans with the shared `RecoveryScan.Find` (RecoveryScan.cs, PR-only file, already in the merged tree) which handles bracket pairs (`parser.BracketPairs`) + opaque regions (`parser.RecoveryOpaqueTerminals`) + trivia runs + EOF. It has NO token-jump hook.
- Grammar side (stage 3) has the inline scan loops with the A10 token-jump (`CollectGrammarTerminals` + longest-token jump) and hardcoded `{[()])` char counters.
- The merged tree keeps the grammar-side test `Tests/ParserTests/Recovery/A10A11TokenScanTests.cs`, which asserts `parser.S3ScanPositions <= 24` (measured 19 with token-jump, 30 without). So token-jump MUST survive functionally; dropping the PR scanner is not acceptable either (policy).
- Since `RecoveryScan.cs` must NOT be edited (one-file constraint), the reconciliation is a single new private helper in RecoveryEngine.cs — `FindStopWithTokenJump` — a port of `RecoveryScan.Find` (same boundary algorithm/data: BracketPairs stack, opaque regions, trivia runs, EOF rule, notePosition) plus the A10 token-jump branch and the grammar-side `(pos, terminal)` match cache. Used by both GenerateS3 and GenerateS6.

## Hunk-by-hunk decisions
1. `AddSuffixObligations` stop-set filter (was ~728): ours `seen.Add(t)` vs theirs catch-all filter + `stopSet.Contains`. KEPT BOTH: `!IsCatchAllRegexTerminal(t) && seen.Add(t)` (grammar semantics, PR's pre-seeded `seen` HashSet for O(1) dedup).
2. GenerateS3 pre-scan block (was ~750): ours empty, theirs `matchCache` + local `Match` + `grammarTerminals`. KEPT grammar (`grammarTerminals`); `matchCache`/`Match` moved into the new shared helper (they are scan-local).
3. GenerateS3 scan body (was ~786): ours `new RecoveryScan(...).Find(e, maxS, scanStops, parser.NoteS3ScanPosition)` vs theirs inline curly/paren/bracket + trivia + token-jump loop. MERGED: cost-ordered `scanStops` + EOF append (ours) fed into `FindStopWithTokenJump(...)` (PR boundary mechanism + grammar token-jump). Grammar's hardcoded bracket chars dropped in favour of `parser.BracketPairs`/`RecoveryOpaqueTerminals`.
4. GenerateS6 anchor-First filter (was ~1074): same shape as hunk 1 → KEPT BOTH (`!IsCatchAllRegexTerminal(t) && seen.Add(t)`).
5. GenerateS6 terminators filter (was ~1083): same → KEPT BOTH.
6. GenerateS6 scan result (was ~1101): ours `s = scanned.Position; foundT = scanned.Terminal;` vs theirs inline `pos` loop (references `pos`, which does not exist post-merge). MERGED: scan call switched to `FindStopWithTokenJump(parser, input, e, input.Length, stopSet, grammarTerminals)`, result assignment kept from ours.

## Symbol consistency check (all exist in merged tree)
- `CollectGrammarTerminals`, `IsCatchAllRegexTerminal`, `CatchAllRegexTerminalKinds`, `AddSuffixObligations`, `GetSuffixObligationFirsts` — RecoveryEngine.cs (grammar side, kept).
- `SeqFrameLocation { ElementIndex, Sequence }` — StackFrame.cs / Parser.cs / FollowSetCalculator.cs (PR plumbing, kept).
- `parser.BracketPairs`, `parser.RecoveryOpaqueTerminals`, `BracketPair` — Parser.RecoveryBoundaries.cs, BracketPairs.cs (PR, kept).
- `TerminalComparer.Instance` / `TerminalComparer.KeyComparer` — TerminalComparer.cs.
- `RecoveryScan` — still used by GarbageScanner.cs (untouched); no longer referenced from RecoveryEngine.cs.
- No symbol had to be renamed; nothing invented beyond the new private helper `FindStopWithTokenJump`.

## Acceptance results
- `Select-String -Path ExtensibleParser/Recovery/RecoveryEngine.cs -Pattern '^(<<<<<<<|=======|>>>>>>>)'` → 0 matches.
- `dotnet build ExtensibleParser/ExtensibleParser.csproj` → Build succeeded, 0 Warning(s), 0 Error(s).
- Encoding/line endings preserved: UTF-8 without BOM (as before), 1364 CRLF, 0 lone LF.
- Index left untouched (file still `UU`); staging/commit left to the orchestrating session.

## Status
- [x] Progress file created
- [x] Analyze conflict regions (6 regions, 18 markers)
- [x] Resolve 6 hunks (0 markers left)
- [x] Symbol consistency check
- [x] Marker check + build (0 errors, 0 warnings)

## Follow-up suggestion (not done — out of scope)
Fold the A10 token-jump into `RecoveryScan.Find` as an optional `IReadOnlyList<Terminal>? tokenVocabulary` parameter so `FindStopWithTokenJump` and `RecoveryScan` do not duplicate the boundary algorithm.
