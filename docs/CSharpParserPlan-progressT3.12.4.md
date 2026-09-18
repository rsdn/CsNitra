# CSharpParserPlan — T3.12.4 progress (`not`/`and`/`or` pattern composition, C# 12.0)

Status: DONE

## Task
Extend the grammar with `not`/`and`/`or` pattern composition:
```
if (x is not null) { }
if (x is > 0 and < 10) { }
if (x is 1 or 2 or 3) { }
```

## Roslyn syntax found
- `C:\RSDN\roslyn\src\Compilers\CSharp\Portable\Parser\LanguageParser_Patterns.cs`
  - `ParsePattern` (53-56) → `ParseDisjunctivePattern`.
  - `ParseDisjunctivePattern` (58-71): `result = ParseConjunctivePattern(...)`; `while (CurrentToken.ContextualKind == OrKeyword) result = BinaryPattern(OrPattern, result, EatToken(), ParseConjunctivePattern(...))`. → `or` is the LOWEST precedence.
  - `ParseConjunctivePattern` (104-117): `result = ParseNegatedPattern(...)`; `while (CurrentToken.ContextualKind == AndKeyword) result = BinaryPattern(AndPattern, result, EatToken(), ParseNegatedPattern(...))`. → `and` is the MIDDLE precedence.
  - `ParseNegatedPattern` (158-185): `if (CurrentToken.ContextualKind == NotKeyword) return UnaryPattern(EatToken(), ParseNegatedPattern(...))` else `ParsePrimaryPattern(...)`. → `not` is the HIGHEST precedence (prefix, right-recursive).
  - Relational pattern — `ParsePrimaryPattern` (217-228): `case LessThanToken/LessThanEqualsToken/GreaterThanToken/GreaterThanEqualsToken/EqualsEqualsToken/ExclamationEqualsToken: return RelationalPattern(EatToken(), ParseSubExpression(Precedence.Relational))`. → a relational pattern (`> 0`, `< 10`) is a PRIMARY pattern. Needed for the `> 0 and < 10` test.
  - `IsValidPatternDesignation` (407-457): `and`/`or` are NOT a pattern designation when the next token can start a pattern (identifier / relational op / `(`/`[`/`{`); they ARE a designation only before a terminator (`)`/`]`/`}`/`,`/`;`/`?`/`:`). This is why `int and string` is `int` + `and` + `string`, not a declaration pattern `int and`.
- Version gate: pattern composition + relational patterns are semantic (binder) features; the PARSER accepts the form wherever the rule exists. Version purity comes from the rules being Cs12-only.

## Existing pattern model
- Cs7.grammar:130-135 `Pattern` (plain, non-TDOPP rule): DiscardPattern / DeclarationPattern / VarPattern / TypePattern / ConstantPattern.
- Cs12.grammar (T3.12.3): re-declares `Pattern`, appends ListPattern / SlicePattern.
- `Pattern` is used in `TypeIsPattern` (Cs7:108), `PatternCaseLabel` (Cs7:145), `SwitchArm` (Cs8:43).
- NO relational pattern existed (the `<`/`>` operators are only Expression operators, Cs1.grammar:698-701).

## Key constraint discovered
- The meta-grammar (`Tests/ParserTests/CsNitra/CsNitraGrammarText.cs`) only lets a rule reference TOP-LEVEL rules (`Ref`). Named alternatives (`| Name = ...`) are just labels, NOT referenceable rules (RuleGenerator.cs:86-99).
- A `|` (union) is NOT valid inside a `(...)` Group — a choice of literals must be a separate Rule with alternatives (meta-grammar `Alternative` vs `RuleExpression`).
- The base patterns are ALTERNATIVES of `Pattern`, not top-level rules. So the composition cannot reference "a base pattern" directly.
- The parser has a parse-depth guard (Parser.Recovery.cs:152-163, `_maxParseDepth = inputLength*4+128`; Parser.cs:495-496 returns Failure when exceeded). A left-recursive cycle `Pattern → DisjunctivePattern → ConjunctivePattern → NegatedPattern → Pattern` would hit the depth guard and FAIL. So the composition must reference a SEPARATE top-level rule that does NOT include the composition.

