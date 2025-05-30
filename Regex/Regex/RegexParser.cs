#nullable enable
using ExtensibleParaser;

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
            Accept('|');
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
                case '*': Accept('*'); node = new RegexStar(node); break;
                case '+': Accept('+'); node = new RegexPlus(node); break;
                case '?': Accept('?'); node = new RegexOptional(node); break;
                default: return node;
            }
    }

    private RegexNode ParseFactor()
    {
        return Peek() switch
        {
            '(' => parseGroup(),
            '[' => parseCharClass(),
            '.' => parseRegexAnyChar(),
            '$' => parseEndOfLine(),
            '\\' => parseEscape(),
            var c => new RegexChar(Consume())
        };

        RegexEndOfLine parseEndOfLine()
        {
            Accept('$');
            return new RegexEndOfLine();
        }
        RegexNode parseEscape()
        {
            return parseEscapedChar() switch
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
        RegexGroup parseGroup()
        {
            Accept('(');
            var node = ParseAlternation();
            Accept(')');
            return new RegexGroup(node);
        }
        RegexAnyChar parseRegexAnyChar()
        {
            Accept('.');
            return new RegexAnyChar();
        }
        RegexNode parseCharClass()
        {
            Accept('[');
            var negated = Peek() == '^' && Consume() == '^';
            var classNodes = new List<RegexCharClass>();

            while (Peek() != ']')
                classNodes.Add(parseRangeCharClass());

            Accept(']');
            return CombineCharClasses(classNodes, negated);

            RegexCharClass toCharClass(RegexNode node) => node switch
            {
                RegexChar x => new RangesCharClass([new CharRange(x.Value, x.Value)], Negated: false),
                RegexCharClass x => x,
                var x => throw new InvalidCastException($"Unsupported type {x.GetType().Name}. Expected {nameof(RegexCharClass)}.")
            };

            RegexCharClass parseRangeCharClass()
            {
                var firstPos = _pos;
                var first = parse();

                if (Peek() == '-' && _pos + 1 < _pattern.Length && _pattern[_pos + 1] != ']')
                {
                    Accept('-');
                    var secodPos = _pos;
                    var secod = parse();

                    return new RangesCharClass(
                        [new CharRange(expect<RegexChar>(first, firstPos).Value, expect<RegexChar>(secod, secodPos).Value)],
                        Negated: false);
                }

                return toCharClass(first);

                T expect<T>(RegexNode node, int pos) => node is T t
                    ? t
                    : throw new InvalidCastException($"Unexpected type {node.GetType().Name} at {pos}. Expected: {nameof(RegexChar)}");

                RegexNode parse() => Peek() == '\\' ? parseEscape() : new RegexChar(Consume());
            }
        }
        char parseEscapedChar()
        {
            Accept('\\');
            return AceptChar() switch
            {
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                '\\' => '\\',
                var c => c
            };
        }
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
    private char Consume()
    {
        Guard.IsTrue(_pos >= 0);
        Guard.IsTrue(_pos < _pattern.Length);
        return _pattern[_pos++];
    }

    private char AceptChar()
    {
        if (_pos >= _pattern.Length)
            throw new FormatException("Unexpected end of pattern");
        return _pattern[_pos++];
    }

    private void Accept(char expected) => Guard.AreEqual(expected: expected, actual: Consume());
}
