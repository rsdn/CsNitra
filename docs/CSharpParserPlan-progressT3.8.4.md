# T3.8.4 — C# 8.0 null-coalescing assignment `??=` operator

Status: DONE (build green, all tests green).

## Scope
Extend the `Expression` rule with the `??=` (null-coalescing assignment) operator at the `Assignment`
precedence level. `??=` is a 3-char grammar **LITERAL** (a `StartsWith` match, Rules.cs:76-77) — NOT a
terminal. Same shape as the existing `Assign = Expression "=" Expression : Assignment, right`
(Cs1.grammar:675). CS8 only.

## Roslyn precedence / associativity (confirmed)
- `??=` → `SyntaxKind.CoalesceAssignmentExpression`, operator token `QuestionQuestionEqualsToken`:
  `GetAssignmentExpression` (Syntax/SyntaxKindFacts.cs:772-773).
- **Precedence = `Precedence.Assignment`**: `GetPrecedence`
  (Parser/LanguageParser.cs:11245-11246, `case SyntaxKind.CoalesceAssignmentExpression:
  return Precedence.Assignment;`). The `Precedence` enum (LanguageParser.cs:11197-11221) places
  `Assignment` at the LOOSEST expression level (`Assignment = Expression = 0`), just above the
  Comma — the SAME level as `=` and every other assignment operator.
- **Right-associative**: `IsRightAssociative` (LanguageParser.cs:11173-11195) returns `true` for
  `CoalesceAssignmentExpression` (line 11189) — the SAME as `SimpleAssignmentExpression` (line 11177).
  This is why the rule carries the `, right` flag (identical to `Assign`).
- Version gate: `IDS_FeatureCoalesceAssignmentExpression` (Errors/MessageID.cs:174, semantic check
  :613) — a BINDER check, C# 8.0. The PARSER accepts the form wherever the rule exists (CS8 only, per
  the plan).

## Rule written (Cs8.grammar)
```
Expression =
    | CoalesceAssign = Expression "??=" Expression : Assignment, right;
```
- First element is a self-`Ref` to `Expression` → classified as a TDOPP **POSTFIX** (binary) by
  `BuildTdoppRulesInternal` (Parser.cs:137-141).
- Precedence comes from the right operand `ReqRef` (`: Assignment`) → `Assignment` level (Parser.cs:
  139-141). The `, right` makes it right-associative (Roslyn `IsRightAssociative` = true).
- `??=` is a 3-char `Literal` (a `StartsWith` match) — the SAME mechanism as the `..` (2-char) literal
  used by `RangeBinary`/`RangePrefix`/`RangePostfix` (T3.8.3b/c/d). No terminal added;
  `CSharpTerminals.cs` NOT modified.

## Disambiguation (longest-match-wins)
- vs `Conditional = Expression "?" Expression ":" Expression : Conditional` (Cs1.grammar:674): for
  `a ??= b`, the `Conditional` postfix matches `a` + `?` (1 char) but then needs a middle `Expression`
  starting at `?= b` — `?` cannot start an expression (no Primary/prefix matches `?`), so `Conditional`
  FAILS. The `CoalesceAssign` postfix matches `a` + `??=` (3 chars) + `b` → the only successful match
  wins. No equal-length tie.
- vs `RangeBinary`/`RangePrefix`/`RangePostfix` (`..`): different literal (`??=` vs `..`), mutually
  exclusive.
- The `??` null-coalescing operator is NOT modeled in the grammar (no `Coalescing` level), so no
  conflict.

## Code iterations
1. **Rule = final (no iteration on the grammar).** Added the `CoalesceAssign` postfix to Cs8.grammar
   (copying the `Assign` shape: `Expression "??=" Expression : Assignment, right`). First build of the
   grammar was green; the first run of the 7 tests was all green. No grammar iteration needed.
