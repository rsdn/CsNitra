using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.14.2 — C# 14.0 `ref` field. A `ref` field is a field declared with the `ref` modifier:
//   class MyClass {
//       ref int _value;
//   }
// (and `ref readonly int _value;`).
//
// Roslyn: parsed through the STANDARD field-declaration path (ParseNormalFieldDeclaration,
// LanguageParser.cs:5200-5220); `ref` is a general declaration modifier (GetModifierExcludingScoped,
// LanguageParser.cs:1322-1323 -> DeclarationModifiers.Ref) riding in the field's modifiers list. The
// binder folds `ref`/`ref readonly` into the type (SkipRefInField, SourceMemberFieldSymbol.cs:533-534;
// RefKind.Ref / RefKind.RefReadOnly) and enforces the C# version (IDS_FeatureRefFields,
// MessageID.cs:262/557 — a "semantic check").
//
// Cs14.grammar models it by re-declaring FieldModifier (append, T0.3 merge) to add "ref" to the Cs1
// union (Cs1.grammar:155-165) + Cs11 `required` (Cs11.grammar:93). The Cs1 Field (Cs1.grammar:148) then
// consumes `ref` as a field modifier. Consistent with Cs7 MethodModifier `ref` return (Cs7.grammar:237)
// and Cs7 StructModifier `ref struct` (Cs7.grammar:512). All Cs14-only.
//
// Positives use CreateParser(14); version-purity negatives use CreateParser(13) (Cs1..Cs13, no CS14):
// at v13 `ref` is not a FieldModifier, so `ref int _value;` REJECTS.
[TestClass]
public class Cs14RefFieldTests
{
    // === POSITIVE (version 14): the `ref` field. ===

    [TestMethod]
    public void RefField_Simple_Succeeds()
        => AssertParsesV14("class C { ref int _value; }");

    [TestMethod]
    public void RefField_RefReadonly_Succeeds()
        => AssertParsesV14("class C { ref readonly int _value; }");

    [TestMethod]
    public void RefField_InRefStruct_Succeeds()
        => AssertParsesV14("ref struct S { ref int _value; }");

    [TestMethod]
    public void RefField_WithAccessModifier_Succeeds()
        => AssertParsesV14("class C { public ref int _value; }");

    // === VERSION-PURITY (version 13, no CS14): the `ref` field must REJECT. ===
    // At v13 `ref` is not a FieldModifier, so `ref int _value;` matches no Field (Type=ref fails — ref is
    // reserved) and no other member alternative, so it REJECTS.

    [TestMethod]
    public void RefField_Simple_RejectedAtV13()
        => AssertFailsV13("class C { ref int _value; }");

    [TestMethod]
    public void RefField_RefReadonly_RejectedAtV13()
        => AssertFailsV13("class C { ref readonly int _value; }");

    [TestMethod]
    public void RefField_InRefStruct_RejectedAtV13()
        => AssertFailsV13("ref struct S { ref int _value; }");

    // === REGRESSION (version 14): pre-existing `ref` / readonly forms must stay green (no CS14 leak). ===

    [TestMethod]
    public void RefReturnMethod_StillParsesAtV14_Succeeds()
    {
        // `ref int M() { }` (a ref RETURN method, Cs7) must still route to Method, not Field, at v14.
        // Field FAILS (after the name it expects `;` but finds `(`); Method matches (MethodModifier=ref).
        AssertParsesV14("class C { ref int M() { return 0; } }");
    }

    [TestMethod]
    public void ReadonlyField_StillParsesAtV14_Succeeds()
    {
        // A plain readonly field (every version) must still parse (FieldModifier=readonly, no `ref`).
        AssertParsesV14("class C { readonly int _x; }");
    }

    [TestMethod]
    public void RefStruct_EmptyBody_StillParsesAtV14_Succeeds()
    {
        // `ref struct S { }` (a Cs7 feature) must still parse at v14 (no `ref` field inside).
        AssertParsesV14("ref struct S { }");
    }

    // === NEGATIVE (version 14, malformed): must REJECT at v14. ===

    [TestMethod]
    public void RefField_MissingDeclarator_Rejected()
    {
        // `ref int;` — a missing variable-declarator name: Field (FieldModifier=ref, Type=int) then
        // expects a VariableDeclarator but finds `;`, so it fails.
        AssertFailsV14("class C { ref int; }");
    }

    // === helpers ===

    private static void AssertParsesV14(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(14), input);

    private static void AssertFailsV13(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(13), input);

    private static void AssertFailsV14(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(14), input);

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
