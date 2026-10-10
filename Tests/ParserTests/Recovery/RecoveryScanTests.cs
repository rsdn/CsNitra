using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace Recovery;

[TestClass]
public sealed class RecoveryScanTests
{
    [DataTestMethod]
    [DataRow("$ { ; } ;")]
    [DataRow("$ { ] ; } ;")]
    [DataRow("$ ( ; ) ;")]
    [DataRow("$ [ ; ] ;")]
    public void BothSkipStrategiesIgnoreNestedTerminators(string input)
    {
        var parser = new Parser(RecoveryTerminals.Trivia());
        parser.Rules["S"] = [new Seq([new Literal("a"), new Literal("b"), new Literal(";")], "S")];
        parser.BuildTdoppRules();
        var terminal = new Literal(";");
        var snapshot = new FailureSnapshot(0, [new StackFrame("S", 0, new SeqFrameLocation(0), [],
            new RecoveryOptions { Terminators = [terminal] })], new Literal("a"), []);
        var candidates = RecoveryEngine.Generate(0, snapshot, input, parser, Result.Kind.Failure, "S", 0, 0);
        foreach (var candidate in candidates.Where(c => c.Rank is 3 or 6))
            Assert.AreEqual(input.LastIndexOf(';'), candidate.Pos, $"S{candidate.Rank}");
        Assert.IsTrue(candidates.Any(c => c.Rank == 6));
    }

    [TestMethod]
    public void EmptySkipAtEofIsNotAnS3Repair()
    {
        var parser = new Parser(RecoveryTerminals.Trivia());
        parser.Rules["S"] = [new Seq([new Literal("a"), new Literal(";")], "S")];
        parser.BuildTdoppRules();
        var snapshot = new FailureSnapshot(1, [new StackFrame("S", 0, new SeqFrameLocation(1), [],
            new RecoveryOptions { Terminators = [EofTerminal.Instance] })], new Literal(";"), []);
        Assert.IsFalse(RecoveryEngine.Generate(1, snapshot, "a", parser, Result.Kind.Partial, "S", 0, 1)
            .Any(c => c.Rank == 3));
    }

    [TestMethod]
    public void CustomBoundariesAreDeterministicAndIdempotent()
    {
        var parser = new Parser(RecoveryTerminals.Trivia());
        parser.AddBracketPair(new Literal("<"), new Literal(">"));
        parser.AddBracketPair(new Literal("<["), new Literal("]>"));
        parser.AddBracketPair(new Literal("<["), new Literal("!>"));
        parser.AddBracketPair(new Literal("<["), new Literal("]>"));
        parser.AddBracketPair(new Literal(""), new Literal(""));
        Assert.AreEqual(4, parser.BracketPairs.Count);
        var reversed = new Parser(RecoveryTerminals.Trivia());
        foreach (var pair in parser.BracketPairs.Reverse())
            reversed.AddBracketPair(pair.Open, pair.Close);
        CollectionAssert.AreEqual(parser.BracketPairs.ToArray(), reversed.BracketPairs.ToArray());
        foreach (var text in new[] { "$ <[ ; ]> ;", "$ <[ ; !> ;", "$ < ; > ;" })
        {
            AssertStopsAtLastSemicolon(parser, text);
            AssertStopsAtLastSemicolon(reversed, text);
        }
    }

    [TestMethod]
    public void OpaqueRegionsHideBracketsAndTerminators()
    {
        var parser = new Parser(RecoveryTerminals.Trivia());
        parser.AddRecoveryOpaqueTerminal(new Quoted());
        AssertStopsAtLastSemicolon(parser, "$ \"{ ; }\" ;");
    }

    [TestMethod]
    public void OpaqueTokenCanBeAnIntactSuffixAnchor()
    {
        var parser = new Parser(RecoveryTerminals.Trivia());
        var quoted = new Quoted();
        parser.AddRecoveryOpaqueTerminal(quoted);
        var sequence = new Seq([new Literal("a"), new Literal("b"), quoted], "S");
        parser.Rules["S"] = [sequence];
        parser.BuildTdoppRules();
        var snapshot = new FailureSnapshot(0,
            [new StackFrame("S", 0, new SeqFrameLocation(0) { Sequence = sequence }, [],
                new RecoveryOptions { Terminators = [quoted] })], new Literal("a"), []);
        var candidates = RecoveryEngine.Generate(0, snapshot, "$ \"ok\"", parser,
            Result.Kind.Failure, "S", 0, 0);
        Assert.IsTrue(candidates.Any(c => c.Rank == 3));
        Assert.IsTrue(candidates.Any(c => c.Rank == 6));
        foreach (var candidate in candidates.Where(c => c.Rank is 3 or 6))
            Assert.AreEqual(2, candidate.Pos, $"S{candidate.Rank}");
    }

    [TestMethod]
    public void UnmatchedNestedDamageFallsBackWithoutLosingSource()
    {
        var parser = new Parser(RecoveryTerminals.Trivia());
        parser.Profile = RecoveryProfile.Test;
        parser.Rules["S"] = [new Seq([new Literal("a"), new Literal("b"), new Literal(";")], "S")];
        parser.BuildTdoppRules();
        var text = "a $ " + new string('(', 4000) + ";";
        var result = parser.Parse(text, "S", out _);
        Assert.IsTrue(result.TryGetSuccess(out var root, out var end));
        Assert.AreEqual(text.Length, end);
        RecoveryStructureTests.AssertCoverage(root, text.Length);
        Assert.IsTrue(parser.RecoveryDiagnostics.Any(d => d.Kind == RecoveryKind.Unrecovered));
    }

    private static void AssertStopsAtLastSemicolon(Parser parser, string input)
    {
        parser.Rules["S"] = [new Seq([new Literal("a"), new Literal("b"), new Literal(";")], "S")];
        parser.BuildTdoppRules();
        var snapshot = new FailureSnapshot(0, [new StackFrame("S", 0, new SeqFrameLocation(0), [],
            new RecoveryOptions { Terminators = [new Literal(";")] })], new Literal("a"), []);
        var candidates = RecoveryEngine.Generate(0, snapshot, input, parser, Result.Kind.Failure, "S", 0, 0);
        foreach (var candidate in candidates.Where(c => c.Rank is 3 or 6))
            Assert.AreEqual(input.LastIndexOf(';'), candidate.Pos, $"S{candidate.Rank}: {input}");
    }

    private sealed record Quoted() : Terminal("Quoted")
    {
        public override int TryMatch(string input, int startPos) => startPos < input.Length && input[startPos] == '"'
            ? input.IndexOf('"', startPos + 1) is var end && end >= 0 ? end - startPos + 1 : input.Length - startPos
            : -1;
    }
}
