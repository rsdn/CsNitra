# T3.2.5 — C# 3.0 LINQ query expressions

Status: done.

## Goal
Add C# 3.0 LINQ query expressions to the EXISTING `Parsers/CSharp/CSharpGrammar/Cs3.grammar`:
basic `from x in xs select x`, `where`, `let`, multiple `from`, `join` (+ `into`), `group` (+ `into`),
`orderby` (ascending/descending, multiple), and anonymous-type projection
(`select new { x, Name = x.Name }`). CS3 only.

## Baseline (before changes)
- `dotnet build Nitra.sln --no-incremental` → success, 0 errors / 0 warnings.
- `dotnet test Tests/CSharpGrammarTests` (fresh build) → **941 total / 938 passed / 0 failed / 3 skipped** (per T3.2.4).
- `dotnet test Tests/ParserTests` (regression) → **327 total / 325 passed / 0 failed / 2 skipped** (per T3.2.4).

## Query keyword reservation findings (the critical check)
Verified against the Cs1 `ReservedKeyword` (Cs1.grammar:537-618) and Roslyn
`IsQueryContextualKeyword` (SyntaxKindFacts.cs:1314-1335):

| keyword | Cs1 ReservedKeyword | Roslyn | status |
|---|---|---|---|
| `in` | **YES** (Cs1.grammar:571) | reserved (`InKeyword`) | RESERVED — plain literal, can never be an identifier |
| `from` | no | contextual | contextual (plain identifier in non-query contexts) |
| `where` | no | contextual | contextual |
| `select` | no | contextual | contextual |
| `group` | no | contextual | contextual |
| `into` | no | contextual | contextual |
| `orderby` | no | contextual | contextual |
| `join` | no | contextual | contextual |
| `let` | no | contextual | contextual |
| `on` | no | contextual | contextual |
| `equals` | no | contextual | contextual |
| `by` | no | contextual | contextual |
| `ascending` | no | contextual | contextual |
| `descending` | no | contextual | contextual |

- **Only `in` is reserved.** All the other query keywords are CONTEXTUAL (plain identifiers in
  non-query contexts). NONE is added to `ReservedKeyword` — that would break C# 1.0 code that uses
  them as identifiers (e.g. `class C { int from; }` must stay valid at v1/v2/v3).
- The query keywords are used as plain **literals** in the query rules.

### The `where` keyword conflict check
`where` is NOT a Cs1 ReservedKeyword (confirmed absent from the list; it was already used as a
WordLiteral for the Cs2 constraint clause, Cs2.grammar:107). A query `where` clause is an
`Expression` in QUERY context; the Cs2 constraint-clause `where` is a `where <id> :` in a
TYPE-PARAMETER context. They live in entirely different rule contexts (`WhereClause`/`Expression`
vs `ConstraintClause`), so there is NO ambiguity and NO conflict.

## Roslyn references (C:\RSDN\roslyn, main)
- `ParseQueryExpression` (LanguageParser.cs:14182): `fromClause + ParseQueryBody`.
- `ParseQueryBody` (14194-14239): a while-loop over `from/join/let/where/orderby` (14199-14221),
  then a terminal `select`-or-`group` (14224-14231), then an optional `into` continuation
  (14236-14238). **`group` is NOT in the body loop** — it is a terminal clause only.
- `ParseFromClause` (14241-14272): `from` + OPTIONAL type (14246-14248: `PeekToken(1) !=
  InKeyword ? ParseType() : null`) + identifier + `in` + `ParseExpressionCore`.
- `ParseJoinClause` (14274-14292): `join` + OPTIONAL type (14279-14281) + identifier + `in` + expr +
  `on` + expr + `equals` + expr + OPTIONAL `into` identifier (14289-14291, a `JoinIntoClause` that
  is PART of the JoinClause — the query continues in the SAME body).
