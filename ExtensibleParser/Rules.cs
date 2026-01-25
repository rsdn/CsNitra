namespace ExtensibleParaser;

/// <summary>
/// Base class for all grammar rules in the parser.
/// Rules define how the parser recognizes patterns in the input text.
/// </summary>
/// <param name="Kind">A descriptive name for this rule type, used for error reporting and AST construction</param>
public abstract record Rule(string Kind)
{
    public abstract override string ToString();

    /// <summary>
    /// Inlines references to other rules for optimization.
    /// This replaces references to simple rules with their actual definitions.
    /// </summary>
    /// <param name="inlineableRules">Dictionary of rules that can be inlined</param>
    /// <returns>A new rule with inlined references</returns>
    public virtual Rule InlineReferences(Dictionary<string, Rule> inlineableRules) => this;

    /// <summary>
    /// Recursively collects all sub-rules of a specific type.
    /// Useful for finding specific rule types within complex rule structures.
    /// </summary>
    /// <typeparam name="T">The type of sub-rules to find</typeparam>
    /// <returns>All sub-rules of type T within this rule</returns>
    public abstract IEnumerable<Rule> GetSubRules<T>() where T : Rule;
}

/// <summary>
/// Base class for terminal rules that match specific text patterns in the input.
/// Terminals are the "leaf" rules that directly consume characters from the input string.
/// Examples: keywords, numbers, identifiers, punctuation.
/// </summary>
/// <param name="Kind">A descriptive name for this terminal type</param>
public abstract record Terminal(string Kind) : Rule(Kind)
{
    public override Rule InlineReferences(Dictionary<string, Rule> inlineableRules) => this;

    /// <summary>
    /// Attempts to match this terminal starting at the specified position in the input string.
    /// Unlike regular expressions, this only tries to match from the start position and doesn't search ahead.
    /// </summary>
    /// <param name="input">The input string to match against</param>
    /// <param name="startPos">The starting position for matching</param>
    /// <returns>
    /// -1 if matching fails, 
    /// 0 or more if matching succeeds (0 for patterns that can match empty strings, like "a*"),
    /// representing the number of characters matched
    /// </returns>
    public abstract int TryMatch(string input, int startPos);

    public override IEnumerable<Rule> GetSubRules<T>()
    {
        if (this is T)
            yield return this;
    }
}

/// <summary>
/// A terminal that matches a specific literal string value.
/// Example: new Literal("if") matches the exact text "if".
/// </summary>
/// <param name="Value">The exact string to match</param>
/// <param name="Kind">Optional custom name for this literal</param>
public sealed record Literal(string Value, string? Kind = null) : Terminal(Kind ?? Value)
{
    public override Rule InlineReferences(Dictionary<string, Rule> inlineableRules) => this;

    public override int TryMatch(string input, int startPos) =>
        input.AsSpan(startPos).StartsWith(Value, StringComparison.Ordinal) ? Value.Length : -1;

    public override string ToString() => $"«{Value}»";

    public override IEnumerable<Rule> GetSubRules<T>()
    {
        if (this is T)
            yield return this;
    }
}

/// <summary>
/// A sequence of rules that must all match in order.
/// Example: new Seq([new Literal("if"), new Literal("("), ...], "IfStatement")
/// represents the pattern "if ( ... )".
/// </summary>
/// <param name="Elements">The rules that must match in sequence</param>
/// <param name="Kind">A descriptive name for this sequence</param>
public record Seq(Rule[] Elements, string Kind) : Rule(Kind)
{
    public override string ToString() => string.Join<Rule>(" ", Elements);

    public override Rule InlineReferences(Dictionary<string, Rule> inlineableRules) =>
        new Seq(Elements.Select(e => e.InlineReferences(inlineableRules)).ToArray(), Kind);

    public override IEnumerable<Rule> GetSubRules<T>()
    {
        if (this is T)
            yield return this;
        foreach (var element in Elements)
            foreach (var subRule in element.GetSubRules<T>())
                yield return subRule;
    }
}

/// <summary>
/// Matches one or more repetitions of a rule.
/// Similar to the "+" operator in regular expressions.
/// Example: new OneOrMany(Terminals.Ident()) matches one or more identifiers.
/// </summary>
/// <param name="Element">The rule to repeat</param>
/// <param name="Kind">Optional custom name for this repetition</param>
public record OneOrMany(Rule Element, string? Kind = null) : Rule(Kind ?? nameof(OneOrMany))
{
    public override string ToString() => $"{Element}+";

    public override Rule InlineReferences(Dictionary<string, Rule> inlineableRules) =>
        new OneOrMany(Element.InlineReferences(inlineableRules), Kind);

