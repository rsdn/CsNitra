#nullable enable

using ExtensibleParser.Recovery;

#if RECOVERY
namespace Recovery;

[TestClass]
public sealed class SpeculativeCacheTests
{
    [TestMethod]
    public void Test_Speculative_Hit_Miss_And_Reset()
    {
        var cache = new SpeculativeCache();

        // 1-й вызов — промах: compute вызывается, результат пишется в кэш.
        var first = cache.Speculative("r", 0, () => (true, 10));
        Assert.AreEqual((true, 10), first);
        Assert.AreEqual(1, cache.Misses);
        Assert.AreEqual(0, cache.Hits);

        // 2-й вызов — хит: compute НЕ вызывается, возвращается кэшированное значение.
        var second = cache.Speculative("r", 0, () => (false, -1));
        Assert.AreEqual((true, 10), second);
        Assert.AreEqual(1, cache.Hits);
        Assert.AreEqual(1, cache.Misses);

        // Reset — чистит кэш и счётчики.
        cache.Reset();
        Assert.AreEqual(0, cache.Hits);
        Assert.AreEqual(0, cache.Misses);

        // После Reset ключ исчез: 3-й вызов снова промах (кэш действительно очищен).
        var third = cache.Speculative("r", 0, () => (false, -1));
        Assert.AreEqual((false, -1), third);
        Assert.AreEqual(1, cache.Misses);
        Assert.AreEqual(0, cache.Hits);
    }
}
#endif
