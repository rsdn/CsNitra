using CsNitra.Ast;
using ExtensibleParser;

namespace CsNitra.TypeChecking;

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
