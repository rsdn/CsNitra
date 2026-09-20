#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;

#if RECOVERY
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
}
#endif
