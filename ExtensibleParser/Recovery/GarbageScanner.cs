namespace ExtensibleParser.Recovery;

internal static class GarbageScanner
{
    public const int CharacterLimit = 64;

    // Recognition is local to the actual repeating item. Boundaries need the shared lexical
    // policy, but unlike coarse resynchronization a local scan cannot cross a nested region.
    public static int FindEnd(Parser parser, string input, int start, IReadOnlyList<Terminal> vocabulary)
    {
        var limit = Math.Min(input.Length, start + CharacterLimit);
        bool recognized(int position) => vocabulary.Any(t => t is not (EofTerminal or EpsilonTerminal) &&
            t.TryMatch(input, position) > 0);
        if (start >= input.Length || recognized(start) || parser.Trivia.TryMatch(input, start) > 0)
            return -1;
        var scan = new RecoveryScan(parser, input);
        for (var position = start; position <= limit && position < input.Length;)
        {
            if (recognized(position))
                return position > start ? position : -1;
            if (scan.IsBoundary(position))
                return -1;
            var trivia = parser.Trivia.TryMatch(input, position);
            position += trivia > 0 ? trivia : 1;
        }
        return -1;
    }
}
