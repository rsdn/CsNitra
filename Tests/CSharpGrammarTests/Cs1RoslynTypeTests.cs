using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// Тесты типов C# 1.0, отобранные из Roslyn (методология T1.3.2 —
// docs/CSharpParserPlan-progressT1.3.2.md, разделы «Selection criteria» и «Roslyn repo map»).
// Критерии: POSITIVE = Roslyn фиксирует 0 parse-ошибок для формы (семантические ошибки допустимы);
// NEGATIVE = Roslyn фиксирует parse-ошибку в скоупе Cs1. Адаптации (контекст ParseTypeName/поле/
// локальный → заголовок делегата/параметр/enum-база) документированы в комментариях.
[TestClass]
public class Cs1RoslynTypeTests
{
    // === src/Compilers/CSharp/Test/Syntax/Parsing/NameParsingTests.cs ===

    [TestMethod]
    public void Roslyn_NameParsingTestBasicTypeName()
    {
        // Roslyn: NameParsingTests.cs TestBasicTypeName — ParseTypeName("goo"), 0 ошибок.
        // Адаптация: ParseTypeName-контекст → return-тип делегата.
        Cs1RoslynTestHelper.AssertParses("delegate goo D();");
    }

    [TestMethod]
    public void Roslyn_NameParsingTestDottedTypeName()
    {
        // Roslyn: NameParsingTests.cs TestDottedTypeName — ParseTypeName("goo.bar"), 0 ошибок.
        Cs1RoslynTestHelper.AssertParses("delegate goo.bar D();");
    }

    [TestMethod]
    public void Roslyn_NameParsingTestKnownTypeNames_Bool()
    {
        // Roslyn: NameParsingTests.cs TestKnownTypeNames — ParseKnownTypeName(BoolKeyword), 0 ошибок.
        Cs1RoslynTestHelper.AssertParses("delegate bool D();");
    }

    [TestMethod]
    public void Roslyn_NameParsingTestKnownTypeNames_Byte()
    {
        // Roslyn: NameParsingTests.cs TestKnownTypeNames — ParseKnownTypeName(ByteKeyword), 0 ошибок.
        Cs1RoslynTestHelper.AssertParses("delegate byte D();");
    }

    [TestMethod]
    public void Roslyn_NameParsingTestKnownTypeNames_SByte()
    {
        // Roslyn: NameParsingTests.cs TestKnownTypeNames — ParseKnownTypeName(SByteKeyword), 0 ошибок.
        Cs1RoslynTestHelper.AssertParses("delegate sbyte D();");
    }

    [TestMethod]
    public void Roslyn_NameParsingTestKnownTypeNames_Short()
    {
        // Roslyn: NameParsingTests.cs TestKnownTypeNames — ParseKnownTypeName(ShortKeyword), 0 ошибок.
        Cs1RoslynTestHelper.AssertParses("delegate short D();");
    }

    [TestMethod]
    public void Roslyn_NameParsingTestKnownTypeNames_UShort()
    {
        // Roslyn: NameParsingTests.cs TestKnownTypeNames — ParseKnownTypeName(UShortKeyword), 0 ошибок.
        Cs1RoslynTestHelper.AssertParses("delegate ushort D();");
    }

    [TestMethod]
    public void Roslyn_NameParsingTestKnownTypeNames_Int()
    {
        // Roslyn: NameParsingTests.cs TestKnownTypeNames — ParseKnownTypeName(IntKeyword), 0 ошибок.
        Cs1RoslynTestHelper.AssertParses("delegate int D();");
    }

    [TestMethod]
    public void Roslyn_NameParsingTestKnownTypeNames_UInt()
    {
        // Roslyn: NameParsingTests.cs TestKnownTypeNames — ParseKnownTypeName(UIntKeyword), 0 ошибок.
        Cs1RoslynTestHelper.AssertParses("delegate uint D();");
    }

    [TestMethod]
    public void Roslyn_NameParsingTestKnownTypeNames_Long()
    {
        // Roslyn: NameParsingTests.cs TestKnownTypeNames — ParseKnownTypeName(LongKeyword), 0 ошибок.
        Cs1RoslynTestHelper.AssertParses("delegate long D();");
    }

    [TestMethod]
    public void Roslyn_NameParsingTestKnownTypeNames_ULong()
    {
        // Roslyn: NameParsingTests.cs TestKnownTypeNames — ParseKnownTypeName(ULongKeyword), 0 ошибок.
        Cs1RoslynTestHelper.AssertParses("delegate ulong D();");
    }