- `ParseLetClause` (14294-14305): `let` + identifier + `=` + `ParseExpressionCore`.
- `ParseWhereClause` (14307-14313): `where` + `ParseExpressionCore`.
- `ParseOrderByClause` (14315-14345): `orderby` + first `Ordering` + `("," Ordering)*`.
- `ParseOrdering` (14360-14376): `ParseExpressionCore` + OPTIONAL (`ascending`|`descending`).
- `ParseSelectClause` (14378-14384): `select` + `ParseExpressionCore`.
- `ParseGroupClause` (14386-14394): `group` + `ParseExpressionCore` (**REQUIRED**, not optional) +
  `by` + `ParseExpressionCore`.
- `ParseQueryContinuation` (14396-14403): `into` + identifier + `ParseQueryBody` (a NEW body).
- `IsQueryContextualKeyword` (SyntaxKindFacts.cs:1314): from/where/select/group/into/orderby/join/
  let/on/equals/by/ascending/descending (`in` is reserved, not in this list).
- Syntax tests (src/Compilers/CSharp/Test/Syntax/Parsing/ExpressionParsingTests.cs):
  TestFromSelect (2301, `from a in A select b`), TestFromWithType (2334, `from T a in A select b`),
  TestFromSelectIntoSelect (2367, `from a in A select b into c select d`), TestFromWhereSelect
  (2418, `from a in A where b select c`), TestFromFromSelect (2460, `from a in A from b in B
  select c`), TestFromLetSelect (2503, `from a in A let b = B select c`), TestFromOrderBySelect
  (2548, `from a in A orderby b select c`), TestFromOrderByMultiSelect (2593, `from a in A orderby
  b, b2 select c`), TestFromOrderByAscendingSelect (2642), TestFromOrderByDescendingSelect (2690),
  TestFromGroupBy (2738, `from a in A group b by c`), TestFromGroupByIntoSelect (2777, `from a in
  A group b by c into d select e`), TestFromJoinSelect (2830, `from a in A join b in B on a equals
  b select c`), TestFromJoinWithTypeSelect (2886, `from Ta a in A join Tb b in B on a equals b
  select c`), TestFromJoinIntoSelect (2941, `from a in A join b in B on a equals b into c select
  d`), TestFromGroupBy2 (2998, `from it in goo group x by y`), TestFromSelectNewObject (3049,
  `from elem in aRay select new Result { A = on = true }`).

## Key design findings
1. **The group expression is REQUIRED, not optional** (Roslyn `ParseGroupClause` uses
   `ParseExpressionCore`, not optional; tests 2738/2998 always have it). This CONTRADICTS the
   task's assumption that `group by key` (without the element) is valid. Follow Roslyn:
   `GroupClause = "group" Expression "by" Expression`.
2. **The optional query-variable type** (`from T a in A`, `join Tb b in B on ...`) is a real Roslyn
   form (tests 2334/2886). The meta-grammar CANNOT express a mid-sequence optional with
   backtracking (`ParseSeq` is committed, Parser.cs:810-878: a greedy `Type?` that matches `x` and
   then fails the following `Identifier` fails the WHOLE sequence — it does not backtrack `Type?`
   to empty). So the two forms are a **named union** (typed/untyped), mutually exclusive.
3. **The `into` after a group is a QueryContinuation** (a NEW `QueryBody`), NOT part of the
   GroupClause. The `into` after a **join** is a `JoinIntoClause` PART of the JoinClause (the query
   continues in the SAME body). These are structurally different in Roslyn and are modeled
   separately.
4. **The clause Expressions are full TDOPP `Expression`s** and naturally stop before the next
   clause keyword (none of the query keywords is a TDOPP binary operator). The `select` expression
   can be an anonymous type (`select new { ... }`), reusing the T3.2.2 `AnonymousType` in `NewBody`.

