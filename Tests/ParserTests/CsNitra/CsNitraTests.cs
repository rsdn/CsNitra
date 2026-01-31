using CsNitra.Ast;
using ExtensibleParser;

namespace CsNitra;

[TestClass]
public class CsNitraTests
{
    [TestMethod]
    public void ShouldParseItself()
    {
        var grammarText = GetGrammarText();
        var result = new CsNitraParser().Parse<GrammarAst>(grammarText);

        if (result is Failed(var error))
            Assert.Fail(error.GetErrorText());

        if (result is not Success<GrammarAst>(var ast))
        {
            Assert.Fail("result is not success");
            return;
        }

        Assert.IsNotNull(ast);
        Assert.IsTrue(ast.Statements.Count > 0);
        Assert.IsTrue(ast.Statements.Any(s => s is SimpleRuleStatementAst simple && IsGrammarRule(simple)));

        var usingRule = ast.Statements.OfType<RuleStatementAst>().FirstOrDefault(r => r.Name.Value == "Using");
        Assert.IsNotNull(usingRule);

        Assert.AreEqual(2, usingRule.Alternatives.Count);
        Assert.IsTrue(usingRule.Alternatives[0] is NamedAlternativeAst);
        Assert.IsTrue(usingRule.Alternatives[1] is NamedAlternativeAst);

        Assert.IsTrue(ast.StartPos >= 0);
        Assert.IsTrue(ast.EndPos <= grammarText.Length);
        Assert.IsTrue(ast.EndPos > ast.StartPos);

        foreach (var statement in ast.Statements)
        {
            Assert.IsTrue(statement.StartPos >= ast.StartPos);
            Assert.IsTrue(statement.EndPos <= ast.EndPos);

            if (statement is RuleStatementAst rs)
            {
                Assert.IsFalse(string.IsNullOrEmpty(rs.Name.Value));
                Assert.IsTrue(rs.Alternatives.Count > 0);

                foreach (var alt in rs.Alternatives)
                {
                    if (alt is NamedAlternativeAst named)
                        Assert.IsFalse(string.IsNullOrEmpty(named.Name.Value));
                    else if (alt is AnonymousAlternativeAst anon)
                        Assert.IsTrue(anon.RuleRef.Parts.Count > 0);
                }
            }
        }
    }

    private static bool IsGrammarRule(SimpleRuleStatementAst simple) => simple.Name.Value == "Grammar" && simple.Expression.ToString() == "Usings=«Using*» Statements=«Statement*»";

    private static string GetGrammarText() =>
        """
        precedence Primary, UnaryPrefix, UnaryPostfix, Named, Sequence;
        
        Grammar = Usings=Using* Statements=Statement*;
        
        QualifiedIdentifier = (Identifier; ".")+;
        
        Using =
            | Open  = "using" QualifiedIdentifier ";"
            | Alias = "using" Identifier "=" QualifiedIdentifier ";";
        
        Statement =
            | Precedence = "precedence" (Identifier; ",")+ ";"
            | Rule       = Identifier "=" ("|" Alternative)+ ";"
            | SimpleRule = Identifier "=" RuleExpression ";";
        
        Alternative =
            | Named = Identifier "=" RuleExpression
            | QualifiedIdentifier;
        
        RuleExpression =
            | CharLiteral
            | StringLiteral
            | Sequence      = Left=RuleExpression : Sequence Right=RuleExpression : Sequence
            | Named         = Name=Identifier "=" RuleExpression : Named
            | Optional      = RuleExpression : UnaryPostfix "?"
            | OftenMissed   = RuleExpression : UnaryPostfix "??"
            | OneOrMany     = RuleExpression : UnaryPostfix "+"
            | ZeroOrMany    = RuleExpression : UnaryPostfix "*"
            | AndPredicate  = "&" RuleExpression : UnaryPrefix
            | NotPredicate  = "!" RuleExpression : UnaryPrefix
            | RuleRef       = Ref=QualifiedIdentifier (":" Precedence=Identifier ("," Associativity)?)?
            | Group         = "(" RuleExpression ")"
            | SeparatedList = "(" RuleExpression ";" RuleExpression SeparatorModifier=(":" Modifier)? ")" Count;
        
        Associativity =
            | Left  = "left"
            | Right = "right";
        Modifier =
            | Optional = "?"
            | Required = "!";
        Count =
            | OneOrMeny  = "+"
            | ZeroOrMeny = "*";
        """;
}
