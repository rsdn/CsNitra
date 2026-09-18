# T1.3 progress — the real `#if`/`#elif` condition (TDOPP)

Status: DONE (build clean 0W/0E; sanity check passes on 8 inputs; all 15 existing committed tests
still pass; throwaway sanity test + temp dump deleted before finishing).

## Scope (from plan T1.3)
Replace the `LineEnd` condition placeholder in `If`/`Elif` (introduced in T1.2.2) with a real
TDOPP `Condition` rule implementing the C# `#if`/`#elif` condition grammar:

```
or    := and ( '||' and )*
and   := eq  ( '&&' eq )*
eq    := unary ( ('=='|'!=') unary )*
unary := '!' unary | primary
primary := '(' or ')' | identifier | 'true' | 'false'
```

There is **NO `defined` operator** — a bare identifier IS the "is it defined" test, and
`true`/`false` are just identifiers matched by `Symbol` (the later interpreter resolves them).

Precedence (low→high): `||` < `&&` < `==`/`!=` < `!` < primary.

## Steps
- [x] Read plan (Semantics section), T1.2.2 progress, current grammar/terminals/`Preprocessor.cs`.
- [x] Read the TDOPP + `precedence` syntax reference in `Cs1.grammar` (lines 667-717) and the
      meta-grammar (`CsNitraParser.cs`, `RuleGenerator.cs`, `ParserExtensions.cs`,
      `CsNitraTypeChecker.cs`, `Parser.cs` `BuildTdoppRules`) to confirm the exact syntax and
      that `BuildFromAst` runs the type-checker (so every `: Level` must be declared in a
      `precedence` statement).
- [x] Rewrite `If`/`Elif` to `"if"/"elif" Ws* Condition LineEnd` and append the `precedence`
      statement + `Condition` TDOPP rule + `CondAtom` primary rule.
- [x] Confirmed **no terminal changes** are needed (only existing `Ws`/`Symbol`/`LineEnd` +
      grammar literals are used).
- [x] Build → 0 Warning / 0 Error.
- [x] Throwaway sanity check (8 inputs, tree dump) → deleted.
- [x] Ran the existing committed `CsPreprocessorTests` suite → 15/15 pass (no regression).

## Key design decisions

### `precedence` statement (highest→lowest)
`precedence CondUnary, CondEquality, CondLogicalAnd, CondLogicalOr;`

Verified against `CsNitraTypeChecker.ResolvePrecedenceDependencies`: the first identifier in the
statement gets the HIGHEST binding power (`bp = Count`), decreasing by 1 each entry, last = 1.
So: `CondUnary`(`!`)=4 (tightest), `CondEquality`(`==`/`!=`)=3, `CondLogicalAnd`(`&&`)=2,
`CondLogicalOr`(`||`)=1 (loosest). This matches the required low→high order
`||` < `&&` < `==`/`!=` < `!` < primary (primary is the TDOPP base, no level). The statement is a
top-level `Statement` (meta-grammar `Precedence` alternative), placed right before `Condition`
(mirroring `Cs1.grammar` placing `precedence` directly above `Expression`). It is a single
statement, so no `TryMergePrecedenceList` anchor-merging is involved.

