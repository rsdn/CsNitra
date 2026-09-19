# RecoveryImprovementPlan — 1.3.6 (A4-2 / C1 acceptance) progress

Sub-point 1.3.6: write the **A4-2 / C1 acceptance tests** — `D1.2` (3+ errors, one unsavable → all
reported + EOF + ≥1 `Unrecovered`) and `D1.3` (strict region → `Success@EOF` + `Unrecovered`, no S1–S5).

Status: **DONE** (1 documented deviation — the pre-existing `.csproj` duplicate-`Compile` fix, §5)

These are **acceptance** tests: they pin the two reporting contracts introduced by 1.3.4 (the
`Unrecovered` emission when S6 is the accepted fallback) and 1.5/C1 (strict ⇒ only S6) as **end-to-end
`Parse`** behaviors, not as direct `RecoveryEngine.Generate` calls. `D1.1` (120 errors → `Success@EOF`)
already lives in 1.3.2 (`RecoveryCorpusTests.cs`).

## New file

`Tests/ParserTests/Recovery/A42AcceptanceTests.cs` — 2 tests, terminal class `A42Terminals`
(`Ident` `[_\l]\w*`, `Trivia` `\s*`). `#if RECOVERY`, Allman braces, 4-space, `#nullable enable` — matches
the other recovery test files. No parser / recovery-engine / existing-test changes.

---

## D1.2 — `Test_D1_2_MixedErrors_UnsavableReportedAsUnrecovered`

**Grammar**

```csharp
parser.Rules["Statement"] = [new Seq([new Literal("int"), A42Terminals.Ident(), new Literal(";")], "Statement")];
parser.Rules["Module"]    = [new Seq([new Ref("Statement"), new Ref("Statement"), new Ref("Statement")], "Module")];
```

- `Module := Seq(Statement, Statement, Statement)` — a **fixed** sequence (not a loop). After the last
  statement the `Seq` is complete, so trailing garbage is a clean `Success` below EOF with **no mismatch**
  (`snapshot == null`) — exactly the scenario where `Generate` emits **only S6** (S1–S4 need a snapshot;
  S5 returns early on `snapshot == null`; S6 is emitted because `parseEnd < EOF`).
- `Statement := Seq(int, Ident, ";")` — a **plain (non-nullable) `";"`** so a missing `";"` is a genuine
  terminal mismatch (a `Failure`, not a silently-absorbed `Partial`). The recovery engine repairs it with
  an S1 insertion and emits an `Inserted` diagnostic (a recoverable error).

**Input**

```
int a int b int c ; $
```

3 errors:
| Span | Error | Class | Repaired by |
|---|---|---|---|
| `int a` | missing `";"` (pos 6) | recoverable | S1 → `Inserted` |
| `int b` | missing `";"` (pos 12) | recoverable | S1 → `Inserted` |
| `int c ;` | correct | — | — |
| `$` | trailing garbage, matches no expected terminal (pos 20) | **unsavable** | S6 (fallback) → `Unrecovered` |

**Why the unsavable error is S6-fallback (independent of budget):** after the two S1 insertions the re-parse
is a clean `Success` below EOF with no mismatch at the garbage boundary, so `snapshot == null` and
`Generate` emits only S6. S0 (re-parse as-is) gives no progress; S6 is the *only* candidate and hence the
fallback. Attempts at that point: S0 + S6 = 2 ≤ the **default** budget of 3 — the result does not depend on
`MaxRecoveryAttemptsPerPosition` (the test sets no budget).

**Observed diagnostics** (deterministic):

```
Inserted    [6..6)   rule=Statement  expected ;, found «int b»
Inserted    [12..12) rule=Statement  expected ;, found «int c»
Skipped     [20..21) rule=Module     bottom skip to 21          (S6)
Unrecovered [20..20) rule=Module     error at 20 not recovered (absorbed to EOF)
```

**Assertions**
1. Parse reaches `Success@EOF` (`end == input.Length`), `ErrorInfo == null`.
2. ≥1 `Unrecovered` diagnostic (the unsavable error — S6 was the accepted fallback).
3. ≥2 repair (`Inserted`/`Skipped`) diagnostics (the recoverable errors are also reported).
4. The `Unrecovered` point is strictly **after** the last `Inserted` (recoverable) error and **before** EOF
   (i.e. in the trailing-garbage region, not inside the recoverable errors).