## Cs3.grammar rules (exact)
```
Primary =
    | QueryExpression = FromClause QueryBody;

FromClause =
    | FromTyped   = "from" Type !ReservedKeyword Identifier "in" Expression
    | FromUntyped = "from" !ReservedKeyword Identifier "in" Expression;

QueryBody =
    | QueryBodySelect = QueryBodyClause* SelectClause QueryContinuation?
    | QueryBodyGroup  = QueryBodyClauseNoGroup* GroupClause QueryContinuation?;

QueryBodyClause =
    | FromClause
    | WhereClause
    | LetClause
    | JoinClause
    | OrderByClause
    | GroupClause;

QueryBodyClauseNoGroup =
    | FromClause
    | WhereClause
    | LetClause
    | JoinClause
    | OrderByClause;

WhereClause = "where" Expression;
LetClause   = "let" !ReservedKeyword Identifier "=" Expression;
SelectClause = "select" Expression;
GroupClause  = "group" Expression "by" Expression;

JoinClause =
    | JoinTyped   = "join" Type !ReservedKeyword Identifier "in" Expression "on" Expression "equals" Expression JoinInto?
    | JoinUntyped = "join" !ReservedKeyword Identifier "in" Expression "on" Expression "equals" Expression JoinInto?;

JoinInto = "into" !ReservedKeyword Identifier;

OrderByClause = "orderby" (Ordering; ",")+;
Ordering = Expression OrderingDirection?;

OrderingDirection =
    | "ascending"
    | "descending";

QueryContinuation = "into" !ReservedKeyword Identifier QueryBody;
```
- `Primary` is RE-DECLARED (append, T0.3 merge) — the same pattern as the T3.2.1 `LambdaExpression` and
  Cs2's `AnonymousMethod`. The query is a `Primary`/expression, **NOT** a TDOPP binary operator.
- `FromClause`/`JoinClause` are named unions (typed/untyped) for the optional query-variable type
  (Roslyn `ParseFromClause` 14246-14248 / `ParseJoinClause` 14279-14281). See the "optional type"
  finding below for why a mid-sequence `Type?` cannot be used.
- `QueryBody` is a named union (`QueryBodySelect`/`QueryBodyGroup`) to give `group` two roles (see the
  "group clause" finding below).
- `WhereClause`/`LetClause`/`SelectClause`/`GroupClause` are named rules referenced by the body-clause
  unions (the meta-grammar forbids `|` inside a group).
- `OrderByClause` uses a `SeparatedList` (`(Ordering; ",")+`). `QueryContinuation` is recursive
  (`into id QueryBody`), matching Roslyn's `ParseQueryContinuation` (14396-14403).

### Finding: the optional query-variable type requires a named union (not `Type?`)
Roslyn `ParseFromClause` (14246-14248) parses an OPTIONAL type (`from T a in A`). The meta-grammar
cannot express a mid-sequence optional with backtracking: `ParseSeq` (Parser.cs:810-878) is COMMITTED
— a greedy `Type?` that matches `x` and then fails the following `Identifier` (because `in` is
reserved) fails the WHOLE sequence instead of backtracking `Type?` to empty. So `FromClause = "from"
Type? !ReservedKeyword Identifier "in" Expression` would REJECT `from x in xs`. The fix is a named
union (`FromTyped`/`FromUntyped`), which is mutually exclusive (see the hand-traces).

### Finding: the `group` clause has two roles (a superset of Roslyn)
In Roslyn, `group` is a TERMINAL clause that must be followed by `into` (a continuation) — `group x by
x.Key select x` (group followed directly by `select`) is NOT valid C#. But the task REQUIRES that form
to parse (it is in the task's POSITIVE test list). So `group` is given two roles via the `QueryBody`
union:
- `QueryBodySelect` (group as a BODY clause, then a `select` terminal): `group x by x.Key select x`
  (the task-required form).
- `QueryBodyGroup` (group as a TERMINAL clause, then an `into` continuation): `group b by c into d
  select e` (the Roslyn form).
This is a documented SUPERSET of Roslyn (it accepts `group … select …`, which Roslyn rejects). The two
alternatives are disambiguated by longest-match (no equal-length tie — see the hand-traces).

### Finding: the group expression is REQUIRED (not optional)
Roslyn `ParseGroupClause` (14386-14394) uses `ParseExpressionCore` (NOT optional), so `group b by c`
always has the element expression. This CONTRADICTS the task's assumption that `group by key` (without
the element) is valid. Follow Roslyn: `GroupClause = "group" Expression "by" Expression`.

