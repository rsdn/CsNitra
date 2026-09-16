using ExtensibleParser;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// Roslyn-отбор простых операторов C# 1.0 (T2.2.1) и циклов/условных (T2.2.2). Каждый тест помечен источником
// (file:method, C:\RSDN\roslyn) и адаптацией под Block-start-rule (C# 1.0: без ref-возврата,
// goto case — константа, break/continue — без метки). Парсинг — по правилу Block (см.
// Cs1StatementTestHelper).
[TestClass]
public class Cs1RoslynStatementTests
{
    // === StatementParsingTests (src/Compilers/CSharp/Test/Syntax/Parsing/StatementParsingTests.cs) ===

    [TestMethod]
    public void Roslyn_EmptyStatement_Parses()
    {
        // StatementParsingTests.TestEmptyStatement → ";" (обёрнуто в блок).
        Cs1StatementTestHelper.AssertParses("{ ; }");
    }

    [TestMethod]
    public void Roslyn_LabeledStatement_Parses()
    {
        // StatementParsingTests.TestLabeledStatement → "label: ;" (обёрнуто в блок).
        Cs1StatementTestHelper.AssertParses("{ label: ; }");
    }

    [TestMethod]
    public void Roslyn_BreakStatement_Parses()
    {
        // StatementParsingTests.TestBreakStatement → "break;" (обёрнуто в блок).
        Cs1StatementTestHelper.AssertParses("{ break; }");
    }

    [TestMethod]
    public void Roslyn_ContinueStatement_Parses()
    {
        // StatementParsingTests.TestContinueStatement → "continue;" (обёрнуто в блок).
        Cs1StatementTestHelper.AssertParses("{ continue; }");
    }

    [TestMethod]
    public void Roslyn_GotoStatement_Parses()
    {
        // StatementParsingTests.TestGotoStatement → "goto label;" (обёрнуто в блок).
        Cs1StatementTestHelper.AssertParses("{ goto label; }");
    }

    [TestMethod]
    public void Roslyn_GotoCaseStatement_Parses()
    {
        // StatementParsingTests.TestGotoCaseStatement → "goto case label;". АДАПТАЦИЯ: Roslyn парсит
        // ParseExpressionCore (LanguageParser.cs:9963), но C# 1.0 требует константное выражение → "5".
        Cs1StatementTestHelper.AssertParses("{ goto case 5; }");
    }

    [TestMethod]
    public void Roslyn_GotoDefault_Parses()
    {
        // StatementParsingTests.TestGotoDefault → "goto default;" (обёрнуто в блок).
        Cs1StatementTestHelper.AssertParses("{ goto default; }");
    }

    [TestMethod]
    public void Roslyn_Return_Parses()
    {
        // StatementParsingTests.TestReturn → "return;" (обёрнуто в блок).
        Cs1StatementTestHelper.AssertParses("{ return; }");
    }

    [TestMethod]
    public void Roslyn_ReturnExpression_Parses()
    {
        // StatementParsingTests.TestReturnExpression → "return a;" (обёрнуто в блок).
        Cs1StatementTestHelper.AssertParses("{ return a; }");
    }

    [TestMethod]
    public void Roslyn_Throw_Parses()
    {
        // StatementParsingTests.TestThrow → "throw;" (обёрнуто в блок).
        Cs1StatementTestHelper.AssertParses("{ throw; }");
    }

    [TestMethod]
    public void Roslyn_ThrowExpression_Parses()
    {
        // StatementParsingTests.TestThrowExpression → "throw a;" (обёрнуто в блок).
        Cs1StatementTestHelper.AssertParses("{ throw a; }");
    }

    // === LanguageParser.IsPossibleLocalDeclarationStatement (LanguageParser.cs:8504-8515) ===
    // Декларация, если текущий токен — преопределённый тип, НЕ за которым идёт "." (int.Parse() —
    // выражение) и "(" (int (x, y) — ошибка). Это ядро disambiguation, покрыто shape-тестами ниже.

    [TestMethod]
    public void Roslyn_PredefinedTypeDeclaration_Parses()
    {
        // IsPossibleLocalDeclarationStatement:8513 — преопределённый тип → декларация.
        Assert.AreEqual("LocalVariableDeclaration", Cs1StatementTestHelper.FirstStatementKind("{ int x = 2; }"));
    }

