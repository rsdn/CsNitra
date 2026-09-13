#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

#if RECOVERY
namespace Recovery;

// 3.1.2b: E2E-тесты author-аннотаций Anchors (T1) и CanStart (T2).
// Минимальная грамматика БЕЗ OftenMissed-терминалов: S0 (re-parse) не может сделать прогресс,
// поэтому S2-resync (авторский якорь / CanStart) — единственный путь до EOF. Это изолирует T1/T2.
// ВАЖНО (см. чек-лист 3.1.2b + план §5): в грамматиках с OftenMissed (MiniC `;`/`}`) S0 перехватывает
// восстановление раньше S2 (штатный приоритет S0 > S2). Поэтому T1/T2 проверяются на входах, которые
// S0/S1 не чинят — именно здесь они и полезны.
[TerminalMatcher]
public sealed partial class T1AnchorTerminals
{
    [Regex(@"[_\l]\w*")]
    public static partial Terminal Ident();

    [Regex(@"\s*")]
    public static partial Terminal Trivia();
}

[TestClass]
public sealed class T1AnchorReproTests
{
    private static string Describe(IReadOnlyList<RecoveryDiagnostic> diags)
        => string.Join("; ", diags.Select(d => $"{d.Kind} [{d.StartPos}..{d.EndPos}) «{d.Message}» rule={d.RuleName ?? "-"}"));

    // Тест 10 (T1, §3.4): авторский Anchors ведёт structural resync к следующему члену.
    // Item = int ident ;   Module = RecoveryRule(ZeroOrMany(Item), Anchors: [Ref("Item")]).
    // Вход: валидный item, мусор ###, валидный item. Без OftenMissed → только S2-resync (авторский
    // якорь Item) доходит до EOF. Ожидание: Success@EOF + ErrorInfo null + ≥1 Skipped «resync point».
    [TestMethod]
    public void Test_T1_AuthorAnchor_ResyncToNextItem()
    {
        var parser = new Parser(T1AnchorTerminals.Trivia());
        parser.Rules["Item"] =
        [
            new Seq([new Literal("int"), T1AnchorTerminals.Ident(), new Literal(";")], "Item"),
        ];
        parser.Rules["Module"] =
        [
            new RecoveryRule(new ZeroOrMany(new Ref("Item"), "Items"), new RecoveryOptions { Anchors = [new Ref("Item")] }),
        ];
        parser.BuildTdoppRules();

        var input = "int a; ### int b;";
        var result = parser.Parse(input, "Module", out _);

        var diag = Describe(parser.RecoveryDiagnostics);
        Assert.IsTrue(result.TryGetSuccess(out var node, out var end) && end == input.Length,
            $"Expected Success@EOF via S2 resync, got {result.ResultKind}@{result.NewPos}/{result.MaxFailPos} len={input.Length} ErrorInfo={parser.ErrorInfo?.Pos} diag={diag}");
        Assert.IsNull(parser.ErrorInfo,
            $"Expected ErrorInfo null (recovered), got ErrorInfo@{parser.ErrorInfo?.Pos}. diag={diag}");
        Assert.IsTrue(parser.RecoveryDiagnostics.Any(d => d.Kind == RecoveryKind.Skipped && d.Message.Contains("resync point")),
            $"Expected ≥1 Skipped 'resync point' diagnostic (S2 author-anchor resync used, not S0/S3), got: {diag}");
    }

    // Тест 11 (T2, §3.4/§3.9): мягкий CanStart даёт прогресс, когда полный якорь (T1) не валиден.
    // Item = int ident ;   ItemStart = int ident (мягкое)   Module = RecoveryRule(ZeroOrMany(Item), CanStart: [Ref("ItemStart")]).
    // Вход: валидный item, мусор ###, СЛОМАННЫЙ item (нет ;). Полный Item (T1) на `int b` не валиден
    // (нет ;), но мягкий ItemStart (CanStart) матчит `int b` → S2 T2 resync → прогресс до EOF.
    // Ожидание: Success@EOF + ErrorInfo null + ≥1 Skipped «resync point» (S2 T2 использован).
    [TestMethod]
    public void Test_T2_AuthorCanStart_DoubleError()
    {
        var parser = new Parser(T1AnchorTerminals.Trivia());
        parser.Rules["Item"] =
        [
            new Seq([new Literal("int"), T1AnchorTerminals.Ident(), new Literal(";")], "Item"),
        ];
        parser.Rules["ItemStart"] =
        [
            new Seq([new Literal("int"), T1AnchorTerminals.Ident()], "ItemStart"),
        ];
        parser.Rules["Module"] =
        [
            new RecoveryRule(new ZeroOrMany(new Ref("Item"), "Items"), new RecoveryOptions { CanStart = [new Ref("ItemStart")] }),
        ];
        parser.BuildTdoppRules();

        var input = "int a; ### int b";
        var result = parser.Parse(input, "Module", out _);

        var diag = Describe(parser.RecoveryDiagnostics);
        Assert.IsTrue(result.TryGetSuccess(out var node, out var end) && end == input.Length,
            $"Expected Success@EOF via S2 T2 (CanStart) resync, got {result.ResultKind}@{result.NewPos}/{result.MaxFailPos} len={input.Length} ErrorInfo={parser.ErrorInfo?.Pos} diag={diag}");
        Assert.IsNull(parser.ErrorInfo,
            $"Expected ErrorInfo null (recovered), got ErrorInfo@{parser.ErrorInfo?.Pos}. diag={diag}");
        Assert.IsTrue(parser.RecoveryDiagnostics.Any(d => d.Kind == RecoveryKind.Skipped && d.Message.Contains("resync point")),
            $"Expected ≥1 Skipped 'resync point' diagnostic (S2 T2 resync used, not S0/S3), got: {diag}");
    }
}
#endif
