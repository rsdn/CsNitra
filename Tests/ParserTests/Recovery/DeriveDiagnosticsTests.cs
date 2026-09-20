#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace Recovery;

// A5-2 (6.1.1): DeriveDiagnostics — чистый вывод recovery-диагностики из финального дерева
// (единый источник правды). Дерево несёт два вида recovery-узлов:
//   • нулевая вставка (IsRecovery, не абсорбер, ширина 0) → Inserted;
//   • абсорбер (IsAbsorber, ширина > 0) → Skipped, несущий текст узла.
// Unrecovered — НЕ узел дерева (нулевой маркер в точке восстановления при принятии S6-дна,
// A4-2: выводится из РЕЗУЛЬТАТА), поэтому обход дерева его НЕ выдаёт.
// Терминалы переиспользуют RecoveryTerminals (IterativeRecoveryTests.cs, namespace Recovery).
[TestClass]
public sealed class DeriveDiagnosticsTests
{
    // ============ 1. Вставка (missing token) → Inserted, нулевая ширина ============
    // Module := a b c, вход "a c" (пропущен "b"). Mismatch в точке "b" → S1 вставляет "b"
    // (нулевой recovery-узел, не абсорбер) → Success@EOF. Вывод: ровно одна Inserted в pos 2.
    [TestMethod]
    public void Test_DeriveDiagnostics_Insertion()
    {
        var parser = NewInsertionParser();
        var input = "a c";
        var result = parser.Parse(input, "Module", out _);

        Assert.IsTrue(result.TryGetSuccess(out var node, out var end) && end == input.Length,
            $"expected Success@EOF, got {result.ResultKind}@{result.NewPos}/{result.MaxFailPos}");

        var diags = DiagnosticDerivation.DeriveDiagnostics(node, input);

        Assert.AreEqual(1, diags.Count, $"expected exactly 1 derived diagnostic, got: {Describe(diags)}");
        Assert.AreEqual(RecoveryKind.Inserted, diags[0].Kind, Describe(diags));
        Assert.AreEqual(2, diags[0].StartPos, $"insertion expected at pos 2 (where \"b\" is missing), got: {Describe(diags)}");
        Assert.AreEqual(2, diags[0].EndPos, $"insertion must be zero-width, got: {Describe(diags)}");
    }

    // ============ 2. Абсорбер (хвостовой мусор) → Skipped, несущий текст узла ============
    // Module := Expr Expr, Expr := Number, вход "12 34 ###". Хвост "###" не совпадает → S6-дно
    // кладёт абсорбер в дерево (IsAbsorber). Вывод: ровно одна Skipped до EOF, Message == текст узла.
    [TestMethod]
    public void Test_DeriveDiagnostics_AbsorberCarriesSkippedText()
    {
        var parser = NewAbsorberParser();
        var input = "12 34 ###";
        var result = parser.Parse(input, "Module", out _);

        Assert.IsTrue(result.TryGetSuccess(out var node, out var end) && end == input.Length,
            $"expected Success@EOF, got {result.ResultKind}@{result.NewPos}/{result.MaxFailPos}");

        var diags = DiagnosticDerivation.DeriveDiagnostics(node, input);

        var skipped = diags.Where(d => d.Kind == RecoveryKind.Skipped).ToList();
        Assert.AreEqual(1, skipped.Count, $"expected exactly 1 Skipped (absorber), got: {Describe(diags)}");
        Assert.AreEqual(input.Length, skipped[0].EndPos, $"absorber expected to end at EOF, got: {Describe(diags)}");

        // Диагностика несёт текст узла (спрос у дерева — единый источник правды).
        var nodeText = input.Substring(skipped[0].StartPos, skipped[0].EndPos - skipped[0].StartPos);
        Assert.AreEqual(nodeText, skipped[0].Message, $"absorber diagnostic must carry the node text, got: {Describe(diags)}");
        Assert.AreEqual("###", nodeText, $"absorber must cover the garbage \"###\", got text \"{nodeText}\": {Describe(diags)}");
    }

