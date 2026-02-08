using CsNitra.Ast;

namespace CsNitra;

[TestClass]
public class NamingTests
{
    [TestMethod]
    public void Plural_ShouldConvertToPlural()
    {
        // Simple cases
        Assert.AreEqual("items", Naming.Plural("item"));
        Assert.AreEqual("boxes", Naming.Plural("box"));
        Assert.AreEqual("buses", Naming.Plural("bus"));

        // Words ending with -y
        Assert.AreEqual("cities", Naming.Plural("city"));
        Assert.AreEqual("countries", Naming.Plural("country"));

        // Exceptions for -y
        Assert.AreEqual("days", Naming.Plural("day"));
        Assert.AreEqual("boys", Naming.Plural("boy"));

        // Words ending with -f, -fe
        Assert.AreEqual("leaves", Naming.Plural("leaf"));
        Assert.AreEqual("wives", Naming.Plural("wife"));

        // Words ending with -s, -x, -z, -ch, -sh
        Assert.AreEqual("classes", Naming.Plural("class"));
        Assert.AreEqual("foxes", Naming.Plural("fox"));
        Assert.AreEqual("quizzes", Naming.Plural("quiz"));
        Assert.AreEqual("churches", Naming.Plural("church"));
        Assert.AreEqual("dishes", Naming.Plural("dish"));

        // Test words ending with -z (should double the z)
        Assert.AreEqual("quizzes", Naming.Plural("quiz"));
        Assert.AreEqual("fizzes", Naming.Plural("fizz"));
        Assert.AreEqual("buzzes", Naming.Plural("buzz"));
    }

    [TestMethod]
    public void IsValidIdentifier_ShouldValidateCorrectly()
    {
        // Valid identifiers
        Assert.IsTrue(Naming.IsValidIdentifier("item"));
        Assert.IsTrue(Naming.IsValidIdentifier("item123"));
        Assert.IsTrue(Naming.IsValidIdentifier("_item"));
        Assert.IsTrue(Naming.IsValidIdentifier("Item"));
        Assert.IsTrue(Naming.IsValidIdentifier("item_name"));
        Assert.IsTrue(Naming.IsValidIdentifier("ItemName"));

        // Invalid identifiers
        Assert.IsFalse(Naming.IsValidIdentifier("123item"));
        Assert.IsFalse(Naming.IsValidIdentifier("item-name"));
        Assert.IsFalse(Naming.IsValidIdentifier("item name"));
        Assert.IsFalse(Naming.IsValidIdentifier("item.name"));
        Assert.IsFalse(Naming.IsValidIdentifier(""));
        Assert.IsFalse(Naming.IsValidIdentifier("+"));
    }

    [TestMethod]
    public void InferName_ShouldReturnNameForIdentifier()
    {
        // Arrange
        var identifier = new Identifier("item", 0, 4);

        // Act
        var name = Naming.InferName(identifier);

        // Assert
        Assert.AreEqual("item", name);
    }

    [TestMethod]
    public void InferName_ShouldUseAliasForLiteral()
    {
        // Arrange
        var literal = new Literal("+", 0, 1);

        // Act
        var name = Naming.InferName(literal);

        // Assert
        Assert.AreEqual("Plus", name);
    }

    [TestMethod]
    public void InferName_ShouldCapitalizeKeywordLiteral()
    {
        // Arrange
        var literal = new Literal("if", 0, 2);

        // Act
        var name = Naming.InferName(literal);

        // Assert
        Assert.AreEqual("If", name);
    }

    [TestMethod]
    public void InferName_ShouldReturnNullForInvalidLiteral()
    {
        // Arrange
        var literal = new Literal("123", 0, 3);

        // Act
        var name = Naming.InferName(literal);

        // Assert
        Assert.IsNull(name);
    }

    [TestMethod]
    public void InferName_ShouldUseAliasForLiteralAst()
    {
        // Arrange
        var literalAst = new LiteralAst("+", 0, 1);

        // Act
        var name = Naming.InferName(literalAst);

        // Assert
        Assert.AreEqual("Plus", name);
    }

    [TestMethod]
    public void InferName_ShouldCapitalizeFirstLetterForLiteralAst()
    {
        // Arrange
        var literalAst = new LiteralAst("item", 0, 4);

        // Act
        var name = Naming.InferName(literalAst);

        // Assert
        Assert.AreEqual("Item", name);
    }

