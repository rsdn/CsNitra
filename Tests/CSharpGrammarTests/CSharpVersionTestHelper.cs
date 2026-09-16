using CSharpGrammar;

namespace CSharpGrammarTests;

public static class CSharpVersionTestHelper
{
    public static CSharpParser CreateParser(int version) =>
        new(
            EmbeddedGrammar.LoadGrammarUpTo(version),
            CSharpTerminals.Trivia(),
            CSharpTerminals.GetAll());
}
