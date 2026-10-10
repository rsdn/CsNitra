# UsingStaticRecoveryProbe — execution checklist

Branch: `pr/n11_codex/error-recovery-completion`. No commits (user instruction overrides the skill's commit step).

- [✅] 1. `ParseHealth` helper → `ExtensibleParser/Recovery/ParseHealth.cs` (namespace `ExtensibleParser.Recovery`)
- [✅] 2. Unit test → `Tests/ParserTests/Recovery/UsingStaticRecoveryTests.cs` (result: FAILED by design — verdict FalseGreen, AbsorberFraction=75)
- [✅] 3. Probe test → `Tests/CSharpGrammarTests/CSharpCorpusHealthProbeTests.cs` (build 0/0; reported Skipped)
- [✅] 4. Build verification: `dotnet build Tests/ParserTests --no-incremental` + `dotnet build Tests/CSharpGrammarTests --no-incremental`
- [✅] 5. Targeted run: `dotnet test Tests/ParserTests --no-build --filter "FullyQualifiedName~UsingStaticRecoveryTests"` (result: FAILED — verdict FalseGreen, AbsorberBytes=45/60, AbsorberFraction=75)
- [✅] 6. Full run: `dotnet test Tests/ParserTests --no-build` → Failed: 1, Passed: 493, Skipped: 2, Total: 496 (only failure = UsingStaticRecoveryTests, by design; baseline 493+2=495 intact)
- [✅] 7. Optional probe run: done with `CSNITRA_ROSLYN_ROOT=C:\RSDN\roslyn`; 10/10 Recovered (see Trace in final report); `[Ignore]` restored, rebuilt, probe Skipped again
- [✅] 8. Final report (files, test results, `git status --short`, no commit)

## Round 2 (2026-10-11): coverage invariant I4

- [✅] 9. Extend `ExtensibleParser/Recovery/ParseHealth.cs`: `Uncovered` verdict, `CoversInput` flag, span-contiguity check; verdict priority Failed → Uncovered → FalseGreen → Clean → Recovered (first build attempt hit transient file locks by testhost/devenv; retry after testhost died → 0/0)
- [✅] 10. `dotnet build Tests/ParserTests --no-incremental` → 0/0; targeted run `UsingStaticRecoveryTests` → FAILED, verdict=FalseGreen, CoversInput=True, AbsorberFraction=75
- [✅] 11. Probe rerun: done; 10/10 verdict=Uncovered (coversInput=False), summary `processed=10 clean=0 recovered=0 falseGreen=0 uncovered=10 failed=0`; `[Ignore]` restored, rebuilt
- [✅] 12. Full run: `dotnet test Tests/ParserTests --no-build` → Failed: 1, Passed: 493, Skipped: 2, Total: 496 (identical to round-1 baseline; only failure = UsingStaticRecoveryTests, by design)
- [✅] 13. Round-2 report

## Deviations (round 2)

5. Probe Trace/summary extended with `coversInput=` and `uncovered=` counter — required by the round-2 report format; the "не трогать другие файлы" constraint is scoped to the ParseHealth edit itself.
6. `ToString()`: `CoversInput` added as a positional record property → the compiler-generated record `ToString()` includes it automatically; no custom `ToString` override.

## Deviations

1. **Probe location**: plan said `Tests/ParserTests/Recovery/CSharpCorpusHealthProbeTests.cs`; `ParserTests.csproj` has no reference to `Parsers/CSharp/CSharpGrammar` and the guardrail forbids editing `.csproj`. User decision (2026-10-10): probe goes to `Tests/CSharpGrammarTests/` (reference already present there).
2. **ParseHealth location**: plan said test project (`Tests/ParserTests/Recovery/ParseHealth.cs`) and guardrail said "not in ExtensibleParser". User decision (2026-10-10): `ParseHealth` lives in `ExtensibleParser/Recovery/ParseHealth.cs` (production library). This overrides the "не менять production-код" guardrail for this one new file only.
3. **Analyze signature**: plan's `Analyze(ISyntaxNode? root, int inputLength)` is self-contradictory — the report carries `SuccessAtEof` and the verdict depends on it, yet the plan states the helper cannot compute it from the tree. Resolution: `Analyze(ISyntaxNode? root, int inputLength, bool successAtEof)`.
4. **Build verification** extended to `Tests/CSharpGrammarTests` because the probe moved there (deviation 1).