---

## D1.3 — `Test_D1_3_StrictRegion_SuccessAtEof_Unrecovered_NoRepair`

**Grammar**

```csharp
var strictExpr = new RecoveryRule(new Literal("b"), new RecoveryOptions { Recoverable = false });
parser.Rules["StrictExpr"] = [strictExpr];
parser.Rules["Module"]     = [new Seq([new Literal("a"), new Ref("StrictExpr")], "Module")];
```

The strict region is the same construction as `S6BottomTests.Test_S6_Only_Generated_In_Strict_Region`
(a `RecoveryRule` with `RecoveryOptions { Recoverable = false }`), but exercised **end-to-end through
`Parse`** (not a direct `RecoveryEngine.Generate` call).

**Input**

```
a c
```

The strict region expects `"b"` but the input has `"c"` — the error is **inside** the strict region.

**Why only S6 (per C1):** the mismatch at the strict region captures a snapshot whose stack contains a
`Recoverable = false` frame, so `Generate` suppresses S1–S5 and emits only S6 (`parseEnd < EOF`). S6 (the
guaranteed floor) absorbs to EOF → `Success@EOF`, and because the accepted candidate is S6 (rank 6) the
`Unrecovered` diagnostic is emitted at the recovery point (1.3.4 gate `candidate.Rank == 6`).

**Observed diagnostics** (deterministic):

```
Skipped     [0..3)  rule=Module  bottom skip to 3   (S6 — the sole candidate)
Unrecovered [2..2)  rule=Module  error at 2 not recovered (absorbed to EOF)
```

**Assertions**
1. Parse reaches `Success@EOF` (`end == input.Length`), `ErrorInfo == null` (S6 is the only candidate, per C1).
2. ≥1 `Unrecovered` diagnostic (S6 was the accepted fallback).
3. **0 `Inserted` diagnostics** — S1/S4 (the only S1–S5 strategies that emit `Inserted`) are suppressed in
   the strict region (C1).
4. Any `Skipped` diagnostic is the S6 "bottom skip" (the only candidate in the strict region): the count of
   `Skipped` never exceeds the count of `Unrecovered` (1:1 pairing of the S6 bottom with its `Unrecovered`).

---

## Verification (one-shot)

| Command | Result |
|---|---|
| `dotnet build Tests/ParserTests/ParserTests.csproj` | **0 errors** |
| `dotnet test Tests/ParserTests/ParserTests.csproj` | **Total: 346 · Passed: 344 · Failed: 0 · Skipped: 2** |

- The 2 skipped are pre-existing `[Ignore("WIP")]` in `GrammarValidationTests.cs`
  (`ShouldReportErrorForUndefinedRuleReference`, `RequiredSubruleNamesAreNotSpecified`) — unrelated.
- Baseline before this change was **344 total · 342 passed · 2 skipped**; the +2 passed / +2 total is exactly
  the two new `A42AcceptanceTests`.

## Files changed

- `Tests/ParserTests/Recovery/A42AcceptanceTests.cs` — **new**: the two A4-2/C1 acceptance tests (D1.2, D1.3)
  + `A42Terminals`.
- `Tests/ParserTests/ParserTests.csproj` — **reverted to HEAD** (see deviation §5): removed two pre-existing
  `<Compile Include>` lines that duplicated the SDK default glob and broke the build with `NETSDK1022`.

No parser / recovery-engine / existing-test changes. Not committed.

## Deviations

1. **Removed two pre-existing `<Compile Include>` lines from `ParserTests.csproj`** (`Recovery\A42AcceptanceTests.cs`
   and `Recovery\UnrecoveredTests.cs`). These lines were already present (staged) in the working tree before this
   task, but the SDK default glob (`EnableDefaultCompileItems`, on by default) already includes every `.cs` under
   the project, so the explicit includes were **duplicates** and failed the build with
   `NETSDK1022: Duplicate 'Compile' items were included … 'Recovery\A42AcceptanceTests.cs'; 'Recovery\UnrecoveredTests.cs'`.
   Removing them returns the `.csproj` to its committed (HEAD) state; the default glob then compiles both test
   files. This is a build-file fix, not a parser/engine/test change, and was required to satisfy the
   "0 errors" verification. (The task's "add test files only" is honored for source: the only source change is
   the new test file; the `.csproj` edit merely undoes a pre-existing, build-breaking duplicate.)
