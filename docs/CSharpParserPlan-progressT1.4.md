# T1.4 — C# 1.0 type grammar + tests + Roslyn test selection

## Status
Done — `Type = Identifier;` placeholder replaced with the full C# 1.0 type grammar (predefined types, qualified names, arrays with all rank forms, pointers, arbitrary pointer/array interleaving). 65 new tests (27 simple + 38 Roslyn-derived), all suites green (CSharpGrammarTests 276/276, ParserTests 324/326 with 2 pre-existing skips, RegexTests 9/9, WiWorkflowTests 1/1). One flaky timing test in ParserTests (`RecoveryPerfTests.Test_Recovery_Performance_Scaling`, machine-load dependent, documented in T1.3.1) failed once and passed on rerun — unrelated to this change. No engine changes needed.

## Grammar changes (Parsers/CSharp/CSharpGrammar/Cs1.grammar)

Replaced `Type = Identifier;` with:

```
Type =
    | PredefinedType
    | QualifiedName
    | PointerType = Type : TypePointer "*"
    | ArrayType = Type : TypeArray ArrayRankSpecifier;

PredefinedType =
    | "bool" | "byte" | "char" | "decimal" | "double" | "float" | "int"
    | "long" | "object" | "sbyte" | "short" | "string" | "uint" | "ulong"
    | "ushort" | "void";

ArrayRankSpecifier = "[" ","* "]";
```

plus a second precedence statement (after the temporary Expression one):

```
precedence TypeArray, TypePointer, Comma;
```

### Semantics ported from Roslyn (C:\RSDN\roslyn, main, HEAD 9f220ee5d10)
Note: `Parser/SyntaxParser_Types.cs` does not exist in this checkout — the type-parsing code lives in `Parser/LanguageParser.cs` (see RoslynGrammarMap.md §1.5).

