using ExtensibleParaser;

namespace CsNitra.Ast;

public abstract partial record Symbol
{
    public Identifier Name { get; }
    public Source Source { get; }
    public int StartPos => Name.StartPos;
    public int EndPos => Name.EndPos;
    public string Text => Name.Value;

    protected Symbol(Identifier name, Source source)
    {
        Name = name;
        Source = source;
    }
}

public sealed partial record PrecedenceSymbol(
    Identifier Name,
    Source Source,
    int BindingPower
) : Symbol(Name, Source)
{
    public override string ToString() => $"{Name.Value}={BindingPower}";
}

public sealed partial record RuleSymbol(
    Identifier Name,
    Source Source,
    RuleStatementAst? RuleStatement,
    SimpleRuleStatementAst? SimpleRuleStatement
) : Symbol(Name, Source)
{
    public override string ToString() => $"Rule({Name.Value})";
}

public sealed partial record TerminalSymbol(
    Identifier Name,
    Source Source,
    Terminal Terminal
) : Symbol(Name, Source)
{
    public override string ToString() => $"Terminal({Name.Value})";
}

public sealed partial record Scope
{
    public Scope? Parent { get; }
    public CsNitraAst? ScopeNode { get; }

    private readonly Dictionary<string, PrecedenceSymbol> _precedences = new();
    private readonly Dictionary<string, RuleSymbol> _rules = new();
    private readonly Dictionary<string, TerminalSymbol> _terminals = new();

    public Scope(Scope? parent = null, CsNitraAst? scopeNode = null)
    {
        Parent = parent;
        ScopeNode = scopeNode;
    }

    public void AddSymbol(PrecedenceSymbol symbol) =>
        _precedences[symbol.Name.Value] = symbol;

    public void AddSymbol(RuleSymbol symbol) =>
        _rules[symbol.Name.Value] = symbol;

    public void AddSymbol(TerminalSymbol symbol) =>
        _terminals[symbol.Name.Value] = symbol;

    public PrecedenceSymbol? FindPrecedence(string name, bool recursive = true)
    {
        if (_precedences.TryGetValue(name, out var symbol))
            return symbol;

        if (recursive && Parent != null)
            return Parent.FindPrecedence(name, recursive);

        return null;
    }

    public RuleSymbol? FindRule(string name, bool recursive = true)
    {
        if (_rules.TryGetValue(name, out var symbol))
            return symbol;

        if (recursive && Parent != null)
            return Parent.FindRule(name, recursive);

        return null;
    }

    public TerminalSymbol? FindTerminal(string name, bool recursive = true)
    {
        if (_terminals.TryGetValue(name, out var symbol))
            return symbol;

        if (recursive && Parent != null)
            return Parent.FindTerminal(name, recursive);

        return null;
    }

    public Symbol? FindAnySymbol(string name, bool recursive = true) =>
        FindPrecedence(name, recursive: false) as Symbol
        ?? FindRule(name, recursive: false) as Symbol
        ?? FindTerminal(name, recursive: false) as Symbol
        ?? (recursive && Parent != null ? Parent.FindAnySymbol(name, recursive) : null);

    public IEnumerable<PrecedenceSymbol> GetAllPrecedences() => _precedences.Values;
    public IEnumerable<RuleSymbol> GetAllRules() => _rules.Values;
    public IEnumerable<TerminalSymbol> GetAllTerminals() => _terminals.Values;
}

public sealed partial record TypeCheckingContext
{
    private readonly Stack<Scope> _scopeStack = new();
    private readonly List<Diagnostic> _diagnostics = new();

    public Scope CurrentScope => _scopeStack.Peek();
    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;
    public Scope GlobalScope => _scopeStack.First();

    public Source Source { get; }

    public TypeCheckingContext(Source source, IEnumerable<(string Name, Terminal Terminal)> terminals)
    {
        Source = source;
        _scopeStack.Push(new Scope());

        // Добавляем предопределенные терминалы
        foreach (var (name, terminal) in terminals)
        {
            // Создаем фиктивный идентификатор для терминала
            var identifier = new Identifier(name, 0, 0);
            var symbol = new TerminalSymbol(identifier, source, terminal);
            GlobalScope.AddSymbol(symbol);
        }
    }

    public ScopeDisposer EnterScope(CsNitraAst? scopeNode = null)
    {
        var newScope = new Scope(CurrentScope, scopeNode);
        _scopeStack.Push(newScope);
        return new ScopeDisposer(this);
    }

