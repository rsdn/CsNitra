#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace Recovery;

// A4-7 (7.5.2): применение BuildExpectedSet (7.5.1) к финальным артефактам — FatalError
// (FinalizeResult) и Unrecovered (A4-2, AddUnrecoveredIfS6) — при наличии FailureSnapshot в точке E.
// No-snapshot fallback: старое поведение (FatalError — _expected, Unrecovered — сообщение без expected).
[TestClass]
public sealed class ExpectedSetApplicationTests
{
    private sealed record SpaceTrivia(string Name) : Terminal(Name)
    {
        public override int TryMatch(string input, int startPos)
        {
            var len = 0;
            while (startPos + len < input.Length && input[startPos + len] == ' ')
                len++;
            return len;
        }
    }

    // Форма 7.5.1: Start := "a" Body "z"; Body := "b" Mid "c"; Mid := Seq("m").
    // Вход "abX": падение в e=2 (на 'X'), снимок существует в e: топ-кадр Mid Seq(0), Expected = {m}.
    // Источники: s1 (Expected топа) = {m}, s2 (терминаторы) = {c, EOF}, s3 (суффиксные First) = {c, z}
    // → объединение {m, c, z, EOF}.
    private static Parser NewParser()
    {
        var parser = new Parser(new SpaceTrivia("Trivia"));
        parser.Rules["Start"] = [new Seq([new Literal("a"), new Ref("Body"), new Literal("z")], "Start")];
        parser.Rules["Body"] = [new Seq([new Literal("b"), new Ref("Mid"), new Literal("c")], "Body")];
        parser.Rules["Mid"] = [new Seq([new Literal("m")], "Mid")];
        parser.BuildTdoppRules();
        return parser;
    }

    // ============ 1. FatalError: полный контекстный expected-набор (снимок в E) ============

    [TestMethod]
    public void Test_FatalError_WithSnapshotAtE_FullExpectedSet()
    {
        var parser = NewParser();
        parser.MaxRecoveryIterations = 0; // без кандидатов — финальное состояние невосстановлено в e
        var result = parser.Parse("abX", "Start", out _);

        Assert.IsFalse(result.TryGetSuccess(out _, out var end) && end == 3,
            $"expected an unrecovered result, got {result.ResultKind}@{result.NewPos}");
        var error = parser.ErrorInfo;
        Assert.IsNotNull(error, "expected FatalError (unrecovered below EOF)");
        Assert.AreEqual(2, error.Pos, "FatalError must be at the recovery point e=2");

        // Полный контекстный набор: Expected топ-кадра («m»), терминатор («EOF»), First суффиксного
        // обязательства («z») — не только самая дальняя терминальная точка.
        var kinds = error.Expecteds.Select(t => t.Kind).ToList();
        CollectionAssert.AreEquivalent(new[] { "EOF", "c", "m", "z" }, kinds);
        Assert.IsTrue(kinds.Contains("m"), "set must contain the top-frame Expected 'm'");
        Assert.IsTrue(kinds.Contains("EOF"), "set must contain the terminator EOF");
        Assert.IsTrue(kinds.Contains("z"), "set must contain the suffix-obligation First 'z'");
    }

    // ============ 2. Контроль: снимка в E нет — старое поведение _expected (один терминал) ============

    [TestMethod]
    public void Test_FatalError_NoSnapshotAtE_OldExpectedBehavior()
    {
        var parser = new Parser(new SpaceTrivia("Trivia"));
        parser.Rules["Start"] = [new Literal("a")];
        parser.BuildTdoppRules();
        parser.MaxRecoveryIterations = 0; // без кандидатов — финальное состояние невосстановлено в e
        var result = parser.Parse("b", "Start", out _);

        Assert.IsFalse(result.IsSuccess);
        var error = parser.ErrorInfo;
        Assert.IsNotNull(error, "expected FatalError");
        Assert.AreEqual(0, error.Pos);
        // Mismatch в pos 0 == ErrorPos (не дальше) → снимка нет (FailureSnapshotAt(0) == null) —
        // fallback: старый _expected = {a} (одна самая дальняя терминальная точка).
        CollectionAssert.AreEqual(new[] { "a" }, error.Expecteds.Select(t => t.Kind).ToArray());
    }

