#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace Recovery;

[TerminalMatcher]
public sealed partial class SingleTokenDeletionTerminals
{
    [Regex(@"\s*")]
    public static partial Terminal Trivia();
}

// 5a.4.2: single-token deletion (GenerateS1b). Грамматика S := 'a' 'b', вход "aab":
// 'a'@0→1, 'b' промах@1 (видит 'a') → mismatch в E=1, snapshot {Expected: {b}, FailedTerminal: b}.
// GenerateS1b: triviaLen = Trivia.TryMatch("aab", 2) = 0, pos = 2, len = 2 - 1 = 1.
// Literal("b").TryMatch("aab", 2) = 1 >= 0, 'b' ∈ Expected → абсорбер [1..2).
// Re-parse: 'a'@0→1, absorber skip@1→2, 'b'@2→3 = EOF.
// Главный ассерт (падает без правки): RecoveryDiagnostics содержит диагностику Kind == Extraneous
// (без правки её нет — принимается другой кандидат, не single-token deletion).
[TestClass]
public sealed class SingleTokenDeletionTests
{
    private static Parser NewParser()
    {
        var parser = new Parser(SingleTokenDeletionTerminals.Trivia());
        parser.Rules["S"] = [new Seq([new Literal("a"), new Literal("b")], "S")];
        parser.BuildTdoppRules();
        return parser;
    }

    [TestMethod]
    public void Test_SingleTokenDeletion_ExtraneousDiagnostic()
    {
        var parser = NewParser();
        var input = "aab";
        var result = parser.Parse(input, "S", out _);

        // Главный ассерт (падает без правки): есть диагностика Extraneous (S1b single-token deletion).
        Assert.IsTrue(
            parser.RecoveryDiagnostics.Any(d => d.Kind == RecoveryKind.Extraneous),
            $"Expected an Extraneous diagnostic (single-token deletion), got [{string.Join("; ", parser.RecoveryDiagnostics.Select(d => $"{d.Kind} [{d.StartPos}..{d.EndPos})"))}]");

        // Сопутствующие ассерты: восстановлено до EOF без ошибки.
        Assert.IsNull(parser.ErrorInfo);
        Assert.IsTrue(result.TryGetSuccess(out _, out var end) && end == 3,
            $"Expected Success@EOF (end=3), got {result.ResultKind}@{result.NewPos}/{result.MaxFailPos}");
    }
}
