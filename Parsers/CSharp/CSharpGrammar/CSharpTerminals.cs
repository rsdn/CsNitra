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

    public static Terminal InterpolatedStringLiteral() => _interpolatedStringLiteral;

    public static Terminal VerbatimStringLiteral() => _verbatimStringLiteral;

    public static Terminal RawStringLiteral() => _rawStringLiteral;

    public static Terminal RawInterpolatedStringLiteral() => _rawInterpolatedStringLiteral;

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
        InterpolatedStringLiteral(),
        VerbatimStringLiteral(),
        RawStringLiteral(),
        RawInterpolatedStringLiteral(),
        CharLiteral()
    ];

    private static readonly Terminal _trivia = new TriviaTerminal();

    private static readonly Terminal _stringLiteral = new StringLiteralTerminal();

    private static readonly Terminal _interpolatedStringLiteral = new InterpolatedStringLiteralTerminal();

    private static readonly Terminal _verbatimStringLiteral = new VerbatimStringLiteralTerminal();

    private static readonly Terminal _rawStringLiteral = new RawStringLiteralTerminal();

    private static readonly Terminal _rawInterpolatedStringLiteral = new RawInterpolatedStringLiteralTerminal();

    private sealed record StringLiteralTerminal : Terminal
    {
        public StringLiteralTerminal() : base("StringLiteral")
        {
        }

        public override int TryMatch(string input, int startPos)
            => StringLiteralScanner.TryScanPlainString(input, startPos);

        public override string ToString() => "StringLiteral";
    }

    private sealed record InterpolatedStringLiteralTerminal : Terminal
    {
        public InterpolatedStringLiteralTerminal() : base("InterpolatedStringLiteral")
        {
        }

        public override int TryMatch(string input, int startPos)
        {
            var length = StringLiteralScanner.TryScanInterpolatedString(input, startPos);
            if (length >= 0)
                return length;

            return StringLiteralScanner.TryScanAtInterpolatedString(input, startPos);
        }

        public override string ToString() => "InterpolatedStringLiteral";
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

    private sealed record RawInterpolatedStringLiteralTerminal : Terminal
    {
        public RawInterpolatedStringLiteralTerminal() : base("RawInterpolatedStringLiteral")
        {
        }

        public override int TryMatch(string input, int startPos)
            => StringLiteralScanner.TryScanRawInterpolatedString(input, startPos);

        public override string ToString() => "RawInterpolatedStringLiteral";
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
