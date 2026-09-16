using ExtensibleParser;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// Сложные операторы C# 1.0 (T2.2.3): switch / try / using / lock / checked / unchecked. Парсинг — по
// правилу Block (start rule "Block"; см. Cs1StatementTestHelper). Roslyn-отбор — в Cs1RoslynStatementTests.
// Ключевые: (1) switch-секции со стеком меток `case 1: case 2:` = ОДНА секция; (2) try требует catch и/или
// finally (`try { }` невалиден); (3) using — декларация-vs-выражение через longest-match; (4) catch-all
// `catch { }` валиден. См. docs/CSharpParserPlan-progressT2.2.3.md.
[TestClass]
public class Cs1SwitchTryTests
{
    // === switch ===

    [TestMethod]
    public void Switch_SingleCase_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ switch (e) { case 1: x = 1; } }");
    }

    [TestMethod]
    public void Switch_MultipleCases_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ switch (e) { case 1: x = 1; case 2: x = 2; } }");
    }

    [TestMethod]
    public void Switch_StackedLabels_Parses()
    {
        // case 1: case 2: x = 3; — ОДНА секция с двумя метками, затем операторы.
        Cs1StatementTestHelper.AssertParses("{ switch (e) { case 1: case 2: x = 3; } }");
    }

    [TestMethod]
    public void Switch_Default_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ switch (e) { default: x = 0; } }");
    }

    [TestMethod]
    public void Switch_Mixed_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ switch (e) { case 1: x = 1; default: x = 0; } }");
    }

    [TestMethod]
    public void Switch_CharCase_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ switch (c) { case 'a': x = 1; } }");
    }

    [TestMethod]
    public void Switch_StringCase_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ switch (s) { case \"s\": x = 1; } }");
    }

    [TestMethod]
    public void Switch_BoolCase_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ switch (b) { case true: x = 1; } }");
    }

    [TestMethod]
    public void Switch_NegativeConstant_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ switch (e) { case -1: x = 1; } }");
    }

    [TestMethod]
    public void Switch_HexConstant_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ switch (e) { case 0x1F: x = 1; } }");
    }

    [TestMethod]
    public void Switch_Empty_Parses()
    {
        // Пустой switch (без секций) валиден.
        Cs1StatementTestHelper.AssertParses("{ switch (e) { } }");
    }

    [TestMethod]
    public void Switch_NestedBlock_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ switch (e) { case 1: { x = 1; } } }");
    }

    [TestMethod]
    public void Switch_BreakInCase_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ switch (e) { case 1: break; default: break; } }");
    }

    // === try / catch / finally ===

    [TestMethod]
    public void Try_Catch_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ try { } catch (E e) { } }");
    }

    [TestMethod]
    public void Try_Finally_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ try { } finally { } }");
    }

    [TestMethod]
    public void Try_CatchFinally_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ try { } catch (E e) { } finally { } }");
    }

    [TestMethod]
    public void Try_MultipleCatches_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ try { } catch (E e) { } catch (F f) { } }");
    }

    [TestMethod]
    public void Try_MultipleCatchesFinally_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ try { } catch (E e) { } catch (F f) { } finally { } }");
    }

    [TestMethod]
    public void Try_CatchAll_Parses()
    {
        // catch-all `catch { }` — валиден (Roslyn Declaration == null, TestTryCatchWithNoExceptionDeclaration).
        Cs1StatementTestHelper.AssertParses("{ try { } catch { } }");
    }

    [TestMethod]
    public void Try_CatchNoName_Parses()
    {
        // catch (E) — тип без имени (имя опционально).
        Cs1StatementTestHelper.AssertParses("{ try { } catch (E) { } }");
    }

    [TestMethod]
    public void Try_CatchQualifiedType_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ try { } catch (System.Exception e) { } }");
    }

    [TestMethod]
    public void Try_WithStatements_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ try { x = 1; } catch (E e) { y = 2; } }");
    }

    // === using ===

    [TestMethod]
    public void Using_Declaration_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ using (int x = Foo()) { } }");
    }

    [TestMethod]
    public void Using_Expression_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ using (obj) { } }");
    }

    [TestMethod]
    public void Using_DeclarationNoInit_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ using (int x) { } }");
    }

    [TestMethod]
    public void Using_ExpressionMember_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ using (obj.Foo) { } }");
    }

    [TestMethod]
    public void Using_ExpressionInvocation_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ using (Foo()) { } }");
    }

    // === lock ===

    [TestMethod]
    public void Lock_Block_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ lock (obj) { } }");
    }

    [TestMethod]
    public void Lock_Expression_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ lock (obj) x = 1; }");
    }

    // === checked / unchecked ===

    [TestMethod]
    public void Checked_Block_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ checked { x = y + z; } }");
    }

    [TestMethod]
    public void Checked_ExpressionStatement_Parses()
    {
        // checked (x = y + z); — `checked` + ExpressionStatement `(x = y + z);` (см. progressT2.2.3).
        Cs1StatementTestHelper.AssertParses("{ checked (x = y + z); }");
    }

    [TestMethod]
    public void Unchecked_Block_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ unchecked { x = y + z; } }");
    }

    [TestMethod]
    public void Unchecked_ExpressionStatement_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ unchecked (x = y + z); }");
    }

    // === Disambiguation / shape (longest-match) ===

    [TestMethod]
    public void Using_Declaration_Shape()
    {
        // using (int x = Foo()) — два имени подряд → декларация (не выражение).
        Assert.AreEqual("ResourceDeclaration", Cs1StatementTestHelper.UsingResourceKind("{ using (int x = Foo()) { } }"));
    }

    [TestMethod]
    public void Using_Expression_Shape()
    {
        // using (obj) — одно выражение → Expression-альтернатива (не декларация). Простой идентификатор
        // `obj` совпадает с `PrimaryExpr` (первая prefix-альтернатива Expression), поэтому Kind = PrimaryExpr.
        Assert.AreEqual("PrimaryExpr", Cs1StatementTestHelper.UsingResourceKind("{ using (obj) { } }"));
    }

    [TestMethod]
    public void Switch_StackedLabels_OneSection_Shape()
    {
        // case 1: case 2: x = 3; — ОДНА секция с ДВУМЯ метками.
        Assert.AreEqual(1, Cs1StatementTestHelper.SwitchSectionCount("{ switch (e) { case 1: case 2: x = 3; } }"));
        Assert.AreEqual(2, Cs1StatementTestHelper.SwitchFirstSectionLabelCount("{ switch (e) { case 1: case 2: x = 3; } }"));
    }

    [TestMethod]
    public void Switch_MultipleSections_Shape()
    {
        // case 1: ... case 2: ... — ДВЕ секции по одной метке.
        Assert.AreEqual(2, Cs1StatementTestHelper.SwitchSectionCount("{ switch (e) { case 1: x = 1; case 2: x = 2; } }"));
        Assert.AreEqual(1, Cs1StatementTestHelper.SwitchFirstSectionLabelCount("{ switch (e) { case 1: x = 1; case 2: x = 2; } }"));
    }

    // === Невалидные формы ===

    [TestMethod]
    public void Try_NoCatchFinally_Fails()
    {
        // try { } — нет ни catch, ни finally (Roslyn ERR_ExpectedEndTry, 9393-9402).
        Cs1StatementTestHelper.AssertFails("{ try { } }");
    }

    [TestMethod]
    public void Try_TwoFinally_Fails()
    {
        // finally — один; второй finally остаётся вне try → блок не закрывается.
        Cs1StatementTestHelper.AssertFails("{ try { } finally { } finally { } }");
    }

    [TestMethod]
    public void Try_FinallyBeforeCatch_Fails()
    {
        // finally идёт ПОСЛЕ catch*; finally-до-catch невалиден.
        Cs1StatementTestHelper.AssertFails("{ try { } finally { } catch (E e) { } }");
    }

    [TestMethod]
    public void Case_NonConstant_Fails()
    {
        // case x: — идентификатор, не константное выражение (C# 1.0).
        Cs1StatementTestHelper.AssertFails("{ switch (e) { case x: y = 1; } }");
    }

    [TestMethod]
    public void Switch_NonLabelledSection_Fails()
    {
        // Секция ОБЯЗАНА начинаться с метки (SwitchLabel+ — one-or-more); оператор без метки невалиден.
        Cs1StatementTestHelper.AssertFails("{ switch (e) { y = 1; } }");
    }

    [TestMethod]
    public void Using_Malformed_Fails()
    {
        // using (int x Foo()) — после декларатора `int x` ожидается `)`, а не `Foo`.
        Cs1StatementTestHelper.AssertFails("{ using (int x Foo()) { } }");
    }

    [TestMethod]
    public void Checked_NothingAfter_Fails()
    {
        // checked; — после `checked` ни блок, ни expression-statement.
        Cs1StatementTestHelper.AssertFails("{ checked; }");
    }

    [TestMethod]
    public void Unchecked_NothingAfter_Fails()
    {
        Cs1StatementTestHelper.AssertFails("{ unchecked; }");
    }

    [TestMethod]
    public void Lock_NoStatement_Fails()
    {
        // lock (obj) — нет оператора после скобок.
        Cs1StatementTestHelper.AssertFails("{ lock (obj) }");
    }

    [TestMethod]
    public void Switch_UnclosedBrace_Fails()
    {
        // Не хватает закрывающей `}` внешнего блока.
        Cs1StatementTestHelper.AssertFails("{ switch (e) { case 1: x = 1; }");
    }

    [TestMethod]
    public void Try_CatchNoBlock_Fails()
    {
        // catch требует блок; `catch (E e) x = 1;` невалиден.
        Cs1StatementTestHelper.AssertFails("{ try { } catch (E e) x = 1; }");
    }
}
