# T1.3.2 — Select Roslyn test cases for Cs1 parser

## Status
Done — 138 Roslyn-derived tests written, all suites green (CSharpGrammarTests 223/223, ParserTests 324/326, 2 pre-existing skips, WiWorkflowTests 1/1, RegexTests 9/9). One in-scope grammar defect found and fixed (empty namespace body); one engine-level defect found and reported (empty compilation unit).

## Our grammar scope (from `Parsers/CSharp/CSharpGrammar/Cs1.grammar`)
- CompilationUnit = interleaved NamespaceMembers (using open/alias, extern alias, namespace, type decl)
- Class/struct/interface/enum/delegate headers with EMPTY bodies only
- Modifiers per type (no partial/static/new-on-types; no duplicate-modifier rejection)
- Base lists (`: A, B`, no trailing comma), enum base (underlying type, `Type = Identifier` matches keywords)
- Enum members (attrs + constant values, trailing comma legal)
- Delegate params: attrs, ref/out/params, required `;`
- Attribute lists (trailing comma legal), argument lists (trailing comma ILLEGAL), named args (constant values only)
- Constants: true/false, string/verbatim string, char, decimal/hex int w/ suffix, unary +/- (no real literals)
- TypeName = `!ReservedKeyword Identifier` (keyword/identifier boundary)
- Trailing `;` after namespace/type bodies legal

## Roslyn repo map (scanned)

### `src\Compilers\CSharp\Test\Syntax\Parsing\` (PRIMARY — 74 files)
| File (KB) | Scanned? | Result |
|---|---|---|
| DeclarationParsingTests.cs (838) | yes, focused ranges 29-558, 560-1793, 1993-2472, 5677-5751, 8560-8635, 9413-9780, 12938-12960 | MAIN SOURCE: extern/using, global attrs, namespaces, class/struct/interface/delegate headers, trailing-`;` theory tests |
| ParsingErrorRecoveryTests.cs (424) | yes, focused ranges 507-1111, 2436-2575, 2662-2860 | MAIN SOURCE: namespace/base-list/delegate/enum/attribute garbage + EOF recovery negatives |
| NameParsingTests.cs (72) | yes, 30-130, 453-752 | keyword-as-name negatives (TestMissingNameDueToKeyword), known-type names |
| UsingDirectiveParsingTests.cs (210) | method map + 160-235, 2377-2526 | `using int;` negative, `using V = void;` negative; rest is C# 6+ (using static) / pointers / tuples / dynamic — excluded |
| ParserRegressionTests.cs (41) | yes, 97-356 | mostly statements/garbage — no in-scope candidates |
| DeclarationParsingTests_MissingIdentifiers.cs (312) | yes, 1757-1956 | enum missing-identifier negatives (Enum01/02); rest has members/records — excluded |
| AccessorDeclarationParsingTests.cs (121) | spot-check 695-740 | `class get { }` contextual-keyword type name positive |
| FileModifierParsingTests.cs (136), ClosedModifierParsingTests.cs (144) | spot-check | `file`/`closed`/`safe` modifiers — C# 14/15, excluded |
| LexicalAndXml\LexicalTests.cs (187) | method map + 876-975, 3741-3820 | literal/keyword lexing; used indirectly (literal shapes via enum/attribute args); no direct file-parse candidates |
| Generated\Syntax.Test.xml.Generated.cs (828) | method map | factory tests only — no parse inputs |
| RoundTrippingTests.cs (38), ParsingTests.cs (19), other Parsing files | method maps | no in-scope candidates (statements, lambdas, records, collections, patterns…) |

### `src\Compilers\CSharp\Test\` other folders (mapped, spot-checked)
- `Symbol\Symbols\Source\EnumTests.cs` — READ 36-330 — MAIN SOURCE for enum positive/negative shapes (ExplicateInit, MixedInit, OutOfUnderlyingRange, NullEnumBody, EnumEOFBeforeMembers, NoEnumBody_01/02, CS1001ERR_IdentifierExpected_NoIDForEnum, FlagOnEnum, AttributeOnEnum, CS0109WRN_ModifiersForEnum, CS1041ERR_ModifiersForEnumMember)
- `Symbol\SymbolDisplay\SymbolDisplayTests.cs` — grep: `using Goo = N1.N2.N3;` (:2938), `using NAB = N.A.B;` (:5670)
- `Symbol\Symbols\Retargeting\RetargetingTests.cs` — grep: `public enum AttributeTargets { Method = 0x0040, }` (:1028)
- `Semantic\Semantics\UnsafeTests.cs` — grep: `unsafe struct Inner { }` (:2657)
- `Semantic\Semantics\MethodBodyModelTests.cs` — grep: `interface IC : IA, IB { }` (:552)
- `Semantic\Semantics\SemanticErrorTests.cs` — grep: `struct S : I { }` (:10363)
- `Emit\CodeGen\CodeGenFunctionPointersTests.cs` — grep: `unsafe class C { }` (:11568)
- `Emit\Emit\EmitMetadataTests.cs` — grep: `interface I1 : I2, I5 { }` (:339)
- `Emit3\Semantics\ParamsCollectionTests.cs` — grep: `[Test('1')]` char attr arg (:476)
- `CSharp15\UnsafeEvolutionTests.cs` — spot-check 4761-4766, 9380-9450 — C# 15 evolution (`unsafe class C;`) — excluded
- `EndToEnd`, `Emit*`, `IOperation`, `CommandLine`, `WinRT`, `CSharp15`, `Symbol` (rest) — mapped via grep for `extern alias`, `unsafe (class|struct|…)`, `struct X :`, `interface X :`, enum literals — only the lines above were in scope

