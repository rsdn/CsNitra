using ExtensibleParser;

namespace CsNitra;

[TerminalMatcher]
public sealed partial class CsNitraTerminals
{
    [Regex(@"(//[^\n]*|/\*[^\*]*\*/|\s)*")]
    public static partial Terminal Trivia();

    [Regex(@"[_\l]\w*")]
    public static partial Terminal Identifier();

    [Regex(@"[0-9]+")]
    public static partial Terminal DecimalIntegerLiteral();

    [Regex("""
        "([^"\\]|\\.)*"|'([^'\\]|\\.)*'
        """)]
    public static partial Terminal Literal();
}