    [TestMethod]
    public void InferName_ShouldReturnNameForRuleRefExpression()
    {
        // Arrange
        var identifier = new Identifier("Expression", 0, 10);
        var qualifiedIdentifier = new QualifiedIdentifierAst(
            new[] { identifier },
            [],
            0, 10);
        var ruleRef = new RuleRefExpressionAst(qualifiedIdentifier, null, 0, 10);

        // Act
        var name = Naming.InferName(ruleRef);

        // Assert
        Assert.AreEqual("Expression", name);
    }

    [TestMethod]
    public void InferName_ShouldReturnPluralNameForOneOrManyExpression()
    {
        // Arrange
        var element = new LiteralAst("item", 0, 4);
        var plus = new Literal("+", 4, 5);
        var oneOrMany = new OneOrManyExpressionAst(element, plus, 0, 5);

        // Act
        var name = Naming.InferName(oneOrMany);

        // Assert
        Assert.AreEqual("Items", name);
    }

    [TestMethod]
    public void InferName_ShouldReturnPluralNameForZeroOrManyExpression()
    {
        // Arrange
        var element = new LiteralAst("box", 0, 3);
        var star = new Literal("*", 3, 4);
        var zeroOrMany = new ZeroOrManyExpressionAst(element, star, 0, 4);

        // Act
        var name = Naming.InferName(zeroOrMany);

        // Assert
        Assert.AreEqual("Boxes", name);
    }

    [TestMethod]
    public void InferName_ShouldReturnNameForSeparatedListExpression()
    {
        // Arrange
        var element = new LiteralAst("parameter", 0, 9);
        var separator = new LiteralAst(",", 9, 10);
        var count = new Literal("+", 14, 15);
        var separatedList = new SeparatedListExpressionAst(
            element, separator, null, count, 0, 15);

        // Act
        var name = Naming.InferName(separatedList);

        // Assert
        Assert.AreEqual("Parameters", name);
    }

    [TestMethod]
    public void InferName_ShouldReturnExplicitNameForNamedExpression()
    {
        // Arrange
        var nameId = new Identifier("Left", 0, 4);
        var eq = new Literal("=", 4, 5);
        var expression = new LiteralAst("item", 5, 9);
        var named = new NamedExpressionAst(nameId, eq, expression, 0, 9);

        // Act
        var name = Naming.InferName(named);

        // Assert
        Assert.AreEqual("Left", name);
    }

    [TestMethod]
    public void InferName_ShouldRecurseIntoGroupExpression()
    {
        // Arrange
        var open = new Literal("(", 0, 1);
        var expression = new LiteralAst("item", 1, 5);
        var close = new Literal(")", 5, 6);
        var group = new GroupExpressionAst(open, expression, close, 0, 6);

        // Act
        var name = Naming.InferName(group);

        // Assert
        Assert.AreEqual("Item", name);
    }

    [TestMethod]
    public void InferName_ShouldReturnNullForSequenceExpression()
    {
        // Arrange
        var left = new LiteralAst("a", 0, 1);
        var right = new LiteralAst("b", 2, 3);
        var sequence = new SequenceExpressionAst(left, right, 0, 3);

        // Act
        var name = Naming.InferName(sequence);

        // Assert
        Assert.IsNull(name);
    }

    [TestMethod]
    public void InferName_ShouldReturnNameForAnonymousAlternative()
    {
        // Arrange
        var identifier = new Identifier("Expression", 0, 10);
        var qualifiedIdentifier = new QualifiedIdentifierAst(
            new[] { identifier },
            [],
            0, 10);
        var pipe = new Literal("|", 10, 11);
        var anon = new AnonymousAlternativeAst(pipe, qualifiedIdentifier, 10, 20);

        // Act
        var name = Naming.InferName(anon);

        // Assert
        Assert.AreEqual("Expression", name);
    }