    [TestMethod]
    public void Roslyn_NameParsingTestKnownTypeNames_Float()
    {
        // Roslyn: NameParsingTests.cs TestKnownTypeNames — ParseKnownTypeName(FloatKeyword), 0 ошибок.
        Cs1RoslynTestHelper.AssertParses("delegate float D();");
    }

    [TestMethod]
    public void Roslyn_NameParsingTestKnownTypeNames_Double()
    {
        // Roslyn: NameParsingTests.cs TestKnownTypeNames — ParseKnownTypeName(DoubleKeyword), 0 ошибок.
        Cs1RoslynTestHelper.AssertParses("delegate double D();");
    }

    [TestMethod]
    public void Roslyn_NameParsingTestKnownTypeNames_Decimal()
    {
        // Roslyn: NameParsingTests.cs TestKnownTypeNames — ParseKnownTypeName(DecimalKeyword), 0 ошибок.
        Cs1RoslynTestHelper.AssertParses("delegate decimal D();");
    }

    [TestMethod]
    public void Roslyn_NameParsingTestKnownTypeNames_String()
    {
        // Roslyn: NameParsingTests.cs TestKnownTypeNames — ParseKnownTypeName(StringKeyword), 0 ошибок.
        Cs1RoslynTestHelper.AssertParses("delegate string D();");
    }

    [TestMethod]
    public void Roslyn_NameParsingTestKnownTypeNames_Object()
    {
        // Roslyn: NameParsingTests.cs TestKnownTypeNames — ParseKnownTypeName(ObjectKeyword), 0 ошибок.
        Cs1RoslynTestHelper.AssertParses("delegate object D();");
    }

    [TestMethod]
    public void Roslyn_NameParsingTestPointerTypeName()
    {
        // Roslyn: NameParsingTests.cs TestPointerTypeName — ParseTypeName("goo*"), 0 ошибок.
        Cs1RoslynTestHelper.AssertParses("delegate goo* D();");
    }

    [TestMethod]
    public void Roslyn_NameParsingTestPointerTypeNameWithMultipleAsterisks()
    {
        // Roslyn: NameParsingTests.cs TestPointerTypeNameWithMultipleAsterisks — ParseTypeName("goo***"),
        // 0 ошибок, глубина указателей 3.
        Cs1RoslynTestHelper.AssertParses("delegate goo*** D();");
    }

    [TestMethod]
    public void Roslyn_NameParsingTestArrayTypeName()
    {
        // Roslyn: NameParsingTests.cs TestArrayTypeName — ParseTypeName("goo[]"), 0 ошибок, ранг 1.
        Cs1RoslynTestHelper.AssertParses("delegate goo[] D();");
    }

    [TestMethod]
    public void Roslyn_NameParsingTestMultiDimensionalArrayTypeName()
    {
        // Roslyn: NameParsingTests.cs TestMultiDimensionalArrayTypeName — ParseTypeName("goo[,,]"),
        // 0 ошибок, ранг 3.
        Cs1RoslynTestHelper.AssertParses("delegate goo[,,] D();");
    }

    [TestMethod]
    public void Roslyn_NameParsingTestMultiRankedArrayTypeName()
    {
        // Roslyn: NameParsingTests.cs TestMultiRankedArrayTypeName — ParseTypeName("goo[][,][,,]"),
        // 0 ошибок, 3 ранг-спецификатора (ранги 1, 2, 3).
        Cs1RoslynTestHelper.AssertParses("delegate goo[][,][,,] D();");
    }

    [TestMethod]
    public void Roslyn_NameParsingTestMissingNameDueToKeyword()
    {
        // Roslyn: NameParsingTests.cs TestMissingNameDueToKeyword — ParseName("class") →
        // ERR_UnexpectedToken + ERR_IdentifierExpected. Ключевое слово не может быть именем типа.
        Cs1RoslynTestHelper.AssertFails("delegate class D();");
    }

    // === src/Compilers/CSharp/Test/Syntax/Parsing/TypeArgumentListParsingTests.cs ===
    // Контекст Roslyn — generic-аргументы (CS2+); отбирается только ФОРМА типа, var/generics
    // убраны, форма помещена в параметр делегата (критерий 4 — адаптация).

