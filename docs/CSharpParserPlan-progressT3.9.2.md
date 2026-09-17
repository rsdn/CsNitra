# T3.9.2 — C# 9.0: `with` expression (record with expression)

Status: IN PROGRESS

## Task
Extend the EXPRESSION syntax with the `with` expression (record with expression, C# 9.0).
CS9 only: `CreateParser(8)` must REJECT the `with` expression; `CreateParser(9)` must accept it.

```csharp
var p2 = p with { X = 10 };
var p3 = new Point(1, 2) with { Y = 20 };
```

## Roslyn syntax found (C:\RSDN\roslyn, main, src/Compilers\CSharp\Portable)

File: `Parser/LanguageParser.cs`
- `ParseWithExpression` (13458-13479): `openBrace = EatToken(OpenBraceToken)`; then
  `ParseCommaSeparatedSyntaxList(..., allowTrailingSeparator: true, requireOneElement: false)` where each
  element is `ParseExpressionCore()`. Returns
  `WithExpression(receiverExpression, withKeyword, InitializerExpression(WithInitializerExpression, openBrace, list, closeBrace))`.
  So the form is `<expr> with { <expression-list> }`; the body is a `WithInitializerExpression` (an
  `InitializerExpressionSyntax`) whose elements are full expressions.
- `GetOperatorExpressionKind` (11785-11786): `token1Kind == WithKeyword && PeekToken(1).Kind ==
  OpenBraceToken` -> `SyntaxKind.WithExpression`. The `with` operator is recognized only when a `{`
  follows the `with` keyword.
- The `with` expression is dispatched in the binary-expression path (11614-11615):
  `if (operatorExpressionKind == SyntaxKind.WithExpression) return ParseWithExpression(leftOperand, operatorToken);`
  (the receiver is the left sub-expression).

File: `Generated/CSharpSyntaxGenerator/CSharpSyntaxGenerator.SourceGenerator/Syntax.xml.Internal.Generated.cs`
- `WithExpressionSyntax` (5843-5847): `ExpressionSyntax expression` (the receiver), `SyntaxToken
  withKeyword`, `InitializerExpressionSyntax initializer`.

Version gate: the `with` expression is a BINDER/semantic feature (records, C# 9.0); the PARSER accepts
the form wherever the rule exists. Version purity comes from the rule being Cs9-only.

## The rule (Cs9.grammar)
```
PostfixOp =
    | WithExpr = "with" Initializer;
```
Design notes:
- The `with` expression is a POSTFIX expression: a record (primary) expression followed by `with { ... }`.
  Modeled as a `PostfixOp` alternative (re-declared, T0.3 append-merge), so it is applied inside
  `PrimaryExpr = Primary PostfixOp*` (Cs1.grammar:637).
- Reuses the Cs3 `Initializer` (Cs3.grammar:103, `"{" (InitializerElement; ",")* "}"`) for the `{ ... }`
  body. `InitializerElement` (Cs3.grammar:125-127) handles both a member assignment (`X = 10`,
  `MemberAssignment`) and a plain expression (`NotMemberAssignment`) — exactly the `with` initializer
  element forms.
- `with` is a CONTEXTUAL keyword (NOT in the Cs1 ReservedKeyword, Cs1.grammar:537-618), so it is matched
  here as a plain literal; it remains a valid identifier name elsewhere (consistent with `record`, T3.9.1).
- Boundary: the reused `Initializer` uses the default (Forbidden) trailing-separator behavior, so a
  trailing comma (`p with { X = 10, }`) is rejected — a minor deviation from Roslyn
  (`allowTrailingSeparator: true`), acceptable for the minimal task.

## Code iterations
- (pending)

## Version-purity results
- (pending)

## Tests
- (pending)

## Verification
- (pending)

## Files changed
- `Parsers/CSharp/CSharpGrammar/Cs9.grammar`
- `Tests/CSharpGrammarTests/Cs9WithExpressionTests.cs` (new)
- `docs/CSharpParserPlan-progressT3.9.2.md` (this file)
