using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.11.2 — C# 11.0 generic attributes (Cs11.grammar). A generic attribute is an attribute whose
// attribute type carries a type argument list: [MyAttribute<int>], [MyAttribute<int, string>].
// Positives use CreateParser(11); version-purity negatives use CreateParser(10) (no CS11).
// Roslyn: ParseAttribute (LanguageParser.cs:1181) -> ParseQualifiedName (7031) -> ParseSimpleName
// (6182): identifier + optional "<" TypeArgumentList ">" -> GenericName.
// See docs/CSharpParserPlan-progressT3.11.2.md.
[TestClass]
public class Cs11GenericAttributeTests
{
    // === POSITIVE (version 11): a simple generic attribute. ===

    [TestMethod]
    public void GenericAttribute_SingleTypeArg_Succeeds()
        => AssertParsesV11("[MyAttribute<int>] class MyClass { }");

    // === POSITIVE (version 11): a generic attribute with multiple type arguments. ===

    [TestMethod]
    public void GenericAttribute_MultipleTypeArgs_Succeeds()
        => AssertParsesV11("[MyAttribute<int, string>] class MyOtherClass { }");

    // === NEGATIVE (version 10, version-purity): generic attribute is not available at v10. ===

    [TestMethod]
    public void GenericAttribute_SingleTypeArg_RejectedAtV10()
        => AssertFailsV10("[MyAttribute<int>] class MyClass { }");

    [TestMethod]
    public void GenericAttribute_MultipleTypeArgs_RejectedAtV10()
        => AssertFailsV10("[MyAttribute<int, string>] class MyOtherClass { }");

    // === helpers ===

    private static void AssertParsesV11(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(11), input);

    private static void AssertFailsV10(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(10), input);

    private static void AssertParses(CSharpParser parser, string input)
    {
        var result = parser.Parse(input, "Grammar", out _);
        var success = result.TryGetSuccess(out var node, out var end)
            && end == input.Length
            && parser.Parser.ErrorInfo is null
            && parser.Parser.RecoveryDiagnostics.Count == 0;

        Assert.IsTrue(success,
            $"Expected «{input}» to fully parse (end={end}/{input.Length}, errorPos={parser.Parser.ErrorPos})");
        Assert.IsNotNull(node);
    }

    private static void AssertFails(CSharpParser parser, string input)
    {
        var result = parser.Parse(input, "Grammar", out _);
        Assert.IsFalse(
            result.TryGetSuccess(out _, out var end) && end == input.Length
                && parser.Parser.RecoveryDiagnostics.Count == 0,
            $"Expected parse failure or recovery diagnostics for: {input}");
    }
}
