using System.Diagnostics;
using Diagnostics;
using Tests.Extensions;

namespace Regex;

[TestClass]
public class RegexParserTests
{
    [TestInitialize]
    public void Setup()
    {
        //Trace.Listeners.Add(new ConsoleTraceListener());
        Trace.AutoFlush = true;
    }

    [TestMethod]
    public void UnicodeLetterClass()
    {
        var parser = new RegexParser(@"\l");
        var node = parser.Parse();

        Assert.IsInstanceOfType(node, typeof(LetterCharClass));
        var letterClass = (LetterCharClass)node;

        Assert.IsTrue(letterClass.Matches('я'));   // Русская буква
        Assert.IsTrue(letterClass.Matches('漢'));  // Китайский иероглиф
        Assert.IsFalse(letterClass.Matches('5')); // Не буква
    }

    [TestMethod]
    public void CombinedClasses()
    {
        var parser = new RegexParser(@"[a-z\d]");
        var node = parser.Parse();

        Assert.IsInstanceOfType(node, typeof(RegexAlternation));
        var alt = (RegexAlternation)node;
        Assert.AreEqual(2, alt.Nodes.Count);
        Assert.IsTrue(node.ToString() == @"[a-z]|\d");

        Assert.IsTrue(alt.Nodes[0] is RangesCharClass { Negated: false, Ranges: [CharRange { From: 'a', To: 'z' }] });  // [a-z]
        Assert.IsTrue(alt.Nodes[0].ToString() == "[a-z]");
        Assert.IsTrue(alt.Nodes[1] is DigitCharClass { Negated: false });   // \d
        Assert.IsTrue(alt.Nodes[1].ToString() == "\\d");
    }

    [TestMethod]
    public void LetterClass()
    {
        var parser = new RegexParser(@"\l");
        var node = parser.Parse();
        Assert.IsInstanceOfType(node, typeof(LetterCharClass));
        Assert.IsFalse(((LetterCharClass)node).Negated);
    }

    [TestMethod]
    public void NfaConstruction()
    {
        var parser = new RegexParser("a|b");
        var nfa = new NfaBuilder().Build(parser.Parse());

        const string expected = """
        State 0:
          ε -> State 2
          ε -> State 4
        State 2:
          a -> State 3
        State 3:
          ε -> State 1
        State 1 (final):
        State 4:
          b -> State 5
        State 5:
          ε -> State 1
        """;

        var actual = NfaPrinter.Print(nfa.StartState).Trim();
        Assert.AreEqual(expected.Trim().NormalizeEol(), actual.NormalizeEol());
    }

    [TestMethod]
    public void DfaConstruction()
    {
        var parser = new RegexParser("a|b");
        var nfa = new NfaBuilder().Build(parser.Parse());
        var dfa = new DfaBuilder().Build(nfa.StartState);

        const string expected = """
        State 0:
          a -> State 1
          b -> State 2
        State 1 (final):
        State 2 (final):
        """;

        var actual = DfaPrinter.Print(dfa).Trim();
        Assert.AreEqual(expected.Trim().NormalizeEol(), actual.NormalizeEol());
    }

