# RecoveryImprovementPlan — 5a.2 (B3) progress

Wave 5a.2 (B3) makes the S2/S3 recovery scans cheap: a trivia-jump in S2/S3 plus a min-`IndexOf` jump in S2,
backed by two scan-position counters. This file tracks the sub-points; 5a.2.2 / 5a.2.3 / 5a.2.4 sections
will be appended later.

---

## 5a.2.1 — счётчики сканированных позиций (инфраструктура)

Sub-point 5a.2.1: add the `S2ScanPositions` / `S3ScanPositions` counters to `Parser` (reset in `Recover`),
modeled exactly on the existing `HygieneRemovals` counter. This is pure infrastructure — the increment lives
in 5a.2.2 (S2) / 5a.2.3 (S3), inside `RecoveryEngine`, so **no increment is added here** and **no test** is
added for this sub-point.

Status: **DONE** (no deviations)

### What was added

`ExtensibleParser/Parser.Recovery.cs` — **modified**, all inside `#if RECOVERY` / `partial class Parser`:

- **Two read-only counters**, placed right after `HygieneRemovals` (modeled on it — `public int … { get; private set; }`):
  - `public int S2ScanPositions { get; private set; }` (`Parser.Recovery.cs:37`)
  - `public int S3ScanPositions { get; private set; }` (`Parser.Recovery.cs:41`)
- **Reset** — in the beginning of `Recover`, in the reset block right next to `HygieneRemovals = 0;`:
  - `S2ScanPositions = 0;` (`Parser.Recovery.cs:300`)
  - `S3ScanPositions = 0;` (`Parser.Recovery.cs:301`)

No increment here (the increment is in 5a.2.2/5a.2.3, in `RecoveryEngine`). No changes to `RecoveryEngine`,
S1–S6, `SpeculativeCache`, `HygieneCore`/`ApplyPatches`/`RollbackPatches`/`PatchMemo`, the signature of
`RecoveryEngine.Generate`, or any `.csproj`.

### The test

**None.** This sub-point is infrastructure; the counters are verified in 5a.2.2 / 5a.2.3, where the increment
lives. No test file was created and no reflection-based smoke test was added (it would mean nothing).

### Verification (one-shot)

| Command | Result |
|---|---|
| `dotnet build Tests/ParserTests/ParserTests.csproj` | **0 errors** |
| `dotnet test Tests/ParserTests/ParserTests.csproj` | **Total: 353 · Passed: 351 · Failed: 0 · Skipped: 2** |

- The 2 skipped are the same pre-existing `[Ignore("WIP")]` in `GrammarValidationTests.cs`
  (`ShouldReportErrorForUndefinedRuleReference`, `RequiredSubruleNamesAreNotSpecified`) — unrelated.
- Baseline is **351 passed / 0 failed / 2 skipped**; after = **351 passed / 0 / 2** — exactly the same (no new
  tests, no regression).

### Files changed

- `ExtensibleParser/Parser.Recovery.cs` — **modified**: added the `S2ScanPositions` / `S3ScanPositions`
  read-only counters and the two `… = 0;` reset lines in the `Recover` reset block.
- `docs/RecoveryImprovementPlan-progress5a.2.md` — **new**: this progress file (5a.2.1 section).

No parser-engine / `RecoveryEngine` / S1–S6 / `SpeculativeCache` / `Generate`-signature / `.csproj` changes.
Not committed.

### Deviations

None.

---

## 5a.2.1a — Note-методы для счётчиков

Sub-point 5a.2.1a: add two public Note methods to `Parser` so the recovery engine (a separate static class
that takes `parser` as a parameter) can increment the `S2ScanPositions` / `S3ScanPositions` counters despite
their `private set`. This is pure infrastructure — **no test** is added for this sub-point.

Status: **DONE** (no deviations)

### What was added

`ExtensibleParser/Parser.Recovery.cs` — **modified**, all inside `#if RECOVERY` / `partial class Parser`,
placed right next to the existing `S2ScanPositions` / `S3ScanPositions` counters (after `S3ScanPositions`):

- `public void NoteS2ScanPosition() => S2ScanPositions++;` (`Parser.Recovery.cs:44`)
- `public void NoteS3ScanPosition() => S3ScanPositions++;` (`Parser.Recovery.cs:45`)

The counters themselves, the reset block in `Recover`, and everything else are untouched. No changes to
`RecoveryEngine`, S1–S6, `SpeculativeCache`, `HygieneCore`/`ApplyPatches`/`RollbackPatches`/`PatchMemo`, the
signature of `RecoveryEngine.Generate`, or any `.csproj`.

### The test

**None.** This sub-point is infrastructure; the counters (and the Note methods) are exercised in 5a.2.2 /
5a.2.3, where the increment lives. No test file was created.

### Verification (one-shot)

| Command | Result |
|---|---|
| `dotnet build Tests/ParserTests/ParserTests.csproj` | **0 errors** |
| `dotnet test Tests/ParserTests/ParserTests.csproj` | **Total: 353 · Passed: 351 · Failed: 0 · Skipped: 2** |

- The 2 skipped are the same pre-existing `[Ignore("WIP")]` in `GrammarValidationTests.cs`
  (`ShouldReportErrorForUndefinedRuleReference`, `RequiredSubruleNamesAreNotSpecified`) — unrelated.
- Baseline is **351 passed / 0 failed / 2 skipped**; after = **351 passed / 0 / 2** — exactly the same (no new
  tests, no regression).

### Files changed

- `ExtensibleParser/Parser.Recovery.cs` — **modified**: added the two public Note methods
  (`NoteS2ScanPosition` / `NoteS3ScanPosition`) next to the existing counters.
- `docs/RecoveryImprovementPlan-progress5a.2.md` — **modified**: this 5a.2.1a section appended.

No parser-engine / `RecoveryEngine` / S1–S6 / `SpeculativeCache` / `Generate`-signature / `.csproj` changes.
Not committed.

### Deviations

None.