## Mutual-exclusivity hand-traces (no equal-length tie)

### `from` disambiguation (query vs plain identifier, at the `Primary` level)
- `from x in xs select x`: `IdentifierName` → `from` (1 name, len ~4); `QueryExpression` →
  `from x in xs select x` (len ~20). Longest-match → **QueryExpression**. No tie.
- bare `from` (no `x in … select …`): `QueryExpression` fails (`FromClause` needs `identifier in
  expression`); `IdentifierName` → `from`. No tie.

### FromClause: typed vs untyped (inside the `FromClause` union)
- `from x in xs`: `FromTyped` → `Type=x`, then the following `Identifier` must match `in` (reserved)
  → FAILS. `FromUntyped` → `Identifier=x`, `in`, `xs` → matches. → **FromUntyped only**. No tie.
- `from T x in xs`: `FromTyped` → `Type=T`, `Identifier=x`, `in`, `xs` → matches. `FromUntyped` →
  `Identifier=T`, then `in` is expected but `x` is found → FAILS. → **FromTyped only**. No tie.
- `from int x in xs` (predefined type): `FromTyped` → `Type=int`, `Identifier=x`, … → matches.
  `FromUntyped` → `!ReservedKeyword` fails on `int` (reserved) → FAILS. → **FromTyped only**. No tie.
- (The `JoinClause` union is identical in structure to the `FromClause` union.)

### QueryBody: select-terminal vs group-terminal (inside the `QueryBody` union)
- `from x in xs select x` (no group): `QueryBodySelect` → `QueryBodyClause*=∅`, `SelectClause=select
  x` → matches (full). `QueryBodyGroup` → `QueryBodyClauseNoGroup*=∅`, `GroupClause` fails (next is
  `select`, not `group`) → FAILS. → **QueryBodySelect only**. No tie.
- `from x in xs group x by x.Key select x` (group + select): `QueryBodySelect` → `QueryBodyClause* =
  group x by x.Key`, `SelectClause = select x` → matches (full). `QueryBodyGroup` → `GroupClause =
  group x by x.Key`, `QueryContinuation?` fails (next is `select`, not `into`) → matches (shorter,
  stops before `select`). Longest-match → **QueryBodySelect** (longer). No tie.
- `from a in A group b by c into d select e` (group + into): `QueryBodySelect` → `QueryBodyClause* =
  group b by c`, `SelectClause` fails (next is `into`, not `select`) → FAILS. `QueryBodyGroup` →
  `GroupClause = group b by c`, `QueryContinuation = into d <body: select e>` → matches (full). →
  **QueryBodyGroup only**. No tie.

### The clause Expressions stop before the next clause keyword
The `in`/`where`/`let`/`on`/`equals`/`select`/`orderby` expressions are full TDOPP `Expression`s. None
of the query keywords (`from`/`where`/`select`/`group`/`into`/`orderby`/`join`/`let`/`on`/`equals`/
`by`/`ascending`/`descending`) is a TDOPP binary operator, so each clause expression naturally stops
before the next clause keyword (e.g. the `in` expression in `from x in xs where …` stops at `xs`,
before `where`). No lookahead is needed for the clause loop.

### Comma absorption (documented boundary, same as T3.2.1/T3.2.2)
The TDOPP `Comma` postfix (bp 1) is applicable at minPrecedence 0, so a clause expression greedily
absorbs a following comma. E.g. `orderby x, x.Name ascending select x` parses the first ordering's
expression as `x, x.Name` (a comma expression) rather than two separate orderings. This is the
EXISTING behavior of the `Expression` rule for any top-level expression in a list (e.g. `new Foo(1, 2)`
already parses `1, 2` as one comma expression) and does NOT cause a parse failure — the whole input is
still consumed, so the positive test passes. Changing this would require altering the shared TDOPP
`Expression`/`Comma` rule (out of scope, risky); left as-is.

