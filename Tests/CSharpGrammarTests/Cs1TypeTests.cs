using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// Простые кейсы грамматики типов C# 1.0 (T1.4): преопределённые типы, квалифицированные имена,
// массивы (все формы рангов), указатели, комбинации в любом порядке, невалидные формы.
// Roslyn-отбор — в Cs1RoslynTypeTests.
[TestClass]
public class Cs1TypeTests
{
    // === Преопределённые типы ===

    [TestMethod]
    public void Predefined_Char_DelegateReturn_Succeeds()
    {
        // Roslyn SyntaxFacts.IsPredefinedType (SyntaxKindFacts.cs:316-340) включает char,
        // но TestKnownTypeNames его не покрывает.
        Cs1RoslynTestHelper.AssertParses("delegate char D();");
    }

    [TestMethod]
    public void Predefined_Void_DelegateReturn_Succeeds()
    {
        // Roslyn ParseTypeOrVoid (LanguageParser.cs:7541-7550) / ParseUnderlyingType:7975-7978 —
        // void принимается как PredefinedType (семантическая ошибка в не-return контекстах).
        Cs1RoslynTestHelper.AssertParses("delegate void D();");
    }

    [TestMethod]
    public void Predefined_WholeWordBoundary_Succeeds()
    {
        // `integer` — идентификатор (тип с именем integer), а не `int` + мусор:
        // WordLiteral whole-word (T1.3.1.1) не матчит префикс.
        Cs1RoslynTestHelper.AssertParses("delegate integer D();");
    }

    // === Квалифицированные имена ===

    [TestMethod]
    public void Qualified_DeepDotted_DelegateReturn_Succeeds()
    {
        // Roslyn ParseQualifiedName (LanguageParser.cs:7031-7048) — произвольная глубина `.`.
        Cs1RoslynTestHelper.AssertParses("delegate A.B.C.D D();");
    }

    [TestMethod]
    public void Qualified_Dotted_BaseList_Succeeds()
    {
        // T1.3.1 фиксировал: «Base lists with dotted names do not parse until T1.4» — теперь парсится.
        Cs1RoslynTestHelper.AssertParses("class C : A.B.C { }");
    }

