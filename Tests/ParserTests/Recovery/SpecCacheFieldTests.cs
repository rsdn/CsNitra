#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

#if RECOVERY
namespace Recovery;

[TerminalMatcher]
public sealed partial class SpecCacheFieldTerminals
{
    [Regex(@"[a]")]
    public static partial Terminal A();

    [Regex(@"\s*")]
    public static partial Terminal Trivia();
}

// B2: кэш спекулятивных парсов поднят в поле на Parser (время жизни — один Recover). Тест
// проверяет поле/аксессор/счётчики/сброс напрямую, НЕ трогая GenerateS2 (то, что GenerateS2 пишет
// в общий кэш, — в 5a.1.3).
[TestClass]
public sealed class SpecCacheFieldTests
{
    // Минимальная грамматика: терминал A = [a], правило R = A (чтобы Parse имел валидное start-правило).
    private static Parser NewParser()
    {
        var parser = new Parser(SpecCacheFieldTerminals.Trivia());
        parser.Rules["R"] = [SpecCacheFieldTerminals.A()];
        parser.BuildTdoppRules();
        return parser;
    }

    [TestMethod]
    public void Test_SpecCache_Field_Counters_And_Reset()
    {
        var parser = NewParser();

        // 1-й вызов через публичный аксессор — промах: compute вызывается, результат пишется в кэш.
        parser.SpecCache.Speculative("x", 0, () => (true, 1));
        Assert.AreEqual(1, parser.SpecCacheMisses);
        Assert.AreEqual(0, parser.SpecCacheHits);

        // 2-й вызов с тем же ключом (другой compute) — хит: compute НЕ вызывается.
        parser.SpecCache.Speculative("x", 0, () => (false, -1));
        Assert.AreEqual(1, parser.SpecCacheHits);
        Assert.AreEqual(1, parser.SpecCacheMisses);

        // Parse на любом входе (даже чистом) вызывает Recover → сброс кэша и счётчиков.
        parser.Parse("a", "R", out _);
        Assert.AreEqual(0, parser.SpecCacheHits);
        Assert.AreEqual(0, parser.SpecCacheMisses);
    }
}
#endif
