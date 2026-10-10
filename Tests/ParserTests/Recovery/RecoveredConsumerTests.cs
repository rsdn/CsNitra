using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace Recovery;

[TestClass]
public sealed class RecoveredConsumerTests
{
    [TestMethod]
    public void GrammarAstConsumerRejectsMissingTerminator()
    {
        var result = new CsNitra.CsNitraParser().Parse<CsNitra.Ast.GrammarAst>("Grammar = \"a\"");
        Assert.IsInstanceOfType<Failed>(result);
    }

    [TestMethod]
    public void JsonAstConsumerReportsSyntaxFailureBeforeVisitingRepairs() =>
        Assert.ThrowsException<InvalidOperationException>(() => new Json.JsonParser().Parse("[1,]"));

    [TestMethod]
    public void DotConsumerRejectsMissingClosingBrace()
    {
        var result = new Dot.DotParser().Parse("digraph G { a -> b;");
        Assert.IsInstanceOfType<Dot.DotParser.Result.Failure>(result);
    }

    [TestMethod]
    public void CppConsumerRejectsIncompleteKnownConstruct()
    {
        var result = new CppSimplifiedParser.CppParser().Parse("enum E { A", "EnumDecl");
        Assert.IsInstanceOfType<Failed>(result);
    }

    [TestMethod]
    public void CompilerPartialAtEofStillReportsFailure()
    {
        var parser = new Parser(RecoveryTerminals.Trivia()) { Profile = RecoveryProfile.Compiler };
        parser.Rules["S"] = [new Seq([new Literal("a"), new Literal("b")], "S")];
        parser.BuildTdoppRules();
        var result = parser.Parse("a", "S", out _);
        Assert.IsFalse(result.IsSuccess);
        Assert.IsNotNull(parser.ErrorInfo);
        Assert.AreEqual(0, parser.Metrics.TotalAccept);
    }
}
