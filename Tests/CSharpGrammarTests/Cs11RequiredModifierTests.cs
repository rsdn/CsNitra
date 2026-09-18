using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.11.3 — C# 11.0 `required` modifier (Cs11.grammar). `required` is a CONTEXTUAL keyword and a
// modifier on PROPERTIES and FIELDS:
//   public required string Name { get; set; }
//   public required int Age;
// It is APPENDED to the Cs1 FieldModifier/PropertyModifier unions (T0.3 merge — the same pattern as
// `partial` in Cs2.grammar T3.1.3 and `this` in Cs3.grammar T3.2.4), so the merged modifiers accept
// `required` only at v11+. It is NOT added to ReservedKeyword (it is contextual — `int required;`
// stays a valid field named `required`).
// Positives use CreateParser(11); version-purity negatives use CreateParser(10) (no CS11).
// Roslyn: GetModifierExcludingScoped (LanguageParser.cs:1324-1332, IdentifierToken +
// ContextualKind.RequiredKeyword -> DeclarationModifiers.Required); MessageID.cs:549
// (IDS_FeatureRequiredMembers) -> CSharp11. See docs/CSharpParserPlan-progressT3.11.3.md.
[TestClass]
public class Cs11RequiredModifierTests
{
    // === POSITIVE (version 11): a required auto-property (Cs3 auto-accessors). ===

    [TestMethod]
    public void RequiredProperty_Auto_Succeeds()
        => AssertParsesV11("class Person { public required string Name { get; set; } }");

    // === POSITIVE (version 11): a required field (Cs1 field). ===

    [TestMethod]
    public void RequiredField_Succeeds()
        => AssertParsesV11("class Person { public required int Age; }");

    // === POSITIVE (version 11): a required property with a block body (Cs1 concrete property). ===

    [TestMethod]
    public void RequiredProperty_BlockBody_Succeeds()
        => AssertParsesV11("class Person { public required int Age { get { return 0; } set { } } }");

    // === POSITIVE (version 11): a required field with multiple declarators. ===

    [TestMethod]
    public void RequiredField_MultiDeclarator_Succeeds()
        => AssertParsesV11("class Person { public required int Age, Weight; }");

    // === POSITIVE (version 11): `required` is still a plain identifier (contextual keyword) — a
    // field named `required` parses. Confirms we did NOT add `required` to ReservedKeyword. ===

    [TestMethod]
    public void Required_AsFieldName_Succeeds()
        => AssertParsesV11("class Person { int required; }");

    // === NEGATIVE (version 10, version-purity): `required` is not a modifier at v10. ===

    [TestMethod]
    public void RequiredProperty_Auto_RejectedAtV10()
        => AssertFailsV10("class Person { public required string Name { get; set; } }");

    [TestMethod]
    public void RequiredField_RejectedAtV10()
        => AssertFailsV10("class Person { public required int Age; }");

    [TestMethod]
    public void RequiredProperty_BlockBody_RejectedAtV10()
        => AssertFailsV10("class Person { public required int Age { get { return 0; } set { } } }");

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
