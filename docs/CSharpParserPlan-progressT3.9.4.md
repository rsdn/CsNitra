# T3.9.4 — C# 9.0: top-level statements

Status: COMPLETE (build + tests green; pending orchestrator verification/commit)

## Task
Extend the COMPILATION UNIT syntax with top-level statements (C# 9.0). Top-level statements allow
statements at the top level of a file (outside any class/method), before any type declarations:

```csharp
Console.WriteLine("Hello, World!");
var x = 42;
```

CS9 only: `CreateParser(8)` must REJECT top-level statements; `CreateParser(9)` must accept them.

## Roslyn syntax found (C:\RSDN\roslyn, main, src/Compilers\CSharp\Portable)

File: `Parser/LanguageParser.cs`
- `ParseCompilationUnit` (168) -> `ParseCompilationUnitCore` (180) -> `ParseNamespaceBody` (410)
  with `parentKind = SyntaxKind.CompilationUnit`.
- `ParseNamespaceBodyWorker` (562): at the top level (`isGlobal`, 572) the `default` case (726-729)
  calls `ParseMemberDeclarationOrStatement(parentKind)` — which parses EITHER a member declaration OR
  a global statement. A statement result is wrapped in a `GlobalStatement` node (2636-2664, 2782,
  2928, 2955) and added to the SAME `Members` list as member declarations (760). So in Roslyn a
  top-level statement is a `GlobalStatement` (wrapping one `StatementSyntax`) sitting in the
  compilation unit's `Members`, before any type declaration.
- Disambiguation (584-724): `namespace`/`using`/`extern` are routed to the DIRECTIVE/member path
  UNLESS the token after the keyword forces a statement — `using` with a following `(` (661-664, a
  using statement) or a possible top-level using-local-declaration, `extern` that is not an
  extern-alias directive (633-638). Everything else (an expression, a type name, a block, a statement
  keyword) falls through to `default` and is parsed as a member-or-statement.

File: `Syntax/Syntax.xml.Internal.Generated.cs`
- `GlobalStatementSyntax`: a wrapper node holding a single `StatementSyntax` — a top-level statement
  is a `Statement` wrapped in `GlobalStatement`.

Version gate: `IDS_FeatureTopLevelStatements` -> a BINDER/semantic (C# 9.0) feature; the PARSER
accepts the form wherever the rule exists. Version purity comes from the rule being Cs9-only
(consistent with `record`/`with`/`init`, T3.9.1-3).

## The rule (Cs9.grammar, appended after the T3.9.3 init-accessor section, Cs9.grammar:217-221)
```
// === Compilation unit: APPEND the top-level-statements alternative (T0.3 merge). ===
CompilationUnit = GlobalStatement* NamespaceMember*;

// A top-level statement: a Statement wrapped in GlobalStatement (Roslyn GlobalStatementSyntax).
GlobalStatement = Statement;
```
Design notes:
- `CompilationUnit` is re-declared (T0.3 append-merge) to ADD a second alternative to the Cs1
  `CompilationUnit` (Cs1.grammar:3, `NamespaceMember*`). Result:
  `CompilationUnit = NamespaceMember* | GlobalStatement* NamespaceMember*`.
- The engine's parse succeeds only if the start rule consumes the ENTIRE input (Parser.cs:217
  `end == input.Length`). The two alternatives are mutually exclusive at the start of the file:
  - a file that STARTS with a statement (expression / `var` / type name / block / statement keyword)
    cannot be matched by `NamespaceMember*` (each member starts with a reserved keyword:
    using/extern/namespace, or a type-declaration keyword) -> the first alternative matches 0 members,
    leaves the input unconsumed -> FAILS the full-consumption check -> the second alternative
    (top-level-statements path) wins.
  - a file that STARTS with a member is matched by the first alternative; the second alternative also
    matches (0 statements + the same members) -> equal-length tie -> the FIRST alternative (Cs1,
    declaration order) wins (Parser.cs:296-316). So no existing member-only file changes its parse.
- `GlobalStatement = Statement;` is a thin wrapper (Roslyn `GlobalStatement` holds one statement).
- `using` is a reserved keyword (Cs1 ReservedKeyword), so a top-level `using X;` is a UsingDirective
  (member), never a UsingStatement (which requires `(`) -> no statement/member overlap. `extern`
  alias likewise has no statement form.
- CS9 only: at v8 the second `CompilationUnit` alternative is ABSENT (Cs9-only), so a file starting
  with a statement matches 0 members and REJECTS.
- Boundary: the minimal task covers a single statement, multiple statements, and statements followed
  by a type declaration. A statement AFTER a type declaration is rejected (the greedy `GlobalStatement*`
  stops at the first non-statement, so a trailing statement is left unconsumed — matching Roslyn's
  "top-level statements must come first" rule).

## Code iterations
1. Wrote the two rules in `Cs9.grammar` (re-declared `CompilationUnit` append-merge + new
   `GlobalStatement = Statement;`). `dotnet build Nitra.sln --no-incremental` -> 0 errors. (The
   grammar is parsed at RUNTIME, so the build only confirms the project compiles and the resource is
   embedded; correctness is verified by the tests below.)
2. Wrote `Cs9TopLevelStatementsTests.cs` (8 tests) following the `Cs9RecordsTests.cs` pattern
   (`CSharpVersionTestHelper.CreateParser(9)` positives / `CreateParser(8)` version-purity negatives).
   No csproj change needed (the SDK auto-includes all `.cs`; did NOT add any explicit
   `<Compile Include>`).
3. First run of `Cs9TopLevelStatementsTests` -> 8/8 pass on the first try (no iteration needed — the
   longest-match + full-consumption disambiguation worked as designed).
4. Full `CSharpGrammarTests` suite -> all green (see Verification). `ParserTests` regression -> all
   green. No pre-existing test regressed (the change only ADDS a second `CompilationUnit`
   alternative; a member-only file still parses via the first alternative, and a previously-failing
   statement-file now succeeds — it cannot turn a green test red).

## Version-purity results
- v9 (`CreateParser(9)`) ACCEPTS a single top-level statement, multiple top-level statements, and
  top-level statements followed by a type declaration.
- v8 (`CreateParser(8)`) REJECTS a top-level expression statement and a top-level local variable
  (the second `CompilationUnit` alternative is Cs9-only; a statement-starting file matches 0
  NamespaceMembers -> unconsumed -> rejects).
- v9 regression: a member-only file (e.g. `class C { }`, `using X; class C { }`) still parses via the
  first `CompilationUnit` alternative (equal-length tie -> first wins); no existing compilation-unit
  test regressed.

## Tests (`Tests/CSharpGrammarTests/Cs9TopLevelStatementsTests.cs`, CRLF + UTF-8 BOM, 8 tests)
- Positive (v9): `TopLevelSingleExpressionStatement_Succeeds` (`Console.WriteLine("Hello, World!");`),
  `TopLevelSingleLocalVariable_Succeeds` (`var x = 42;`), `TopLevelMultipleStatements_Succeeds`
  (`var x = 42;\nvar y = x + 1;\n`), `TopLevelStatementsThenType_Succeeds`
  (`Console.WriteLine("hi");\nclass C { }\n`), `TopLevelMultipleStatementsThenType_Succeeds`
  (`var a = 1;\nvar b = 2;\nclass C { }\n`).
- Version-purity negative (v8): `TopLevelExpressionStatement_RejectedAtV8`,
  `TopLevelLocalVariable_RejectedAtV8`.
- Malformed negative (v9): `TopLevelMissingSemicolon_Rejected` (`Console.WriteLine("hi")` — no `;`).

## Verification
- `dotnet build Nitra.sln --no-incremental` -> 0 errors.
- `dotnet test Tests/CSharpGrammarTests` (fresh build) -> **1362 passed, 0 failed, 3 skipped**
  (1365 total; the 3 skips are pre-existing — baseline without my change is 1354/0/3, so my change
  adds exactly 8 passing tests).
- `dotnet test Tests/ParserTests` (regression) -> **325 passed, 0 failed, 2 skipped** (327 total;
  the 2 skips are pre-existing, matching the T3.9.3 baseline).

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs9.grammar` (re-declared `CompilationUnit` + new `GlobalStatement`)
- `Tests/CSharpGrammarTests/Cs9TopLevelStatementsTests.cs` (new, 8 tests)
- `docs/CSharpParserPlan-progressT3.9.4.md` (this file)
- `docs/CSharpParserPlan-checklist.md` (T3.9.4 `[ ]` -> `[~]` in-progress; NOT marked `[✅]` —
  the orchestrator marks it after verifying/committing)
