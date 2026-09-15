using System.Collections.Generic;
using ExtensibleParser;

namespace CSharpGrammar;

[TerminalMatcher]
public sealed partial class CSharpTerminals
{
    [Regex(@"[_\l]\w*")]
    public static partial Terminal Identifier();

    [Regex(@"[0-9]+")]
    public static partial Terminal DecimalIntegerLiteral();

    [Regex(@"0[xX][0-9a-fA-F]+")]
    public static partial Terminal HexIntegerLiteral();

    [Regex(@"0[0-7]+")]
    public static partial Terminal OctalIntegerLiteral();

    [Regex(@"[uU]?[lL]?|[lL][uU]?")]
    public static partial Terminal IntegerSuffix();

    [Regex(@"[0-9]+\.[0-9]*|\.[0-9]+")]
    public static partial Terminal DecimalRealLiteral();

    [Regex(@"[eE][+-]?[0-9]+")]
    public static partial Terminal Exponent();

    [Regex(@"[fFdDmM]")]
    public static partial Terminal RealSuffix();

    public static Terminal StringLiteral() => _stringLiteral;

    public static Terminal VerbatimStringLiteral() => _verbatimStringLiteral;

    public static Terminal RawStringLiteral() => _rawStringLiteral;

    public static Terminal InterpolatedRegularText() => _interpolatedRegularText;

    public static Terminal InterpolatedRegularEscape() => _interpolatedRegularEscape;

    public static Terminal InterpolatedVerbatimText() => _interpolatedVerbatimText;

    public static Terminal InterpolatedRawText() => _interpolatedRawText;

    public static Terminal RegularFormatText() => _regularFormatText;

    public static Terminal VerbatimFormatText() => _verbatimFormatText;

    public static Terminal RawFormatText() => _rawFormatText;

    [Regex(@"'([^'\n\\]|\\.)'")]
    public static partial Terminal CharLiteral();

    public static Terminal Trivia() => _trivia;

    public static IReadOnlyList<Terminal> GetAll() => [
        Trivia(),
        Identifier(),
        DecimalIntegerLiteral(),
        HexIntegerLiteral(),
        OctalIntegerLiteral(),
        IntegerSuffix(),
        DecimalRealLiteral(),
        Exponent(),
        RealSuffix(),
        StringLiteral(),
        VerbatimStringLiteral(),
        RawStringLiteral(),
        InterpolatedRegularText(),
        InterpolatedRegularEscape(),
        InterpolatedVerbatimText(),
        InterpolatedRawText(),
        RegularFormatText(),
        VerbatimFormatText(),
        RawFormatText(),
        CharLiteral()
    ];

    private static readonly Terminal _trivia = new TriviaTerminal();

    private static readonly Terminal _stringLiteral = new StringLiteralTerminal();

    private static readonly Terminal _verbatimStringLiteral = new VerbatimStringLiteralTerminal();

    private static readonly Terminal _rawStringLiteral = new RawStringLiteralTerminal();

    private static readonly Terminal _interpolatedRegularText = new InterpolatedRegularTextTerminal();

    private static readonly Terminal _interpolatedRegularEscape = new InterpolatedRegularEscapeTerminal();

    private static readonly Terminal _interpolatedVerbatimText = new InterpolatedVerbatimTextTerminal();

    private static readonly Terminal _interpolatedRawText = new InterpolatedRawTextTerminal();

    private static readonly Terminal _regularFormatText = new RegularFormatTextTerminal();

    private static readonly Terminal _verbatimFormatText = new VerbatimFormatTextTerminal();

    private static readonly Terminal _rawFormatText = new RawFormatTextTerminal();

    private sealed record StringLiteralTerminal : Terminal
    {
        public StringLiteralTerminal() : base("StringLiteral")
        {
        }

        public override int TryMatch(string input, int startPos)
            => StringLiteralScanner.TryScanPlainString(input, startPos);

        public override string ToString() => "StringLiteral";
    }

    // Greedy run of safe content chars: [^\{\}\\"]+ (newline allowed — the run absorbs it;
    // the global trivia scanner then has nothing left to eat, see InterpolatedStringGrammar §2.2).
    private sealed record InterpolatedRegularTextTerminal : Terminal
    {
        public InterpolatedRegularTextTerminal() : base("InterpolatedRegularText")
        {
        }

        public override int TryMatch(string input, int startPos)
        {
            var length = input.Length;
            if (startPos >= length)
                return -1;

            var pos = startPos;
            while (pos < length && input[pos] is not ('{' or '}' or '\\' or '"'))
                pos++;

            return pos > startPos ? pos - startPos : -1;
        }

        public override string ToString() => "InterpolatedRegularText";
    }

    // '\' + a valid escape sequence. \{ / \} (incl. \u007B / \u007D) and invalid escapes do
    // not match (CS1053 / invalid escape) — the hole/text cycle then fails.
    private sealed record InterpolatedRegularEscapeTerminal : Terminal
    {
        public InterpolatedRegularEscapeTerminal() : base("InterpolatedRegularEscape")
        {
        }

