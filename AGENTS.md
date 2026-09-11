# CsNitra — Extensible Language Framework

## Quick Start

```
dotnet restore
dotnet build
dotnet test
```

- **SDK**: .NET 8.0 (`global.json` pins 8.0.100 with `latestFeature` rollforward).
- **Build/test from repo root** so `Nitra.sln` is picked up. `Directory.Build.props` centralizes `bin/` and `obj/` at repo root for every project — even a direct single-`.csproj` build. Do not expect per-project `bin/`/`obj/` folders.
- **Single test**: `dotnet test Tests/ParserTests --filter "FullyQualifiedName~JsonParserTests"` (vstest `--filter` works on all three test projects).
- **GraphViz is a test prerequisite**: `RegexTests` shells out to `dot` (`Regex/Regex/Dot.cs`) to render NFA/DFA SVGs. Without GraphViz on PATH, those tests throw.
- **Entry point**: `ExtensibleParser/Parser.cs` — the most important file. Start code analysis here to understand the parser engine.

## Architecture

### Core libraries (shared by everything)
| Path | Target | Role |
|---|---|---|
| `ExtensibleParser/` | netstandard2.0 | PEG/TDOPP parser engine, rule types, parse tree, memoization |
| `Regex/Regex/` | netstandard2.0 | Custom DFA-based regex engine (not System.Text.RegularExpressions) |
| `TerminalGenerator/` | netstandard2.0 | Roslyn source generator — compiles `[Regex]` attributes on terminal classes into DFA matchers at build time |
| `Shared/` | — | Shared items (`Shared.projitems`) imported by multiple projects: `GlobalUsings.cs`, `StringExtensions.cs`, `NetStandard2_0Support.cs` |

### Parser implementations
| Path | What it parses |
|---|---|
| `Parsers/Json/` | JSON |
| `Parsers/Cpp/CppSimplifiedParser/` | Simplified C++ (namespace/enum extraction) |
| `Parsers/Cpp/CppInteropGenerator/` | C++ interop code generator |
| `Parsers/Cpp/CppInteropGeneratorTest/` | Test harness for CppInteropGenerator |
| `Parsers/Dot/DotParser/` | DOT graph format |
| `Parsers/Dot/Workflow/` | Workflow domain model |
| `Parsers/Dot/WorkflowGenerator/` | DOT workflow code generator |
| `Parsers/Dot/WiWorkflow/` | Workflow implementation (console exe) |
| `Parsers/CsNitra/CsNitraGrammar/` | CsNitra grammar parser (meta-circular) |

### Tests
| Path | Framework | Tests |
|---|---|---|
| `Tests/ParserTests/` | MSTest 3.6.4 (`MSTest.Sdk`) | Parser engine + all parser implementations |
| `Tests/RegexTests/` | MSTest 3.6.4 (`MSTest.Sdk`) | Regex/DFA engine (needs GraphViz on PATH for SVG tests) |
| `Tests/WiWorkflowTests/` | MSTest 3.6.4 (`Microsoft.NET.Sdk`) | Workflow integration (currently a single placeholder test) |

- All three test projects set `[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]` — tests run concurrently at method level. Keep tests stateless/thread-safe; avoid shared statics or shared output files (e.g. `RegexTests` writes `regex_*.svg` files — keep such paths unique per test).

## Build-Time Source Generators

Two Roslyn generators run at build time — no separate codegen step.

**`TerminalGenerator`** processes classes marked `[TerminalMatcher]` whose methods carry `[Regex("pattern")]` and generates the DFA-based `TryMatch` implementation. Terminal classes must be declared `partial` (the generator emits the matching partial half).

**Every project that defines terminals must reference both:**
- `Regex/Regex` with `OutputItemType="Analyzer"` — provides the runtime DFA library
- `TerminalGenerator` with `OutputItemType="Analyzer" ReferenceOutputAssembly="false"` — the source generator

If terminals fail to compile with missing `TryMatch`, verify these analyzer references are present.

**`WorkflowGenerator`** is a second generator: it reads a DOT file passed via `AdditionalFiles` (e.g. `Parsers/Dot/WiWorkflow/wf.dot`) and generates C# workflow code for the `WiWorkflow` console app.

## Shared Projects

Several `.shproj` / `.projitems` pairs exist (e.g. `Shared/`, `ExtensibleParser.ExtensibleParser.Shared/`). These import shared `.cs` files into multiple consuming projects via `<Import Project="...\*.projitems" Label="Shared" />`. Do not delete `.projitems` files — they are the binding between shared code and consumers.

## In Progress: Error Recovery

