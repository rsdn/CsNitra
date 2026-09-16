using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// Простые кейсы выражений C# 1.0 (T2.3.1): new (объект/массив), is/as, sizeof/typeof,
// взаимодействия приоритетов. Парсинг — напрямую по правилу Expression (start rule "Expression";
// см. Cs1ExpressionTestHelper). Roslyn-отбор — в Cs1RoslynExpressionTests.
[TestClass]
public class Cs1ExpressionTests
{
    // === new: объект ===

    [TestMethod]
    public void NewObject_EmptyArgs_Succeeds()
    {
        Cs1ExpressionTestHelper.AssertParses("new Foo()");
    }

    [TestMethod]
    public void NewObject_WithArgs_Succeeds()
    {
        Cs1ExpressionTestHelper.AssertParses("new Foo(1, 2)");
    }

    [TestMethod]
    public void NewObject_QualifiedName_Succeeds()
    {
        Cs1ExpressionTestHelper.AssertParses("new A.B.C()");
    }

    [TestMethod]
    public void NewObject_ExpressionArgs_Succeeds()
    {
        // Аргументы — полные выражения (вызов, бинарный оператор).
        Cs1ExpressionTestHelper.AssertParses("new Foo(f(x), a + b)");
    }

    [TestMethod]
    public void NewObject_PredefinedType_Succeeds()
    {
        // int — PredefinedType как базовый тип (семантически int не имеет ctor, но синтаксис чист).
        Cs1ExpressionTestHelper.AssertParses("new int()");
    }

    // === new: массив ===

    [TestMethod]
    public void NewArray_SingleSize_Succeeds()
    {
        Cs1ExpressionTestHelper.AssertParses("new int[5]");
    }

    [TestMethod]
    public void NewArray_MultiSize_Succeeds()
    {
        Cs1ExpressionTestHelper.AssertParses("new int[2, 3]");
    }

    [TestMethod]
    public void NewArray_Initializer_Succeeds()
    {
        // Опущенный размер + array-инициализатор (C# 1.0).
        Cs1ExpressionTestHelper.AssertParses("new int[] { 1, 2, 3 }");
    }

    [TestMethod]
    public void NewArray_SizeAndInitializer_Succeeds()
    {
        // Явный размер + array-инициализатор.
        Cs1ExpressionTestHelper.AssertParses("new int[3] { 1, 2, 3 }");
    }

    [TestMethod]
    public void NewArray_MultiRankInitializer_Succeeds()
    {
        // Многомерный (ранг 2, явные размеры) + инициализатор. Опущенные размеры в многомерном
        // массиве не валидны (C#: размеры обязательны для ранга > 1).
        Cs1ExpressionTestHelper.AssertParses("new int[2, 2] { 1, 2, 3, 4 }");
    }

    [TestMethod]
    public void NewArray_ExpressionSize_Succeeds()
    {
        // Размер — полное выражение.
        Cs1ExpressionTestHelper.AssertParses("new int[n + 1]");
    }

    [TestMethod]
    public void NewArray_QualifiedName_Succeeds()
    {
        Cs1ExpressionTestHelper.AssertParses("new A.B[3]");
    }

    // === is / as ===

    [TestMethod]
    public void Is_PredefinedType_Succeeds()
    {
        Cs1ExpressionTestHelper.AssertParses("x is int");
    }

    [TestMethod]
    public void Is_ArrayType_Succeeds()
    {
        // RHS — Type (не Expression): массивный ранг должен парситься целиком.
        Cs1ExpressionTestHelper.AssertParses("x is int[]");
    }

    [TestMethod]
    public void Is_QualifiedType_Succeeds()
    {
        Cs1ExpressionTestHelper.AssertParses("x is System.String");
    }

    [TestMethod]
    public void Is_PointerType_Succeeds()
    {
        // Указательный тип (unsafe, но синтаксис чист).
        Cs1ExpressionTestHelper.AssertParses("x is int*");
    }

    [TestMethod]
    public void Is_PointerToArray_Succeeds()
    {
        // Чередование рангов: указатель на массив.
        Cs1ExpressionTestHelper.AssertParses("x is int[]*");
    }

    [TestMethod]
    public void As_PredefinedType_Succeeds()
    {
        Cs1ExpressionTestHelper.AssertParses("x as int");
    }

    [TestMethod]
    public void As_ArrayType_Succeeds()
    {
        Cs1ExpressionTestHelper.AssertParses("x as int[]");
    }

    [TestMethod]
    public void As_QualifiedType_Succeeds()
    {
        Cs1ExpressionTestHelper.AssertParses("x as System.String");
    }

    // === sizeof / typeof ===

    [TestMethod]
    public void SizeOf_PredefinedType_Succeeds()
    {
        Cs1ExpressionTestHelper.AssertParses("sizeof(int)");
    }

    [TestMethod]
    public void TypeOf_PredefinedType_Succeeds()
    {
        Cs1ExpressionTestHelper.AssertParses("typeof(int)");
    }

    [TestMethod]
    public void SizeOf_QualifiedType_Succeeds()
    {
        Cs1ExpressionTestHelper.AssertParses("sizeof(System.String)");
    }

    [TestMethod]
    public void TypeOf_ArrayType_Succeeds()
    {
        Cs1ExpressionTestHelper.AssertParses("typeof(int[])");
    }

