
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

    [Regex(@"֎?")] // Временное решение. Дале нужно написать специальное правило допускающее разбор пустой строки.
    public static partial Terminal Error();
}