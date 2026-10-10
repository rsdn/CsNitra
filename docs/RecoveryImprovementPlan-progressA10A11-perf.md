# Progress — A10+A11 perf measurement (V1 corpus, wall time on vs off)

Task: measure V1 (10 largest Roslyn .cs) wall time with A10+A11 (current tree) vs without (HEAD `RecoveryEngine.cs`). A10 reduces scan iterations but probes ALL grammar terminals per non-stop position — on a 100+-terminal C# grammar the work-per-iteration may grow so the net effect is negative. `S3ScanPositions` counts iterations, not TryMatch calls → need wall time.

## Intent (before any work)
- Harness: `%TEMP%\opencode\probe-harness.ps1` (exists, from 9.1d; loads `bin\Debug\net8.0\ParserTests.dll`, sets `CSNITRA_ROSLYN_ROOT=C:\RSDN\roslyn`, invokes `Recovery.CSharpCorpusProbeTests.Test_CSharp_Corpus_Probe()` directly, prints `wallMs=`).
- Measurement-only probe adjustment: `CSharpCorpusProbeTests.cs` Trace line gains `S3ScanPositions={parser.Parser.S3ScanPositions}` (the probe did not log it; the requested table needs it). Applied ONCE before both runs → identical probe code in both configs, no comparison bias. Not a production file.
- Run 1 (ON): current tree (A10+A11 in RecoveryEngine.cs) → `dotnet build Tests\ParserTests --no-incremental` → harness → `%TEMP%\opencode\probe-A10A11-on.log`.
- Run 2 (OFF): `git stash push -- ExtensibleParser/Recovery/RecoveryEngine.cs` → build → harness → `%TEMP%\opencode\probe-A10A11-off.log` → `git stash pop` → build → verify diff restored (90/5).
- Decision rule: ON ≤ OFF or diff < 5% → keep A10 as is. ON > OFF by > 5% → add `HashSet<char>` first-char pre-check to the token-jump loops, re-measure ON.
- One run per measurement (deterministic corpus). No commits. Only RecoveryEngine.cs touched in production code.

## Log
- 2026-10-10 (local): harness found intact (9.1d version). Probe Trace gained `S3ScanPositions={parser.Parser.S3ScanPositions}` (measurement-only, applied once before BOTH runs — no comparison bias; counter resets at `Recover` start → per-parse total, S3 scan only, S6 uncounted).
- Run ON: build 0 errors → harness → `probe-A10A11-on.log`: wallMs=8520, Σelapsed=2560ms, processed=10, timedOut=0.
- Run OFF: `git stash push -- RecoveryEngine.cs` (diff empty) → build 0 errors → harness → `probe-A10A11-off.log`: wallMs=8510, Σelapsed=2715ms → `git stash pop` (clean, my stash dropped; remaining stash entries pre-existing) → build 0 errors → diff verified 90/5.

## Results

| file | elapsedMs ON | elapsedMs OFF | S3ScanPos ON | S3ScanPos OFF | Passes ON/OFF |
|---|---|---|---|---|---|
| NullableReferenceTypesTests.cs | 187 | 204 | 4 | 2 | 3/3 |
| DefaultInterfaceImplementationTests.cs | 65 | 62 | 2 | 1 | 2/2 |
| CollectionExpressionTests.cs | 48 | 54 | 2 | 1 | 2/2 |
| ExtensionTests.cs | 44 | 45 | 1 | 1 | 2/2 |
| Syntax.xml.Internal.Generated.cs | 97 | 120 | 2 | 2 | 2/2 |
| UnionsTests.cs | 753 | 805 | 1 | 1 | 2/2 |
| StaticAbstractMembersInInterfacesTests.cs | 1260 | 1316 | 2 | 1 | 2/2 |
| OutVarTests.cs | 33 | 39 | 2 | 1 | 2/2 |
| RefFieldTests.cs | 31 | 38 | 2 | 1 | 2/2 |
| ExtensionIndexersTests.cs | 42 | 32 | 2 | 1 | 2/2 |
| **Σ elapsedMs** | **2560** | **2715** | 19 | 11 | 21/19 |
| **wallMs (harness total)** | **8520** | **8510** | — | — | — |

`absorberFraction=100%`, Memo, RecoveryNodes identical in both configs (Wave 9 conclusion unchanged: grammar-coverage limit, not recovery).

## Decision
KEEP A10+A11 as is — no `HashSet<char>` pre-check.
- Primary metric wall time: 8520 vs 8510 = +0.12% → within noise (<5%) → decision rule says keep.
- Sharper parse-only comparison (Σ per-file elapsedMs, wallMs is dominated by identical JIT/preprocessor/GC): ON is 155 ms FASTER (−5.7%); ON wins 8/10 files, the 2 losses are 3–10 ms absolute.
- S3ScanPositions rose (19 vs 11) exactly as A11 predicts: the scan no longer stops at the first mid-word Ident, it travels further — but each position jumps whole tokens, so the extra grammar-terminal probing (100+ terminals) does not produce a net cost on this corpus. The feared work-per-iteration blowup is not observed.
- One run per config per task instruction (deterministic corpus).