    // ============ 3. Unrecovered — НЕ узел дерева (A4-2: из результата) ============
    // Тот же вход, что в (2): S6-дно — принятый кандидат, поэтому в НАКОПЛЁННОМ списке есть
    // Unrecovered в точке восстановления. Но в ДЕРЕВЕ Unrecovered-узла нет (только абсорбер).
    // Чистый вывод из дерева, следовательно, НЕ содержит Unrecovered — он выводится из результата
    // (A4-2) и комбинируется с обходом дерева при подключении к API (6.1.2).
    [TestMethod]
    public void Test_DeriveDiagnostics_UnrecoveredIsNotATreeNode()
    {
        var parser = NewAbsorberParser();
        var input = "12 34 ###";
        var result = parser.Parse(input, "Module", out _);

        Assert.IsTrue(result.TryGetSuccess(out var node, out _), $"expected Success, got {result.ResultKind}");

        // Контроль: накопленный список действительно содержит Unrecovered (S6 — fallback).
        Assert.IsTrue(parser.RecoveryDiagnostics.Any(d => d.Kind == RecoveryKind.Unrecovered),
            $"precondition: accumulated list has Unrecovered (S6 fallback), got: {Describe(parser.RecoveryDiagnostics)}");

        // Чистый вывод из дерева НЕ содержит Unrecovered (маркер не является узлом дерева).
        var diags = DiagnosticDerivation.DeriveDiagnostics(node, input);
        Assert.IsFalse(diags.Any(d => d.Kind == RecoveryKind.Unrecovered),
            $"Unrecovered is result-derived (A4-2), not a tree node, so the tree walk must not emit it, got: {Describe(diags)}");
    }

    // ============ 4. Комбинация: вставка + абсорбер, детерминированный порядок по позиции ============
    // Module := a b c, вход "a c ###": пропущен "b" (вставка в pos 2) + хвост "###" (абсорбер).
    // Вывод содержит и Inserted, и Skipped; список неубывает по StartPos; вставка раньше абсорбера.
    [TestMethod]
    public void Test_DeriveDiagnostics_CombinedOrderedByPosition()
    {
        var parser = NewInsertionParser();
        var input = "a c ###";
        var result = parser.Parse(input, "Module", out _);

        Assert.IsTrue(result.TryGetSuccess(out var node, out var end) && end == input.Length,
            $"expected Success@EOF, got {result.ResultKind}@{result.NewPos}/{result.MaxFailPos}");

        var diags = DiagnosticDerivation.DeriveDiagnostics(node, input);

        Assert.IsTrue(diags.Any(d => d.Kind == RecoveryKind.Inserted), $"expected an Inserted, got: {Describe(diags)}");
        Assert.IsTrue(diags.Any(d => d.Kind == RecoveryKind.Skipped), $"expected a Skipped (absorber), got: {Describe(diags)}");

        // Детерминизм: список неубывает по StartPos.
        for (var i = 1; i < diags.Count; i++)
            Assert.IsTrue(diags[i - 1].StartPos <= diags[i].StartPos, $"diagnostics must be ordered by position, got: {Describe(diags)}");

        // Вставка (pos 2) раньше абсорбера (хвост).
        var inserted = diags.First(d => d.Kind == RecoveryKind.Inserted);
        var skipped = diags.First(d => d.Kind == RecoveryKind.Skipped);
        Assert.IsTrue(inserted.StartPos < skipped.StartPos, $"insertion must precede the absorber, got: {Describe(diags)}");
    }

    // ============ Хелперы ============

    // Module := a b c — пропущенный "b" вставляет нулевая вставка (S1).
    private static Parser NewInsertionParser()
    {
        var parser = new Parser(RecoveryTerminals.Trivia());
        parser.Rules["Module"] = [new Seq([new Literal("a"), new Literal("b"), new Literal("c")], "Module")];
        parser.BuildTdoppRules();
        return parser;
    }

    // Module := Expr Expr, Expr := Number — хвостовой мусор глотает абсорбер (S6-дно).
    private static Parser NewAbsorberParser()
    {
        var parser = new Parser(RecoveryTerminals.Trivia());
        parser.Rules["Expr"] = [RecoveryTerminals.Number()];
        parser.Rules["Module"] = [new Seq([new Ref("Expr"), new Ref("Expr")], "Module")];
        parser.BuildTdoppRules();
        return parser;
    }

    private static string Describe(IReadOnlyList<RecoveryDiagnostic> diags)
        => string.Join("; ", diags.Select(d => $"{d.Kind} [{d.StartPos}..{d.EndPos}) \"{d.Message}\""));
}
