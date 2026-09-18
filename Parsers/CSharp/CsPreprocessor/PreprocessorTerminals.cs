using System.Collections.Generic;
using ExtensibleParser;

namespace CsPreprocessor;

public static class PreprocessorTerminals
{
    public static Terminal NoOpTrivia() => _noOpTrivia;

    public static Terminal CodeLine() => _codeLine;

    public static Terminal DirectiveLine() => _directiveLine;

    public static IReadOnlyList<Terminal> GetAll() => [
        NoOpTrivia(),
        CodeLine(),
        DirectiveLine()
    ];

    private static readonly Terminal _noOpTrivia = new NoOpTriviaTerminal();

    private static readonly Terminal _codeLine = new CodeLineTerminal();

    private static readonly Terminal _directiveLine = new DirectiveLineTerminal();

    private sealed record NoOpTriviaTerminal : Terminal
    {
        public NoOpTriviaTerminal() : base("NoOpTrivia")
        {
        }

        public override int TryMatch(string input, int startPos) => 0;

        public override bool Injectable => false;

        public override string ToString() => "NoOpTrivia";
    }

    private sealed record CodeLineTerminal : Terminal
    {
        public CodeLineTerminal() : base("CodeLine")
        {
        }

        public override int TryMatch(string input, int startPos) =>
            LineSupport.IsDirectiveStart(input, startPos) ? -1 : LineSupport.LineLength(input, startPos);

        public override bool Injectable => false;

        public override string ToString() => "CodeLine";
    }

    private sealed record DirectiveLineTerminal : Terminal
    {
        public DirectiveLineTerminal() : base("DirectiveLine")
        {
        }

        public override int TryMatch(string input, int startPos) =>
            LineSupport.IsDirectiveStart(input, startPos) ? LineSupport.LineLength(input, startPos) : -1;

        public override bool Injectable => false;

        public override string ToString() => "DirectiveLine";
    }

    private static class LineSupport
    {
        public static bool IsDirectiveStart(string input, int startPos)
        {
            for (var pos = startPos; pos < input.Length; pos++)
            {
                var c = input[pos];
                if (c is '\n' or '\r')
                    return false;
                if (c == '#')
                    return true;
                if (!char.IsWhiteSpace(c))
                    return false;
            }
            return false;
        }

        public static int LineLength(string input, int startPos)
        {
            for (var pos = startPos; pos < input.Length; pos++)
                if (input[pos] == '\n')
                    return pos - startPos + 1;
            return input.Length - startPos;
        }
    }
}
