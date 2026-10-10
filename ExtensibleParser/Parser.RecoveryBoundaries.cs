using ExtensibleParser.Recovery;

namespace ExtensibleParser;

public partial class Parser
{
    private static readonly IReadOnlyList<BracketPair> DefaultBracketPairs = Array.AsReadOnly(new[]
    {
        new BracketPair(new Literal("("), new Literal(")")),
        new BracketPair(new Literal("["), new Literal("]")),
        new BracketPair(new Literal("{"), new Literal("}"))
    });
    private readonly List<BracketPair> _bracketPairs = new();
    private readonly List<Terminal> _opaqueTerminals = new();
    private IReadOnlyList<BracketPair>? _orderedBracketPairs;
    private IReadOnlyList<Terminal>? _opaqueView;

    public IReadOnlyList<BracketPair> BracketPairs => _bracketPairs.Count == 0
        ? DefaultBracketPairs
        : _orderedBracketPairs ??= Array.AsReadOnly(_bracketPairs
            .OrderBy(p => BoundaryKey(p.Open), StringComparer.Ordinal)
            .ThenBy(p => BoundaryKey(p.Close), StringComparer.Ordinal).ToArray());

    public IReadOnlyList<Terminal> RecoveryOpaqueTerminals => _opaqueView ??= _opaqueTerminals.AsReadOnly();

    public void AddBracketPair(Terminal open, Terminal close)
    {
        if (open is null || close is null)
            throw new ArgumentNullException(open is null ? nameof(open) : nameof(close));
        if (_bracketPairs.Any(p => TerminalComparer.Instance.Equals(p.Open, open) &&
            TerminalComparer.Instance.Equals(p.Close, close)))
            return;
        _bracketPairs.Add(new BracketPair(open, close));
        _orderedBracketPairs = null;
    }

    // An opaque matcher must consume the complete region (or its malformed extent).
    // Recovery does not infer opacity from ordinary regex terminals.
    public void AddRecoveryOpaqueTerminal(Terminal terminal)
    {
        if (terminal is null)
            throw new ArgumentNullException(nameof(terminal));
        if (!_opaqueTerminals.Contains(terminal, TerminalComparer.Instance))
            _opaqueTerminals.Add(terminal);
    }

    private static string BoundaryKey(Terminal terminal) => terminal is Literal literal
        ? literal.Value
        : terminal.GetType().FullName + ":" + terminal.Kind;
}
