using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.9.1 — C# 9.0 records (Cs9.grammar). Positives use CreateParser(9) (Cs1+...+Cs9 merged);
// version-purity negatives use CreateParser(8) (Cs1+...+Cs8, no CS9). See
// docs/CSharpParserPlan-progressT3.9.1.md. A record is a NEW type declaration:
// `record` + optional class/struct + name + optional positional param list + optional base list +
// (block body | ";"). The "record" contextual keyword makes it mutually exclusive with every existing
// TypeDeclaration alternative (each starts with a reserved keyword), so it is reachable only at CS9.
[TestClass]
public class Cs9RecordsTests
{
    // === POSITIVE (version 9): positional record, no body (semicolon). ===
    // Roslyn tryScanRecordStart (LanguageParser.cs:1931) + paramList (1827) + semicolon body (1850).

    [TestMethod]
    public void Record_Positional_Succeeds()
        => AssertParsesV9("record Point(int X, int Y);");

    // === POSITIVE (version 9): positional record with a base list. ===
    // Roslyn baseList (LanguageParser.cs:1830).

    [TestMethod]
    public void Record_PositionalWithBase_Succeeds()
        => AssertParsesV9("record Point(int X, int Y) : Base;");

    // === POSITIVE (version 9): positional record with a body. ===
    // Roslyn openBrace/members/closeBrace (LanguageParser.cs:1858-1911).

    [TestMethod]
    public void Record_PositionalWithBody_Succeeds()
        => AssertParsesV9("record Point(int X, int Y) { }");

    // === POSITIVE (version 9): positional record with a base list and a body. ===

    [TestMethod]
    public void Record_PositionalWithBaseAndBody_Succeeds()
        => AssertParsesV9("record Point(int X, int Y) : Base { }");

    // === POSITIVE (version 9): explicit class kind (Roslyn recordModifier, 1936). ===

    [TestMethod]
    public void RecordClass_Succeeds()
        => AssertParsesV9("record class Point(int X, int Y);");

    // === POSITIVE (version 9): explicit struct kind. ===

    [TestMethod]
    public void RecordStruct_Succeeds()
        => AssertParsesV9("record struct Point(int X, int Y);");

    // === POSITIVE (version 9): non-positional record (no parameter list), semicolon. ===
    // Roslyn paramList is null when "(" does not follow the name (1827-1828).

    [TestMethod]
    public void Record_NonPositional_Succeeds()
        => AssertParsesV9("record Point;");

    // === POSITIVE (version 9): non-positional record with a body. ===

    [TestMethod]
    public void Record_NonPositionalWithBody_Succeeds()
        => AssertParsesV9("record Point { }");

    // === POSITIVE (version 9): empty positional parameter list. ===

    [TestMethod]
    public void Record_EmptyParameterList_Succeeds()
        => AssertParsesV9("record Point();");

    // === POSITIVE (version 9): single positional parameter. ===

    [TestMethod]
    public void Record_SingleParameter_Succeeds()
        => AssertParsesV9("record Point(int X);");

    // === POSITIVE (version 9): trailing semicolon after a body (Roslyn TryEatToken(Semicolon), 1913). ===

    [TestMethod]
    public void Record_TrailingSemicolon_Succeeds()
        => AssertParsesV9("record Point { };");

    // === POSITIVE (version 9): access modifier. ===

    [TestMethod]
    public void Record_Public_Succeeds()
        => AssertParsesV9("public record Point(int X, int Y);");

    // === POSITIVE (version 9): attribute list before the record. ===

    [TestMethod]
    public void Record_Attribute_Succeeds()
        => AssertParsesV9("[Attr] record Point(int X);");

    // === POSITIVE (version 9): multiple base types. ===

    [TestMethod]
    public void Record_MultipleBases_Succeeds()
        => AssertParsesV9("record Point(int X) : Base1, Base2;");

    // === POSITIVE (version 9): record with a member in the body (a field, Cs1 Field). ===

    [TestMethod]
    public void Record_BodyWithMember_Succeeds()
        => AssertParsesV9("record Point(int X) { int Z; }");

    // === POSITIVE (version 9): nested record inside a class (TypeDeclaration in ClassMember). ===

    [TestMethod]
    public void Record_Nested_Succeeds()
        => AssertParsesV9("class Outer { record Inner(int X); }");

    // === POSITIVE (version 9): record struct with a base list and a body. ===

    [TestMethod]
    public void RecordStruct_WithBaseAndBody_Succeeds()
        => AssertParsesV9("record struct Point(int X) : IBase { }");

    // === POSITIVE (version 9): record inside a namespace. ===

    [TestMethod]
    public void Record_InNamespace_Succeeds()
        => AssertParsesV9("namespace N { record Point(int X); }");

    // === NEGATIVE (version 8, version-purity): records are not available at v8. ===
    // At v8 the RecordDeclaration alternative is ABSENT (Cs9-only); no TypeDeclaration alternative
    // starts with the contextual "record" keyword -> the declaration is unconsumed -> REJECTS. At v9
    // it PARSES.

    [TestMethod]
    public void Record_RejectedAtV8()
        => AssertFailsV8("record Point(int X, int Y);");

    // === NEGATIVE (version 8, version-purity): record struct at v8. ===

    [TestMethod]
    public void RecordStruct_RejectedAtV8()
        => AssertFailsV8("record struct Point(int X);");

    // === NEGATIVE (version 8, version-purity): record class at v8. ===

    [TestMethod]
    public void RecordClass_RejectedAtV8()
        => AssertFailsV8("record class Point(int X);");

    // === NEGATIVE (version 9, malformed): missing name (a record requires a TypeName). ===

    [TestMethod]
    public void Record_MissingName_Rejected()
        => AssertFailsV9("record (int X);");

    // === NEGATIVE (version 9, malformed): missing close paren in the parameter list. ===

    [TestMethod]
    public void Record_MissingCloseParen_Rejected()
        => AssertFailsV9("record Point(int X;");

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
