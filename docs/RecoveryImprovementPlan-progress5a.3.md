# RecoveryImprovementPlan — 5a.3 (quiet zone) progress

Wave 5a.3 introduces the quiet zone: after a candidate is accepted at recovery point `E`, mismatches at
`pos <= E` during the subsequent re-parse must not pollute `ErrorPos` / `_expected` / the snapshot.
This file tracks the sub-points; the 5a.3.2 section will be appended later.

---

## 5a.3.1 — состояние тихой зоны (инфраструктура)

Sub-point 5a.3.1: add the `_quietZoneEnd` state to `Parser` — field + reset in `Recover` + partial-hook
`InQuietZone` (declaration in `Parser.cs`, implementation in `Parser.Recovery.cs`) + activation in
`TryCandidate` (both success branches). This is pure infrastructure — the **suppression** logic
(`ReportMismatch` / `CaptureSnapshot`) is **not** added here (it is 5a.3.2), and **no test** is added for
this sub-point (verified in 5a.3.2).

Status: **DONE** (no deviations)

### What was added

`ExtensibleParser/Parser.Recovery.cs` — **modified**, all inside `#if RECOVERY` / `partial class Parser`:

- **Field** — right next to `_recoveryPoint`:
  - `private int _quietZoneEnd = -1;` (`Parser.Recovery.cs:16`)
- **Reset** — in the reset block at the start of `Recover`, right next to `_recoveryPoint = -1;`:
  - `_quietZoneEnd = -1;` (`Parser.Recovery.cs:310`)
- **Partial-hook implementation** — next to the other partial-hook implementations (`IsRecoveryPosition`):
  - `private partial bool InQuietZone(int pos) => _quietZoneEnd >= 0 && pos <= _quietZoneEnd;`
    (`Parser.Recovery.cs:155`)
- **Activation** — in the local function `TryCandidate`, in **both** branches where
  `recoveredThisIteration = true` is set, before it (per the task spec; the plan's "after" is the same
  statement position — nothing in between):
  - Success@EOF branch: `_quietZoneEnd = e;` (`Parser.Recovery.cs:407`)
  - `e2 > e` branch: `_quietZoneEnd = e;` (`Parser.Recovery.cs:416`)

`ExtensibleParser/Parser.cs` — **modified** (declaration only), next to the other partial-hook
declarations (`ResetRecoveryPoint`):

- `private partial bool InQuietZone(int pos);` (`Parser.cs:83`)

No suppression logic here (the suppression is in 5a.3.2, in `ReportMismatch` / `CaptureSnapshot`).
No changes to S1–S6, `SpeculativeCache`, `HygieneCore`/`ApplyPatches`/`RollbackPatches`/`PatchMemo`,
`ReportMismatch`, `CaptureSnapshot`, `Speculative`, the `TryCandidate` save/restore, the signature of
`RecoveryEngine.Generate`, or any `.csproj`.

### The test

**None.** This sub-point is infrastructure; the quiet zone is verified in 5a.3.2 (`QuietZoneTests`).
No test file was created.

### Verification (one-shot)

| Command | Result |
|---|---|
| `dotnet build Tests/ParserTests/ParserTests.csproj` | **0 errors** |
| `dotnet test Tests/ParserTests/ParserTests.csproj` | **Total: 360 · Passed: 358 · Failed: 0 · Skipped: 2** |

- The 2 skipped are the same pre-existing `[Ignore("WIP")]` in `GrammarValidationTests.cs`
  (`ShouldReportErrorForUndefinedRuleReference`, `RequiredSubruleNamesAreNotSpecified`) — unrelated.
- Baseline is **358 passed / 0 failed / 2 skipped**; after = **358 passed / 0 / 2** — exactly the same
  (no new tests, no regression).

### Files changed

- `ExtensibleParser/Parser.Recovery.cs` — **modified**: added the `_quietZoneEnd` field, its reset in
  `Recover`, the `InQuietZone` partial-hook implementation, and the activation (`_quietZoneEnd = e;`) in
  both success branches of `TryCandidate`.
- `ExtensibleParser/Parser.cs` — **modified** (declaration only): added the `InQuietZone` partial-hook
  declaration next to `ResetRecoveryPoint`.