Error recovery is mid-implementation (tracked in git: `ExtensibleParser/Recovery/` is new). Before touching recovery-related code:

- `docs/RecoverySystemPlan.md` — architecture and phased plan (written in Russian)
- `docs/RecoverySystemChecklist.md` — phase-by-phase status (e.g. 0.1–0.2 done, 0.3+ pending)
- Tests live in `Tests/ParserTests/Recovery/`

## CI

- **Workflow**: `.github/workflows/dotnet.yml`
- Runs on ubuntu-latest, windows-latest, macOS-latest with .NET 8.0.x
- Steps: `dotnet restore` → `dotnet test --configuration Release --no-restore`
- Installs GraphViz via `.github/actions/install-graphviz` — required by `RegexTests`, which shells out to `dot`

## Conventions

- **Core libs**: `netstandard2.0`, `ImplicitUsings=disable`, `Nullable=enable`, `LangVersion=preview`
- **Test projects**: `net8.0`, `ImplicitUsings=enable`, `Nullable=enable`
- **Editor config**: 4-space indent, CRLF line endings, CA1050 suppressed, collection expression style = never
- **Grammars**: Defined in C# using `Rule` types (`Seq`, `Alt`, `Ref`, `Literal`, `SeparatedList`, etc.) — not DSL files. See `ExtensibleParser/Rules.cs` for the rule type catalog.
- **Dynamic grammar extension**: The parser can modify its rule set at runtime during parsing. This allows grammars to extend themselves on the fly — for example, loading syntax extensions via directives like `using` to open namespaces, or introducing new operators and keywords mid-parse.
- **Longest-match wins**: Unlike classic PEG, the first alternative does not win. The parser tries all alternatives and selects the one that consumes the longest substring. In the rare case of equal-length matches, the parser reports an error — resolve with lookahead predicates (`&`, `!`).
- **Separated loops**: `SeparatedList` replaces manual recursion for `Element, Separator, Element, ...` patterns. It supports `CanBeEmpty` and configurable `SeparatorEndBehavior` (`Optional`, `Required`, `Forbidden`) to control trailing separators. `OneOrMany` and `ZeroOrMany` cover unadorned repetition.
- **Parse Tree**: Not a traditional AST — it's a more detailed, highly abstract tree automatically constructed from the grammar structure. Every grammar rule produces a corresponding node, preserving the full parse structure including separators, trivia, and intermediate constructs.
- **TDOPP**: Expression parsing uses precedence-based left-recursive rules via `ReqRef`. Call `parser.BuildTdoppRules()` before parsing if grammar uses TDOPP rules.

## Code Formatting

- **Braces**: Always on a new line (Allman style).
- **Single-line statements**: No braces. Always on a new line after the condition. Braces only when multiple statements.
  ```csharp
  if (x)
      Foo();
  else
  {
      Baz();
      Bar();
  }
  ```
- **Namespaces**: File-scoped, braceless (`namespace Foo;`).
- **Records**: Use `record` for all data-carrying types.
- **Operators at line break**: Place operator at end of line. Wrap only when exceeding 120 characters.
  ```csharp
  var result = someVeryLongExpression
      + anotherPart
      + yetAnother;
  ```
- **Expression-bodied members**: Prefer `=>` for single-expression methods/properties.
- **Ternary over if/else**: Prefer `condition ? a : b` for simple branching.
- **Pattern matching**: Use `or` patterns instead of chained equality.
  ```csharp
  // good
  if (c is '\r' or '\n') ...
  // avoid
  if (c == '\r' || c == '\n') ...
  ```
- **`is not` pattern**: Prefer `x is not SomeType` over `!(x is SomeType)`.
- **Inheritance**: Every type must be explicitly `abstract` or `sealed`. No implicitly inheritable types.
- **Naming**:
  - Types, methods, type-level constants — `PascalCase`
  - Fields — `_camelCase` (leading underscore)
  - Local variables, parameters, local functions — `camelCase`
- **Primary constructors**: Preferred over field-then-constructor pattern.
  ```csharp
  public class JsonVisitor(string input) : ISyntaxVisitor { }
  ```
- **Switch expressions**: Preferred over switch statements for value mapping.
- **Target-typed `new`**: Omit type when inferable.
  ```csharp
  var list = new List<DotAst>(); // not List<DotAst> list = new List<DotAst>();
  ```
- **Discards**: Use `_` for unused parameters and out variables.
- **Local functions**: Prefer local functions over lambdas for encapsulated logic.
- **Raw string literals**: Use `"""..."""` for regex patterns and multi-line strings.
- **`global using`**: For directives that apply across an entire namespace (see `DotParser.cs`).
