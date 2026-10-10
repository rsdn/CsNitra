# Progress — Sub-point 12: Trivia-aware coverage in ParseHealth

## Intentions (before analysis)

1. `ParseHealthReport`: add `int UnionCoverage` between `AbsorberFraction` and `RecoveryNodes`
   = total bytes covered by the UNION of all terminal spans (merge overlapping/adjacent intervals).
2. Change `Analyze` signature to `Analyze(ISyntaxNode? root, string input, bool successAtEof, Terminal trivia)`
   (drop `inputLength`, use `input.Length`). Update Russian XML docs: invariant I4 becomes
   trivia-aware — gaps between terminal spans allowed only if fully consumed by the trivia terminal.
3. Replace strict-contiguity `CoversInput` with trivia-aware algorithm:
   - collect all TerminalNode spans (same traversal as now);
   - no spans → `coversInput = input.Length == 0`, `unionCoverage = 0`;
   - sort by StartPos;
   - helper `IsTriviaGap(input, from, to, trivia)`: loop `trivia.TryMatch(input, pos)`;
     `len <= 0` (failure -1 or zero-progress 0) → false; else `pos += len`;
   - require trivia gap before first span, between every uncovered gap while walking with
     `coveredUntil = max(coveredUntil, next.EndPos)` (overlaps tolerated), and after last span to EOF;
   - `unionCoverage` = merged-interval sum (extend when `start <= currentEnd`, else flush + new; sum `end - start`).
4. Verdict decision order UNCHANGED: Failed → Uncovered → FalseGreen → Clean → Recovered.
5. Callers (exact updates):
   - `UsingStaticRecoveryTests.cs`: `ParseHealth.Analyze(node, input, end == input.Length, parser.Trivia)`;
   - `CSharpCorpusHealthProbeTests.cs`: `ParseHealth.Analyze(success ? node : null, input, success, parser.Parser.Trivia)`
     and add `unionCoverage={health.UnionCoverage}` right after `absorberBytes={health.AbsorberBytes}` in Trace.WriteLine.
     Nothing else touched; `[Ignore]` stays.
6. Style: Allman braces, 4-space indent, CRLF, UTF-8 BOM preserved, Russian XML docs.
7. Acceptance: `dotnet build Tests/ParserTests` → 0 errors; `dotnet build Tests/CSharpGrammarTests` → 0 errors.
   No tests run. Build errors outside the three files → STOP and report.

## Status log

- [x] Read all three target files (current state confirmed as in CONTEXT).
- [x] Edit ParseHealth.cs: `UnionCoverage` inserted in the record between `AbsorberFraction` and
      `RecoveryNodes`; `Analyze(ISyntaxNode?, string input, bool successAtEof, Terminal trivia)`;
      span collection + sort moved into `Analyze`; trivia-aware `CoversInput(input, spans, trivia)`;
      new `IsTriviaGap` and `UnionCoverage(spans)` helpers; verdict order untouched;
      Russian XML docs updated (I4 now trivia-aware).
- [x] Update UsingStaticRecoveryTests.cs caller → `Analyze(node, input, end == input.Length, parser.Trivia)`.
- [x] Update CSharpCorpusHealthProbeTests.cs caller → `Analyze(success ? node : null, input, success, parser.Parser.Trivia)`
      + `unionCoverage={health.UnionCoverage}` inserted after `absorberBytes=...` in the Trace line.
      `[Ignore]` and everything else untouched.
- [x] Encoding check: all three files still UTF-8 BOM (EF BB BF) + CRLF.
- [x] `dotnet build Tests/ParserTests` → Build succeeded, 0 Warning(s), 0 Error(s).
- [x] `dotnet build Tests/CSharpGrammarTests` → Build succeeded, 0 Warning(s), 0 Error(s).
- [x] No tests run (orchestrator verifies). No commits.

## Notes

- Only the three allowed files were modified by this sub-point. `git status` also shows
  `ExtensibleParser/Recovery/RecoveryEngine.cs` as modified — that is another subagent's concurrent
  work in the same working tree, not touched here; both builds above still pass with it present.
- `Math` / `List<>` resolve via `Shared/GlobalUsings.cs` (`global using System;`,
  `global using System.Collections.Generic;`), so no new usings were added to ParseHealth.cs.
