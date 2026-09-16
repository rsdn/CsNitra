using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.2.5 — C# 3.0 LINQ query expressions. Positives use CreateParser(3) (Cs1+Cs2+Cs3 merged);
// version-purity negatives use CreateParser(2) (Cs1+Cs2, no CS3); the contextual-keyword-as-identifier
// tests use CreateParser(1) (Cs1 only — `from`/`select` etc. are plain identifiers in C# 1.0).
//
// A query expression is a Primary-level expression starting with the contextual keyword `from`. It is
// added to Primary (re-declare, append — the same pattern as the T3.2.1 LambdaExpression). The query
// body is a loop of body clauses (from/join/let/where/orderby) followed by a terminal select/group
// clause, then an optional `into` continuation. The clause expressions are full TDOPP Expressions.
//
// Only `in` is a reserved keyword; all other query keywords (from/where/select/group/into/orderby/
// join/let/on/equals/by/ascending/descending) are contextual (plain identifiers in non-query
// contexts) and are used as literals in the query rules (NOT added to ReservedKeyword).
// See docs/CSharpParserPlan-progressT3.2.5.md for the hand-traces and Roslyn references.
[TestClass]
public class Cs3QueryExpressionTests
{
    // === POSITIVE (version 3): the required query forms. ===

    [TestMethod]
    public void Query_FromSelect_Succeeds()
        => AssertParsesV3("class C { void M() { var q = from x in xs select x; } }");

    [TestMethod]
    public void Query_FromWhereSelect_Succeeds()
        => AssertParsesV3("class C { void M() { var q = from x in xs where x > 0 select x; } }");

    [TestMethod]
    public void Query_FromLetSelect_Succeeds()
        => AssertParsesV3("class C { void M() { var q = from x in xs let y = x * 2 select y; } }");

    [TestMethod]
    public void Query_MultipleFrom_Succeeds()
        => AssertParsesV3("class C { void M() { var q = from x in xs from y in ys select x + y; } }");

    [TestMethod]
    public void Query_FromJoinSelect_Succeeds()
        => AssertParsesV3("class C { void M() { var q = from x in xs join y in ys on x.Key equals y.Key select x; } }");

    [TestMethod]
    public void Query_FromJoinIntoSelect_Succeeds()
        => AssertParsesV3("class C { void M() { var q = from x in xs join y in ys on x.Key equals y.Key into z select z; } }");

    [TestMethod]
    public void Query_FromGroupBySelect_Succeeds()
        => AssertParsesV3("class C { void M() { var q = from x in xs group x by x.Key select x; } }");

    [TestMethod]
    public void Query_FromOrderByDescendingSelect_Succeeds()
        => AssertParsesV3("class C { void M() { var q = from x in xs orderby x descending select x; } }");

    [TestMethod]
    public void Query_FromOrderByMultipleAscending_Succeeds()
        => AssertParsesV3("class C { void M() { var q = from x in xs orderby x, x.Name ascending select x; } }");

    [TestMethod]
    public void Query_FromSelectAnonymousType_Succeeds()
        => AssertParsesV3("class C { void M() { var q = from x in xs select new { x, Name = x.Name }; } }");

    // === POSITIVE (version 3): Roslyn-derived, adapted to full declarations. ===

    [TestMethod]
    public void Roslyn_TestFromSelect_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ExpressionParsingTests.cs:2301
        // TestFromSelect — "from a in A select b".
        AssertParsesV3("class C { void M() { var q = from a in A select b; } }");
    }

    [TestMethod]
    public void Roslyn_TestFromWithType_Succeeds()
    {
        // Roslyn: ExpressionParsingTests.cs:2334 TestFromWithType — "from T a in A select b"
        // (the optional query-variable type).
        AssertParsesV3("class C { void M() { var q = from T a in A select b; } }");
    }

    [TestMethod]
    public void Roslyn_TestFromSelectIntoSelect_Succeeds()
    {
        // Roslyn: ExpressionParsingTests.cs:2367 TestFromSelectIntoSelect — "from a in A select b
        // into c select d" (the `into` after a select is a QueryContinuation, a new QueryBody).
        AssertParsesV3("class C { void M() { var q = from a in A select b into c select d; } }");
    }

    [TestMethod]
    public void Roslyn_TestFromGroupByIntoSelect_Succeeds()
    {
        // Roslyn: ExpressionParsingTests.cs:2777 TestFromGroupByIntoSelect — "from a in A group b by
        // c into d select e" (the `into` after a group is a QueryContinuation).
        AssertParsesV3("class C { void M() { var q = from a in A group b by c into d select e; } }");
    }

    [TestMethod]
    public void Roslyn_TestFromJoinWithTypeSelect_Succeeds()
    {
        // Roslyn: ExpressionParsingTests.cs:2886 TestFromJoinWithTypeSelect — "from Ta a in A join
        // Tb b in B on a equals b select c" (the optional query-variable type on a join).
        AssertParsesV3("class C { void M() { var q = from Ta a in A join Tb b in B on a equals b select c; } }");
    }

    // === POSITIVE (version 1 / 3): query keywords as plain identifiers (contextual). ===
    // `from`/`select`/etc. are contextual keywords (plain identifiers in non-query contexts). They
    // are NOT added to ReservedKeyword, so they remain valid identifiers at v1/v2/v3.
    [TestMethod]
    public void Contextual_From_AsFieldName_ParsesAtV1_Succeeds()
        => AssertParses(CSharpVersionTestHelper.CreateParser(1), "class C { int from; }");

    [TestMethod]
    public void Contextual_Select_AsFieldName_ParsesAtV1_Succeeds()
        => AssertParses(CSharpVersionTestHelper.CreateParser(1), "class C { int select; }");

    [TestMethod]
    public void Contextual_From_AsFieldName_ParsesAtV2_Succeeds()
        => AssertParses(CSharpVersionTestHelper.CreateParser(2), "class C { int from; }");

    [TestMethod]
    public void Contextual_From_AsFieldName_ParsesAtV3_Succeeds()
        => AssertParsesV3("class C { int from; }");

    // === VERSION-PURITY (version 2, no CS3): query expressions must REJECT at v2. ===

    [TestMethod]
    public void Query_FromSelect_RejectedAtV2()
        => AssertFailsV2("class C { void M() { var q = from x in xs select x; } }");

    [TestMethod]
    public void Query_FromWhereSelect_RejectedAtV2()
        => AssertFailsV2("class C { void M() { var q = from x in xs where x > 0 select x; } }");

    // === NEGATIVE (version 3, malformed): must REJECT at v3. ===

    [TestMethod]
    public void Query_MissingSelect_Rejected()
        => AssertFailsV3("class C { void M() { var q = from x in xs; } }");

    [TestMethod]
    public void Query_MissingIn_Rejected()
        => AssertFailsV3("class C { void M() { var q = from x select x; } }");

    [TestMethod]
    public void Query_MissingSelectExpression_Rejected()
        => AssertFailsV3("class C { void M() { var q = from x in xs select; } }");

    [TestMethod]
    public void Query_MissingWhereExpression_Rejected()
        => AssertFailsV3("class C { void M() { var q = from x in xs where select x; } }");

    [TestMethod]
    public void Query_MissingOrdering_Rejected()
        => AssertFailsV3("class C { void M() { var q = from x in xs orderby select x; } }");

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