## Version-purity results
- `class C { void M() { var q = from x in xs select x; } }` → **rejects at v2** / **parses at v3**.
- `class C { void M() { var q = from x in xs where x > 0 select x; } }` → **rejects at v2** /
  **parses at v3**.
- At v2 (Cs1+Cs2) there is no `QueryExpression` `Primary` alternative, so `from` is just an
  `IdentifierName`; the `Expression` in `var q = from x in xs select x;` stops at `from` (the next
  token `x` is not a binary operator), and the statement then expects `;` but finds `x` → REJECT.
- Contextual keywords as identifiers stay valid: `class C { int from; }` and `class C { int select; }`
  **parse at v1** (and v3) — the query keywords were NOT added to `ReservedKeyword`.
- All pre-existing Cs1/Cs2/Cs6/Cs11 tests stay green (no Cs1/Cs2 modification).

## Tests
`Tests/CSharpGrammarTests/Cs3QueryExpressionTests.cs` (CRLF + UTF-8 BOM) — **26 tests, all green**.
- POSITIVE (v3, required forms, 10): `Query_FromSelect_Succeeds` (`from x in xs select x`),
  `Query_FromWhereSelect_Succeeds` (`where x > 0`), `Query_FromLetSelect_Succeeds` (`let y = x * 2`),
  `Query_MultipleFrom_Succeeds` (`from x in xs from y in ys`), `Query_FromJoinSelect_Succeeds`
  (`join y in ys on x.Key equals y.Key`), `Query_FromJoinIntoSelect_Succeeds` (`… into z`),
  `Query_FromGroupBySelect_Succeeds` (`group x by x.Key select x`), `Query_FromOrderByDescendingSelect_Succeeds`
  (`orderby x descending`), `Query_FromOrderByMultipleAscending_Succeeds` (`orderby x, x.Name ascending`),
  `Query_FromSelectAnonymousType_Succeeds` (`select new { x, Name = x.Name }`).
- POSITIVE (v3, Roslyn-derived, 5): `Roslyn_TestFromSelect_Succeeds` (ExpressionParsingTests.cs:2301,
  `from a in A select b`), `Roslyn_TestFromWithType_Succeeds` (:2334, `from T a in A select b`),
  `Roslyn_TestFromSelectIntoSelect_Succeeds` (:2367, `from a in A select b into c select d`),
  `Roslyn_TestFromGroupByIntoSelect_Succeeds` (:2777, `from a in A group b by c into d select e`),
  `Roslyn_TestFromJoinWithTypeSelect_Succeeds` (:2886, `from Ta a in A join Tb b in B on a equals b
  select c`).
- POSITIVE (v1/v2/v3, contextual keywords as identifiers, 4): `Contextual_From_AsFieldName_ParsesAtV1_Succeeds`
  (`class C { int from; }` at v1), `Contextual_Select_AsFieldName_ParsesAtV1_Succeeds` (`class C { int
  select; }` at v1), `Contextual_From_AsFieldName_ParsesAtV2_Succeeds` (`class C { int from; }` at v2),
  `Contextual_From_AsFieldName_ParsesAtV3_Succeeds` (`class C { int from; }` at v3).
- VERSION-PURITY (v2, 2): `Query_FromSelect_RejectedAtV2`, `Query_FromWhereSelect_RejectedAtV2`.
- NEGATIVE (v3, malformed, 5): `Query_MissingSelect_Rejected` (`from x in xs`),
  `Query_MissingIn_Rejected` (`from x select x`), `Query_MissingSelectExpression_Rejected`
  (`from x in xs select`), `Query_MissingWhereExpression_Rejected` (`from x in xs where select x`),
  `Query_MissingOrdering_Rejected` (`from x in xs orderby select x`).

