using CsPreprocessor;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CsPreprocessorTests;

[TestClass]
public sealed class DirectiveStackTests
{
    private static DirectiveStack Create(params string[] commandLineSymbols) => new(commandLineSymbols);

    [TestMethod]
    public void InitialState_Active_NoUnfinishedIf_CmdlineFallback()
    {
        var stack = Create("CMD");

        Assert.IsTrue(stack.IsActive);
        Assert.IsFalse(stack.HasUnfinishedIf);
        Assert.IsTrue(stack.IsDefined("CMD"), "a command-line symbol is defined");
        Assert.IsFalse(stack.IsDefined("NOPE"), "a non-cmdline symbol is not defined");
    }

    [TestMethod]
    public void IfTrue_Active_HasUnfinishedIf_EndIfRestoresActive()
    {
        var stack = Create();

        Assert.IsTrue(stack.If(true));
        Assert.IsTrue(stack.IsActive);
        Assert.IsTrue(stack.HasUnfinishedIf);

        stack.EndIf();
        Assert.IsTrue(stack.IsActive);
        Assert.IsFalse(stack.HasUnfinishedIf);
    }

    [TestMethod]
    public void IfFalse_Inactive_EndIfRestoresActive()
    {
        var stack = Create();

        Assert.IsFalse(stack.If(false));
        Assert.IsFalse(stack.IsActive);
        Assert.IsTrue(stack.HasUnfinishedIf);

        stack.EndIf();
        Assert.IsTrue(stack.IsActive);
        Assert.IsFalse(stack.HasUnfinishedIf);
    }

    [TestMethod]
    public void IsDefined_DrivesIf()
    {
        var stack = Create();
        stack.Define("A");

        Assert.IsTrue(stack.If(stack.IsDefined("A")));
    }

    [TestMethod]
    public void FirstTrueBranchWins_FalseIf_TrueElif_FalseElse()
    {
        var stack = Create();

        Assert.IsFalse(stack.If(false));
        Assert.IsTrue(stack.Elif(true), "elif taken because the prior branch was false");
        Assert.IsTrue(stack.IsActive);
        Assert.IsFalse(stack.Else(), "else not taken because the elif branch was already taken");

        stack.EndIf();
        Assert.IsTrue(stack.IsActive);
    }

    [TestMethod]
    public void FirstTrueBranchWins_TrueIf_FalseElif_FalseElse()
    {
        var stack = Create();

        Assert.IsTrue(stack.If(true));
        Assert.IsFalse(stack.Elif(true), "elif not taken because the if branch was already taken");
        Assert.IsFalse(stack.Else(), "else not taken because the if branch was already taken");

        stack.EndIf();
        Assert.IsTrue(stack.IsActive);
    }

    [TestMethod]
    public void Define_Undef_MostRecentWins()
    {
        var stack = Create();

        stack.Define("A");
        Assert.IsTrue(stack.IsDefined("A"));

        stack.Undef("A");
        Assert.IsFalse(stack.IsDefined("A"));

        stack.Define("A");
        Assert.IsTrue(stack.IsDefined("A"));
    }

    [TestMethod]
    public void Define_InInactiveRegion_HasNoEffect()
    {
        var stack = Create();

        stack.If(false);
        stack.Define("B");
        stack.Else();
        stack.EndIf();

        Assert.IsFalse(stack.IsDefined("B"));
    }

    [TestMethod]
    public void Undef_InInactiveRegion_HasNoEffect()
    {
        var stack = Create();

        stack.Define("A");
        stack.If(false);
        stack.Undef("A");
        stack.EndIf();

        Assert.IsTrue(stack.IsDefined("A"));
    }

    [TestMethod]
    public void Define_InTakenBranch_PersistsAfterEndIf()
    {
        var stack = Create();

        stack.If(true);
        stack.Define("B");
        stack.EndIf();

        Assert.IsTrue(stack.IsDefined("B"));
    }

    [TestMethod]
    public void NestedIf_Active_DefinePersists_AfterEndIfs()
    {
        var stack = Create();

        stack.If(true);
        stack.If(true);
        stack.Define("X");
        stack.EndIf();
        stack.EndIf();

        Assert.IsTrue(stack.IsDefined("X"));
        Assert.IsTrue(stack.IsActive);
    }

    [TestMethod]
    public void NestedIf_InactiveOuter_InnerDefineNoEffect_OuterElseActive()
    {
        var stack = Create();

        stack.If(false);
        var innerTaken = stack.If(true);
        Assert.IsFalse(innerTaken, "inner #if is not active because the outer region is inactive");
        Assert.IsFalse(stack.IsActive);
        stack.Define("Y");
        stack.EndIf();
        Assert.IsTrue(stack.Else(), "the outer else branch is active");
        stack.Define("Z");
        stack.EndIf();

        Assert.IsFalse(stack.IsDefined("Y"));
        Assert.IsTrue(stack.IsDefined("Z"));
    }

    [TestMethod]
    public void HasUnfinishedIf_AcrossPushPop()
    {
        var stack = Create();

        Assert.IsFalse(stack.HasUnfinishedIf);

        stack.If(true);
        Assert.IsTrue(stack.HasUnfinishedIf);

        stack.If(true);
        Assert.IsTrue(stack.HasUnfinishedIf);

        stack.EndIf();
        Assert.IsTrue(stack.HasUnfinishedIf);

        stack.EndIf();
        Assert.IsFalse(stack.HasUnfinishedIf);
    }

    [TestMethod]
    public void StrayElseElifEndIf_OnEmptyStack_AreSafeNoOps()
    {
        var stack = Create();

        var elseResult = stack.Else();
        var elifResult = stack.Elif(true);
        stack.EndIf();

        Assert.IsFalse(elseResult);
        Assert.IsFalse(elifResult);
        Assert.IsTrue(stack.IsActive, "IsActive stays true after no-op stray directives");
        Assert.IsFalse(stack.HasUnfinishedIf);
    }
}
