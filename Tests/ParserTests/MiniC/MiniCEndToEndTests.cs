#nullable enable

using System.Text;
using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace MiniC;

// E2E-набор на MiniC (чек-лист 3.1, план v2 §4 Фаза 3 + §3.7 инварианты I4/I5/I6 + §3.9 аннотации).
// Грамматика — общая MiniCGrammar (Terminals — сгенерированные). Если сценарию нужны авторские
// аннотации — RecoveryRule-обёртки добавляются в MiniCGrammar.ConfigureRules (см. заметки в чек-листе 3.1).
[TestClass]
public sealed class MiniCEndToEndTests
{
    private readonly Parser _parser = new(Terminals.Trivia());

    [TestInitialize]
    public void Initialize()
        => MiniCGrammar.ConfigureRules(_parser);

    // ============ Тест 1: пропущенная } функции (модуль) ============

    [TestMethod]
    public void Test_MissingClosingBrace_Function()
    {
        // Функция внутри модуля, пропущена закрывающая } (тело завершено, ; на месте).
        // После } идёт следующая функция — есть точка resync (одиночная функция с } в EOF
        // уводит recovery-цикл в бесконечную рекурсию re-parse — см. заметки 3.1).
        var input = "int foo() { int x;\nint bar() { return 0; }";
        var result = _parser.Parse(input, "Module", out _);

        Assert.IsTrue(result.TryGetSuccess(out var node, out var end),
            $"Expected Success@EOF, got {result.ResultKind}@{result.NewPos}/{result.MaxFailPos} len={input.Length} ErrorInfo={_parser.ErrorInfo?.Pos}");
        Assert.AreEqual(input.Length, end);
        Assert.IsNull(_parser.ErrorInfo);
        Assert.IsTrue(CostCalculator.CountRecoveryNodes((Node)node) > 0,
            $"Expected recovery nodes in tree, got 0. Diagnostics: {Describe(_parser.RecoveryDiagnostics)}");
    }

    // ============ Тест 2: 2 пропущенные ; (по одной в каждом блоке) ============
    // Отклонение от ТЗ (≥2 Inserted-диагностики): пропущенная ; в MiniC — OftenMissed,
    // восстанавливается S0 (re-parse) БЕЗ диагностики (Inserted-диагностику даёт только
    // engine S1, а plain-Literal ; ломает recovery на этой грамматике — проверено).
    // Поэтому проверяем Success@EOF + ≥2 recovery-узла в дереве (по одному на пропущенную ;).
    // Также recovery MiniC восстанавливает только 1 пропущенную ; на блок, поэтому 2 ; —
    // в двух блоках (двух функциях).

    [TestMethod]
    public void Test_MissingSemicolon_MultipleStatements()
    {
        var input = "int f1() { int x int y; } int f2() { int a int b; }";
        var result = _parser.Parse(input, "Module", out _);
        var node = result.TryGetSuccess(out var n, out var end) ? (Node)n : null;

        Assert.IsTrue(node is not null && end == input.Length,
            $"Expected Success@EOF, got {result.ResultKind}@{result.NewPos}/{result.MaxFailPos} len={input.Length} ErrorInfo={_parser.ErrorInfo?.Pos} passes={_parser.RecoveryPasses} diag={Describe(_parser.RecoveryDiagnostics)} tree={(node is null ? "-" : DescribeTree(node))}");
        Assert.IsNull(_parser.ErrorInfo);

        var recoveryNodes = CostCalculator.CountRecoveryNodes(node!);
        Assert.IsTrue(recoveryNodes >= 2,
            $"Expected >= 2 recovery nodes (one per missing ;), got {recoveryNodes}. tree={DescribeTree(node!)}");
    }

    // ============ Тест 3: неожиданный токен в выражении ============

    [TestMethod]
    public void Test_UnexpectedToken_Expression()
    {
        // @ — ErrorOperator (regex [\\\/*+\-<=>!@#$%^&]+), восстанавливается RecoveryOperator-правилом.
        var input = "int foo() { int z; z = x @ 5; return z; }";
        var result = _parser.Parse(input, "Module", out _);

        var atEof = result.TryGetSuccess(out var node, out var end) && end == input.Length
            || result.TryGetPartial(out var pnode, out var pend) && pend == input.Length;
        Assert.IsTrue(atEof,
            $"Expected recovered (Success/Partial)@EOF, got {result.ResultKind}@{result.NewPos}/{result.MaxFailPos} len={input.Length} ErrorInfo={_parser.ErrorInfo?.Pos}");

        // §3.6: восстановлено до EOF ⇒ ErrorInfo == null.
        if (atEof)
            Assert.IsNull(_parser.ErrorInfo);
        else
            Assert.IsNotNull(_parser.ErrorInfo);
    }

