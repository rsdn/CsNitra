using CSharpGrammar;
using ExtensibleParser;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

[TestClass]
public class CSharpParserMultiTextTests
{
    [TestMethod]
    public void Ctor_MultiText_AppendedAlternative_Parses()
    {
        var parser = CreateParser();

        var oldAlt = parser.Parse("x", "Grammar", out _);
        Assert.IsNull(parser.Parser.ErrorInfo);
        Assert.IsTrue(oldAlt.TryGetSuccess(out var oldNode, out var oldEnd),
            $"Expected old alternative 'x' to parse, error at pos {parser.Parser.ErrorPos}");
        Assert.IsNotNull(oldNode);
        Assert.AreEqual(1, oldEnd);

        var newAlt = parser.Parse("y", "Grammar", out _);
        Assert.IsNull(parser.Parser.ErrorInfo);
        Assert.IsTrue(newAlt.TryGetSuccess(out var newNode, out var newEnd),
            $"Expected new alternative 'y' to parse, error at pos {parser.Parser.ErrorPos}");
        Assert.IsNotNull(newNode);
        Assert.AreEqual(1, newEnd);
    }

    [TestMethod]
    public void Ctor_MultiText_UnknownTerminalInSecondFile_ThrowsWithSecondFilePath()
    {
        var ex = Assert.ThrowsException<InvalidOperationException>(() => new CSharpParser(
            [
                (Text: "Grammar = Statement*;\nStatement = \"x\";", Path: "Cs1.grammar"),
                (Text: "Statement = UnknownTerminal;", Path: "Cs2.grammar"),
            ],
            SmokeTestTerminals.Trivia(),
            [SmokeTestTerminals.Identifier(), SmokeTestTerminals.Number()]));

        Assert.IsTrue(ex.Message.Contains("Cs2.grammar:"), ex.Message);
        Assert.IsFalse(ex.Message.Contains("Cs1.grammar:"), ex.Message);
        Assert.IsTrue(ex.Message.Contains("UnknownTerminal"), ex.Message);
    }

    private static CSharpParser CreateParser() =>
        new(
            [
                (Text: "Grammar = Statement*;\nStatement = \"x\";", Path: "Cs1.grammar"),
                (Text: "Statement = | Y = \"y\";", Path: "Cs2.grammar"),
            ],
            SmokeTestTerminals.Trivia(),
            [SmokeTestTerminals.Identifier(), SmokeTestTerminals.Number()]);
}
