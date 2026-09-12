namespace ExtensibleParser.Recovery;

/// <summary>
/// Стабильный синглтон, эмулирующий EOF: TryMatch == 0 iff position >= input.Length.
/// Общий для FollowSetCalculator и engine; компаратор сравнивает его по ссылке.
/// </summary>
public sealed record EofTerminal : Terminal
{
    private EofTerminal() : base("EOF")
    {
    }

    public static readonly EofTerminal Instance = new();

    public override int TryMatch(string input, int position) => position >= input.Length ? 0 : -1;
}

/// <summary>
/// Стабильный синглтон пустого (ε) терминала: TryMatch всегда 0.
/// Общий для FollowSetCalculator и engine; компаратор сравнивает его по ссылке.
/// </summary>
public sealed record EpsilonTerminal : Terminal
{
    private EpsilonTerminal() : base("ε")
    {
    }

    public static readonly EpsilonTerminal Instance = new();

    public override int TryMatch(string input, int position) => 0;
}

/// <summary>
/// Единая идентичность терминалов: Literal — по Value, остальные (включая EOF/ε-синглтоны) — по ссылке.
/// Используется в терминальном кэше, слое инъекций и follow/first-множествах.
/// </summary>
public static class TerminalComparer
{
    public static readonly IEqualityComparer<Terminal> Instance = new TerminalEqualityComparer();
    public static readonly IEqualityComparer<(int Pos, Terminal Terminal)> KeyComparer = new KeyEqualityComparer();

    private sealed class TerminalEqualityComparer : IEqualityComparer<Terminal>
    {
        public bool Equals(Terminal? x, Terminal? y)
        {
            if (x is null || y is null)
                return x is null && y is null;

            if (x is Literal lx && y is Literal ly)
                return lx.Value == ly.Value;

            return ReferenceEquals(x, y);
        }

        public int GetHashCode(Terminal obj)
        {
            if (obj is Literal l)
                return l.Value.GetHashCode();
            return obj.GetHashCode();
        }
    }

    private sealed class KeyEqualityComparer : IEqualityComparer<(int Pos, Terminal Terminal)>
    {
        public bool Equals((int Pos, Terminal Terminal) x, (int Pos, Terminal Terminal) y)
            => x.Pos == y.Pos && Instance.Equals(x.Terminal, y.Terminal);

        public int GetHashCode((int Pos, Terminal Terminal) obj)
            => (obj.Pos * 397) ^ Instance.GetHashCode(obj.Terminal);
    }
}
