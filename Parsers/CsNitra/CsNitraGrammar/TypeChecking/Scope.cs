using CsNitra.Ast;

namespace CsNitra.TypeChecking;

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

    public void AddSymbol(PrecedenceSymbol symbol) => _precedences[symbol.Name.Value] = symbol;

    public void AddSymbol(RuleSymbol symbol) => _rules[symbol.Name.Value] = symbol;

    public void AddSymbol(TerminalSymbol symbol) => _terminals[symbol.Name.Value] = symbol;

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
