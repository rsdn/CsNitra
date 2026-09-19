#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

#if RECOVERY
namespace Recovery;

// 5a.1.3: GenerateS2 читает/пишет ОБЩИЙ кэш parser.SpecCache, а не локальный Dictionary.
// Терминалы — точная копия TierBudgetTests.TierBudgetTerminals (имя изменено, чтобы не было
// CS0101: оба файла в namespace Recovery).
[TerminalMatcher]
public sealed partial class SpecCacheSharedTerminals
{
    [Regex(@"\d+")]
    public static partial Terminal Number();

    [Regex(@"[_\l]\w*")]
    public static partial Terminal Ident();

    [Regex(@"\s*")]
    public static partial Terminal Trivia();
}

// B2 (5a.1.3): переключение GenerateS2 на общий кэш. До правки локальный Dictionary не инкрементит
// обёртку SpeculativeCache → SpecCacheMisses == 0; после правки GenerateS2 пишет в parser.SpecCache →
// SpecCacheMisses > 0.
[TestClass]
public sealed class SpecCacheSharedTests
{
    // Грамматика TierBudgetTests: Module := '{' ZeroOrMany(Stmt) '}' ; Stmt := Ident ':' Expr ';' ;
    // Expr — TDOPP с шестью операторами.
    private static Parser NewParser()
    {
        var parser = new Parser(SpecCacheSharedTerminals.Trivia());
        parser.Rules["Expr"] = new Rule[]
        {
            SpecCacheSharedTerminals.Number(),
            new Seq([new Ref("Expr"), new Literal("+"), new ReqRef("Expr", 100)], "Add"),
            new Seq([new Ref("Expr"), new Literal("-"), new ReqRef("Expr", 100)], "Sub"),
            new Seq([new Ref("Expr"), new Literal("*"), new ReqRef("Expr", 200)], "Mul"),
            new Seq([new Ref("Expr"), new Literal("/"), new ReqRef("Expr", 200)], "Div"),
            new Seq([new Ref("Expr"), new Literal("=="), new ReqRef("Expr", 50)], "Eq"),
            new Seq([new Ref("Expr"), new Literal("!="), new ReqRef("Expr", 50)], "Neq"),
        };
        parser.Rules["Stmt"] = [new Seq([SpecCacheSharedTerminals.Ident(), new Literal(":"), new Ref("Expr"), new Literal(";")], "Stmt")];
        parser.Rules["Module"] = [new Seq([new Literal("{"), new ZeroOrMany(new Ref("Stmt"), "Stmts"), new Literal("}")], "Module")];
        parser.BuildTdoppRules();
        return parser;
    }

    [TestMethod]
    public void Test_GenerateS2_Writes_To_Shared_SpecCache()
    {
        var parser = NewParser();
        // Ident 'b' после мусора: скан S2 FirstMatchesAt(Ref(Stmt))=Ident совпадает на 'b' → Speculative вызывается.
        // ('{ a: 1+ ### ; }' без 'b' не трогает Speculative: First(Stmt) не совпадает с '### ;', выигрывает S3.)
        var input = "{ a: 1+ ### b ; }";
        parser.Parse(input, "Module", out _);

        // GenerateS2 (resync) спекулятивно парсит якоря через общий кэш → хотя бы один промах.
        // До правки (локальный Dictionary) обёртка не инкрементится → SpecCacheMisses == 0.
        Assert.IsTrue(parser.SpecCacheMisses > 0,
            $"Expected SpecCacheMisses > 0 (GenerateS2 writes to the shared parser.SpecCache), got " +
            $"{parser.SpecCacheMisses}; passes={parser.RecoveryPasses} gen={parser.EngineGenerateCalls} " +
            $"diags=[{string.Join("; ", parser.RecoveryDiagnostics.Select(d => $"{d.Kind} [{d.StartPos}..{d.EndPos}) {d.Message}"))}]");
    }
}
#endif
