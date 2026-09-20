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

    // B4: degradation table — level -> strategy parameters, clamping, and profile TimeBudgets.
    [TestMethod]
    public void Test_Degradation_Table_And_TimeBudgets()
    {
        var l0 = Degradation.Get(0);
        Assert.AreEqual(RecoveryStrategy.All, l0.Mask);
        Assert.AreEqual(1.0, l0.MaxSkipMultiplier);
        Assert.IsTrue(l0.Speculation);
        Assert.IsFalse(l0.ForceS6);

        var l1 = Degradation.Get(1);
        Assert.AreEqual(RecoveryStrategy.All, l1.Mask);
        Assert.AreEqual(4.0, l1.MaxSkipMultiplier);
        Assert.IsFalse(l1.Speculation);

        var l2 = Degradation.Get(2);
        Assert.AreEqual(RecoveryStrategy.S1 | RecoveryStrategy.S3 | RecoveryStrategy.S6, l2.Mask);
        Assert.AreEqual(0.5, l2.MaxSkipMultiplier);

        var l3 = Degradation.Get(3);
        Assert.AreEqual(RecoveryStrategy.S6, l3.Mask);
        Assert.IsTrue(l3.ForceS6);

        // Clamping: out-of-range indices map to the nearest level.
        Assert.AreEqual(Degradation.Levels[0], Degradation.Get(-1));
        Assert.AreEqual(Degradation.Levels[3], Degradation.Get(99));

        // TimeBudget: Test disables it (Infinite), Ide uses the 500ms default.
        Assert.AreEqual(Timeout.InfiniteTimeSpan, RecoveryProfile.Test.TimeBudget);
        Assert.AreEqual(TimeSpan.FromMilliseconds(500), RecoveryProfile.Ide.TimeBudget);
    }

    // B4: effective properties reflect DegradationLevel (deterministic, manual level setting — no wall-clock).
    // Level 0 (default): full strategy, full MaxSkip, speculation on, no forced S6.
    [TestMethod]
    public void Test_EffectiveProperties_Level0_Default()
    {
        var parser = NewParser();
        Assert.AreEqual(0, parser.DegradationLevel);

        Assert.AreEqual(RecoveryStrategy.All, parser.EffectiveStrategyMask);
        Assert.AreEqual(parser.Profile.MaxSkip, parser.EffectiveMaxSkip);
        Assert.IsTrue(parser.SpeculationEnabled);
        Assert.IsFalse(parser.ForceS6);
    }

    // B4: level 1 — MaxSkip x4 (expansion), speculation off, full mask, no forced S6.
    [TestMethod]
    public void Test_EffectiveProperties_Level1()
    {
        var parser = NewParser();
        parser.DegradationLevel = 1;

        Assert.AreEqual(parser.Profile.MaxSkip * 4, parser.EffectiveMaxSkip);
        Assert.IsFalse(parser.SpeculationEnabled);
        Assert.AreEqual(RecoveryStrategy.All, parser.EffectiveStrategyMask);
        Assert.IsFalse(parser.ForceS6);
    }

    // B4: level 2 — mask narrows to S1|S3|S6, speculation off.
    [TestMethod]
    public void Test_EffectiveProperties_Level2()
    {
        var parser = NewParser();
        parser.DegradationLevel = 2;

        Assert.AreEqual(RecoveryStrategy.S1 | RecoveryStrategy.S3 | RecoveryStrategy.S6, parser.EffectiveStrategyMask);
        Assert.IsFalse(parser.SpeculationEnabled);
    }

    // B4: level 3 — mask narrows to S6, forced S6.
    [TestMethod]
    public void Test_EffectiveProperties_Level3()
    {
        var parser = NewParser();
        parser.DegradationLevel = 3;

        Assert.AreEqual(RecoveryStrategy.S6, parser.EffectiveStrategyMask);
        Assert.IsTrue(parser.ForceS6);
    }

    // B4: base StrategyMask is respected — EffectiveStrategyMask at level 0 == the profile mask.
    [TestMethod]
    public void Test_EffectiveProperties_Respects_Base_StrategyMask()
    {
        var parser = NewParser();
        var mask = RecoveryStrategy.All & ~RecoveryStrategy.S2;
        parser.Profile = parser.Profile with { StrategyMask = mask };
        parser.DegradationLevel = 0;

        Assert.AreEqual(mask, parser.EffectiveStrategyMask);
    }
}