## Rule written (Cs12.grammar)
```
Pattern =
    | ListPattern  = "[" (Pattern; ",")* "]"          // T3.12.3 (unchanged)
    | SlicePattern = ".." Pattern?                    // T3.12.3 (unchanged)
    | RelationalPattern  = RelationalOperator Expression : Relational   // T3.12.4 (new base pattern)
    | DisjunctivePattern = ConjunctivePattern ("or" ConjunctivePattern)*;  // T3.12.4 (composition, lowest)

ConjunctivePattern = NegatedPattern ("and" NegatedPattern)*;   // middle

NegatedPattern =
    | NotPrefix = "not" NegatedPattern                // highest (right-recursive prefix)
    | PrimaryPattern;                                 // leaf = a base pattern

PrimaryPattern =                                        // duplicated base patterns (left-recursion guard)
    | DiscardPattern     = "_"
    | DeclarationPattern = Type !ReservedKeyword !("and") !("or") Identifier
    | VarPattern         = "var" !ReservedKeyword !("and") !("or") Identifier
    | TypePattern        = Type
    | ConstantPattern    = !(!ReservedKeyword Identifier "=>") !("(" (LambdaParameter; ",")* ")" "=>") Expression
    | ListPattern        = "[" (Pattern; ",")* "]"
    | SlicePattern       = ".." Pattern?
    | RelationalPattern  = RelationalOperator Expression : Relational;

RelationalOperator =
    | ">" | ">=" | "<" | "<=" | "==" | "!=";
```
- `DisjunctivePattern` is added to `Pattern` (T0.3 append); it subsumes `ConjunctivePattern` and `NegatedPattern`, so a single alternative is enough.
- `PrimaryPattern` duplicates the base patterns (the Cs7/Cs12 base alternatives + the new RelationalPattern) because the composition must reference a rule WITHOUT the composition (the left-recursion guard).
- The `!("and") !("or")` lookaheads on the PrimaryPattern Declaration/Var patterns mirror Roslyn `IsValidPatternDesignation` (a composition keyword is not a designation when a pattern can follow).

## Code iterations
1. **First attempt** — wrote the composition with the relational operator as an inline group:
   `RelationalPattern = (">" | ">=" | "<" | "<=" | "==" | "!=") Expression : Relational`.
   Build OK, but the first test run FAILED at grammar-parse time:
   `(3711,33): Expected: "{", "??", "+", "*", "?", Literal, "context", Identifier, "(", "&", "!", ")", ";"`
   pointing at the `(` after `RelationalPattern =`. **Root cause:** `|` is not a valid `RuleExpression` operator — a choice of literals must be a separate Rule with `|`-alternatives, not a `(...)` Group. **Fix:** extracted a `RelationalOperator` Rule (six `|`-alternatives) and used `RelationalPattern = RelationalOperator Expression : Relational`. Rebuild + test → 11/12 passed.
2. **Second attempt (the `and` designation bug)** — `AndPattern_Type_Succeeds` (`x is int and string`) FAILED: `end=-1/53, errorPos=38` (at `string`). **Root cause:** `and` is a contextual keyword (a valid identifier), so the DeclarationPattern (`Type !ReservedKeyword Identifier`) greedily consumed `int and` as a declaration pattern (type `int`, designation `and`), leaving `string` unmatched. **Fix:** added `!("and") !("or")` lookaheads to the PrimaryPattern Declaration/Var patterns (mirroring Roslyn `IsValidPatternDesignation`), so the composition path parses `int` + `and` + `string` (length 14), which beats the base DeclarationPattern's `int and` (length 6) under longest-match-wins. Rebuild + test → 12/12 passed.
3. **csproj corruption (external)** — `roslyn_run_specific_test` (Roslyn MCP) appended an explicit `<ItemGroup><Compile Include="Cs12ListPatternTests.cs" /><Compile Include="Cs12PatternCompositionTests.cs" /></ItemGroup>` to `CSharpGrammarTests.csproj`, causing `NETSDK1022: Duplicate 'Compile' items`. **Fix:** `git restore -- Tests/CSharpGrammarTests/CSharpGrammarTests.csproj` (back to the committed state, no explicit Compile items). All subsequent builds/tests ran via the `bash` tool (`dotnet build`/`dotnet test`) to avoid re-corruption. The csproj is UNCHANGED vs HEAD.