    private void ExitScope()
    {
        if (_scopeStack.Count > 1)
            _scopeStack.Pop();
    }

    public void ReportError(string message, SourceSpan location) =>
        _diagnostics.Add(new Diagnostic(message, location, DiagnosticSeverity.Error));

    public void ReportError(string message, CsNitraAst node) =>
        ReportError(message, new SourceSpan(node.StartPos, node.EndPos));

    public PrecedenceSymbol? FindPrecedence(Identifier identifier) =>
        identifier != null ? CurrentScope.FindPrecedence(identifier.Value) : null;

    public RuleSymbol? FindRule(Identifier identifier) =>
        identifier != null ? CurrentScope.FindRule(identifier.Value) : null;

    public TerminalSymbol? FindTerminal(Identifier identifier) =>
        identifier != null ? CurrentScope.FindTerminal(identifier.Value) : null;

    public sealed class ScopeDisposer(TypeCheckingContext context) : IDisposable
    {
        public void Dispose() => context.ExitScope();
    }
}

/// <summary>
/// Типизатор грамматики
/// </summary>
public sealed partial record TypeChecker
{
    private readonly TypeCheckingContext _context;
    private readonly List<PrecedenceDependency> _precedenceDependencies = new();

    public TypeChecker(Source source, IEnumerable<(string Name, Terminal Terminal)> terminals) =>
        _context = new TypeCheckingContext(source, terminals);

    public (IReadOnlyList<Diagnostic> Diagnostics, Scope GlobalScope) CheckGrammar(GrammarAst grammar)
    {
        CollectDeclarations(grammar);
        ResolveAndCheck(grammar);
        return (_context.Diagnostics, _context.GlobalScope);
    }

    private void CollectDeclarations(GrammarAst grammar)
    {
        foreach (var statement in grammar.Statements)
        {
            switch (statement)
            {
                case PrecedenceStatementAst prec:
                    CollectPrecedenceDeclaration(prec);
                    break;
                case RuleStatementAst rule:
                    CollectRuleDeclaration(rule);
                    break;
                case SimpleRuleStatementAst simpleRule:
                    CollectSimpleRuleDeclaration(simpleRule);
                    break;
            }
        }
    }

    private void CollectPrecedenceDeclaration(PrecedenceStatementAst node) =>
        _precedenceDependencies.Add(new PrecedenceDependency(
            node.Precedences.ToList(),
            new SourceSpan(node.StartPos, node.EndPos)
        ));

    private void CollectRuleDeclaration(RuleStatementAst node)
    {
        var symbol = new RuleSymbol(node.Name, _context.Source, node, null);
        _context.GlobalScope.AddSymbol(symbol);
    }

    private void CollectSimpleRuleDeclaration(SimpleRuleStatementAst node)
    {
        var symbol = new RuleSymbol(node.Name, _context.Source, null, node);
        _context.GlobalScope.AddSymbol(symbol);
    }

    private void ResolveAndCheck(GrammarAst grammar)
    {
        ResolvePrecedenceDependencies();

        foreach (var statement in grammar.Statements)
        {
            switch (statement)
            {
                case RuleStatementAst rule:
                    CheckRule(rule);
                    break;
                case SimpleRuleStatementAst simpleRule:
                    CheckSimpleRule(simpleRule);
                    break;
            }
        }

        ResolveSymbolReferences(grammar);
    }

    private void ResolvePrecedenceDependencies()
    {
        var orderedPrecedences = new List<Identifier>();
        var allIdentifiers = new HashSet<string>();

        foreach (var dependency in _precedenceDependencies)
        {
            if (!TryMergePrecedenceList(dependency.Identifiers, orderedPrecedences, allIdentifiers))
            {
                _context.ReportError(
                    $"Cannot merge precedence list '{string.Join(", ", dependency.Identifiers.Select(i => i.Value))}' " +
                    $"with existing precedence order",
                    dependency.Location
                );
                return;
            }
        }

        for (int i = 0, bp = orderedPrecedences.Count; i < orderedPrecedences.Count; i++, bp--)
        {
            var symbol = new PrecedenceSymbol(
                orderedPrecedences[i],
                _context.Source,
                BindingPower: bp
            );
            _context.GlobalScope.AddSymbol(symbol);
        }
    }

