using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CSharpGrammar;
using ExtensibleParser;

namespace CsPreprocessor;

public static class PreprocessorTerminals
{
    // The engine's trivia: reuses the main C# parser's comment/whitespace scanner
    // (CSharpTerminals.Trivia) so a '#' inside a /* ... */ comment is not seen as a
    // directive-start. Unlike the raw C# trivia (which treats newlines as whitespace and
    // would merge blank lines into the previous line's trailing trivia), this wrapper lets
    // comments span lines but keeps whitespace from crossing a line boundary, so lines still
    // tile [0, len).
    public static Terminal Trivia() => _trivia;

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

    private static readonly Terminal _trivia = new PreprocessorTriviaTerminal();

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

    private sealed record PreprocessorTriviaTerminal : Terminal
    {
        private static readonly Terminal _csharpTrivia = CSharpTerminals.Trivia();

        public PreprocessorTriviaTerminal() : base("PreprocessorTrivia")
        {
        }

        public override int TryMatch(string input, int startPos)
        {
            var pos = startPos;
            var length = input.Length;
            while (pos < length)
            {
                // Comment: delegate to the main parser's trivia (handles // and /* */ with
                // nesting). A comment may span multiple lines, so it is NOT limited to the
                // current line — this is what hides a '#' inside a multi-line comment.
                if (input[pos] == '/' && pos + 1 < length && input[pos + 1] is '/' or '*')
                {
                    var commentLength = _csharpTrivia.TryMatch(input, pos);
                    if (commentLength <= 0)
                        break;
                    pos += commentLength;
                    continue;
                }

                // Whitespace: keep it on the current line (stop at the first '\n') so blank lines
                // stay distinct and lines still tile [0, len). A '\r' is consumed as whitespace
                // (consistent with CodeLine's LineLength, which only stops at '\n'), and the
                // trailing '\n' is left for the line's own terminal (CodeLine / LineEnd) to match.
                var c = input[pos];
                if (c == '\n')
                    break;
                if (char.IsWhiteSpace(c))
                {
                    while (pos < length && char.IsWhiteSpace(input[pos]) && input[pos] != '\n')
                        pos++;
                    continue;
                }

                break;
            }
            return pos - startPos;
        }

        public override bool Injectable => false;

        public override string ToString() => "PreprocessorTrivia";
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

        public override int TryMatch(string input, int startPos)
        {
            // Match the rest of the line INCLUDING its line ending (the '\n'), mirroring CodeLine's
            // LineLength. The engine's trivia (PreprocessorTrivia) stops at '\n', so the newline is
            // left here to be consumed — this keeps the directive line's node covering the full line
            // so lines still tile [0, len). For a message (e.g. `#error "boom"`) the content is the
            // message plus the line ending; the interpreter trims it.
            for (var pos = startPos; pos < input.Length; pos++)
                if (input[pos] == '\n')
                    return pos - startPos + 1;
            return input.Length - startPos;
        }

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
                    return !MultiLineStrings.IsInsideSpan(input, pos);
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

    // T4.4.3 — a line-start '#' inside a multi-line string literal is NOT a directive-start. The
    // string extent is obtained by parsing the string as a C# Expression with the main C# parser's
    // `Expression` rule (no hand-rolled string-content scanning): a string literal is exactly what is
    // at the detected start, so the parsed Expression spans precisely the literal (plus, in the rare
    // case of a following operator, only same/later CODE lines — never a directive line). Spans that
    // contain a newline are recorded; a '#' inside any such span is rejected as a directive-start.
    private static class MultiLineStrings
    {
        private const string ExpressionRule = "Expression";

        // Cs11 = raw strings (""""...""""), the newest string form that can span lines. Cs1 supplies
        // regular/verbatim/char + the base Expression; Cs6 interpolated. Loading the ascending run
        // mirrors the intended grammar composition (EmbeddedGrammar.LoadGrammarUpTo).
        private const int MaxGrammarVersion = 11;

        // The CSharpParser.Parser is a mutable, non-thread-safe instance (its Parse mutates
        // memo/cache/error state). Instead of a shared parser guarded by a global lock (which would
        // serialize all concurrent parses), give each thread its OWN parser: lazily built once per
        // thread on first use, then reused. The expensive grammar build is paid once PER THREAD
        // (acceptable), and no lock is needed because each thread's parser is used only by that thread.
        [ThreadStatic]
        private static CSharpParser? _parser;

        // Parsing is synchronous per source on one thread, and MSTest runs methods on separate
        // threads, so a reference-keyed per-thread cache is both efficient (computed once per input)
        // and safe under method-level parallelism.
        [ThreadStatic]
        private static string? _cachedInput;

        [ThreadStatic]
        private static IReadOnlyList<(int Start, int End)>? _cachedSpans;