    [TestMethod]
    public void Roslyn_TypeArgumentListTestPredefinedType()
    {
        // Roslyn: TypeArgumentListParsingTests.cs TestPredefinedType — форма `string` в тип-позиции,
        // 0 parse-ошибок (PredefinedType/StringKeyword).
        // Адаптация: generic-контекст убран, `string` → параметр делегата.
        Cs1RoslynTestHelper.AssertParses("delegate void D(string s);");
    }

    [TestMethod]
    public void Roslyn_TypeArgumentListTestArrayType()
    {
        // Roslyn: TypeArgumentListParsingTests.cs TestArrayType — форма `X[]` в тип-позиции,
        // 0 parse-ошибок (ArrayType + ArrayRankSpecifier ранга 1).
        // Адаптация: generic-контекст убран, `X[]` → параметр делегата.
        Cs1RoslynTestHelper.AssertParses("delegate void D(X[] a);");
    }

    [TestMethod]
    public void Roslyn_TypeArgumentListTestPredefinedPointerType()
    {
        // Roslyn: TypeArgumentListParsingTests.cs TestPredefinedPointerType — форма `int*` в
        // тип-позиции, 0 parse-ошибок (PointerType + PredefinedType/IntKeyword).
        // Адаптация: generic-контекст убран, `int*` → параметр делегата.
        Cs1RoslynTestHelper.AssertParses("delegate void D(int* p);");
    }

    // === src/Compilers/CSharp/Test/Symbol/Symbols/Source/EnumTests.cs ===

    [TestMethod]
    public void Roslyn_EnumTestFullNameForEnumBaseType()
    {
        // Roslyn: EnumTests.cs TestFullNameForEnumBaseType — 16 enum-объявлений (8 целочисленных
        // ключевых типов + 8 квалифицированных System.X), VerifyDiagnostics() без ошибок.
        Cs1RoslynTestHelper.AssertParses(
            "public enum Works1 : byte { }" + "\n" +
            "public enum Works2 : sbyte { }" + "\n" +
            "public enum Works3 : short { }" + "\n" +
            "public enum Works4 : ushort { }" + "\n" +
            "public enum Works5 : int { }" + "\n" +
            "public enum Works6 : uint { }" + "\n" +
            "public enum Works7 : long { }" + "\n" +
            "public enum Works8 : ulong { }" + "\n" +
            "public enum Breaks1 : System.Byte { }" + "\n" +
            "public enum Breaks2 : System.SByte { }" + "\n" +
            "public enum Breaks3 : System.Int16 { }" + "\n" +
            "public enum Breaks4 : System.UInt16 { }" + "\n" +
            "public enum Breaks5 : System.Int32 { }" + "\n" +
            "public enum Breaks6 : System.UInt32 { }" + "\n" +
            "public enum Breaks7 : System.Int64 { }" + "\n" +
            "public enum Breaks8 : System.UInt64 { }");
    }

    [TestMethod]
    public void Roslyn_EnumTestBadEnumBaseType()
    {
        // Roslyn: EnumTests.cs TestBadEnumBaseType — enum-базы `string` / `System.String`:
        // parse чистый, только семантическая CS1008 (ERR_IntegralTypeExpected).
        Cs1RoslynTestHelper.AssertParses(
            "public enum Breaks1 : string { }" + "\n" +
            "public enum Breaks2 : System.String { }");
    }

    [TestMethod]
    public void Roslyn_EnumTestInvalidEnumUnderlyingType_Array()
    {
        // Roslyn: EnumTests.cs InvalidEnumUnderlyingType — `enum E1 : int[] { }`: parse чистый,
        // только семантическая CS1008. Адаптация: из 4 объявлений взято E1 (E3 `dynamic` — CS4,
        // E4 `T` — generic-параметр CS2, вне скоупа).
        Cs1RoslynTestHelper.AssertParses("enum E1 : int[] { }");
    }

    [TestMethod]
    public void Roslyn_EnumTestInvalidEnumUnderlyingType_Pointer()
    {
        // Roslyn: EnumTests.cs InvalidEnumUnderlyingType — `enum E2 : int* { }`: parse чистый,
        // семантические CS0214 (unsafe) + CS1008.
        Cs1RoslynTestHelper.AssertParses("enum E2 : int* { }");
    }

