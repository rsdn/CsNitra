# T3.4 — C# 5.0 `async`/`await`

Status: done.

## Goal
Add C# 5.0 `async`/`await` to a NEW grammar file `Parsers/CSharp/CSharpGrammar/Cs5.grammar`:
- the `async` METHOD MODIFIER (`async void M() { }`, `async Task M() { }`);
- the `await` UNARY PREFIX expression (`await M();`, `await Task.Delay(100);`, `var x = await M();`).

CS5 only. Both `async` and `await` are CONTEXTUAL keywords (plain identifiers in C# 1.0–4.0, keywords
in C# 5.0+).

## Baseline (before changes)
- `dotnet build Nitra.sln --no-incremental` → success, 0 errors / 0 warnings.
- `dotnet test Tests/CSharpGrammarTests` → **1007 total / 1004 passed / 0 failed / 3 skipped**.
- `dotnet test Tests/ParserTests` (regression) → **327 total / 325 passed / 0 failed / 2 skipped**.

## Cs1 MethodModifier / Expression / Primary / unary-prefix-operator structure (found)
- **`MethodModifier`** (Cs1.grammar:288-300) is a NAMED UNION of the C# 1.0 method modifiers
  (`public`/`private`/`protected`/`internal`/`static`/`virtual`/`override`/`abstract`/`new`/`extern`/
  `sealed`/`unsafe`). `async` is NOT in it (Cs1 comment at :287 documents the exclusion: "async (CS5)").
  The Cs1 `Method` rule (Cs1.grammar:265) is `Attributes? MethodModifier* Type TypeName "("
  ParameterList? ")" MethodBody`, so it consumes the merged `MethodModifier*`.
- **`Expression`** (Cs1.grammar:633-673) is a TDOPP rule. Its PREFIX alternatives (a `Seq` that does
  NOT start with a self-`Ref` to `Expression` — `BuildTdoppRulesInternal`, Parser.cs:137-149) are:
  `PrimaryExpr = Primary PostfixOp*` (634), then the UNARY PREFIX operators
  `UnaryPlus = "+" Expression : Unary` (635), `UnaryMinus = "-" Expression : Unary` (636),
  `UnaryNot = "!" Expression : Unary` (637), `UnaryBitNot = "~" Expression : Unary` (638),
  `PreInc = "++" Expression : Unary` (639), `PreDec = "--" Expression : Unary` (640), and
  `CastExpr = "(" Type ")" Expression : Cast` (648). Its POSTFIX (binary) alternatives all start with a
  self-`Ref` to `Expression` (`Mul = Expression "*" Expression : Multiplicative`, …, `Comma =
  Expression "," Expression : Comma`).
- **`Primary`** (Cs1.grammar:675-720) includes `IdentifierName = !ReservedKeyword Identifier` (682) —
  so a non-reserved word (like `async`/`await`) is a valid `Primary` (a plain identifier).
- **TDOPP precedence list** (Cs1.grammar:623-626, merged with `TypeArray, TypePointer, Comma` at 631):
  `Cast, Unary, Multiplicative, Additive, Relational, Equality, LogicalAnd, LogicalXor, LogicalOr,
  CondAnd, CondOr, Conditional, Assignment, Comma` (16 entries after the T1.4 merge; `bp = count -
  index`, so `Cast` is highest, `Comma` is lowest = 1). A prefix operator's operand `Expression : Lvl`
  is a `ReqRef` with `Precedence = Lvl` (RuleGenerator.cs:90-94): minPrecedence = Lvl's binding power,
  so an operator applies iff `bp > minPrecedence || (bp == minPrecedence && right)` (Parser.cs:360).
- **`ReservedKeyword`** (Cs1.grammar:537-618): `async` and `await` are **NOT** in it (verified by grep
  — no `"async"`/`"await"` literal in Cs1.grammar). So both remain valid `IdentifierName`s at every
  version (the contextual-keyword design).

## The `async` modifier approach (Part 1)
Re-declare `MethodModifier` in Cs5 to **APPEND** `"async"` (T0.3 merge — the same pattern as `partial`
in T3.1.3 and `static` in Cs2). The Cs1 `Method` rule uses the merged `MethodModifier*`, so NO
re-declaration of `Method` is needed. `"async"` is a `WordLiteral` (RuleGenerator.cs:77-79) and matches
whole-word (`async`, not `asyncX`).

```
MethodModifier =
    | "async";
```

Version purity: at v4 `async` is not a method modifier, so `async void M() { }` REJECTS (parsed as a
`Type` name `async`, then `void` (reserved) cannot be the method name — the T2.1.5 finding,
progressT2.1.5.md:96); at v5 the merged modifier accepts `async` and it PARSES. `async` combines with
other modifiers in any order (Roslyn `ParseModifiers` is a generic any-order loop): `public async`,
`static async`.

## The `await` expression approach (Part 2) — TDOPP prefix operator (NOT a Primary)
Chosen: a **TDOPP prefix operator** appended to the Cs1 `Expression` rule, mirroring the Cs1 `-`/`!`/`~`
prefix operators and Roslyn exactly:

```
Expression =
    | AwaitExpr = "await" Expression : Unary;
```

- **Why a TDOPP prefix operator (not a `Primary`)**: Roslyn parses `await` in the prefix-expression
  phase (`parseUnaryOrPrimaryExpression`, LanguageParser.cs:11488-11492) with operand
  `ParseSubExpression(GetPrecedence(SyntaxKind.AwaitExpression))`, and `GetPrecedence` returns
  **`Precedence.Unary`** (LanguageParser.cs:11299-11301) — the SAME level as the Cs1 `-`/`!`/`~`/`++`/
  `--` prefix operators. So `AwaitExpr = "await" Expression : Unary` is a faithful, drop-in analog.
  It is a PREFIX alternative (it starts with the `"await"` literal, not a self-`Ref` to `Expression`),
  so the TDOPP builder classifies it as a prefix (Parser.cs:137-149) and it is tried among the prefix
  alternatives.
- **The TDOPP Comma issue (T3.3.2) is handled by the `: Unary` precedence — no separate
  `Expression : Comma` is needed.** The operand `Expression : Unary` sets minPrecedence = Unary's
  binding power (the second-highest). The `Comma` operator (binding power 1, the lowest) is therefore
  NOT applicable to the operand (`1 > UnaryBp` is false), so the operand does NOT absorb a following
  `,` in a comma-separated context (e.g. an argument list `M(await x, y)` → two arguments). Every
  operator at a higher level (Cast) still applies. This is the same minPrecedence mechanism the Cs1
  prefix operators already use.
- **Why NOT a `Primary`**: a `Primary`-level `AwaitExpr = "await" Expression` would have the operand at
  minPrecedence 0, so it WOULD absorb the `Comma` (the T3.3.2 defect), and it would not match Roslyn's
  `Precedence.Unary` placement. The TDOPP prefix form is both more correct and simpler.

## The `await` version-purity analysis (the v4 behavior)
`await` is NOT reserved (it is never added to `ReservedKeyword`), so it remains a valid `IdentifierName`
at every version. The `AwaitExpr` alternative is present only at v5+. Consequences:
- **v5 (Cs1+…+Cs5)**: `await M()` → the `AwaitExpr` prefix matches `await M()` (longer than the bare
  `await` `PrimaryExpr`) → an **await expression**. Bare `await` (no operand) → `AwaitExpr` fails,
  `PrimaryExpr` → a plain identifier.
- **v4 (Cs1+Cs2+Cs3+Cs4, no CS5)**: `AwaitExpr` is ABSENT. `await` is a plain name:
  - a **bare `await x;`** parses as a **local declaration of type `await` named `x`** (valid C# 1.0 —
    the T2.1.5 D2 finding, progressT2.1.5.md:97,162-166; the non-strict version-purity analogous to
    `var x = 1;` at v1 / `dynamic x = 1;` at v3). This PARSES at v4 (documented, not a reject).
  - **`await N();`** (the await form with an invocation operand) **REJECTS** at v4: with `AwaitExpr`
    absent, `await` is read as a name — as a local-declaration type (`await N`, then fails on the `()`)
    or as a bare identifier expression (`await`, then fails on the following `N`). The `()` forces the
    expression reading (the same role `return` plays in T2.1.5's `return await x;`). This is the clean
    v4-vs-v5 discriminator for the await EXPRESSION.
- The task's premise that `await M()` "likely parses as a member-access expression at v4" is NOT
  accurate for the spaced form `await M()` (two identifiers in a row are not an expression); the spaced
  form REJECTS at v4, while a bare `await x;` parses as a local declaration. (A dotted `await.M()`
  would parse as a member-access invocation at BOTH v4 and v5 — not a discriminator.) Documented as the
  actual behavior.

## Mutual-exclusivity hand-traces (no equal-length tie)
`await` (TDOPP prefix, v5):
- `await M()`: `PrimaryExpr` (IdentifierName) → `await` (5 chars); `AwaitExpr` → `await M()` (9 chars).
  Longest-match → **AwaitExpr**. No tie.
- bare `await` (e.g. `await;`): `AwaitExpr` fails (no operand); `PrimaryExpr` → `await` (5 chars).
  **PrimaryExpr only**. No tie.
- `await x` (single-identifier operand): `PrimaryExpr` → `await` (5); `AwaitExpr` → `await x` (7).
  Longest-match → **AwaitExpr**. No tie.
- `await x * y`: `AwaitExpr` operand `Expression : Unary` = `x` (the `*`/Additive is below the Unary
  level, so it is NOT absorbed); the outer `* y` is a binary postfix on `(await x)`. → `(await x) * y`.
- `M(await x, y)` (argument list): the argument `Expression` = `AwaitExpr`; its operand `x` does NOT
  absorb the `,` (Comma bp 1 < Unary bp); the separator `,` is consumed by the argument list. → two
  arguments. (No T3.3.2-style comma collapse.)

`async` (method modifier, v5):
- `async void M() { }`: `MethodModifier*` = `async`, `Type` = `void`, `TypeName` = `M`, `()`, `MethodBody
  = { }` → **match** via the Cs1 `Method` alternative.
- `public async void M() { }` / `static async Task M() { }`: `MethodModifier*` = `public async` /
  `static async` (any order), then the Cs1 `Method` body → **match**.
- `class C { int async; }` (field NAME, v1–v5): `async`/`await` are not reserved → a valid
  `VariableDeclarator` identifier → **match** at every version (must stay green).
- `class C { void M() { async N(); } }` (async in an EXPRESSION context, v5): `async` is a valid
  `IdentifierName`, but `async N()` is two identifiers in a row with no operator → not a valid
  `Expression` → **reject**. (`async` is only a method modifier, never an expression operator.)

## Roslyn references (C:\RSDN\roslyn, main)
- **`async` modifier**:
  - Contextual keyword: `GetModifierExcludingScoped` (Portable/Parser/LanguageParser.cs:1329-1330) maps
    `IdentifierToken` + `ContextualKind == AsyncKeyword` → `DeclarationModifiers.Async` in the generic
    `ParseModifiers` loop (1347). `ReconsiderTypeAsAsyncModifier` (LanguageParser.cs:3406-3427) re-reads
    a type-position `async` as a modifier.
  - CS5 gate: `IDS_FeatureAsync` (Portable/Errors/MessageID.cs:95) requires `LanguageVersion.CSharp5`
    (MessageID.cs:701); the check is a BINDER concern (`Symbols/Source/ModifierUtils.cs:116`) — the
    PARSER accepts the form (parser-vs-binder split).
  - Syntax tests: `AsyncParsingTests.cs:38` (`SimpleAsyncMethod`, `async void M() { }`),
    `AsyncParsingTests.cs:283` (`MethodAsyncVarAsync`, `static async void M(object async) { async.F();
    }`).
- **`await` expression**:
  - Contextual keyword + unary prefix: `IsAwaitExpression` (LanguageParser.cs:11376-11427) disambiguates
    it; `parseUnaryOrPrimaryExpression` (LanguageParser.cs:11488-11492) builds an `AwaitExpression` with
    operand `ParseSubExpression(GetPrecedence(SyntaxKind.AwaitExpression))`; `GetPrecedence`
    (LanguageParser.cs:11299-11301) returns `Precedence.Unary` — the SAME level as the Cs1 `-`/`!`/`~`
    prefix operators. The version gate is a BINDER concern (`Binder/Binder_Await.cs:22`).
  - Syntax tests: `ExpressionParsingTests.cs:3214` (`await Task.Delay()` — an `AwaitExpression` whose
    operand is an `InvocationExpression` on the member-access `Task.Delay`), `AwaitParsingTests.cs:33`.

## Cs5.grammar rules
- `MethodModifier = | "async";` (append — the CS5 method modifier).
- `Expression = | AwaitExpr = "await" Expression : Unary;` (append — the CS5 await prefix operator).

## Setup steps
- [x] Create `Parsers/CSharp/CSharpGrammar/Cs5.grammar` (CRLF, no BOM).
- [x] Add `<EmbeddedResource Include="Cs5.grammar" />` to `CSharpGrammar.csproj` (between Cs4 and Cs6).
- [x] Add version-table entry `new(5, "Cs5.grammar", "Cs5.grammar")` to `EmbeddedGrammar.cs` (ascending).
- [x] Add `Tests/CSharpGrammarTests/Cs5AsyncAwaitTests.cs` (CRLF + UTF-8 BOM).
- [x] Update `CSharpVersionInfrastructureTests.cs` count/order tests for the new Cs5 (v6: 5→6, v11: 6→7,
  with `Cs5.grammar` at index 4; added a `LoadGrammarUpTo_5_...` test).

## Version-purity results
- `class C { async void M() { } }` → **rejects at v4** (async not a method modifier) / **parses at v5**. ✓
- `class C { async Task M() { await N(); } }` → **parses at v5** (async modifier + await expression). ✓
- `class C { void M() { await x; } }` (bare) → **parses at v4** (local declaration of type `await` named
  `x`) — non-strict version-purity (documented). ✓
- `class C { void M() { await N(); } }` → **rejects at v4** (the await EXPRESSION is CS5-only) /
  **parses at v5**. ✓
- `class C { int async; }` / `class C { int await; }` → **parse at v1 and v4** (and v5) — `async`/
  `await` are contextual keywords (valid names at every version). ✓
- All pre-existing Cs1/Cs2/Cs3/Cs4/Cs6/Cs11 tests stay green (no Cs1/Cs2/Cs3/Cs4 modification).

## Tests
`Tests/CSharpGrammarTests/Cs5AsyncAwaitTests.cs` (CRLF + UTF-8 BOM) — **17 tests, all green**.
- POSITIVE (v5, async modifier, 6): `AsyncVoidMethod_Succeeds` (`async void M() { }`,
  AsyncParsingTests.cs:38), `AsyncTaskMethod_Await_Succeeds` (`async Task M() { await N(); }`),
  `AsyncVoidMethod_VarAwait_Succeeds` (`async void M() { var x = await N(); }`),
  `AsyncTaskMethod_AwaitTaskDelay_Succeeds` (`async Task M() { await Task.Delay(100); }`,
  ExpressionParsingTests.cs:3214), `PublicAsyncVoidMethod_Succeeds` (`public async void M() { }`),
  `StaticAsyncTaskMethod_Succeeds` (`static async Task M() { }`).
- POSITIVE (v5, Roslyn-derived, 2): `AsyncParamName_MemberAccess_Roslyn_Succeeds`
  (`static async void M(object async) { async.F(); }`, AsyncParsingTests.cs:283 — `async` as modifier +
  parameter name + member-access base), `Await_ExpressionBodiedReturn_Succeeds`
  (`async Task M() { return await N(); }` — the await expression is a full `Expression`).
- POSITIVE (v1–v4, contextual names, 4): `Async_AsFieldName_ParsesAtV1` / `Async_AsFieldName_ParsesAtV4`
  (`class C { int async; }`), `Await_AsFieldName_ParsesAtV1` / `Await_AsFieldName_ParsesAtV4`
  (`class C { int await; }`).
- NEGATIVE (v4, version-purity, 2): `AsyncVoidMethod_RejectedAtV4` (`async void M() { }`),
  `AwaitInvocation_RejectedAtV4` (`class C { void M() { await N(); } }`).
- DOCUMENT (v4, non-strict, 1): `Await_AsLocalDeclaration_ParsesAtV4` (`class C { void M() { await x; }
  }` — parses as a local declaration of type `await` named `x`).
- NEGATIVE/DOCUMENT (v5, malformed, 2): `Await_NoOperand_ParsesAsBareIdentifierAtV5`
  (`class C { async void M() { await; } }` — PARSES as a bare identifier `await` + `;`; a documented
  deviation from Roslyn, where `await;` in an async method is a parse error, because our `await` is not
  reserved), `AsyncInExpressionContext_RejectedAtV5` (`class C { void M() { async N(); } }` — REJECTS).

## Verification
- [x] `dotnet build Nitra.sln --no-incremental` → **success, 0 errors / 0 warnings**.
- [x] `dotnet test Tests/CSharpGrammarTests` (fresh build) → **1025 total / 1022 passed / 0 failed /
  3 skipped**. Baseline before T3.4: 1007 total / 1004 passed / 3 skipped. Delta = **+18** (17 new
  `Cs5AsyncAwaitTests` + 1 new `CSharpVersionInfrastructureTests.LoadGrammarUpTo_5_...`). All
  pre-existing Cs1 / Cs2 / Cs3 / Cs4 / Cs6 / Cs11 tests remain green; the 3 updated
  `CSharpVersionInfrastructureTests` count/order tests pass against the new version table.
- [x] `dotnet test Tests/ParserTests` (regression; engine + CsNitra meta-grammar unchanged) →
  **327 total / 325 passed / 0 failed / 2 skipped** — matches baseline.

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs5.grammar` (new — `async` method modifier + `await` prefix operator).
- `Parsers/CSharp/CSharpGrammar/CSharpGrammar.csproj` (added `<EmbeddedResource Include="Cs5.grammar" />`).
- `Tests/CSharpGrammarTests/EmbeddedGrammar.cs` (version-table entry for version 5).
- `Tests/CSharpGrammarTests/Cs5AsyncAwaitTests.cs` (new — 17 tests).
- `Tests/CSharpGrammarTests/CSharpVersionInfrastructureTests.cs` (3 count/order tests updated for Cs5 +
  1 new `LoadGrammarUpTo_5_...` test).
- `docs/CSharpParserPlan-checklist.md` (T3.4 `[~]` → `[✅]`).
- `docs/CSharpParserPlan-progressT3.4.md` (this file).

## Boundary decisions / deviations
- **`async` version-purity via `MethodModifier` re-declaration, NOT reservation** (the `partial`
  T3.1.3 / `static` Cs2 precedent): `async` is a contextual keyword (Roslyn `IdentifierToken` +
  `ContextualKind == AsyncKeyword`; LanguageParser.cs:1329-1330), so it is NOT added to
  `ReservedKeyword`. It is APPENDED to the Cs1 `MethodModifier` union (T0.3 merge), so it is a method
  modifier only at v5+. Consequence: at v4 `async` is a plain name (not a modifier), so
  `async void M() { }` REJECTS; at v5 it PARSES. `async`/`await` remain valid names at every version
  (`class C { int async; }` / `class C { int await; }` parse at v1–v5).
- **`await` is a TDOPP prefix operator (`Expression : Unary`), NOT a `Primary`**: it mirrors Roslyn
  exactly (`GetPrecedence(AwaitExpression) = Precedence.Unary`, LanguageParser.cs:11299-11301) and the
  Cs1 `-`/`!`/`~` prefix operators. The `: Unary` precedence sets the operand's minPrecedence to the
  Unary level, which EXCLUDES the `Comma` operator (binding power 1) — so the T3.3.2 comma-absorption
  defect does not apply and no separate `Expression : Comma` is needed. A `Primary`-level `await` would
  have the operand at minPrecedence 0 (absorbing the Comma) and would not match Roslyn's placement.
- **The `await;` (no operand) case PARSES at v5 as a bare identifier** (a documented deviation from
  Roslyn): our `await` is NOT reserved, so it is a valid `IdentifierName` and the `AwaitExpr` alternative
  fails (no operand) → `await;` is an expression statement with the identifier `await`. Roslyn commits
  to the await reading in an async context (`IsAwaitExpression`, LanguageParser.cs:11376: `IsInAsync` →
  always await) and reports a missing-operand parse error; our grammar's contextual-keyword design (the
  `dynamic`/`var`/`partial` precedent) does not. Covered as a POSITIVE documenting the actual behavior.
- **The task's v4 premise for `await M()` is corrected**: the spaced form `await M()` REJECTS at v4
  (two identifiers in a row are not an expression; `AwaitExpr` is absent); a bare `await x;` parses as a
  local declaration of type `await` named `x` (non-strict version-purity, the T2.1.5 D2 finding). The
  clean v4-vs-v5 discriminator for the await EXPRESSION is `await N();` (rejects at v4, parses at v5).
- **Updated `CSharpVersionInfrastructureTests.cs`** (3 count/order tests + 1 new test): adding a version
  to the table necessarily changes the `LoadGrammarUpTo(n)` file count/order; these tests hardcode the
  counts, so they were updated (v6: 5→6, v11: 6→7, with `Cs5.grammar` at index 4). In-scope: a direct,
  necessary consequence of the required version-table entry (the same update the Cs4 task needed).