        public static bool IsInsideSpan(string input, int pos)
        {
            foreach (var (start, end) in GetSpans(input))
                if (pos >= start && pos < end)
                    return true;
            return false;
        }

        private static IReadOnlyList<(int Start, int End)> GetSpans(string input)
        {
            if (ReferenceEquals(_cachedInput, input))
                return _cachedSpans!;

            _cachedInput = input;
            _cachedSpans = ComputeSpans(input);
            return _cachedSpans;
        }

        private static IReadOnlyList<(int Start, int End)> ComputeSpans(string input)
        {
            var spans = new List<(int Start, int End)>();
            var pos = 0;
            var length = input.Length;
            while (pos < length)
            {
                // A string starts at '@'+'"' (verbatim) or '"' (regular or raw; a preceding '$' run is
                // an interpolated prefix — the expression start is still the quote/quote-run).
                var c = input[pos];
                var isStringStart = c == '"' || (c == '@' && pos + 1 < length && input[pos + 1] == '"');
                if (!isStringStart)
                {
                    pos++;
                    continue;
                }

                // T1 — ordinary and verbatim literals get a cheap linear-scan extent; raw (a quote run
                // of 3+) and interpolated strings still go through the C# Expression parser (T2/T3 will
                // replace those). StringExtent returns -1 for the parser-kept forms.
                var extent = StringExtent(input, pos, length);
                if (extent < 0)
                    extent = ParseExpressionLength(input, pos);
                if (extent <= 0)
                {
                    pos++;
                    continue;
                }

                var end = pos + extent;
                if (input.IndexOf('\n', pos, extent) >= 0)
                    spans.Add((pos, end));

                pos = end;
            }

            return spans;
        }

        // T1 — linear-scan extent of an ordinary or verbatim string literal (no parser). Returns -1
        // for the forms that still need the C# Expression parser: raw strings (a quote run of 3+) and
        // interpolated strings (a '$' immediately before the opening quote). For a well-formed
        // ordinary or verbatim literal the extent equals what ParseExpressionLength returns for the
        // same input: the literal is the whole expression when no operator follows it on the same
        // parse. A verbatim scan is hole-unaware (a lone '"' closes the literal), which matches the
        // grammar's verbatim rule when the parse starts at the '@' (as it does here).
        private static int StringExtent(string input, int pos, int length)
        {
            if (input[pos] == '@')
            {
                // Verbatim: scan to the closing '"'; a doubled '""' is an escaped quote inside.
                var i = pos + 2;
                while (i < length)
                {
                    if (input[i] == '"')
                    {
                        if (i + 1 < length && input[i + 1] == '"')
                        {
                            i += 2;
                            continue;
                        }
                        break;
                    }
                    i++;
                }
                return (i < length ? i + 1 : length) - pos;
            }

            // A quote run of 3+ is a raw string — keep the parser (T2).
            var q = pos;
            while (q < length && input[q] == '"')
                q++;
            if (q - pos >= 3)
                return -1;

            // A '$' immediately before the opening quote marks an interpolated string — keep the parser (T3).
            if (pos > 0 && input[pos - 1] == '$')
                return -1;

            // Ordinary: scan to the first unescaped '"' or '\n' or EOF.
            var j = pos + 1;
            while (j < length)
            {
                if (input[j] == '\\' && j + 1 < length)
                {
                    j += 2;
                    continue;
                }
                if (input[j] is '"' or '\n')
                    break;
                j++;
            }
            return (j < length && input[j] == '"' ? j + 1 : j) - pos;
        }

        private static int ParseExpressionLength(string input, int exprStart)
        {
            var result = GetParser().Parser.ParseSubRule(input, ExpressionRule, exprStart);
            return result.TryGetSuccess(out _, out var end) ? end - exprStart : -1;
        }

        private static CSharpParser GetParser()
        {
            var parser = _parser;
            if (parser is null)
                _parser = parser = BuildParser();
            return parser;
        }

        private static CSharpParser BuildParser()
        {
            var assembly = typeof(CSharpParser).Assembly;
            var grammars = new List<(string Text, string Path)>();
            for (var version = 1; version <= MaxGrammarVersion; version++)
            {
                var suffix = $"Cs{version}.grammar";
                grammars.Add((Load(assembly, suffix), suffix));
            }

            return new CSharpParser(grammars, CSharpTerminals.Trivia(), CSharpTerminals.GetAll());
        }

        private static string Load(Assembly assembly, string resourceSuffix)
        {
            var resourceName = assembly.GetManifestResourceNames()
                .Single(n => n.EndsWith(resourceSuffix, StringComparison.Ordinal));
            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded resource '{resourceSuffix}' not found in {assembly.FullName}");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
