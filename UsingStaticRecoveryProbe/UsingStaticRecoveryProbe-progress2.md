# UsingStaticRecoveryProbe — Progress Log (sub-point 2)

## Intent

Create exactly one new test file: `Tests/ParserTests/Recovery/UsingStaticRecoveryTests.cs`.

Goal: probe whether the error-recovery branch solves the `using static` problem on a
minimal input. The Cs1..Cs14 C# grammar does not know `using static`; this minimal
grammar must fail at `static` (reserved-word barrier via `NotPredicate`), and recovery
must resync at `namespace N;` so the absorber covers only the `using static Foo.Bar;`
region (~22 of 60 chars, fraction ≈ 37% → verdict `Recovered`). If recovery instead
absorbs everything to EOF, the fraction is 100% → `FalseGreen` → the test fails, which
is the honest answer.

## Plan

1. [ ] Inspect sibling files to mirror style exactly:
       `Tests/ParserTests/Recovery/AnchorResyncTests.cs` (structure),
       `Tests/ParserTests/Recovery/AbsorberPlacementTests.cs` (diagnostic message style).
2. [ ] Verify `ParseHealth` / `ParseHealthReport` / `ParseVerdict` API in `ExtensibleParser.Recovery`.
3. [ ] Write the file per specification:
       - `#nullable enable`, usings `ExtensibleParser`, `ExtensibleParser.Recovery`, `namespace Recovery;`
       - `[TerminalMatcher] public sealed partial class UsingStaticTerminals` with `Ident` + `Trivia` regex terminals.
       - `[TestClass] public sealed class UsingStaticRecoveryTests` with private static `NewParser()`
         installing Module / CompilationUnit / NamespaceMember / QualifiedName / TypeName / ReservedKeyword rules,
         then `BuildTdoppRules()`.
       - One test: `Test_UsingStatic_GarbageNotAbsorbedToEof` with two asserts (EOF success + `Verdict != FalseGreen`).
       - Russian comment block above the test class.
4. [ ] Style: Allman braces, 4-space indent, CRLF, UTF-8 BOM (match siblings).
5. [ ] Acceptance: `dotnet build Tests/ParserTests --no-incremental` → 0 errors.
6. [ ] Acceptance: `dotnet test Tests/ParserTests --no-build --filter "FullyQualifiedName~UsingStaticRecoveryTests"`
       → record outcome verbatim. A FAIL of the main assert (FalseGreen) is legitimate; do NOT weaken the test.
7. [ ] Report: full file content, build result, exact test outcome + full assertion message if failed.

## Constraints

- Do NOT commit. Do NOT touch any file other than the deliverable + this progress file.
- Do NOT set any profile / budget / MaxRecovery* knobs (Parser.Profile defaults to RecoveryProfile.Ide).
- Do NOT edit the .csproj (SDK glob picks up new .cs files).

## Log

- (init) Progress file created. Starting sibling inspection.
- Inspected `AnchorResyncTests.cs` (structure mirrored: `#nullable enable`, usings, `namespace Recovery;`,
  `[TerminalMatcher]` partial class, `NewParser()` + `BuildTdoppRules()`).
- Confirmed diagnostic-message style from `AbsorberPlacementTests.cs` lines 115-117:
  `{result.ResultKind}@{result.NewPos}/{result.MaxFailPos} len={input.Length} ErrorInfo={...} passes={...}`.
- Confirmed `ParseHealth` API in `ExtensibleParser/Recovery/ParseHealth.cs`:
  `Analyze(ISyntaxNode?, int, bool)` → `ParseHealthReport(SuccessAtEof, AbsorberBytes, AbsorberFraction, RecoveryNodes, Verdict)`;
  FalseGreen rule = `absorberBytes * 2 > inputLength` (so 22/60 → Recovered, 60/60 → FalseGreen).
- Confirmed rule records: `Seq(Rule[] Elements, string Kind)`, `ZeroOrMany(Rule, string?)`,
  `NotPredicate(Rule)`, `Literal(string, string?)`, `Ref(string, string?)`;
  `Result.ResultKind/NewPos/MaxFailPos/TryGetSuccess(out ISyntaxNode, out int)` all exist.
- Encoding survey of `Tests/ParserTests/Recovery/*.cs`: mixed (some BOM, some not; some CRLF, some LF-only).
  Followed the mandatory STYLE directive: UTF-8 BOM + CRLF. Verified: BOM=True, CRLF=66, lone LF=0.
- Wrote `Tests/ParserTests/Recovery/UsingStaticRecoveryTests.cs` exactly per spec
  (no profile/budget/MaxRecovery* knobs set; no `[Ignore]`; exactly two asserts).
- Next: `dotnet build Tests/ParserTests --no-incremental`, then the filtered test run.
- BUILD: `dotnet build Tests/ParserTests --no-incremental` → `Build succeeded. 0 Warning(s) 0 Error(s)`.
- TEST: `dotnet test Tests/ParserTests --no-build --filter "FullyQualifiedName~UsingStaticRecoveryTests"`
  → **FAILED** (Failed: 1, Passed: 0). Assert 1 (Success@EOF) PASSED; the main assert (Assert 2) FAILED:
  `Assert.AreNotEqual failed. Expected any value except:<FalseGreen>. Actual:<FalseGreen>. recovery absorbed the
  file: ParseHealthReport { SuccessAtEof = True, AbsorberBytes = 45, AbsorberFraction = 75, RecoveryNodes = 2, Verdict = FalseGreen }`
- Interpretation (honest answer): recovery DID reach EOF, but absorbed 45/60 chars (75%) instead of the
  expected ~22/60 (37%) — it did not resync at `namespace N;`. Verdict = FalseGreen → the branch does NOT
  solve the `using static` problem on this minimal input. Test left unchanged per spec (a FalseGreen failure
  is a legitimate result).
- `git status --porcelain`: only `Tests/ParserTests/Recovery/UsingStaticRecoveryTests.cs` added by me
  (plus this progress dir); no tracked files modified. Nothing committed.
- DONE.
