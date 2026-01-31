#nullable enable

using Diagnostics;
using ExtensibleParaser;
using NitraConstruction.Common;
using System.Diagnostics;
using Tests.Extensions;

namespace NitraConstruction;

[TestClass]
public partial class SeparatedListTests
{
    private readonly Parser _parser = new(Terminals.Trivia(), new Log(LogImportance.High));

    [TestInitialize]
    public void Initialize()
    {
        var closingBracket = new OftenMissed(new Literal(")"));

        _parser.Rules["ExpOptional"] = new Rule[]
        {
            Terminals.Number(),
            Terminals.Ident(),
            new Seq([Terminals.Ident(), new Literal("("), new SeparatedList(new Ref("ExpOptional"), new Literal(","), Kind: "ArgsRestOptional", EndBehavior: SeparatorEndBehavior.Optional), closingBracket], "CallOptional"),

            Terminals.ErrorEmpty(),
        };

        _parser.Rules["ExpRequired"] = new Rule[]
        {
            Terminals.Number(),
            Terminals.Ident(),
            new Seq([Terminals.Ident(), new Literal("("), new SeparatedList(new Ref("ExpRequired"), new Literal(","), Kind: "ArgsRestRequired", EndBehavior: SeparatorEndBehavior.Required), closingBracket], "CallRequired"),

            Terminals.ErrorEmpty(),
        };

        _parser.Rules["ExpForbidden"] = new Rule[]
        {
            Terminals.Number(),
            Terminals.Ident(),
            new Seq([Terminals.Ident(), new Literal("("), new SeparatedList(new Ref("ExpForbidden"), new Literal(","), Kind: "ArgsRestForbidden", EndBehavior: SeparatorEndBehavior.Forbidden), closingBracket], "CallForbidden"),

            Terminals.ErrorEmpty(),
        };

        _parser.BuildTdoppRules();
    }

    // Optional

    [TestMethod]
    public void OptionalCallWith0Args() =>
        TestSeparatedList(
            "ExpOptional",
            "func()",
            "Call: func()"
        );

    [TestMethod]
    public void OptionalCallWith1Args() =>
        TestSeparatedList(
            "ExpOptional",
            "func(1)",
            "Call: func(1)"
        );

    [TestMethod]
    public void OptionalCallWith2Args() =>
        TestSeparatedList(
            "ExpOptional",
            "func(1, 2)",
            "Call: func(1, 2)"
        );

    [TestMethod]
    public void OptionalCallWith3Args() =>
        TestSeparatedList(
            "ExpOptional",
            "func(1, 2, 3)",
            "Call: func(1, 2, 3)"
        );


    [TestMethod]
    public void OptionalCallWith1ArgsWitEndDelim() =>
        TestSeparatedList(
            "ExpOptional",
            "func(1, )",
            "Call: func(1)"
        );

    [TestMethod]
    public void OptionalCallWith2ArgsWitEndDelimArgs() =>
        TestSeparatedList(
            "ExpOptional",
            "func(1, 2, )",
            "Call: func(1, 2)"
        );

    [TestMethod]
    public void OptionalCallWith3ArgsWitEndDelim() =>
        TestSeparatedList(
            "ExpOptional",
            "func(1, 2, 3, )",
            "Call: func(1, 2, 3)"
        );

    // Required

    [TestMethod]
    public void RequiredCallWith1Args() =>
        TestSeparatedListRecovery(
            "ExpRequired",
            "func(1)",
            "func",
            expecteds: [","]
        );

    [TestMethod]
    public void RequiredCallWith2Args() =>
        TestSeparatedListRecovery(
            "ExpRequired",
            "func(1, 2)",
            "func",
            expecteds: [","]
        );

    [TestMethod]
    public void RequiredCallWith3Args() =>
        TestSeparatedListRecovery(
            "ExpRequired",
            "func(1, 2, 3)",
            "func",
            expecteds: [","]
        );


    [TestMethod]
    public void RequiredCallWith1ArgsWitEndDelim() =>
        TestSeparatedList(
            "ExpRequired",
            "func(1, )",
            "Call: func(1)"
        );

    [TestMethod]
    public void RequiredCallWith2ArgsWitEndDelimArgs() =>
        TestSeparatedList(
            "ExpRequired",
            "func(1, 2, )",
            "Call: func(1, 2)"
        );

