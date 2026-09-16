using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.2.2 — C# 3.0 object/collection initializers + anonymous types (Cs3.grammar). Positives use
// CreateParser(3) (Cs1+Cs2+Cs3 merged); version-purity negatives use CreateParser(2) (Cs1+Cs2, no
// CS3). The initializer `{ ... }` is a NEW construct after an object creation (or a bare `new`):
//   ObjectCreationWithInitializer = NewBaseType ArgumentList? Initializer   (REQUIRES the Initializer)
//   AnonymousType                 = AnonymousInitializer                    (a `{` directly after `new`)
// Each InitializerElement is EITHER a MemberAssignment (Identifier = Expression, single `=`) OR a
// plain Expression (collection element); the two are mutually exclusive (the `!("==")` lookahead +
// the `!(Identifier !("==") "=")` guard avoid the `=`/`==` mis-match and the equal-length tie).
// See docs/CSharpParserPlan-progressT3.2.2.md for the hand-traces and the `=`/`==` finding.
[TestClass]
public class Cs3InitializerTests
{
    // === POSITIVE (version 3): object initializers. ===

    [TestMethod]
    public void ObjectInitializer_TwoMembers_Succeeds()
        => AssertParsesV3("class C { int X; int Y; void M() { var c = new C { X = 5, Y = 6 }; } }");

    [TestMethod]
    public void ObjectInitializer_WithArgs_Succeeds()
        => AssertParsesV3("class C { C(int x) { } void M() { var c = new C(1) { X = 5 }; } }");

    [TestMethod]
    public void ObjectInitializer_Empty_Succeeds()
        => AssertParsesV3("class C { void M() { var c = new C { }; } }");

    // === POSITIVE (version 3): collection initializers. ===

    [TestMethod]
    public void CollectionInitializer_Ints_Succeeds()
        => AssertParsesV3("class C { void M() { var l = new List<int> { 1, 2, 3 }; } }");

    [TestMethod]
    public void CollectionInitializer_NestedObjectCreations_Succeeds()
        => AssertParsesV3("class C { void M() { var l = new List<C> { new C { X = 1 }, new C { X = 2 } }; } }");

    [TestMethod]
    public void CollectionInitializer_ComparisonElement_Succeeds()
    {
        // A collection element that is a comparison (`x == 5 ? 1 : 2`), NOT a member assignment:
        // `x == 5` is an Expression (the `!("==")` lookahead rejects MemberAssignment).
        AssertParsesV3("class C { void M() { var l = new List<int> { x == 5 ? 1 : 2 }; } }");
    }

    // === POSITIVE (version 3): anonymous types. ===

    [TestMethod]
    public void AnonymousType_TwoMembers_Succeeds()
        => AssertParsesV3("class C { void M() { var a = new { X = 5, Y = 6 }; } }");

    // === POSITIVE (version 3): Roslyn-derived, adapted to full declarations. ===

    [TestMethod]
    public void Roslyn_NewWithEmptyInitializer_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ExpressionParsingTests.cs:1209
        // TestNewWithEmptyInitializer — "new a() { }" (object creation, empty args + empty initializer).
        AssertParsesV3("class C { C() { } void M() { var c = new C() { }; } }");
    }

    [TestMethod]
    public void Roslyn_NewWithNoArgumentsAndInitializers_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ExpressionParsingTests.cs:1284
        // TestNewWithNoArgumentsAndInitializers — "new a { b, c, d }" (collection initializer, 3 elements).
        AssertParsesV3("class C { void M() { var l = new List<int> { 1, 2, 3 }; } }");
    }

    [TestMethod]
    public void Roslyn_NewWithNoArgumentsAndAssignmentInitializer_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ExpressionParsingTests.cs:1310
        // TestNewWithNoArgumentsAndAssignmentInitializer — "new a { B = b }" (object initializer, member assignment).
        AssertParsesV3("class C { int B; void M() { var c = new C { B = 5 }; } }");
    }

    [TestMethod]
    public void Roslyn_AnonymousObjectCreation_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ExpressionParsingTests.cs:1932
        // TestAnonymousObjectCreation — "new {a, b}" (anonymous type). Adapted to named members
        // (Roslyn's parser accepts unnamed members; the binder rejects them — parser-vs-binder split).
        AssertParsesV3("class C { void M() { var a = new { X = 5, Y = 6 }; } }");
    }

    // === VERSION-PURITY (version 2, no CS3): initializers must REJECT at v2. ===

    [TestMethod]
    public void ObjectInitializer_RejectedAtV2()
        => AssertFailsV2("class C { int X; void M() { var c = new C { X = 5 }; } }");

    [TestMethod]
    public void CollectionInitializer_RejectedAtV2()
        => AssertFailsV2("class C { void M() { var l = new List<int> { 1, 2, 3 }; } }");

    [TestMethod]
    public void AnonymousType_RejectedAtV2()
        => AssertFailsV2("class C { void M() { var a = new { X = 5 }; } }");

    // === NEGATIVE (version 3, malformed): must REJECT at v3. ===

    [TestMethod]
    public void ObjectInitializer_MissingValue_Rejected()
        => AssertFailsV3("class C { void M() { var c = new C { X = }; } }");

    [TestMethod]
    public void AnonymousType_Empty_Rejected()
    {
        // `new { }` (empty anonymous type) is NOT valid in CS3 (binder CS0659 "Anonymous type must
        // have at least one member"); enforced at the parse level via AnonymousInitializer's `+`.
        AssertFailsV3("class C { void M() { var a = new { }; } }");
    }

    [TestMethod]
    public void ObjectInitializer_TrailingComma_Rejected()
        => AssertFailsV3("class C { void M() { var c = new C { X = 5, }; } }");

    [TestMethod]
    public void AnonymousType_Unclosed_Rejected()
        => AssertFailsV3("class C { void M() { var a = new { X = 5; } }");

    // === helpers ===

    private static void AssertParsesV3(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(3), input);

    private static void AssertFailsV3(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(3), input);

    private static void AssertFailsV2(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(2), input);

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