    public override IEnumerable<Rule> GetSubRules<T>()
    {
        if (this is T)
            yield return this;
        foreach (var subRule in Element.GetSubRules<T>())
            yield return subRule;
    }
}

/// <summary>
/// Matches zero or more repetitions of a rule.
/// Similar to the "*" operator in regular expressions.
/// Example: new ZeroOrMany(Terminals.Whitespace()) matches optional whitespace.
/// </summary>
/// <param name="Element">The rule to repeat</param>
/// <param name="Kind">Optional custom name for this repetition</param>
public record ZeroOrMany(Rule Element, string? Kind = null) : Rule(Kind ?? nameof(ZeroOrMany))
{
    public override string ToString() => $"{Element}*";

    public override Rule InlineReferences(Dictionary<string, Rule> inlineableRules) =>
        new ZeroOrMany(Element.InlineReferences(inlineableRules), Kind);

    public override IEnumerable<Rule> GetSubRules<T>()
    {
        if (this is T)
            yield return this;
        foreach (var subRule in Element.GetSubRules<T>())
            yield return subRule;
    }
}

/// <summary>
/// Matches an optional rule (zero or one occurrence).
/// Similar to the "?" operator in regular expressions.
/// Example: new Optional(new Literal(";")) matches an optional semicolon.
/// </summary>
/// <param name="Element">The optional rule</param>
/// <param name="Kind">Optional custom name</param>
public record Optional(Rule Element, string? Kind = null) : Rule(Kind ?? nameof(Optional))
{
    public override string ToString() => $"{Element}?";

    public override Rule InlineReferences(Dictionary<string, Rule> inlineableRules) =>
        new Optional(Element.InlineReferences(inlineableRules), Kind);

    public override IEnumerable<Rule> GetSubRules<T>()
    {
        if (this is T)
            yield return this;
        foreach (var subRule in Element.GetSubRules<T>())
            yield return subRule;
    }
}

/// <summary>
/// A special optional rule used for error recovery.
/// When parsing fails at a recovery position, this rule succeeds without consuming input.
/// Used to handle commonly omitted syntax elements during error recovery.
/// Example: OftenMissed(new Literal("}")) allows parsing to continue when a closing brace is missing.
/// </summary>
/// <param name="Element">The rule that is often missed</param>
/// <param name="Kind">Name for this rule, typically "Error" for error recovery</param>
public record OftenMissed(Rule Element, string Kind = "Error") : Rule(Kind)
{
    public override string ToString() => $"{Element}?";

    public override Rule InlineReferences(Dictionary<string, Rule> inlineableRules) =>
        new OftenMissed(Element.InlineReferences(inlineableRules));

    public override IEnumerable<Rule> GetSubRules<T>()
    {
        if (this is T)
            yield return this;
        foreach (var subRule in Element.GetSubRules<T>())
            yield return subRule;
    }
}

/// <summary>
/// A reference to another rule by name, enabling recursive grammar definitions.
/// Example: new Ref("Expression") refers to a rule named "Expression".
/// During parsing, the parser looks up and applies the referenced rule.
/// </summary>
/// <param name="RuleName">The name of the referenced rule</param>
/// <param name="Kind">Optional custom name for this reference</param>
public record Ref(string RuleName, string? Kind = null) : Rule(Kind ?? nameof(RuleName))
{
    public override string ToString() => RuleName;

    public override Rule InlineReferences(Dictionary<string, Rule> inlineableRules) =>
        inlineableRules.TryGetValue(RuleName, out var rule) ? rule.InlineReferences(inlineableRules) : this;

    public override IEnumerable<Rule> GetSubRules<T>()
    {
        if (this is T)
            yield return this;
    }
}