## Version-purity results
- v12 (`CreateParser(12)`): ACCEPTS `x is not null`, `x is not int`, `x is > 0 and < 10`, `x is int and string`, `x is 1 or 2 or 3`, `x is > 0`. Regression: `x is int` (a bare type pattern) still parses (the FIRST base alternative wins the equal-length tie with the DisjunctivePattern path).
- v11 (`CreateParser(11)`): REJECTS `x is not null`, `x is > 0 and < 10`, `x is 1 or 2 or 3`, `x is > 0` (the DisjunctivePattern / RelationalPattern alternatives and the PrimaryPattern / ConjunctivePattern / NegatedPattern rules are ABSENT; a composed/relational pattern matches no Pattern alternative).

## Tests (pos/neg)
File: `Tests/CSharpGrammarTests/Cs12PatternCompositionTests.cs` (CRLF + UTF-8 BOM). 12 tests, all green.
- Positive (v12): 7 — `NotPattern_NotNull_Succeeds` (`not null`), `NotPattern_Type_Succeeds` (`not int`), `AndPattern_Relational_Succeeds` (`> 0 and < 10`), `AndPattern_Type_Succeeds` (`int and string`), `OrPattern_Constant_Succeeds` (`1 or 2 or 3`), `RelationalPattern_Simple_Succeeds` (`> 0`), `TypePattern_IsInt_StillSucceedsAtV12` (regression).
- Negative (v11 version-purity): 4 — `NotPattern_NotNull_RejectedAtV11`, `AndPattern_Relational_RejectedAtV11`, `OrPattern_Constant_RejectedAtV11`, `RelationalPattern_Simple_RejectedAtV11`.
- Negative (v12 malformed): 1 — `OrPattern_TrailingOperator_Rejected` (`1 or` — trailing `or` with no right-hand pattern).

## Verification
- `dotnet build Nitra.sln --no-incremental` → Build succeeded, 0 Warning(s), 0 Error(s).
- `dotnet test Tests/CSharpGrammarTests` (fresh build) → Passed: 1470, Failed: 0, Skipped: 3, Total: 1473.
- `dotnet test Tests/ParserTests` (regression) → Passed: 325, Failed: 0, Skipped: 2, Total: 327.

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs12.grammar` (added RelationalPattern + DisjunctivePattern to `Pattern`; new top-level rules `ConjunctivePattern`, `NegatedPattern`, `PrimaryPattern`, `RelationalOperator`; CRLF / no BOM preserved)
- `Tests/CSharpGrammarTests/Cs12PatternCompositionTests.cs` (new; CRLF + UTF-8 BOM)
- `docs/CSharpParserPlan-progressT3.12.4.md` (this file)
- `docs/CSharpParserPlan-checklist.md` (already `[~]` for T3.12.4 — NOT marked `[✅]`; left for the orchestrator)
- `Tests/CSharpGrammarTests/CSharpGrammarTests.csproj` — REVERTED to committed state (removed the `roslyn_run_specific_test`-injected duplicate `<Compile>` items); ends up UNCHANGED vs HEAD.

## Working-tree hygiene
- Did NOT stage/commit anything (orchestrator commits).
- Did NOT touch `docs/RecoveryImprovementPlan.md` / `docs/antlr4-analysis.md` (left as found, unstaged).
- The test csproj was reverted (not modified from HEAD) so it is not part of the change set.
