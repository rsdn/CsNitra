using ExtensibleParaser;

namespace MiniC;

[TerminalMatcher]
public sealed partial class Terminals
{
    [Regex(@"\d+")]
    public static partial Terminal Number();

    [Regex(@"[_\l]\w*")]
    public static partial Terminal Ident(); 

    [Regex(@"\s*")]
    public static partial Terminal Trivia();

    [Regex(@"[/*+-<=>!@#$%^&]+")]
    public static partial RecoveryTerminal ErrorOperator();

    public static Terminal ErrorEmpty() => _error;

    private static Terminal _error = new EmptyTerminal("Error");
}
