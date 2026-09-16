using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// Тесты выражений C# 1.0, отобранные из Roslyn (T2.3.1). Критерии: POSITIVE = Roslyn фиксирует
// 0 parse-ошибок для формы (семантические ошибки допустимы); форма C# 1.0-совместима.
// Адаптация: Roslyn-контекст (for/if/локальный) → bare Expression (start rule "Expression",
// см. Cs1ExpressionTestHelper); имена подставлены под нейтральные.
[TestClass]
public class Cs1RoslynExpressionTests
{
    // === src/Compilers/CSharp/Test/Syntax/Parsing/ForStatementParsingTest.cs ===

    [TestMethod]
    public void Roslyn_ForStatement_VariousExpressions_Typeof()
    {
        // Roslyn: ForStatementParsingTest.TestVariousExpressions_Typeof — `for (typeof(int);...)`,
        // 0 parse-ошибок (TypeOfExpression + PredefinedType/IntKeyword).
        // Адаптация: for-инициализатор → bare Expression.
        Cs1ExpressionTestHelper.AssertParses("typeof(int)");
    }

    [TestMethod]
    public void Roslyn_ForStatement_VariousExpressions_ArrayCreation()
    {
        // Roslyn: ForStatementParsingTest.TestVariousExpressions_ArrayCreation —
        // `for (new int[] { };...)`, 0 parse-ошибок (ArrayCreationExpression + ArrayType +
        // ArrayRankSpecifier с omitted size).
        // Адаптация: for-инициализатор → bare Expression.
        Cs1ExpressionTestHelper.AssertParses("new int[] { }");
    }

    [TestMethod]
    public void Roslyn_ForStatement_VariousExpressions_ObjectCreation1()
    {
        // Roslyn: ForStatementParsingTest.TestVariousExpressions_ObjectCreation1 —
        // `for (new A();...)`, 0 parse-ошибок (ObjectCreationExpression + IdentifierName +
        // пустой ArgumentList).
        // Адаптация: for-инициализатор → bare Expression.
        Cs1ExpressionTestHelper.AssertParses("new A()");
    }

    // === src/Compilers/CSharp/Test/Syntax/Parsing/RoundTrippingTests.cs ===

    [TestMethod]
    public void Roslyn_RoundTripping_Bug909419_IsInConditional()
    {
        // Roslyn: RoundTrippingTests.Bug909419 — `int n1 = test is Test ? 0 : 1;`,
        // ParseAndRoundTripping (0 parse-ошибок). is/Relational внутри Conditional.
        // Адаптация: локальный → bare Expression; test→x, Test→T.
        Cs1ExpressionTestHelper.AssertParses("x is T ? 0 : 1");
    }

    [TestMethod]
    public void Roslyn_RoundTripping_Bug909419_AsInConditionalEquality()
    {
        // Roslyn: RoundTrippingTests.Bug909419 — `int n2 = null == test as Test ? 0 : 1;`,
        // ParseAndRoundTripping (0 parse-ошибок). as/Relational связывает раньше ==/Equality:
        // (null == (test as Test)) ? 0 : 1.
        // Адаптация: локальный → bare Expression; test→x, Test→T.
        Cs1ExpressionTestHelper.AssertParses("null == x as T ? 0 : 1");
    }

    // === src/Compilers/CSharp/Test/Syntax/Parsing/NullableParsingTests.cs ===

    [TestMethod]
    public void Roslyn_NullableParsing_IsArrayTypeInConditional()
    {
        // Roslyn: NullableParsingTests (TestIsTypeTestArray) — `x is T[] ? y : z`, 0 parse-ошибок.
        // RHS — массивный тип (Type, не Expression): ранг `[]` парсится целиком.
        // Адаптация: bare Expression (форма уже выражение).
        Cs1ExpressionTestHelper.AssertParses("x is T[] ? y : z");
    }

    // === src/Compilers/CSharp/Test/Syntax/Parsing/PatternParsingTests.cs ===

    [TestMethod]
    public void Roslyn_PatternParsing_IsJaggedArrayInConditional()
    {
        // Roslyn: PatternParsingTests — `o is A[][] ? b : c`, 0 parse-ошибок (type test,
        // не pattern: RHS — тип). Jagged-массив (два ранга) парсится целиком.
        // Адаптация: bare Expression (форма уже выражение).
        Cs1ExpressionTestHelper.AssertParses("o is A[][] ? b : c");
    }

    // === Негативные (version purity / malformed), отобранные из Roslyn ===

    [TestMethod]
    public void Roslyn_ErrorMessage_NewNoParens_Fails()
    {
        // Roslyn: ParserErrorMessageTests.CS1526ERR_BadNewExpr — `new int;` → ERR_BadNewExpr
        // (CS1526): object-creation без argument-list/инициализатора — parse-ошибка.
        // Адаптация: локальный → bare Expression; int→Foo (имя не важно для синтаксиса).
        Cs1ExpressionTestHelper.AssertFails("new Foo");
    }
}