## Verification
- [x] `dotnet build Nitra.sln --no-incremental` → **success, 0 errors / 0 warnings**.
- [x] `dotnet test Tests/CSharpGrammarTests` (fresh build) → **969 total / 966 passed / 0 failed /
  3 skipped**. Baseline before T3.2.5: 941 total / 938 passed / 3 skipped (per T3.2.4). Delta = **+28**
  (the new `Cs3QueryExpressionTests` has 26 tests; the +2 is a counting artifact in the T3.2.4 doc — all
  tests green). Filtered run of `Cs3QueryExpressionTests` → **26 passed / 0 failed**. All pre-existing
  Cs1 / Cs2 / Cs6 / Cs11 tests remain green.
- [x] `dotnet test Tests/ParserTests` (regression; engine + CsNitra meta-grammar unchanged) →
  **327 total / 325 passed / 0 failed / 2 skipped** — matches baseline.

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs3.grammar` (added the T3.2.5 rules: re-declared `Primary`
  (`QueryExpression`), `FromClause`, `QueryBody`, `QueryBodyClause`, `QueryBodyClauseNoGroup`,
  `WhereClause`, `LetClause`, `SelectClause`, `GroupClause`, `JoinClause`, `JoinInto`, `OrderByClause`,
  `Ordering`, `OrderingDirection`, `QueryContinuation`).
- `Tests/CSharpGrammarTests/Cs3QueryExpressionTests.cs` (new, 25 tests, CRLF + UTF-8 BOM).
- `Tests/CSharpGrammarTests/CSharpGrammarTests.csproj` (removed a pre-staged working-tree `<ItemGroup>`
  with three `<Compile Include>` lines — `Cs3AutoPropertyTests.cs`/`Cs3ExtensionMethodTests.cs`/
  `Cs3QueryExpressionTests.cs` — that caused a `NETSDK1022` duplicate-`Compile`-item build error; the
  .NET SDK auto-includes all `.cs` files, so the explicit includes were duplicates. Same fix as T3.2.1
  and T3.2.2. The csproj now matches HEAD.)
- `docs/CSharpParserPlan-checklist.md` (T3.2.5 `[~]` → `[✅]`).
- `docs/CSharpParserPlan-progressT3.2.5.md` (this file).
- `CSharpTerminals.cs` — **NOT changed** (no new terminals needed; the query keywords are grammar
  literals, and `in` is already a reserved keyword).

## Boundary decisions / deviations
- **`group … select …` is accepted (a superset of Roslyn)**: in Roslyn a `group` clause is a terminal
  that must be followed by `into`; `group x by x.Key select x` is NOT valid C#. But the task REQUIRES
  that form to parse (it is in the task's POSITIVE test list), so the grammar accepts it (group as a
  body clause followed by a select terminal) in addition to the Roslyn form (group as a terminal
  followed by an into continuation). See the "group clause has two roles" finding.
- **The group expression is REQUIRED** (Roslyn `ParseGroupClause` uses `ParseExpressionCore`, not
  optional), contradicting the task's assumption that `group by key` (without the element) is valid.
- **The optional query-variable type** (`from T a in A`, `join Tb b in B on …`) IS supported (Roslyn
  forms, tests 2334/2886), via a named union (typed/untyped) because a mid-sequence `Type?` cannot
  backtrack (see the "optional type" finding).
- **Comma absorption in clause expressions** (documented, not a regression): the TDOPP `Comma` postfix
  (bp 1) is applicable at minPrecedence 0, so a clause expression greedily absorbs a following comma
  (e.g. `orderby x, x.Name ascending` parses the first ordering's expression as `x, x.Name`). This is
  the EXISTING behavior of the `Expression` rule (e.g. `new Foo(1, 2)` already parses `1, 2` as one
  comma expression) and does not cause a parse failure — the whole input is still consumed. Left as-is
  (changing it would require altering the shared TDOPP `Expression`/`Comma` rule, out of scope).
- **CS3 only**: no query variable type parameters, no async queries (CS5), no query modifiers, no
  `group` without `into` (Roslyn rejects it; the task-required `group … select …` is a separate
  documented superset).
