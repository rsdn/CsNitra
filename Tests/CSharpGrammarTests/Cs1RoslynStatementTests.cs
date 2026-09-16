using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// Roslyn-отбор простых операторов C# 1.0 (T2.2.1). Каждый тест помечен источником
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
}
