using ExtensibleParaser;

namespace Calc;

public static partial class Terminals
{
    public static Terminal Number() => new Literal("0", "Number");

    public static Terminal Ident() => new Literal("a", "Ident");

    public static Terminal Trivia() => new EmptyTerminal("Trivia");
}
