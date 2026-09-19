namespace ExtensibleParser.Recovery;

#if RECOVERY

// Кэш результатов спекулятивного parse на scratch-копии (B2): (rule,pos) → (Ok,EndPos) + D2-счётчики
// хитов/промахов. Хит — compute не вызывается; промах — compute вызывается и результат пишется.
public sealed class SpeculativeCache
{
    private readonly Dictionary<(string Rule, int Pos), (bool Ok, int EndPos)> _cache = new();
    private int _hits;
    private int _misses;

    public int Hits => _hits;

    public int Misses => _misses;

    public (bool Ok, int EndPos) Speculative(string rule, int pos, Func<(bool Ok, int EndPos)> compute)
    {
        var key = (Rule: rule, Pos: pos);
        if (_cache.TryGetValue(key, out var cached))
        {
            _hits++;
            return cached;
        }

        _misses++;
        var result = compute();
        _cache[key] = result;
        return result;
    }

    public void Reset()
    {
        _cache.Clear();
        _hits = 0;
        _misses = 0;
    }
}

#endif
