#nullable enable

using ExtensibleParser;

#if RECOVERY
namespace Recovery;

// 2.1/B1: точная гигиена memo. Сценарий «одна ошибка в конце длинного файла»:
// re-парсинг после восстановления должен удалять только невалидные записи (start-правило на
// currentStartPos + Failure в ре-парс-регионе), а не всю таблицу мемо. Иначе число удалений
// было бы пропорционально длине файла (здесь ~200), а не ограничено.
[TestClass]
public sealed class HygieneTests
{
    // Module := ZeroOrMany(Stmt); Stmt := Seq("a", ";"). Без recovery-правил: хвостовой мусор
    // восстанавливается нижним слоем (S3/S6), как в FinalStateTests (хвостовой мусор → Success@EOF).
    private static Parser NewParser()
    {
        var parser = new Parser(new EmptyTerminal("Trivia"));
        parser.Rules["Module"] = [new ZeroOrMany(new Ref("Stmt"), "ModuleStatements")];
        parser.Rules["Stmt"] = [new Seq([new Literal("a"), new Literal(";")], "Stmt")];
        parser.BuildTdoppRules();
        return parser;
    }

    // Одна ошибка (буква "b") в конце файла из 200 корректных "a;":
    //  - восстановление доводит парсинг до Success@EOF;
    //  - HygieneRemovals ограничен (не пропорционален 200) — гигиена точная.
    [TestMethod]
    public void OneErrorAtEndOfLongFile_HygieneIsBounded()
    {
        const int statements = 200;
        var input = string.Concat(Enumerable.Repeat("a;", statements)) + "b";
        var parser = NewParser();

        var result = parser.Parse(input, "Module", out _);

        Assert.IsTrue(result.TryGetSuccess(out _, out var end),
            $"Expected Success@EOF, got {result.ResultKind}@{result.NewPos}/{result.MaxFailPos} (len={input.Length})");
        Assert.AreEqual(input.Length, end, "Recovery must reach EOF");

        // Точная гигиена: число удалений не растёт с длиной файла. Старая (неточная) гигиена
        // удаляла всю мемо (~202 записи) на каждом кандидате → число удалений было бы ~200+.
        Assert.IsTrue(parser.HygieneRemovals <= 10,
            $"HygieneRemovals={parser.HygieneRemovals} is not bounded (expected <= 10, not proportional to {statements} statements)");
    }
}
#endif
