using ExtensibleParser;

namespace CppSimplifiedParser;

// ==================== Terminals ====================
[TerminalMatcher]
public sealed partial class CppTerminals
{
    [Regex(@"(//[^\n]*|\s)*")]
    public static partial Terminal Trivia();

    [Regex(@"[\l_]\w*")]
    public static partial Terminal Identifier();

    [Regex(@"[^,}\n\r]+")]
    public static partial Terminal EnumExpression();

    [Regex(@"([^{}\n]*\n|[^{}\n]+$|[^{}\n]+)")]
    public static partial Terminal AnyLine();
}
