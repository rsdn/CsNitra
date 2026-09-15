using ExtensibleParser;

namespace CSharpGrammarTests;

[TerminalMatcher]
public static partial class SmokeTestTerminals
{
    [Regex(@"(//[^\n]*|/\*[^\*]*\*/|\s)*")]
    public static partial Terminal Trivia();

    [Regex(@"[_\l]\w*")]
    public static partial Terminal Identifier();

    [Regex(@"\d+")]
    public static partial Terminal Number();
}
