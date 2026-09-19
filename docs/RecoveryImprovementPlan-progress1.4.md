# 1.4 (unblocked part) — remove `InvalidOperationException` on `Partial@EOF` in the CsNitra consumer

Checklist item 1.4 (A5-4 bottom-contract) has two halves:

1. **Unblocked (done here):** the consumer (`CsNitraParser.Parse<T>`) threw `InvalidOperationException` on a `Partial` (recovered `Partial@EOF`) result. Removed that throw path — the consumer now handles a partial/recovered result gracefully.
2. **Blocked on A3 `RecoveryProfile` (Wave 4.1, deferred):** profile-dependent result semantics — IDE profile returns `Success<T>` with tree covering whole input (absorbers), `Failed(FatalError)` only in Compiler profile, `Unrecovered`/`InsufficientStack` diagnostics on tree. NOT implemented here.

## Throw location

`Parsers/CsNitra/CsNitraGrammar/CsNitraParser.cs:147-148` (in `Parse<T>`, the method span cited by the plan: lines 140-153).

Why this was the only reachable throw: per the final-state semantics (§3.6, `Parser.Recovery.cs:257-263` and `Parser.NoRecovery.cs:39-40`), `ErrorInfo == null` iff the result is `Success@EOF` or `Partial@EOF` (recovery) / `Success` (no-recovery). Any other outcome (Success<EOF, Partial<EOF, Failure) always carries a `FatalError` and returns `Failed` at line 144-145 before the throw. So the throw at line 148 fired exactly on the recovered `Partial@EOF` result — the 1.4 case.

## Change

Before:

```csharp
if (!result.TryGetSuccess(out var node, out _))
    throw new InvalidOperationException("Failed to parse");
```

After:

```csharp
if (!result.TryGetSuccess(out var node, out _) && !result.TryGetPartial(out node, out _))
    throw new InvalidOperationException("Failed to parse");
```

A `Partial` result is now accepted like `Success`: the (whole-input, I4-complete) partial tree is visited by `CsNitraVisitor` and returned as `Success<T>`. Diagnostics are not swallowed:

- the unrecovered path (`Failed(FatalError)`) is untouched;
- on a final `Partial@EOF` the `RecoveryDiagnostics` list is empty by invariant (RecoveryAuthorGuide §3.6 note: any accepted candidate completes the hole to `Success@EOF`; `Partial@EOF` as a final arises only when no candidate was accepted, holes are described by the recovery nodes in the tree itself), and they remain available on the parser instance (`Parser.RecoveryDiagnostics`, reachable via `CsNitraParser.InternalParser`);
- absorbers are filtered from `SeqNode.Elements`/`ListNode.Elements` (the clean visitor view), so the visitor does not throw on them.

The remaining throw now guards only a state the parser contract guarantees impossible (a `Failure` result without `ErrorInfo`) — a defensive invariant check, no longer reachable on `Partial@EOF`. It is kept (rather than replaced with a fabricated `Failed`) because `Failed` is not the only hard-failure signal — `Failed(errorInfo)` already is — and inventing a `FatalError` position for an impossible state would be worse.

## Files changed

- `Parsers/CsNitra/CsNitraGrammar/CsNitraParser.cs` — one line: `Parse<T>` now accepts `Partial` results (visits the recovered tree, returns `Success<T>`) instead of throwing on them.

No other files touched. No parser/recovery-engine changes. No `RecoveryProfile` (A3) implementation.

## Test results

- `dotnet build Nitra.sln` — 0 errors.
- `dotnet test Tests/ParserTests/ParserTests.csproj` — Total: 346, Passed: 344, Failed: 0, Skipped: 2 (the 2 skips are pre-existing `[Ignore("WIP")]` in `GrammarValidationTests`).
- CsNitra-specific tests (no dedicated CsNitra test project exists; they live in `Tests/ParserTests/CsNitra/`): `FullyQualifiedName~CsNitra` — Total: 48, Passed: 46, Failed: 0, Skipped: 2 (same pre-existing WIP ignores).

## Deviations

- The A3-profile half of 1.4 (IDE vs Compiler result semantics, `Unrecovered`/`InsufficientStack` diagnostics on tree) is deferred to Wave 4.1 with `RecoveryProfile`. Until then the consumer is profile-agnostic: recovered `Partial@EOF` → `Success<T>` (recovered tree), unrecovered → `Failed(FatalError)`.
- Checklist `docs/RecoveryImprovementPlan-checklist.md` left as-is (1.4 already `[~]` in the working tree from before this session; it stays `[~]` because the A3 half is deferred).
