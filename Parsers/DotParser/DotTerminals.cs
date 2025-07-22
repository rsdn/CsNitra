using ExtensibleParaser;

namespace Dot;

[TerminalMatcher]
public sealed partial class DotTerminals
{
    [Regex(@"[\l_]\w*")]
    public static partial Terminal Identifier();

    [Regex("""
        "(\\.|[^"])*"
        """)]
    public static partial Terminal QuotedString();

    [Regex(@"\d+")]
    public static partial Terminal Number();

    [Regex(@"(//[^\n]*|\s)*")]
    public static partial Terminal Trivia();
}
