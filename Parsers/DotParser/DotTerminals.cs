using ExtensibleParaser;

namespace Dot;

[TerminalMatcher]
public sealed partial class DotTerminals
{
    [Regex(@"[\l_]\w*")]
    public static partial Terminal Identifier();

    //[Regex(@"""[^""]*""")]
    //public static partial Terminal QuotedString();
    public static Terminal QuotedString() => _quotedString;

    [Regex(@"\d+")]
    public static partial Terminal Number();

    [Regex(@"(//[^\n]*|\s)*")]
    public static partial Terminal Trivia();

    private sealed record QuotedStringMatcher() : Terminal(Kind: "QuotedString")
    {
        public override int TryMatch(string input, int startPos)
        {
            if (startPos >= input.Length)
                return -1;

            var i = startPos;
            var c = input[i];

            if (c != '\"')
                return -1;

            for (i++; i < input.Length; i++)
            {
                c = input[i];

                if (c == '\\' && peek() != '\0')
                    i++;
                else if (c == '\n')
                    return -1;
                else if (c == '\"')
                {
                    i++;
                    break;
                }
            }

            return i - startPos;
            char peek() => i + 1 < input.Length ? input[i + 1] : '\0';
        }

        public override string ToString() => @"QuotedString";
    }

    private static readonly Terminal _quotedString = new QuotedStringMatcher();
}
