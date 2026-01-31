using CsNitra.Ast;
using ExtensibleParaser;

namespace CsNitra.Tests;

[TestClass]
public class GrammarValidationTests
{
    [TestMethod]
    public void ShouldReportErrorForUndefinedRuleReference()
    {
        var expectedName = "UndefinedRule";
        var grammarText = $"""
            Grammar = Statement*;

            Statement = Identifier "=" {expectedName} ";";
            """;

        var parseResult = new CsNitraParser().Parse<GrammarAst>(grammarText);

        if (parseResult is not Success<GrammarAst>(var grammar))
        {
            Assert.Fail("Grammar should parse successfully");
            return;
        }

        var typeChecker = new TypeChecker(new SourceText(grammarText, "test.grammar"), terminals: [
            CsNitraTerminals.Identifier(),
            CsNitraTerminals.Literal(),
        ]);
        var (diagnostics, _) = typeChecker.CheckGrammar(grammar);
        Assert.AreEqual(expected: 1, actual: diagnostics.Count);
        var e = diagnostics[0];
        Assert.AreEqual(expected: DiagnosticSeverity.Error, actual: e.Severity);
        Assert.IsTrue(e.Message.Contains(expectedName), "Should report error for undefined rule reference");
        var actualName = grammarText[e.Location.Start..e.Location.End];
        Assert.AreEqual(expected: expectedName, actual: actualName);
    }

    [TestMethod]
    public void RequiredSubruleNamesAreNotSpecified()
    {
        var grammarText = """
        Grammar = Statements=Identifier*;                 // Test all cases where subrule names are required
    
        // ERROR CASES:
        Rule1 = "test" Identifier*;                      // CASE 1: ZeroOrMany without name - ERROR
        Rule2 = "test" Identifier+;                      // CASE 2: OneOrMany without name - ERROR  
        Rule3 = "test" (Identifier; ",")+;               // CASE 3: SeparatedList without name - ERROR
        BadNestedName = "test" Second=Items=Identifier;  // CASE 4: Nested name assignment - ERROR
    
        // NO ERROR CASES:
        Rule4 = "test" Identifier Identifier;            // CASE 5: Sequence directly in rule - NO ERROR (gets name from rule)
        Rule5 = "test" Identifier Identifier Identifier; // CASE 6: Long sequence directly in rule - NO ERROR (gets name from rule)
        Rule6 = "test" (Identifier Identifier);          // CASE 7: Group with sequence - NO ERROR (inherits name)
        Rule7 = "test" Identifier?;                      // CASE 8: Optional without name - NO ERROR (allowed)
        Rule8 = "test" Identifier??;                     // CASE 9: OftenMissed without name - NO ERROR (allowed)
    
        // CORRECT cases (with names):
        GoodRule1 = "test" Items=Identifier*;            // ZeroOrMany with name
        GoodRule2 = "test" Items=Identifier+;            // OneOrMany with name
        GoodRule3 = Left="test" Right=Identifier;        // Sequence with names
        GoodRule4 = "test" Items=(Identifier; ",")+;     // SeparatedList with name
        GoodRule5 =                                      // Named alternatives
            | Alt1=Items=Identifier*
            | Alt2=Left=Identifier Right=Identifier;
    
        // Complex case from parser grammar
        ComplexRule =
            | RuleRef = Ref=Identifier PrecedenceWithAssociativity=(":" Precedence=Identifier Associativity=("," Identifier)?)?
            | SeparatedList = "(" Element=Identifier ";" Separator=Identifier SeparatorModifier=(":" Identifier)? ")" "*";
        """;

        var manualParseResult = new CsNitraParser().Parse<GrammarAst>(grammarText);

        if (manualParseResult is Failed(var error))
            Assert.Fail($"Manual parser failed to parse grammar: {error.GetErrorText()}");

        if (manualParseResult is not Success<GrammarAst>(var manualGrammar))
        {
            Assert.Fail("Manual parse result is not success");
            return;
        }

        var typeChecker = new TypeChecker(new SourceText(grammarText, "test.grammar"), terminals: [
            CsNitraTerminals.Identifier(),
            CsNitraTerminals.Literal(),
        ]);

        var (diagnostics, globalScope) = typeChecker.CheckGrammar(manualGrammar);

        // Display all diagnostics in format (startPos, endPos): Message
        Console.WriteLine("All diagnostics:");
        foreach (var diagnostic in diagnostics)
        {
            var errorText = grammarText[diagnostic.Location.Start..diagnostic.Location.End];
            Console.WriteLine($"  ({diagnostic.Location.Start}, {diagnostic.Location.End}): {diagnostic.Severity} - {diagnostic.Message}");
        }

        // Filter name-related errors
        var nameErrors = diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error &&
                       (d.Message.Contains("must have a name") || d.Message.Contains("Nested name")))
            .ToList();

        // Should have 4 errors: Rule1, Rule2, Rule3, BadNestedName
        Assert.AreEqual(expected: 5, actual: nameErrors.Count,
            $"Expected 5 name-related errors (for Rule1, Rule2, Rule3, BadNestedName, Rule6), got {nameErrors.Count}.");

        // Check specific errors
        var errorLocations = nameErrors
            .Select(e => (e.Location.Start, e.Location.End,
                         Text: grammarText[e.Location.Start..e.Location.End]))
            .ToList();

        // Verify errors point to correct locations
        var expectedErrors = new[]
        {
            ("Identifier*", "Rule1"),
            ("Identifier+", "Rule2"),
            ("(Identifier; \",\")+", "Rule3"),
            ("Items", "BadNestedName"),
            ("(Identifier Identifier)", "Rule6"),
        };

        foreach (var (expectedText, ruleName) in expectedErrors)
        {
            var hasError = errorLocations.Any(loc => loc.Text.Contains(expectedText));

            Assert.IsTrue(hasError,
                $"Missing error for {ruleName} with text '{expectedText}'");
        }

        // Check for other errors (like undefined symbols)
        var otherErrors = diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error
                && !d.Message.Contains("must have a name")
                && !d.Message.Contains("Nested name"))
            .ToList();

        // For debugging: show all unexpected other errors
        if (otherErrors.Count > 0)
        {
            Console.WriteLine("Unexpected non-name errors:");
            foreach (var diagnostic in otherErrors.Where(e => !e.Message.Contains("'Statement' not found")))
            {
                var errorText = grammarText[diagnostic.Location.Start..diagnostic.Location.End];
                Console.WriteLine($"  ({diagnostic.Location.Start}, {diagnostic.Location.End}): {diagnostic.Message}");
                Console.WriteLine($"    Text: '{errorText}'");
            }

            Assert.Fail($"Found {otherErrors.Count} unexpected errors");
        }
    }

}




