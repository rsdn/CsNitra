using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.1.3 — C# 2.0 `partial` (class/struct/method) + `sealed override` (Cs2.grammar).
// `partial` is a contextual keyword (NOT reserved, T1.3.1) and is APPENDED to the Cs1 modifier
// unions in Cs2 (T0.3 merge), so the merged modifiers accept `partial` only at v2+. The Cs1
// ClassDeclaration/StructDeclaration/Method use those merged modifiers, so no re-declaration of the
// declarations is needed. `sealed` is a hard keyword already present in the Cs1 MethodModifier (Cs1),
// so `sealed override` is a C# 1.0 feature that already parses at v1 (no Cs2 change). See
// docs/CSharpParserPlan-progressT3.1.3.md for the hand-traces and the Roslyn refs.
[TestClass]
public class Cs2PartialSealedTests
{
    // === POSITIVE (version 2): partial class / struct / method. ===

    [TestMethod]
    public void PartialClass_Succeeds()
        => AssertParsesV2("partial class C { }");

    [TestMethod]
    public void PartialStruct_Succeeds()
        => AssertParsesV2("partial struct S { }");

    [TestMethod]
    public void PartialMethodDeclaration_Succeeds()
        => AssertParsesV2("class C { partial void M(); }");

    [TestMethod]
    public void PartialMethodImplementation_Succeeds()
        => AssertParsesV2("class C { partial void M() { } }");

    [TestMethod]
    public void PartialMethodDeclaration_WithParams_Succeeds()
        => AssertParsesV2("class C { partial void M(int x, string y); }");

    // === POSITIVE (version 2): sealed override (a C# 1.0 feature; parses at v2 as well). ===

    [TestMethod]
    public void SealedOverride_Succeeds()
        => AssertParsesV2("class Base { virtual void M() { } } class C : Base { sealed override void M() { } }");

    // `sealed` + `partial` on a class: both are ClassModifiers (any order, binder-checked). Valid.
    [TestMethod]
    public void SealedPartialClass_Succeeds()
        => AssertParsesV2("sealed partial class C { }");

    // === POSITIVE (version 2): Roslyn-derived, adapted to full declarations. ===

    [TestMethod]
    public void PartialMethodDeclaration_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/RoundTrippingTests.cs
        // PartialMethodWithLanguageVersion2 — `partial class P { partial void M(); }` parses cleanly
        // at LanguageVersion.CSharp2 (void partial-method declaration, `;` body).
        AssertParsesV2("partial class P { partial void M(); }");
    }

    [TestMethod]
    public void NonVoidPartialMethod_Parses_Roslyn()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ParserErrorMessageTests.cs
        // PartialMethodsVersionThree — `partial int Goo() { }`. The CS2 void-only return-type
        // restriction is a BINDER diagnostic (CS8023), not a parse error: the parser accepts the
        // non-void partial method syntactically (parser-vs-binder split). Adapted to a declaration
        // (`;` body) for the class-member context.
        AssertParsesV2("class C { partial int M(); }");
    }

    // === POSITIVE (version 2): binder-concern forms that the parser accepts. ===
    // These are semantically invalid in real C# (binder errors) but are syntactically valid method
    // declarations, so the parser accepts them (parser-vs-binder split, consistent throughout).

    // `sealed` without `override` (binder CS0106); `sealed` is a Cs1 MethodModifier, so it parses.
    [TestMethod]
    public void SealedWithoutOverride_Parses()
        => AssertParsesV2("class C { sealed void M() { } }");

    // `override sealed` (reversed order): Roslyn's ParseModifiers is a generic any-order loop, so the
    // parser accepts either order; ordering/legality is a binder concern.
    [TestMethod]
    public void OverrideSealedOrder_Parses()
        => AssertParsesV2("class Base { virtual void M() { } } class C : Base { override sealed void M() { } }");

    // Whitespace before the `;` body is trivia (skipped after every terminal), so the "wrong
    // spacing" form parses identically to `partial void M();`.
    [TestMethod]
    public void PartialMethodDeclaration_TriviaBeforeSemicolon_Parses()
        => AssertParsesV2("class C { partial void M() ; }");

    // === POSITIVE (version 1): sealed override is a C# 1.0 feature (already in Cs1 MethodModifier). ===

    [TestMethod]
    public void SealedOverride_ParsesAtV1()
        => AssertParsesV1("class Base { virtual void M() { } } class C : Base { sealed override void M() { } }");

    // === NEGATIVE (version 1, version-purity): `partial` must REJECT at version 1. ===

    [TestMethod]
    public void PartialClass_RejectedAtV1()
        => AssertFailsV1("partial class C { }");

    [TestMethod]
    public void PartialStruct_RejectedAtV1()
        => AssertFailsV1("partial struct S { }");

    [TestMethod]
    public void PartialMethodDeclaration_RejectedAtV1()
        => AssertFailsV1("class C { partial void M(); }");

    [TestMethod]
    public void PartialMethodImplementation_RejectedAtV1()
        => AssertFailsV1("class C { partial void M() { } }");

    // === NEGATIVE (version 2, malformed): must REJECT at version 2. ===

    [TestMethod]
    public void PartialClass_MissingBody_Rejected()
        => AssertFailsV2("partial class C");

    [TestMethod]
    public void PartialMethod_MissingParameterList_Rejected()
        => AssertFailsV2("partial void M");

    [TestMethod]
    public void PartialMethod_MissingBody_Rejected()
        => AssertFailsV2("class C { partial void M() }");

    // === helpers ===

    private static void AssertParsesV2(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(2), input);

    private static void AssertParsesV1(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(1), input);

    private static void AssertFailsV2(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(2), input);

    private static void AssertFailsV1(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(1), input);

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
