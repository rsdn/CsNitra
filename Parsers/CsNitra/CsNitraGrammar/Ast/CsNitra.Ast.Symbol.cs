using CsNitra.TypeChecking;

namespace CsNitra.Ast;

// AST extensions for storing symbol references
public partial record RuleStatementAst
{
    public RuleSymbol? Symbol { get; set; }
}

public partial record SimpleRuleStatementAst
{
    public RuleSymbol? Symbol { get; set; }
}

public partial record AnonymousAlternativeAst
{
    public Symbol? ReferencedSymbol { get; set; }
}

public partial record RuleRefExpressionAst
{
    public Symbol? ReferencedSymbol { get; set; }
    public PrecedenceSymbol? PrecedenceSymbol { get; set; }
}

public partial record PrecedenceAst
{
    public PrecedenceSymbol? PrecedenceSymbol { get; set; }
}
