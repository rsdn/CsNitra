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

---

## 5a.2.2 — S2: счётчик + trivia jump

Sub-point 5a.2.2: in the S2 scan loop increment `parser.S2ScanPositions` at every iteration and skip a
trivia run with one `Trivia.TryMatch` jump. The `FirstMatchesAt` / `Speculative` / `AddResyncCandidate`
logic is unchanged.

Status: **DONE** (one deviation — see below: the task-specified test input cannot fail before the change;
a second in-window test method was added to carry the before/after regression)

### What was changed

`ExtensibleParser/Recovery/RecoveryEngine.cs` — **modified**, inside `GenerateS2`, at the start of the
S2 scan loop body (`for (var s = e; s <= maxS && !foundT1; s++)`, `RecoveryEngine.cs:282`):

- `parser.NoteS2ScanPosition();` (`RecoveryEngine.cs:284`) — increments `S2ScanPositions` at every
  iteration.
- Trivia jump (`RecoveryEngine.cs:285-293`): `var triviaLen = parser.Trivia.TryMatch(input, s);` — if
  `triviaLen > 0`, `s += triviaLen - 1; continue;`. **Jump offset: `k - 1`, not `k`** — the loop's own
  `s++` after `s += k - 1` lands the scan exactly on `s + k`, the first non-trivia position. (`s += k`
  would overshoot by one and skip a real position.)

No changes to S1/S3/S4/S5/S6, the `GenerateS2` signature, `SpeculativeCache`,
`CreateScratchParser`/`ParseRuleOnce`, `HygieneCore`/`ApplyPatches`/`RollbackPatches`/`PatchMemo`, the
signature of `RecoveryEngine.Generate`, or any `.csproj`.

### The test

`Tests/ParserTests/Recovery/S2TriviaJumpTests.cs` — **new** (same grammar as `TierBudgetTests`; S2 anchor
`Ref("Stmt")` derived from the `Stmts` loop; the mismatch in Expr puts the recovery point `e` on the first
`#`; `Speculative("Stmt")` at `b` fails — `Stmt` requires `:` — so the S2 scan runs to `maxS`; the actual
resync is an S3 terminator chain `b (Ident) → ; → } → EOF` + S6 bottom):

1. `S2TriviaJump_SameResync_FewerScannedPositions` — the task-specified test: inputs
   `"{ a: 1+ ### b ; }"` (no padding) and `"{ a: 1+   ### b ; }"` (3-space padding between `1+` and
   `###`). Asserts: (a) `S2ScanPositions > 0` for both (the scan is triggered and reaches `Speculative`
   at `b`); (b) the recovery outcome is the same — identical normalized diagnostics (Kind, length,
   message without absolute positions; the S6 "bottom skip" length minus the padding delta) and the S3
   resync positions shifted by exactly the padding delta (2); (c) `S2ScanPositions(padded) -
   S2ScanPositions(no-padding) < 3`.
2. `S2TriviaJump_PaddingInsideScanWindow_IsSkipped` — **added (deviation)**: the task-specified padding
   lies *before* `e` (the parser skips the trivia before reporting the mismatch, so `e` lands on the
   first `#`), i.e. outside the S2 scan window `[e..maxS]`; the window shifts rigidly with the padding,
   so the counter difference is **0 both before and after the change** and assert (c) passes in both
   states — the specified test cannot fail before the change (with a padding delta of 2 the maximum
   possible difference is 2 < 3). This second method puts the padding *inside* the scan window (4 spaces
   between `###` and `b`, delta 3) and asserts the same (a)/(b)/(c) shape: before the change
   `diff == 3` → **fails**; after the change `diff == 0` → passes. It is the regression test for the jump.

### Verification (one-shot)

| Command | Result |
|---|---|
| `dotnet build Tests/ParserTests/ParserTests.csproj` | **0 errors** (0 warnings) |
| `dotnet test Tests/ParserTests/ParserTests.csproj` | **Total: 355 · Passed: 353 · Failed: 0 · Skipped: 2** |

- Baseline is **351 passed / 0 failed / 2 skipped**; after = **353 passed / 0 / 2** — exactly baseline +
  the 2 new tests, no regression. The 2 skipped are the same pre-existing `[Ignore("WIP")]` in
  `GrammarValidationTests.cs` — unrelated.
- Counter values (measured): task inputs — before the jump: 23 / 23 (diff 0); after the jump: 23 / 23
  (diff 0). In-window inputs — before the jump: 26 / 23 (**diff 3, test fails**); after the jump: 23 / 23
  (diff 0, test passes). The jump saves exactly the padding length; the padded scan window is longer by
  the same amount (it runs to `input.Length`), hence the net diff is 0 after the change.
- Pre-change confirmation: the jump was temporarily reverted (counter kept), the test run showed
  `S2TriviaJump_PaddingInsideScanWindow_IsSkipped` **failing** with `padded S2ScanPositions=26,
  no-padding=23 (diff=3, expected < 3)` while `S2TriviaJump_SameResync_FewerScannedPositions` still
  passed (diff 0); the jump was then re-applied and both tests pass.

### Files changed

- `ExtensibleParser/Recovery/RecoveryEngine.cs` — **modified**: `NoteS2ScanPosition()` increment +
  trivia jump (`s += triviaLen - 1; continue;`) at the start of the S2 scan loop body in `GenerateS2`.
- `Tests/ParserTests/Recovery/S2TriviaJumpTests.cs` — **new**: the task-specified test (task inputs,
  asserts (a)/(b)/(c)) + the in-window regression test method that fails without the jump.
- `docs/RecoveryImprovementPlan-progress5a.2.md` — **modified**: this 5a.2.2 section appended.

No S1/S3/S4/S5/S6 / `GenerateS2`-signature / `SpeculativeCache` / `CreateScratchParser` /
`ParseRuleOnce` / `HygieneCore` / `ApplyPatches` / `RollbackPatches` / `PatchMemo` /
`Generate`-signature / `.csproj` changes. Not committed.

### Deviations

1. **Jump offset is `s += k - 1`** (not `s += k` as literally written in the plan), per the task's own
   offset note: with the loop's `s++`, `s += k - 1` lands the scan exactly on the first non-trivia
   position `s + k`; `s += k` would overshoot by one.
2. **The task-specified test cannot fail before the change.** The 3-space padding between `1+` and
   `###` is before the recovery point `e` (the parser skips trivia before the mismatch is reported, so
   `e` is the first `#`), i.e. outside the S2 scan window `[e..maxS]`. All scan windows shift rigidly by
   the padding delta, so `S2ScanPositions(padded) - S2ScanPositions(no-padding) == 0` both before and
   after the change (verified: 23/23 in both states); with a padding delta of 2 the difference can never
   reach 3. The specified test therefore passes in both states and cannot serve as the before/after
   regression. To satisfy "the test MUST fail before the change", a second test method
   (`S2TriviaJump_PaddingInsideScanWindow_IsSkipped`) was added with the padding inside the scan window
   (4 spaces between `###` and `b`): it fails before the change (diff 3) and passes after (diff 0). The
   task-specified test itself is kept as-is (asserts (a)/(b)/(c) with the exact specified inputs).
3. **Assert (b) compares normalized diagnostics** (Kind, region length, message with absolute positions
   stripped) plus an explicit check that the S3 resync positions are shifted by exactly the padding
   delta — the raw diagnostics contain absolute positions (S6 "bottom skip to N", "error at N …"), so
   literal equality between the two inputs is impossible by construction.
