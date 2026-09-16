using ExtensibleParser;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// Простые операторы C# 1.0 (T2.2.1): Block + empty / expression-stmt / local-variable-declaration /
// return / throw / break / continue / goto / labeled. Парсинг — по правилу Block (start rule "Block";
// см. Cs1StatementTestHelper). Roslyn-отбор — в Cs1RoslynStatementTests.
[TestClass]
public class Cs1StatementTests
{
    // === Пустой оператор ===

    [TestMethod]
    public void Empty_Statement_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ ; }");
    }

    // === Expression statement ===

    [TestMethod]
    public void Expression_Assignment_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ x = 5; }");
    }

    [TestMethod]
    public void Expression_MemberAccess_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ x.y = 5; }");
    }

    [TestMethod]
    public void Expression_Invocation_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ Foo(); }");
    }

    [TestMethod]
    public void Expression_Complex_Parses()
    {
        // Полное выражение (бинарный оператор + вызов) как оператор.
        Cs1StatementTestHelper.AssertParses("{ x = Foo(a + b); }");
    }

    // === Local variable declaration ===

    [TestMethod]
    public void LocalDecl_PredefinedType_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ int x = 5; }");
    }

    [TestMethod]
    public void LocalDecl_IdentifierType_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ X x = 5; }");
    }

    [TestMethod]
    public void LocalDecl_NoInit_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ int x; }");
    }

    [TestMethod]
    public void LocalDecl_ArrayType_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ int[] x = new int[3]; }");
    }

    [TestMethod]
    public void LocalDecl_MultiDeclarator_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ int a = 1, b = 2; }");
    }

    [TestMethod]
    public void LocalDecl_MultiDeclarator_NoInit_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ int a, b; }");
    }

    // === return / throw ===

    [TestMethod]
    public void Return_NoExpr_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ return; }");
    }

    [TestMethod]
    public void Return_WithExpr_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ return x; }");
    }

    [TestMethod]
    public void Throw_NoExpr_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ throw; }");
    }

    [TestMethod]
    public void Throw_WithExpr_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ throw ex; }");
    }

    // === break / continue ===

    [TestMethod]
    public void Break_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ break; }");
    }

    [TestMethod]
    public void Continue_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ continue; }");
    }

    // === goto ===

    [TestMethod]
    public void Goto_Label_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ goto l; }");
    }

    [TestMethod]
    public void Goto_Case_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ goto case 5; }");
    }

    [TestMethod]
    public void Goto_Default_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ goto default; }");
    }

    // === Labeled statement ===

    [TestMethod]
    public void Labeled_Expression_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ l: x = 5; }");
    }

    [TestMethod]
    public void Labeled_Return_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ l: return; }");
    }

    [TestMethod]
    public void Labeled_Block_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ l: { x = 5; } }");
    }

    // === Block ===

    [TestMethod]
    public void EmptyBlock_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ }");
    }

    [TestMethod]
    public void NestedBlock_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ { x = 5; } }");
    }

    [TestMethod]
    public void MultipleStatements_Parses()
    {
        Cs1StatementTestHelper.AssertParses("{ x = 5; y = 3; }");
    }

    // === Disambiguation: декларация vs expression-statement (longest-match) ===

    [TestMethod]
    public void Disambiguation_ReservedTypeIsDeclaration_Shape()
    {
        // int x = 5; — декларация: int — reserved, не начало Expression → только LocalVariableDeclaration.
        Assert.AreEqual("LocalVariableDeclaration", Cs1StatementTestHelper.FirstStatementKind("{ int x = 5; }"));
    }

    [TestMethod]
    public void Disambiguation_IdentTypeIsDeclaration_Shape()
    {
        // X x = 5; — декларация: X x — два имени подряд, не выражение → только LocalVariableDeclaration.
        Assert.AreEqual("LocalVariableDeclaration", Cs1StatementTestHelper.FirstStatementKind("{ X x = 5; }"));
    }

    [TestMethod]
    public void Disambiguation_SingleNameIsExpression_Shape()
    {
        // x = 5; — expression statement: после Type=x идёт =, не идентификатор → декларация не складывается.
        Assert.AreEqual("ExpressionStatement", Cs1StatementTestHelper.FirstStatementKind("{ x = 5; }"));
    }

    [TestMethod]
    public void Disambiguation_MemberAccessIsExpression_Shape()
    {
        // x.y = 5; — expression statement: Type=x (затем .) или x.y (затем =) → декларация не складывается.
        Assert.AreEqual("ExpressionStatement", Cs1StatementTestHelper.FirstStatementKind("{ x.y = 5; }"));
    }

    [TestMethod]
    public void Disambiguation_InvocationIsExpression_Shape()
    {
        // Foo(); — expression statement: после Type=Foo идёт (, не идентификатор → декларация не складывается.
        Assert.AreEqual("ExpressionStatement", Cs1StatementTestHelper.FirstStatementKind("{ Foo(); }"));
    }

    // === Невалидные формы ===

    [TestMethod]
    public void Invalid_UnclosedBlock_Fails()
    {
        Cs1StatementTestHelper.AssertFails("{ x = 5;");
    }

    [TestMethod]
    public void Invalid_LocalDeclNoName_Fails()
    {
        // int = 5; — нет имени переменной после типа.
        Cs1StatementTestHelper.AssertFails("{ int = 5; }");
    }

    [TestMethod]
    public void Invalid_LocalDeclNoSemicolon_Fails()
    {
        Cs1StatementTestHelper.AssertFails("{ int x = 5 }");
    }

    [TestMethod]
    public void Invalid_ReturnMalformed_Fails()
    {
        // return = ; — = не является началом Expression.
        Cs1StatementTestHelper.AssertFails("{ return = ; }");
    }

    [TestMethod]
    public void Invalid_GotoNoTarget_Fails()
    {
        Cs1StatementTestHelper.AssertFails("{ goto; }");
    }

    [TestMethod]
    public void Invalid_BreakWithLabel_Fails()
    {
        // C# 1.0: break без метки (Roslyn допускает метку только для error recovery, 9335).
        Cs1StatementTestHelper.AssertFails("{ break x; }");
    }

    [TestMethod]
    public void Invalid_ContinueWithLabel_Fails()
    {
        // C# 1.0: continue без метки (Roslyn допускает метку только для error recovery, 9344).
        Cs1StatementTestHelper.AssertFails("{ continue x; }");
    }

    [TestMethod]
    public void Invalid_GotoCaseIdentifier_Fails()
    {
        // C# 1.0: case-метка goto — константное выражение, не идентификатор.
        Cs1StatementTestHelper.AssertFails("{ goto case x; }");
    }

    [TestMethod]
    public void Invalid_ReservedWordAsLabel_Fails()
    {
        // Метка — истинный идентификатор, не ключевое слово.
        Cs1StatementTestHelper.AssertFails("{ return: x = 5; }");
    }

    [TestMethod]
    public void Invalid_ReservedWordAsVarName_Fails()
    {
        // Имя переменной — истинный идентификатор, не ключевое слово.
        Cs1StatementTestHelper.AssertFails("{ int return; }");
    }

    // === T2.3.3: преопределённый тип в начале member-access ===
    // Roslyn: преопределённый тип — начало expression ТОЛЬКО сразу перед "." (иначе не
    // выражение). LanguageParser.cs:8514 + ParsePrimaryExpressionWithoutPostfix (12078-12093).
    // В statement-контексте: `int` без "." — это (неполная) декларация, а не expression-statement.

    [TestMethod]
    public void PredefinedMember_ExpressionStatement_Parses()
    {
        // { int.Parse(); } — int перед "." → expression-statement (не декларация).
        Cs1StatementTestHelper.AssertParses("{ int.Parse(); }");
    }

    [TestMethod]
    public void PredefinedMember_PropertyExpressionStatement_Parses()
    {
        // { double.MaxValue; } — int перед "." → expression-statement.
        Cs1StatementTestHelper.AssertParses("{ double.MaxValue; }");
    }

    [TestMethod]
    public void LocalDecl_StringType_Parses()
    {
        // Декларация с преопределённым типом string (req 4).
        Cs1StatementTestHelper.AssertParses("{ string s = \"a\"; }");
    }

    [TestMethod]
    public void Invalid_BarePredefinedType_Fails()
    {
        // { int; } — int без "." — не выражение (тип не значение) и не полная декларация (нет имени).
        Cs1StatementTestHelper.AssertFails("{ int; }");
    }

    [TestMethod]
    public void Invalid_BarePredefinedTypeAssignment_Fails()
    {
        // { x = int; } — RHS int без "." — не выражение.
        Cs1StatementTestHelper.AssertFails("{ x = int; }");
    }

    [TestMethod]
    public void Invalid_BareNew_Fails()
    {
        // { new; } — new — зарезервированное слово, не идентификатор (guard T2.3.1 сохранён).
        Cs1StatementTestHelper.AssertFails("{ new; }");
    }

    [TestMethod]
    public void Invalid_BareTypeof_Fails()
    {
        // { typeof; } — typeof требует ( Type ), не голый идентификатор (guard сохранён).
        Cs1StatementTestHelper.AssertFails("{ typeof; }");
    }

    [TestMethod]
    public void Invalid_BareSizeof_Fails()
    {
        // { sizeof; } — sizeof требует ( Type ), не голый идентификатор (guard сохранён).
        Cs1StatementTestHelper.AssertFails("{ sizeof; }");
    }
}