    [TestMethod]
    public void SizeOf_PointerType_Succeeds()
    {
        Cs1ExpressionTestHelper.AssertParses("sizeof(int*)");
    }

    // === Взаимодействия приоритетов ===

    [TestMethod]
    public void Precedence_AdditiveBindsTighterThanIs_Succeeds()
    {
        // a + b is int → (a + b) is int: Additive (выше) связывает раньше is/Relational.
        Cs1ExpressionTestHelper.AssertParses("a + b is int");
    }

    [TestMethod]
    public void Precedence_IsBindsTighterThanEquality_Succeeds()
    {
        // x is int == y → (x is int) == y: is/Relational связывает раньше ==/Equality.
        Cs1ExpressionTestHelper.AssertParses("x is int == y");
    }

    [TestMethod]
    public void Precedence_IsInConditional_Succeeds()
    {
        Cs1ExpressionTestHelper.AssertParses("x is int ? a : b");
    }

    [TestMethod]
    public void Precedence_SizeOfAsPrimaryInAdditive_Succeeds()
    {
        // sizeof(int) — первичное; sizeof(int) + 1 → (sizeof(int)) + 1.
        Cs1ExpressionTestHelper.AssertParses("sizeof(int) + 1");
    }

    [TestMethod]
    public void Precedence_TypeOfInInvocation_Succeeds()
    {
        // f(typeof(int)) — typeof как аргумент.
        Cs1ExpressionTestHelper.AssertParses("f(typeof(int))");
    }

    [TestMethod]
    public void Precedence_NewInAdditive_Succeeds()
    {
        // new Foo() + 1 → (new Foo()) + 1.
        Cs1ExpressionTestHelper.AssertParses("new Foo() + 1");
    }

    [TestMethod]
    public void Precedence_IsBindsTighterThanLogicalAnd_Succeeds()
    {
        // x is int && y is int → (x is int) && (y is int).
        Cs1ExpressionTestHelper.AssertParses("x is int && y is int");
    }

    [TestMethod]
    public void Precedence_UnaryBindsTighterThanIs_Succeeds()
    {
        // !x is int → (!x) is int: Unary (выше) связывает раньше is/Relational.
        Cs1ExpressionTestHelper.AssertParses("!x is int");
    }

    // === Невалидные формы (version purity + malformed) ===

    [TestMethod]
    public void Invalid_NewNoParens_Fails()
    {
        // new T без скобок/размеров/инициализатора — ERR_BadNewExpr (CS1526), см.
        // ParserErrorMessageTests.CS1526ERR_BadNewExpr (`new int;`).
        Cs1ExpressionTestHelper.AssertFails("new Foo");
    }

    [TestMethod]
    public void Invalid_NewGeneric_Fails()
    {
        // Дженерики — CS2, вне C# 1.0.
        Cs1ExpressionTestHelper.AssertFails("new Foo<int>()");
    }

    [TestMethod]
    public void Invalid_NewObjectInitializer_Fails()
    {
        // Object-инициализатор — CS3, вне C# 1.0.
        Cs1ExpressionTestHelper.AssertFails("new Foo { Prop = 1 }");
    }

    [TestMethod]
    public void Invalid_NewCollectionInitializer_Fails()
    {
        // Collection-инициализатор — CS3, вне C# 1.0.
        Cs1ExpressionTestHelper.AssertFails("new Foo { 1, 2 }");
    }

    [TestMethod]
    public void Invalid_NewPointer_Fails()
    {
        // new-указателя нет в C# 1.0 (NewBaseType — только PredefinedType | QualifiedName).
        Cs1ExpressionTestHelper.AssertFails("new int*");
    }

    [TestMethod]
    public void Invalid_NewUnclosedBracket_Fails()
    {
        Cs1ExpressionTestHelper.AssertFails("new int[5");
    }

    [TestMethod]
    public void Invalid_IsNullableType_Fails()
    {
        // Nullable value type (T?) — CS2, вне C# 1.0.
        Cs1ExpressionTestHelper.AssertFails("x is int?");
    }

    [TestMethod]
    public void Invalid_IsGenericType_Fails()
    {
        // Дженерик-тип — CS2, вне C# 1.0.
        Cs1ExpressionTestHelper.AssertFails("x is Foo<int>");
    }

    [TestMethod]
    public void Invalid_TypeOfNullableType_Fails()
    {
        // Nullable value type (T?) — CS2, вне C# 1.0.
        Cs1ExpressionTestHelper.AssertFails("typeof(int?)");
    }

    [TestMethod]
    public void Invalid_SizeOfUnclosedParen_Fails()
    {
        Cs1ExpressionTestHelper.AssertFails("sizeof(int");
    }

    [TestMethod]
    public void Invalid_TypeOfMultipleTypes_Fails()
    {
        // typeof принимает ровно один тип.
        Cs1ExpressionTestHelper.AssertFails("typeof(int, int)");
    }

    [TestMethod]
    public void Invalid_IsMissingType_Fails()
    {
        Cs1ExpressionTestHelper.AssertFails("x is");
    }

    [TestMethod]
    public void Invalid_NewMissingType_Fails()
    {
        Cs1ExpressionTestHelper.AssertFails("new");
    }
}