    // ПРИМЕЧАНИЕ: Roslyn IsPossibleLocalDeclarationStatement:8514 гласит, что "int.Parse()" —
    // выражение (тип + "."). Текущая грамматика НЕ парсит "int.Parse()": "int" — reserved, а
    // Primary-идентификатор имеет guard "!ReservedKeyword Identifier" (Cs1.grammar:333, T2.3.1),
    // поэтому reserved-тип не может начинать Expression. Это предсуществующее ограничение, вне
    // скоупа T2.2.1 (см. progressT2.2.1, Boundary decisions).

    // === LanguageParser.ParseLocalDeclarationStatement (LanguageParser.cs:10482-10574) ===

    [TestMethod]
    public void Roslyn_LocalDeclaration_Parses()
    {
        // ParseLocalDeclarationStatement:10568-10574 — VariableDeclaration(type, variables) + Semicolon.
        Cs1StatementTestHelper.AssertParses("{ int x = 5; }");
    }

    [TestMethod]
    public void Roslyn_LocalDeclarationMultipleDeclarators_Parses()
    {
        // ParseVariableDeclarators (5294) — список деклараторов через запятую.
        Cs1StatementTestHelper.AssertParses("{ int a = 1, b = 2; }");
    }

    // === T2.2.2: циклы и условные операторы (StatementParsingTests.cs, C:\RSDN\roslyn) ===
    // Адаптация: обёрнуто в Block (C# 1.0: нет method body); `var` (CS3) заменён типом; `for(var ...)`
    // не в C# 1.0. Dangling else — по ParseIfStatement (LanguageParser.cs:10013).

    [TestMethod]
    public void Roslyn_IfStatement_Parses()
    {
        // StatementParsingTests.TestIf (2012) → "if (a) { }".
        Cs1StatementTestHelper.AssertParses("{ if (a) { } }");
    }

    [TestMethod]
    public void Roslyn_IfElseStatement_Parses()
    {
        // StatementParsingTests.TestIfElse (2035) → "if (a) { } else { }".
        Cs1StatementTestHelper.AssertParses("{ if (a) { } else { } }");
    }

    [TestMethod]
    public void Roslyn_IfElseIfStatement_Parses()
    {
        // StatementParsingTests.TestIfElseIf (2061) → "if (a) { } else if (b) { }".
        Cs1StatementTestHelper.AssertParses("{ if (a) { } else if (b) { } }");
    }

    [TestMethod]
    public void Roslyn_WhileStatement_Parses()
    {
        // StatementParsingTests.TestWhile (1468) → "while(a) { }".
        Cs1StatementTestHelper.AssertParses("{ while (a) { } }");
    }

    [TestMethod]
    public void Roslyn_DoWhileStatement_Parses()
    {
        // StatementParsingTests.TestDoWhile (1490) → "do { } while (a);".
        Cs1StatementTestHelper.AssertParses("{ do { } while (a); }");
    }

    [TestMethod]
    public void Roslyn_ForStatementEmpty_Parses()
    {
        // StatementParsingTests.TestFor (1515) → "for(;;) { }" (все три части пустые).
        Cs1StatementTestHelper.AssertParses("{ for ( ; ; ) { } }");
    }

    [TestMethod]
    public void Roslyn_ForStatementWithDeclaration_Parses()
    {
        // StatementParsingTests.TestForWithVariableDeclaration (1541) → "for(T a = 0;;) { }".
        // АДАПТАЦИЯ: T — identifier-тип (C# 1.0), init — декларация (не var — CS3).
        Cs1StatementTestHelper.AssertParses("{ for (T a = 0; ; ) { } }");
    }

    [TestMethod]
    public void Roslyn_ForeachStatement_Parses()
    {
        // StatementParsingTests.TestForEach (1919) → "foreach(T a in b) { }".
        // АДАПТАЦИЯ: T — identifier-тип (C# 1.0); deconstruction-форма (C# 7) вне скоупа.
        Cs1StatementTestHelper.AssertParses("{ foreach (T a in b) { } }");
    }

