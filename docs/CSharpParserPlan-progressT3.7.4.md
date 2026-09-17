# T3.7.4 — C# 7.2 type/constant patterns (verify)

Status: done (see the Deviations section). **No grammar change was needed** — the type/constant patterns
(T3.6.2) and the generic type/declaration patterns are ALREADY supported by the existing rules.

## Goal
Verify and complete the **C# 7.2 type/constant patterns** in the EXISTING `Parsers/CSharp/CSharpGrammar/Cs7.grammar`
(which already has the CS7.0 pattern-matching rules from T3.6.2, the CS7.1 `default` literal from T3.7.1, the
CS7.2 `in` parameter from T3.7.2, and the CS7.2 `ref`/`readonly` struct from T3.7.3; CS7.1/7.2 features go into
Cs7.grammar, version 7):
- **Type patterns**: `x is int`, `x is string` — ALREADY DONE in T3.6.2 (`TypePattern = Type`).
- **Constant patterns**: `x is 5`, `x is "a"` — ALREADY DONE in T3.6.2 (`ConstantPattern = Expression`).
- **Generic type patterns** (C# 7.2): `x is List<int>`, `x is List<int> y` — VERIFY whether this already parses
  (the `Type` rule includes the greedy `TypeName` from T3.1.1.1, which includes the `TypeArgumentList`).

CS7.2 (per the plan's decision, in Cs7.grammar = version 7; the version-purity boundary is v6 rejects / v7
accepts).

## Baseline (before changes)
- `dotnet build Nitra.sln --no-incremental` → success, 0 errors / 0 warnings.
- `dotnet test Tests/CSharpGrammarTests` → **1193 total / 1190 passed / 0 failed / 3 skipped** (matches T3.7.3).
- `dotnet test Tests/ParserTests` → **327 total / 325 passed / 0 failed / 2 skipped** (matches T3.7.3).

## Existing structure found
- **`Pattern`** (Cs7.grammar:116-121, T3.6.2):
  ```
  Pattern =
      | DiscardPattern     = "_"
      | DeclarationPattern = Type !ReservedKeyword Identifier
      | VarPattern         = "var" !ReservedKeyword Identifier
      | TypePattern        = Type
      | ConstantPattern    = Expression;
  ```
- **`TypeIsPattern`** (Cs7.grammar:107-108, T3.6.2): re-declares Cs1 `Expression` to add a NEW postfix operator
  at the Relational level: `TypeIsPattern = Expression : Relational "is" Pattern`.
- **`TypeIs`** (Cs1.grammar:662, C# 1.0): the EXISTING `is` postfix at the Relational level:
  `TypeIs = Expression : Relational "is" Type`. This is the C# 1.0 type check — it accepts ANY `Type`, including
  a generic type.
- **`Type`** (Cs1.grammar:508-512): `| PredefinedType | QualifiedName | PointerType | ArrayType`.
- **`QualifiedName`** (Cs1.grammar:21): `TypeName NamespaceSegment*`.
- **`TypeName`** (merged, Cs1.grammar:23 + Cs2.grammar:44): `| !ReservedKeyword Identifier` (Cs1)
  `| !ReservedKeyword Identifier TypeArgumentList` (Cs2, greedy, T3.1.1.1).
- **`TypeArgumentList`** (Cs2.grammar:48, T3.1.1.1): `"<" (Type; ",")+ ">"`.

So the merged `Type` (at v2+) handles `List<int>` via `QualifiedName = TypeName = List<int>` (the greedy Cs2
alternative with a `TypeArgumentList`), `System.Collections.Generic.List<int>` via the `NamespaceSegment*` loop,
and `Dictionary<string, int>` via a multi-arg `TypeArgumentList`.

## Verification results (empirical)
The generic type pattern ALREADY PARSES — no grammar change was needed:
- `x is int` (type pattern) — parses at v1–v7 (Cs1 `TypeIs` with `Type = int`). Already tested in T3.6.2.
- `x is 5` (constant pattern) — parses at v7 only (Cs7 `TypeIsPattern` + `ConstantPattern`); REJECTS at v6.
  Already tested in T3.6.2.
- `x is List<int>` (generic type pattern) — **parses at v2–v7** (Cs1 `TypeIs` with `Type = List<int>` via the
  greedy `TypeName`). NOT a C# 7.2 feature — it is the C# 1.0 `is` operator with a type + the C# 2.0 generic type
  (Deviation D1).
- `x is List<int> y` (generic declaration pattern) — parses at v7 only (Cs7 `TypeIsPattern` +
  `DeclarationPattern = Type List<int> + Identifier y`); REJECTS at v6. This is the CS7.0-specific part.
- `x is System.Collections.Generic.List<int>` (qualified generic) — parses at v7.
- `x is Dictionary<string, int>` (multi-arg generic) — parses at v7.
- `x is A<B>` / `x is X<T>` (single-arg / type-parameter generic) — parse at v7.

## Approach
**No grammar change.** The generic type pattern is already handled by the Cs1 `TypeIs` rule (the C# 1.0 type
check) combined with the greedy `TypeName` (T3.1.1.1). The generic declaration pattern is already handled by the
Cs7 `TypeIsPattern` rule + the `Pattern` rule's `DeclarationPattern` alternative (which uses `Type`, so it handles
generic types). The task's "add the generic type pattern support if it does NOT parse" branch is not taken — it
already parses. This is a pure verification + test task.

## Mutual-exclusivity hand-traces
The two `is` postfixes at the Relational level (the Cs1 `TypeIs`, declared first; the Cs7 `TypeIsPattern`,
declared later) are disambiguated by longest-match with equal-length ties to the FIRST (declaration order):
- **`x is List<int>` (v7)**: `TypeIs` (Cs1) matches `x is List<int>` (Type = List<int>); `TypeIsPattern` (Cs7)
  matches `x is List<int>` (Pattern = TypePattern = Type = List<int>). Both at the same length → the FIRST
  (Cs1 `TypeIs`) wins the tie → parsed as the C# 1.0 type check with `Type = List<int>`. (Same as `x is int`.)
- **`x is List<int>` (v6)**: `TypeIsPattern` (Cs7) is ABSENT. Only `TypeIs` (Cs1) is present → matches `x is
  List<int>` (Type = List<int>). PARSES. (Deviation D1.)
- **`x is List<int> y` (v7)**: `TypeIs` (Cs1) matches `x is List<int>` (Type = List<int>), leaving ` y` unparsed
  (shorter). `TypeIsPattern` (Cs7) → Pattern = DeclarationPattern = Type List<int> + Identifier y → matches the
  WHOLE `x is List<int> y` (longer). `TypeIsPattern` wins (longest-match) → parsed as a declaration pattern.
  PARSES.
- **`x is List<int> y` (v6)**: `TypeIsPattern` (Cs7) is ABSENT. `TypeIs` (Cs1) matches `x is List<int>`, leaving
  ` y` unparsed → the `if` condition expects `)` but finds ` y` → the statement fails. REJECT.
- **`x is 5` (v7)**: `TypeIs` (Cs1) → Type = 5? `5` is not a Type (not a PredefinedType/QualifiedName) → FAILS.
  `TypeIsPattern` (Cs7) → Pattern = ConstantPattern = Expression = 5 → matches. PARSES.
- **`x is 5` (v6)**: `TypeIsPattern` (Cs7) is ABSENT. `TypeIs` (Cs1) → Type = 5 FAILS. No other `is` postfix
  matches → REJECT.

## Roslyn references (C:\RSDN\roslyn, main, src/Compilers/CSharp)
- **The form + version gate** — `Binder_Operators.cs:4821` (`BindIsOperator`): the `is` operator FIRST tries to
  bind the RHS as a TYPE (`tryBindAsType`, the C# 1.0 type check — works for ANY type including a generic
  `List<int>`); ONLY if that fails AND it is not `_` AND `IDS_FeaturePatternMatching` is enabled does it fall
  back to binding as a pattern (constant/declaration, C# 7.0). So `x is List<int>` is a C# 1.0 type check (no
  version gate) + a C# 2.0 generic type — NOT a C# 7.2 feature.
- **`IDS_FeaturePatternMatching`** — `MessageID.cs:675` (`// semantic check`) maps to
  `LanguageVersion.CSharp7` (`MessageID.cs:682`) — the pattern-matching feature gate is C# 7.0 (a BINDER check,
  not a parse check). This gates the constant/declaration/discard patterns, NOT the type pattern.
