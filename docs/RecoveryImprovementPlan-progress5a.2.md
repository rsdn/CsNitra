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

---

## 5a.2.3 — S3: счётчик + trivia jump (решение (б): по всему trivia)

Sub-point 5a.2.3: in the S3 scan loop increment `parser.S3ScanPositions` at every iteration and skip a
trivia run (including `//` and `/* */` comments) with one `Trivia.TryMatch` jump. The `Match`/depth logic
is unchanged. Decision (б): jump over ALL trivia — brackets inside `/* ... */` no longer affect
`curly/paren/bracket` (fix).

Status: **DONE** (no deviations; test inputs corrected to be strictly after `e` per the new rule)

### What was changed

`ExtensibleParser/Recovery/RecoveryEngine.cs` — **modified**, inside `GenerateS3`, in the S3 scan loop
(`for (var s = e + 1; s <= maxS; s++)`, `RecoveryEngine.cs:428`):

- `parser.NoteS3ScanPosition();` — at the start of the loop body (before the `switch`).
- Trivia jump — **after** the `switch (input[s-1])` block (so `input[s-1]` is still counted in
  `curly/paren/bracket`) and **before** the `foreach` terminator check:
  `var triviaLen = parser.Trivia.TryMatch(input, s); if (triviaLen > 0) { s += triviaLen - 1; continue; }`.
  Jump offset: `k - 1` (the loop's `s++` lands the scan exactly on `s + k`, the first non-trivia position).

No changes to S1/S2/S4/S5/S6, `SpeculativeCache`, `CreateScratchParser`/`ParseRuleOnce`,
`HygieneCore`/`ApplyPatches`/`RollbackPatches`/`PatchMemo`, the signature of `RecoveryEngine.Generate`,
or any `.csproj`.

### The test

`Tests/ParserTests/Recovery/S3TriviaJumpTests.cs` — **new** (grammar: same as `TierBudgetTests` but with a
wider Trivia terminal `\s + // + /* */` to make decision (б) testable):

1. `S3TriviaJump_SameResync_FewerScannedPositions` — inputs `"{ a: 1+ ### ; }"` (no padding) and
   `"{ a: 1+ ###    ; }"` (3-space padding between `###` and `;` — strictly after `e`). Asserts:
   (a) `S3ScanPositions > 0`; (b) S3 `skip to terminator` diagnostics are the same (normalized);
   (c) `S3ScanPositions(padded) - S3ScanPositions(no-padding) < 3`.
2. `S3TriviaJump_ClosingBraceInsideBlockComment_NotCounted` — inputs `"{ a: 1+ ### ; }"` (no comment)
   and `"{ a: 1+ ### /* } */ ; }"` (comment between `###` and `;` — strictly after `e`). Asserts that S3
   did NOT stop on the `}` inside the comment: resync positions match the no-comment variant shifted by
   the comment length (delta 7).

### Verification (one-shot)

| Command | Result |
|---|---|
| `dotnet build Tests/ParserTests/ParserTests.csproj` | **0 errors** |
| `dotnet test Tests/ParserTests/ParserTests.csproj` | **Total: 357 · Passed: 355 · Failed: 0 · Skipped: 2** |

- Baseline is **353 passed / 0 failed / 2 skipped** (after 5a.2.2); after = **355 passed / 0 / 2** —
  exactly baseline + the 2 new tests, no regression.

### Files changed

- `ExtensibleParser/Recovery/RecoveryEngine.cs` — **modified**: `NoteS3ScanPosition()` increment +
  trivia jump (after `switch`, before terminator check) in `GenerateS3`.
- `Tests/ParserTests/Recovery/S3TriviaJumpTests.cs` — **new**: 2 tests with padding/comment strictly
  after `e`.
- `docs/RecoveryImprovementPlan.md` — **modified**: added "Rules for test inputs for scans" section;
  fixed 5a.2.2/5a.2.3/5a.2.4 templates.
- `docs/RecoveryImprovementPlan-progress5a.2.md` — **modified**: this 5a.2.3 section appended.

### Deviations

None. (The initial subagent run had the same pre-`e` padding error as 5a.2.2; test inputs were corrected
to be strictly after `e` per the new rule before commit.)

---

## 5a.2.4 — S2: прыжок к ближайшему multi-char Literal (IndexOf)

Sub-point 5a.2.4: in the S2 scan jump to the nearest multi-char `Literal` occurrence (via
`input.IndexOf`) instead of step-by-step `FirstMatchesAt` at every position. Precomputed set of
multi-char `Literal` values from the First-sets of all `anchors` and `canStart` rules; empty set →
jump skipped entirely (no behavior change). `Speculative`/`AddResyncCandidate`/`FirstMatchesAt`/the
trivia jump/counter increment/`foreach` blocks are unchanged.

Status: **DONE** (one deviation — see below: the task-specified test input cannot fail before the
change because the anchor's First-set in the `TierBudgetTests` grammar contains no multi-char
`Literal`; a second test with a minimal grammar deviation carries the before/after regression)

### What was changed

`ExtensibleParser/Recovery/RecoveryEngine.cs` — **modified**, inside `GenerateS2`:

- **Before the loop** (`RecoveryEngine.cs:281-292`): precompute `jumpLiterals` — a
  `HashSet<string>(StringComparer.Ordinal)` of multi-char `Literal` values (`Literal { Value.Length: > 1 }`)
  from `FirstSets.Get(refRule, calculator)` of all `anchors` and all `canStart` rules.
- **In the loop**, after the trivia jump and before `foreach (var anchor in anchors)`
  (`RecoveryEngine.cs:308-329`): if the set is non-empty, compute
  `next = min(input.IndexOf(lit, s, StringComparison.Ordinal) for lit where >= 0)`; if no `lit` found
  (`next < 0`) → `break` (no more multi-char `Literal` occurrences in the scan window); if `next > s`
  → `s = next - 1; continue;` (the loop's `s++` lands the scan exactly on `next`); if `next == s`
  → fall through to the existing `FirstMatchesAt` check (the multi-char `Literal` matches at `s`).

No changes to S1/S3/S4/S5/S6, `SpeculativeCache`, `CreateScratchParser`/`ParseRuleOnce`,
`HygieneCore`/`ApplyPatches`/`RollbackPatches`/`PatchMemo`, the signature of `RecoveryEngine.Generate`,
or any `.csproj`.

### The test

`Tests/ParserTests/Recovery/S2IndexOfTests.cs` — **new** (grammar copied from `TierBudgetTests`; S2
anchor `Ref("Stmt")` derived from the `Stmts` loop; the mismatch in Expr puts `e` on the first `#`;
a full `Stmt` follows the garbage, so `Speculative("Stmt")` succeeds there and T1 is found):

1. `S2IndexOf_SpecifiedInput_SameResync` — the task-specified test: inputs `"{ a: 1+ ### x: 2 ; }"`
   (no padding) and `"{ a: 1+ ###    x: 2 ; }"` (3-space padding between `###` and `x` — strictly
   after `e`). Asserts: (a) `S2ScanPositions > 0` for both (the S2 scan is triggered and reaches
   `Speculative` at `x`); (b) the recovery outcome is the same — identical normalized diagnostics
   (Kind, length with the skip-diagnostics delta subtracted, message without absolute positions) and
   the S2 T1 resync positions (`skip to resync point`) shifted by exactly the padding delta (3).
   **Note:** in this grammar `First(Stmt) = {Ident}` (a regex terminal) — no multi-char `Literal` in
   the anchor's First-set, so the precomputed set is empty and the jump is inert: the counter is
   identical for both inputs and identical before/after the change (5/5). This test documents the
   specified scenario and verifies the jump does not change the outcome when inactive.
2. `S2IndexOf_MultiCharKeyword_JumpSkipsPositions` — **added (deviation)**: the same grammar but
   `Stmt := 'let' Ident ':' Expr ';'` (a multi-char keyword at the start, so
   `First(Stmt) = {Literal("let")}` — the jump set is non-empty and the jump is active). Inputs
   `"{ let a: 1+ ### let x: 2 ; }"` (no padding) and `"{ let a: 1+ ###    let x: 2 ; }"` (3-space
   padding between `###` and `let` — strictly after `e`). Asserts: (a) `S2ScanPositions > 0`;
   (c) `S2ScanPositions < (t1 - e + 1)` — the scan window size — i.e. the jump skipped positions
   (with the jump the counter is 2: `e` and the `let` occurrence; without the jump it is 5: every
   window position → **fails before the change**); (b) the recovery outcome is the same — identical
   normalized diagnostics and the S2 T1 resync positions shifted by exactly the padding delta (3).

### Verification (one-shot)

| Command | Result |
|---|---|
| `dotnet build Tests/ParserTests/ParserTests.csproj` | **0 errors** (0 warnings) |
| `dotnet test Tests/ParserTests/ParserTests.csproj` | **Total: 359 · Passed: 357 · Failed: 0 · Skipped: 2** |

- Baseline is **355 passed / 0 failed / 2 skipped** (after 5a.2.3); after = **357 passed / 0 / 2** —
  exactly baseline + the 2 new tests, no regression. The 2 skipped are the same pre-existing
  `[Ignore("WIP")]` in `GrammarValidationTests.cs` — unrelated.
- Counter values (measured, `S2ScanPositions`, no-padding / padded):
  - task-specified inputs (original grammar, jump set empty): **without the jump: 5 / 5**;
    **with the jump: 5 / 5** (the jump is inert — no behavior change).
  - keyword-grammar inputs (jump set = `{"let"}`): **without the jump: 5 / 5** (the scan walks every
    window position `[e..t1]`); **with the jump: 2 / 2** (the scan checks only `e` and the `let`
    occurrence — it jumps over the 3 intermediate positions).
- Pre-change confirmation: the jump was temporarily reverted (`jumpLiterals.Count > 0 && false`),
  the test run showed `S2IndexOf_MultiCharKeyword_JumpSkipsPositions` **failing** with
  `S2ScanPositions=5, window [e..t1] size=5 (e=12, t1=16) — scan did not skip positions` while
  `S2IndexOf_SpecifiedInput_SameResync` still passed (5/5, inert jump); the jump was then re-applied
  and both tests pass.

### Files changed

- `ExtensibleParser/Recovery/RecoveryEngine.cs` — **modified**: precomputed `jumpLiterals` set
  (before the S2 loop) + the min-`IndexOf` jump (after the trivia jump, before the anchor `foreach`)
  in `GenerateS2`.
- `Tests/ParserTests/Recovery/S2IndexOfTests.cs` — **new**: the task-specified test (task inputs,
  asserts (a)/(b)) + the keyword-grammar regression test method that fails without the jump.
- `docs/RecoveryImprovementPlan-progress5a.2.md` — **modified**: this 5a.2.4 section appended.

No S1/S3/S4/S5/S6 / `SpeculativeCache` / `CreateScratchParser`/`ParseRuleOnce` /
`HygieneCore`/`ApplyPatches`/`RollbackPatches`/`PatchMemo` / `Generate`-signature / `.csproj`
changes. Not committed.

### Deviations

1. **The task-specified test cannot fail before the change.** The S2 anchor is `Ref("Stmt")` (derived
   from the `Stmts` loop frame); in the `TierBudgetTests` grammar `First(Stmt) = {Ident}` — a regex
   terminal, so the precomputed multi-char `Literal` set is **empty** and the jump is never executed
   for the specified inputs (verified: counter 5/5 both before and after the change). The specified
   test therefore passes in both states and cannot serve as the before/after regression. To satisfy
   "the test MUST fail before the change", a second test method
   (`S2IndexOf_MultiCharKeyword_JumpSkipsPositions`) was added with a minimal grammar deviation —
   `Stmt := 'let' Ident ':' Expr ';'` (a multi-char keyword at the start, so
   `First(Stmt) = {Literal("let")}` and the jump set is non-empty): it fails before the change
   (counter 5 = window size) and passes after (counter 2 < window size 5). The task-specified test
   itself is kept as-is (asserts (a)/(b) with the exact specified inputs).
 2. **Assert (b) compares normalized diagnostics** (Kind, region length, message with absolute
    positions stripped) plus an explicit check that the S2 T1 resync positions are shifted by exactly
    the padding delta — the raw diagnostics contain absolute positions ("skip to resync point N"), so
    literal equality between the two inputs is impossible by construction. The length of skip
    diagnostics (`[e..resyncPos)`) differs by the padding delta between the two inputs, so the
    normalization subtracts the delta from the length of every `Skipped` diagnostic.

---

## 5a.2.4a — IndexOf jump: только при чисто multi-char Literal First

Sub-point 5a.2.4a: the min-`IndexOf` jump in `GenerateS2` (5a.2.4) has a `break` (no multi-char `Literal`
found) and a jump (one found). For **mixed-First** anchors — a First-set containing BOTH multi-char
`Literal`s AND regex/single-char terminals — both are unsafe: they skip positions where the regex /
single-char terminal would match. The jump is now enabled **only** when every anchor/canStart First-set is
**purely** multi-char `Literal`; otherwise it is disabled entirely (behavior reverts to pre-5a.2.4).

Status: **DONE** (one deviation — see below: the plan's `Alt(...)` has no dedicated rule type in this
parser; the mixed-First is expressed as two alternatives, yielding the identical First-set)

### What was changed

`ExtensibleParser/Recovery/RecoveryEngine.cs` — **modified**, inside `GenerateS2`, at the
`jumpLiterals` precomputation (before the S2 scan loop):

- Added a `jumpSafe` flag (`RecoveryEngine.cs:290`). While collecting `jumpLiterals` from the First-sets of
  all `anchors` and `canStart` rules, any terminal that is **not** a multi-char `Literal`
  (`t is not Literal { Value.Length: > 1 }` — i.e. a regex, a single-char `Literal`, or anything else)
  sets `jumpSafe = false` (`RecoveryEngine.cs:297`, `RecoveryEngine.cs:305`).
- After the loops, `if (!jumpSafe) jumpLiterals.Clear();` (`RecoveryEngine.cs:307-308`) — the jump set is
  emptied so the existing in-loop jump logic (`if (jumpLiterals.Count > 0)`) is skipped entirely, exactly
  as when the set was empty before 5a.2.4. **The jump logic in the loop is unchanged.**

Note: for a `Ref` anchor `FirstSets.Get` already filters `Epsilon` (`.Where(t => t.Kind != "ε")`) and a
First-set never contains `Eof`, so the simple "any non-multi-char-`Literal` terminal ⇒ `jumpSafe = false`"
check is exactly the intended safety condition (no extra `Eof`/`Epsilon` special-casing needed).

No changes to S1/S3/S4/S5/S6, `SpeculativeCache`, `CreateScratchParser`/`ParseRuleOnce`,
`HygieneCore`/`ApplyPatches`/`RollbackPatches`/`PatchMemo`, the signature of `RecoveryEngine.Generate`,
or any `.csproj`.

### The test

`Tests/ParserTests/Recovery/S2IndexOfTests.cs` — **modified**. The `NewParser(bool)` / `Parse(bool, …)`
helpers were generalized to a `StmtKind` enum (`Ident` / `Keyword` / `Mixed`); the two existing tests now
call `Parse(StmtKind.Ident, …)` and `Parse(StmtKind.Keyword, …)` (behavior unchanged). Added a third test:

1. `S2IndexOf_MixedFirst_DisablesJump_SameResync` — the task-specified mixed-First test:
   `Stmt := 'let' Ident ':' Expr ';' | Ident ':' Expr ';'` → `First(Stmt) = {Literal("let"), Ident(regex)}`.
   Inputs `"{ a: 1+ ### let x: 2 ; }"` (`let` after the garbage — T1 via the `Literal` path) and
   `"{ a: 1+ ### foo: 2 ; }"` (`foo`, an `Ident`, after the garbage, no `let` — T1 via the regex path).
   `e` = first `#` (position 8) for both; both T1 matches start at position 12. Asserts:
   (a) `S2ScanPositions > 0` for both (the S2 scan is triggered);
   (b) an S2 T1 `skip to resync point` diagnostic exists for **both** inputs;
   (c) the resync positions (T1 candidate) are **identical** for both inputs (`[12]` == `[12]`).

### Verification (one-shot)

| Command | Result |
|---|---|
| `dotnet build Tests/ParserTests/ParserTests.csproj` | **0 errors** (0 warnings) |
| `dotnet test Tests/ParserTests/ParserTests.csproj` | **Total: 360 · Passed: 358 · Failed: 0 · Skipped: 2** |

- Baseline is **357 passed / 0 failed / 2 skipped** (after 5a.2.4); after = **358 passed / 0 / 2** —
  exactly baseline + the 1 new test, no regression. The 2 skipped are the same pre-existing `[Ignore("WIP")]`
  in `GrammarValidationTests.cs` — unrelated.
- **Pre-change confirmation (guard disabled):** the `jumpLiterals.Clear()` was temporarily bypassed
  (`jumpSafe = true;` forced before the `if`). The run showed `S2IndexOf_MixedFirst_DisablesJump_SameResync`
  **failing** — the `foo` (Ident) input produced **no** S2 T1 resync (only S3 `skip to terminator` +
  S6 `bottom skip` + `Unrecovered`), because the jump stayed active on `Literal("let")` and
  `IndexOf("let", s)` found no occurrence → `break` → the `foo` regex match was lost. The `let` input still
  resynced (T1 found). The guard was then re-applied and the full suite is green (358/0/2).

### Files changed

- `ExtensibleParser/Recovery/RecoveryEngine.cs` — **modified**: `jumpSafe` safety flag in the
  `jumpLiterals` precomputation of `GenerateS2` + `jumpLiterals.Clear()` when `!jumpSafe` (disables the
  jump for mixed-First anchors).
- `Tests/ParserTests/Recovery/S2IndexOfTests.cs` — **modified**: `NewParser`/`Parse` generalized to a
  `StmtKind` enum (existing tests updated, behavior unchanged) + the new `S2IndexOf_MixedFirst_DisablesJump_SameResync`
  test (mixed-First anchor; asserts the T1 resync position is the same for the `let` and the `foo` inputs).
- `docs/RecoveryImprovementPlan-progress5a.2.md` — **modified**: this 5a.2.4a section appended.

No S1/S3/S4/S5/S6 / `SpeculativeCache` / `CreateScratchParser`/`ParseRuleOnce` /
`HygieneCore` / `ApplyPatches` / `RollbackPatches` / `PatchMemo` / `Generate`-signature / `.csproj`
changes. Not committed.

### Deviations

1. **`Alt(...)` has no dedicated rule type in this parser.** Alternatives are expressed as an array of
   `Rule` in `parser.Rules[name]` (the same mechanism used by the `Expr` rules). The plan's
   `Stmt := Alt(Literal("let"), Ident) ':' Expr ';'` is therefore written as two alternatives —
   `Seq([Literal("let"), Ident, ":", Expr, ";"])` **or** `Seq([Ident, ":", Expr, ";"])`. `FollowSetCalculator`
   unions the First-sets of all alternatives, so `First(Stmt) = {Literal("let"), Ident}` — exactly the
   mixed-First the plan specifies, and the recovery behavior (T1 resync position) is identical.