    [TestMethod]
    public void CollectAllSubruleNames_ShouldCollectNamesRecursively()
    {
        // Arrange
        // Create expression: Named = Left:Expression Right:Expression+
        var leftName = new Identifier("Left", 0, 4);
        var eq1 = new Literal("=", 4, 5);
        var leftExpr = new LiteralAst("Expression", 5, 15);
        var leftNamed = new NamedExpressionAst(leftName, eq1, leftExpr, 0, 15);

        var rightName = new Identifier("Right", 15, 20);
        var eq2 = new Literal("=", 20, 21);
        var rightElement = new LiteralAst("Expression", 21, 31);
        var plus = new Literal("+", 31, 32);
        var rightOneOrMany = new OneOrManyExpressionAst(rightElement, plus, 21, 32);
        var rightNamed = new NamedExpressionAst(rightName, eq2, rightOneOrMany, 15, 32);

        var sequence = new SequenceExpressionAst(leftNamed, rightNamed, 0, 32);

        // Act
        var names = Naming.CollectAllSubruleNames(sequence, name: "sequence");

        // Debug output to see what we got
        Console.WriteLine("Collected names:");
        foreach (var kvp in names)
        {
            var exprType = kvp.Key.GetType().Name;
            var exprStr = kvp.Key.ToString();
            var nameStr = kvp.Value ?? "null";
            Console.WriteLine($"  {exprType}: '{exprStr}' -> '{nameStr}'");
        }

        // Assert
        // Expected nodes:
        // 1. sequence (null)
        // 2. leftNamed (Left)
        // 3. leftExpr (Expression)
        // 4. rightNamed (Right)
        // 5. rightOneOrMany (Expressions)
        // 6. rightElement (Expression)
        // Total: 6 nodes

        Assert.AreEqual(6, names.Count,
            $"Expected 6 nodes but got {names.Count}. Nodes: {string.Join(", ", names.Keys.Select(k => k.GetType().Name))}");

        // Check specific names
        Assert.AreEqual("sequence", names[sequence], "Sequence should have 'sequence' name");
        Assert.AreEqual("Left", names[leftNamed], "LeftNamed should be 'Left'");
        Assert.AreEqual("Expression", names[leftExpr], "LeftExpr should be 'Expression'");
        Assert.AreEqual("Right", names[rightNamed], "RightNamed should be 'Right'");
        Assert.AreEqual("Expressions", names[rightOneOrMany], "RightOneOrMany should be 'Expressions'");
        Assert.AreEqual("Expression", names[rightElement], "RightElement should be 'Expression'");
    }

    [TestMethod]
    public void TryGetName_ShouldReturnNameAndEmptyError_WhenNameExists()
    {
        // Arrange
        var expression = new LiteralAst("item", 0, 4);

        // Act
        var (name, errorIfNull) = Naming.TryGetName(expression, "test expression");

        // Assert
        Assert.AreEqual("Item", name);
        Assert.AreEqual(string.Empty, errorIfNull);
    }

    [TestMethod]
    public void TryGetName_ShouldReturnNullAndError_WhenNameCannotBeInferred()
    {
        // Arrange
        var expression = new LiteralAst("+++", 0, 3); // No alias for this

        // Act
        var (Name, ErrorIfNull) = Naming.TryGetName(expression, "operator");

        // Assert
        Assert.IsNull(Name);
        Assert.AreEqual("""
            Cannot infer name for operator: "+++"
            """, ErrorIfNull);
    }

    [TestMethod]
    public void InferName_ShouldHandleComplexOperatorsWithAliases()
    {
        // Test various operators with aliases
        testLiteralAlias("+=", "PlusEquals");
        testLiteralAlias("&&", "AndAnd");
        testLiteralAlias("??", "NullCoalescing");
        testLiteralAlias("?.", "NullConditional");
        testLiteralAlias("..", "Range");
        testLiteralAlias("=>", "Arrow");
        return;

        static void testLiteralAlias(string literalValue, string expectedAlias)
        {
            // Arrange
            var literal = new Literal(literalValue, 0, literalValue.Length);

            // Act
            var name = Naming.InferName(literal);

            // Assert
            Assert.AreEqual(expectedAlias, name);
        }
    }

    [TestMethod]
    public void InferName_ShouldPreserveCaseForAlreadyCapitalizedIdentifiers()
    {
        // Arrange
        var literal = new Literal("HTML", 0, 4);

        // Act
        var name = Naming.InferName(literal);

        // Assert
        Assert.AreEqual("HTML", name);
    }

    [TestMethod]
    public void InferName_ShouldHandleAlreadyCapitalizedLiteralAst()
    {
        // Arrange
        var literalAst = new LiteralAst("HTML", 0, 4);

        // Act
        var name = Naming.InferName(literalAst);

        // Assert
        Assert.AreEqual("HTML", name);
    }
}