/// Key features of TDOPP (Top-Down Operator Precedence Parsing) in this parser:
/// 1. Operators are defined in a single rule with natural left-recursive syntax
/// 2. Precedence and associativity control binding strength
/// 3. No need for complex nested grammar rules
/// 
/// Example from MiniC using the grammar format you provided:
///   Expr = 
///     | Number
///     | Ident
///     | Expr "*" Expr : 200           // Multiplication with precedence 200
///     | Expr "+" Expr : 100           // Addition with precedence 100  
///     | "-" Expr : 300                // Unary minus with precedence 300
///     | Expr "=" Expr : 10, right     // Assignment with precedence 10, right-associative
/// 
/// This corresponds to the code representation:
///   Expr = 
///     | Terminals.Number()
///     | Terminals.Ident()
///     | new Seq([new Ref("Expr"), new Literal("*"), new ReqRef("Expr", 200)], "Mul")
///     | new Seq([new Ref("Expr"), new Literal("+"), new ReqRef("Expr", 100)], "Add")
///     | new Seq([new Literal("-"), new ReqRef("Expr", 300)], "Neg")
///     | new Seq([new Ref("Expr"), new Literal("="), new ReqRef("Expr", 10, Right: true)], "Assignment")
/// 
/// The parser automatically transforms this using TDOPP to handle operator precedence,
/// allowing expressions like "1 + 2 * 3" to be parsed correctly as "1 + (2 * 3)".
/// </summary>
/// <param name="RuleName">The name of the referenced expression rule</param>
/// <param name="Precedence">Precedence level - higher numbers bind tighter (e.g., multiplication: 200, addition: 100)</param>
/// <param name="Right">True for right-associative operators (like "="), false for left-associative (like "+")</param>
/// <param name="Kind">Optional custom name</param>
public record ReqRef(string RuleName, int Precedence = 0, bool Right = false, string? Kind = null) : Ref(RuleName, Kind ?? nameof(RuleName))
{
    public override string ToString() => $"{RuleName} {{{Precedence}, {(Right ? "left" : "right")}}}";

    public override Rule InlineReferences(Dictionary<string, Rule> inlineableRules) => this;

    public override IEnumerable<Rule> GetSubRules<T>()
    {
        if (this is T)
            yield return this;
    }
}

/// <summary>
/// A rule with precedence information used in TDOPP (Top-Down Operator Precedence Parsing).
/// Represents the postfix part of an operator rule, including the operator itself and its right operand.
/// Created automatically when the parser transforms left-recursive operator rules.
/// </summary>
/// <param name="Kind">The kind/name of this rule</param>
/// <param name="Seq">The sequence representing the operator and its right operand</param>
/// <param name="Precedence">The precedence level of this operator</param>
/// <param name="Right">True for right-associative operators</param>
public record RuleWithPrecedence(string Kind, Seq Seq, int Precedence, bool Right);

/// <summary>
/// A rule transformed for TDOPP (Top-Down Operator Precedence Parsing).
/// The parser automatically converts left-recursive operator rules into this format.
/// 
/// How it works:
/// 1. Original grammar: Expr = Expr "+" Expr | Expr "*" Expr | Number
/// 2. TDOPP transformation splits it into:
///    - Prefix: What can start an expression (Number, etc.)
///    - Postfix: Operators with their right operands and precedence
///    
/// This enables efficient parsing without exponential backtracking for expressions.
/// The parser uses precedence to decide which operator to apply first.
/// </summary>
/// <param name="Name">Reference to this rule</param>
/// <param name="Kind">The kind/name of this rule</param>
/// <param name="Prefix">Rules that can start an expression (operands, unary operators)</param>
/// <param name="Postfix">Operators with their right operands and precedence information</param>
/// <param name="RecoveryPrefix">Prefix rules used during error recovery</param>
/// <param name="RecoveryPostfix">Postfix rules used during error recovery</param>
public record TdoppRule(
    Ref Name,
    string Kind,
    Rule[] Prefix,
    RuleWithPrecedence[] Postfix,
    Rule[] RecoveryPrefix,
    RuleWithPrecedence[] RecoveryPostfix
) : Rule(Kind)
{
    public override string ToString() =>
        $"{Name} = Prefix: {string.Join<Rule>(" | ", Prefix)}" +
        $" Postfix: {string.Join("", Postfix.Select(x => $" {{{x.Precedence}{(x.Right ? "" : " right")}}} {x.Seq}"))}";

    public override IEnumerable<Rule> GetSubRules<T>()
    {
        if (this is T)
            yield return this;
        foreach (var prefix in Prefix)
            foreach (var subRule in prefix.GetSubRules<T>())
                yield return subRule;
        foreach (var postfix in Postfix)
            foreach (var subRule in postfix.Seq.GetSubRules<T>())
                yield return subRule;
        foreach (var recoveryPrefix in RecoveryPrefix)
            foreach (var subRule in recoveryPrefix.GetSubRules<T>())
                yield return subRule;
        foreach (var recoveryPostfix in RecoveryPostfix)
            foreach (var subRule in recoveryPostfix.Seq.GetSubRules<T>())
                yield return subRule;
    }
}

/// <summary>
/// A positive lookahead predicate that checks if a rule would match without consuming input.
/// Succeeds only if the predicate rule matches at the current position.
/// This rule itself does not consume input - the predicate is evaluated without advancing position.
/// The rule that follows the predicate in the grammar is responsible for consuming input.
/// Example: In a sequence like "&RuleA RuleB", the AndPredicate checks if RuleA matches,
/// then RuleB is parsed separately.
/// </summary>
/// <param name="PredicateRule">The rule to check without consuming input</param>
public record AndPredicate(Rule PredicateRule) : Rule("&")
{
    public override string ToString() => $"&{PredicateRule}";

