using CsNitra.Ast;
using ExtensibleParser;

namespace CsNitra;

[TestClass]
public class GrammarMergeTests
{
    [TestMethod]
    public void AppendAlternative_ReDeclaredRule_AllAlternativesParse()
    {
        var parser = BuildParser(
            "Grammar = | A = \"a\" | B = \"b\";\nStatement = \"x\";",
            "Statement = | Y = \"y\";");

        AssertParseSuccess(parser, "Grammar", "a");
        AssertParseSuccess(parser, "Grammar", "b");
        AssertParseSuccess(parser, "Statement", "x");
        AssertParseSuccess(parser, "Statement", "y");

        var statementAlts = parser.Rules["Statement"];
        Assert.AreEqual(2, statementAlts.Length);
        AssertLiteral(statementAlts[0], "x");
        AssertLiteral(statementAlts[1], "y");
    }

    [TestMethod]
    public void Merge_SimpleWithSimpleSameName_BothParse()
    {
        var parser = BuildParser("Rule = \"a\";", "Rule = \"b\";");

        AssertParseSuccess(parser, "Rule", "a");
        AssertParseSuccess(parser, "Rule", "b");

        var alts = parser.Rules["Rule"];
        Assert.AreEqual(2, alts.Length);
        AssertLiteral(alts[0], "a");
        AssertLiteral(alts[1], "b");
    }

    [TestMethod]
    public void Merge_PipedWithSimple_AllParseInFileOrder()
    {
        var parser = BuildParser("Rule = | A = \"a\" | B = \"b\";", "Rule = \"c\";");

        AssertParseSuccess(parser, "Rule", "a");
        AssertParseSuccess(parser, "Rule", "b");
        AssertParseSuccess(parser, "Rule", "c");

        var alts = parser.Rules["Rule"];
        Assert.AreEqual(3, alts.Length);
        AssertLiteral(alts[0], "a");
        AssertLiteral(alts[1], "b");
        AssertLiteral(alts[2], "c");
    }

    [TestMethod]
    public void Merge_ThreeFiles_AlternativesInFileOrder()
    {
        var parser = BuildParser("Rule = | A = \"a\";", "Rule = | B = \"b\";", "Rule = | C = \"c\";");

        AssertParseSuccess(parser, "Rule", "a");
        AssertParseSuccess(parser, "Rule", "b");
        AssertParseSuccess(parser, "Rule", "c");

        var alts = parser.Rules["Rule"];
        Assert.AreEqual(3, alts.Length);
        AssertLiteral(alts[0], "a");
        AssertLiteral(alts[1], "b");
        AssertLiteral(alts[2], "c");
    }

    [TestMethod]
    public void PrecedenceAcrossFiles_MergedLists_TdoppBuildsAndParses()
    {
        var parser = BuildParser(
            "precedence Left, Mid;\nExpr = | Num = \"1\";",
            "precedence Mid, Right;\nExpr = | Mul = Expr \"*\" Expr : Right;");

        AssertParseSuccess(parser, "Expr", "1");
        AssertParseSuccess(parser, "Expr", "1*1");

        Assert.AreEqual(1, parser.TdoppRules["Expr"].Postfix.Length);
    }

    [TestMethod]
    public void BuildFromTexts_UnknownTerminalInSecondFile_ErrorNamesSecondFile()
    {
        var parser = new Parser(MiniC.Terminals.Trivia());
        var files = new[]
        {
            (Text: "Rule = \"a\";", Path: "Cs1.grammar"),
            (Text: "Rule2 = UnknownThing;", Path: "Cs2.grammar"),
        };

        var ex = Assert.ThrowsException<InvalidOperationException>(() => parser.BuildFromTexts(files, []));

        Assert.IsTrue(ex.Message.Contains("Cs2.grammar:"), ex.Message);
        Assert.IsFalse(ex.Message.Contains("Cs1.grammar:"), ex.Message);
        Assert.IsTrue(ex.Message.Contains("UnknownThing"), ex.Message);
    }

    [TestMethod]
    public void BuildFromAst_SingleText_BehavesAsBefore()
    {
        var (grammar, source) = ParserExtensions.ParseTexts([(Text: "Rule = | A = \"a\" | B = \"b\";", Path: "Cs1.grammar")]);
        var parser = new Parser(MiniC.Terminals.Trivia());

        parser.BuildFromAst(grammar, source, []);
        parser.BuildTdoppRules();

        AssertParseSuccess(parser, "Rule", "a");
        AssertParseSuccess(parser, "Rule", "b");

        var alts = parser.Rules["Rule"];
        Assert.AreEqual(2, alts.Length);
        AssertLiteral(alts[0], "a");
        AssertLiteral(alts[1], "b");
    }

    [TestMethod]
    public void Metacircular_MultiFileGrammar_ExtendedRuleParsesConcatenatedGrammar()
    {
        var files = new[]
        {
            (Text: CsNitraGrammarText.GetGrammarText(), Path: "Cs1.grammar"),
            (Text: "Using = | Unused = \"zzz\";", Path: "Cs2.grammar"),
        };
        var (grammar, source) = ParserExtensions.ParseTexts(files);

        var manualParseResult = new CsNitraParser().Parse<GrammarAst>(source.Text);
        Assert.IsTrue(manualParseResult is Success<GrammarAst>,
            "Manual parser must parse the concatenated grammar text");

        var parser = new Parser(CsNitraTerminals.Trivia());
        parser.BuildFromAst(grammar, source, [
            CsNitraTerminals.Identifier(),
            CsNitraTerminals.Literal(),
        ]);
        parser.BuildTdoppRules();

        var result = parser.Parse(source.Text, startRule: "Grammar", out _);

        Assert.IsNull(parser.ErrorInfo);
        Assert.IsTrue(result.TryGetSuccess(out var node, out var end),
            $"Generated parser failed to parse concatenated grammar. Error at pos: {parser.ErrorPos}");
        Assert.IsNotNull(node);
        Assert.AreEqual(source.Text.Length, end);

        var usingAlts = parser.Rules["Using"];
        Assert.AreEqual(3, usingAlts.Length);
        AssertLiteral(usingAlts[^1], "zzz");
    }

    private static Parser BuildParser(string file1, string file2, string? file3 = null)
    {
        var files = new List<(string Text, string Path)>
        {
            (file1, "Cs1.grammar"),
            (file2, "Cs2.grammar"),
        };
        if (file3 != null)
            files.Add((file3, "Cs3.grammar"));

        var parser = new Parser(MiniC.Terminals.Trivia());
        parser.BuildFromTexts(files, []);
        parser.BuildTdoppRules();
        return parser;
    }

    private static void AssertParseSuccess(Parser parser, string startRule, string input)
    {
        var result = parser.Parse(input, startRule, out _);

        Assert.IsNull(parser.ErrorInfo, $"Expected no parse error for '{input}'");
        Assert.IsTrue(result.TryGetSuccess(out var node, out var end),
            $"Expected '{input}' to parse as '{startRule}', error at pos {parser.ErrorPos}");
        Assert.IsNotNull(node);
        Assert.AreEqual(input.Length, end);
    }

    private static void AssertLiteral(Rule rule, string value) =>
        Assert.AreEqual(value, ((ExtensibleParser.Literal)rule).Value);
}
