#nullable enable
namespace Regex;

public class RegexParser
{
    private readonly string _pattern;
    private int _pos;

    public RegexParser(string pattern) => _pattern = pattern;

    public RegexNode Parse()
    {
        var node = ParseAlternation();
        if (_pos < _pattern.Length)
            throw new FormatException($"Unexpected char at position {_pos}");
        return node;
    }

    private RegexNode ParseAlternation()
    {
        var nodes = new List<RegexNode> { ParseConcat() };
        while (Peek() == '|')
        {
            Consume();
            nodes.Add(ParseConcat());
        }
        return nodes.Count == 1 ? nodes[0] : new RegexAlternation(nodes);
    }

    private RegexNode ParseConcat()
    {
        var nodes = new List<RegexNode>();
        while (Peek() is char c && c is not (')' or '|'))
            nodes.Add(ParseAtom());
        return nodes.Count == 1 ? nodes[0] : new RegexConcat(nodes);
    }

    private RegexNode ParseAtom()
    {
        var node = ParseFactor();
        while (true) switch (Peek())
            {
                case '*': Consume(); node = new RegexStar(node); break;
                case '+': Consume(); node = new RegexPlus(node); break;
                case '?': Consume(); node = new RegexOptional(node); break;
                default: return node;
            }
    }

    private RegexNode ParseFactor()
    {
        return Peek() switch
        {
            '(' => ParseGroup(),
            '[' => ParseCharClass(),
            '.' => ConsumeAndReturn(new RegexAnyChar()),
            '\\' => ParseEscape(),
            var c => new RegexChar(Consume())
        };
    }

    private RegexNode ParseGroup()
    {
        Consume();
        var node = ParseAlternation();
        Expect(')');
        return new RegexGroup(node);
    }

    private RegexNode ParseEscape()
    {
        Consume();
        return ExpectChar() switch
        {
            'w' => new WordCharClass(Negated: false),
            'W' => new WordCharClass(Negated: true),
            'l' => new LetterCharClass(Negated: false),
            'L' => new LetterCharClass(Negated: true),
            'd' => new DigitCharClass(Negated: false),
            'D' => new DigitCharClass(Negated: true),
            's' => new WhitespaceCharClass(Negated: false),
            'S' => new WhitespaceCharClass(Negated: true),
            var c => new RegexChar(c)
        };
    }

    private RegexNode ParseCharClass()
    {
        Consume();
        bool negated = Peek() == '^' && Consume() == '^';
        var classNodes = new List<RegexCharClass>();
        while (Peek() != ']')
        {
            if (Peek() == '\\')
            {
                Consume();
                classNodes.Add(ParseEscapedCharClass());
            }
            else
            {
                classNodes.Add(ParseRangeCharClass());
            }
        }
        Consume();
        return CombineCharClasses(classNodes, negated);
    }

    private RegexCharClass ParseEscapedCharClass()
    {
        var c = ExpectChar();
        return c switch
        {
            'w' => new WordCharClass(Negated: false),
            'W' => new WordCharClass(Negated: true),
            'd' => new DigitCharClass(Negated: false),
            'D' => new DigitCharClass(Negated: true),
            'l' => new LetterCharClass(Negated: false),
            'L' => new LetterCharClass(Negated: true),
            's' => new WhitespaceCharClass(Negated: false),
            'S' => new WhitespaceCharClass(Negated: true),
            _ => new RangesCharClass([new CharRange(c, c)], Negated: false)
        };
    }

    private RegexCharClass ParseRangeCharClass()
    {
        var from = ExpectChar();
        if (Peek() == '-' && _pos + 1 < _pattern.Length && _pattern[_pos + 1] != ']')
        {
            Consume();
            var to = ExpectChar();
            if (from > to) throw new FormatException("Invalid range");
            return new RangesCharClass([new CharRange(from, to)], Negated: false);
        }
        return new RangesCharClass([new CharRange(from, from)], Negated: false);
    }

    private RegexNode CombineCharClasses(List<RegexCharClass> classes, bool negated)
    {
        if (negated)
            return new NegatedCharClassGroup(classes);

        if (classes.All(c => c is RangesCharClass))
            return new RangesCharClass(classes.Cast<RangesCharClass>().SelectMany(c => c.Ranges).ToArray(), negated);

        return classes.Count == 1 ? classes[0] : new RegexAlternation(classes);
    }

    private char? Peek() => _pos < _pattern.Length ? _pattern[_pos] : null;
    private char Consume() => _pattern[_pos++];
    private char ExpectChar()
    {
        if (_pos >= _pattern.Length)
            throw new FormatException("Unexpected end of pattern");
        return _pattern[_pos++];
    }

    private void Expect(char expected)
    {
        if (Consume() != expected)
            throw new FormatException($"Expected '{expected}'");
    }

    private RegexNode ConsumeAndReturn(RegexNode node)
    {
        Consume();
        return node;
    }
}