    private bool TryMergePrecedenceList(
        List<Identifier> newList,
        List<Identifier> existingList,
        HashSet<string> allIdentifiers)
    {
        if (existingList.Count == 0)
        {
            existingList.AddRange(newList);
            allIdentifiers.UnionWith(newList.Select(i => i.Value));
            return true;
        }

        var intersection = newList.FirstOrDefault(id => allIdentifiers.Contains(id.Value));
        if (intersection == null)
            return false;

        int existingPos = existingList.FindIndex(id => id.Value == intersection.Value);

        int insertIndex = existingPos;
        for (int i = 0; i < newList.Count; i++)
        {
            if (newList[i].Value == intersection.Value)
                break;

            if (!allIdentifiers.Contains(newList[i].Value))
            {
                existingList.Insert(insertIndex++, newList[i]);
                allIdentifiers.Add(newList[i].Value);
            }
            else
                return false;
        }

        insertIndex = existingPos + 1;
        bool foundIntersection = false;
        for (int i = 0; i < newList.Count; i++)
        {
            if (newList[i].Value == intersection.Value)
            {
                foundIntersection = true;
                continue;
            }

            if (foundIntersection)
            {
                if (!allIdentifiers.Contains(newList[i].Value))
                {
                    existingList.Insert(insertIndex++, newList[i]);
                    allIdentifiers.Add(newList[i].Value);
                }
                else
                    return false;
            }
        }

        return true;
    }

    private void CheckRule(RuleStatementAst node)
    {
        if (_context.FindRule(node.Name) == null)
        {
            _context.ReportError($"Rule '{node.Name}' not found in symbol table", node);
            return;
        }

        using var _ = _context.EnterScope(node);

        foreach (var alternative in node.Alternatives)
            CheckAlternative(alternative);
    }

    private void CheckSimpleRule(SimpleRuleStatementAst node)
    {
        if (_context.FindRule(node.Name) == null)
        {
            _context.ReportError($"Rule '{node.Name}' not found in symbol table", node);
            return;
        }

        using var _ = _context.EnterScope(node);
        CheckRuleExpression(node.Expression);
    }

    private void CheckAlternative(AlternativeAst alternative)
    {
        switch (alternative)
        {
            case NamedAlternativeAst named:
                using (_context.EnterScope(named))
                    CheckRuleExpression(named.Expression);
                break;
            case AnonymousAlternativeAst anon:
                CheckRuleReference(anon.RuleRef);
                break;
        }
    }

    private void CheckRuleExpression(RuleExpressionAst expression)
    {
        switch (expression)
        {
            case RuleRefExpressionAst ruleRef:
                CheckRuleRefExpression(ruleRef);
                break;
            case SequenceExpressionAst seq:
                CheckRuleExpression(seq.Left);
                CheckRuleExpression(seq.Right);
                break;
            case NamedExpressionAst named:
                CheckRuleExpression(named.Expression);
                break;
            case OptionalExpressionAst opt:
                CheckRuleExpression(opt.Expression);
                break;
            case OftenMissedExpressionAst om:
                CheckRuleExpression(om.Expression);
                break;
            case OneOrManyExpressionAst oneOrMany:
                CheckRuleExpression(oneOrMany.Expression);
                break;
            case ZeroOrManyExpressionAst zeroOrMany:
                CheckRuleExpression(zeroOrMany.Expression);
                break;
            case AndPredicateExpressionAst and:
                CheckRuleExpression(and.Expression);
                break;
            case NotPredicateExpressionAst not:
                CheckRuleExpression(not.Expression);
                break;
            case GroupExpressionAst group:
                CheckRuleExpression(group.Expression);
                break;
            case SeparatedListExpressionAst list:
                CheckRuleExpression(list.Element);
                CheckRuleExpression(list.Separator);
                break;
        }
    }

    private void CheckRuleRefExpression(RuleRefExpressionAst node)
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

    private void CheckRuleReference(QualifiedIdentifierAst node)
    {
        if (node.Parts.Count == 1)
        {
            var identifier = node.Parts[0];
            if (_context.FindRule(identifier) == null && _context.FindTerminal(identifier) == null)
                _context.ReportError($"Symbol '{identifier}' not found", node);
        }
    }

    private void ResolveSymbolReferences(GrammarAst grammar)
    {
        var visitor = new SymbolReferenceResolver(_context);
        visitor.VisitGrammar(grammar);
    }
}

/// <summary>
/// Вспомогательный класс для разрешения ссылок на символы в AST
/// </summary>
internal sealed partial record SymbolReferenceResolver
{
    private readonly TypeCheckingContext _context;

