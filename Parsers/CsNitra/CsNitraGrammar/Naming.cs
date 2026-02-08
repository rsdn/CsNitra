using CsNitra.Ast;
using Literal = CsNitra.Ast.Literal;

namespace CsNitra;

public static class Naming
{
    /// <summary>
    /// Checks if string is a valid C# identifier.
    /// </summary>
    public static bool IsValidIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        // First character must be underscore or letter (Unicode category)
        var firstChar = name[0];
        if (!(firstChar == '_' || char.IsLetter(firstChar)))
            return false;

        // Subsequent characters must be underscore, letter, or digit
        for (int i = 1; i < name.Length; i++)
        {
            var c = name[i];
            if (c != '_' && !char.IsLetterOrDigit(c))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Default implementation for getting alias for literal strings.
    /// Users can provide their own implementation via Func<string, string?> delegate.
    /// </summary>
    public static string? DefaultGetLiteralAlias(string literal) => literal switch
    {
        // Punctuation
        "(" => "OpenParen",
        ")" => "CloseParen",
        "{" => "OpenBrace",
        "}" => "CloseBrace",
        "[" => "OpenBracket",
        "]" => "CloseBracket",
        "<" => "LessThan",
        ">" => "GreaterThan",

        // Operators and symbols
        "+" => "Plus",
        "-" => "Minus",
        "*" => "Star",
        "/" => "Slash",
        "=" => "Equals",
        "==" => "DoubleEquals",
        "!=" => "NotEquals",
        "<=" => "LessThanOrEqual",
        ">=" => "GreaterThanOrEqual",
        "!" => "Not",
        "&" => "And",
        "|" => "Or",
        "^" => "Xor",
        "~" => "Tilde",
        "?" => "Question",
        ":" => "Colon",
        ";" => "Semicolon",
        "," => "Comma",
        "." => "Dot",
        "=>" => "Arrow",
        "->" => "RightArrow",
        "<-" => "LeftArrow",
        "++" => "Increment",
        "--" => "Decrement",
        "+=" => "PlusEquals",
        "-=" => "MinusEquals",
        "*=" => "StarEquals",
        "/=" => "SlashEquals",
        "%=" => "PercentEquals",
        "&=" => "AndEquals",
        "|=" => "OrEquals",
        "^=" => "XorEquals",
        "<<" => "LeftShift",
        ">>" => "RightShift",
        "<<=" => "LeftShiftEquals",
        ">>=" => "RightShiftEquals",
        "&&" => "AndAnd",
        "||" => "OrOr",
        "??" => "NullCoalescing",
        "?." => "NullConditional",
        "?[" => "NullConditionalIndex",
        "??=" => "NullCoalescingAssignment",

        // Common punctuation combinations
        "::" => "DoubleColon",
        "..." => "Ellipsis",
        ".." => "Range",

        // String and char markers
        "\"" => "Quote",
        "'" => "Apostrophe",
        "`" => "Backtick",
        "@\"" => "VerbatimString",
        "$\"" => "InterpolatedString",
        "@$\"" => "VerbatimInterpolatedString",

        _ => null
    };

    /// <summary>
    /// Attempts to infer name for subrule.
    /// Returns null if name cannot be inferred.
    /// </summary>
    public static string? InferName(CsNitraAst astNode, Func<string, string?>? tryGetLiteralAlias = null)
    {
        tryGetLiteralAlias ??= DefaultGetLiteralAlias; // Use default implementation if no custom delegate provided
        return astNode switch
        {
            Identifier identifier => identifier.Value,
            Literal literal => InferNameFromLiteralString(literal.Value, tryGetLiteralAlias),
            LiteralAst literalAst => InferNameFromLiteralString(literalAst.Value, tryGetLiteralAlias),
            RuleRefExpressionAst ruleRef => ruleRef.Ref.ToString(),
            OneOrManyExpressionAst oneOrMany => InferNameForLoop(oneOrMany.Element, plural: true, tryGetLiteralAlias),
            ZeroOrManyExpressionAst zeroOrMany => InferNameForLoop(zeroOrMany.Element, plural: true, tryGetLiteralAlias),
            SeparatedListExpressionAst separatedList => InferNameForLoop(separatedList.Element, plural: true, tryGetLiteralAlias),
            NamedExpressionAst named => named.Name.Value,
            GroupExpressionAst group => InferName(group.Expression, tryGetLiteralAlias),
            OftenMissedExpressionAst oftenMissed => InferName(oftenMissed.Expression, tryGetLiteralAlias),
            AndPredicateExpressionAst andPredicate => InferName(andPredicate.Expression, tryGetLiteralAlias),
            NotPredicateExpressionAst notPredicate => InferName(notPredicate.Expression, tryGetLiteralAlias),
            SequenceExpressionAst _ => null,
            AnonymousAlternativeAst anon => anon.RuleRef.ToString(),
            _ => null
        };
    }

    /// <summary>
    /// Infers name for loop expressions (pluralizes if needed).
    /// </summary>
    public static string? InferNameForLoop(RuleExpressionAst element, bool plural, Func<string, string?> tryGetLiteralAlias)
    {
        var elementName = InferName(element, tryGetLiteralAlias);

        if (elementName == null)
            return null;

        if (plural && !elementName.EndsWith("s", StringComparison.OrdinalIgnoreCase))
            elementName = Plural(elementName);

        return elementName;
    }

    /// <summary>
    /// Infers name from literal string value.
    /// Uses provided delegate for alias resolution.
    /// </summary>
    private static string? InferNameFromLiteralString(string literalValue, Func<string, string?> tryGetLiteralAlias)
    {
        // First check custom alias resolver
        var alias = tryGetLiteralAlias(literalValue);
        if (alias != null)
            return alias;

        // Check if content is a valid identifier
        if (IsValidIdentifier(literalValue))
        {
            // Capitalize first letter for C# naming convention
            if (literalValue.Length > 0 && char.IsLower(literalValue[0]))
                return char.ToUpperInvariant(literalValue[0]) + literalValue[1..];
            return literalValue;
        }

        return null;
    }

    /// <summary>
    /// Converts singular noun to plural form (simple English rules).
    /// </summary>
    public static string Plural(string singular)
    {
        if (string.IsNullOrEmpty(singular))
            return singular;

        // Special handling for English irregular plurals
        return singular.ToLowerInvariant() switch
        {
            // Irregular plurals that change completely
            "child" => "children",
            "person" => "people",
            "man" => "men",
            "woman" => "women",
            "foot" => "feet",
            "tooth" => "teeth",
            "goose" => "geese",
            "mouse" => "mice",
            "louse" => "lice",

            // Irregular plurals with Latin/Greek origins
            "analysis" => "analyses",
            "axis" => "axes",
            "basis" => "bases",
            "crisis" => "crises",
            "thesis" => "theses",
            "criterion" => "criteria",
            "phenomenon" => "phenomena",
            "datum" => "data",
            "medium" => "media",
            "curriculum" => "curricula",

            // Words that stay the same in plural
            "sheep" => "sheep",
            "fish" => "fish",
            "deer" => "deer",
            "species" => "species",
            "series" => "series",
            "aircraft" => "aircraft",

            _ => applyRegularPluralRules(singular)
        };

        // Local function for applying regular pluralization rules
        static string applyRegularPluralRules(string s)
        {
            // Words ending in -y (but not -ay, -ey, -oy, -uy)
            if (s.EndsWith("y", StringComparison.OrdinalIgnoreCase)
            && !s.EndsWith("ay", StringComparison.OrdinalIgnoreCase)
            && !s.EndsWith("ey", StringComparison.OrdinalIgnoreCase)
            && !s.EndsWith("oy", StringComparison.OrdinalIgnoreCase)
            && !s.EndsWith("uy", StringComparison.OrdinalIgnoreCase))
                return s[..^1] + "ies";

            // Words ending in -s, -x, -z, -ch, -sh
            if (s.EndsWith("s", StringComparison.OrdinalIgnoreCase)
            || s.EndsWith("x", StringComparison.OrdinalIgnoreCase)
            || s.EndsWith("z", StringComparison.OrdinalIgnoreCase)
            || s.EndsWith("ch", StringComparison.OrdinalIgnoreCase)
            || s.EndsWith("sh", StringComparison.OrdinalIgnoreCase))
                // Special case: words ending in single -z need to double the z
                return s.EndsWith("z", StringComparison.OrdinalIgnoreCase) && !s.EndsWith("zz", StringComparison.OrdinalIgnoreCase)
                    ? s + "zes"
                    : s + "es";

            // Words ending in -f (but not -ff)
            if (s.EndsWith("f", StringComparison.OrdinalIgnoreCase)
            && !s.EndsWith("ff", StringComparison.OrdinalIgnoreCase))
                return s[..^1] + "ves";

            // Words ending in -fe
            if (s.EndsWith("fe", StringComparison.OrdinalIgnoreCase))
                return s[..^2] + "ves";

            // Words ending in -o (typically add -es)
            if (s.EndsWith("o", StringComparison.OrdinalIgnoreCase)
            && !s.EndsWith("oo", StringComparison.OrdinalIgnoreCase))
            {
                // Common exceptions that just add -s
                return s.ToLowerInvariant() switch
                {
                    "photo" => s + "s",
                    "piano" => s + "s",
                    "halo" => s + "s",
                    "echo" => s + "es",
                    "hero" => s + "es",
                    "potato" => s + "es",
                    "tomato" => s + "es",
                    _ => s + "es"
                };
            }

            // Default: add 's'
            return s + "s";
        }
    }

    /// <summary>
    /// Attempts to get name for subrule with error message.
    /// </summary>
    public static (string? Name, string ErrorIfNull) TryGetName(
        RuleExpressionAst expression,
        string context,
        Func<string, string?>? tryGetLiteralAlias = null) => InferName(expression, tryGetLiteralAlias) switch
        {
            null => (null, $"Cannot infer name for {context}: {expression}"),
            var name => (name, "")
        };

    /// <summary>
    /// Collects names of all subrules in expression.
    /// </summary>
    public static Dictionary<RuleExpressionAst, string?> CollectAllSubruleNames(
        RuleExpressionAst expression,
        string? name,
        Func<string, string?>? tryGetLiteralAlias = null)
    {
        var result = new Dictionary<RuleExpressionAst, string?>();
        if (expression is SequenceExpressionAst)
            result[expression] = name;
        CollectNamesRecursive(expression, name, result, tryGetLiteralAlias);
        return result;
    }

    private static void CollectNamesRecursive(
        RuleExpressionAst expression,
        string? name,
        Dictionary<RuleExpressionAst, string?> result,
        Func<string, string?>? tryGetLiteralAlias)
    {
        //if (expression is OptionalExpressionAst optional)
        //    expression = optional.Expression;

        if (result.ContainsKey(expression))
            return;

        // Get name for current expression
        name ??= InferName(expression, tryGetLiteralAlias);

        if (expression is NamedExpressionAst named)
            expression = named.Expression;

        result[expression] = name;

        // Recursively process child expressions
        switch (expression)
        {
            case SequenceExpressionAst seq:
                foreach (var expr in seq.FlattenSequence())
                    CollectNamesRecursive(expr, name: null, result, tryGetLiteralAlias);
                break;
            case OptionalExpressionAst optional:
                CollectNamesRecursive(optional.Expression, name: null, result, tryGetLiteralAlias);
                break;
            case OftenMissedExpressionAst oftenMissed:
                CollectNamesRecursive(oftenMissed.Expression, name: null, result, tryGetLiteralAlias);
                break;

            case OneOrManyExpressionAst oneOrMany:
                CollectNamesRecursive(oneOrMany.Element, name: null, result, tryGetLiteralAlias);
                break;

            case ZeroOrManyExpressionAst zeroOrMany:
                CollectNamesRecursive(zeroOrMany.Element, name: null, result, tryGetLiteralAlias);
                break;

            case AndPredicateExpressionAst andPredicate:
                CollectNamesRecursive(andPredicate.Expression, name, result, tryGetLiteralAlias);
                break;

            case NotPredicateExpressionAst notPredicate:
                CollectNamesRecursive(notPredicate.Expression, name, result, tryGetLiteralAlias);
                break;

            case RuleRefExpressionAst ruleRef:
                // Don't recurse into RuleRef - it's a reference to another rule
                break;

            case GroupExpressionAst group:
                CollectNamesRecursive(group.Expression, name, result, tryGetLiteralAlias);
                break;

            case SeparatedListExpressionAst separatedList:
                CollectNamesRecursive(separatedList.Element, name, result, tryGetLiteralAlias);
                CollectNamesRecursive(separatedList.Separator, name, result, tryGetLiteralAlias);
                break;

            // Terminal nodes don't have child expressions
            case LiteralAst _:
                break;

            default:
                throw new InvalidOperationException(
                    $"Unsupported expression type: {expression.GetType().Name}");
        }
    }
}
