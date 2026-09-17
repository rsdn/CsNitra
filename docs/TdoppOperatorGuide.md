# TDOPP Operator Guide (CsNitra)

A practical, short reference for adding operators to the CsNitra grammar. Read this before touching a `Cs*.grammar` `Expression` rule.

## 1. TL;DR

TDOPP operators are declared as **alternatives of one left-recursive rule** (for C#: `Expression`, Cs1.grammar:633). The engine (`BuildTdoppRulesInternal`, Parser.cs:123-162) classifies each alternative by its **first element**:

| First element of the alternative | Kind | Shape |
|---|---|---|
| a **literal** (any non-self-Ref) | **PREFIX** (unary) | `OpName = "lit" Expression : Level` |
| a **self-Ref** to the same rule (`Expression`) | **BINARY** (a.k.a. "postfix") | `OpName = Expression : L "lit" Expression : R` |

- **Prefix** starts an expression (`-x`, `await x`, `^i`).
- **Binary** has a left operand already (`x + y`, `x is T`, `x switch {…}`). In TDOPP a binary operator is the "postfix" — it is applied *after* the left operand.
- Precedence = the `: Level` on the operand (§4). Higher binding power = binds tighter.

> A C-style postfix (`x++`, `x[i]`, `x.M`) is **not** a TDOPP level — it is the `PostfixOp` loop in `PrimaryExpr = Primary PostfixOp*` (Cs1.grammar:634, 730-735). Do not model `x++` as a TDOPP operator.

## 2. Add a PREFIX operator

Append to the version `.grammar` that re-declares `Expression` (T0.3 merge):

```
Expression =
    | MyOp = "literal" Expression : Unary;
```

- First element is the **literal** → classified as a prefix (Parser.cs:148-149).
- Operand is `Expression : Unary` (a `ReqRef`, RuleGenerator.cs:90-94). `: Unary` (bp 15) binds tighter than every binary operator and excludes the Comma (bp 1) (§5). Use `: Unary` for prefix operands.

Real examples:
- Cs5 `await`: `AwaitExpr = "await" Expression : Unary` (Cs5.grammar:74)
- Cs7 `ref`: `RefExpr = "ref" Expression : Unary` (Cs7.grammar:295); `throw`: `ThrowExpression = "throw" Expression : Unary` (Cs7.grammar:362)
- Cs8 `^`: `IndexExpr = "^" Expression : Unary` (Cs8.grammar:156)
- Cs1 unary: `UnaryPlus = "+" Expression : Unary` (Cs1.grammar:635) … `PreDec = "--" Expression : Unary` (Cs1.grammar:640); cast `CastExpr = "(" Type ")" Expression : Cast` (Cs1.grammar:648)

## 3. Add a BINARY (postfix) operator

```
Expression =
    | MyOp = Expression : L "literal" Expression : R;
```

- First element is a **self-Ref to `Expression`** → the builder strips it and keeps the rest as a postfix (Parser.cs:137-141).
- The operator's **precedence** = the `: Level` on the **right** operand (the `ReqRef` in `rest`, Parser.cs:139-141). The left operand is the already-parsed prefix (`currentResult`); it is not re-parsed.

Real examples (Cs1.grammar): `Mul = Expression "*" Expression : Multiplicative` (649), `Add = Expression "+" Expression : Additive` (652), `Less = Expression "<" Expression : Relational` (654), `Equal = Expression "==" Expression : Equality` (664), `And = Expression "&&" Expression : CondAnd` (669), `Assign = Expression "=" Expression : Assignment, right` (672 — `, right` = right-associative, RuleGenerator.cs:94).

**RHS that is not an Expression** (e.g. a `Type`): put the `: Level` on the **first** element; the builder falls back to it (Parser.cs:144-145).
- `TypeIs = Expression : Relational "is" Type` (Cs1.grammar:662) — precedence from the first element; the RHS `Type` is a plain `Ref` parsed at bp 0.
- Cs8 switch: `SwitchExpression = Expression : Unary "switch" "{" … "}"` (Cs8.grammar:35) — a postfix at the Unary level.

## 4. The precedence list

Cs1.grammar:623-626 (merged with the Type list at 631):

```
precedence
    Cast, Unary, Multiplicative, Additive, Relational, Equality,
    LogicalAnd, LogicalXor, LogicalOr, CondAnd, CondOr,
    Conditional, Assignment, Comma;
precedence TypeArray, TypePointer, Comma;   // merges before Comma (shared anchor)
```

Binding powers come from the **merged** list: **first name = highest**, last = 1 (CsNitraTypeChecker.cs:56-64; merge logic 67-122). Final order / binding power:

| Level | bp | Used by |
|---|---|---|
| Cast | 16 | `(T) x` |
| Unary | 15 | `-x`, `await`, `ref`, `throw`, `^`, `x switch` |
| Multiplicative | 14 | `* / %` |
| Additive | 13 | `+ -` |
| Relational | 12 | `< > <= >= is as` |
| Equality | 11 | `== !=` |
| LogicalAnd | 10 | `&` |
| LogicalXor | 9 | `^` (binary) |
| LogicalOr | 8 | `\|` |
| CondAnd | 7 | `&&` |
| CondOr | 6 | `\|\|` |
| Conditional | 5 | `? :` |
| Assignment | 4 | `=` (right-assoc) |
| TypeArray | 3 | `T[]` (Type only) |
| TypePointer | 2 | `T*` (Type only) |
| Comma | 1 | `,` (lowest) |

**Choosing a level**: match the operator's binding strength — tighter = higher in the list. An operator applies to a left operand when `postfix.Precedence > minPrecedence || (== && right-assoc)` (Parser.cs:360). So `x + y * z` → `*` (14) binds before `+` (13).

To add a **new level**, add another `precedence` statement sharing an **anchor** name (e.g. ending in `Comma`) and insert the new name at the right position (pattern: Cs1.grammar:631).

## 5. Operand binding (`: Level`)

The `: Level` on an operand is a `ReqRef` whose binding power becomes that operand's **minPrecedence**, controlling which operators it may absorb:

- **`: Unary` (bp 15)** — a *tight* operand: primary + prefix + cast only. No binary operator (all bp ≤ 14) applies, and the Comma (bp 1) does not. Use for **prefix-operator operands** so `await x + y` = `(await x) + y`. Also stops before a `,`.
- **`: Comma` (bp 1)** — a *full* binary operand, but the Comma operator (bp 1) is excluded, so it stops before a `,`. Use for operands in **comma-separated contexts** (argument lists, tuple elements, parameter defaults, switch arms) so `f(a + b, c)` does not let the first arg swallow `, c`.

Both exclude the Comma (the T3.3.2 fix); they differ in how much binary expression they allow.

Real examples:
- `: Unary` — Cs5 await (Cs5.grammar:74), Cs7 ref/throw (Cs7.grammar:295/362), Cs8 `^` (Cs8.grammar:156).
- `: Comma` — Cs4 `Argument = … Expression : Comma` (Cs4.grammar:127-128, rationale 114-124), Cs7 tuple elements (Cs7.grammar:59-60), Cs4 `Parameter` default (Cs4.grammar:201), Cs8 `SwitchArm = Pattern "=>" Expression : Comma` (Cs8.grammar:43).

## 6. Mutual-exclusivity

The engine is **longest-match-wins**: it tries every prefix alternative and keeps the one consuming the most input (Parser.cs:296-316).

- **REQUIRED-new-construct**: a re-declared alternative is reachable only if it requires a construct (a reserved keyword or a distinctive literal) no existing alternative has. Then the two cannot match the same input → no competition. This is how every version file appends to `Expression`/`Statement`.
- **Longest-match-wins**: if two alternatives both match, the longer one wins.
- **Equal-length tie → the FIRST alternative (declaration order) wins** (Parser.cs:296-316; see the T3.3.2 notes). It is **not** an error. So a later-version alternative that ties in length with an earlier one **loses** to the earlier one — make them mutually exclusive instead of relying on length.

> ⚠️ `AGENTS.md` says "equal-length matches → the parser reports an error". That is inaccurate for the current engine: the code and every `Cs*.grammar` header say the first (earlier-declared) alternative wins, with no error. Rely on mutual-exclusivity, not on a tie error.

## 7. Common pitfalls

1. **Binary operator: the first element must be a self-Ref to the SAME rule.** `Primary "op" Expression` is not a self-Ref to `Expression`, so the builder classifies the whole alternative as a **prefix** (Parser.cs:148-149) — tried at the start of an expression, never fires as a binary operator. Use `Expression` as the first element.
2. **No `: Level` anywhere → precedence 0 → never applicable.** If the self-Ref and the RHS are both plain `Ref` (no `ReqRef`), the postfix gets precedence 0 (Parser.cs:144-145); `0 > minPrecedence` is never true (Parser.cs:360), so the operator silently never applies. Always carry a `: Level` (on the RHS, or on the first element for a non-Expression RHS).
3. **`: Comma` for comma-separated operands (T3.3.2).** A plain `Expression` operand in an argument list absorbs the following `,` (the Comma operator, bp 1, applies at minPrecedence 0). Use `Expression : Comma` (or `: Unary`) so the operand stops at the separator (Cs4.grammar:114-128).
4. **Operators with OPTIONAL operands are not standard TDOPP binary operators.** A TDOPP binary operator always requires a left operand (the prefix) and a right operand (the `ReqRef`). A range `E1? ".." E2?` (both optional) does not fit `Expression : L "op" Expression : R`. Model such constructs as a **Primary** (a self-contained unit) or as a combination of prefix/postfix/binary alternatives — not as one binary operator. (This is why the C# `..` range, T3.8.3b, is not a plain TDOPP binary op.)

## 8. Worked example — add the Cs8 `^` (index-from-end) prefix operator

Goal: `x[^1]` — a `^` prefix operator used as an indexer argument.

1. **Type**: `^` starts an expression (no left operand) → **prefix**. (The `^` binary XOR already exists as a postfix, Cs1.grammar:667 — different phase, no conflict.)
2. **Level**: operand should bind tight, like every unary → `: Unary`.
3. **Mutual exclusivity**: no existing `Expression` prefix starts with the `^` *symbol* (the XOR `^` is a postfix, tried only after a left operand). `^1`: `PrimaryExpr` fails (no Primary matches `^`), `IndexExpr` → `^ 1` (sole match). `x ^ y`: `PrimaryExpr` → `x`; `IndexExpr` fails (starts with `x`); the `BitXor` postfix applies → `x ^ y`. No tie.
4. **Append** to `Cs8.grammar` (re-declares `Expression`):
   ```
   Expression =
       | IndexExpr = "^" Expression : Unary;
   ```
   (Cs8.grammar:155-156)
5. **Result**: `^ x + y` = `(^ x) + y` (Unary 15 > Additive 13); in `x[^i, ^j]` each `^` operand stops before the `,` (Comma bp 1 excluded).
