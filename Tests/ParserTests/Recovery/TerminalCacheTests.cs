#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

#if RECOVERY
namespace Recovery;

[TestClass]
public sealed class TerminalCacheTests
{
    // Терминал-счётчик: считает вызовы TryMatch, всегда mismatch.
    private sealed record MismatchCounter(int[] Counter) : Terminal("T")
    {
        public override int TryMatch(string input, int startPos)
        {
            Counter[0]++;
            return -1;
        }
    }

    // Сгенерированный терминал: идентичность по инстансу, не по Kind.
    private sealed record GenTerminal(string Name) : Terminal(Name)
    {
        public override int TryMatch(string input, int startPos) => -1;
    }

    // Терминал-триггер: матчит "a" в начале; side-effect'ом инжектит Target на TargetPos.
    private sealed record TriggerTerminal(Parser Parser, Terminal Target, int TargetPos) : Terminal("Trigger")
    {
        public override int TryMatch(string input, int startPos)
        {
            if (startPos == 0 && input.Length > 0 && input[0] == 'a')
            {
                Parser.AddInjection(Target, TargetPos, Injection.Insert("T"));
                return 1;
            }
            return -1;
        }
    }

    private static Parser NewParser() => new(new EmptyTerminal("Trivia"));

    // ============ 1. Кэш mismatch стабилен ============

    [TestMethod]
    public void Test_Cache_Mismatch_Stable()
    {
        var counter = new int[1];
        var parser = NewParser();
        var t = new MismatchCounter(counter);
        parser.Rules["Start"] =
        [
            new Seq([t, new Literal("a")], "A1"),
            new Seq([t, new Literal("b")], "A2"),
        ];
        parser.BuildTdoppRules();

        var result = parser.Parse("x", "Start", out _);

        Assert.IsFalse(result.IsSuccess);
        // (0, T) запрашивается в обеих альтернативах, но TryMatch — ровно 1 раз (второй — из кэша).
        Assert.AreEqual(1, counter[0]);
    }

    // ============ 2. Инъекция поверх кэша ============

    [TestMethod]
    public void Test_Injection_Overrides_Cache()
    {
        var counter = new int[1];
        var parser = NewParser();
        var t = new MismatchCounter(counter);
        var trigger = new TriggerTerminal(parser, t, 1);
        parser.Rules["Start"] = [new Seq([trigger, t, new Literal("x")], "Start")];
        parser.BuildTdoppRules();

        // Одна итерация Parse: триггер (первый элемент) матчит "a" на 0 и side-effect'ом
        // инжектит T на позицию 1. T в норме mismatch, но инъекция проверяется ПЕРЕД кэшем,
        // поэтому T «совпадает» нулевым матчем (IsRecovery), затем "x" → успех.
        var result = parser.Parse("ax", "Start", out _);

        Assert.IsTrue(result.IsSuccess);
        // TryMatch T не вызывается: инъекция перехватывает до кэша/TryMatch.
        Assert.AreEqual(0, counter[0]);

        result.TryGetSuccess(out var node, out _);
        if (node is not SeqNode seq)
            throw new InvalidOperationException("Start result should be a SeqNode");
        Assert.IsTrue(seq.Elements[1] is TerminalNode tn && tn.IsRecovery && tn.Kind == "T" && tn.ContentLength == 0);
    }

    // ============ 3. Идентичность Literal vs instance ============

    [TestMethod]
    public void Test_Comparer_LiteralByValue_InstanceByReference()
    {
        var l1 = new Literal(";");
        var l2 = new Literal(";");
        Assert.IsTrue(TerminalComparer.Instance.Equals(l1, l2));
        Assert.AreEqual(TerminalComparer.Instance.GetHashCode(l1), TerminalComparer.Instance.GetHashCode(l2));

        var g1 = new GenTerminal("T");
        var g2 = new GenTerminal("T");
        Assert.IsFalse(TerminalComparer.Instance.Equals(g1, g2));
    }

    // ============ 4. EOF-синглтон ============

    [TestMethod]
    public void Test_Eof_Singleton_Identity()
    {
        var rules = new Dictionary<string, Rule[]>
        {
            {"Start", new Rule[] { new Literal("a") } },
        };

        var calc = new FollowSetCalculator(rules, "Start");
        var follow = calc.GetFollowSet("Start");
        var eof = follow.First(t => t.Kind == "EOF");

        Assert.IsTrue(ReferenceEquals(eof, EofTerminal.Instance));
    }
}

#endif
