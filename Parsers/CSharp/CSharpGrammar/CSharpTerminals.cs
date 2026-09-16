using ExtensibleParser;

namespace CSharpGrammar;

[TerminalMatcher]
public sealed partial class CSharpTerminals
{
    [Regex(@"[_\l]\w*")]
    public static partial Terminal Identifier();

    [Regex(@"[0-9]+")]
    public static partial Terminal DecimalIntegerLiteral();

    [Regex(@"0[xX][0-9a-fA-F]+")]
    public static partial Terminal HexIntegerLiteral();

    [Regex(@"0[0-7]+")]
    public static partial Terminal OctalIntegerLiteral();

    [Regex(@"[uU]?[lL]?|[lL][uU]?")]
    public static partial Terminal IntegerSuffix();

    [Regex(@"[0-9]+\.[0-9]*|\.[0-9]+")]
    public static partial Terminal DecimalRealLiteral();

    [Regex(@"[eE][+-]?[0-9]+")]
    public static partial Terminal Exponent();

    [Regex(@"[fFdDmM]")]
    public static partial Terminal RealSuffix();

    // Greedy run of safe raw-content chars: [^\{\}"]+ (newline allowed).
    [Regex("""[^\{\}"]+""")]
    public static partial Terminal InterpolatedRawText();

    // Greedy run of safe regular-interpolated content chars: [^\{\}\\"]+ (newline allowed).
    [Regex("""[^\{\}\\"]+""")]
    public static partial Terminal InterpolatedRegularText();

    // Greedy run of safe verbatim-interpolated content chars: [^\{\}"]+ (newline allowed).
    [Regex("""[^\{\}"]+""")]
    public static partial Terminal InterpolatedVerbatimText();

    // Run of raw format chars up to '}': [^}]* (empty allowed).
    [Regex("""[^}]*""")]
    public static partial Terminal RawFormatText();

    // One valid plain-string escape: \ + (["'\\0abfnrtv] | x hex+ | u hex4 | U00 (0 hex5 | 10 hex4))
    // — \U value restricted to <= 0x0010FFFF. Plain groups: the regex engine has no (?:).
    [Regex("""\\(["'\\0abfnrtv]|x[0-9a-fA-F]+|u[0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f]|U00(0[0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f]|10[0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f]))""")]
    public static partial Terminal StringEscape();

    // Greedy run of plain-string content: [^"\\<LF><CR>]+ (no newlines).
    // LF/CR as regex escapes: the pattern value must stay free of raw control chars — the
    // generator interpolates it into a code comment and char literals, and Roslyn treats
    // U+0085/U+2028/U+2029 (and CR/LF) as line terminators there. U+0085/U+2028/U+2029 are
    // inexpressible (no \u escapes in the engine) — accepted as text, see progress2.md.
    [Regex("""[^"\\\n\r]+""")]
    public static partial Terminal StringText();

    // Greedy run of verbatim content: [^"]+ (newline allowed).
    [Regex("""[^"]+""")]
    public static partial Terminal NonQuoteText();