        public override int TryMatch(string input, int startPos)
        {
            if (startPos >= input.Length || input[startPos] != '\\')
                return -1;

            var end = StringLiteralScanner.TryScanEscape(input, startPos, out var codePoint);
            if (end < 0)
                return -1;

            if (codePoint is 0x7B or 0x7D)
                return -1;

            return end - startPos;
        }

        public override string ToString() => "InterpolatedRegularEscape";
    }

    // Greedy run of safe content chars: [^\{\}"]+ (newline allowed).
    private sealed record InterpolatedVerbatimTextTerminal : Terminal
    {
        public InterpolatedVerbatimTextTerminal() : base("InterpolatedVerbatimText")
        {
        }

        public override int TryMatch(string input, int startPos)
            => TryScanNonBraceQuoteRun(input, startPos);

        public override string ToString() => "InterpolatedVerbatimText";
    }

    // Greedy run of safe content chars: [^\{\}"]+ (newline allowed).
    private sealed record InterpolatedRawTextTerminal : Terminal
    {
        public InterpolatedRawTextTerminal() : base("InterpolatedRawText")
        {
        }

        public override int TryMatch(string input, int startPos)
            => TryScanNonBraceQuoteRun(input, startPos);

        public override string ToString() => "InterpolatedRawText";
    }

    private sealed record VerbatimStringLiteralTerminal : Terminal
    {
        public VerbatimStringLiteralTerminal() : base("VerbatimStringLiteral")
        {
        }

        public override int TryMatch(string input, int startPos)
            => StringLiteralScanner.TryScanVerbatimString(input, startPos);

        public override string ToString() => "VerbatimStringLiteral";
    }

    private sealed record RawStringLiteralTerminal : Terminal
    {
        public RawStringLiteralTerminal() : base("RawStringLiteral")
        {
        }

        public override int TryMatch(string input, int startPos)
            => StringLiteralScanner.TryScanRawString(input, startPos);

        public override string ToString() => "RawStringLiteral";
    }

    // Regular format: run up to '}'. '\'-escapes (CS1053 on \{ / \} / invalid), a single '"'
    // is a premature end (error), '{' is skipped (Roslyn keeps scanning).
    private sealed record RegularFormatTextTerminal : Terminal
    {
        public RegularFormatTextTerminal() : base("RegularFormatText")
        {
        }

        public override int TryMatch(string input, int startPos)
        {
            var length = input.Length;
            var pos = startPos;

            while (pos < length)
            {
                var c = input[pos];

                if (c == '}')
                    return pos - startPos;

                if (c == '\\')
                {
                    var end = StringLiteralScanner.TryScanEscape(input, pos, out var codePoint);
                    if (end < 0)
                        return -1;

                    if (codePoint is 0x7B or 0x7D)
                        return -1;

                    pos = end;
                    continue;
                }

                if (c == '"')
                    return -1;

                pos++;
            }

            return -1;
        }

        public override string ToString() => "RegularFormatText";
    }

    // Verbatim format: run up to '}'. '""' is an escape, a single '"' is a premature end.
    private sealed record VerbatimFormatTextTerminal : Terminal
    {
        public VerbatimFormatTextTerminal() : base("VerbatimFormatText")
        {
        }

        public override int TryMatch(string input, int startPos)
        {
            var length = input.Length;
            var pos = startPos;

            while (pos < length)
            {
                var c = input[pos];

                if (c == '}')
                    return pos - startPos;

                if (c == '"')
                {
                    if (pos + 1 < length && input[pos + 1] == '"')
                    {
                        pos += 2;
                        continue;
                    }

                    return -1;
                }

                pos++;
            }

            return -1;
        }

        public override string ToString() => "VerbatimFormatText";
    }

    // Raw format: run up to '}' (no escapes; '"' is content).
    private sealed record RawFormatTextTerminal : Terminal
    {
        public RawFormatTextTerminal() : base("RawFormatText")
        {
        }

        public override int TryMatch(string input, int startPos)
        {
            var length = input.Length;
            var pos = startPos;

            while (pos < length)
            {
                if (input[pos] == '}')
                    return pos - startPos;

                pos++;
            }

            return -1;
        }

        public override string ToString() => "RawFormatText";
    }

    private static int TryScanNonBraceQuoteRun(string input, int startPos)
    {
        var length = input.Length;
        if (startPos >= length)
            return -1;

        var pos = startPos;
        while (pos < length && input[pos] is not ('{' or '}' or '"'))
            pos++;

        return pos > startPos ? pos - startPos : -1;
    }

    private sealed record TriviaTerminal : Terminal
    {
        public TriviaTerminal() : base("Trivia")
        {
        }

        public override int TryMatch(string input, int startPos)
        {
            var pos = startPos;
            var length = input.Length;

            while (pos < length)
            {
                var c = input[pos];

                if (char.IsWhiteSpace(c))
                {
                    while (pos < length && char.IsWhiteSpace(input[pos]))
                        pos++;
                    continue;
                }

                if (c != '/' || pos + 1 >= length)
                    break;

                var next = input[pos + 1];

                if (next == '/')
                {
                    while (pos < length && input[pos] is not '\n' and not '\r')
                        pos++;
                    continue;
                }

                if (next == '*')
                {
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
                    continue;
                }

                break;
            }

            return pos - startPos;
        }

        public override string ToString() => "Trivia";
    }
}
