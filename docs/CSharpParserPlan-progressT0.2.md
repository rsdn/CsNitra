# T0.2 Progress — Tests/CSharpGrammarTests

## Plan
- [x] Step 1: Create `Tests/CSharpGrammarTests/CSharpGrammarTests.csproj` + `MSTestSettings.cs`
- [x] Step 2: Create `SmokeTestTerminals.cs` (Trivia/Identifier/Number)
- [x] Step 3: Create embedded-resource helper (`Cs1.grammar` loader)
- [x] Step 4: Create `CSharpParserTests.cs` (positive smoke + negative ctor test)
- [x] Step 5: Add project to `Nitra.sln`, nest under `Tests` folder
- [x] Step 6: `dotnet test Tests/CSharpGrammarTests` — all green
- [x] Step 7: `dotnet build Nitra.sln` — no new warnings

## Log

- Step 1 done: csproj (MSTest.Sdk/3.6.4, net8.0, refs: CSharpGrammar, ExtensibleParser, Regex+TerminalGenerator as analyzers) + MSTestSettings.cs created.
- Step 2 done: `SmokeTestTerminals` (static partial, `[TerminalMatcher]`) with Trivia (whitespace/comments), Identifier (`[_\l]\w*`), Number (`\d+`).
- Step 3 done: `EmbeddedGrammar.LoadCs1Grammar()` reads `Cs1.grammar` from `typeof(CSharpParser).Assembly` by suffix match.
- Step 4 done: `CSharpParserTests` — 3 positive/parse tests + 1 negative ctor test.
- Step 5 done: `dotnet sln Nitra.sln add` — GUID {80C1380D-60CF-4855-9774-4C709BB669DD}, auto-nested under Tests folder.
- Running `dotnet test Tests/CSharpGrammarTests`...
- First run: 2 failed, 2 passed. Fixes applied:
  - `triviaLength` is the leading-trivia length consumed at `startPos` (0 when input starts with a token), not total trivia → assert `0`.
  - Malformed input `var x = ;` is **recovered** by the recovery engine (reaches EOF, `ErrorInfo == null`) rather than failing hard → assert `RecoveryDiagnostics.Count > 0` instead of a hard failure.
- Step 6 done: `dotnet test Tests/CSharpGrammarTests` → **4 passed, 0 failed**.
- Step 7 done: `dotnet build Nitra.sln` → **Build succeeded, 0 warnings, 0 errors**.