### Repo-wide (outside `src\Compilers\CSharp\Test`)
- `src\RoslynSdk\Samples\CSharp\CSharpToVisualBasicConverter\CSharpToVisualBasicConverter.Test\TestFiles\AllConstructs.cs` — READ fully — used as source for the interleaved-compilation-unit positive test (adapted: members/directives/generics removed)
- `src\Workspaces\CSharpTest\OrganizeImports\OrganizeUsingsTests.cs` — grep: `extern alias` + `using X = N.N.N;` shapes (corroborates alias-using shapes; not used directly)
- `src\Workspaces\CSharpTest\Formatting\FormattingElasticTriviaTests.cs`, `src\Interactive\**`, `src\Scripting\**`, `src\ExpressionEvaluator\**`, `src\Analyzers\CSharp\Tests\**` — mapped via `extern alias` grep — no additional in-scope parse candidates (interactive/semantic/analyzer contexts)
- `src\Compilers\VisualBasic\**`, `src\Features\**`, `src\EditorFeatures\**`, `src\LanguageServer\**` — VB / IDE feature tests; no C# 1.0 declaration-parse candidates

## Selection criteria
1. Input must consist ONLY of constructs in the committed Cs1 grammar scope (empty bodies, no members, no generics, no CS2+ features).
2. POSITIVE = Roslyn asserts 0 parse errors for the input shape (semantic errors OK, e.g. `d = -65536` out of range is fine — parse is clean).
3. NEGATIVE = Roslyn asserts parse errors for the input, AND the failure reason is in scope (missing identifier, unexpected char, missing `;`/`{`/`}`, keyword in identifier position, trailing comma where illegal).
4. Adaptations (documented per test): stripping C# 2+ bits (generics `<T>`, `using static`, targets `assembly:`, expressions in enum values) from otherwise in-scope inputs; keeping the same failure point.
5. One test per Roslyn test method (Roslyn theory cases expanded to the specific type keyword used).

