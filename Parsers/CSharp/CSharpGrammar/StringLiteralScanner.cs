using System.Globalization;

namespace CSharpGrammar;

internal static class StringLiteralScanner
{
    private enum StringKind
    {
        Normal,
        Verbatim,
        SingleLineRaw,
        MultiLineRaw
    }

    public static int TryScanPlainString(string input, int pos)
    {
        if (pos >= input.Length || input[pos] != '"')
            return -1;

        if (pos + 2 < input.Length && input[pos + 1] == '"' && input[pos + 2] == '"')
            return -1;

        return ToLength(pos, TryScanStringContents(input, pos + 1, StringKind.Normal, quoteCount: 1));
    }

    // Raw string (CS11): opening quote run N >= 3, no '$'. pos at the first '"'.
    public static int TryScanRawString(string input, int pos)
    {
        if (pos >= input.Length || input[pos] != '"')
            return -1;

        var quoteCount = CountRun(input, pos, '"');
        if (quoteCount < 3)
            return -1;

        return TryScanRawContents(input, pos, quoteStart: pos, quoteCount);
    }

    public static int TryScanVerbatimString(string input, int pos)
    {
        if (pos + 1 >= input.Length || input[pos] != '@' || input[pos + 1] != '"')
            return -1;

        return ToLength(pos, TryScanStringContents(input, pos + 2, StringKind.Verbatim, quoteCount: 1));
    }

    private static int ToLength(int start, int absoluteEnd) => absoluteEnd < 0 ? -1 : absoluteEnd - start;

    // Single/multi-line decision: whitespace after the opening quotes followed by a newline
    // makes the literal multi-line (the newline is part of the open-quote section); otherwise
    // single-line with content starting right after the quotes.
    private static int TryScanRawContents(string input, int start, int quoteStart, int quoteCount)
    {
        var afterQuotes = quoteStart + quoteCount;
        var afterWhitespace = ConsumeWhitespace(input, afterQuotes);

        if (afterWhitespace < input.Length && IsNewLine(input[afterWhitespace]))
        {
            var contentStart = SkipNewLine(input, afterWhitespace);
            if (IsIllegalEmptyMultiLineRaw(input, contentStart, quoteCount))
                return -1;

            return ToLength(start, TryScanStringContents(input, contentStart, StringKind.MultiLineRaw, quoteCount));
        }

        return ToLength(start, TryScanStringContents(input, afterQuotes, StringKind.SingleLineRaw, quoteCount));
    }

    // Multi-line raw strings must contain at least one line of content: whitespace followed
    // by a quote run >= N right after the opening newline is illegal (CS9002).
    private static bool IsIllegalEmptyMultiLineRaw(string input, int contentStart, int quoteCount)
    {
        var q = ConsumeWhitespace(input, contentStart);
        return CountRun(input, q, '"') >= quoteCount;
    }

    private static int CountRun(string input, int pos, char ch)
    {
        var length = input.Length;
        var count = 0;
        while (pos + count < length && input[pos + count] == ch)
            count++;
        return count;
    }

    private static int ConsumeWhitespace(string input, int pos)
    {
        var length = input.Length;
        while (pos < length && IsRawWhitespace(input[pos]))
            pos++;
        return pos;
    }

    private static int SkipNewLine(string input, int pos)
        => pos + 1 < input.Length && input[pos] == '\r' && input[pos + 1] == '\n' ? pos + 2 : pos + 1;

    private static bool IsRawWhitespace(char ch)
        => ch is ' ' or '\t' or '\v' or '\f' or '\u00A0' or '\uFEFF' or '\u001A'
        || (ch > 255 && CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.SpaceSeparator);

