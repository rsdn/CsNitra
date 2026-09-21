#nullable enable

using ExtensibleParser;

namespace Recovery;

[TerminalMatcher]
public sealed partial class ProbeDepthCeilingTerminals
{
    [Regex(@"\s*")]
    public static partial Terminal Trivia();
}

// A5-1 D3 (5b.4.3): ParseRuleOnceProbed — one-shot probe parse с явным потолком глубины.
// Потолок = лимит работы, а не условие принятия: CutPos (первое срабатывание guard'а —
// самая глубокая достигнутая точка) сообщает, как далеко дошёл probe; решение о принятии
// принимает потребитель по числу токенов в потреблённом span'е.
[TestClass]
public sealed class ProbeDepthCeilingTests
{
    // Start = "v" N1; N1..N9 = Ref-цепочка; N10 = "z" (10 уровней, спуск не потребляет вход).
    private static Parser MakeDeepParser()
    {
        var parser = new Parser(ProbeDepthCeilingTerminals.Trivia());
        parser.Rules["Start"] = [new Seq([new Literal("v"), new Ref("N1")], "Start")];
        parser.Rules["N1"] = [new Ref("N2")];
        parser.Rules["N2"] = [new Ref("N3")];
        parser.Rules["N3"] = [new Ref("N4")];
        parser.Rules["N4"] = [new Ref("N5")];
        parser.Rules["N5"] = [new Ref("N6")];
        parser.Rules["N6"] = [new Ref("N7")];
        parser.Rules["N7"] = [new Ref("N8")];
        parser.Rules["N8"] = [new Ref("N9")];
        parser.Rules["N9"] = [new Ref("N10")];
        parser.Rules["N10"] = [new Literal("z")];
        parser.BuildTdoppRules();
        return parser;
    }

    // S = "a" "b" (плоская грамматика, глубина далеко под потолком 8).
    private static Parser MakeShallowParser()
    {
        var parser = new Parser(ProbeDepthCeilingTerminals.Trivia());
        parser.Rules["S"] = [new Seq([new Literal("a"), new Literal("b")], "S")];
        parser.BuildTdoppRules();
        return parser;
    }

    [TestMethod]
    public void Test_Deep_Chain_CeilingCut()
    {
        var parser = MakeDeepParser();
        var (result, ceilingCut, cutPos) = parser.ParseRuleOnceProbed("Start", 0, 0, "v x", 8);

        Assert.IsTrue(ceilingCut, "цепочка глубже потолка 8 → guard должен срезать parse");
        Assert.AreEqual(2, cutPos, "первое срабатывание guard'а — сразу после \"v \" (спуск не потребляет вход)");
        Assert.IsFalse(result.TryGetSuccess(out _, out _), $"ceiling cut → parse не успешен, got {result.ResultKind}@{result.NewPos}");
    }

    [TestMethod]
    public void Test_Shallow_Success_NoCut()
    {
        var parser = MakeShallowParser();
        var (result, ceilingCut, _) = parser.ParseRuleOnceProbed("S", 0, 0, "ab", 8);

        Assert.IsFalse(ceilingCut, "плоская грамматика под потолком → guard не срабатывает");
        Assert.IsTrue(result.TryGetSuccess(out _, out var end) && end == 2,
            $"Expected Success@2, got {result.ResultKind}@{result.NewPos}");
    }

    [TestMethod]
    public void Test_Shallow_Failure_NoCut()
    {
        var parser = MakeShallowParser();
        var (result, ceilingCut, _) = parser.ParseRuleOnceProbed("S", 0, 0, "ac", 8);

        Assert.IsFalse(ceilingCut, "обычный syntax-failure, не ceiling cut");
        Assert.IsFalse(result.TryGetSuccess(out _, out _), $"\"ac\" не парсится в S = \"a\" \"b\", got {result.ResultKind}@{result.NewPos}");
    }

    [TestMethod]
    public void Test_Repeated_Calls_Independent()
    {
        // Второй вызов с тем же (rule, pos) попал бы в memo (ParseRule проверяет memo до кадров)
        // и guard бы не сработал — для проверки per-call reset берём ДРУГУЮ позицию (другой
        // memo-ключ): если _guardFired/_guardFiredPos из первого вызова не сброшены, второй
        // отчёт даст устаревшую позицию срезки (2), а не свою (6).
        var parser = MakeDeepParser();
        var input = "v x v x";
        var (r1, cut1, pos1) = parser.ParseRuleOnceProbed("Start", 0, 0, input, 8);
        var (r2, cut2, pos2) = parser.ParseRuleOnceProbed("Start", 0, 4, input, 8);

        Assert.IsTrue(cut1 && cut2, "оба вызова должны быть ceiling cut (reset guard state per call)");
        Assert.AreEqual(2, pos1, "первый вызов: спуск после \"v \" в позиции 2");
        Assert.AreEqual(6, pos2, "второй вызов: спуск после второго \"v \" в позиции 6 (не устаревшая pos1)");
        Assert.IsFalse(r1.TryGetSuccess(out _, out _));
        Assert.IsFalse(r2.TryGetSuccess(out _, out _));
    }
}
