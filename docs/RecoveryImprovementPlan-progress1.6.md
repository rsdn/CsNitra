# Recovery Improvement — Progress 1.6 (C2): Partial-after-recovery semantics

**Scope:** verify that S6 (the guaranteed bottom) absorbers give `Success@EOF` (not `Partial`), and
clarify the `Partial@EOF` semantics. A4-2/A5-4 contract: after recovery the result is `Success@EOF`
(the whole-input tree + diagnostics).

## Conclusion (TL;DR)

- **S6 recovery gives `Success@EOF`, never `Partial`.** No fix was needed — the current behavior is
  already correct.
- **`Partial@EOF` is a distinct, intentional state, NOT an S6 outcome.** It arises when the partial
  parse (via the TDOPP postfix path) already reached EOF (`parseEnd == EOF`), so there is no trailing
  region and S6 is **not** generated. No candidate is accepted; the raw `Partial` stands.

## `Partial` semantics (when reachable, what it means)

`Result.Kind.Partial` is produced **during parsing** (not by the recovery engine) and represents
"parsed a prefix of the rule but could not complete it". It is produced in:

- `ParseSeq` base case (`Parser.cs` ~818): an element fails **with progress** (`elemIdx > 0 && newPos > startPos`)
  → `Result.Partial(BuildPartialSeqNode(...), newPos, maxFailPos, ctx)`.
- `ParseOneOrMany` / `ParseZeroOrMany` (`Parser.cs` ~622/672): if any iteration was Partial → `Result.Partial`.
- `ParseSeparatedList` (`Parser.cs` ~942/975/1009/1018): if any element/separator was Partial → `Result.Partial`.
- `ParseRule` (TDOPP) postfix path (`Parser.cs` `ContinueFromPartialPostfix` ~418/421): a Partial prefix is
  propagated up through the postfix chain → `Result.Partial`.

A `Partial` result carries a tree covering `[startPos..NewPos)` (a real, non-empty prefix) plus a
`ParseContext` describing where/why it stopped. It is a **valid prefix fact** (Hygiene never removes
Partial/Success, only stale Failure).

### `Partial@EOF` specifically

`Partial@EOF` (a `Partial` whose `NewPos == input.Length`) is a **final** state that is **reachable** and
**intentional**:

- **When it occurs:** the partial parse reached EOF through the postfix path, and the recovery engine
  accepted **no** candidate. Concretely (see `FinalStateTests.Test_PartialAtEof_RecoveredWithHoles` and
  `PartialAfterRecoveryTests.Test_PartialAtEof_Is_Distinct_State_Not_S6`): grammar
  `Expr = "a" | "a+" | "b" | Seq(Ref(Expr), "+", ReqRef(Expr,100), Inner Seq("b","c"))`, input `a+bb` →
  the postfix `Inner Seq("b","c")` is missing `c` at EOF → base-case `Partial` in `ParseSeq` → propagated
  up the postfix path → `Partial@4 = Partial@EOF`.
- **Why no candidate is accepted:** S6 is generated **only when `parseEnd < EOF`** (`RecoveryEngine.Generate`,
  `RecoveryEngine.cs:45`). Here `parseEnd == EOF`, so there is no trailing region to absorb and S6 is not
  generated. (S1–S4 need a snapshot; in this construction the snapshot at `e=EOF` is not captured because a
  later prefix failure overwrites `_lastSnapshot.Pos`, so `FailureSnapshotAt(EOF) == null`.) Hence zero
  candidates → the raw `Partial@EOF` stands.
- **What it means:** "reached EOF; the tree covers the whole input, but the tree is **incomplete (holes)**
  and recovery applied nothing." It is a **recovered state** (`FinalizeResult`, `Parser.Recovery.cs:259-263`:
  `recovered = (Success && end==EOF) || (Partial && end==EOF)` → `ErrorInfo = null`) with **empty**
  `RecoveryDiagnostics`. The holes are described by the tree itself (Partial/recovery nodes, I4), not by
  diagnostics. This is distinct from `Partial<EOF` (unrecovered → `ErrorInfo = FatalError`).

## Does S6 give `Success@EOF` or `Partial@EOF`?