- **Predefined set — exactly 16**: `SyntaxFacts.IsPredefinedType` (`Syntax/SyntaxKindFacts.cs:316-340`): bool byte sbyte int uint short ushort long ulong float double decimal string char object void. (RoslynGrammarMap.md says "17" but lists 16; the code is authoritative.) `void` is included: Roslyn accepts it as a PredefinedType with a binder error in non-return contexts (`ParseUnderlyingType:7975-7978`, `ParseTypeOrVoid:7541-7550`); `void*` works naturally via the pointer postfix.
- **Postfix cycle** — `ParseTypeCore` (`LanguageParser.cs:7614-7676`): after the underlying type, any number of `*` (pointer) and `[...]` (array rank) in any order. Ported as two self-recursive alternatives of `Type` → TDOPP postfixes. Each `*` is one postfix application (`ParsePointerTypeMods:8173-8182` consumes the whole run; the engine's postfix loop reproduces the same nested `PointerType(PointerType(...))` shape one star at a time).
- **Rank specifier in type context** — `ParseArrayRankSpecifier` (`LanguageParser.cs:7860-7918`): each comma adds one dimension, so `[` + n commas + `]` = rank n+1 (`[]` = rank 1, `[,]` = rank 2, `[,,]` = rank 3). Non-omitted sizes are expressions and only appear in `new`/`stackalloc` contexts — NOT in type context (C# 1.0 `array_type: type [ ]`), hence `ArrayRankSpecifier = "[" ","* "]"` with no expressions: `T[1]` is a parse failure in our grammar (task: «мусор в rank» → fail). The "don't end on a comma" recovery branch (`:7897-7901`) and omitted/non-omitted mixing (`:7903-7912`) are Roslyn error-recovery, not accepted syntax — out of scope.
- **Underlying type** — `ParseUnderlyingType` (`LanguageParser.cs:7969-8001`): predefined | qualified name | (tuple C#7 / function pointer C#9 — excluded). Qualified names via the existing `QualifiedName = TypeName NamespaceSegment*` / `TypeName = !ReservedKeyword Identifier` (C# 1.0 form; `::` alias names are C# 2, excluded).
- **Not in C# 1.0 (excluded by version purity)**: nullable `?` (CS2 — also note the T1.3.1 provisional table mentioned it for T1.4, but the T1.4 task scope explicitly excludes `?`/`!`), NRT annotations (CS8), generics `T<...>` (CS2), tuples (CS7), function pointers (CS10+), `ref`/`out` (already `ParameterModifier`, not part of `Type`).

### Engine mechanics (verified in code, no engine changes needed)
- **Self-recursive alternatives become postfixes** — `BuildTdoppRulesInternal` (`ExtensibleParser/Parser.cs:111-150`): an alternative `Seq { [Ref self, .. rest] }` is a postfix; precedence comes from a `ReqRef` in `rest`, else from the self-ref if it is a `ReqRef`, else 0. Hence `: TypePointer` / `: TypeArray` are attached to the FIRST (self) `Type` ref: `PointerType = Type : TypePointer "*"`.
- **Postfix with precedence 0 is never applicable** — `ContinueFromPartialPostfix` (`Parser.cs:348`): `isApplicable = postfix.Precedence > minPrecedence || (== && Right)`. `Type` is always parsed with `minPrecedence = 0` (plain `Ref`, `Parser.cs:496`), so a precedence-0 postfix would never fire. Therefore the second `precedence` statement was required (task anticipated this).
- **Precedence merge** — `CsNitraTypeChecker.ResolvePrecedenceDependencies`/`TryMergePrecedenceList` (`Parsers/CsNitra/CsNitraGrammar/TypeChecking/CsNitraTypeChecker.cs:38-122`): multiple `precedence` statements merge via a shared anchor; binding powers are assigned from the merged list, first name = highest (`:56-64`). `precedence TypeArray, TypePointer, Comma;` merges before `Comma` → final order `[..., Assignment=4, TypeArray=3, TypePointer=2, Comma=1]`. Expression's relative order is untouched; Type's levels are never referenced by Expression, so no cross-rule interaction. (The two Type postfixes never compete at the same position — different leading tokens `*` vs `[` — so their relative order is immaterial.)
- **Multiple postfixes at one position** — `ContinueFromPartialPostfix` tries all applicable postfixes per position and keeps the longest (`Parser.cs:345-380`); with distinct leading tokens at most one matches, so no equal-length ties. The zero-progress guard (`:395-399`) cannot trigger (every postfix consumes ≥ 1 char).
- **No equal-length prefix ties** — at any position at most one of `PredefinedType` (a reserved keyword) and `QualifiedName` (`!ReservedKeyword Identifier`) can match; the 16 predefined keywords are all in `ReservedKeyword`, so the sets are disjoint.
- **Inlining** — `BuildTdoppRules` computes `inlineableRules` from `TdoppRules`, which is empty on the first (and only) call → inlining is a no-op; `Ref("ArrayRankSpecifier")` stays a ref and parses via its own TDOPP rule. Behavior unaffected.

## Roslyn repo map (scanned for T1.4)

### `src\Compilers\CSharp\Test\Syntax\Parsing\` (PRIMARY)
| File | Scanned? | Result |
|---|---|---|
| NameParsingTests.cs (72 KB) | fully read (1-1252) | MAIN SOURCE: TestBasicTypeName, TestDottedTypeName, TestKnownTypeNames (14 types), TestPointerTypeName(W), TestArrayTypeName, TestMultiDimensional/MultiRankedArrayTypeName, TestMissingNameDueToKeyword. Excluded: TestAliasedName/TestGlobalAliasedName/TestDoubleAliasName (`::` — CS2), TestGenericName*/TestOpenName* (CS2), TestNullableTypeName (CS2), TestVarianceInNameBad etc. (CS2), Unicode identifier tests (T1.2 scope, lexer) |
| TypeArgumentListParsingTests.cs (193 KB) | read 1-340 + method map | SOURCE for type SHAPES: TestPredefinedType (`string`), TestArrayType (`X[]`), TestPredefinedPointerType (`int*`) — contexts are CS2+/CS3 (generics, var), shapes adapted to delegate parameters (criterion 4). Rest is generic-argument disambiguation (CS2) — excluded |
| DeclarationParsingTests.cs, ParsingErrorRecoveryTests.cs, UsingDirectiveParsingTests.cs, others | method maps (via T1.3.2) | no NEW in-scope type candidates beyond T1.3.2 coverage (headers/base lists already covered; garbage/recovery cases already selected in T1.3.2) |

### `src\Compilers\CSharp\Test\` other folders (mapped, spot-checked)
- `Symbol\Symbols\Source\EnumTests.cs` — READ 880-1029 — MAIN SOURCE: TestFullNameForEnumBaseType (16 enum bases, 0 errors), TestBadEnumBaseType (`string`/`System.String` bases — parse clean, semantic CS1008), InvalidEnumUnderlyingType (`int[]`/`int*` bases — parse clean, semantic CS1008/CS0214; `dynamic` CS4 and generic `T` CS2 members excluded)
- `Semantic\Semantics\UnsafeTests.cs` — READ 2660-2720 + grep — TypeIsUnsafe fields f0-f3 (`int*`, `int**`, `int*[]`, `int*[][]` — parse clean, semantic CS0306 only for generic f4-f7); `operator long*(C* i)` (:12443)
- `Emit2\Emit\ManagedAddressTests.cs` — READ 20-70 — `int[]*` field/local/parameter (parse clean, semantic WRN_ManagedAddr)
- `Emit2\CodeGen\EditAndContinue\LocalSlotMappingTests.cs` — grep — `E***[,,]` local (:5043) — triple pointer + rank 3
- `Emit\Emit\EntryPointTests.cs` — grep — `Main(string[,] goo)` (:69) — rank-2 array parameter
- `Semantic\Semantics\BindingAsyncTasklikeMoreTests.cs` — grep — `void F(void* p)` (:1111)
- `Semantic\Semantics\DelegateTypeTests.cs` — grep — `void F2(byte*[] a)` (:11853)
- `Semantic\Semantics\SemanticErrorTests.cs` — grep — `int **j = &i;` (:11008) — shape already covered via TypeIsUnsafe f1, not double-selected
- `Emit\CodeGen\UnsafeTests.cs`, `Symbol\Symbols\TypeTests.cs`, `Symbol\Symbols\TypeUnificationTests.cs`, `Emit\CodeGen\CodeGenCheckedTests.cs` etc. — grep — `int[,]`/`T[,]` field shapes: parse-clean but already covered by rank forms above; no new candidates

## Selection criteria (same as T1.3.2)
1. Input must consist ONLY of constructs in the committed Cs1 grammar scope (type declarations with empty bodies, delegates, parameters, base lists, enum bases; no members, no statements, no CS2+ features).
2. POSITIVE = Roslyn asserts 0 parse errors for the input shape (semantic errors OK, e.g. `enum E : int[]` → CS1008 is a binder diagnostic, parse is clean).
3. NEGATIVE = Roslyn asserts parse errors for the input, AND the failure reason is in scope (keyword in type position).
4. Adaptations (documented per test): ParseTypeName/field/local/parameter contexts → delegate return type / delegate parameter / enum base; stripping C#2+ context (generics, var, async) while keeping the exact type shape.
5. One test per Roslyn test method (TestKnownTypeNames theory calls expanded to the 14 type keywords).

## Candidate counts
- Roslyn test methods examined: ~30 (NameParsingTests fully, TypeArgumentListParsingTests partial, EnumTests 3 methods, Unsafe/ManagedAddress/LocalSlotMapping/EntryPoint/BindingAsync/DelegateType via grep+read)
- In-scope candidates found: ~40
- **Selected: 38** (37 positive / 1 negative) — plus 27 simple cases (16 pos / 11 neg) = **65 total (53 pos / 12 neg)**
- Excluded, main reasons:
  - `::` alias-qualified names (CS2 extern alias) — 3 Roslyn tests
  - generics/open generics/variance (CS2) — ~10
  - nullable `?` (CS2) / NRT (CS8) — 1 + simple negatives
  - statements/locals/fields as context (adapted instead of excluded where the type shape is C#1)
  - Unicode identifier lexing (T1.2 scope)

## Excluded notable cases (detail)
- `TestAliasedName`/`TestGlobalAliasedName`/`TestDoubleAliasName` (`goo::bar`, `global::bar`) — alias names are C# 2 (extern alias); Roslyn latest parses them with 0 errors, so they are NOT Roslyn negatives — excluded as out-of-scope feature, covered by a simple version-purity negative `Invalid_AliasQualifiedName_Fails`
- `TestNullableTypeName` (`goo?`) — CS2; Roslyn latest parses with 0 errors → not a Roslyn negative; simple negative `Invalid_NullableValueType_Fails`
- `InvalidEnumUnderlyingType` members E3 (`dynamic`, CS4) and E4 (`T`, generic type parameter, CS2) — out of Cs1 scope
- `TypeIsUnsafe` fields f4-f7 (`C<int*>` etc.) — generic type arguments (CS2)
- `TestPredefinedType`/`TestArrayType`/`TestPredefinedPointerType` as-is — contexts use `var` (CS3) + generics (CS2); only the type shapes selected, adapted to delegate parameters

## Boundary decisions / deviations
1. **`var` / `dynamic` as type names PARSE (positive), contrary to the task's negative list («var → fail (CS2)», «dynamic → fail (CS4)»).** In C# 1.0 both words were ordinary identifiers (not in the C# 1.0 keyword list); Roslyn's lexer classifies them as contextual keywords → `IdentifierToken` at EVERY language version including C# 1 (`SyntaxKindFacts.cs:1253-1312`), so Roslyn itself parses `var`/`dynamic` as type names with 0 parse errors even at `LanguageVersion.CSharp1`. T1.3.1's ReservedKeyword decision (verified against Roslyn, `docs/CSharpParserPlan-progressT1.3.1.md` §«ReservedKeyword — C# 1.0 set (81 words)») explicitly keeps them unreserved. The task's "fail" is satisfied at the FEATURE level: the Cs1 grammar has no implicit-local-typing (`var x = …`) and no `dynamic` type feature — there is no construct to reject. Syntax level: `delegate var D();` / `delegate dynamic D();` are valid C# 1.0 (a delegate returning a type named `var`/`dynamic`). Documented as `Contextual_Var_AsTypeName_Succeeds` / `Contextual_Dynamic_AsTypeName_Succeeds`.
2. **No nullable `?`** — the T1.3.1 provisional table said "T1.4: + nullable `?`", but the T1.4 task scope explicitly excludes it (CS2 nullable value types / CS8 NRT). Excluded; a future Cs2 stage appends the alternative (merge-friendly: the Type postfix design already anticipates a third postfix level).
3. **Octal integer literals (flagged by T1.3.1 "for T1.4") — NOT done here.** That is a terminal-level change (`DecimalIntegerLiteral`/`OctalIntegerLiteral` regexes in `CSharpTerminals.cs`), i.e. T1.2.x (lexer) scope, not the type grammar; touching it would change literal parsing for all existing tests. Carried to the terminal backlog.
4. **Rank specifier rejects sizes in type context** (`T[1]` fails) — deliberate: C# 1.0 type syntax has no sizes (they exist only in `new`/`stackalloc` expressions, out of Cs1 scope); Roslyn's permissive expression-in-rank parsing is error-recovery-shaped and would require an Expression rule inside the rank, which is not the C# 1.0 type grammar.
5. **`enum E : int[] { }` / `enum E : int* { }` are POSITIVES** — Roslyn parses them with 0 parse errors (semantic CS1008/CS0214 only); the grammar models the parser (same parser-vs-binder split as T1.3.1).

## Tests written
- `Tests/CSharpGrammarTests/Cs1TypeTests.cs` — 27 tests (16 pos / 11 neg): predefined char/void/whole-word, qualified names (deep dotted, dotted base lists — the T1.3.1 "does not parse until T1.4" gap), array ranks 2/4 + trivia, pointer with spaces, combos (`int[]*[]`, `int*[]*`, `N.M*[]`, multi-declaration with ref/out params), contextual var/dynamic (decision 1), negatives (generics, open generic, nullable, NRT, `::`, reserved keyword, rank size/garbage/unclosed, bare `*`/`[]`)
- `Tests/CSharpGrammarTests/Cs1RoslynTypeTests.cs` — 38 tests (37 pos / 1 neg): NameParsingTests (basic/dotted names, 14 known types, pointer 1/3 stars, array rank 1/3, multi-ranked, keyword negative), TypeArgumentListParsingTests (3 adapted shape tests), EnumTests (full-name bases ×16, bad bases, array/pointer bases), UnsafeTests TypeIsUnsafe (f1-f3) + class-pointer param, ManagedAddressTests (`int[]*`), LocalSlotMappingTests (`E***[,,]`), EntryPointTests (`string[,]`), BindingAsyncTasklikeMoreTests (`void*`), DelegateTypeTests (`byte*[]`)

**Total: 65 (53 positive / 12 negative).** All green; CSharpGrammarTests project total 276/276 (baseline 211 + 65).

## Verification
- `dotnet build Nitra.sln --no-incremental` — 0 warnings, 0 errors
- `dotnet test Tests/CSharpGrammarTests --no-build` — 276/276 passed (baseline 211 + 65 new; all 138 T1.3.2 Roslyn tests stay green; Cs1+Cs6 merged-grammar InterpolatedStringTests green)
- `dotnet test Tests/ParserTests --no-build` — 324 passed, 2 pre-existing skips, 0 failed (one run showed the known flaky `RecoveryPerfTests.Test_Recovery_Performance_Scaling` timing failure — machine-load dependent, documented in T1.3.1, passed on rerun)
- `dotnet test Tests/RegexTests --no-build` — 9/9 passed
- `dotnet test Tests/WiWorkflowTests --no-build` — 1/1 passed
- `dotnet format Tests/CSharpGrammarTests --verify-no-changes` — new files clean; pre-existing ENDOFLINE findings in `InterpolatedStringTests.cs` / `CSharpTerminalsTests.cs` (not touched by this task)
- Hygiene: CRLF + UTF-8 BOM in both new test files (matches committed test files); `Cs1.grammar` stays CRLF/no-BOM as before

## Files
- `Parsers/CSharp/CSharpGrammar/Cs1.grammar` — `Type` re-declared (main artifact) + `PredefinedType` + `ArrayRankSpecifier` + second `precedence` statement
- `Tests/CSharpGrammarTests/Cs1TypeTests.cs` — new (27 tests)
- `Tests/CSharpGrammarTests/Cs1RoslynTypeTests.cs` — new (38 tests)
- `docs/CSharpParserPlan-progressT1.4.md` — this file
- No changes to `ExtensibleParser`, `Cs6.grammar`, terminals, or the temporary `Expression`
