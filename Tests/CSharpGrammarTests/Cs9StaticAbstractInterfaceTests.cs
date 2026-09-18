using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.9.5 — C# 9.0 static abstract members in interfaces.
// A static abstract interface member is BOTH `static` and `abstract` (no body; the implementing class
// must supply a static implementation):
//   interface IShape { static abstract double Area { get; } static abstract void Draw(); }
// Positives use CreateParser(9) (Cs1+...+Cs9 merged); version-purity negatives use CreateParser(8)
// (Cs1+...+Cs8, no CS9). Roslyn: the parser treats `static abstract` as plain modifiers (the generic
// ParseModifiers loop, LanguageParser.cs:3238/3259/1347); the static+abstract-in-interface feature is a
// BINDER/semantic (C# 9.0) check (MessageID.cs:233, ModifierUtils.cs:216-228) — the plan's
// parser-vs-binder split. Version purity comes from the rules being CS9-only: the Cs1/Cs2/Cs8
// InterfaceMethod rules were restricted to InterfaceMethodModifier (MethodModifier minus static/
// abstract, Cs1.grammar) so `static abstract` rejects at v1-v8, and the Cs9-only
// StaticAbstractInterfaceMethod / StaticAbstractInterfaceProperty (Cs9.grammar) are the sole matchers
// at v9. See docs/CSharpParserPlan-progressT3.9.5.md.
[TestClass]
public class Cs9StaticAbstractInterfaceTests
{
    // === POSITIVE (version 9): static abstract method (the task example). ===
    // `static abstract void Draw();` — Cs9 StaticAbstractInterfaceMethod
    // (Attributes? InterfaceAccessModifier* "static" "abstract" Type TypeName "(" ParameterList? ")" ";").
    // The Cs1/Cs2/Cs8 InterfaceMethod FAIL (InterfaceMethodModifier cannot consume static/abstract).

    [TestMethod]
    public void StaticAbstractMethod_Succeeds()
        => AssertParsesV9("interface I { static abstract void Draw(); }");

    // === POSITIVE (version 9): static abstract property (the task example). ===
    // `static abstract double Area { get; }` — Cs9 StaticAbstractInterfaceProperty
    // (Attributes? InterfaceAccessModifier* "static" "abstract" Type TypeName "{" InterfaceAccessor+ "}" ";"?).

    [TestMethod]
    public void StaticAbstractProperty_Succeeds()
        => AssertParsesV9("interface I { static abstract double Area { get; } }");

    // === POSITIVE (version 9): the full task example (both members in one interface). ===

    [TestMethod]
    public void BothMembersInOneInterface_Succeeds()
        => AssertParsesV9("interface I { static abstract double Area { get; } static abstract void Draw(); }");

    // === POSITIVE (version 9): static abstract method with a leading access modifier. ===
    // `public static abstract void Draw();` — InterfaceAccessModifier* consumes `public`, then the
    // REQUIRED `static abstract` (the canonical C# order).

    [TestMethod]
    public void StaticAbstractMethodWithAccessModifier_Succeeds()
        => AssertParsesV9("interface I { public static abstract void Draw(); }");

    // === POSITIVE (version 9): static abstract property with get/set accessors. ===
    // `static abstract int X { get; set; }` — InterfaceAccessor+ = `get;` `set;`.

    [TestMethod]
    public void StaticAbstractPropertyGetSet_Succeeds()
        => AssertParsesV9("interface I { static abstract int X { get; set; } }");

    // === POSITIVE (version 9): static abstract method with parameters. ===
    // `static abstract void Draw(int x);` — ParameterList = `(int x)`.

    [TestMethod]
    public void StaticAbstractMethodWithParams_Succeeds()
        => AssertParsesV9("interface I { static abstract void Draw(int x); }");

    // === NEGATIVE (version 8, version-purity): a static abstract method is CS9-only. ===
    // At v8 the Cs9 StaticAbstractInterface* rules are ABSENT and the Cs1/Cs2/Cs8 InterfaceMethod
    // (InterfaceMethodModifier*) cannot consume `static`/`abstract` -> no InterfaceMember alternative
    // matches -> REJECTS. At v9 it PARSES.

    [TestMethod]
    public void StaticAbstractMethod_RejectedAtV8()
        => AssertFailsV8("interface I { static abstract void Draw(); }");

    // === NEGATIVE (version 8, version-purity): a static abstract property is CS9-only. ===
    // At v8 the Cs1 InterfaceProperty (PropertyModifier*) consumes `static` then `Type` sees the
    // reserved `abstract` and fails; the Cs9 rule is absent -> REJECTS. At v9 it PARSES.

    [TestMethod]
    public void StaticAbstractProperty_RejectedAtV8()
        => AssertFailsV8("interface I { static abstract double Area { get; } }");

    // === NEGATIVE (version 8, version-purity): a static abstract method with an access modifier. ===

    [TestMethod]
    public void StaticAbstractMethodWithAccessModifier_RejectedAtV8()
        => AssertFailsV8("interface I { public static abstract void Draw(); }");

    // === NO-REGRESSION (version 8): a no-body interface method parses at v8 (unchanged by T3.9.5). ===

    [TestMethod]
    public void NoBodyMethod_ParsesAtV8()
        => AssertParsesV8("interface I { void P(); }");

    // === NO-REGRESSION (version 9): a default interface method (body) still parses at v9 (T3.8.6). ===

    [TestMethod]
    public void DefaultInterfaceMethod_ParsesAtV9()
        => AssertParsesV9("interface I { void M() { } }");

    // === NO-REGRESSION (version 9): a `new` default interface method still parses at v9 (T3.8.6). ===

    [TestMethod]
    public void NewDefaultInterfaceMethod_ParsesAtV9()
        => AssertParsesV9("interface I { new void M() { } }");

    // === NO-REGRESSION (version 1): a CLASS static abstract method parses at v1 (C# 1.0, unchanged). ===
    // The class Method (Cs1.grammar:265) still uses MethodModifier* (with static/abstract); only the
    // InterfaceMethod rules were restricted. So a class static abstract method is unaffected.

    [TestMethod]
    public void ClassStaticAbstractMethod_ParsesAtV1()
        => AssertParsesV1("class C { static abstract void M(); }");

    // === helpers ===

    private static void AssertParsesV9(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(9), input);

    private static void AssertParsesV8(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(8), input);

    private static void AssertParsesV1(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(1), input);

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
