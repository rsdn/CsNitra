
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

    public static Terminal Error() => _error;

    private static Terminal _error = new ErrorTerminal();

    private record ErrorTerminal() : Terminal("Error")
    {
        public override int TryMatch(string input, int position) => 0;
    }
}