    // ============ Тест 4: две соседние функции, в каждой пропущена ( ============
    // Отклонение от ТЗ (пропущенная }): } — OftenMissed, восстанавливается S0 БЕЗ диагностики.
    // Пропущенная ( не имеет OftenMissed → engine S1 вставляет → Inserted-диагностика.
    // Поэтому в каждой функции пропущена ( — обе ошибки дают Inserted-диагностику.

    [TestMethod]
    public void Test_NestedErrors_MultipleBlocks()
    {
        var input = "int foo ) { return 1; }\nint bar ) { return 2; }";
        var result = _parser.Parse(input, "Module", out _);

        Assert.IsTrue(result.TryGetSuccess(out var node, out var end) && end == input.Length,
            $"Expected Success@EOF, got {result.ResultKind}@{result.NewPos}/{result.MaxFailPos} len={input.Length} ErrorInfo={_parser.ErrorInfo?.Pos} diag={Describe(_parser.RecoveryDiagnostics)}");
        Assert.IsNull(_parser.ErrorInfo);

        var diags = _parser.RecoveryDiagnostics.ToList();
        Assert.IsTrue(diags.Count >= 2,
            $"Expected >= 2 diagnostics (one per missing }}), got {diags.Count}. All: {Describe(diags)} tree={DescribeTree((Node)node)}");
    }

    // ============ Тест 5: корректная программа + хвостовой мусор до true-EOF ============
    // Сценарий true-EOF trailing-garbage: корректная функция, затем `###` в самом конце
    // (до EOF). Бюджет 16 (глубокий сценарий). После 3.0a (stack guard) + 3.0b (бюджет 3)
    // + 3.0c (абсорбер на уровне цикла) — Success@EOF + Skipped-диагностика + абсорбер-узел.

    [TestMethod]
    public void Test_TrailingGarbage()
    {
        _parser.MaxRecoveryAttemptsPerPosition = 16;
        var input = "int foo() { return 0; } ###";
        var result = _parser.Parse(input, "Module", out _);

        Assert.IsTrue(result.TryGetSuccess(out var node, out var end) && end == input.Length,
            $"Expected Success@EOF, got {result.ResultKind}@{result.NewPos}/{result.MaxFailPos} len={input.Length} ErrorInfo={_parser.ErrorInfo?.Pos} passes={_parser.RecoveryPasses} gen={_parser.EngineGenerateCalls} snap={(_parser.LastSnapshot is null ? "null" : $"pos={_parser.LastSnapshot.Pos} top={_parser.LastSnapshot.Stack[^1].RuleName} failed={_parser.LastSnapshot.FailedTerminal?.Kind}")} diag={Describe(_parser.RecoveryDiagnostics)}");

        // Абсорбер мусора: Skipped-диагностика (kind Trailing или аналог).
        var skipped = _parser.RecoveryDiagnostics.Where(d => d.Kind == RecoveryKind.Skipped).ToList();
        Assert.IsTrue(skipped.Count >= 1,
            $"Expected >= 1 Skipped diagnostic (absorber), got {skipped.Count}. All: {Describe(_parser.RecoveryDiagnostics)}");

        // Мусор в дереве: абсорбер — recovery-узел (IsRecovery).
        var recoveryNodes = CostCalculator.CountRecoveryNodes((Node)node);
        Assert.IsTrue(recoveryNodes >= 1,
            $"Expected >= 1 recovery node (absorber), got {recoveryNodes}. tree={DescribeTree((Node)node)}");
    }

    // ============ Тест 6 (I6, §3.7): recovery латентен — корректный код даёт ровно дерево без recovery ============

