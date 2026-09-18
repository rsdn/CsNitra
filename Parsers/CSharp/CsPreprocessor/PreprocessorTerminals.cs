using System.Collections.Generic;
using ExtensibleParser;

namespace CsPreprocessor;

public static class PreprocessorTerminals
{
    public static Terminal NoOpTrivia() => _noOpTrivia;

    public static Terminal CodeLine() => _codeLine;

    public static Terminal Ws() => _ws;

    public static Terminal Symbol() => _symbol;

    public static Terminal LineEnd() => _lineEnd;

    public static IReadOnlyList<Terminal> GetAll() => [
        NoOpTrivia(),
        CodeLine(),
        Ws(),
        Symbol(),
        LineEnd()
    ];

    private static readonly Terminal _noOpTrivia = new NoOpTriviaTerminal();

    private static readonly Terminal _codeLine = new CodeLineTerminal();

    private static readonly Terminal _ws = new WsTerminal();

    private static readonly Terminal _symbol = new SymbolTerminal();

    private static readonly Terminal _lineEnd = new LineEndTerminal();

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

    private sealed record WsTerminal : Terminal
    {
        public WsTerminal() : base("Ws")
        {
        }

        public override int TryMatch(string input, int startPos) =>
            startPos < input.Length && input[startPos] is ' ' or '\t' ? 1 : -1;

        public override bool Injectable => false;

        public override string ToString() => "Ws";
    }

    private sealed record SymbolTerminal : Terminal
    {
        public SymbolTerminal() : base("Symbol")
        {
        }

        public override int TryMatch(string input, int startPos)
        {
            if (startPos >= input.Length)
                return -1;

            var first = input[startPos];
            if (first != '_' && !char.IsLetter(first))
                return -1;

            var pos = startPos + 1;
            while (pos < input.Length && (input[pos] == '_' || char.IsLetterOrDigit(input[pos])))
                pos++;

            return pos - startPos;
        }

        public override bool Injectable => false;

        public override string ToString() => "Symbol";
    }

    private sealed record LineEndTerminal : Terminal
    {
        public LineEndTerminal() : base("LineEnd")
        {
        }

        public override int TryMatch(string input, int startPos) =>
            LineSupport.LineLength(input, startPos);

        public override bool Injectable => false;

        public override string ToString() => "LineEnd";
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
