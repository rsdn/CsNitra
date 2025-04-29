#nullable enable
using System.Text;

namespace Regex;

public abstract record RegexNode
{
    public abstract string ToString(int precedence);
    public sealed override string ToString() => ToString(0);
}

public record RegexChar(char Value) : RegexNode
{
    public override string ToString(int precedence) =>
        Value.ToString().Replace("\\", "\\\\");
}

public record RegexAnyChar : RegexNode
{
    public override string ToString(int precedence) => ".";
}

public record CharRange(char From, char To);

public abstract record RegexCharClass(bool Negated) : RegexNode
{
    public abstract bool Matches(char c);
}

public record RangesCharClass(CharRange[] Ranges, bool Negated) : RegexCharClass(Negated)
{
    public override string ToString(int precedence)
    {
        var sb = new StringBuilder();
        sb.Append(Negated ? "[^" : "[");
        foreach (var range in Ranges)
        {
            sb.Append(
                range.From == range.To
                    ? EscapeChar(range.From)
                    : $"{EscapeChar(range.From)}-{EscapeChar(range.To)}"
            );
        }
        sb.Append(']');
        return sb.ToString();
    }

    public override bool Matches(char c) => Ranges.Any(r => c >= r.From && c <= r.To) ^ Negated;

    private static string EscapeChar(char c) => c switch
    {
        '\\' => @"\\",
        ']' => @"\]",
        '-' => @"\-",
        '^' => @"\^",
        '\t' => @"\t",
        '\n' => @"\n",
        '\r' => @"\r",
        _ when char.IsControl(c) => $"\\u{(int)c:X4}",
        _ => c.ToString()
    };
}

public record WordCharClass(bool Negated) : RegexCharClass(Negated)
{
    public override string ToString(int precedence) => Negated ? @"\W" : @"\w";
    public override bool Matches(char c) => (char.IsLetterOrDigit(c) || c == '_') ^ Negated;
}

public record DigitCharClass(bool Negated) : RegexCharClass(Negated)
{
    public override string ToString(int precedence) => Negated ? @"\D" : @"\d";
    public override bool Matches(char c) => char.IsDigit(c) ^ Negated;
}

public record LetterCharClass(bool Negated) : RegexCharClass(Negated)
{
    public override string ToString(int precedence) => Negated ? @"\L" : @"\l";
    public override bool Matches(char c) => char.IsLetter(c) ^ Negated;
}

public record WhitespaceCharClass(bool Negated) : RegexCharClass(Negated)
{
    public override string ToString(int precedence) => Negated ? @"\S" : @"\s";
    public override bool Matches(char c) => char.IsWhiteSpace(c) ^ Negated;
}

public record RegexConcat(IReadOnlyList<RegexNode> Nodes) : RegexNode
{
    public override string ToString(int precedence)
    {
        var sb = new StringBuilder();
        if (precedence > 1) sb.Append('(');
        sb.Append(string.Join("", Nodes.Select(n => n.ToString(1))));
        if (precedence > 1) sb.Append(')');
        return sb.ToString();
    }
}

public record RegexAlternation(IReadOnlyList<RegexNode> Nodes) : RegexNode
{
    public override string ToString(int precedence)
    {
        var joined = string.Join("|", Nodes.Select(n => n.ToString(2)));
        return precedence > 2 ? $"({joined})" : joined;
    }
}

public record RegexStar(RegexNode Node) : RegexNode
{
    public override string ToString(int precedence)
    {
        var s = Node.ToString(3);
        return precedence > 3 ? $"({s})*" : $"{s}*";
    }
}

public record RegexPlus(RegexNode Node) : RegexNode
{
    public override string ToString(int precedence)
    {
        var s = Node.ToString(3);
        return precedence > 3 ? $"({s})+" : $"{s}+";
    }
}

public record RegexOptional(RegexNode Node) : RegexNode
{
    public override string ToString(int precedence)
    {
        var s = Node.ToString(3);
        return precedence > 3 ? $"({s})?" : $"{s}?";
    }
}

public record RegexGroup(RegexNode Node) : RegexNode
{
    public override string ToString(int precedence) => $"({Node})";
}

public record NegatedCharClassGroup(List<RegexCharClass> Classes) : RegexCharClass(false)
{
    public override string ToString(int precedence) => $"[^{string.Join("", Classes)}]";
    public override bool Matches(char c) => !Classes.Any(cls => cls.Matches(c));
}
