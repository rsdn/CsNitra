#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace Recovery;

// A5-5 (5b.3.2): поведение SeparatedList.SoftSeparator — если обязательный разделитель не совпал,
// но на позиции стоит мягкий разделитель (SoftSeparator), он потребляется инлайн: одна
// Skipped-диагностика, парсинг списка продолжается, глобальное восстановление (S2/S3) не запускается.
// SoftSeparator == null (default) — no-op: поведение без изменений.
[TestClass]
public sealed class SoftSeparatorTests
{
    // Item = Number; List = SeparatedList(Item, ",", SoftSeparator: softSeparator)
    private static Parser NewParser(Terminal? softSeparator)
    {
        var parser = new Parser(new EmptyTerminal("Trivia"));
        parser.Rules["Item"] = [RecoveryTerminals.Number()];
        parser.Rules["List"] =
        [
            new SeparatedList(new Ref("Item"), new Literal(","), Kind: "List", SoftSeparator: softSeparator)
        ];
        parser.BuildTdoppRules();
        return parser;
    }

    [TestMethod]
    public void SoftSeparator_Mismatch_IsRecoveredInline_WithSingleSkippedDiagnostic()
    {
        var parser = NewParser(softSeparator: new Literal(";"));
        var input = "1;2";
        var result = parser.Parse(input, "List", out _);

        Assert.IsTrue(result.TryGetSuccess(out var node, out var end));
        Assert.AreEqual(input.Length, end);
        Assert.IsNull(parser.ErrorInfo);

        // (1) ровно одна диагностика — Skipped мягкого разделителя
        Assert.AreEqual(1, parser.RecoveryDiagnostics.Count, Describe(parser.RecoveryDiagnostics));
        var diag = parser.RecoveryDiagnostics[0];
        Assert.AreEqual(RecoveryKind.Skipped, diag.Kind);
        Assert.AreEqual(";", diag.Terminal?.Kind);
        Assert.AreEqual(1, diag.StartPos);
        Assert.AreEqual(2, diag.EndPos);

        // (2) список разобрался: оба элемента в дереве, лишних нет
        Assert.IsInstanceOfType(node, typeof(ListNode));
        var list = (ListNode)node;
        Assert.AreEqual(2, list.RawElements.Count);
        Assert.AreEqual(1, list.Delimiters.Count);

        // (3) глобальное S2/S3 не запускалось: без кандидатов engine, без сканов stop-множества, один проход
        Assert.AreEqual(1, parser.RecoveryPasses);
        Assert.AreEqual(0, parser.EngineGenerateCalls);
        Assert.AreEqual(0, parser.S2ScanPositions);
        Assert.AreEqual(0, parser.S3ScanPositions);
    }

    [TestMethod]
    public void SoftSeparator_Multiple_Mismatches_OneDiagnosticEach()
    {
        var parser = NewParser(softSeparator: new Literal(";"));
        var input = "1;2;3";
        var result = parser.Parse(input, "List", out _);

        Assert.IsTrue(result.TryGetSuccess(out var node, out var end));
        Assert.AreEqual(input.Length, end);
        Assert.IsNull(parser.ErrorInfo);

        // по одной Skipped-диагностике на потреблённый мягкий разделитель
        Assert.AreEqual(2, parser.RecoveryDiagnostics.Count, Describe(parser.RecoveryDiagnostics));
        Assert.IsTrue(parser.RecoveryDiagnostics.All(d => d.Kind == RecoveryKind.Skipped && d.Terminal?.Kind == ";"));
        CollectionAssert.AreEqual(new int[] { 1, 3 }, parser.RecoveryDiagnostics.Select(d => d.StartPos).ToArray());

        Assert.IsInstanceOfType(node, typeof(ListNode));
        var list = (ListNode)node;
        Assert.AreEqual(3, list.RawElements.Count);
        Assert.AreEqual(2, list.Delimiters.Count);

        Assert.AreEqual(1, parser.RecoveryPasses);
        Assert.AreEqual(0, parser.EngineGenerateCalls);
    }

    [TestMethod]
    public void SoftSeparator_Null_NoInlineRecovery_GlobalRecoveryInstead()
    {
        var parser = NewParser(softSeparator: null);
        var input = "1;2";
        var result = parser.Parse(input, "List", out _);

        // Прежнее поведение: список останавливается на несовпавшем разделителе (Optional end behavior) —
        // один элемент; остаток обрабатывает глобальное восстановление, а не инлайн-soft separator.
        Assert.IsTrue(result.TryGetSuccess(out var node, out var end));
        Assert.AreEqual(input.Length, end);
        Assert.IsNull(parser.ErrorInfo);

        Assert.IsInstanceOfType(node, typeof(ListNode));
        var list = (ListNode)node;
        Assert.AreEqual(1, list.RawElements.Count);

        // инлайн-диагностики мягкого разделителя нет
        Assert.IsFalse(parser.RecoveryDiagnostics.Any(d => d.Terminal is { Kind: ";" }), Describe(parser.RecoveryDiagnostics));

        // глобальное восстановление запускалось (в отличие от soft separator-случая)
        Assert.IsTrue(parser.EngineGenerateCalls > 0 || parser.RecoveryPasses > 1,
            $"expected global recovery, passes={parser.RecoveryPasses}, engine={parser.EngineGenerateCalls}, diag={Describe(parser.RecoveryDiagnostics)}");
    }

    private static string Describe(System.Collections.Generic.IReadOnlyList<RecoveryDiagnostic> diags)
        => string.Join("; ", diags.Select(d => $"{d.Kind} [{d.StartPos}..{d.EndPos}) term={d.Terminal?.Kind ?? "-"} {d.Message}"));
}