    [TestMethod]
    public void DfaMatching()
    {
        var testCases = new[]
        {
            (Start: 0, Pattern: @"[\\\/*+\-<=>!@#$%^&]+", Input: ";",         Expected: -1),
            (Start: 0, Pattern: @"[\\\/*+\-<=>!@#$%^&]+", Input: @"\/-",      Expected: 3),
            (Start: 0, Pattern: @"[\\\/*+\-<=>!@#$%^&]+", Input: @"\/-;",     Expected: 3),
            (Start: 0, Pattern: @"[\l_]\w*",              Input: "_ifField",  Expected: 8),
            (Start: 0, Pattern: @"[\l_]\w*",              Input: "ifField",   Expected: 7),
            (Start: 0, Pattern: @"[\l_]\w*",              Input: "1ifField",  Expected: -1),
            (Start: 0, Pattern: @"\d*",                   Input: "1ifField",  Expected: 1),
            (Start: 0, Pattern: "a+",                     Input: "aaa",       Expected: 3),
            (Start: 0, Pattern: "a+",                     Input: "aaax",      Expected: 3),
            (Start: 0, Pattern: "a*",                     Input: "",          Expected: 0),
            (Start: 1, Pattern: "a*",                     Input: "sz",        Expected: 0),
            (Start: 0, Pattern: "a*",                     Input: "aac",       Expected: 2),
            (Start: 0, Pattern: "a*",                     Input: "aaa",       Expected: 3),
            (Start: 1, Pattern: "a*",                     Input: "xaaay",     Expected: 3),
            (Start: 0, Pattern: "a|b",                    Input: "a",         Expected: 1),
            (Start: 0, Pattern: @"[\l_]\w*",              Input: "_ifField",  Expected: 8),
            (Start: 0, Pattern: @"\w+",                   Input: "мама",      Expected: 4),
            (Start: 0, Pattern: @"\d+",                   Input: "123",       Expected: 3),
            (Start: 0, Pattern: "[a-c]",                  Input: "c",         Expected: 1),
            (Start: 0, Pattern: "[a-c]",                  Input: "d",         Expected: -1),
            (Start: 0, Pattern: "1(2|3)4",                Input: "124",       Expected: 3),
            (Start: 0, Pattern: "1(2|3)4",                Input: "134",       Expected: 3),
            (Start: 0, Pattern: "1(2|3)4",                Input: "1345",      Expected: 3),
            (Start: 3, Pattern: "(ab)*",                  Input: "234ababde", Expected: 4),
            (Start: 0, Pattern: "(ab)*",                  Input: "abab12",    Expected: 4),
            (Start: 0, Pattern: "ab",                     Input: "cabab",     Expected: -1),
            (Start: 0, Pattern: "(ab)?",                  Input: "cabab",     Expected: 0),
            (Start: 0, Pattern: "(ab)?",                  Input: "abab",      Expected: 2),
            (Start: 0, Pattern: "(ab)?(ab)?",             Input: "abab",      Expected: 4),
            (Start: 0, Pattern: @"[^\W\d]\w*",            Input: "d1",        Expected: 2),
            (Start: 0, Pattern: @"[^\W\d]+",              Input: "1234",      Expected: -1),
            (Start: 0, Pattern: @"[^\W\d]+",              Input: "мама",      Expected: 4),
            (Start: 0, Pattern: @"[^\W\d]+",              Input: "qwerty",    Expected: 6),
            (Start: 0, Pattern: @"[^\W\d]+",              Input: "!@#$%",     Expected: -1),
            (Start: 0, Pattern: @"\\[0-da-fA-f][0-da-fA-f]",                          Input: @"\F0",   Expected: 3),
            (Start: 0, Pattern: @"\\[0-da-fA-f][0-da-fA-f][0-da-fA-f]?[0-da-fA-f]?",  Input: @"\F0",   Expected: 3),
            (Start: 0, Pattern: @"\\[0-da-fA-f][0-da-fA-f][0-da-fA-f]?[0-da-fA-f]?",  Input: @"\F0A",  Expected: 4),
            (Start: 0, Pattern: @"\\[0-da-fA-f][0-da-fA-f][0-da-fA-f]?[0-da-fA-f]?",  Input: @"\F0A9", Expected: 5),
        };

        var i = 0;
        foreach (var (Start, Pattern, Input, Expected) in testCases)
        {
            var actual = doTest(Start, Pattern, Input, i);
            if (Expected != actual)
            {
                Trace.WriteLine($"Parsing pattern: '{Pattern}' with input '{Input}' FAILED! Expected: {Expected} Actual: {actual}");
                _ = doTest(Start, Pattern, Input, i, new Log());
            }
            Assert.AreEqual(Expected, actual, $"Parsing pattern: '{Pattern}' with input '{Input}'");
            i++;
        }

        static int doTest(int Start, string Pattern, string Input, int caseIndex, Log? log = null)
        {
            var parser = new RegexParser(Pattern);
            var regexNode = parser.Parse();
            var nfa = new NfaBuilder().Build(regexNode);
            
            if (log != null)
                NfaToDot.GenerateSvg(nfa.StartState, Pattern, $"regex_{caseIndex:D2}_NFA.svg");
            
            var dfa = new DfaBuilder(log).Build(nfa.StartState);

            if (log != null)
                DfaToDot.GenerateSvg(dfa, Pattern, $"regex_{caseIndex:D2}_DFA.svg");

            var result = DfaInterpreter.TryMatch(dfa, Input, Start, log);
            return result;
        }
    }
}
