namespace ExtensibleParser.Recovery;

// Shared text scanning for S3/S6. A nested region is skipped whole; an enclosing closer
// is eligible as a stop before it is consumed. No grammar-specific quoting is assumed.
internal sealed class RecoveryScan(Parser parser, string input)
{
    public bool IsBoundary(int position) => parser.BracketPairs.Any(p =>
        p.Open.TryMatch(input, position) > 0 || p.Close.TryMatch(input, position) > 0) ||
        parser.RecoveryOpaqueTerminals.Any(t => t.TryMatch(input, position) > 0);

    public (int Position, Terminal Terminal) Find(int start, int limit, IReadOnlyList<Terminal> stops,
        Action? notePosition = null)
    {
        var stack = new Stack<Terminal[]>();
        var pairs = parser.BracketPairs;
        var opaque = parser.RecoveryOpaqueTerminals;
        var position = start;
        limit = Math.Min(limit, input.Length);
        while (position <= limit)
        {
            if (position > start)
                notePosition?.Invoke();
            if (position == input.Length)
                return position > start && stops.Any(t => t is EofTerminal) ? (position, EofTerminal.Instance) : (-1, EofTerminal.Instance);

            var triviaLength = parser.Trivia.TryMatch(input, position);
            if (triviaLength > 0)
            {
                position += triviaLength;
                continue;
            }
            if (position > start && stack.Count == 0)
                foreach (var terminal in stops)
                    if (terminal is not EofTerminal && terminal.TryMatch(input, position) > 0)
                        return (position, terminal);

            var opaqueLength = 0;
            foreach (var terminal in opaque)
                opaqueLength = Math.Max(opaqueLength, terminal.TryMatch(input, position));
            if (opaqueLength > 0)
            {
                position += opaqueLength;
                continue;
            }

            var closeLength = 0;
            if (stack.Count > 0)
                foreach (var closer in stack.Peek())
                    closeLength = Math.Max(closeLength, closer.TryMatch(input, position));
            var openLength = 0;
            foreach (var pair in pairs)
                openLength = Math.Max(openLength, pair.Open.TryMatch(input, position));
            if (closeLength > 0 && closeLength >= openLength)
            {
                stack.Pop();
                position += closeLength;
            }
            else if (openLength > 0)
            {
                // All equally-long openers contribute acceptable closers; registration order is irrelevant.
                var closers = pairs.Where(p => p.Open.TryMatch(input, position) == openLength)
                    .Select(p => p.Close).Distinct(TerminalComparer.Instance).ToArray();
                stack.Push(closers);
                position += openLength;
            }
            else
                position++;
        }
        return (-1, EofTerminal.Instance);
    }
}
