using CsNitra.Ast;

namespace CsNitra.TypeChecking;

internal sealed class TypeCheckerVisitor : AstVisitor
{
    private readonly TypeCheckingContext _context;
    private readonly Stack<bool> _nameStack = new();

    public TypeCheckerVisitor(TypeCheckingContext context)
    {
        _context = context;
    }

    private bool HasName => _nameStack.Count > 0 ? _nameStack.Peek() : false;

    public void PushName(bool hasName) => _nameStack.Push(hasName);
    public void PopName() => _nameStack.Pop();

    public override void Visit(GrammarAst node)
    {
        // This method is called after declaration collection
        foreach (var statement in node.Statements)
        {
            if (statement is RuleStatementAst or SimpleRuleStatementAst)
            {
                statement.Accept(this);
            }
        }
    }

    public override void Visit(RuleStatementAst node)
    {
        if (_context.FindRule(node.Name) == null)
        {
            _context.ReportError($"Rule '{node.Name}' not found in symbol table", node);
            return;
        }

        using var _ = _context.EnterScope(node);

        foreach (var alternative in node.Alternatives)
        {
            alternative.Accept(this);
        }
    }

    public override void Visit(SimpleRuleStatementAst node)
    {
        if (_context.FindRule(node.Name) == null)
        {
            _context.ReportError($"Rule '{node.Name}' not found in symbol table", node);
            return;
        }

        using var _ = _context.EnterScope(node);
        PushName(true);
        node.Expression.Accept(this);
        PopName();
    }

    public override void Visit(NamedAlternativeAst node)
    {
        using (_context.EnterScope(node))
        {
            PushName(true);
            node.Expression.Accept(this);
            PopName();
        }
    }

    public override void Visit(AnonymousAlternativeAst node)
    {
        if (node.RuleRef.Parts.Count == 1)
        {
            var identifier = node.RuleRef.Parts[0];
            if (_context.FindRule(identifier) == null && _context.FindTerminal(identifier) == null)
                _context.ReportError($"Symbol '{identifier}' not found", node);
        }
    }

    public override void Visit(SequenceExpressionAst node)
    {
        if (!HasName)
            Error(node, name: "Name");

        // Left part can be a sequence
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
            _context.ReportError($"Nested name ({nestedText}) assignment is not allowed", nested.Name);
        }

        PushName(true);
        node.Expression.Accept(this);
        PopName();
    }

    public override void Visit(OptionalExpressionAst node)
    {
        PushName(false);
        node.Expression.Accept(this);
        PopName();
    }

    public override void Visit(OftenMissedExpressionAst node)
    {
        PushName(false);
        node.Expression.Accept(this);
        PopName();
    }

    public override void Visit(OneOrManyExpressionAst node)
    {
        if (!HasName)
            Error(node);

        PushName(false);
        node.Expression.Accept(this);
        PopName();
    }

    public override void Visit(ZeroOrManyExpressionAst node)
    {
        if (!HasName)
            Error(node);

        PushName(false);
        node.Expression.Accept(this);
        PopName();
    }

    public override void Visit(GroupExpressionAst node)
    {
        if (!HasName)
        {
            Error(node, name: "Name");
            // Suppress repeated error message
            PushName(true);
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

        PushName(false);
        node.Element.Accept(this);
        PopName();

        PushName(false);
        node.Separator.Accept(this);
        PopName();
    }

    public override void Visit(RuleRefExpressionAst node)
    {
        if (node.Ref.Parts.Count == 1)
        {
            var identifier = node.Ref.Parts[0];
            if (_context.FindRule(identifier) == null && _context.FindTerminal(identifier) == null)
                _context.ReportError($"Symbol '{identifier}' not found", identifier);
        }

        if (node.Precedence != null)
        {
            var precedenceSymbol = _context.FindPrecedence(node.Precedence.Precedence);
            if (precedenceSymbol == null)
                _context.ReportError($"Precedence '{node.Precedence.Precedence}' not found", node.Precedence);
        }
    }

    private void Error(CsNitraAst node, string name = "Items")
    {
        var text = node.GetType().Name.Replace("ExpressionAst", "");
        _context.ReportError($"{node} ({text}) expression must have a name (e.g., {name}={node})", node);
    }
}
