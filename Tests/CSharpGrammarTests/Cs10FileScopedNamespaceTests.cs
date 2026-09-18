using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.10.1 — C# 10.0 file-scoped namespace (Cs10.grammar). Positives use CreateParser(10) (Cs1+...+
// Cs10 merged); version-purity negatives use CreateParser(9) (Cs1+...+Cs9, no CS10). A file-scoped
// namespace uses a single ";" instead of a "{ ... }" block: "namespace A; class C { }". It is a NEW
// NamespaceMember alternative (T0.3 append-merge) whose body is the SAME NamespaceBody (Cs1.grammar:19)
// a block-scoped namespace uses; the greedy NamespaceBody* consumes the rest of the file. See
// docs/CSharpParserPlan-progressT3.10.1.md.
[TestClass]
public class Cs10FileScopedNamespaceTests
{
    // === POSITIVE (version 10): a simple file-scoped namespace with a single type. ===
    // Roslyn ParseNamespaceDeclarationCore (LanguageParser.cs:264-267, 292-303).

    [TestMethod]
    public void FileScopedSingleType_Succeeds()
        => AssertParsesV10("namespace A; class C { }");

    // === POSITIVE (version 10): a file-scoped namespace with multiple types. ===
    // Both types belong to the file-scoped namespace (greedy NamespaceBody, LanguageParser.cs:292).

    [TestMethod]
    public void FileScopedMultipleTypes_Succeeds()
        => AssertParsesV10("namespace A; class B { } class C { }");

    // === POSITIVE (version 10): a file-scoped namespace with a nested type. ===
    // The nested type is a TypeDeclaration inside ClassBody (Cs1.grammar:102, :119).

    [TestMethod]
    public void FileScopedNestedType_Succeeds()
        => AssertParsesV10("namespace A; class Outer { class Inner { } }");

    // === POSITIVE (version 10): a file-scoped namespace with an empty body. ===
    // NamespaceBody = NamespaceMember* (zero-or-more) -> "namespace A;" alone is valid.

    [TestMethod]
    public void FileScopedEmptyBody_Succeeds()
        => AssertParsesV10("namespace A;");

    // === POSITIVE (version 10): a using directive INSIDE the file-scoped namespace body. ===
    // Roslyn: the file-scoped body holds Usings (FileScopedNamespaceDeclarationSyntax.Usings,
    // Syntax.xml.Syntax.Generated.cs:9821).

    [TestMethod]
    public void FileScopedWithUsingInside_Succeeds()
        => AssertParsesV10("namespace A; using System; class C { }");

    // === POSITIVE (version 10): a using directive BEFORE the file-scoped namespace. ===
    // The using is a separate top-level NamespaceMember (Cs1.grammar:5-9) preceding the
    // FileScopedNamespaceDeclaration member.

    [TestMethod]
    public void UsingBeforeFileScoped_Succeeds()
        => AssertParsesV10("using System; namespace A; class C { }");

    // === POSITIVE (version 10): a dotted namespace name. ===
    // QualifiedName = TypeName NamespaceSegment* (Cs1.grammar:21, :25).

    [TestMethod]
    public void FileScopedDottedName_Succeeds()
        => AssertParsesV10("namespace A.B.C; class D { }");

    // === POSITIVE (version 10): a block-scoped namespace still parses (regression). ===
    // NamespaceDeclaration (Cs1.grammar:17) is untouched; the two forms disambiguate on "{" vs ";".

    [TestMethod]
    public void BlockScopedNamespace_StillSucceeds()
        => AssertParsesV10("namespace A { class C { } }");

    // === NEGATIVE (version 9, version-purity): file-scoped namespaces are not available at v9. ===
    // At v9 the FileScopedNamespaceDeclaration alternative is ABSENT (Cs10-only); "namespace A;"
    // matches no NamespaceMember (the block-scoped NamespaceDeclaration needs "{") -> unconsumed ->
    // REJECTS. At v10 it PARSES.

    [TestMethod]
    public void FileScopedSingleType_RejectedAtV9()
        => AssertFailsV9("namespace A; class C { }");

    [TestMethod]
    public void FileScopedEmptyBody_RejectedAtV9()
        => AssertFailsV9("namespace A;");

    [TestMethod]
    public void FileScopedWithUsing_RejectedAtV9()
        => AssertFailsV9("namespace A; using System; class C { }");

    // === helpers ===

    private static void AssertParsesV10(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(10), input);

    private static void AssertFailsV9(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(9), input);

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