    [TestMethod]
    public void Roslyn_DanglingElse_BindsToInnerIf_Shape()
    {
        // LanguageParser.ParseIfStatement (10013): then-statement (10026) парсится ПЕРЕД else (10028),
        // поэтому else связывается с НАИБЛИЖАЙШИМ if. "if (a) if (b) c; else d;" → else у внутреннего if.
        var outer = (SeqNode)Cs1StatementTestHelper.FirstStatementNode("{ if (a) if (b) c; else d; }");
        Assert.AreEqual("none", Cs1StatementTestHelper.IfElsePresent(outer), "outer if must NOT own the else");
        var inner = (SeqNode)outer.Elements[4];
        Assert.AreEqual("IfStatement", inner.Kind);
        Assert.AreEqual("some", Cs1StatementTestHelper.IfElsePresent(inner), "inner if must own the else");
    }

    // === T2.2.3: сложные операторы (StatementParsingTests.cs, C:\RSDN\roslyn) ===
    // Адаптация: обёрнуто в Block; case-метки — константы (C# 1.0: Roslyn парсит expression, константность —
    // на биндинге; здесь `Constant`); `var`/using-декларация (C# 8) — вне скоупа.

    [TestMethod]
    public void Roslyn_SwitchStatement_Parses()
    {
        // StatementParsingTests.TestSwitch (2116) → "switch (a) { }" (пустой switch).
        Cs1StatementTestHelper.AssertParses("{ switch (a) { } }");
    }

    [TestMethod]
    public void Roslyn_SwitchWithCase_Parses()
    {
        // StatementParsingTests.TestSwitchWithCase (2141) → "switch (a) { case b:; }".
        // АДАПТАЦИЯ: `b` — идентификатор; C# 1.0 case-метка — константа → "1".
        Cs1StatementTestHelper.AssertParses("{ switch (a) { case 1: ; } }");
    }

    [TestMethod]
    public void Roslyn_SwitchWithMultipleLabels_Parses()
    {
        // StatementParsingTests.TestSwitchWithMultipleLabelsOnOneCase (2256) → "switch (a) { case b: case c:; }".
        // АДАПТАЦИЯ: константы "1"/"2" вместо идентификаторов; стек меток = ОДНА секция.
        Cs1StatementTestHelper.AssertParses("{ switch (a) { case 1: case 2: ; } }");
    }

    [TestMethod]
    public void Roslyn_TryCatch_Parses()
    {
        // StatementParsingTests.TestTryCatch (1223) → "try { } catch(T e) { }".
        Cs1StatementTestHelper.AssertParses("{ try { } catch (T e) { } }");
    }

    [TestMethod]
    public void Roslyn_TryCatchAll_Parses()
    {
        // StatementParsingTests.TestTryCatchWithNoExceptionDeclaration (1282) → "try { } catch { }".
        // catch-all валиден (Roslyn Declaration == null).
        Cs1StatementTestHelper.AssertParses("{ try { } catch { } }");
    }

    [TestMethod]
    public void Roslyn_TryCatchMultipleAndFinally_Parses()
    {
        // StatementParsingTests.TestTryCatchWithMultipleCatchesAndFinally (1372) →
        // "try { } catch(T e) { } catch(T2) { } catch { } finally { }".
        Cs1StatementTestHelper.AssertParses("{ try { } catch (T e) { } catch (T2) { } catch { } finally { } }");
    }

    [TestMethod]
    public void Roslyn_UsingWithDeclaration_Parses()
    {
        // StatementParsingTests.TestUsingWithDeclaration (2356) → "using (T a = b) { }".
        Cs1StatementTestHelper.AssertParses("{ using (T a = b) { } }");
    }

    [TestMethod]
    public void Roslyn_UsingWithExpression_Parses()
    {
        // StatementParsingTests.TestUsingWithExpression (2334) → "using (a) { }".
        Cs1StatementTestHelper.AssertParses("{ using (a) { } }");
    }

    [TestMethod]
    public void Roslyn_LockStatement_Parses()
    {
        // StatementParsingTests.TestLock (2095) → "lock (a) { }".
        Cs1StatementTestHelper.AssertParses("{ lock (a) { } }");
    }

    [TestMethod]
    public void Roslyn_CheckedStatement_Parses()
    {
        // StatementParsingTests.TestChecked (1417) → "checked { }".
        Cs1StatementTestHelper.AssertParses("{ checked { } }");
    }

    [TestMethod]
    public void Roslyn_UncheckedStatement_Parses()
    {
        // StatementParsingTests.TestUnchecked (1434) → "unchecked { }".
        Cs1StatementTestHelper.AssertParses("{ unchecked { } }");
    }
}
