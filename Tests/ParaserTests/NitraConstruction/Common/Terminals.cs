using ExtensibleParaser;

namespace NitraConstruction.Common;

public sealed partial class Terminals
{
    public static Terminal Number() => new Literal("0", "Number");

    public static Terminal Ident() => new Literal("a", "Ident");

    public static Terminal Trivia() => new EmptyTerminal("Trivia");

    public static RecoveryTerminal ErrorOperator() => new EmptyTerminal("ErrorOperator");

    public static Terminal ErrorEmpty() => _error;

    private static readonly Terminal _error = new EmptyTerminal("Error");
}