    // ============ 3. Unrecovered (A4-2): полный контекстный набор в сообщении (снимок в E) ============

    [TestMethod]
    public void Test_Unrecovered_WithSnapshotAtE_FullExpectedSetInMessage()
    {
        var parser = NewParser();
        parser.ForcedDegradationLevel = 3; // hard limit: S0 без прогресса → форс-дно S6 → Unrecovered
        var result = parser.Parse("abX", "Start", out _);

        Assert.IsTrue(result.TryGetSuccess(out _, out var end) && end == 3,
            $"S6 bottom must reach EOF, got {result.ResultKind}@{result.NewPos}");
        Assert.IsNull(parser.ErrorInfo, "recovered (S6 bottom) → ErrorInfo = null (A5-4)");

        var unrecovered = parser.RecoveryDiagnostics.Where(d => d.Kind == RecoveryKind.Unrecovered).ToList();
        Assert.AreEqual(1, unrecovered.Count, $"expected exactly 1 Unrecovered, got: {Describe(parser.RecoveryDiagnostics)}");
        Assert.AreEqual(2, unrecovered[0].StartPos, "Unrecovered must be at the recovery point e=2");

        // «expecting» — полный контекстный набор: Expected топ-кадра («m»), терминатор («EOF»),
        // First суффиксного обязательства («z»).
        const string prefix = "error at 2 not recovered (absorbed to EOF), expecting: ";
        var message = unrecovered[0].Message;
        Assert.IsTrue(message.StartsWith(prefix), $"expected the A4-7 message with the expected set, got: {message}");
        var expected = message[prefix.Length..].Split(',').Select(s => s.Trim()).ToList();
        CollectionAssert.AreEquivalent(new[] { "EOF", "c", "m", "z" }, expected);
    }

    // ============ 4. Контроль: Unrecovered без снимка в E — старое сообщение без expected ============

    // Module := Expr Expr, Expr := "12" | "34": "12 34 ###" — Success до EOF без mismatch в точке e=6
    // (единственный mismatch при 3 «до» e) → снимка в e нет; S6-дно → Unrecovered со СТАРЫМ
    // сообщением (без «expecting») — no-snapshot fallback.
    [TestMethod]
    public void Test_Unrecovered_NoSnapshotAtE_OldMessage()
    {
        var parser = new Parser(new SpaceTrivia("Trivia"));
        parser.Rules["Module"] = [new Seq([new Ref("Expr"), new Ref("Expr")], "Module")];
        parser.Rules["Expr"] = [new Literal("12"), new Literal("34")];
        parser.BuildTdoppRules();
        var result = parser.Parse("12 34 ###", "Module", out _);

        Assert.IsTrue(result.TryGetSuccess(out _, out var end) && end == 9,
            $"S6 bottom must reach EOF, got {result.ResultKind}@{result.NewPos}");

        var unrecovered = parser.RecoveryDiagnostics.Where(d => d.Kind == RecoveryKind.Unrecovered).ToList();
        Assert.AreEqual(1, unrecovered.Count, $"expected exactly 1 Unrecovered, got: {Describe(parser.RecoveryDiagnostics)}");
        Assert.AreEqual("error at 6 not recovered (absorbed to EOF)", unrecovered[0].Message,
            "no snapshot at e → the old message without expected (fallback)");
    }

    private static string Describe(IReadOnlyList<RecoveryDiagnostic> diags)
        => string.Join("; ", diags.Select(d => $"{d.Kind} [{d.StartPos}..{d.EndPos}) {d.Message}"));
}
