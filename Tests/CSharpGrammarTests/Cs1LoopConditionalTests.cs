using ExtensibleParser;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// Циклы и условные операторы C# 1.0 (T2.2.2): if / while / do-while / for / foreach. Парсинг — по
// правилу Block (start rule "Block"; см. Cs1StatementTestHelper). Roslyn-отбор — в
// Cs1RoslynStatementTests. Ключевое: dangling else — else связывается с НАИБЛИЖАЙШИМ (внутренним) if
// (см. shape-тесты ниже и docs/CSharpParserPlan-progressT2.2.2.md).
[TestClass]
public class Cs1LoopConditionalTests
{
    // === if ===

    [TestMethod]
    public void If_ThenBlock_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ if (a) { x = 5; } }");
    }

    [TestMethod]
    public void If_ThenExpression_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ if (a) x = 5; }");
    }

    [TestMethod]
    public void If_ElseBlock_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ if (a) { x = 5; } else { y = 3; } }");
    }

    [TestMethod]
    public void If_ElseExpression_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ if (a) x = 5; else y = 3; }");
    }

    [TestMethod]
    public void If_ElseIf_Else_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ if (a) x = 5; else if (b) y = 3; else z = 7; }");
    }

    [TestMethod]
    public void If_NestedThen_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ if (a) if (b) x = 5; }");
    }

    // === while ===

    [TestMethod]
    public void While_Block_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ while (x) { i++; } }");
    }

    [TestMethod]
    public void While_Expression_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ while (x) i++; }");
    }

    [TestMethod]
    public void While_Nested_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ while (x) while (y) i++; }");
    }

    // === do-while ===

    [TestMethod]
    public void DoWhile_Block_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ do { i++; } while (x); }");
    }

    [TestMethod]
    public void DoWhile_Expression_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ do i++; while (x); }");
    }

    [TestMethod]
    public void DoWhile_Nested_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ do { do i++; while (x); } while (y); }");
    }

    // === for ===

    [TestMethod]
    public void For_EmptyParts_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ for (; ;) x = 5; }");
    }

    [TestMethod]
    public void For_DeclarationInit_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ for (int i = 0; i < n; i++) x = 5; }");
    }

    [TestMethod]
    public void For_ExpressionInit_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ for (i = 0; i < n; i++) x = 5; }");
    }

    [TestMethod]
    public void For_NoCondition_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ for (i = 0; ; ) x = 5; }");
    }

    [TestMethod]
    public void For_NoUpdate_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ for (int i = 0; i < n; ) x = 5; }");
    }

    [TestMethod]
    public void For_Block_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ for (int i = 0; i < n; i++) { x = 5; } }");
    }

    [TestMethod]
    public void For_Nested_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ for (int i = 0; i < n; i++) for (int j = 0; j < m; j++) x = 5; }");
    }

    // === foreach ===

    [TestMethod]
    public void Foreach_PredefinedType_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ foreach (int x in arr) y = x; }");
    }

    [TestMethod]
    public void Foreach_IdentifierType_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ foreach (X x in arr) y = x; }");
    }

    [TestMethod]
    public void Foreach_StringType_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ foreach (string s in list) y = s; }");
    }

    [TestMethod]
    public void Foreach_Block_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ foreach (int x in arr) { y = x; } }");
    }

    [TestMethod]
    public void Foreach_IndexerExpression_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ foreach (int x in arr[i]) y = x; }");
    }

    // === Dangling else: else связывается с НАИБЛИЖАЙШИМ (внутренним) if ===
    // Механизм: `("else" Statement)?` — Optional (жадный, Parser.cs ParseOptional 543). then-Statement
    // внутреннего if парсится ПЕРЕД проверкой else внешнего if, поэтому внутренний if поглощает else.

    [TestMethod]
    public void DanglingElse_Simple_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ if (a) c; else d; }");
    }

    [TestMethod]
    public void DanglingElse_BindsToInnerIf_Shape()
    {
        // if (a) if (b) c; else d; → else binds to the INNER if (not the outer).
        var outer = (SeqNode)Cs1StatementTestHelper.FirstStatementNode("{ if (a) if (b) c; else d; }");
        Assert.AreEqual("IfStatement", outer.Kind);
        Assert.AreEqual("none", Cs1StatementTestHelper.IfElsePresent(outer), "outer if must NOT own the else");
        Assert.AreEqual("IfStatement", Cs1StatementTestHelper.IfThenKind(outer), "outer then-statement is the inner if");
        var inner = (SeqNode)outer.Elements[4];
        Assert.AreEqual("some", Cs1StatementTestHelper.IfElsePresent(inner), "inner if must own the else");
    }

    [TestMethod]
    public void DanglingElse_BlockBodyBindsToOuterIf_Shape()
    {
        // if (a) { if (b) c; } else d; → else binds to the OUTER if (inner if is inside a block).
        var outer = (SeqNode)Cs1StatementTestHelper.FirstStatementNode("{ if (a) { if (b) c; } else d; }");
        Assert.AreEqual("IfStatement", outer.Kind);
        Assert.AreEqual("some", Cs1StatementTestHelper.IfElsePresent(outer), "outer if must own the else");
        Assert.AreEqual("Block", Cs1StatementTestHelper.IfThenKind(outer), "outer then-statement is a block");
    }

    [TestMethod]
    public void DanglingElse_ThreeNestedBindsToInnermost_Shape()
    {
        // if (a) if (b) if (c) d; else e; → else binds to the INNERMOST if.
        var a = (SeqNode)Cs1StatementTestHelper.FirstStatementNode("{ if (a) if (b) if (c) d; else e; }");
        Assert.AreEqual("IfStatement", a.Kind);
        Assert.AreEqual("none", Cs1StatementTestHelper.IfElsePresent(a));
        var b = (SeqNode)a.Elements[4];
        Assert.AreEqual("IfStatement", b.Kind);
        Assert.AreEqual("none", Cs1StatementTestHelper.IfElsePresent(b));
        var c = (SeqNode)b.Elements[4];
        Assert.AreEqual("IfStatement", c.Kind);
        Assert.AreEqual("some", Cs1StatementTestHelper.IfElsePresent(c), "innermost if must own the else");
        Assert.AreEqual("ExpressionStatement", Cs1StatementTestHelper.IfThenKind(c));
    }

    [TestMethod]
    public void DanglingElse_ElseIfChain_Parses()
    {
        // else-if chain: each else-statement is itself an if; no dangling-else conflict.
        Cs1StatementTestHelper.AssertParses("{ if (a) x = 1; else if (b) y = 2; else if (c) z = 3; else w = 4; }");
    }

    // === Невалидные формы ===

    [TestMethod]
    public void Invalid_DoWhileNoSemicolon_Fails()
    {
        // do { } while (x) — нет ";" после while-условия.
        Cs1StatementTestHelper.AssertFails("{ do { i++; } while (x) }");
    }

    [TestMethod]
    public void Invalid_ForMissingSemicolon_Fails()
    {
        // for (int i = 0 i < n; ) — нет ";" между init и condition.
        Cs1StatementTestHelper.AssertFails("{ for (int i = 0 i < n; ) x = 5; }");
    }

    [TestMethod]
    public void Invalid_ForUnclosedParen_Fails()
    {
        // for (int i = 0; i < n; i++ — нет закрывающей ")".
        Cs1StatementTestHelper.AssertFails("{ for (int i = 0; i < n; i++ x = 5; }");
    }

    [TestMethod]
    public void Invalid_ForeachNoIdentifier_Fails()
    {
        // foreach (int in arr) — нет имени переменной между типом и "in".
        Cs1StatementTestHelper.AssertFails("{ foreach (int in arr) x = 5; }");
    }

    [TestMethod]
    public void Invalid_ForeachReservedAsVarName_Fails()
    {
        // foreach (int if in arr) — имя переменной — истинный идентификатор, не ключевое слово.
        Cs1StatementTestHelper.AssertFails("{ foreach (int if in arr) x = 5; }");
    }

    [TestMethod]
    public void Invalid_IfUnclosedParen_Fails()
    {
        // if (a x = 5; — нет закрывающей ")" после условия.
        Cs1StatementTestHelper.AssertFails("{ if (a x = 5; }");
    }

    [TestMethod]
    public void Invalid_WhileUnclosedParen_Fails()
    {
        // while (x { i++; } — нет закрывающей ")" после условия.
        Cs1StatementTestHelper.AssertFails("{ while (x { i++; } }");
    }
}
