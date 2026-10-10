using ExtensibleParser;

namespace CSharpGrammar;

// Extents belong to the host language. Probe complete grammar rules on an isolated parser;
// fragment terminals such as StringText must never make quotes or interpolation holes opaque.
internal sealed record RecoveryStringTerminal : Terminal
{
    private readonly Parser _host;
    private readonly Parser _scratch;

    public RecoveryStringTerminal(Parser parser) : base("RecoveryString")
    {
        _host = parser;
        _scratch = new Parser(parser.Trivia);
    }

    public override int TryMatch(string input, int startPos)
    {
        var prefixEnd = startPos;
        while (prefixEnd < input.Length && input[prefixEnd] is '@' or '$')
            prefixEnd++;
        if (prefixEnd >= input.Length || input[prefixEnd] != '"')
            return -1;
        var prefix = input.Substring(startPos, prefixEnd - startPos);
        var quoteEnd = prefixEnd;
        while (quoteEnd < input.Length && input[quoteEnd] == '"')
            quoteEnd++;
        var verbatim = prefix.Contains("@");
        var raw = !verbatim && quoteEnd - prefixEnd >= 3;
        var rule = (verbatim, raw, prefix.Contains("$")) switch
        {
            (true, _, true) => "VerbatimInterpolatedStringLiteral",
            (true, _, false) => "VerbatimStringLiteral",
            (_, true, true) => "RawInterpolatedStringLiteral",
            (_, true, false) => "RawStringLiteral",
            (_, _, true) => "InterpolatedStringLiteral",
            _ => "StringLiteral"
        };
        RefreshGrammar();
        var context = Parser.ContextCount;
        try
        {
            Parser.ContextCount = null;
            if (_scratch.Rules.ContainsKey(rule) &&
                _scratch.ParseSubRule(input, rule, startPos).TryGetSuccess(out _, out var end) && end > startPos)
                return end - startPos;
        }
        finally
        {
            Parser.ContextCount = context;
        }
        // A shorter lexical family must not expose the interior of a malformed raw string.
        // Multiline families conservatively consume to EOF; regular strings end at a line boundary.
        if (verbatim || raw)
            return input.Length - startPos;
        var position = prefixEnd + 1;
        while (position < input.Length && input[position] is not ('\r' or '\n'))
            position++;
        return position - startPos;
    }

    private void RefreshGrammar()
    {
        // Rule arrays are shared, so in-place edits are visible. Replacements/additions/removals
        // require refreshing the dictionaries, including TDOPP rules after a host rebuild.
        if (_scratch.Rules.Count != _host.Rules.Count || _host.Rules.Any(entry =>
            !_scratch.Rules.TryGetValue(entry.Key, out var rules) || !ReferenceEquals(rules, entry.Value)))
        {
            _scratch.Rules.Clear();
            foreach (var entry in _host.Rules)
                _scratch.Rules[entry.Key] = entry.Value;
        }
        if (_scratch.TdoppRules.Count != _host.TdoppRules.Count || _host.TdoppRules.Any(entry =>
            !_scratch.TdoppRules.TryGetValue(entry.Key, out var rule) || !ReferenceEquals(rule, entry.Value)))
        {
            _scratch.TdoppRules.Clear();
            foreach (var entry in _host.TdoppRules)
                _scratch.TdoppRules[entry.Key] = entry.Value;
        }
    }
}