### TDOPP `Condition` rule mirrors the `Cs1.grammar` `Expression` pattern
First alternative = base (no `: Level`); each other alternative = `Name = <operand(s)> <op> ...
: <Level>` (binary) or `Name = "<op>" <operand> : <Level>` (unary/prefix). No `, right`
(preprocessor operators are all left-associative; the plan grammar uses `*` repetition = left).
The `: <Level>` attaches to the LAST operand reference (the `ReqRef`), exactly as in
`Mul = Expression "*" Expression : Multiplicative`. `BuildTdoppRulesInternal` then splits each
alternative: a `Seq` whose first element is a `Ref` to `Condition` (with a `ReqRef` in the rest)
becomes a **postfix** (binary operator, precedence = the `ReqRef`'s level); everything else
(bare `CondAtom`, and the `"!"` prefix) becomes a **prefix**. Verified: `CondEq`/`CondNotEq`/
`CondAnd`/`CondOr` → postfix; `CondPrimary`/`CondNot` → prefix.

### `CondAtom` primary
`CondAtom = | Paren = "(" Ws* Condition Ws* ")" | Symbol;`
`true`/`false` are matched by `Symbol` (an identifier) — no separate `"true"`/`"false"`
alternatives (which would equal-length-conflict with `Symbol`). `Paren` groups a full inner
`Condition`.

## Deviations from the task's suggested grammar (both REQUIRED, both verified)

### 1. Explicit `Ws*` between every condition token
The task's suggested grammar omitted whitespace, e.g. `CondEq = Condition "==" Condition`.
That CANNOT work here: `Preprocessor.BuildParser` constructs `new Parser(NoOpTrivia())`, and
`NoOpTriviaTerminal.TryMatch` returns 0 — i.e. **no automatic trivia/whitespace skipping**.
So in `A == B` the `==` is not adjacent to `A`; a bare `Condition "==" Condition` would stop at
`A` and leave ` == B` to be swallowed by `LineEnd` (condition silently truncated to `A`).

Fix: insert `Ws*` around each operator and inside the parens, consistent with the existing
rules that already do this explicitly (`Define = "define" Ws+ Symbol LineEnd`,
`DirectiveLine = Ws* "#" Directive`):

```
Condition =
    | CondPrimary = CondAtom
    | CondNot     = "!" Ws* Condition : CondUnary
    | CondEq      = Condition Ws* "==" Ws* Condition : CondEquality
    | CondNotEq   = Condition Ws* "!=" Ws* Condition : CondEquality
    | CondAnd     = Condition Ws* "&&" Ws* Condition : CondLogicalAnd
    | CondOr      = Condition Ws* "||" Ws* Condition : CondLogicalOr;

CondAtom =
    | Paren = "(" Ws* Condition Ws* ")"
    | Symbol;
```

The leading `Ws*` of each postfix backtracks correctly when the operator does not follow (PEG
`Seq` is all-or-nothing), so a trailing space before the newline is left for `LineEnd` and the
tiling invariant holds. Verified: `#if A   \n` still tiles (condition=`A`, `LineEnd`=`   \n`).

### 2. The paren alternative is NAMED (`Paren = "(" ...`)
The task suggested a bare `| "(" Condition ")"`. The meta-grammar's `Alternative` rule only
accepts three forms: `| Name = <RuleExpression>` (named), `| <QualifiedIdentifier>` (single
ref), `| <Literal>` (single literal). A bare multi-token sequence alternative is NOT supported —
`| "(" Condition ")"` would be mis-parsed as `| "("` (AnonymousLiteral) leaving `Condition ")"`
dangling and failing the whole `CondAtom` rule. Every sequence alternative in `Cs1.grammar`
is named for the same reason (e.g. `Parens = "(" Expression ")"`, `MemberAccess = "."
Identifier`). So the paren alternative is named `Paren`. (The name is discarded by
`RuleGenerator.GenerateRuleFromStatement` — only the expression is emitted — so it has no
runtime effect beyond satisfying the meta-grammar.)

## Grammar text (final `Preprocessor.grammar`, new/changed parts)

Changed:
```
If = "if" Ws* Condition LineEnd;

Elif = "elif" Ws* Condition LineEnd;
```

Appended (after `KnownKeyword`):
```
precedence CondUnary, CondEquality, CondLogicalAnd, CondLogicalOr;

Condition =
    | CondPrimary = CondAtom
    | CondNot     = "!" Ws* Condition : CondUnary
    | CondEq      = Condition Ws* "==" Ws* Condition : CondEquality
    | CondNotEq   = Condition Ws* "!=" Ws* Condition : CondEquality
    | CondAnd     = Condition Ws* "&&" Ws* Condition : CondLogicalAnd
    | CondOr      = Condition Ws* "||" Ws* Condition : CondLogicalOr;

CondAtom =
    | Paren = "(" Ws* Condition Ws* ")"
    | Symbol;
```

Everything else (`PreprocessorFile`/`Line`/`DirectiveLine`/`Directive`/`Else`/`EndIf`/`Define`/
`Undef`/`Error`/`Warning`/`LineDir`/`Region`/`EndRegion`/`Pragma`/`Nullable`/`Shebang`/
`BadDirective`/`KnownKeyword`) is UNCHANGED.

## Terminal changes
**None.** `PreprocessorTerminals.cs` and `Preprocessor.cs` are untouched. Only existing
`Ws`/`Symbol`/`LineEnd` terminals + grammar literals are used; `BuildTdoppRules()` was already
called by `Preprocessor.BuildParser`.

## Node shapes produced (verified via throwaway tree dump — observed, not guessed)

The `If`/`Elif` `SeqNode` children are: [`"if"`/`"elif"` literal, `Ws*`, **Condition**, `LineEnd`].
The **Condition** child is the TDOPP tree. Note: the primary is represented **directly** as a
`Symbol` or `Paren` node — there is NO `CondAtom`/`CondPrimary` wrapper node (CsNitra returns the
matching alternative's node directly; the `CondAtom`/`CondPrimary` names are transparent). This
is simpler for the later interpreter. Binary/unary op nodes are `SeqNode`s whose `Elements` are
`[left, Ws, <op-literal>, Ws, right]` (binary) or `[<op-literal>, Ws, operand]` (unary); the
op-literal carries an *inferred* Kind (`||`→`OrOr`, `&&`→`AndAnd`, `==`→`DoubleEquals`,
`!=`→`NotEquals`, `!`→`Not`, `(`→`OpenParen`, `)`→`CloseParen`).

Observed shapes (condition child only):
- `A || B && C`  → `CondOr(Symbol A, CondAnd(Symbol B, Symbol C))`   (`&&` tighter than `||`)
- `!A`           → `CondNot(Symbol A)`
- `A == B`       → `CondEq(Symbol A, Symbol B)`
- `A != B`       → `CondNotEq(Symbol A, Symbol B)`
- `(A || B) && C`→ `CondAnd(Paren(CondOr(Symbol A, Symbol B)), Symbol C)`   (parens group)
- `true`         → `Symbol "true"`   (bare identifier; interpreter resolves it)
- `A&&B||C`      → `CondOr(CondAnd(Symbol A, Symbol B), Symbol C)`   (no-space form)
- `!A && B || C == D` → `CondOr(CondAnd(CondNot(Symbol A), Symbol B), CondEq(Symbol C, Symbol D))`

The last case confirms the full precedence chain in one expression: `!` tightest, then `==`,
then `&&`, then `||` loosest, with correct left-associativity.

## Tiling (verified)
Every input: parse **succeeds**, top `PreprocessorFile [0, source.Length)`, `newPos ==
source.Length`, and the line nodes tile contiguously. Within a condition the `Symbol`/`Ws`/
`<op>`/nested nodes tile contiguously too (e.g. `CondOr [11,22)` = `A`[11,12) `Ws`[12,13)
`||`[13,15) `Ws`[15,16) `CondAnd`[16,22)). The trailing `LineEnd` still consumes any trailing
content + the newline, so each directive line tiles exactly.

## Final state
- `Parsers/CSharp/CsPreprocessor/Preprocessor.grammar` — `If`/`Elif` now use `Condition`;
  appended `precedence CondUnary, CondEquality, CondLogicalAnd, CondLogicalOr;` + `Condition`
  (TDOPP, 6 alternatives) + `CondAtom` (2 alternatives). EmbeddedResource.
- `Parsers/CSharp/CsPreprocessor/PreprocessorTerminals.cs` — **unchanged**.
- `Parsers/CSharp/CsPreprocessor/Preprocessor.cs` — **unchanged** (still `NoOpTrivia` +
  `BuildFromAst` + `BuildTdoppRules()`).

## Verify
- Build: `dotnet build Parsers/CSharp/CsPreprocessor/CsPreprocessor.csproj` → **0 Warning / 0 Error**.
- Existing committed suite: `dotnet test Tests/CsPreprocessorTests/CsPreprocessorTests.csproj`
  → **Total: 15, Passed: 15, Failed: 0** (no regression from the grammar change).
- Sanity check (throwaway `T13SanityCheck` + temp dump, both deleted after running): the 8 inputs
  above all **parse**, tile `[0, len)`, and produce the condition shapes listed.

## Open questions / notes for the interpreter (T2.x)
- The condition primary is a bare `Symbol`/`Paren` node (no `CondAtom` wrapper). The interpreter
  should key the condition off the `If`/`Elif` `SeqNode`'s Condition child and dispatch on its
  Kind: `Symbol` (defined-test / true / false), `Paren` (recurse into inner Condition),
  `CondNot` (negate operand), `CondEq`/`CondNotEq`/`CondAnd`/`CondOr` (left = first non-`Ws`
  element, right = last element, op = the op literal between them).
- Malformed conditions (e.g. `#if` with no condition, `#if A =` dangling operator) are now parse
  errors (they were silently accepted as empty `LineEnd` conditions before T1.3). Handling them is
  error-recovery scope (T4.x), out of scope here.
- `true`/`false` are `Symbol` nodes; the interpreter resolves them as boolean literals.

## Tests (T1.3)

File added: `Tests/CsPreprocessorTests/PreprocessorConditionTests.cs` (`[TestClass]`, `sealed`).
Each test builds a fresh parser via `Preprocessor.BuildParser()` (per-test, stateless/thread-safe).

### Tree-walking helpers (confirmed against the real parse tree, not assumed)
- `ParseTop(source)` — parse `PreprocessorFile`; asserts `top.Kind == "PreprocessorFile"`,
  `top == [0, source.Length)`, `newPos == source.Length`, and that the top-level lines tile
  contiguously (each `lines[i].StartPos == lines[i-1].EndPos`, last `EndPos == source.Length`).
- `GetIfNode(top)` — the `SeqNode Kind=If` that is a direct child of a `DirectiveLine`.
- `GetCondition(ifNode)` — the `If` `SeqNode`'s direct children are
  `[If-literal, Ws(SeqNode), Condition, LineEnd]`; the condition is the unique child whose
  `Kind` is **not** in `{If, Ws, LineEnd}`. (The `Ws*` after `if` is a `SeqNode Kind=Ws`; the
  `if` keyword is a `TerminalNode Kind=If`.)
- `Describe(node, source)` — recursive shape string:
  - `TerminalNode Kind=Symbol` → its text (the identifier / `true` / `false`);
  - `SeqNode Kind=Paren` → `Paren(<inner>)` where inner is the unique child not
    `OpenParen`/`CloseParen` (Ws filtered out);
  - `SeqNode Kind=CondNot` → `CondNot(<last non-Ws child>)`;
  - `SeqNode Kind=CondAnd|CondOr|CondEq|CondNotEq` → `<Kind>(<first non-Ws>, <last non-Ws>)`.

The primary appears **directly** as `Symbol`/`Paren` (no `CondAtom`/`CondPrimary` wrapper), as
noted in the implementation section — the helpers match `Symbol` by its text and `Paren` by Kind.

### Tests + assertions
1. `SingleIdentifier_IsASymbolWithNoOperatorNode` — `#if FOO`: condition is a
   `TerminalNode Kind=Symbol` with text `FOO`; `Describe == "FOO"` (no operator node).
2. `UnaryNot_IsCondNot` — `#if !A`: `Describe == "CondNot(A)"`.
3. `And_IsCondAnd` — `#if A && B`: `Describe == "CondAnd(A, B)"`.
4. `Or_IsCondOr` — `#if A || B`: `Describe == "CondOr(A, B)"`.
5. `And_BindsTighterThanOr` — `#if A || B && C`: `Describe == "CondOr(A, CondAnd(B, C))"`
   (NOT `CondOr(CondAnd(A,B), C)`).
6. `Or_GroupsLoosely` — `#if A && B || C`: `Describe == "CondOr(CondAnd(A, B), C)"`.
7. `Equality_IsCondEq` / `Inequality_IsCondNotEq` — `#if A == B` → `CondEq(A, B)`;
   `#if A != B` → `CondNotEq(A, B)`.
8. `Parens_OverridePrecedence` — `#if (A || B) && C`: `Describe == "CondAnd(Paren(CondOr(A, B)), C)"`.
9. `FullChain_CorrectPrecedenceAndAssociativity` — `#if !A && B || C == D`:
   `Describe == "CondOr(CondAnd(CondNot(A), B), CondEq(C, D))"`.
10. `True_IsASymbol` — `#if true`: condition is a `TerminalNode Kind=Symbol` with text `true`
    (no special node).
11. `WhitespaceIsInsensitive_ProducesTheSameShape` — `#if A&&B` and `#if A && B` both
    `Describe == "CondAnd(A, B)"` and are equal to each other.
12. `Tiling_HoldsForAllConditions` — for every condition in the test set, source
    `int a;\n#if <cond>\nint b;\n` parses, `top == [0, source.Length)`, and the 3 lines tile
    contiguously (asserted inside `ParseTop`; the test also asserts exactly 3 lines).

### Result
- `dotnet test Tests/CsPreprocessorTests/CsPreprocessorTests.csproj` →
  **Total: 28, Passed: 28, Failed: 0** (15 pre-existing + 13 new). The 13 new
  `PreprocessorConditionTests` all pass in isolation as well.
- **No production bug found.** All 13 tests pass against the current grammar; the TDOPP
  `Condition` produces the expected operator Kinds, precedence (`||` < `&&` < `==`/`!=` < `!`
  < primary), left-associativity, paren grouping, direct `Symbol`/`Paren` primaries, and the
  tiling invariant holds for every input.
