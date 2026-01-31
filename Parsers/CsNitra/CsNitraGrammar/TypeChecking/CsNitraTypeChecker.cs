using CsNitra.Ast;
using ExtensibleParaser;

namespace CsNitra.TypeChecking;

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

    public TypeCheckingContext(Source source, IEnumerable<Terminal> terminals)
    {
        Source = source;
        _scopeStack.Push(new Scope());

        foreach (var terminal in terminals)
        {
            var identifier = new Identifier(terminal.Kind, 0, terminal.Kind.Length);
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
/// Grammar type checker
/// </summary>
public sealed partial record TypeChecker
{
    private readonly TypeCheckingContext _context;
    private readonly List<PrecedenceDependency> _precedenceDependencies = new();

    public TypeChecker(Source source, IEnumerable<Terminal> terminals) =>
        _context = new TypeCheckingContext(source, terminals);

    public (IReadOnlyList<Diagnostic> Diagnostics, Scope GlobalScope) CheckGrammar(GrammarAst grammar)
    {
        CollectDeclarations(grammar);
        ResolveAndCheck(grammar);
        return (_context.Diagnostics, _context.GlobalScope);
    }

    private void CollectDeclarations(GrammarAst grammar)
    {
        var collector = new DeclarationCollectorVisitor(_context, _precedenceDependencies);
        foreach (var statement in grammar.Statements)
        {
            statement.Accept(collector);
        }
    }

    private void ResolveAndCheck(GrammarAst grammar)
    {
        ResolvePrecedenceDependencies();

        var checker = new TypeCheckerVisitor(_context);
        grammar.Accept(checker);

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

    private void ResolveSymbolReferences(GrammarAst grammar)
    {
        var resolver = new SymbolReferenceResolver(_context);
        grammar.Accept(resolver);
    }
}

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

// Abstract SourceText
public abstract class Source
{
    public abstract string Text { get; }
}

public sealed class SourceText(string text, string filePath) : Source
{
    public override string Text => text;
    public string FilePath => filePath;
}