- **The generic type pattern form** — `PatternParsingTests.cs:300` (`IsPatternPrecedence_3`):
  - `:309` `e is A<B>` (the `is` operator with a single-arg generic type).
  - `:311` `(item is Dictionary<string, object>[])` (the `is` operator with a multi-arg generic ARRAY type).
  - `:325` (`TypeDisambiguation_01`) `where s is X<T> // should disambiguate as a type here` (a generic type
    whose argument is a type parameter).
- **Generic type in an `is` expression** — `PatternParsingTests2.cs:984` (`MissingClosingAngleBracket02`):
  `e is List<int or IEnumerable<int>` → `IsPatternExpression` → `TypePattern` → `GenericName` (`List<int>`) —
  confirms the `TypePattern` holds a generic type. `PatternParsingTests2.cs:953`
  (`MissingClosingAngleBracket01`): `e is List<int` (missing `>`) → parsed as an `IsExpression` (C# 1.0) with
  `List` as the type — confirms the `is` operator with a generic type is a C# 1.0 type check.

## Version-purity results
Verified empirically (all green tests):
- **v7 ACCEPTS**: `x is int`, `x is string`, `x is 5`, `x is List<int>`, `x is List<int> y`,
  `x is System.Collections.Generic.List<int>`, `x is Dictionary<string, int>`, `x is A<B>`, `x is X<T>`.
- **v6 REJECTS**: `x is 5` (constant pattern, CS7.0), `x is List<int> y` (declaration pattern, CS7.0).
- **v6 ACCEPTS (no-regression)**: `x is int` (Cs1 type check), `x is List<int>` (Cs1 type check + Cs2 generic
  type — the task's premise that it rejects at v6 is a FALSE PREMISE, Deviation D1).
- **v2 ACCEPTS (no-regression)**: `x is List<int>` (the `is` operator with a generic type is C# 1.0 + C# 2.0).
- **v1 ACCEPTS (no-regression)**: `x is int` (Cs1 type check).

## Tests
`Tests/CSharpGrammarTests/Cs7PatternVerifyTests.cs` (CRLF + UTF-8 BOM) — **14 tests, all green**.
- POSITIVE (v7, core, 3): `TypePattern_IsInt_Succeeds` (`x is int`), `TypePattern_IsString_Succeeds`
  (`x is string`), `ConstantPattern_IsIntLiteral_Succeeds` (`x is 5`) — verify T3.6.2.
- POSITIVE (v7, generic, 2): `GenericTypePattern_IsListInt_Succeeds` (`x is List<int>` — the key new thing),
  `GenericDeclarationPattern_IsListIntY_Succeeds` (`x is List<int> y`).
- POSITIVE (v1/v6, no-regression, 2): `TypePattern_ParsesAtV1` (`x is int`), `TypePattern_ParsesAtV6`
  (`x is int`) — the C# 1.0 type check.
- POSITIVE (v2/v6, no-regression, 2): `GenericTypePattern_ParsesAtV2` (`x is List<int>`),
  `GenericTypePattern_ParsesAtV6` (`x is List<int>`) — the false-premise correction (D1).
- NEGATIVE (v6, version-purity, 2): `ConstantPattern_RejectedAtV6` (`x is 5`),
  `GenericDeclarationPattern_RejectedAtV6` (`x is List<int> y`).
- POSITIVE (v7, Roslyn-derived, 3): `GenericTypePattern_SingleArg_Roslyn_Succeeds` (`x is A<B>`,
  PatternParsingTests.cs:309), `GenericTypePattern_MultiArgArray_Roslyn_Succeeds` (`x is Dictionary<string,
  int>`, PatternParsingTests.cs:311), `GenericTypePattern_Disambiguation_Roslyn_Succeeds` (`x is X<T>`,
  PatternParsingTests.cs:325).

## Verification
- [x] `dotnet build Nitra.sln --no-incremental` → **success, 0 errors / 0 warnings**.
- [x] `dotnet test Tests/CSharpGrammarTests` (fresh build) → **1207 total / 1204 passed / 0 failed / 3
  skipped**. Baseline before T3.7.4 (after T3.7.3): 1193 total / 1190 passed / 3 skipped. Delta = **+14**
  (all new `Cs7PatternVerifyTests`). All pre-existing Cs1/Cs2/Cs3/Cs4/Cs5/Cs6/Cs11 tests remain green (NO
  grammar change — the only addition is the new test file).
- [x] `dotnet test Tests/ParserTests` (regression; engine + CsNitra meta-grammar unchanged) → **327 total /
  325 passed / 0 failed / 2 skipped** — matches baseline.

## Files changed
Staged T3.7.4 files:
- `Tests/CSharpGrammarTests/Cs7PatternVerifyTests.cs` (new, CRLF + UTF-8 BOM).
- `docs/CSharpParserPlan-progressT3.7.4.md` (this file).
- `docs/CSharpParserPlan-checklist.md` (T3.7.4 marked `[✅]` with the deviations).

NOT staged (no grammar change was needed):
- `Parsers/CSharp/CSharpGrammar/Cs7.grammar` — UNCHANGED (the generic type pattern already parses via the
  existing Cs1 `TypeIs` + the greedy `TypeName`).

Not staged (left as-is):
- `docs/antlr4-analysis.md` — unrelated untracked file, left alone (not staged).

## Deviations / boundary decisions
- **D1 — the task's premise that the generic TYPE pattern `x is List<int>` must REJECT at v6 is a FALSE
  PREMISE.** `x is List<int>` is the C# 1.0 `is` operator with a type (the Cs1 `TypeIs` rule, present at v1–v6)
  combined with a C# 2.0 generic type (the greedy `TypeName` with a `TypeArgumentList`, present at v2+). So it
  PARSES at v2+ (including v6) — verified empirically. This matches Roslyn: `BindIsOperator`
  (Binder_Operators.cs:4821) binds the `is` RHS as a TYPE first (C# 1.0, no version gate); the
  `IDS_FeaturePatternMatching` gate (C# 7.0, MessageID.cs:675) applies only to the FALLBACK pattern binding
  (constant/declaration/discard). Consequence: the task's NEGATIVE-v6 test for `x is List<int>` would FAIL;
  instead it is written as a POSITIVE (no-regression) test (`GenericTypePattern_ParsesAtV2` /
  `GenericTypePattern_ParsesAtV6`). The CS7.0-specific part of the task is the generic DECLARATION pattern
  (`x is List<int> y`), which correctly REJECTS at v6 (`GenericDeclarationPattern_RejectedAtV6`).
- **D2 — "generic type patterns (C# 7.2)" is a mischaracterization in the task.** In Roslyn, the generic type
  pattern is NOT a C# 7.2 feature: it is the C# 1.0 `is` operator with a type + a C# 2.0 generic type (available
  at C# 2.0+). The C# 7.2 features in this file are the `in` parameter (T3.7.2) and the `ref struct`/`readonly
  struct` (T3.7.3). The C# 7.0 pattern-matching features (type/constant/declaration/discard patterns, T3.6.2)
  are what the `Pattern` rule models. The generic type pattern is therefore tested as a v2+/v6 no-regression
  (D1), and the generic declaration pattern as a v7-positive / v6-negative (CS7.0).
- **D3 — no grammar change was needed.** The task's "add the generic type pattern support if it does NOT parse"
  branch is not taken: the generic type pattern already parses via the Cs1 `TypeIs` + the greedy `TypeName`
  (T3.1.1.1), and the generic declaration pattern already parses via the Cs7 `TypeIsPattern` + the `Pattern`
  rule's `DeclarationPattern` alternative. This is a pure verification + test task.