    public SymbolReferenceResolver(TypeCheckingContext context) => _context = context;

    public void VisitGrammar(GrammarAst grammar)
    {
        foreach (var statement in grammar.Statements)
            VisitStatement(statement);
    }

    private void VisitStatement(StatementAst statement)
    {
        switch (statement)
        {
            case RuleStatementAst rule:
                VisitRule(rule);
                break;
            case SimpleRuleStatementAst simpleRule:
                VisitSimpleRule(simpleRule);
                break;
        }
    }

    private void VisitRule(RuleStatementAst node)
    {
        node.Symbol = _context.FindRule(node.Name);
        foreach (var alternative in node.Alternatives)
            VisitAlternative(alternative);
    }

    private void VisitSimpleRule(SimpleRuleStatementAst node)
    {
        node.Symbol = _context.FindRule(node.Name);
        VisitRuleExpression(node.Expression);
    }

    private void VisitAlternative(AlternativeAst alternative)
    {
        switch (alternative)
        {
            case NamedAlternativeAst named:
                VisitRuleExpression(named.Expression);
                break;
            case AnonymousAlternativeAst anon:
                if (anon.RuleRef.Parts.Count == 1)
                    anon.ReferencedSymbol = _context.FindRule(anon.RuleRef.Parts[0]);
                break;
        }
    }

    private void VisitRuleExpression(RuleExpressionAst expression)
    {
        switch (expression)
        {
            case RuleRefExpressionAst ruleRef:
                VisitRuleRefExpression(ruleRef);
                break;
            case SequenceExpressionAst seq:
                VisitRuleExpression(seq.Left);
                VisitRuleExpression(seq.Right);
                break;
            case NamedExpressionAst named:
                VisitRuleExpression(named.Expression);
                break;
            case OptionalExpressionAst opt:
                VisitRuleExpression(opt.Expression);
                break;
            case OftenMissedExpressionAst om:
                VisitRuleExpression(om.Expression);
                break;
            case OneOrManyExpressionAst oneOrMany:
                VisitRuleExpression(oneOrMany.Expression);
                break;
            case ZeroOrManyExpressionAst zeroOrMany:
                VisitRuleExpression(zeroOrMany.Expression);
                break;
            case AndPredicateExpressionAst and:
                VisitRuleExpression(and.Expression);
                break;
            case NotPredicateExpressionAst not:
                VisitRuleExpression(not.Expression);
                break;
            case GroupExpressionAst group:
                VisitRuleExpression(group.Expression);
                break;
            case SeparatedListExpressionAst list:
                VisitRuleExpression(list.Element);
                VisitRuleExpression(list.Separator);
                break;
        }
    }

    private void VisitRuleRefExpression(RuleRefExpressionAst node)
    {
        if (node.Ref.Parts.Count == 1)
        {
            var identifier = node.Ref.Parts[0];
            node.ReferencedSymbol = _context.FindRule(identifier) ?? _context.FindTerminal(identifier) as Symbol;
        }

        if (node.Precedence != null)
            node.PrecedenceSymbol = _context.FindPrecedence(node.Precedence.Precedence);
    }
}

// Вспомогательные типы

public sealed partial record Diagnostic(
    string Message,
    SourceSpan Location,
    DiagnosticSeverity Severity
);

public enum DiagnosticSeverity
{
    Error,
    Warning,
    Info
}

public sealed partial record SourceSpan(int Start, int End)
{
    public int Length => End - Start;
}

public sealed partial record PrecedenceDependency(
    List<Identifier> Identifiers,
    SourceSpan Location
);

// Расширения AST для хранения ссылок на символы

public partial record RuleStatementAst
{
    public RuleSymbol? Symbol { get; set; }
}

public partial record SimpleRuleStatementAst
{
    public RuleSymbol? Symbol { get; set; }
}

public partial record AnonymousAlternativeAst
{
    public Symbol? ReferencedSymbol { get; set; }
}

public partial record RuleRefExpressionAst
{
    public Symbol? ReferencedSymbol { get; set; }
    public PrecedenceSymbol? PrecedenceSymbol { get; set; }
}

public partial record PrecedenceAst
{
    public PrecedenceSymbol? PrecedenceSymbol { get; set; }
}

// Абстрактный SourceText
public abstract class Source
{
    public abstract string Text { get; }
}

public sealed class SourceText(string text, string filePath) : Source
{
    public override string Text => text;
    public string FilePath => filePath;
}
