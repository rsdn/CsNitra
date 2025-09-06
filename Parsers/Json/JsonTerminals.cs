using ExtensibleParaser;

namespace Json;

[TerminalMatcher]
public sealed partial class JsonTerminals
{
    [Regex(@"\s*")]
    public static partial Terminal Whitespace();

    [Regex("""
        ("([^"\\]|\\.)*"|'([^'\\]|\\.)*')
        """)]
    public static partial Terminal String();

    [Regex(@"-?(0|[1-9]\d*)(\.\d+)?([eE][+-]?\d+)?")]
    public static partial Terminal Number();

    [Regex("true")]
    public static partial Terminal True();

    [Regex("false")]
    public static partial Terminal False();

    [Regex("null")]
    public static partial Terminal Null();
}