    private static int TryScanStringContents(string input, int pos, StringKind kind, int quoteCount)
    {
        var length = input.Length;
        var allowNewline = kind is StringKind.Verbatim or StringKind.MultiLineRaw;

        while (pos < length)
        {
            var ch = input[pos];

            if (!allowNewline && IsNewLine(ch))
                return -1;

            // Multi-line raw: a line starting (after whitespace) with a quote run >= N is the
            // closing line; the entire run is the close.
            if (kind is StringKind.MultiLineRaw && IsNewLine(ch))
            {
                var closePos = ConsumeWhitespace(input, SkipNewLine(input, pos));
                var closeQuoteCount = CountRun(input, closePos, '"');
                if (closeQuoteCount >= quoteCount)
                    return closePos + closeQuoteCount;
            }

            switch (ch)
            {
                case '"':
                    if (kind is StringKind.Normal or StringKind.Verbatim)
                    {
                        if (kind is StringKind.Verbatim && pos + 1 < length && input[pos + 1] == '"')
                        {
                            pos += 2;
                            continue;
                        }

                        return pos + 1;
                    }

                    // Raw: a quote run shorter than N is content; a run >= N ends the string
                    // and the entire run is the close (excess quotes are a semantic error).
                    var quoteRun = CountRun(input, pos, '"');
                    if (quoteRun >= quoteCount)
                        return pos + quoteRun;

                    pos += quoteRun;
                    continue;
                case '\\' when kind is StringKind.Normal:
                    {
                        var end = TryScanEscape(input, pos, out _);
                        if (end < 0)
                            return -1;

                        pos = end;
                        continue;
                    }
                default:
                    pos++;
                    continue;
            }
        }

        return -1;
    }

    // Internal: reused by the InterpolatedRegularEscape terminal (T3.5.2).
    internal static int TryScanEscape(string input, int pos, out uint codePoint)
    {
        codePoint = 0;

        if (pos + 1 >= input.Length)
            return -1;

        var ch = input[pos + 1];

        switch (ch)
        {
            case '"':
            case '\'':
            case '\\':
                codePoint = ch;
                return pos + 2;
            case '0':
                return pos + 2;
            case 'a':
                codePoint = 0x07;
                return pos + 2;
            case 'b':
                codePoint = 0x08;
                return pos + 2;
            case 'f':
                codePoint = 0x0C;
                return pos + 2;
            case 'n':
                codePoint = 0x0A;
                return pos + 2;
            case 'r':
                codePoint = 0x0D;
                return pos + 2;
            case 't':
                codePoint = 0x09;
                return pos + 2;
            case 'v':
                codePoint = 0x0B;
                return pos + 2;
            case 'x':
                return TryScanHexEscape(input, pos + 2, minDigits: 1, maxDigits: 4, out codePoint);
            case 'u':
                return TryScanHexEscape(input, pos + 2, minDigits: 4, maxDigits: 4, out codePoint);
            case 'U':
                {
                    var end = TryScanHexEscape(input, pos + 2, minDigits: 8, maxDigits: 8, out codePoint);
                    return end < 0 || codePoint > 0x0010FFFF ? -1 : end;
                }
            default:
                return -1;
        }
    }

    private static int TryScanHexEscape(string input, int pos, int minDigits, int maxDigits, out uint codePoint)
    {
        codePoint = 0;
        var length = input.Length;

        if (pos >= length || !IsHexDigit(input[pos]))
            return -1;

        var count = 0;
        while (count < maxDigits && pos < length && IsHexDigit(input[pos]))
        {
            codePoint = (codePoint << 4) + (uint)HexValue(input[pos]);
            pos++;
            count++;
        }

        return count >= minDigits ? pos : -1;
    }

    private static bool IsNewLine(char ch) => ch is '\r' or '\n' or '\u0085' or '\u2028' or '\u2029';

    private static bool IsHexDigit(char ch) => ch is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

    private static int HexValue(char ch) => ch switch
    {
        >= '0' and <= '9' => ch - '0',
        >= 'a' and <= 'f' => ch - 'a' + 10,
        _ => ch - 'A' + 10
    };
}
