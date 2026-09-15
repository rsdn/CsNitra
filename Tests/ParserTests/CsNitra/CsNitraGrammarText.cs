namespace CsNitra;

public static class CsNitraGrammarText
{
    public static string GetGrammarText() =>
        """
        Grammar = Usings=Using* Statements=Statement*;

        QualifiedIdentifier = (Identifier; ".")+;

        Using =
            | OpenUsing  = "using" QualifiedIdentifier ";"
            | AliasUsing = "using" Identifier "=" QualifiedIdentifier ";";

        Statement =
            | Precedence = "precedence" Precedences=(Identifier; ",")+ ";"
            | Rule       = Identifier "=" Alternatives=Alternative+ ";"
            | SimpleRule = Identifier "=" RuleExpression ";";

        Alternative =
            | NamedAlternative = "|" Identifier "=" RuleExpression
            | AnonymousAlternative = "|" QualifiedIdentifier;

        precedence Primary, Postfix, Predicate, Naming, Optional, Sequence;

        RuleExpression =
            // prefix rules (Primary)
            | Literal
            | RuleRef       = Ref=QualifiedIdentifier PrecedenceWithAssociativity=(":" Precedence=Identifier Associativity=("," Associativity)?)?
            | Group         = "(" RuleExpression ")"
            | SeparatedList = "(" Element=RuleExpression ";" Separator=RuleExpression SeparatorModifier=(":" Modifier)? ")" Count

            // postfix rules (operators)
            | OftenMissed   = RuleExpression : Postfix "??"
            | OneOrMany     = RuleExpression : Postfix "+"
            | ZeroOrMany    = RuleExpression : Postfix "*"
            | AndPredicate  = "&" RuleExpression : Predicate
            | NotPredicate  = "!" RuleExpression : Predicate
            | Named         = Name=Identifier "=" RuleExpression : Naming
            | Optional      = RuleExpression : Optional "?"
            | Sequence      = Left=RuleExpression : Sequence Right=RuleExpression : Sequence;

        Associativity                 =
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
