# T0.1 Progress — CSharpGrammar project + CSharpParser shell

Status: DONE (build green, sanity check passed)

## Plan
1. [x] Create `Parsers/CSharp/CSharpGrammar/` project (csproj + CSharpParser + Cs1.grammar)
2. [x] Add project to `Nitra.sln` (nested under `Parsers\CSharp` solution folder)
3. [x] Build `Nitra.sln`, fix warnings
4. [x] Sanity-check the build path (throwaway check, then delete)

## Verified context
- `CsNitraParser.Parse<T>(string) where T : CsNitra.Ast.CsNitraAst` -> `ExtensibleParser.ParseResult` (`Success<T>(Program)` / `Failed(FatalError)`). File: `Parsers/CsNitra/CsNitraGrammar/CsNitraParser.cs`.
- `ParserExtensions.BuildFromAst(this Parser, GrammarAst, Source, IEnumerable<Terminal>)` in namespace `CsNitra` (runs AstSimplifier + TypeChecker + RuleGenerator; throws on type-check errors). File: `Parsers/CsNitra/CsNitraGrammar/ParserExtensions.cs`.
- `Source` (abstract, `Text`) / `SourceText(text, filePath)` in `CsNitra.TypeChecking`. File: `Parsers/CsNitra/CsNitraGrammar/TypeChecking/Source.cs`.
- `Parser(Terminal trivia, Log? log = null)` in `ExtensibleParser`. `Parser.Parse(string input, string startRule, out int triviaLength, int startPos = 0)` returns `Result` (readonly record struct in `ExtensibleParser`).
- `BuildTdoppRules()` on `Parser` finalizes TDOPP + follow calc; call once at the end.
- `Failed.ErrorInfo` is `FatalError`; `GetErrorText()` is an `ExtensibleParser` extension.
- Terminal pattern: `[TerminalMatcher] partial class` with `[Regex(...)] static partial Terminal Foo();`.
- Shared.projitems provides global usings: System, System.Collections.Generic, System.IO, System.Linq.
- Solution: `Nitra.sln`; solution folder `Parsers` (GUID CAB1ED79-...) with subfolders Json/Cpp/CsNitra/Dot. `dotnet sln add --solution-folder` to nest.
- Directory.Build.props centralizes bin/obj; RECOVERY defined by default.

## Decisions
- Namespace: `CSharpGrammar` (matches project + RootNamespace).
- `CSharpParser` = `sealed` class holding built `Parser` (matches repo convention, e.g. `JsonParser`), exposes `Parser` property + convenience `Parse` method.
- Error handling on grammar parse failure: **throw** `InvalidOperationException` with `GetErrorText()` (fail fast; documented here).
- Merge-friendly for T0.3: building uses the public `BuildFromAst` (accumulates into `parser.Rules`) then a single `BuildTdoppRules()`, so later merging = parse each text -> `BuildFromAst` per grammar -> one `BuildTdoppRules`. No special API needed now.
- `Cs1.grammar`: minimal valid CsNitra grammar (start rule `Grammar`), references `Identifier`/`Number` terminals (to be added in T0.2). EmbeddedResource.

## Public API created
```csharp
namespace CSharpGrammar;

public sealed class CSharpParser
{
    public Parser Parser { get; }                                   // ExtensibleParser.Parser, ready to use
    public CSharpParser(string grammarText, Terminal trivia,
                        IEnumerable<Terminal> terminals, string? sourcePath = null);
    public Result Parse(string input, string startRule, out int triviaLength);
}
```
- `Parser` ctor param `trivia` is the trivia `Terminal`; `terminals` = the non-trivia terminals the grammar references by name.
- On grammar-text parse failure: throws `InvalidOperationException` (message includes `GetErrorText()`).
- `sourcePath` defaults to `"grammar"` when null.

## Log
- (start) Explored metacircular infra + conventions.
- Created `Parsers/CSharp/CSharpGrammar/`: `CSharpGrammar.csproj` (netstandard2.0, RootNamespace=CSharpGrammar, refs ExtensibleParser + CsNitraGrammar + Regex/TerminalGenerator analyzers, EmbeddedResource Cs1.grammar, Shared.projitems import), `CSharpParser.cs`, `Cs1.grammar` (minimal valid grammar: `Grammar`/`Statement`/`Expression`, refs `Identifier`/`Number` terminals). All CRLF.
- Added to `Nitra.sln` via `dotnet sln add --solution-folder "Parsers\CSharp"` → nested CSharpGrammar → CSharp → Parsers. Config platforms + nesting verified.
- **PRE-EXISTING BUILD BREAK (fixed):** HEAD commit `ce473d1` ("MiniC grammar BOM + explicit compile include (leftover from prior session)") added a redundant `<Compile Include="MiniC\MiniCGrammar.cs" />` to `Tests/ParserTests/ParserTests.csproj`, duplicating the SDK default glob → `NETSDK1022` fails the WHOLE solution build, independent of my change (confirmed by building ParserTests in isolation). Removed that one line to unblock the mandated `dotnet build Nitra.sln`. Unrelated to T0.1 scope but required for the build to succeed.
- `dotnet build Nitra.sln` → **Build succeeded, 0 Warning(s), 0 Error(s)**; `CSharpGrammar.dll` produced with no warnings.
- Sanity check (throwaway console app in temp dir, deleted after): `new CSharpParser(grammarText, CsNitraTerminals.Trivia(), [Identifier, Literal], "check.grammar")` then `Parse("let x = y;\nlet a = \"hi\";", "Grammar", out _)` → `SUCCESS: parsed 24/24 chars`. Confirms grammar-parse → BuildFromAst (type-check + rule gen) → BuildTdoppRules → parse, incl. a rule with alternatives.
- Cleanup: throwaway temp project removed; repo git status contains only intended changes.

## Final files
- Created: `Parsers/CSharp/CSharpGrammar/CSharpGrammar.csproj`, `Parsers/CSharp/CSharpGrammar/CSharpParser.cs`, `Parsers/CSharp/CSharpGrammar/Cs1.grammar`, this progress file.
- Modified: `Nitra.sln` (project + folder), `Tests/ParserTests/ParserTests.csproj` (pre-existing fix, 1 line).
