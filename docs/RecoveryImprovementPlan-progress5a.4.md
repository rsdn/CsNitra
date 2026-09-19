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

---

## 5a.4.2 — генератор `GenerateS1b` (single-token deletion) + подключение в `Generate`

Sub-point 5a.4.2: add the `GenerateS1b` generator (single-token deletion) to `RecoveryEngine` and wire it
into `Generate` between `GenerateS1` and `GenerateS2`. At a recovery point `E` where the first non-trivia
token after `E` matches an expected terminal, the engine emits a rank-1 candidate that absorbs the
offending token (`[E..E+len)`) and re-parses, emitting an `Extraneous` diagnostic.

Status: **DONE** (one deviation: pre-existing broken `.csproj` artifact reverted — see below)

### What was changed

**Generator** — `ExtensibleParser/Recovery/RecoveryEngine.cs`, new `private static void GenerateS1b(int e,
FailureSnapshot snapshot, string input, Parser parser, List<RecoveryCandidate> candidates)`
(`RecoveryEngine.cs:118`), placed after `GenerateS1` and before `GenerateS2`. Logic, exactly per spec:

1. `var triviaLen = parser.Trivia.TryMatch(input, e + 1); var pos = e + 1 + triviaLen;` — if
   `pos >= input.Length` → return.
2. `var len = pos - e;` (absorber length `[e..e+len)`).
3. `var top = snapshot.Stack[^1]; var ruleName = top.RuleName;`
4. For each terminal `t` in `snapshot.Expected`: if `t is not EofTerminal and not EpsilonTerminal &&
   t.TryMatch(input, pos) >= 0` — emit one candidate and **return**:
   - `Id: "S1b:{ruleName}:{t.Kind}"`, `Rank: 1`, `Pos: e`, `Cost: CostCalculator.SkipCost(input, e, e + len)`,
     `RuleName: ruleName`, `TerminalKind: t.Kind`.
   - `Apply: p => p.ApplyInjection(t, e, Injection.Absorb(t.Kind, len))`,
     `Rollback: p => p.RollbackInjection(t, e, hadOld ? old : null)`.
   - `Diagnostics: [new RecoveryDiagnostic(e, e + len, RecoveryKind.Extraneous, $"extraneous token, expected {t.Kind}", t, ruleName)]`.
5. If no terminal matches — return (no candidate).

**Connection** — in `Generate` (`RecoveryEngine.cs:33`), between `GenerateS1` (`:32`) and `GenerateS2`
(`:34`): `GenerateS1b(e, snapshot, input, parser, candidates);`. Emission order: `GenerateS1` →
`GenerateS1b` → `GenerateS2`. At equal sort keys S1 wins the tiebreak by stability (stable sort preserves
emission order).

### The test

`Tests/ParserTests/Recovery/SingleTokenDeletionTests.cs` — **new**. Grammar `S := 'a' 'b'` (two
single-char `Literal`s in a `Seq`), `[TerminalMatcher]` class `SingleTokenDeletionTerminals` with a
`Trivia` terminal (`[Regex(@"\s*")]`), input `"aab"`, recovery enabled (default). Trace: `'a'`@0→1,
`'b'` промах@1 → mismatch в E=1, snapshot `{Expected: {b}, FailedTerminal: b}`. `GenerateS1b`:
`triviaLen = 0`, `pos = 2`, `len = 1`; `Literal("b").TryMatch("aab", 2) = 1 >= 0`, `'b'` ∈ `Expected` →
абсорбер `[1..2)`. Re-parse: `'a'`@0→1, absorber skip@1→2, `'b'`@2→3 = EOF.

- **Main assert (fails without the fix):** `parser.RecoveryDiagnostics` contains a diagnostic with
  `Kind == RecoveryKind.Extraneous`.
- Accompanying asserts: `parser.ErrorInfo == null`; `result.TryGetSuccess(out _, out var end) && end == 3`.

### Verification (one-shot)

| Command | Result |
|---|---|
| `dotnet build Tests/ParserTests/ParserTests.csproj` | **0 errors** |
| `dotnet test Tests/ParserTests/ParserTests.csproj` | **Total: 361 · Passed: 359 · Failed: 0 · Skipped: 2** |

- Baseline is **358 passed / 0 failed / 2 skipped**; after = **359 passed / 0 / 2** — exactly `358 + 1
  (new test)` / 0 / 2. The 2 skipped are the same pre-existing `[Ignore("WIP")]` in
  `GrammarValidationTests.cs` — unrelated.
- **Fails without the fix:** with the `GenerateS1b` call temporarily removed from `Generate`, the test
  fails: `Assert.IsTrue failed. Expected an Extraneous diagnostic (single-token deletion), got
  [Skipped [1..3)]`. (The competing diagnostic without the fix is S3's `Skipped`, not S1's `Inserted` —
  S1's zero-width insertion only reaches `end=1` and makes no progress, so it is rejected; S3's
  skip-to-EOF is what is accepted. The main assert fails either way: no `Extraneous`.) The `GenerateS1b`
  call was then re-applied and the test passes.

### Files changed

- `ExtensibleParser/Recovery/RecoveryEngine.cs` — **modified**: added `GenerateS1b` (`:118`) and its
  connection in `Generate` (`:33`).
- `Tests/ParserTests/Recovery/SingleTokenDeletionTests.cs` — **new**: the `SingleTokenDeletionTests` test.

### Deviation

A **pre-existing uncommitted** modification to `Tests/ParserTests/ParserTests.csproj` (line
`<Compile Include="Recovery\SingleTokenDeletionTests.cs" />`) was present in the working tree before this
sub-point started. It is not part of the committed baseline and duplicates the SDK's default glob, so once
the test file exists it triggers `NETSDK1022` (duplicate `Compile` items) and breaks the build. The
solution itself requires **no** `.csproj` change (the SDK auto-includes the new test file). To satisfy the
"0 errors" verification the csproj was restored to its committed baseline (`git checkout --
Tests/ParserTests/ParserTests.csproj`), i.e. that one stray line was removed. No other `.csproj` was
touched and no package/reference/TFM was changed.
