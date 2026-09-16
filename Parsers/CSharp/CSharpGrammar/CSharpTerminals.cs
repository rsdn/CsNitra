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

    [Regex(@"0[bB][01]+")]
    public static partial Terminal BinaryIntegerLiteral();

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

    // === C# 7.0 digit separators (T3.6.5). Each terminal REQUIRES at least one `_` (the `+` on the
    // `(_…)` group), so it is mutually exclusive with the Cs1 no-separator terminal above: a literal
    // with no `_` matches only the Cs1 terminal, and a literal with a `_` matches only the separated
    // terminal (no equal-length tie). Each `_` must be followed by at least one digit (`_[0-9]+`), so
    // a leading `_`, a trailing `_`, and consecutive `__` are all rejected (Roslyn Lexer.cs:801-842,
    // IDS_FeatureDigitSeparator). ===
    [Regex(@"[0-9]+(_[0-9]+)+")]
    public static partial Terminal SeparatedDecimalIntegerLiteral();

    [Regex(@"0[xX][0-9a-fA-F]+(_[0-9a-fA-F]+)+")]
    public static partial Terminal SeparatedHexIntegerLiteral();

    [Regex(@"0[bB][01]+(_[01]+)+")]
    public static partial Terminal SeparatedBinaryIntegerLiteral();

    // A real literal with at least one `_` somewhere: a separator in the integer part, or in the
    // fractional part, or the `.\d` form (three alternatives, each requiring a `_`).
    [Regex(@"[0-9]+(_[0-9]+)+\.[0-9]*(_[0-9]+)*|[0-9]+\.[0-9]*(_[0-9]+)+|\.[0-9]+(_[0-9]+)+")]
    public static partial Terminal SeparatedDecimalRealLiteral();

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

    // Non-interpolated raw string literal (C# 11) as ONE match: opening run of 3+ quotes,
    // content where every quote is part of a 1..2 run (alt: single " | "" + non-quote), 1+
    // content (rejects """""" empty, scanner -1), closing run of 3+ (whole run, scanner
    // semantics). One match = no intra-match trivia skip (a OneOrMany quote-literal source
    // would let the post-terminal trivia skip merge runs separated by whitespace).
    // Content quote runs of 3..N-1 at N>=4 match as quote parts — scanner-consistent.
    [Regex("""""
        """+("|""[^"]|[^"])+"""+
        """"")]
    public static partial Terminal RawString();

    // One valid plain-string escape, same set as StringEscape. Escapes RESOLVING to '{' / '}'
    // (\x7B..\x7D, \u007B..\u007D, \U0000007B..\U0000007D) are accepted — the regex engine
    // cannot check hex values (D7; Roslyn rejects them, CS1053). \{ / \} / invalid: no match.
    [Regex("""\\(["'\\0abfnrtv]|x[0-9a-fA-F]+|u[0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f]|U00(0[0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f]|10[0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f]))""")]
    public static partial Terminal InterpolatedRegularEscape();

    public static Terminal RawOpenBraceLiteral() => _rawOpenBraceLiteral;

    public static Terminal RawCloseBraceLiteral() => _rawCloseBraceLiteral;

    public static Terminal RawHoleOpenBraces() => _rawHoleOpenBraces;

    // Regular format text: run up to '}' — non-} non-quote non-backslash chars or a valid
    // escape (D7: \x7B..\x7D accepted). A lone '"' or an invalid escape stops the run and the
    // hole rule's trailing '}' then fails — rule-level accept/reject = old imperative terminal.
    // One match = comments inside the format stay literal text (no intra-match trivia skip).
    [Regex("""([^}"\\]|\\(["'\\0abfnrtv]|x[0-9a-fA-F]+|u[0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f]|U00(0[0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f]|10[0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f][0-9A-Fa-f])))*""")]
    public static partial Terminal RegularFormatText();

    // Verbatim format text: run up to '}' — non-} non-quote chars or an exact '""' pair. A
    // lone '"' stops the run and the hole rule's trailing '}' then fails (old terminal: -1).
    [Regex("""([^}"]|"")*""")]
    public static partial Terminal VerbatimFormatText();

    [Regex(@"'([^'\n\\]|\\.)'")]
    public static partial Terminal CharLiteral();

    public static Terminal Trivia() => _trivia;

    public static IReadOnlyList<Terminal> GetAll() => [
        Trivia(),
        Identifier(),
        DecimalIntegerLiteral(),
        SeparatedDecimalIntegerLiteral(),
        HexIntegerLiteral(),
        SeparatedHexIntegerLiteral(),
        BinaryIntegerLiteral(),
        SeparatedBinaryIntegerLiteral(),
        OctalIntegerLiteral(),
        IntegerSuffix(),
        DecimalRealLiteral(),
        SeparatedDecimalRealLiteral(),
        Exponent(),
        RealSuffix(),
        InterpolatedRegularText(),
        InterpolatedRegularEscape(),
        InterpolatedVerbatimText(),
        InterpolatedRawText(),
        StringEscape(),
        StringText(),
        NonQuoteText(),
        RawString(),
        RawOpenBraceLiteral(),
        RawCloseBraceLiteral(),
        RawHoleOpenBraces(),
        RegularFormatText(),
        VerbatimFormatText(),
        RawFormatText(),
        CharLiteral()
    ];

    private static readonly Terminal _trivia = new TriviaTerminal();

    private static readonly Terminal _rawOpenBraceLiteral = new RawOpenBraceLiteralTerminal();

    private static readonly Terminal _rawCloseBraceLiteral = new RawCloseBraceLiteralTerminal();

    private static readonly Terminal _rawHoleOpenBraces = new RawHoleOpenBracesTerminal();

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
