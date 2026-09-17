# T3.9.3 — C# 9.0: `init` accessor (init-only setter)

Status: COMPLETE (build + tests green; pending orchestrator verification/commit)

## Task
Extend the PROPERTY syntax with the `init` accessor (init-only setter, C# 9.0).
CS9 only: `CreateParser(8)` must REJECT the `init` accessor; `CreateParser(9)` must accept it.

```csharp
class Point {
    public int X { get; init; }
    public int Y { get; set; init; }
}
```

## Roslyn syntax found (C:\RSDN\roslyn, main, src/Compilers\CSharp\Portable)

File: `Syntax/SyntaxKindFacts.cs`
- `GetContextualKeyword` (1419-1420): `case "init": return SyntaxKind.InitKeyword;` — `init` is a
  CONTEXTUAL keyword (NOT a reserved keyword), exactly like `record`/`with` (T3.9.1/T3.9.2).
- `GetText` (1874-1875): `case SyntaxKind.InitKeyword: return "init";`.
- `SyntaxKind.cs:412`: `InitKeyword = 8443`; `SyntaxKind.cs:915`: `InitAccessorDeclaration = 9060`.

File: `Parser/LanguageParser.cs`
- `ParseAccessorDeclaration` (4632-4729): the generic accessor parser. It eats an `IdentifierToken`
  (4646), maps it to an accessor kind via `GetAccessorKind` (4648), and then accepts a BLOCK body
  (`{`, 4683-4687), an ARROW body (`=>`, 4683), or a SEMICOLON / auto form (`;`, 4688-4690).
  So `init` is just another accessor name that can be `init;`, `init { }`, or `init => ...`.
- `GetAccessorKind` (4737-4748): `SyntaxKind.InitKeyword => SyntaxKind.InitAccessorDeclaration`
  (4743), alongside `GetKeyword`/`SetKeyword`/`AddKeyword`/`RemoveKeyword`. This is the crux: `init`
  is a FIRST-CLASS accessor name, parsed by the SAME `ParseAccessorDeclaration` as `get`/`set`.
- `ParseAccessorModifiers` (4644): accessors may carry access modifiers (`private init;`), per the
  Roslyn parse test below.

File: `Test/Syntax/Parsing/AccessorDeclarationParsingTests.cs`
- (511-526): an `AccessorList` holding a `GetAccessorDeclaration` (`get;`) followed by an
  `InitAccessorDeclaration` (`private init;`) — i.e. `{ get; private init; }`. Confirms `init` sits in
  the same accessor list as `get`/`set`.

Version gate: `Errors/MessageID.cs:210` `IDS_FeatureInitOnlySetters = MessageBase + 12781`, and
`MessageID.cs:601` `case MessageID.IDS_FeatureInitOnlySetters: // semantic check`. So the init accessor
is a BINDER/semantic (C# 9.0) feature; the PARSER accepts `init` wherever an accessor is parsed.
Version purity comes from the rule being Cs9-only (consistent with `record`, T3.9.1).

## The rule (Cs9.grammar)
```
// (re-declared, T0.3 append-merge — appends to the Cs3 AutoPropertyAccessorList, Cs3.grammar:182-186)
AutoPropertyAccessorList =
    | AutoGetInit    = "get" ";" "init" ";"
    | AutoGetSetInit = "get" ";" "set" ";" "init" ";";
```
Design notes:
- `init` is a CONTEXTUAL keyword (NOT in the Cs1 ReservedKeyword, Cs1.grammar:537-618), so it is matched
  here as a plain literal `"init"`; it remains a valid identifier/type name elsewhere (consistent with
  `record`/`with`).
- The required `init` forms (`get; init;`, `get; set; init;`) are AUTO (semicolon) accessors, so they
  belong to the Cs3 auto-property path. Re-declaring `AutoPropertyAccessorList` (append-merge) adds the
  two `init` shapes to the existing 4 (Cs3.grammar:182-186); the Cs3 `Property` rule
  (Cs3.grammar:176) references it by name, so it picks up the new shapes at v9.
- Longest-match, no tie: for `get; init;` the new `AutoGetInit` (10 chars) beats the Cs3
  `AutoGetterOnly` (4 chars); for `get; set; init;` the new `AutoGetSetInit` (15 chars) beats the Cs3
  `AutoGetSet` (9 chars). Neither collides with the Cs3 shapes (which require `set`, not `init`).
- CS9 only: at v8 the two `init` shapes are absent, so `get; init;` leaves `init;` unconsumed and
  REJECTS (the Cs3 auto list and the Cs1 all-block list both fail).
- Boundary: block-body `init` (`init { }`) and accessor-level access modifiers (`private init`) are
  valid C# 9 but OUT OF SCOPE for the minimal task (the required tests are the auto forms only).

## Code iterations
1. Wrote the rule in `Cs9.grammar` (re-declared `AutoPropertyAccessorList`, append-merge, adding
   `AutoGetInit` + `AutoGetSetInit`). `dotnet build Nitra.sln --no-incremental` → 0 errors.
2. Wrote `Cs9InitAccessorTests.cs` (8 tests). First test run via the Roslyn MCP tool FAILED with
   `NETSDK1022: Duplicate 'Compile' items ... 'Cs9InitAccessorTests.cs'`.
   - Root cause: a PRE-EXISTING working-tree leftover in `CSharpGrammarTests.csproj`
     (`<ItemGroup><Compile Include="Cs9InitAccessorTests.cs" /></ItemGroup>`) added by a previous
     (died) subagent. `git diff` showed it was an uncommitted working-tree change; the committed csproj
     has NO explicit `<Compile Include>` (the SDK auto-includes all `.cs`).
   - Fix: `git restore Tests/CSharpGrammarTests/CSharpGrammarTests.csproj` — reverted the csproj to its
     committed state (removed the broken explicit `<Compile Include>`, restoring SDK auto-include).
     Did NOT add/keep any explicit `<Compile Include>` (per the task's working-tree-hygiene rule).
   - After the revert the csproj is UNMODIFIED in `git status`; my new test file is auto-included.
3. Re-ran `Cs9InitAccessorTests` → 8/8 pass.
4. Baseline purity check: backed up `Cs9.grammar` + moved the test file out, `git restore` the grammar,
   ran the suite → 1346 passed / 0 failed / 3 skipped (baseline). Restored both files. Confirms the 3
   skips are pre-existing and my change adds exactly 8 passing tests (1349 → 1357 total).
5. After the restore dance the Roslyn MCP tool transiently reported 4 flaky failures (stale incremental
   build). A direct `dotnet test` (authoritative) confirmed all 8 pass and the full suite is green.

## Version-purity results
- v9 (`CreateParser(9)`) ACCEPTS `get; init;` and `get; set; init;` (and struct / `private` variants).
- v8 (`CreateParser(8)`) REJECTS `get; init;` and `get; set; init;` (the two `init` shapes are Cs9-only;
  `init;` is left unconsumed → the Cs3 auto list and the Cs1 all-block list both fail).
- v9 regression: plain `get; set;` (no `init`) still parses (the Cs3 auto-property path is intact).

## Tests (`Tests/CSharpGrammarTests/Cs9InitAccessorTests.cs`, CRLF + UTF-8 BOM, 8 tests)
- Positive (v9): `InitAccessor_GetInit_Succeeds` (`{ get; init; }`), `InitAccessor_GetSetInit_Succeeds`
  (`{ get; set; init; }`), `InitAccessor_Struct_Succeeds`, `InitAccessor_Private_Succeeds`,
  `Regression_GetSet_NoInit_Succeeds` (`{ get; set; }`).
- Version-purity negative (v8): `InitAccessor_GetInit_RejectedAtV8`, `InitAccessor_GetSetInit_RejectedAtV8`.
- Malformed negative (v9): `InitAccessor_WithoutGet_Rejected` (`{ init; }` — no `get`, CS0858).

## Verification
- `dotnet build Nitra.sln --no-incremental` → 0 errors.
- `dotnet test Tests/CSharpGrammarTests` → **1354 passed, 0 failed, 3 skipped** (1357 total; the 3 skips
  are pre-existing — baseline without my change is 1346/0/3).
- `dotnet test Tests/ParserTests` (regression) → **325 passed, 0 failed, 2 skipped** (327 total; the 2
  skips are pre-existing — e.g. `Calc/CalcTest.cs:52` throws `NotImplementedException`).

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs9.grammar`
- `Tests/CSharpGrammarTests/Cs9InitAccessorTests.cs` (new)
- `docs/CSharpParserPlan-progressT3.9.3.md` (this file)