    [TestMethod]
    public void Qualified_MultipleDotted_BaseList_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C : A.B, C.D { }");
    }

    // === Массивы ===

    [TestMethod]
    public void Array_Rank2_DelegateReturn_Succeeds()
    {
        // Ранг 2 (одна запятая) — Roslyn NameParsingTests покрывает ранги 1 и 3.
        Cs1RoslynTestHelper.AssertParses("delegate goo[,] D();");
    }

    [TestMethod]
    public void Array_Rank4_DelegateReturn_Succeeds()
    {
        // Обобщение: n запятых = ранг n+1 (Roslyn ParseArrayRankSpecifier).
        Cs1RoslynTestHelper.AssertParses("delegate goo[,,,] D();");
    }

    [TestMethod]
    public void Array_TriviaInRank_Succeeds()
    {
        // Пробелы внутри ранг-спецификатора — тривиум.
        Cs1RoslynTestHelper.AssertParses("delegate int[ , , ] D();");
    }

    // === Указатели ===

    [TestMethod]
    public void Pointer_SpaceBetweenStars_Succeeds()
    {
        // Roslyn UnsafeTests использует форму `int *p` (пробел между типом и `*`);
        // пробелы между самими `*` тоже легальны (каждое `*` — отдельный postfix-проход).
        Cs1RoslynTestHelper.AssertParses("delegate int * * D();");
    }

    // === Комбинации указателей и массивов в любом порядке ===

    [TestMethod]
    public void Combo_ArrayPointerArray_Succeeds()
    {
        // Чередование: массив → указатель → массив.
        Cs1RoslynTestHelper.AssertParses("delegate int[]*[] D();");
    }

    [TestMethod]
    public void Combo_PointerArrayPointer_Succeeds()
    {
        // Чередование: указатель → массив → указатель.
        Cs1RoslynTestHelper.AssertParses("delegate int*[]* D();");
    }

    [TestMethod]
    public void Combo_QualifiedPointerArray_Succeeds()
    {
        // Квалифицированное имя + указатель + массив.
        Cs1RoslynTestHelper.AssertParses("delegate N.M*[] D();");
    }

    [TestMethod]
    public void Combo_MultiDeclaration_AllContexts_Succeeds()
    {
        // Типы в return-типе, параметрах (включая ref/out), unsafe-модификаторе.
        Cs1RoslynTestHelper.AssertParses(
            "unsafe delegate int*[] D1(int[]* a, int**[,,] b);" + "\n" +
            "unsafe delegate void D2(void* p, ref char* c, out long** d);");
    }

    // === Contextual-ключевые слова как имена типов (C# 1.0) ===

    [TestMethod]
    public void Contextual_Var_AsTypeName_Succeeds()
    {
        // ОТКЛОНЕНИЕ от списка негативов задачи («var → fail (CS2)»): в C# 1.0 `var` — обычный
        // идентификатор (не входит в список ключевых слов C# 1.0), Roslyn классифицирует его как
        // contextual keyword → IdentifierToken → валидное имя типа при ЛЮБОЙ версии (включая C# 1).
        // Исключение feature-уровня (implicit local typing) в грамматике Cs1 отсутствует по
        // построению (нет локальных объявлений). Решение T1.3.1 по ReservedKeyword подтверждает:
        // `var`/`dynamic` — не reserved (docs/CSharpParserPlan-progressT1.3.1.md, раздел
        // «ReservedKeyword — C# 1.0 set (81 words)»).
        Cs1RoslynTestHelper.AssertParses("delegate var D();");
    }

    [TestMethod]
    public void Contextual_Dynamic_AsTypeName_Succeeds()
    {
        // Аналогично var: `dynamic` в C# 1.0 — обычный идентификатор (contextual keyword Roslyn,
        // SyntaxKindFacts.cs:1253-1312 → IdentifierToken). Feature `dynamic` (CS4) в грамматике
        // отсутствует по построению.
        Cs1RoslynTestHelper.AssertParses("delegate dynamic D();");
    }

    // === Невалидные формы ===

    [TestMethod]
    public void Invalid_GenericName_Fails()
    {
        // Дженерики — CS2, вне C# 1.0 (version purity).
        Cs1RoslynTestHelper.AssertFails("delegate List<int> D();");
    }

    [TestMethod]
    public void Invalid_OpenGeneric_Fails()
    {
        // Открытое generic-имя — CS2, вне C# 1.0.
        Cs1RoslynTestHelper.AssertFails("delegate goo< D();");
    }

    [TestMethod]
    public void Invalid_NullableValueType_Fails()
    {
        // Nullable value types (`T?`) — CS2, вне C# 1.0.
        Cs1RoslynTestHelper.AssertFails("delegate goo? D();");
    }

    [TestMethod]
    public void Invalid_NrtAnnotation_Fails()
    {
        // NRT-аннотации (`string?`) — CS8, вне C# 1.0.
        Cs1RoslynTestHelper.AssertFails("delegate string? D();");
    }

    [TestMethod]
    public void Invalid_AliasQualifiedName_Fails()
    {
        // Alias-имена `A::B` (extern alias) — CS2, вне C# 1.0.
        Cs1RoslynTestHelper.AssertFails("delegate goo::bar D();");
    }

    [TestMethod]
    public void Invalid_ReservedKeywordAsType_Fails()
    {
        // Reserved-ключевое слово (не из 16 преопределённых) не может быть типом:
        // TypeName = !ReservedKeyword Identifier, PredefinedType — только 16 типов.
        Cs1RoslynTestHelper.AssertFails("delegate new D();");
    }

    [TestMethod]
    public void Invalid_RankWithSize_Fails()
    {
        // Размеры в ранг-спецификаторе допускаются только в new/stackalloc (выражения),
        // в тип-контексте — нет (C# 1.0: array_type: type [ ]).
        Cs1RoslynTestHelper.AssertFails("delegate int[1] D();");
    }

    [TestMethod]
    public void Invalid_RankWithGarbage_Fails()
    {
        Cs1RoslynTestHelper.AssertFails("delegate int[;] D();");
    }

    [TestMethod]
    public void Invalid_RankUnclosed_Fails()
    {
        Cs1RoslynTestHelper.AssertFails("delegate int[ D();");
    }

    [TestMethod]
    public void Invalid_StarWithoutBaseType_Fails()
    {
        // Указатель без базового типа.
        Cs1RoslynTestHelper.AssertFails("delegate * D();");
    }

    [TestMethod]
    public void Invalid_BracketWithoutBaseType_Fails()
    {
        // Массив без базового типа.
        Cs1RoslynTestHelper.AssertFails("delegate [] D();");
    }
}
