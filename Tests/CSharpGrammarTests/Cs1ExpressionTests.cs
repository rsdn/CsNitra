using ExtensibleParser;
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

    // === каст (T2.3.2): (Type) expr ===

    [TestMethod]
    public void Cast_PredefinedType_Succeeds()
    {
        Cs1ExpressionTestHelper.AssertParses("(int) x");
    }

    [TestMethod]
    public void Cast_ArrayType_Succeeds()
    {
        // Тип — массивный: ранг парсится целиком (Type, не Expression).
        Cs1ExpressionTestHelper.AssertParses("(int[]) x");
    }

    [TestMethod]
    public void Cast_PointerType_Succeeds()
    {
        // Указательный тип (unsafe, но синтаксис чист).
        Cs1ExpressionTestHelper.AssertParses("(int*) x");
    }

    [TestMethod]
    public void Cast_PointerToArray_Succeeds()
    {
        // Чередование рангов: указатель на массив.
        Cs1ExpressionTestHelper.AssertParses("(int[]*) x");
    }

    [TestMethod]
    public void Cast_QualifiedType_Succeeds()
    {
        // Квалифицированное имя — и тип, и выражение; каст выигрывает по длине (съедает операнд s).
        Cs1ExpressionTestHelper.AssertParses("(System.String) s");
    }

    [TestMethod]
    public void Cast_ParenthesizedCast_Succeeds()
    {
        // Вложенный каст в скобках.
        Cs1ExpressionTestHelper.AssertParses("((int) x)");
    }

    [TestMethod]
    public void Cast_MemberAccessOperand_Succeeds()
    {
        // Операнд — первичное с postfix: (int) x.y → (int)(x.y) (см. shape-тест ниже).
        Cs1ExpressionTestHelper.AssertParses("(int) x.y");
    }

    [TestMethod]
    public void Cast_UnaryPrefixOperand_Succeeds()
    {
        // Операнд — unарный prefix: (int) ++x → (int)(++x).
        Cs1ExpressionTestHelper.AssertParses("(int) ++x");
    }

    // === преопределённый тип в начале member-access (T2.3.3) ===
    // Roslyn: преопределённый тип — начало expression ТОЛЬКО сразу перед "." (иначе
    // ERR_InvalidExprTerm). LanguageParser.cs:8514 ("int.Parse() is an expression") и
    // ParsePrimaryExpressionWithoutPostfix (12078-12093). Дальнейшие postfix (()/x/[i]) —
    // PostfixOp* в PrimaryExpr.

    [TestMethod]
    public void PredefinedMember_Invocation_Succeeds()
    {
        // int.Parse() — преопределённый тип + member access + вызов.
        Cs1ExpressionTestHelper.AssertParses("int.Parse()");
    }

    [TestMethod]
    public void PredefinedMember_FormatArgs_Succeeds()
    {
        // string.Format("a") — преопределённый тип + member access + аргумент-строка.
        Cs1ExpressionTestHelper.AssertParses("string.Format(\"a\")");
    }

    [TestMethod]
    public void PredefinedMember_PropertyAccess_Succeeds()
    {
        // double.MaxValue — преопределённый тип + member access (без вызова).
        Cs1ExpressionTestHelper.AssertParses("double.MaxValue");
    }

    [TestMethod]
    public void PredefinedMember_CharIsLetter_Succeeds()
    {
        // char.IsLetter('a') — преопределённый тип + member access + вызов с char-аргументом.
        Cs1ExpressionTestHelper.AssertParses("char.IsLetter('a')");
    }

    [TestMethod]
    public void PredefinedMember_BoolParse_Succeeds()
    {
        // bool.Parse("true") — преопределённый тип + member access + вызов.
        Cs1ExpressionTestHelper.AssertParses("bool.Parse(\"true\")");
    }

    [TestMethod]
    public void PredefinedMember_UintMaxValue_Succeeds()
    {
        // uint.MaxValue — преопределённый тип + member access.
        Cs1ExpressionTestHelper.AssertParses("uint.MaxValue");
    }

    [TestMethod]
    public void PredefinedMember_LongMinValue_Succeeds()
    {
        // long.MinValue — преопределённый тип + member access.
        Cs1ExpressionTestHelper.AssertParses("long.MinValue");
    }

    [TestMethod]
    public void PredefinedMember_ChainedInvocation_Succeeds()
    {
        // int.Parse("5").ToString() — первичное int.Parse + postfix: вызов, затем .ToString, затем вызов.
        Cs1ExpressionTestHelper.AssertParses("int.Parse(\"5\").ToString()");
    }

    [TestMethod]
    public void PredefinedMember_InAdditive_Succeeds()
    {
        // double.MaxValue + 1 → (double.MaxValue) + 1: корень — Add, левый операнд — member access.
        Cs1ExpressionTestHelper.AssertParses("double.MaxValue + 1");
    }

    [TestMethod]
    public void QualifiedInt32Parse_Succeeds()
    {
        // System.Int32.Parse() — квалифицированное имя (System — обычный идентификатор, не
        // PredefinedType) — путь IdentifierName + PostfixOp*, НЕ затронут fix'ом (req 5).
        Cs1ExpressionTestHelper.AssertParses("System.Int32.Parse()");
    }

    // === скобки (T2.3.2): (expr) — НЕ каст ===

    [TestMethod]
    public void Parens_AdditiveContinuation_Succeeds()
    {
        // (x) + 1 — скобки, НЕ каст: после (x) идёт бинарный оператор (CanFollowCast(+)=false).
        // Тай-брейк longest-match: PrimaryExpr (Parens) стоит первым prefix-альтернативой → выигрывает.
        // Корень — Add (не CastExpr): доказательство, что это (x)+1, а не (x)(+1).
        var node = Cs1ExpressionTestHelper.ParseExpression("(x) + 1");
        Assert.AreEqual("Add", node.Kind);
    }

    [TestMethod]
    public void Parens_SubtractionContinuation_Succeeds()
    {
        // (x) - 1 — скобки, НЕ каст (CanFollowCast(-)=false). Корень — Sub (не CastExpr).
        var node = Cs1ExpressionTestHelper.ParseExpression("(x) - 1");
        Assert.AreEqual("Sub", node.Kind);
    }

    [TestMethod]
    public void Parens_Alone_Succeeds()
    {
        // (x) — после ) нет операнда каста → только скобки.
        Cs1ExpressionTestHelper.AssertParses("(x)");
    }

    [TestMethod]
    public void Parens_Qualified_Succeeds()
    {
        // (a.b) — a.b и тип, и выражение; после ) нет операнда → только скобки.
        Cs1ExpressionTestHelper.AssertParses("(a.b)");
    }

    [TestMethod]
    public void Parens_Expression_Succeeds()
    {
        // (1 + 2) — литерал не является началом Type → только скобки.
        Cs1ExpressionTestHelper.AssertParses("(1 + 2)");
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

    // === Shape: каст связывает ЖЁСТЧЕ всех бинарных (T2.3.2) ===

    [TestMethod]
    public void Precedence_CastBindsTighterThanAdditive_Shape()
    {
        // (int) x + y → ((int)x) + y, НЕ (int)(x+y). Корень — Add, его левый операнд — каст.
        var node = Cs1ExpressionTestHelper.ParseExpression("(int) x + y");
        var add = node as SeqNode
            ?? throw new InvalidOperationException($"Expected SeqNode root, got {node?.GetType().Name}");
        Assert.AreEqual("Add", add.Kind);
        var cast = add.Elements[0] as SeqNode
            ?? throw new InvalidOperationException($"Expected CastExpr left operand, got {add.Elements[0]?.GetType().Name}");
        Assert.AreEqual("CastExpr", cast.Kind);
    }

    [TestMethod]
    public void Precedence_CastBindsTighterThanMultiplicative_Shape()
    {
        // (int) x * y → ((int)x) * y, НЕ (int)(x*y). Корень — Mul, его левый операнд — каст.
        var node = Cs1ExpressionTestHelper.ParseExpression("(int) x * y");
        var mul = node as SeqNode
            ?? throw new InvalidOperationException($"Expected SeqNode root, got {node?.GetType().Name}");
        Assert.AreEqual("Mul", mul.Kind);
        var cast = mul.Elements[0] as SeqNode
            ?? throw new InvalidOperationException($"Expected CastExpr left operand, got {mul.Elements[0]?.GetType().Name}");
        Assert.AreEqual("CastExpr", cast.Kind);
    }

    [TestMethod]
    public void Precedence_CastOperandIncludesMemberAccess_Shape()
    {
        // (int) x.y → (int)(x.y): postfix .y — внутри операнда каста (корень — CastExpr, а не
        // member-access поверх каста). Соответствует C# 1.0 (как !x.y → !(x.y)).
        var node = Cs1ExpressionTestHelper.ParseExpression("(int) x.y");
        var cast = node as SeqNode
            ?? throw new InvalidOperationException($"Expected SeqNode root, got {node?.GetType().Name}");
        Assert.AreEqual("CastExpr", cast.Kind);
        var operand = cast.Elements[^1];
        Assert.AreEqual("x.y", operand.ToString("(int) x.y"));
    }

    [TestMethod]
    public void Precedence_CastOperandIncludesUnaryPrefix_Shape()
    {
        // (int) ++x → (int)(++x): unарный prefix — внутри операнда каста (корень — CastExpr).
        var node = Cs1ExpressionTestHelper.ParseExpression("(int) ++x");
        var cast = node as SeqNode
            ?? throw new InvalidOperationException($"Expected SeqNode root, got {node?.GetType().Name}");
        Assert.AreEqual("CastExpr", cast.Kind);
        var operand = cast.Elements[^1];
        Assert.AreEqual("++x", operand.ToString("(int) ++x"));
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

    // === Невалидные касты (T2.3.2) ===

    [TestMethod]
    public void Invalid_CastNoOperand_Fails()
    {
        // (int) без операнда — каст требует Expression после ).
        Cs1ExpressionTestHelper.AssertFails("(int)");
    }

    [TestMethod]
    public void Invalid_CastUnclosedType_Fails()
    {
        // (int — незакрытая скобка типа.
        Cs1ExpressionTestHelper.AssertFails("(int");
    }

    [TestMethod]
    public void Invalid_CastUnclosedParen_Fails()
    {
        // (int x — нет ) после типа.
        Cs1ExpressionTestHelper.AssertFails("(int x");
    }

    [TestMethod]
    public void Invalid_CastGeneric_Fails()
    {
        // Дженерик-каст — CS2, вне C# 1.0: < не является частью Type → каст/скобки не парсятся.
        Cs1ExpressionTestHelper.AssertFails("(Foo<int>) x");
    }
}
