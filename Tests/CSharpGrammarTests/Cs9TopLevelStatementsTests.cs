using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.9.4 — C# 9.0 top-level statements (Cs9.grammar). Positives use CreateParser(9) (Cs1+...+Cs9
// merged); version-purity negatives use CreateParser(8) (Cs1+...+Cs8, no CS9). A top-level statement
// is a Statement written at the top level of a file, before any type declaration. The compilation
// unit is re-declared (T0.3 append-merge) to add a second alternative `GlobalStatement*
// NamespaceMember*` to the Cs1 `NamespaceMember*`; longest-match + the full-consumption check pick
// the right path. See docs/CSharpParserPlan-progressT3.9.4.md.
[TestClass]
public class Cs9TopLevelStatementsTests
{
    // === POSITIVE (version 9): a single top-level expression statement. ===
    // Roslyn ParseMemberDeclarationOrStatement (LanguageParser.cs:2589) -> GlobalStatement (2657).

    [TestMethod]
    public void TopLevelSingleExpressionStatement_Succeeds()
        => AssertParsesV9("Console.WriteLine(\"Hello, World!\");");

    // === POSITIVE (version 9): a single top-level local variable declaration. ===
    // Roslyn ParseMemberDeclarationOrStatement -> GlobalStatement (2928).

    [TestMethod]
    public void TopLevelSingleLocalVariable_Succeeds()
        => AssertParsesV9("var x = 42;");

    // === POSITIVE (version 9): multiple top-level statements. ===

    [TestMethod]
    public void TopLevelMultipleStatements_Succeeds()
        => AssertParsesV9("var x = 42;\nvar y = x + 1;\n");

    // === POSITIVE (version 9): top-level statements followed by a type declaration. ===
    // Roslyn: the global statements (Members) precede the type declaration (Members), 760.

    [TestMethod]
    public void TopLevelStatementsThenType_Succeeds()
        => AssertParsesV9("Console.WriteLine(\"hi\");\nclass C { }\n");

    // === POSITIVE (version 9): multiple top-level statements followed by a type declaration. ===

    [TestMethod]
    public void TopLevelMultipleStatementsThenType_Succeeds()
        => AssertParsesV9("var a = 1;\nvar b = 2;\nclass C { }\n");

    // === NEGATIVE (version 8, version-purity): top-level statements are not available at v8. ===
    // At v8 the second CompilationUnit alternative is ABSENT (Cs9-only); a file starting with a
    // statement matches 0 NamespaceMembers -> unconsumed -> REJECTS. At v9 it PARSES.

    [TestMethod]
    public void TopLevelExpressionStatement_RejectedAtV8()
        => AssertFailsV8("Console.WriteLine(\"hi\");");

    [TestMethod]
    public void TopLevelLocalVariable_RejectedAtV8()
        => AssertFailsV8("var x = 42;");

    // === NEGATIVE (version 9, malformed): a top-level statement missing its terminating ";". ===

    [TestMethod]
    public void TopLevelMissingSemicolon_Rejected()
        => AssertFailsV9("Console.WriteLine(\"hi\")");

    // === helpers ===

    private static void AssertParsesV9(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(9), input);

    private static void AssertFailsV9(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(9), input);

    private static void AssertFailsV8(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(8), input);

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
