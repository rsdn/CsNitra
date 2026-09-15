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
            return TryScanRawString(input, pos);

        return ToLength(pos, TryScanStringContents(input, pos + 1, StringKind.Normal, dollarCount: 0, quoteCount: 1));
    }

    public static int TryScanInterpolatedString(string input, int pos)
    {
        if (pos + 1 >= input.Length || input[pos] != '$')
            return -1;

        var next = input[pos + 1];
        if (next == '$')
            return TryScanRawInterpolatedString(input, pos);

        if (next == '@')
        {
            if (pos + 2 >= input.Length || input[pos + 2] != '"')
                return -1;

            return ToLength(pos, TryScanStringContents(input, pos + 3, StringKind.Verbatim, dollarCount: 1, quoteCount: 1));
        }

        if (next == '"')
            return ToLength(pos, TryScanStringContents(input, pos + 2, StringKind.Normal, dollarCount: 1, quoteCount: 1));

        return -1;
    }

    public static int TryScanVerbatimString(string input, int pos)
    {
        if (pos + 1 >= input.Length || input[pos] != '@' || input[pos + 1] != '"')
            return -1;

        return ToLength(pos, TryScanStringContents(input, pos + 2, StringKind.Verbatim, dollarCount: 0, quoteCount: 1));
    }

    public static int TryScanAtInterpolatedString(string input, int pos)
    {
        if (pos + 2 >= input.Length || input[pos] != '@' || input[pos + 1] != '$' || input[pos + 2] != '"')
            return -1;

        return ToLength(pos, TryScanStringContents(input, pos + 3, StringKind.Verbatim, dollarCount: 1, quoteCount: 1));
    }

    public static int TryScanAtString(string input, int pos)
    {
        if (pos + 1 >= input.Length || input[pos] != '@')
            return -1;

        var next = input[pos + 1];
        if (next == '"')
            return TryScanVerbatimString(input, pos);

        if (next == '$')
            return TryScanAtInterpolatedString(input, pos);

        return -1;
    }

    private static int ToLength(int start, int absoluteEnd) => absoluteEnd < 0 ? -1 : absoluteEnd - start;

    // T1.2.5 extension point: raw string (3+ opening quotes). pos at the first '"'.
    private static int TryScanRawString(string input, int pos) => -1;

    // T1.2.5 extension point: raw interpolated string (2+ '$' then 3+ quotes). pos at the first '$'.
    private static int TryScanRawInterpolatedString(string input, int pos) => -1;

    private static int TryScanStringContents(string input, int pos, StringKind kind, int dollarCount, int quoteCount)
    {
        var length = input.Length;
        var allowNewline = kind is StringKind.Verbatim or StringKind.MultiLineRaw;

        while (pos < length)
        {
            var ch = input[pos];

            if (!allowNewline && IsNewLine(ch))
                return -1;

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

                    return -1;
                case '\\' when kind is StringKind.Normal:
                    {
                        var end = TryScanEscape(input, pos, out var codePoint);
                        if (end < 0)
                            return -1;

                        if (dollarCount > 0 && codePoint is 0x7B or 0x7D)
                            return -1;

                        pos = end;
                        continue;
                    }
                case '{' when dollarCount > 0:
                    {
                        if (kind is StringKind.Normal or StringKind.Verbatim)
                        {
                            if (pos + 1 < length && input[pos + 1] == '{')
                            {
                                pos += 2;
                                continue;
                            }

                            var holeEnd = TryScanHoleBalancedText(
                                input, pos + 1, endingChar: '}', isHole: true, kind, dollarCount, quoteCount);
                            if (holeEnd < 0 || input[holeEnd] != '}')
                                return -1;

                            pos = holeEnd + 1;
                            continue;
                        }

                        return -1;
                    }
                case '}' when dollarCount > 0:
                    {
                        if (kind is StringKind.Normal or StringKind.Verbatim)
                        {
                            pos++;
                            if (pos < length && input[pos] == '}')
                            {
                                pos++;
                                continue;
                            }

                            return -1;
                        }

                        return -1;
                    }
                default:
                    pos++;
                    continue;
            }
        }

        return -1;
    }

    // dollarCount/quoteCount are carried for the raw-string (T1.2.5) branches; unused by Normal/Verbatim.
    private static int TryScanHoleBalancedText(
        string input, int pos, char endingChar, bool isHole, StringKind kind, int dollarCount, int quoteCount)
    {
        var length = input.Length;

        while (true)
        {
            if (pos >= length)
                return -1;

            var ch = input[pos];

            switch (ch)
            {
                case '#':
                    return -1;
                case '$':
                    {
                        var next = pos + 1 < length ? input[pos + 1] : '\0';
                        if (next is not ('$' or '@' or '"'))
                        {
                            pos++;
                            continue;
                        }

                        var end = TryScanInterpolatedString(input, pos);
                        if (end < 0)
                            return -1;

                        pos += end;
                        continue;
                    }
                case ':':
                    if (isHole)
                        return TryScanFormatSpecifier(input, pos + 1, kind);
                    pos++;
                    continue;
                case '}':
                case ')':
                case ']':
                    return ch == endingChar ? pos : -1;
                case '"':
                case '\'':
                    {
                        var end = TryScanNestedLiteral(input, pos);
                        if (end < 0)
                            return -1;

                        pos += end;
                        continue;
                    }
                case '@':
                    {
                        var next = pos + 1 < length ? input[pos + 1] : '\0';
                        if (next == '"')
                        {
                            var end = TryScanVerbatimString(input, pos);
                            if (end < 0)
                                return -1;

                            pos += end;
                            continue;
                        }

                        if (next == '$' && pos + 2 < length && input[pos + 2] == '"')
                        {
                            var end = TryScanAtString(input, pos);
                            if (end < 0)
                                return -1;

                            pos += end;
                            continue;
                        }

                        pos++;
                        continue;
                    }
                case '/':
                    {
                        var next = pos + 1 < length ? input[pos + 1] : '\0';
                        if (next == '/')
                        {
                            while (pos < length && input[pos] is not '\n' and not '\r')
                                pos++;

                            continue;
                        }

                        if (next == '*')
                        {
                            var end = TryScanBlockComment(input, pos);
                            if (end < 0)
                                return -1;

                            pos = end;
                            continue;
                        }

                        pos++;
                        continue;
                    }
                case '{':
                case '(':
                case '[':
                    {
                        var close = ch is '{' ? '}' : ch is '(' ? ')' : ']';
                        var end = TryScanBracketed(input, pos, close, kind, dollarCount, quoteCount);
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
    }

    private static int TryScanBracketed(
        string input, int pos, char closing, StringKind kind, int dollarCount, int quoteCount)
    {
        var end = TryScanHoleBalancedText(input, pos + 1, closing, isHole: false, kind, dollarCount, quoteCount);
        if (end < 0 || input[end] != closing)
            return -1;

        return end + 1;
    }

    private static int TryScanNestedLiteral(string input, int pos)
        => input[pos] == '\'' ? ToLength(pos, TryScanCharLiteral(input, pos)) : TryScanPlainString(input, pos);

    private static int TryScanCharLiteral(string input, int pos)
    {
        if (pos >= input.Length || input[pos] != '\'')
            return -1;

        var end = TryScanCharLiteralBody(input, pos + 1, out var charCount);
        if (end < 0)
            return -1;

        return charCount == 1 ? end : -1;
    }

    private static int TryScanCharLiteralBody(string input, int pos, out int charCount)
    {
        charCount = 0;
        var length = input.Length;

        while (pos < length)
        {
            var ch = input[pos];

            if (ch == '\\')
            {
                var end = TryScanEscape(input, pos, out var codePoint);
                if (end < 0)
                    return -1;

                pos = end;
                charCount += codePoint > 0xFFFF ? 2 : 1;
                continue;
            }

            if (ch == '\'')
                return pos + 1;

            if (IsNewLine(ch))
                return -1;

            pos++;
            charCount++;
        }

        return -1;
    }

    private static int TryScanFormatSpecifier(string input, int pos, StringKind kind)
    {
        var length = input.Length;

        while (pos < length)
        {
            var ch = input[pos];

            if (kind is StringKind.Normal && ch == '\\')
            {
                var end = TryScanEscape(input, pos, out var codePoint);
                if (end < 0)
                    return -1;

                if (codePoint is 0x7B or 0x7D)
                    return -1;

                pos = end;
                continue;
            }

            if (ch == '"')
            {
                if (kind is StringKind.Verbatim && pos + 1 < length && input[pos + 1] == '"')
                {
                    pos += 2;
                    continue;
                }

                return -1;
            }

            if (ch == '{')
                return -1;

            if (ch == '}')
                return pos;

            pos++;
        }

        return -1;
    }

    private static int TryScanBlockComment(string input, int pos)
    {
        var length = input.Length;
        pos += 2;
        var depth = 1;

        while (pos < length && depth > 0)
        {
            if (pos + 1 < length && input[pos] == '*' && input[pos + 1] == '/')
            {
                depth--;
                pos += 2;
            }
            else if (pos + 1 < length && input[pos] == '/' && input[pos + 1] == '*')
            {
                depth++;
                pos += 2;
            }
            else
            {
                pos++;
            }
        }

        return depth == 0 ? pos : -1;
    }

    private static int TryScanEscape(string input, int pos, out uint codePoint)
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