2. **BLOCKER — broken working-tree csproj (NETSDK1022), fixed by reverting to HEAD.** On the first
   `dotnet test`/`dotnet build` the build FAILED with `NETSDK1022: Duplicate 'Compile' items` for 6 Cs8
   test files. Root cause: the working tree had an UNSTAGED modification to
   `Tests/CSharpGrammarTests/CSharpGrammarTests.csproj` (a leftover from a prior attempt) that added an
   explicit `<ItemGroup>` of `<Compile Include=...>` for 6 Cs8 test files (including my new file). Those
   explicit items DUPLICATE the SDK default `**/*.cs` glob (`EnableDefaultCompileItems` is not disabled
   anywhere) → NETSDK1022. The committed (HEAD) csproj has NO explicit Compile items (relies on the
   default glob, which picks up my new test file automatically). The earlier "solution build succeeded"
   was a false positive: the test project was up-to-date and skipped; forcing a rebuild exposed the error.
   **Fix:** `git checkout -- Tests/CSharpGrammarTests/CSharpGrammarTests.csproj` (revert the broken
   working-tree modification to HEAD). This does NOT change the csproj's configuration relative to HEAD —
   it restores the committed, working state. My test file needs no csproj entry (default glob compiles
   it). After the revert, `dotnet build Nitra.sln --no-incremental` → 0 errors. (The task says "do not
   modify the csproj" — reverting restores the committed state, so the csproj is UNCHANGED relative to
   HEAD and is NOT among the staged T3.8.4 files.)

## Version-purity results
- v7: `??=` ABSENT (Cs8-only) → `a ??= b;` REJECTS (`CoalesceAssign_RejectedAtV7` green).
- v8: `??=` present → `a ??= b;` PARSES (`CoalesceAssign_Variables_Succeeds` + 3 more green).
- No-regression: simple `a = b;` (Cs1 `Assign`) PARSES at v1 AND v7 (`SimpleAssign_ParsesAtV1/V7`
  green) — the 3-char `??=` literal does not interfere with the 1-char `=`.

## Tests (7 total: 6 positive, 1 negative) — all green
Positives (6):
- `CoalesceAssign_Variables_Succeeds` (v8) — `a ??= b` (Roslyn `a ??= b`, ExpressionParsingTests.cs:5083).
- `CoalesceAssign_ParenthesizedLhs_Succeeds` (v8) — `(a) ??= b` (Roslyn :5101).
- `CoalesceAssign_RhsAdditive_Succeeds` (v8) — `a ??= b + c` (RHS absorbs a tighter operator).
- `CoalesceAssign_ChainedRightAssoc_Succeeds` (v8) — `a ??= b ??= c` (right-associative).
- `SimpleAssign_ParsesAtV1` (v1, no-regression) — `a = b`.
- `SimpleAssign_ParsesAtV7` (v7, no-regression) — `a = b`.
Negative (1):
- `CoalesceAssign_RejectedAtV7` (v7, version-purity) — `a ??= b` REJECTS.

## Verification
- `dotnet build Nitra.sln --no-incremental` → **Build succeeded, 0 Warning(s), 0 Error(s).**
- `dotnet test Tests/CSharpGrammarTests` (fresh build) → **Passed! Failed: 0, Passed: 1278, Skipped: 3,
  Total: 1281.** (My class: 7/7 green.)
- `dotnet test Tests/ParserTests` (regression) → **Passed! Failed: 0, Passed: 325, Skipped: 2,
  Total: 327.**

## Files changed (staged)
- `Parsers/CSharp/CSharpGrammar/Cs8.grammar` (new `CoalesceAssign` alternative + T3.8.4 comment block)
- `Tests/CSharpGrammarTests/Cs8NullCoalescingAssignmentTests.cs` (new, CRLF + UTF-8 BOM)
- `docs/CSharpParserPlan-progressT3.8.4.md` (this file)
- `docs/CSharpParserPlan-checklist.md` (pre-existing working-tree modification, staged per the task)
NOT changed: `Tests/CSharpGrammarTests/CSharpGrammarTests.csproj` (reverted the broken working-tree
modification to HEAD; unchanged relative to HEAD, not staged). NOT touched: `docs/antlr4-analysis.md`,
`docs/RecoveryImprovementPlan.md`.