    public override Rule InlineReferences(Dictionary<string, Rule> inlineableRules) =>
        new AndPredicate(PredicateRule.InlineReferences(inlineableRules));

    public override IEnumerable<Rule> GetSubRules<T>()
    {
        if (this is T)
            yield return this;
        foreach (var subRule in PredicateRule.GetSubRules<T>())
            yield return subRule;
    }
}

/// <summary>
/// A negative lookahead predicate that checks if a rule would NOT match without consuming input.
/// Succeeds only if the predicate rule fails to match at the current position.
/// This rule itself does not consume input - the predicate is evaluated without advancing position.
/// The rule that follows the predicate in the grammar is responsible for consuming input.
/// Example: In a sequence like "!RuleA RuleB", the NotPredicate checks that RuleA doesn't match,
/// then RuleB is parsed separately.
/// </summary>
/// <param name="PredicateRule">The rule that must NOT match</param>
public record NotPredicate(Rule PredicateRule) : Rule("!")
{
    public override string ToString() => $"!{PredicateRule}";

    public override Rule InlineReferences(Dictionary<string, Rule> inlineableRules) =>
        new NotPredicate(PredicateRule.InlineReferences(inlineableRules));

    public override IEnumerable<Rule> GetSubRules<T>()
    {
        if (this is T)
            yield return this;
        foreach (var subRule in PredicateRule.GetSubRules<T>())
            yield return subRule;
    }
}

/// <summary>
/// Matches a list of elements separated by a delimiter.
/// Useful for parsing comma-separated lists, parameter lists, etc.
/// Example: new SeparatedList(Terminals.Ident(), new Literal(","), "Parameters")
/// matches patterns like "a", "a, b", or "a, b, c".
/// </summary>
/// <param name="Element">The rule for list elements</param>
/// <param name="Separator">The rule for separators between elements</param>
/// <param name="Kind">Name for this list rule</param>
/// <param name="EndBehavior">Controls whether a trailing separator is allowed, required, or forbidden</param>
/// <param name="CanBeEmpty">Whether the list can be empty (no elements)</param>
public record SeparatedList(
    Rule Element,
    Rule Separator,
    string Kind,
    SeparatorEndBehavior EndBehavior = SeparatorEndBehavior.Optional,
    bool CanBeEmpty = true)
    : Rule(Kind)
{
    public override string ToString() => $"({Element}; {Separator} {EndBehavior})*{(CanBeEmpty ? "" : "+")}";

    public override Rule InlineReferences(Dictionary<string, Rule> inlineableRules)
    {
        var inlinedElement = Element.InlineReferences(inlineableRules);
        var inlinedSeparator = Separator.InlineReferences(inlineableRules);
        return new SeparatedList(inlinedElement, inlinedSeparator, Kind, EndBehavior, CanBeEmpty);
    }

    public override IEnumerable<Rule> GetSubRules<T>()
    {
        if (this is T)
            yield return this;
        foreach (var subRule in Element.GetSubRules<T>())
            yield return subRule;

        // Мб. не требуется.
        foreach (var subRule in Separator.GetSubRules<T>())
            yield return subRule;
    }
}

/// <summary>
/// Controls the behavior of trailing separators in SeparatedList rules.
/// </summary>
public enum SeparatorEndBehavior
{
    /// <summary>
    /// Optional - a trailing separator may or may not be present
    /// Example: "a, b, c" or "a, b, c,"
    /// </summary>
    Optional,

    /// <summary>
    /// Required - a trailing separator must be present
    /// Example: "a, b, c," (common in some programming languages)
    /// </summary>
    Required,

    /// <summary>
    /// Forbidden - a trailing separator must not be present
    /// Example: "a, b, c" (but not "a, b, c,")
    /// </summary>
    Forbidden
}

/// <summary>
/// Base class for terminal rules used in error recovery.
/// These terminals match even when the input doesn't fully satisfy normal parsing rules,
/// allowing the parser to continue after syntax errors.
/// </summary>
/// <param name="Kind">Name for this recovery terminal</param>
public abstract record RecoveryTerminal(string Kind) : Terminal(Kind);

/// <summary>
/// A terminal that always matches successfully without consuming any input.
/// Used in error recovery to insert missing syntax elements.
/// Example: When a closing brace is missing, EmptyTerminal can match it
/// to allow the parser to continue.
/// </summary>
/// <param name="Kind">Name for this empty terminal</param>
public record EmptyTerminal(string Kind) : RecoveryTerminal(Kind)
{
    public override int TryMatch(string input, int position) => 0;

    public override string ToString() => Kind;
}