    [TestMethod]
    public void RequiredCallWith3ArgsWitEndDelim() =>
        TestSeparatedList(
            "ExpRequired",
            "func(1, 2, 3, )",
            "Call: func(1, 2, 3)"
        );

    // Forbidden

    [TestMethod]
    public void ForbiddenCallWith0Args() =>
        TestSeparatedList(
            "ExpForbidden",
            "func()",
            "Call: func()"
        );

    [TestMethod]
    public void ForbiddenCallWith1Args() =>
        TestSeparatedList(
            "ExpForbidden",
            "func(1)",
            "Call: func(1)"
        );

    [TestMethod]
    public void ForbiddenCallWith2Args() =>
        TestSeparatedList(
            "ExpForbidden",
            "func(1, 2)",
            "Call: func(1, 2)"
        );

    [TestMethod]
    public void ForbiddenCallWith3Args() =>
        TestSeparatedList(
            "ExpForbidden",
            "func(1, 2, 3)",
            "Call: func(1, 2, 3)"
        );


    [TestMethod]
    public void ForbiddenCallWith1ArgsWitEndDelim() =>
        TestSeparatedListRecovery(
            startRule: "ExpForbidden",
            input: "func(1, )",
            expectedAst: "Call: func(1, «Error: expected Expr»)",
            expecteds: ["Number", "Ident"]
        );

    [TestMethod]
    public void ForbiddenCallWith2ArgsWitEndDelimArgs() =>
        TestSeparatedListRecovery(
            "ExpForbidden",
            "func(1, 2, )",
            "Call: func(1, 2, «Error: expected Expr»)",
            expecteds: ["Number", "Ident"]
        );

    [TestMethod]
    public void ForbiddenCallWith3ArgsWitEndDelim() =>
        TestSeparatedListRecovery(
            "ExpForbidden",
            "func(1, 2, 3, )",
            "Call: func(1, 2, 3, «Error: expected Expr»)",
            expecteds: ["Number", "Ident"]
        );

    private void TestSeparatedListRecovery(string startRule, string input, string expectedAst, string[] expecteds)
    {
        Trace.WriteLine($"\n=== TEST START: {input} ===");

        var parseResult = _parser.Parse(input, startRule, out _);
        if (_parser.ErrorInfo is { } error)
        {
            CollectionAssert.AreEquivalent(expected: expecteds, actual: error.Expecteds.Select(x => x.Kind).ToArray());
            Assert.AreEqual(expected: input.IndexOf(')'), error.Pos);
            Trace.WriteLine($"✅ Recovered error: {error.GetErrorText()}");
        }

        Assert.IsTrue(parseResult.TryGetSuccess(out var node, out _));
        Trace.WriteLine($"✅ Parse SUCCESS. Node type: {node.Kind}");
        Trace.WriteLine($"Node structure:\n{node}");

        var visitor = new SeparatedListVisitor(input);
        node.Accept(visitor);

        if (visitor.Result == null)
        {
            Trace.WriteLine("❌ AST generation FAILED");
            Assert.Fail("AST is null");
            return;
        }

        Trace.WriteLine($"Generated AST: {visitor.Result}");
        Assert.AreEqual(expectedAst.NormalizeEol(), visitor.Result.ToString().NormalizeEol());
    }

    private void TestSeparatedList(string startRule, string input, string expectedAst)
    {
        Trace.WriteLine($"\n=== TEST START: {input} ===");

        var parseResult = _parser.Parse(input, startRule, out _);
        if (_parser.ErrorInfo is { } error)
        {
            Trace.WriteLine($"❌ Parse FAILED. {error.AssertIsNonNull().GetErrorText()}");
            Assert.Fail($"Parse failed for: {input}");
            return;
        }

        Assert.IsTrue(parseResult.TryGetSuccess(out var node, out _));
        Trace.WriteLine($"✅ Parse SUCCESS. Node type: {node.Kind}");
        Trace.WriteLine($"Node structure:\n{node}");

        var visitor = new SeparatedListVisitor(input);
        node.Accept(visitor);

        if (visitor.Result == null)
        {
            Trace.WriteLine("❌ AST generation FAILED");
            Assert.Fail("AST is null");
            return;
        }

        Trace.WriteLine($"Generated AST: {visitor.Result}");
        Assert.AreEqual(expectedAst.NormalizeEol(), visitor.Result.ToString().NormalizeEol());
    }
}