## Candidate counts
- Roslyn test methods examined in Parsing folder: ~120 (DeclarationParsingTests ~180 methods mapped, of which ~90 read in focused ranges; ParsingErrorRecoveryTests ~90; others mapped)
- In-scope candidates found: ~95
- **Selected: 138** (positive 71 / negative 67) — exceeds the 40–100 target; extra coverage came from expanding Roslyn theory cases to every type keyword and from ParsingErrorRecoveryTests garbage/EOF variants, each with its own provenance comment.
- Excluded ~85, main reasons:
  - type members / nested types / statements / expressions (~45)
  - generics & constraints `<T>`, `where` (~15)
  - `using static` (C#6), pointers, tuples, dynamic, records, file-scoped namespaces, top-level statements (~15)
  - attribute target specifiers (`[assembly: …]`) — not in grammar (~8; arg-list shapes adapted instead)
  - enum value expressions (`C + 2`, `ValueA | ValueB`) — Constant-only grammar
  - duplicate-modifier rejection — grammar allows `private private enum` (no rejection rule)
  - `__arglist` param, ref return types — not in grammar param/return shapes
  - C# 15 `file`/`closed`/`safe`/evolution-semicolon bodies

## Excluded notable cases (detail)
- `TestUsingStatic*` (DeclarationParsingTests, UsingDirectiveParsingTests) — `using static` is C# 6
- `TestGlobalAttribute*` as-is — attribute target `assembly:` not in grammar; shapes adapted to `[a(…)] class C { }`
- `TestNestedClass*`, `TestNestedDelegate` — nested types are members
- `TestDelegateWithRefReturnType`, `TestDelegateWithArgListParameter` — ref-return / `__arglist` not in grammar
- `TestPartialEnum`, `ScriptParsingTests.EnumDeclaration`, `FileModifierParsingTests` — `partial`/`file` modifiers
- `TypeMissingIdentifier_Enum01` `D = C + 2` — expression value; member dropped in adaptation
- `NoEnumBody_01/02` (`enum Figure ;`) — grammar requires empty body `{ }`
- `Class_SemicolonBody*` (`class C;`) — C# 15 evolution, no block body
- `TestNamespaceWithExternAliasFollowingUsingBad` — Roslyn errors (C# 2+), but C# 1.0 allowed interleaving and our grammar is in scope → selected as POSITIVE

## Grammar defects found

### FIXED: empty namespace body failed to parse (grammar-level)
- Symptom: `namespace a { }`, `namespace a.b.c { }`, `namespace a { namespace b { } }`, `namespace a { };` all failed with `FatalError` at the closing `}`; a namespace with at least one member parsed fine. `class C { }` was unaffected.
- Root cause: `NamespaceBody = NamespaceMember*` is a named rule that can match empty, but it was referenced as a bare `Ref` in `NamespaceDeclaration`. The engine's TDOPP wrapper (`Parser.cs` `ParseRule`) rejects zero-progress (epsilon) success for a named rule at a non-recovery position — an epsilon match is accepted only for recoverable `RecoveryRule` prefixes at a recovery point. So `NamespaceBody` returned `Failure` exactly when the body was empty, and the `Seq` then failed at `}`.
- Why the other empty-capable rules were fine: `Attributes?`, `EnumMemberList?`, `BaseList?`, `ParameterList?`, `EnumMemberValue?`, `AttributeArgumentList?` are all referenced through `Optional` (`?`), which converts the inner failure into a `NoneNode` success *before* the epsilon rejection can matter.
- Fix (Cs1.grammar, one char): `NamespaceDeclaration = "namespace" QualifiedName "{" NamespaceBody? "}" ";"?;` — matches the established `?` pattern. No code consumes the `NamespaceBody` node kind (verified by repo-wide grep), so the `SomeNode`/`NoneNode` wrapper is harmless.
- Verified: the 4 previously failing Roslyn-derived positive tests (`Namespace_Basic_Succeeds`, `Namespace_Dotted_Succeeds`, `Namespace_Nested_Succeeds`, `Namespace_TrailingSemicolon_Succeeds`) now pass; all 223 CSharpGrammarTests + 324 ParserTests stay green.

### REPORTED (not fixed): empty compilation unit fails (engine-level)
- Symptom: `""`, whitespace-only, and comment-only input all fail with `FatalError` at EOF. Roslyn parses these as a valid empty compilation unit (e.g. `Test.Parse("")`).
- Root cause: same epsilon rejection, but at the TOP-LEVEL rule: `Grammar = CompilationUnit` (both can match empty). `Recover` enters via `ParseRule("Grammar", …)`; when the whole input is trivia the prefix succeeds with zero progress and is rejected at the `Grammar` level. No grammar-level change can fix this — whatever the start rule's RHS is, the start rule itself is a named rule subject to the same rejection (wrapping in `?` only moves the rejection up one level).
- Disposition: out of T1.3.2 scope (engine change, not grammar). Needs an engine decision, e.g. accept an epsilon match for the top-level start rule in `Parse`/`Recover`. No Roslyn-derived test for empty input was added (it cannot be green without the engine change); the case is documented here.

## Tests written
- `Tests/CSharpGrammarTests/Cs1RoslynTestHelper.cs` — shared AssertParses/AssertFails/CreateParser helper
- `Tests/CSharpGrammarTests/Cs1RoslynDirectiveTests.cs` — 35 tests (16 pos / 19 neg): extern alias, using, namespace, interleaved compilation unit
- `Tests/CSharpGrammarTests/Cs1RoslynTypeDeclarationTests.cs` — 78 tests (40 pos / 38 neg): class/struct/interface/enum/delegate headers, modifiers, base lists, enum members, delegate params
- `Tests/CSharpGrammarTests/Cs1RoslynAttributeTests.cs` — 25 tests (15 pos / 10 neg): attribute lists, argument lists, constants, keyword boundaries

**Total: 138 (71 positive / 67 negative).** All green; CSharpGrammarTests project total 223/223.

## Verification
- `dotnet build Nitra.sln` — 0 errors
- `dotnet test Tests/CSharpGrammarTests --no-build` — 223/223 passed
- `dotnet test Tests/ParserTests --no-build` — 324 passed, 2 pre-existing skips, 0 failed
- `dotnet test Tests/WiWorkflowTests --no-build` — 1/1 passed
- `dotnet test Tests/RegexTests --no-build` — 9/9 passed (GraphViz on PATH)
