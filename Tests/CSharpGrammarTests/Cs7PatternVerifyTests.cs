using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.7.4 — C# 7.2 type/constant patterns (VERIFY, no grammar change). The type/constant patterns
// (T3.6.2) and the generic type/declaration patterns are ALREADY supported by the existing rules:
//   * The Cs1 `is` is a TDOPP POSTFIX `TypeIs = Expression : Relational "is" Type` (Cs1.grammar:662) —
//     the C# 1.0 type check. The `Type` rule (Cs1.grammar:508) includes the greedy `TypeName`
//     (Cs2.grammar:44, T3.1.1.1) with a `TypeArgumentList`, so `x is List<int>` parses as a C# 1.0 type
//     check with `Type = List<int>` at v2+ (the generic type pattern — NOT a C# 7.2 feature; see
//     docs/CSharpParserPlan-progressT3.7.4.md, Deviation D1).
//   * The Cs7 `is` pattern postfix `TypeIsPattern = Expression : Relational "is" Pattern`
//     (Cs7.grammar:108) + the `Pattern` rule (Cs7.grammar:116) add the CS7.0 constant pattern
//     (`x is 5`) and declaration pattern (`x is List<int> y`) — CS7.0 only.
// Positives use CreateParser(7); version-purity negatives use CreateParser(6) (no CS7). See
// docs/CSharpParserPlan-progressT3.7.4.md for the hand-traces.
[TestClass]
public class Cs7PatternVerifyTests
{
    // === POSITIVE (version 7): `is` type patterns (verify T3.6.2). ===

    [TestMethod]
    public void TypePattern_IsInt_Succeeds()
        => AssertParsesV7("class C { void M() { if (x is int) { } } }");

    [TestMethod]
    public void TypePattern_IsString_Succeeds()
        => AssertParsesV7("class C { void M() { if (x is string) { } } }");

    // === POSITIVE (version 7): `is` constant patterns (verify T3.6.2). ===

    [TestMethod]
    public void ConstantPattern_IsIntLiteral_Succeeds()
        => AssertParsesV7("class C { void M() { if (x is 5) { } } }");

    // === POSITIVE (version 7): generic type patterns (the key new thing — already supported via the
    // greedy TypeName, no grammar change). ===

    [TestMethod]
    public void GenericTypePattern_IsListInt_Succeeds()
        => AssertParsesV7("class C { void M() { if (x is List<int>) { } } List<int> x; }");

    // === POSITIVE (version 7): generic declaration patterns (the CS7.0 declaration pattern with a
    // generic type — already supported via Pattern -> DeclarationPattern = Type ... Identifier). ===

    [TestMethod]
    public void GenericDeclarationPattern_IsListIntY_Succeeds()
        => AssertParsesV7("class C { void M() { if (x is List<int> y) { N(y); } } List<int> x; }");

    // === POSITIVE (version 1 and 6): the type pattern is C# 1.0 — must stay green. ===

    [TestMethod]
    public void TypePattern_ParsesAtV1()
        => AssertParsesV1("class C { void M() { if (x is int) { } } }");

    [TestMethod]
    public void TypePattern_ParsesAtV6()
        => AssertParsesV6("class C { void M() { if (x is int) { } } }");

    // === POSITIVE (version 2 and 6): the GENERIC type pattern is C# 1.0 (the `is` operator with a
    // type) + C# 2.0 (the generic type) — it parses at v2+, so the task's premise that it must REJECT
    // at v6 is a FALSE PREMISE (progress doc, Deviation D1). Verified: it parses at v2 and v6. ===

    [TestMethod]
    public void GenericTypePattern_ParsesAtV2()
        => AssertParsesV2("class C { void M() { if (x is List<int>) { } } List<int> x; }");

    [TestMethod]
    public void GenericTypePattern_ParsesAtV6()
        => AssertParsesV6("class C { void M() { if (x is List<int>) { } } List<int> x; }");

    // === NEGATIVE (version 6, version-purity): the CS7.0 pattern constructs. ===
    // The constant pattern (`x is 5`) and the declaration pattern (`x is List<int> y`) are CS7.0
    // (T3.6.2) — they REJECT at v6. (The generic TYPE pattern `x is List<int>` is NOT rejected at v6 —
    // see GenericTypePattern_ParsesAtV6 above; the task's version-purity negative for it is a false
    // premise, progress doc Deviation D1.)

    [TestMethod]
    public void ConstantPattern_RejectedAtV6()
        => AssertFailsV6("class C { void M() { if (x is 5) { } } }");

    [TestMethod]
    public void GenericDeclarationPattern_RejectedAtV6()
        => AssertFailsV6("class C { void M() { if (x is List<int> y) { } } List<int> x; }");

    // === POSITIVE (version 7): Roslyn-derived, adapted to full declarations. ===

    [TestMethod]
    public void GenericTypePattern_SingleArg_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/PatternParsingTests.cs:309
        // (IsPatternPrecedence_3) — `e is A<B>` (the `is` operator with a single-arg generic type).
        // Adapted to a full method body (a field of the same type is added so the field declaration
        // also parses).
        AssertParsesV7("class C { void M() { if (x is A<B>) { } } A<B> x; }");
    }

    [TestMethod]
    public void GenericTypePattern_MultiArgArray_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/PatternParsingTests.cs:311
        // (IsPatternPrecedence_3) — `(item is Dictionary<string, object>[])` (the `is` operator with a
        // multi-arg generic ARRAY type). Adapted: the array rank is dropped (kept as a multi-arg
        // generic type) to stay within the task's scope; the multi-arg `TypeArgumentList` is the
        // construct under test.
        AssertParsesV7("class C { void M() { if (x is Dictionary<string, int>) { } } Dictionary<string, int> x; }");
    }

    [TestMethod]
    public void GenericTypePattern_Disambiguation_Roslyn_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/PatternParsingTests.cs:325
        // (TypeDisambiguation_01) — `where s is X<T> // should disambiguate as a type here` (the `is`
        // operator with a generic type whose argument is a type parameter). Adapted to a full method
        // body on a generic class (so `T` is a real type parameter).
        AssertParsesV7("class C<T> { void M() { if (x is X<T>) { } } X<T> x; }");
    }

    // === helpers ===

    private static void AssertParsesV7(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(7), input);

    private static void AssertFailsV7(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(7), input);

    private static void AssertParsesV6(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(6), input);

    private static void AssertFailsV6(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(6), input);

    private static void AssertParsesV2(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(2), input);

    private static void AssertParsesV1(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(1), input);

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
