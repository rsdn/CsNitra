# T0.3.1 Progress — multi-file grammar merging

## Approach chosen: text concatenation + single parse + merge semantics in TypeChecker/RuleGenerator

Rationale: parsing once keeps a single `Source` / single coordinate space for all
visitors (TypeChecker, SymbolReferenceResolver, RuleGenerator) with zero re-indexing of
AST positions. A `MultiFileSource` (new `Source` implementation) maps any concatenated
position back to (file path, position) so diagnostics remain usable. AST merge would have
required deep-copying records to rebase every position into a synthetic combined source —
much more code, same result.

Merging semantics are implemented exactly where the plan says:
- `TypeChecking/Scope.cs` — `AddSymbol(RuleSymbol)` now appends to an existing rule instead of overwriting.
- `TypeChecking/Symbols.cs` — `RuleSymbol` holds an ordered list of statements (declaration order across files).
- `RuleGenerator.cs` — emits one combined alternatives array per rule, in declaration order.
- Precedence statements: unchanged (already merge across statements).
- Usings: all usings from all files appear in the single combined `GrammarAst` (concatenation order; nothing in the pipeline consumes usings, so presence is the union).
- Forward references (rule referenced before any declaration): unchanged — all declarations of the whole combined grammar are collected before resolution.

## Steps

- [x] Step 1: Read & analyze existing TypeChecking/RuleGenerator/CSharpParser code.
- [x] Step 2: `MultiFileSource` in `TypeChecking/Source.cs` (concatenation + position→file mapping).
      Files joined with a guaranteed newline separator (a file not ending in `\n`/`\r` gets one appended,
      so a trailing `// comment` in file N cannot swallow file N+1). `Resolve(pos)` → (path, file-local pos);
      `FormatPosition(pos)` → `path:line:column`.
- [x] Step 3: `RuleSymbol` ordered statements + `Scope.AddSymbol` merge.
      `RuleSymbol` ctor changed: `(Identifier, Source, StatementAst)`; holds ordered `Statements` list;
      `AddStatement` appends. `RuleStatement`/`SimpleRuleStatement` kept as "first of kind" properties.
      `Scope.AddSymbol(RuleSymbol)` now appends statements to the existing symbol instead of overwriting.
      `DeclarationCollectorVisitor` updated to the new ctor.
- [x] Step 4: `RuleGenerator` combined-alternatives generation.
      One combined alternatives array per rule, in declaration order (RuleStatement alternatives,
      SimpleRule expression as one alternative). Single-statement rules produce identical output as before.
- [x] Step 5: `ParserExtensions.ParseTexts` / `BuildFromTexts` reusable entry point.
- [x] Step 6: `CSharpParser` multi-text ctor + `Source.FormatPosition` virtual hook (SourceText: `path:line:col`);
      `BuildFromAst` error message now prefixes each error with `source.FormatPosition(...)`.
      (No tests assert the old message format — verified by grep.)
- [x] Step 7: Build + run ParserTests + CSharpGrammarTests.
      `dotnet build Nitra.sln` — 0 warnings, 0 errors.
      `dotnet test Tests/ParserTests --no-build` — 302 passed, 0 failed (2 pre-existing [Ignore]d).
      `dotnet test Tests/CSharpGrammarTests --no-build` — 4 passed, 0 failed.
- [x] Step 8: Manual merge sanity check (throwaway console project, deleted after).
      File 1 `Statement = | A = "a" | B = "b";` + file 2 `Statement = "c";` → all of `a`, `b`, `c` parse.
      Two simple statements same name (`Item = "x";` + `Item = "y";`) → `x`, `y`, `xy` all parse.
      Unknown symbol in file 2 → error `Cs2.grammar:1:13: Symbol 'UnknownThing' not found` (file-accurate).

## Public API added

- `CsNitra.TypeChecking.MultiFileSource` — `Source` over ordered `(Text, Path)` files; `Text` =
  concatenation (newline separator guaranteed), `Resolve(int) → (Path, Position)`,
  `FormatPosition(int) → "path:line:column"`.
- `Source.FormatPosition(int)` — new virtual hook; `SourceText` overrides with `path:line:column`.
- `CsNitra.ParserExtensions.ParseTexts(IReadOnlyList<(string Text, string Path)>) → (GrammarAst, MultiFileSource)`.
- `CsNitra.ParserExtensions.BuildFromTexts(this Parser, IReadOnlyList<(string Text, string Path)>, IEnumerable<Terminal>)`.
- `CSharpGrammar.CSharpParser` — new ctor `(IReadOnlyList<(string Text, string Path)> grammars, Terminal trivia, IEnumerable<Terminal> terminals)`.
  Single-text ctor unchanged.
- `CsNitra.TypeChecking.RuleSymbol` — ctor changed to `(Identifier, Source, StatementAst)`; new
  `Statements` (ordered) + `AddStatement(StatementAst)`; `RuleStatement`/`SimpleRuleStatement` now
  mean "first of kind".

## Decisions / deviations

- No new unit tests left in the repo (T0.3.2 owns tests); sanity check was a throwaway project.
- `BuildFromAst` error text now prefixes each error with `source.FormatPosition(...)`
  (was bare `Diagnostic.ToString()`); no test asserted the old format.
- Usings are not deduplicated — all files' usings appear in the combined AST in concatenation
  order; nothing in the pipeline consumes usings, so this satisfies "union" in the practical sense.