    // === src/Compilers/CSharp/Test/Semantic/Semantics/UnsafeTests.cs (TypeIsUnsafe) ===
    // Roslyn: поля f0-f7 класса; parse чистый (семантические CS0306 только для generic-аргументов
    // f4-f7). Адаптация: поле → return-тип делегата; f0 (`int*`) покрыт
    // Roslyn_TypeArgumentListTestPredefinedPointerType, f4-f7 — generics (CS2), исключены.

    [TestMethod]
    public void Roslyn_UnsafeTypeIsUnsafe_DoublePointer()
    {
        // Roslyn: UnsafeTests.cs TypeIsUnsafe — поле `int** f1;`, 0 parse-ошибок.
        Cs1RoslynTestHelper.AssertParses("delegate int** D();");
    }

    [TestMethod]
    public void Roslyn_UnsafeTypeIsUnsafe_PointerArray()
    {
        // Roslyn: UnsafeTests.cs TypeIsUnsafe — поле `int*[] f2;`, 0 parse-ошибок.
        Cs1RoslynTestHelper.AssertParses("delegate int*[] D();");
    }

    [TestMethod]
    public void Roslyn_UnsafeTypeIsUnsafe_PointerJaggedArray()
    {
        // Roslyn: UnsafeTests.cs TypeIsUnsafe — поле `int*[][] f3;`, 0 parse-ошибок.
        Cs1RoslynTestHelper.AssertParses("delegate int*[][] D();");
    }

    [TestMethod]
    public void Roslyn_UnsafeTests_ClassPointerParam()
    {
        // Roslyn: UnsafeTests.cs:12443 — `unsafe static public implicit operator long*(C* i)`:
        // параметр `C*` (указатель на класс-тип), 0 parse-ошибок.
        // Адаптация: параметр конверсии → параметр/return делегата.
        Cs1RoslynTestHelper.AssertParses("delegate long D(C* i);");
    }

    // === src/Compilers/CSharp/Test/Emit2/Emit/ManagedAddressTests.cs ===

    [TestMethod]
    public void Roslyn_ManagedAddressTests_ArrayPointer()
    {
        // Roslyn: ManagedAddressTests.cs:27,34,36 — `int[]* xp = &x;`, `private int[]* _x;`,
        // `public C(int[]* x)` — указатель на массив, 0 parse-ошибок (семантический WRN_ManagedAddr).
        // Адаптация: поле/локальный → return-тип делегата.
        Cs1RoslynTestHelper.AssertParses("delegate int[]* D();");
    }

    // === src/Compilers/CSharp/Test/Emit2/CodeGen/EditAndContinue/LocalSlotMappingTests.cs ===

    [TestMethod]
    public void Roslyn_LocalSlotMappingTests_PointerTripleArray3D()
    {
        // Roslyn: LocalSlotMappingTests.cs:5043 — локальный `...E***[,,] x` (три указателя +
        // ранг 3), 0 parse-ошибок. Форма C# 1.0-совместима; контекст (generic-enum) адаптирован.
        Cs1RoslynTestHelper.AssertParses("delegate E***[,,] D();");
    }

    // === src/Compilers/CSharp/Test/Emit/Emit/EntryPointTests.cs ===

    [TestMethod]
    public void Roslyn_EntryPointTests_Rank2ArrayParam()
    {
        // Roslyn: EntryPointTests.cs:69 — `public static void Main(string[,] goo)`, 0 parse-ошибок.
        // Адаптация: Main-параметр → параметр делегата.
        Cs1RoslynTestHelper.AssertParses("delegate void D(string[,] goo);");
    }

    // === src/Compilers/CSharp/Test/Semantic/Semantics/BindingAsyncTasklikeMoreTests.cs ===

    [TestMethod]
    public void Roslyn_BindingAsyncTasklikeMoreTests_VoidPointerParam()
    {
        // Roslyn: BindingAsyncTasklikeMoreTests.cs:1111 — `unsafe public static void F(void* p)`,
        // 0 parse-ошибок. Адаптация: async-метод → делегат.
        Cs1RoslynTestHelper.AssertParses("delegate void D(void* p);");
    }

    // === src/Compilers/CSharp/Test/Semantic/Semantics/DelegateTypeTests.cs ===

    [TestMethod]
    public void Roslyn_DelegateTypeTests_BytePointerArrayParam()
    {
        // Roslyn: DelegateTypeTests.cs:11853 — `static void F2(byte*[] a)`, 0 parse-ошибок.
        Cs1RoslynTestHelper.AssertParses("delegate void D(byte*[] a);");
    }
}
