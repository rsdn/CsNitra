# T3.11.4 — `params Span<T>` (C# 11.0)

Status: DONE (build green, all tests green). Checklist item left as `[~]` for the orchestrator to
mark `[✅]` after verifying and committing.

## Task
Extend the grammar with `params Span<T>` / `params ReadOnlySpan<T>` (C# 11.0). The `params`
modifier must accept `Span<T>` and `ReadOnlySpan<T>` (in addition to array types):

    void M(params Span<int> values) { }
    void N(params ReadOnlySpan<int> values) { }
    void O(params int[] values) { }   // still works

Version purity: `CreateParser(10)` (no CS11) must REJECT `params Span<T>`; `CreateParser(11)` must
accept it.

## Roslyn syntax (C:\RSDN\roslyn, src/Compilers/CSharp/Portable)
- Parser: `LanguageParser.cs:4938` `ParseParameter` — modifiers via `ParseParameterModifiers`
  (4948), then `ParseType(mode: ParseTypeMode.Parameter)` (4958) — ANY type, not just arrays.
  `IsParameterModifierExcludingScoped` (4999-5013) returns true for `ParamsKeyword` (5007).
  => The PARSER accepts `params Span<int>` in ALL versions; there is no parser-level change.
- Binder (the actual C# 11 change): `SourceComplexParameterSymbol.cs:1720-1723` —
  `if (collectionTypeKind != CollectionExpressionTypeKind.Array) {
     MessageID.IDS_FeatureParamsCollections.CheckFeatureAvailability(diagnostics, ParameterSyntax); }`
  and `:1747` `ERR_ParamsMustBeCollection`.
- Feature gate: `MessageID.cs:295` `IDS_FeatureFirstClassSpan` (and `IDS_FeatureParamsCollections`)
  -> C# 11.

## Probe (current behavior BEFORE the change)
Ran a temporary probe (Cs11ParamsSpanProbe.cs, since removed):
- `params Span<int> values` at v10 -> ACCEPT (ok=True, no error).
- `params ReadOnlySpan<int> values` at v10 -> ACCEPT.
- `params int[] values` at v10 -> ACCEPT.
- `params Span<int> values` at v11 -> ACCEPT.
=> `params Span<T>` ALREADY parses at every version, because `params` is a Cs1 `ParameterModifier`
(Cs1.grammar:472) and `Span<int>` is a Cs2 generic `TypeName` (Cs2.grammar:44) -> a `Type`. So the
base `Parameter = Attributes? ParameterModifier* Type TypeName` (Cs1.grammar:467) accepts it. Naively
"adding a rule" cannot give version purity — the base rule must be restructured so `params <generic
type>` is NOT accepted pre-11.

## The rule
`params` is pulled OUT of the generic `ParameterModifier` union and into a dedicated `ParamsParameter`
alternative. The Cs1 `ParamsType` excludes `Span<...>` / `ReadOnlySpan<...>` via two negative
lookaheads (so v<=10 REJECTS `params Span<T>` / `params ReadOnlySpan<T>` but still ACCEPTS
`params int[]`, `params c d`, `params dynamic[]`). Cs11 APPENDS a `ParamsParameter` alternative that
accepts `params Span<T>` / `params ReadOnlySpan<T>` (T0.3 merge). The type-argument list is INLINED
(not the Cs2 `TypeArgumentList`) because Cs11.grammar can be loaded without Cs2.grammar
(RawStringLiteralRuleTests load only Cs1+Cs11).

Cs1.grammar (restructure, no net behavior change for non-Span params):
    Parameter = Attributes? ParameterForm;
    ParameterForm =
        | ParamsParameter
        | PlainParameter;
    PlainParameter = ParameterModifier* Type TypeName;
    ParameterModifier =
        | "ref"
        | "out";                 // "params" removed
    ParamsParameter = "params" ParamsType TypeName;
    ParamsType = !("Span" "<") !("ReadOnlySpan" "<") Type;

Cs11.grammar (append):
    ParamsParameter = "params" SpanOrReadOnlySpan "<" (Type; ",")+ ">" TypeName;
    SpanOrReadOnlySpan = | "Span" | "ReadOnlySpan";

## Code iterations
1. Probed current behavior (temporary `Cs11ParamsSpanProbe.cs`): `params Span<int>` /
   `params ReadOnlySpan<int>` / `params int[]` all ACCEPT at v10 and v11. Confirmed the base
   `Parameter` already accepts `params Span<T>` (params = Cs1 modifier, `Span<int>` = Cs2 generic
   type). So "just add a rule" would NOT give version purity — the base rule had to change.
2. Wrote the rule (Cs1.grammar restructure + Cs11.grammar append) and the test file
   `Cs11ParamsSpanTests.cs`. First build (`dotnet build Nitra.sln --no-incremental`) -> 0 errors on
   the first try (the grammar symbol-resolved: `ParamsParameter`/`ParamsType`/`ParameterForm`/
   `PlainParameter`/`SpanOrReadOnlySpan` all reference only Cs1 rules + inline literals; the type-arg
   list is inlined so no Cs2 reference).
3. Ran the new test class (`--filter FullyQualifiedName~Cs11ParamsSpanTests`) -> 9/9 green on the
   first run. No grammar iteration needed: the negative-lookahead `ParamsType` cleanly rejects
   `Span<`/`ReadOnlySpan<` pre-11, and the Cs11 append-merge `ParamsParameter` accepts them at v11
   (no equal-length tie: at v11 the Cs1 `ParamsParameter` alternative fails its lookahead, so only
   the Cs11 alternative matches; `PlainParameter` can never start with `params` because `params` is
   reserved and no longer a modifier).
4. Edge-case probe (temporary `Cs11ParamsSpanEdgeProbe.cs`, since removed) — all correct:
   - `delegate void D(params Span<int> values);` -> v11 ACCEPT, v10 REJECT (errorPos=16 at `Span`).
   - `class C { void M<T>(params Span<T> values) { } }` -> v11 ACCEPT (generic method).
   - `class C { void M(params List<int> values) { } }` -> v10 ACCEPT (permissive: `List<int>` is not
     `Span`/`ReadOnlySpan`, so the base `ParamsType` accepts it — a binder error in real C#, not
     modeled here).
   - `class C { void M(params Span values) { } }` -> v10 ACCEPT (permissive: bare `Span` = a type name).
   - `ref`/`out`/`in` params (v7) and `this` param (v3) -> ACCEPT (no regression).
5. Encoding: the new test file was written LF/no-BOM; converted to CRLF + UTF-8 BOM (matching
   `Cs11GenericAttributeTests.cs`). Both `.grammar` files stayed CRLF/no-BOM (edits preserved CRLF).
   The progress file is LF like the other `CSharpParserPlan-progress*.md` files.

## Version purity
- `CreateParser(11)`: `params Span<int>`, `params ReadOnlySpan<int>`, `params Span<string>`,
  `params ReadOnlySpan<char>`, `params Span<T>` (generic method), `params int[]`, and
  `params Span<int>` alongside other params -> ACCEPT.
- `CreateParser(10)`: `params Span<int>` and `params ReadOnlySpan<int>` -> REJECT (the Cs1
  `ParamsType` negative lookaheads fail on `Span<`/`ReadOnlySpan<`; `PlainParameter` can't consume a
  leading reserved `params`). `params int[]` -> ACCEPT (no-regression).
- No-regression: `ref`/`out`/`in`/`this`/bare params all unchanged (full suite green).

## Tests (Cs11ParamsSpanTests.cs)
- Positive (v11): `ParamsSpanInt_Succeeds`, `ParamsSpanString_Succeeds`,
  `ParamsReadOnlySpanInt_Succeeds`, `ParamsReadOnlySpanChar_Succeeds`, `ParamsArrayInt_Succeeds`,
  `ParamsSpanInt_WithOtherParams_Succeeds` (6).
- Positive no-regression (v10): `ParamsArrayInt_SucceedsAtV10` (1).
- Negative (v10, version-purity): `ParamsSpanInt_RejectedAtV10`, `ParamsReadOnlySpanInt_RejectedAtV10`
  (2).
- 9 tests, all green (verified via `--filter` on the class).

## Verification
- `dotnet build Nitra.sln --no-incremental` -> 0 errors, 0 warnings.
- `dotnet test Tests/CSharpGrammarTests` (fresh build) -> Passed: 1421, Failed: 0, Skipped: 3,
  Total: 1424 (baseline T3.11.3 was 1412; +9 = the new tests).
- `dotnet test Tests/ParserTests` (regression) -> Passed: 325, Failed: 0, Skipped: 2, Total: 327
  (identical to the T3.11.3 baseline — no regression).

## Working tree hygiene
Staged only the T3.11.4 files: `Cs1.grammar`, `Cs11.grammar`, `Cs11ParamsSpanTests.cs`,
`CSharpParserPlan-progressT3.11.4.md`, `CSharpParserPlan-checklist.md`. Left the unrelated untracked
`docs/antlr4-analysis.md` and `docs/RecoveryImprovementPlan.md` alone (not staged). Did NOT commit.
Did NOT modify any `.csproj`. The checklist keeps T3.11.4 as `[~]` (the `[✅]`/T3.11.4 `[~]`
checklist diff is the pre-existing T3.11.3 progression; left for the orchestrator).

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs1.grammar` (params pulled into `ParamsParameter`/`ParamsType`;
  `params` removed from `ParameterModifier`; `ParameterForm`/`PlainParameter` added)
- `Parsers/CSharp/CSharpGrammar/Cs11.grammar` (+`ParamsParameter` `Span`/`ReadOnlySpan` alternative
  + `SpanOrReadOnlySpan`)
- `Tests/CSharpGrammarTests/Cs11ParamsSpanTests.cs` (new, 9 tests)
- `docs/CSharpParserPlan-progressT3.11.4.md` (this file)
- `docs/CSharpParserPlan-checklist.md` (pre-existing T3.11.3 `[✅]` / T3.11.4 `[~]` progression,
  staged; T3.11.4 left `[~]` for the orchestrator)
