namespace ExtensibleParser.Recovery;

// Кэш результатов спекулятивного parse на scratch-копии (B2): (rule,pos,mode) → (Ok,EndPos) + D2-счётчики
// хитов/промахов. Хит — compute не вызывается; промах — compute вызывается и результат пишется.
public sealed class SpeculativeCache
{
    private readonly Dictionary<(string Rule, int Pos, int Mode), (bool Ok, int EndPos)> _cache = new();
    private int _hits;
    private int _misses;

    public int Hits => _hits;

    public int Misses => _misses;

    // A5-1 D3 (5b.4.3): mode-разделённое ключевое пространство: derived-probe parse (mode 1) не
    // смешивается с авторским полным parse (mode 0) под одним (rule, pos) — срезанный результат
    // зонда (false, cutPos), прочитанный строгими author-семантиками, дал бы ложное отклонение.
    public (bool Ok, int EndPos) Speculative(string rule, int pos, int mode, Func<(bool Ok, int EndPos)> compute)
    {
        var key = (Rule: rule, Pos: pos, Mode: mode);
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

    public (bool Ok, int EndPos) Speculative(string rule, int pos, Func<(bool Ok, int EndPos)> compute)
        => Speculative(rule, pos, 0, compute);

    public void Reset()
    {
        _cache.Clear();
        _hits = 0;
        _misses = 0;
    }
}
