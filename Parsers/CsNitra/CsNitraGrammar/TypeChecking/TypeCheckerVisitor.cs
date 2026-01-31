using CsNitra.Ast;
using ExtensibleParser;

namespace CsNitra.TypeChecking;

internal sealed class TypeCheckerVisitor(TypeCheckingContext context) : AstVisitor
{
    private readonly Stack<bool> _nameStack = new();
    private bool HasName => _nameStack.Count > 0 && _nameStack.Peek();

    public void PushName(bool hasName) => _nameStack.Push(hasName);
    public void PopName() => _nameStack.Pop();

    public override void Visit(GrammarAst node)
    {
        // This method is called after declaration collection
        foreach (var statement in node.Statements)
            if (statement is RuleStatementAst or SimpleRuleStatementAst)
                statement.Accept(this);
    }

    public override void Visit(RuleStatementAst node)
    {
        if (context.FindRule(node.Name) == null)
        {
            context.ReportError($"Rule '{node.Name}' not found in symbol table", node);
            return;
        }

        using var _ = context.EnterScope(node);

        foreach (var alternative in node.Alternatives)
            alternative.Accept(this);
    }

    public override void Visit(SimpleRuleStatementAst node)
    {
        if (context.FindRule(node.Name) == null)
        {
            context.ReportError($"Rule '{node.Name}' not found in symbol table", node);
            return;
        }

        using var _ = context.EnterScope(node);
        PushName(hasName: true);
        node.Expression.Accept(this);
        PopName();
    }

    public override void Visit(NamedAlternativeAst node)
    {
        using (context.EnterScope(node))
        {
            PushName(hasName: true);
            node.Expression.Accept(this);
            PopName();
        }
    }

    public override void Visit(AnonymousAlternativeAst node)
    {
        if (node.RuleRef.Parts.Count == 1)
        {
            var identifier = node.RuleRef.Parts[0];
            if (context.FindRule(identifier) == null && context.FindTerminal(identifier) == null)
                context.ReportError($"Symbol '{identifier}' not found", node);
        }
    }

    public override void Visit(SequenceExpressionAst node)
    {
        if (!HasName)
            Error(node, name: "Name");

        var leftHasName = node.Left is SequenceExpressionAst;

        PushName(leftHasName);
        node.Left.Accept(this);
        PopName();

        PushName(leftHasName);
        node.Right.Accept(this);
        PopName();
    }

    public override void Visit(NamedExpressionAst node)
    {
        if (node.Expression is NamedExpressionAst nested)
        {
            var nestedText = $"{node.Name}={nested.Name}=...";
            context.ReportError($"Nested name ({nestedText}) assignment is not allowed", nested.Name);
        }

        PushName(hasName: true);
        node.Expression.Accept(this);
        PopName();
    }

    public override void Visit(OptionalExpressionAst node)
    {
        PushName(hasName: false);
        node.Expression.Accept(this);
        PopName();
    }

    public override void Visit(OftenMissedExpressionAst node)
    {
        PushName(hasName: false);
        node.Expression.Accept(this);
        PopName();
    }

    public override void Visit(OneOrManyExpressionAst node)
    {
        if (!HasName)
            Error(node);

        PushName(hasName: false);
        node.Expression.Accept(this);
        PopName();
    }

    public override void Visit(ZeroOrManyExpressionAst node)
    {
        if (!HasName)
            Error(node);

        PushName(hasName: false);
        node.Expression.Accept(this);
        PopName();
    }

    public override void Visit(GroupExpressionAst node)
    {
        if (!HasName)
        {
            Error(node, name: "Name");
            // Suppress repeated error message
            PushName(hasName: true);
            node.Expression.Accept(this);
            PopName();
            return;
        }

        PushName(HasName);
        node.Expression.Accept(this);
        PopName();
    }

    public override void Visit(SeparatedListExpressionAst node)
    {
        if (!HasName)
            Error(node);

        PushName(hasName: false);
        node.Element.Accept(this);
        PopName();

        PushName(hasName: false);
        node.Separator.Accept(this);
        PopName();
    }

    public override void Visit(RuleRefExpressionAst node)
    {
        Guard.AreEqual(expected: 1, actual: node.Ref.Parts.Count);

        var identifier = node.Ref.Parts[0];
        if (context.FindRule(identifier) == null && context.FindTerminal(identifier) == null)
            context.ReportError($"Symbol '{identifier}' not found", identifier);

        if (node.Precedence != null && context.FindPrecedence(node.Precedence.Precedence) is null)
            context.ReportError($"Precedence '{node.Precedence.Precedence}' not found", node.Precedence);
    }

    private void Error(CsNitraAst node, string name = "Items")
    {
        var text = node.GetType().Name.Replace("ExpressionAst", "");
        context.ReportError($"{node} ({text}) expression must have a name (e.g., {name}={node})", node);
    }
}
