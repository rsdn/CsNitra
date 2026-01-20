namespace ExtensibleParaser;

public sealed record FatalError(string Input, int Pos, (int Line, int Col) Location, ExtensibleParaser.Terminal[] Expecteds);


public static class Extensions
{
    public static string GetErrorText(this FatalError error)
    {
        var result = $"""
            {error.Location}: Expected: {string.Join<Terminal>(", ", error.Expecteds)}
            {HighlightContext(error.Input, error.Pos)}
            """;

        return result;
    }

    public static string HighlightContext(this string input, int errorPos, int contextSize = 60)
    {
        var contextStart = Math.Max(0, errorPos - contextSize / 2);
        var contextLength = Math.Min(contextSize, input.Length - contextStart);
        var offset = errorPos - contextStart;
        var before = input[contextStart..errorPos];
        var escapedCount = before.Count(c => c is '\r' or '\n');
        var context = input.Substring(contextStart, contextLength);

        offset += escapedCount;
        context = context.Replace("\r", @"\r").Replace("\n", @"\n");
        var result = $"""
             ...{context}...
                {new string(' ', offset)}^
            """;

        return result;
    }

    public static (int Line, int Col) PositionToLineCol(this string text, int pos)
    {
        if (pos < 0 || pos > text.Length)
            throw new ArgumentOutOfRangeException(nameof(pos));

        var line = 1;
        var col = 1;
        var i = 0;

        while (i < pos)
        {
            if (i + 1 < text.Length && text[i] == '\r' && text[i + 1] == '\n')
            {
                // \r\n
                line++;
                col = 1;
                i += 2;
            }
            else if (text[i] == '\r' || text[i] == '\n')
            {
                // \r or \n
                line++;
                col = 1;
                i++;
            }
            else
            {
                col++;
                i++;
            }
        }

        return (Line: line, Col: col);
    }
}