    // Raw string opening/closing quote run: a run of 3+ quotes, one [Regex] match. A single
    // match (not OneOrMany of a quote literal) so the engine's post-terminal trivia skip
    // cannot merge two runs separated by whitespace into one "opening" run.
    [Regex("""""
        """+
        """"")]
    public static partial Terminal RawQuoteRun();

    public static Terminal InterpolatedRegularEscape() => _interpolatedRegularEscape;

    public static Terminal RawQuoteContent() => _rawQuoteContent;

    public static Terminal RawOpenBraceLiteral() => _rawOpenBraceLiteral;

    public static Terminal RawCloseBraceLiteral() => _rawCloseBraceLiteral;

    public static Terminal RawHoleOpenBraces() => _rawHoleOpenBraces;

    public static Terminal RegularFormatText() => _regularFormatText;

    public static Terminal VerbatimFormatText() => _verbatimFormatText;

    [Regex(@"'([^'\n\\]|\\.)'")]
    public static partial Terminal CharLiteral();

    public static Terminal Trivia() => _trivia;

    public static IReadOnlyList<Terminal> GetAll() => [
        Trivia(),
        Identifier(),
        DecimalIntegerLiteral(),
        HexIntegerLiteral(),
        OctalIntegerLiteral(),
        IntegerSuffix(),
        DecimalRealLiteral(),
        Exponent(),
        RealSuffix(),
        InterpolatedRegularText(),
        InterpolatedRegularEscape(),
        InterpolatedVerbatimText(),
        InterpolatedRawText(),
        StringEscape(),
        StringText(),
        NonQuoteText(),
        RawQuoteRun(),
        RawQuoteContent(),
        RawOpenBraceLiteral(),
        RawCloseBraceLiteral(),
        RawHoleOpenBraces(),
        RegularFormatText(),
        VerbatimFormatText(),
        RawFormatText(),
        CharLiteral()
    ];

    private static readonly Terminal _trivia = new TriviaTerminal();

    private static readonly Terminal _interpolatedRegularEscape = new InterpolatedRegularEscapeTerminal();

    private static readonly Terminal _rawQuoteContent = new RawQuoteContentTerminal();

    private static readonly Terminal _rawOpenBraceLiteral = new RawOpenBraceLiteralTerminal();

    private static readonly Terminal _rawCloseBraceLiteral = new RawCloseBraceLiteralTerminal();

    private static readonly Terminal _rawHoleOpenBraces = new RawHoleOpenBracesTerminal();

    private static readonly Terminal _regularFormatText = new RegularFormatTextTerminal();

    private static readonly Terminal _verbatimFormatText = new VerbatimFormatTextTerminal();

    // '\' + a valid escape sequence. \{ / \} (incl. \u007B / \u007D) and invalid escapes do
    // not match (CS1053 / invalid escape) — the hole/text cycle then fails.
    private sealed record InterpolatedRegularEscapeTerminal : Terminal
    {
        public InterpolatedRegularEscapeTerminal() : base("InterpolatedRegularEscape")
        {
        }

        public override int TryMatch(string input, int startPos)
        {
            if (startPos >= input.Length || input[startPos] != '\\')
                return -1;

            var end = StringLiteralScanner.TryScanEscape(input, startPos, out var codePoint);
            if (end < 0)
                return -1;

            if (codePoint is 0x7B or 0x7D)
                return -1;

            return end - startPos;
        }

        public override string ToString() => "InterpolatedRegularEscape";
    }

    // Regular format: run up to '}'. '\'-escapes (CS1053 on \{ / \} / invalid), a single '"'
    // is a premature end (error), '{' is skipped (Roslyn keeps scanning).
    private sealed record RegularFormatTextTerminal : Terminal
    {
        public RegularFormatTextTerminal() : base("RegularFormatText")
        {
        }

        public override int TryMatch(string input, int startPos)
        {
            var length = input.Length;
            var pos = startPos;

            while (pos < length)
            {
                var c = input[pos];

                if (c == '}')
                    return pos - startPos;

                if (c == '\\')
                {
                    var end = StringLiteralScanner.TryScanEscape(input, pos, out var codePoint);
                    if (end < 0)
                        return -1;

                    if (codePoint is 0x7B or 0x7D)
                        return -1;

                    pos = end;
                    continue;
                }

                if (c == '"')
                    return -1;

                pos++;
            }

            return -1;
        }

        public override string ToString() => "RegularFormatText";
    }

    // Verbatim format: run up to '}'. '""' is an escape, a single '"' is a premature end.
    private sealed record VerbatimFormatTextTerminal : Terminal
    {
        public VerbatimFormatTextTerminal() : base("VerbatimFormatText")
        {
        }

        public override int TryMatch(string input, int startPos)
        {
            var length = input.Length;
            var pos = startPos;

            while (pos < length)
            {
                var c = input[pos];

                if (c == '}')
                    return pos - startPos;

                if (c == '"')
                {
                    if (pos + 1 < length && input[pos + 1] == '"')
                    {
                        pos += 2;
                        continue;
                    }

                    return -1;
                }

                pos++;
            }

            return -1;
        }

        public override string ToString() => "VerbatimFormatText";
    }

    // Raw string content quote run: a maximal run of exactly 1..2 quotes (a run of 3+ is the
    // closing, not content). Hand-written (D1 category): "maximal run 1..2" is inexpressible
    // declaratively — the regex engine has no negative lookahead, and a literal + !'"'
    // predicate sees the position after the post-terminal trivia skip (a run followed by a
    // newline and the closing run would fail the guard).
    private sealed record RawQuoteContentTerminal : Terminal
    {
        public RawQuoteContentTerminal() : base("RawQuoteContent")
        {
        }

        public override int TryMatch(string input, int startPos)
        {
            var pos = startPos;
            var length = input.Length;
            while (pos < length && input[pos] == '"')
                pos++;

            var run = pos - startPos;
            return run is 1 or 2 ? run : -1;
        }

        public override string ToString() => "RawQuoteContent";
    }

    // Raw string content: a brace run of length 1..D-1, where D is the dollar count of the
    // enclosing literal (Parser.ContextCount, set by the context scope). A run of D+ braces is
    // a hole (or an error) and does not match here.
    private sealed record RawOpenBraceLiteralTerminal : Terminal
    {
        public RawOpenBraceLiteralTerminal() : base("RawOpenBraceLiteral")
        {
        }

        public override int TryMatch(string input, int startPos)
            => TryMatchLiteralBraceRun(input, startPos, '{');

        public override bool Injectable => false;

        public override string ToString() => "RawOpenBraceLiteral";
    }

    private sealed record RawCloseBraceLiteralTerminal : Terminal
    {
        public RawCloseBraceLiteralTerminal() : base("RawCloseBraceLiteral")
        {
        }

        public override int TryMatch(string input, int startPos)
            => TryMatchLiteralBraceRun(input, startPos, '}');

        public override bool Injectable => false;

        public override string ToString() => "RawCloseBraceLiteral";
    }

    // Raw string content: the opening brace run of a hole MINUS the first brace (which a plain
    // "{" literal consumes first), so the full run is D..2D-1 (D = context count): the first
    // K-D braces are literal text, the last D open the hole. Runs < D are literal
    // (RawOpenBraceLiteral); runs >= 2D are an error (CS9006) and do not match. Failing here
    // (one element into the hole seq) keeps the recovery failure shape identical to the
    // pre-parameterization grammar.
    private sealed record RawHoleOpenBracesTerminal : Terminal
    {
        public RawHoleOpenBracesTerminal() : base("RawHoleOpenBraces")
        {
        }

        public override int TryMatch(string input, int startPos)
        {
            // No active context scope (e.g. speculative recovery probes) = cannot match.
            var depth = Parser.ContextCount;
            if (depth is null)
                return -1;

            var pos = startPos;
            var length = input.Length;
            while (pos < length && input[pos] == '{')
                pos++;

            var run = pos - startPos;
            return run >= depth - 1 && run <= 2 * depth - 2 ? run : -1;
        }

        public override bool Injectable => false;

        public override string ToString() => "RawHoleOpenBraces";
    }

    private static int TryMatchLiteralBraceRun(string input, int startPos, char brace)
    {
        // No active context scope (e.g. speculative recovery probes) = cannot match.
        var depth = Parser.ContextCount;
        if (depth is null)
            return -1;

        var pos = startPos;
        var length = input.Length;
        while (pos < length && input[pos] == brace)
            pos++;

        var run = pos - startPos;
        return run > 0 && run < depth ? run : -1;
    }

    private sealed record TriviaTerminal : Terminal
    {
        public TriviaTerminal() : base("Trivia")
        {
        }

        public override int TryMatch(string input, int startPos)
        {
            var pos = startPos;
            var length = input.Length;

            while (pos < length)
            {
                var c = input[pos];

                if (char.IsWhiteSpace(c))
                {
                    while (pos < length && char.IsWhiteSpace(input[pos]))
                        pos++;
                    continue;
                }

                if (c != '/' || pos + 1 >= length)
                    break;

                var next = input[pos + 1];

                if (next == '/')
                {
                    while (pos < length && input[pos] is not '\n' and not '\r')
                        pos++;
                    continue;
                }

                if (next == '*')
                {
                    pos += 2;
                    var depth = 1;
                    while (pos < length && depth > 0)
                    {
                        if (pos + 1 < length && input[pos] == '*' && input[pos + 1] == '/')
                        {
                            depth--;
                            pos += 2;
                        }
                        else if (pos + 1 < length && input[pos] == '/' && input[pos + 1] == '*')
                        {
                            depth++;
                            pos += 2;
                        }
                        else
                        {
                            pos++;
                        }
                    }
                    continue;
                }

                break;
            }

            return pos - startPos;
        }

        public override string ToString() => "Trivia";
    }
}
