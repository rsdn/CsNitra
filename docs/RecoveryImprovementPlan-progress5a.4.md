# RecoveryImprovementPlan — 5a.4 (single-token deletion / `Extraneous`) progress

Wave 5a.4 adds the single-token-deletion strategy (`GenerateS1b`): at a recovery point `E` where the next
token does not match any expected terminal, the engine absorbs the offending token and re-parses,
emitting an `Extraneous` diagnostic. This file tracks the sub-points; the 5a.4.2 section will be
appended later.

---

## 5a.4.1 — `RecoveryKind.Extraneous` (enum)

Sub-point 5a.4.1: add the `Extraneous` value to the `RecoveryKind` enum in
`ExtensibleParser/Recovery/RecoveryDiagnostic.cs`. This is pure infrastructure — the generator and its
wiring into `Generate` (and the diagnostic emission) are **not** added here (that is 5a.4.2), and **no
test** is added for this sub-point (verified in 5a.4.2, `SingleTokenDeletionTests`).

Status: **DONE** (no deviations)

### What was added

`ExtensibleParser/Recovery/RecoveryDiagnostic.cs` — **modified**, the `RecoveryKind` enum only:

- Before: `public enum RecoveryKind { Inserted, Skipped, Unrecovered }`
- After: `public enum RecoveryKind { Inserted, Skipped, Unrecovered, Extraneous }` (`RecoveryDiagnostic.cs:3`)

The `Extraneous` value is appended as the last member (order-preserving; no existing value renamed or
moved). The `RecoveryDiagnostic` record and everything else in the file are untouched. No changes to
S1–S6, `RecoveryEngine`, or any `.csproj`.

### The test

**None.** This sub-point is infrastructure; the `Extraneous` diagnostic is verified in 5a.4.2
(`SingleTokenDeletionTests`). No test file was created.

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

- `ExtensibleParser/Recovery/RecoveryDiagnostic.cs` — **modified**: added the `Extraneous` value to the
  `RecoveryKind` enum (appended after `Unrecovered`).
