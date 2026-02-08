using CsNitra.TypeChecking;

namespace CsNitra.Ast;

internal sealed class AstSimplifier
{
    private readonly List<Diagnostic> _errors = new();

    public IReadOnlyList<Diagnostic> Errors => _errors.AsReadOnly();

    public GrammarAst Transform(GrammarAst ast)
    {
        var newStatements = new StatementAst[ast.Statements.Count];

        for (int i = 0; i < ast.Statements.Count; i++)
            newStatements[i] = TransformStatement(ast.Statements[i]);

        return ast with { Statements = newStatements };
    }

    private StatementAst TransformStatement(StatementAst ast) => ast switch
    {
        RuleStatementAst ruleStmt => TransformRuleStatement(ruleStmt),
        SimpleRuleStatementAst simpleStmt => TransformSimpleRuleStatement(simpleStmt),
        _ => ast
    };

    private RuleStatementAst TransformRuleStatement(RuleStatementAst ast)
    {
        var newAlternatives = new AlternativeAst[ast.Alternatives.Count];

        for (int i = 0; i < ast.Alternatives.Count; i++)
            newAlternatives[i] = TransformAlternative(ast.Alternatives[i]);

        return ast with { Alternatives = newAlternatives };
    }

    private SimpleRuleStatementAst TransformSimpleRuleStatement(SimpleRuleStatementAst ast) =>
        ast with { Expression = TransformExpression(ast.Expression, ast.Name.Value) };

    private AlternativeAst TransformAlternative(AlternativeAst ast) => ast switch
    {
        NamedAlternativeAst namedAlt => TransformNamedAlternative(namedAlt),
        AnonymousAlternativeAst anonAlt => anonAlt,
        _ => ast
    };

    private NamedAlternativeAst TransformNamedAlternative(NamedAlternativeAst ast) =>
        ast with { Expression = TransformExpression(ast.Expression, ast.Name.Value) };

    private RuleExpressionAst TransformExpression(RuleExpressionAst ast, string? kind) => ast switch
    {
        NamedExpressionAst a => TransformNamedExpression(a, kind),
        SequenceExpressionAst a => TransformSequenceExpression(a, kind),
        GroupExpressionAst a => TransformGroupExpression(a, kind),
        OptionalExpressionAst a => TransformOptionalExpression(a, kind),
        OftenMissedExpressionAst a => TransformOftenMissedExpression(a, kind),
        OneOrManyExpressionAst a => TransformOneOrManyExpression(a, kind),
        ZeroOrManyExpressionAst a => TransformZeroOrManyExpression(a, kind),
        AndPredicateExpressionAst a => TransformAndPredicateExpression(a, kind),
        NotPredicateExpressionAst a => TransformNotPredicateExpression(a, kind),
        SeparatedListExpressionAst a => TransformSeparatedListExpression(a, kind),
        RuleRefExpressionAst a => TransformRuleRefExpression(a, kind),
        _ => ast with { Kind = InferKind(ast, kind) }
    };

    private RuleExpressionAst TransformNamedExpression(NamedExpressionAst ast, string? kind)
    {
        if (kind != null)
        {
            _errors.Add(
                new Diagnostic(
                    $"Nested name assignment is not allowed: '{ast.Name.Value}' (nested in '{kind}')",
                    ast.Span,
                    DiagnosticSeverity.Error));
            return ast with { Kind = kind };
        }

        return TransformExpression(ast.Expression, ast.Name.Value);
    }

    private RuleExpressionAst TransformSequenceExpression(SequenceExpressionAst ast, string? kind)
    {
        var elements = FlattenSequence(ast);

        if (elements.Count == 1)
            return TransformExpression(elements[0], kind);

        var transformedElements = new RuleExpressionAst[elements.Count];

        for (int i = 0; i < elements.Count; i++)
            transformedElements[i] = TransformExpression(elements[i], kind: null);

        return new FlattenSequenceExpressionAst(
            Elements: transformedElements,
            StartPos: ast.StartPos,
            EndPos: ast.EndPos,
            Kind: kind
        );
    }

    private List<RuleExpressionAst> FlattenSequence(SequenceExpressionAst ast)
    {
        var result = new List<RuleExpressionAst>();
        collect(ast, result);
        return result;

        static void collect(RuleExpressionAst ast, List<RuleExpressionAst> result)
        {
            if (ast is SequenceExpressionAst seqExpr)
            {
                collect(seqExpr.Left, result);
                collect(seqExpr.Right, result);
            }
            else
                result.Add(ast);
        }
    }

    private RuleExpressionAst TransformGroupExpression(GroupExpressionAst ast, string? kind) => ast with
    {
        Expression = TransformExpression(ast.Expression, kind),
        Kind = InferKind(ast, kind)
    };

    private RuleExpressionAst TransformOptionalExpression(OptionalExpressionAst ast, string? kind) => ast with
    {
        Expression = TransformExpression(ast.Expression, kind: null),
        Kind = InferKind(ast, kind)
    };

    private RuleExpressionAst TransformOftenMissedExpression(OftenMissedExpressionAst ast, string? kind) => ast with
    {
        Expression = TransformExpression(ast.Expression, kind: null),
        Kind = InferKind(ast, kind)
    };

    private RuleExpressionAst TransformOneOrManyExpression(OneOrManyExpressionAst ast, string? kind) => ast with
    {
        Element = TransformExpression(ast.Element, kind: null),
        Kind = InferKind(ast, kind)
    };

    private RuleExpressionAst TransformZeroOrManyExpression(ZeroOrManyExpressionAst ast, string? kind) => ast with
    {
        Element = TransformExpression(ast.Element, kind: null),
        Kind = InferKind(ast, kind)
    };

    private RuleExpressionAst TransformAndPredicateExpression(AndPredicateExpressionAst ast, string? kind) => ast with
    {
        Expression = TransformExpression(ast.Expression, kind: null),
        Kind = InferKind(ast, kind)
    };

    private RuleExpressionAst TransformNotPredicateExpression(NotPredicateExpressionAst ast, string? kind) => ast with
    {
        Expression = TransformExpression(ast.Expression, kind: null),
        Kind = InferKind(ast, kind)
    };

    private RuleExpressionAst TransformSeparatedListExpression(SeparatedListExpressionAst ast, string? kind) => ast with
    {
        Element = TransformExpression(ast.Element, Naming.InferNameForLoop(ast.Element, plural: true, Naming.DefaultGetLiteralAlias)),
        Separator = TransformExpression(ast.Separator, Naming.InferName(ast.Separator, Naming.DefaultGetLiteralAlias)),
        Kind = InferKind(ast, kind)
    };

    private RuleExpressionAst TransformRuleRefExpression(RuleRefExpressionAst ast, string? kind) =>
        ast with { Kind = kind ?? Naming.InferName(ast) };

    private string? InferKind(CsNitraAst ast, string? explicitKind)
    {
        if (explicitKind != null)
            return explicitKind;

        var inferredName = Naming.InferName(ast, Naming.DefaultGetLiteralAlias);

        if (ast is not OptionalExpressionAst and not OftenMissedExpressionAst && inferredName == null)
        {
            _errors.Add(
                new Diagnostic(
                    $"Cannot infer name for sequence expression: {ast}",
                    new SourceSpan(ast.StartPos, ast.EndPos),
                    DiagnosticSeverity.Error));
            return null;
        }

        return inferredName;
    }
}