    [TestMethod]
    public void Test_Recovery_DoesNotBreakCorrectCode()
    {
        var input = "int add(a, b) { return a + b; }\nint main() { return add(1, 2); }";
        var result = _parser.Parse(input, "Module", out _);

        Assert.IsTrue(result.TryGetSuccess(out var node, out var end) && end == input.Length,
            $"Expected Success@EOF, got {result.ResultKind}@{result.NewPos}/{result.MaxFailPos} len={input.Length} ErrorInfo={_parser.ErrorInfo?.Pos} diag={Describe(_parser.RecoveryDiagnostics)}");
        Assert.IsNull(_parser.ErrorInfo);
        Assert.AreEqual(0, CostCalculator.CountRecoveryNodes((Node)node),
            $"Expected zero recovery nodes on correct code (recovery is latent), got {CostCalculator.CountRecoveryNodes((Node)node)}. tree={DescribeTree((Node)node)}");
    }

    // ============ Тест 7 (I4, §3.7): каждый символ входа — ровно в одном терминальном узле (без дыр/наложений) ============

    [TestMethod]
    public void Test_EverythingRepresentedInTree()
    {
        _parser.MaxRecoveryAttemptsPerPosition = 16;
        var input = "int foo() { return 0; } ###";
        var result = _parser.Parse(input, "Module", out _);

        Assert.IsTrue(result.TryGetSuccess(out var node, out var end) && end == input.Length,
            $"Expected Success@EOF, got {result.ResultKind}@{result.NewPos}/{result.MaxFailPos} len={input.Length} ErrorInfo={_parser.ErrorInfo?.Pos} diag={Describe(_parser.RecoveryDiagnostics)}");

        // I4: терминальные узлы (нормальные + вставленные + абсорберы) тайлом покрывают [0, input.Length).
        // ВАЖНО: прогон по [StartPos, EndPos), а НЕ [StartPos, StartPos+ContentLength): хвостовой trivia
        // (белые) поглощается в EndPos СЛЕДУЮЩЕГО терминала (Parser.ParseTerminal), а не отдельным узлом,
        // поэтому ContentLength < EndPos-StartPos и [StartPos, StartPos+ContentLength) дал бы дыры на trivia.
        Assert.IsTrue(SpansTileInput((Node)node, input.Length, out var detail),
            $"Terminal spans must tile [0, {input.Length}) exactly. {detail} tree={DescribeTree((Node)node)}");
    }

    // ============ Тест 8: восстановление доходит до конца строки (end == input.Length) ============

    [TestMethod]
    public void Test_ParsingReachesEndOfString()
    {
        var input = "int f1() { int x int y; } int f2() { int a int b; }";
        var result = _parser.Parse(input, "Module", out _);

        var atEof = result.TryGetSuccess(out _, out var end) && end == input.Length
            || result.TryGetPartial(out _, out var pend) && pend == input.Length;
        Assert.IsTrue(atEof,
            $"Expected parsing to reach end of string (Success/Partial @EOF), got {result.ResultKind}@{result.NewPos}/{result.MaxFailPos} len={input.Length} ErrorInfo={_parser.ErrorInfo?.Pos} diag={Describe(_parser.RecoveryDiagnostics)}");
    }

    // ============ Тест 9 (I5, §3.7): детерминизм — один вход + один грамматика ⇒ идентичное дерево и диагностика ============

    [TestMethod]
    public void Test_Deterministic()
    {
        Parser BuildParser()
        {
            var p = new Parser(Terminals.Trivia());
            MiniCGrammar.ConfigureRules(p);
            p.MaxRecoveryAttemptsPerPosition = 16;
            return p;
        }

        var input = "int foo() { return 0; } ###";
        var p1 = BuildParser();
        var p2 = BuildParser();

        var r1 = p1.Parse(input, "Module", out _);
        var r2 = p2.Parse(input, "Module", out _);

        Assert.IsTrue(r1.TryGetSuccess(out var n1, out var e1) && e1 == input.Length,
            $"run1: Expected Success@EOF, got {r1.ResultKind}@{r1.NewPos}/{r1.MaxFailPos} len={input.Length} diag={Describe(p1.RecoveryDiagnostics)}");
        Assert.IsTrue(r2.TryGetSuccess(out var n2, out var e2) && e2 == input.Length,
            $"run2: Expected Success@EOF, got {r2.ResultKind}@{r2.NewPos}/{r2.MaxFailPos} len={input.Length} diag={Describe(p2.RecoveryDiagnostics)}");

        // I5: идентичное дерево (Kind/StartPos/EndPos/IsRecovery в порядке обхода).
        Assert.AreEqual(CanonicalTree((Node)n1), CanonicalTree((Node)n2),
            "Trees must be structurally identical across runs (I5 determinism).");

        // I5: идентичная диагностика (Kind/StartPos/EndPos/Terminal.Kind/RuleName).
        Assert.AreEqual(Describe(p1.RecoveryDiagnostics), Describe(p2.RecoveryDiagnostics),
            "Recovery diagnostics must be identical across runs (I5 determinism).");
    }

