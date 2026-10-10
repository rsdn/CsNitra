using ExtensibleParser;
using ExtensibleParser.Recovery;

namespace Recovery;

[TestClass]
public sealed class RecoveryPhaseTests
{
    [TestMethod]
    public void NestedPredicateCannotPublishItsFailureSnapshot()
    {
        var parser = new Parser(RecoveryTerminals.Trivia()) { Profile = RecoveryProfile.Compiler };
        var suppressed = false;
        var observer = new Observer(() => suppressed = (bool)typeof(Parser).GetField("_suppressSideEffects",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(parser)!);
        parser.Rules["S"] = [new Seq([new NotPredicate(new Seq([
            new AndPredicate(new Literal("a")), new Literal("a"), observer, new Literal("z")], "Hidden")),
            new Literal("b")], "S")];
        parser.BuildTdoppRules();
        parser.Parse("a", "S", out _);
        Assert.IsTrue(suppressed, "Nested predicates must retain the outer suppression scope.");
        Assert.AreEqual(0, parser.ErrorPos);
        Assert.IsNotNull(parser.LastSnapshot);
        Assert.AreEqual("b", ((Literal)parser.LastSnapshot.FailedTerminal).Value);
    }
    [TestMethod]
    public void IndependentMissingTokensBothRemainVisible()
    {
        var parser = new Parser(RecoveryTerminals.Trivia()) { Profile = RecoveryProfile.Test };
        parser.Rules["Item"] = [new Seq([new Literal("a"), new Literal("b"), new Literal(";")], "Item")];
        parser.Rules["Module"] = [new ZeroOrMany(new Ref("Item"), "Items")];
        parser.BuildTdoppRules();
        Assert.IsTrue(parser.Parse("a ; a ;", "Module", out _).TryGetSuccess(out _, out var end));
        Assert.AreEqual(7, end);
        Assert.AreEqual(2, parser.RecoveryDiagnostics.Count(d => d.Kind == RecoveryKind.Inserted));
    }

    [TestMethod]
    public void CandidateExceptionRollsBackPatchesAndRestoresPhase()
    {
        var parser = new Parser(RecoveryTerminals.Trivia()) { Profile = RecoveryProfile.Test };
        parser.Rules["S"] = [new Seq([new Literal("a"), new Literal("b"), new RecoveryThrower(parser)], "S")];
        parser.BuildTdoppRules();
        Assert.ThrowsException<InvalidOperationException>(() => parser.Parse("a x", "S", out _));
        Assert.AreEqual(0, parser.Injections.Count);
        Assert.AreEqual(0, parser.CurrentStackFrames.Count);
        Assert.AreEqual("Normal", Phase(parser));
        Assert.IsTrue(parser.Parse("a b c", "S", out _).TryGetSuccess(out _, out var end));
        Assert.AreEqual(5, end);
    }

    private static string Phase(Parser parser) => typeof(Parser).GetField("_phase",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(parser)!.ToString()!;

    private sealed record RecoveryThrower(Parser Parser) : Terminal("c")
    {
        public override int TryMatch(string input, int startPos)
        {
            if (Phase(Parser) == "Recovery")
                throw new InvalidOperationException("Candidate parse failed unexpectedly.");
            return startPos < input.Length && input[startPos] == 'c' ? 1 : -1;
        }
    }

    private sealed record Observer(Action Inspect) : Terminal("Observer")
    {
        public override int TryMatch(string input, int startPos)
        {
            Inspect();
            return 0;
        }
    }
}
