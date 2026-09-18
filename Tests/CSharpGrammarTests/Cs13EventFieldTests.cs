using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.13.2 — C# 13.0 `event field`. An event field is an event declaration with an initializer
// (like a field):
//   class MyClass {
//       public event Action MyEvent = () => { };
//   }
//
// Roslyn: ParseEventDeclaration (LanguageParser.cs:5082-5095) -> IsFieldDeclaration (3429-3468) ->
// ParseEventFieldDeclaration (5222-5255): `event` + Type + name + `= Expression` + `;`
// (EventFieldDeclarationSyntax, Syntax.xml.Syntax.Generated.cs:12407; SyntaxKind.EventFieldDeclaration
// = 8874, SyntaxKind.cs:827).
//
// Cs13.grammar models it by re-declaring EventTail (append, T0.3 merge) with an initializer tail
// (`= Expression ;`). The Cs1 Event (Cs1.grammar:429) references EventTail, so it picks up the new
// form automatically. The three EventTail alternatives are mutually exclusive by leading token
// (`;` vs `{` vs `=`). CS13-only: at v<=12 the `= Expression ;` tail is absent, so an event field
// matches no EventTail and REJECTS.
//
// Positives use CreateParser(13); version-purity negatives use CreateParser(12) (Cs1..Cs12, no CS13).
[TestClass]
public class Cs13EventFieldTests
{
    // === POSITIVE (version 13): the event field (event with an initializer). ===

    [TestMethod]
    public void EventField_LambdaInitializer_Succeeds()
        => AssertParsesV13("class MyClass { public event Action MyEvent = () => { }; }");

    [TestMethod]
    public void EventField_Struct_Succeeds()
        => AssertParsesV13("struct S { public event Action E = () => { }; }");

    [TestMethod]
    public void EventField_Private_Succeeds()
        => AssertParsesV13("class C { private event Action E = () => { }; }");

    [TestMethod]
    public void EventField_NullInitializer_Succeeds()
        => AssertParsesV13("class C { public event Action E = null; }");

    [TestMethod]
    public void EventField_AnonymousMethodInitializer_Succeeds()
        => AssertParsesV13("class C { public event Action E = delegate { }; }");

    [TestMethod]
    public void EventField_MultipleMembers_Succeeds()
        => AssertParsesV13("class C { public event Action A = () => { }; public event Action B = () => { }; }");

    // === VERSION-PURITY (version 12, no CS13): the event field must REJECT. ===
    // At v12 the `= Expression ;` EventTail is absent, so `event ... = ...;` matches no EventTail.

    [TestMethod]
    public void EventField_LambdaInitializer_RejectedAtV12()
        => AssertFailsV12("class MyClass { public event Action MyEvent = () => { }; }");

    [TestMethod]
    public void EventField_NullInitializer_RejectedAtV12()
        => AssertFailsV12("class C { public event Action E = null; }");

    // === REGRESSION (version 12 and 13): pre-existing event forms must stay green (no CS13 leak). ===

    [TestMethod]
    public void SimpleEvent_StillParsesAtV12_Succeeds()
        => AssertParsesV12("class C { public event Action E; }");

    [TestMethod]
    public void EventWithAccessors_StillParsesAtV12_Succeeds()
        => AssertParsesV12("class C { public event Action E { add { } remove { } } }");

    [TestMethod]
    public void SimpleEvent_StillParsesAtV13_Succeeds()
        => AssertParsesV13("class C { public event Action E; }");

    [TestMethod]
    public void EventWithAccessors_StillParsesAtV13_Succeeds()
        => AssertParsesV13("class C { public event Action E { add { } remove { } } }");

    // === NEGATIVE (version 13, malformed): must REJECT at v13. ===

    [TestMethod]
    public void EventField_MissingExpression_Rejected()
    {
        // `event Action E = ;` — a missing initializer expression: the EventFieldInit tail
        // (`= Expression ;`) fails (no Expression before `;`), so the Event rule fails.
        AssertFailsV13("class C { public event Action E = ; }");
    }

    [TestMethod]
    public void EventField_MissingSemicolon_Rejected()
    {
        // `event Action E = () => { } }` — a missing `;` after the initializer: the EventFieldInit
        // tail requires a `;`, so the Event rule fails.
        AssertFailsV13("class C { public event Action E = () => { } }");
    }

    // === helpers ===

    private static void AssertParsesV13(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(13), input);

    private static void AssertParsesV12(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(12), input);

    private static void AssertFailsV13(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(13), input);

    private static void AssertFailsV12(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(12), input);

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