    // ============ Хелперы ============

    private static string Describe(IReadOnlyList<RecoveryDiagnostic> diags)
        => string.Join("; ", diags.Select(d => $"{d.Kind} [{d.StartPos}..{d.EndPos}) term={d.Terminal?.Kind ?? "-"} rule={d.RuleName ?? "-"}"));

    private static string DescribeTree(Node node)
    {
        var sb = new StringBuilder();
        DescribeTree(node, sb);
        return sb.ToString();
    }

    private static void DescribeTree(Node node, StringBuilder sb)
    {
        var content = node is TerminalNode t ? $" len={t.ContentLength}" : "";
        var rec = node.IsRecovery ? " [REC]" : "";
        sb.Append($"{node.Kind}[{node.StartPos}-{node.EndPos}){content}{rec} ");
        switch (node)
        {
            case SeqNode seq:
                foreach (var el in seq.RawElements) DescribeTree((Node)el, sb);
                break;
            case ListNode list:
                foreach (var el in list.RawElements) DescribeTree((Node)el, sb);
                foreach (var d in list.Delimiters) DescribeTree((Node)d, sb);
                break;
            case SomeNode some:
                DescribeTree((Node)some.Value, sb);
                break;
        }
    }

    // I4: собирает спаны терминальных узлов [StartPos, EndPos) и проверяет точное покрытие [0, inputLength):
    // первая с 0, последняя до inputLength, смежные без дыр/наложений.
    private static bool SpansTileInput(Node root, int inputLength, out string detail)
    {
        var spans = new List<(int Start, int End)>();
        CollectTerminalSpans(root, spans);
        spans.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.End.CompareTo(b.End));

        if (spans.Count == 0)
        {
            detail = "no terminal nodes in tree";
            return false;
        }
        if (spans[0].Start != 0)
        {
            detail = $"first span starts at {spans[0].Start}, expected 0";
            return false;
        }
        for (var i = 1; i < spans.Count; i++)
        {
            if (spans[i].Start != spans[i - 1].End)
            {
                detail = $"gap/overlap between span[{i - 1}]={spans[i - 1].Start}..{spans[i - 1].End} and span[{i}]={spans[i].Start}..{spans[i].End}";
                return false;
            }
        }
        if (spans[^1].End != inputLength)
        {
            detail = $"last span ends at {spans[^1].End}, expected {inputLength}";
            return false;
        }
        detail = $"ok ({spans.Count} terminal spans)";
        return true;
    }

    private static void CollectTerminalSpans(Node node, List<(int Start, int End)> spans)
    {
        if (node is TerminalNode t)
            spans.Add((t.StartPos, t.EndPos));
        switch (node)
        {
            case SeqNode seq:
                foreach (var el in seq.RawElements) CollectTerminalSpans((Node)el, spans);
                break;
            case ListNode list:
                foreach (var el in list.RawElements) CollectTerminalSpans((Node)el, spans);
                foreach (var d in list.Delimiters) CollectTerminalSpans((Node)d, spans);
                break;
            case SomeNode some:
                CollectTerminalSpans((Node)some.Value, spans);
                break;
        }
    }

    // I5: каноническая сериализация дерева — Kind/StartPos/EndPos/IsRecovery каждого узла в pre-order.
    private static string CanonicalTree(Node node)
    {
        var sb = new StringBuilder();
        WriteCanonical(node, sb);
        return sb.ToString();
    }

    private static void WriteCanonical(Node node, StringBuilder sb)
    {
        sb.Append(node.Kind).Append('/').Append(node.StartPos).Append('-').Append(node.EndPos).Append('/').Append(node.IsRecovery ? 1 : 0).Append(' ');
        switch (node)
        {
            case SeqNode seq:
                foreach (var el in seq.RawElements) WriteCanonical((Node)el, sb);
                break;
            case ListNode list:
                foreach (var el in list.RawElements) WriteCanonical((Node)el, sb);
                foreach (var d in list.Delimiters) WriteCanonical((Node)d, sb);
                break;
            case SomeNode some:
                WriteCanonical((Node)some.Value, sb);
                break;
        }
    }
}