**`Success@EOF`.** Two independent reasons, both verified in code and by tests:

1. **S6's patch is always `Result.Success`.** `GenerateS6` (`RecoveryEngine.cs:725`) builds
   `var value = Result.Success(node, s, 0);` — a `Success` node (real prefix + absorber), never a `Partial`.
   The re-parse reads this memo (`ParseRule`, `Parser.cs:232-243`) and returns `Success@s`.
2. **S6 is generated only when `parseEnd < EOF`** (`RecoveryEngine.cs:45`), i.e. only when there is a
   trailing region to absorb. The absorber covers `[start..s)` up to the next stop position / EOF, so the
   re-parse reaches EOF → `Success@EOF`.

Therefore, whenever S6 is the **accepted** candidate, the final result is `Success@EOF` (the whole-input
tree + diagnostics) — exactly the A4-2/A5-4 contract. `Partial@EOF` can only occur when S6 is **not**
generated (`parseEnd == EOF`), which is the distinct state documented above.

## Fix

**None required.** The current behavior already satisfies the contract (S6 → `Success@EOF`, never
`Partial`). Per the task constraint ("If the current behavior is already correct … do NOT force a change"),
no parser/recovery code was modified. Only tests + documentation were added.

## Tests added

`Tests/ParserTests/Recovery/PartialAfterRecoveryTests.cs` (new, 2 tests):

- `Test_S6_Recovery_Gives_Success_Not_Partial` — fixed-sequence trailing garbage
  (`Module := Expr Expr`, `Expr := Number`, input `12 34 56 78`). `snapshot == null` → S5 returns early →
  S6 is the **only** candidate. Asserts `ResultKind == Success` (explicitly **not** `Partial`),
  `end == input.Length` (Success@EOF), `ErrorInfo == null`, and an S6 `Skipped` ("bottom skip") diagnostic.
- `Test_PartialAtEof_Is_Distinct_State_Not_S6` — TDOPP postfix path (`a+bb` → `Partial@EOF`). Asserts
  `ResultKind == Partial` (explicitly **not** `Success`), `end == input.Length`, `ErrorInfo == null`
  (recovered state), and empty `RecoveryDiagnostics` (no accepted candidate, S6 not generated).
  Documents the explicit meaning of `Partial@EOF`.

Verified (already present, unchanged): `S6BottomTests` (4/4) and `FinalStateTests` (4/4, incl.
`Test_PartialAtEof_RecoveredWithHoles`) — all assert S6 → `Success@EOF` and characterize `Partial@EOF`.

## Files changed

- `Tests/ParserTests/Recovery/PartialAfterRecoveryTests.cs` — **new**: 2 tests pinning the 1.6 contract
  (S6 → `Success@EOF` not `Partial`; `Partial@EOF` is a distinct non-S6 state) with documentation.
- `docs/RecoveryImprovementPlan-progress1.6.md` — **new**: this progress note.

No changes to `ExtensibleParser/` (parser/recovery), the grammar, or consumers.

## Test results

- `dotnet build Tests/ParserTests/ParserTests.csproj` — **0 errors**, 0 warnings.
- `dotnet test Tests/ParserTests/ParserTests.csproj` — **346 passed / 0 failed / 2 skipped** (Total 348).
  The 2 skipped are the pre-existing `[Ignore("WIP")]` tests. (Baseline before this change: 344 passed;
  +2 new tests.)

## Deviations

- The task's phrasing "S6 recovery (trailing garbage)" can be read as implying S6 is always the accepted
  candidate for trailing garbage. In practice, for **loop-based** trailing garbage with a captured
  mismatch snapshot, **S5** (trailing absorber, rank 5) is tried before S6 and is accepted. S6 is the
  accepted bottom specifically when there is **no** trailing-region snapshot (e.g. a fixed sequence that is
  already complete → `snapshot == null`, so S5 returns early) or in a **strict** region (C1: only S6).
  Both paths yield `Success@EOF` (S5's and S6's patches are both `Result.Success`), so the contract holds
  either way. The new S6 test uses the fixed-sequence construction to guarantee S6 is the accepted candidate.
