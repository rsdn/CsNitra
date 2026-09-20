#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace Recovery;

// A3: RecoveryProfile — единый источник recovery-лимитов. Тестируем, что Profile можно установить
// и что convenience-свойства (MaxRecoveryIterations и т.д.) читают/пишут Profile.
[TestClass]
public sealed class RecoveryProfileTests
{
    private static Parser NewParser() => new(new Literal(" "));

    // (1) Profile можно установить; значения читаются через convenience-свойства.
    [TestMethod]
    public void Test_Profile_Sets_And_Reads_Through_Properties()
    {
        var parser = NewParser();
        parser.Profile = RecoveryProfile.Compiler;

        Assert.AreEqual(RecoveryMode.Compiler, parser.Profile.Mode);
        Assert.AreEqual(64, parser.MaxRecoveryIterations);
        Assert.AreEqual(4, parser.S1TierBudget);
        Assert.AreEqual(2, parser.S2TierBudget);
        Assert.AreEqual(2, parser.S3S6TierBudget);
        Assert.AreEqual(1000, parser.Profile.MaxSkip);
    }

    // (2) Смена Profile.MaxIterations через MaxRecoveryIterations setter работает (with-копия).
    [TestMethod]
    public void Test_MaxRecoveryIterations_Setter_Updates_Profile()
    {
        var parser = NewParser();
        Assert.AreEqual(1000, parser.MaxRecoveryIterations);

        parser.MaxRecoveryIterations = 7;

        Assert.AreEqual(7, parser.MaxRecoveryIterations);
        Assert.AreEqual(7, parser.Profile.MaxIterations);
        // Остальные значения профиля не затронуты.
        Assert.AreEqual(4, parser.Profile.S1TierBudget);
        Assert.AreEqual(RecoveryMode.Ide, parser.Profile.Mode);
    }

    // Ide-профиль по умолчанию: значения = текущим хардкодам.
    [TestMethod]
    public void Test_IdeProfile_Defaults_Match_Hardcoded()
    {
        var parser = NewParser();

        Assert.AreEqual(RecoveryMode.Ide, parser.Profile.Mode);
        Assert.AreEqual(1000, parser.Profile.MaxIterations);
        Assert.AreEqual(4, parser.Profile.S1TierBudget);
        Assert.AreEqual(2, parser.Profile.S2TierBudget);
        Assert.AreEqual(2, parser.Profile.S3S6TierBudget);
        Assert.AreEqual(1000, parser.Profile.MaxSkip);
        Assert.AreEqual(128, parser.Profile.MaxParseDepthBase);
        Assert.AreEqual(4, parser.Profile.MaxParseDepthPerChar);
    }

    // A4-5.2: Compiler-профиль — bail на первом падении. MiniC-грамматика (паттерн RecoveryCorpusTests):
    // с recovery-правилами в Expr, чтобы Ide-профиль мог восстанавливать тот же вход.
    private static Parser NewMiniCParser()
    {
        var parser = new Parser(CorpusTerminals.Trivia());
        var closingBrace = new OftenMissed(new Literal("}"));
        var semicolon = new OftenMissed(new Literal(";"));

        parser.Rules["Expr"] = new Rule[]
        {
            CorpusTerminals.Number(),
            CorpusTerminals.Ident(),
            new Seq([new Ref("Expr"), new Literal("+"), new ReqRef("Expr", 100)], "Add"),
            new Seq([new Ref("Expr"), new Literal("="), new ReqRef("Expr", 10, Right: true)], "Assignment"),

            // Recovery rules:
            new Seq([new Ref("Expr"), CorpusTerminals.ErrorOperator(), new ReqRef("Expr", 200)], "RecoveryOperator"),
            new Seq([new Ref("Expr"), CorpusTerminals.ErrorEmpty(), new ReqRef("Expr", 200)], "RecoveryEmptyOperator"),
            CorpusTerminals.ErrorEmpty(),
        };

        parser.Rules["Statement"] = new Rule[]
        {
            new Seq([new Literal("int"), CorpusTerminals.Ident(), semicolon], "VarDecl"),
            new Seq([new Ref("Expr"), semicolon], "ExprStmt"),
        };

        parser.Rules["Block"] = new Rule[]
        {
            new Seq([new Literal("{"), new ZeroOrMany(new Seq([new NotPredicate(new Ref("Function")), new Ref("Statement")], "BlockStatement")), closingBrace], "MultiBlock"),
            new Ref("Statement", "SimplBlock"),
        };

        parser.Rules["Function"] = new Rule[]
        {
            new Seq([new Literal("int"), CorpusTerminals.Ident(), new Literal("("), new Literal(")"), new Ref("Block")], "FunctionDecl"),
        };

        parser.Rules["Module"] = [new ZeroOrMany(new Ref("Function"), "ModuleFunctions")];

        parser.BuildTdoppRules();
        return parser;
    }

    // A4-5.2: Compiler-профиль выходит из Recover сразу после первого ParseRule без Success@EOF:
    // сырой результат падения, без S0/Generate/re-parse/rollback. FinalizeResult (из Parse) ставит ErrorInfo.
    [TestMethod]
    public void Test_CompilerProfile_BailsOnFirstFailure()
    {
        // Несколько ошибок: Ide-профилю нужно >1 прохода, чтобы дойти до EOF (контраст ниже).
        const string input = "int f() { z = x $^ 5; z = a $^ 6; z = b $^ 7; z = c $^ 8; z = d $^ 9; }";

        // Compiler: bail на первом падении — сырой результат, recovery не предпринимается.
        var compiler = NewMiniCParser();
        compiler.Profile = RecoveryProfile.Compiler;
        compiler.Parse(input, "Module", out _);

        Assert.IsNotNull(compiler.ErrorInfo, "Compiler profile must return the raw failure (ErrorInfo set)");
        Assert.AreEqual(1, compiler.RecoveryPasses, "Compiler profile must bail after exactly one pass");
        Assert.AreEqual(0, compiler.EngineGenerateCalls, "Compiler profile must not call Generate");

        // Ide (контраст): тот же вход восстанавливается до EOF за несколько проходов.
        var ide = NewMiniCParser();
        ide.Profile = RecoveryProfile.Ide;
        var ideResult = ide.Parse(input, "Module", out _);

        Assert.IsTrue(ideResult.TryGetSuccess(out _, out var end) && end == input.Length,
            $"Ide profile must recover to EOF, got {ideResult.ResultKind}@{ideResult.NewPos}/{ideResult.MaxFailPos} ErrorInfo={ide.ErrorInfo?.Pos}");
        Assert.IsNull(ide.ErrorInfo);
        Assert.IsTrue(ide.RecoveryPasses > 1, $"Ide profile must use multiple passes, got {ide.RecoveryPasses}");
    }
}
