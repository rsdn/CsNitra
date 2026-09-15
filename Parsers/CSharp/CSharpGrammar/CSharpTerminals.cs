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

    [Regex("""
        "([^"\n\\]|\\.)*"
        """)]
    public static partial Terminal StringLiteral();

    [Regex("""
        @"(""|[^"])*"
        """)]
    public static partial Terminal VerbatimStringLiteral();

    [Regex(@"'([^'\n\\]|\\.)'")]
    public static partial Terminal CharLiteral();

    public static Terminal Trivia() => _trivia;

    private static readonly Terminal _trivia = new TriviaTerminal();

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
