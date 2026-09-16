using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T2.1.1: member infrastructure + Field. Parsed from the Grammar start rule (a full
// `class C { ... }` / `struct S { ... }` / `interface I { ... }`).
//
// Field = Attributes? FieldModifier* Type VariableDeclarator ("," VariableDeclarator)* ";".
// FieldModifier (C# 1.0): access + static/const/readonly/extern/new/volatile. Ordering is NOT
// enforced (Roslyn ParseModifiers is a generic loop; AccessCheckTests.cs:27 uses `static public`).
// InterfaceMember excludes Field (C# 1.0 interfaces have no fields — progressT2.1.1 D1).
[TestClass]
public class Cs1MemberTests
{
    // === Basic fields ===

    [TestMethod]
    public void Field_Basic_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { int x; }");
    }

    [TestMethod]
    public void Field_WithInitializer_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { int x = 5; }");
    }

    [TestMethod]
    public void Field_MultipleDeclaratorsWithInit_Succeeds()
    {
        // Roslyn ParseFieldDeclarationVariableDeclarators (LanguageParser.cs:5262) loops on commas.
        Cs1RoslynTestHelper.AssertParses("class C { int a = 1, b = 2; }");
    }

    [TestMethod]
    public void Field_MultipleDeclaratorsNoInit_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { int a, b; }");
    }

    // === Modifiers (each individually) ===

    [TestMethod]
    public void Field_Public_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { public int x; }");
    }

    [TestMethod]
    public void Field_Private_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { private int x; }");
    }

    [TestMethod]
    public void Field_Protected_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { protected int x; }");
    }

    [TestMethod]
    public void Field_Internal_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { internal int x; }");
    }

    [TestMethod]
    public void Field_Static_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { static int x; }");
    }

    [TestMethod]
    public void Field_Const_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { const int N = 10; }");
    }

    [TestMethod]
    public void Field_Readonly_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { readonly int x; }");
    }

    [TestMethod]
    public void Field_Extern_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { extern int x; }");
    }

    [TestMethod]
    public void Field_New_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { new int x; }");
    }

    [TestMethod]
    public void Field_Volatile_Succeeds()
    {
        // volatile is core C# 1.0 (Roslyn GetModifierExcludingScoped 1314-1315, no feature gate).
        Cs1RoslynTestHelper.AssertParses("class C { volatile int v; }");
    }

    [TestMethod]
    public void Field_MultiModifier_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { public static readonly string S = \"hi\"; }");
    }

    [TestMethod]
    public void Field_StaticConst_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { static const int N = 1; }");
    }

    [TestMethod]
    public void Field_OutOfOrderModifiers_Succeeds()
    {
        // Roslyn does NOT enforce modifier order: ParseModifiers (LanguageParser.cs:1347-1484) is a
        // generic loop; Roslyn's own test code uses `static public` (AccessCheckTests.cs:27).
        Cs1RoslynTestHelper.AssertParses("class C { static public int X; }");
    }

    // === Attributed fields ===

    [TestMethod]
    public void Field_Attributed_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { [Attr] int x; }");
    }

    [TestMethod]
    public void Field_AttributedMultiModifier_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { [Attr1, Attr2] public static int X = 5; }");
    }

    // === Field types ===

    [TestMethod]
    public void Field_ArrayType_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { int[] x; }");
    }

    [TestMethod]
    public void Field_PointerType_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { int* x; }");
    }

    [TestMethod]
    public void Field_QualifiedType_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { A.B x; }");
    }

    // === Mixed members + nested types ===

    [TestMethod]
    public void Field_MixedWithNestedType_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { int x; class D { } }");
    }

    [TestMethod]
    public void NestedClass_WithField_Succeeds()
    {
        // Nested-type recursion: a ClassDeclaration in a ClassBody is a TypeDeclaration member; its
        // own ClassBody is a ClassMember* list, so the inner field parses.
        Cs1RoslynTestHelper.AssertParses("class C { class D { int y; } }");
    }

    [TestMethod]
    public void NestedEnum_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { enum E { A, B } }");
    }

    [TestMethod]
    public void NestedDelegate_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { delegate void D(); }");
    }

    // === Struct fields ===

    [TestMethod]
    public void Struct_Field_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("struct S { int x; }");
    }

    [TestMethod]
    public void Struct_MultiModifierField_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("struct S { public static readonly int N = 1; }");
    }

    // === Interface (nested types only, no fields) ===

    [TestMethod]
    public void Interface_NestedClass_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("interface I { class D { } }");
    }

    [TestMethod]
    public void Interface_NestedEnum_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("interface I { enum E { A } }");
    }

    // === Roslyn-derived ===

    [TestMethod]
    public void Roslyn_FieldDeclaration_TwoDeclarators_Succeeds()
    {
        // Roslyn MemberDeclarationParsingTests.FieldDeclaration (`static int F1 = a, F2 = b;`) —
        // adapted: wrapped in a class.
        Cs1RoslynTestHelper.AssertParses("class C { static int F1 = a, F2 = b; }");
    }

    [TestMethod]
    public void Roslyn_StaticPublicField_Succeeds()
    {
        // Roslyn Test/Semantic/Semantics/AccessCheckTests.cs:27 (`static public int c_pub;`) —
        // proves modifier order is not enforced.
        Cs1RoslynTestHelper.AssertParses("class C { static public int c_pub; }");
    }

    // === Negatives ===

    [TestMethod]
    public void Field_MissingName_Fails()
    {
        Cs1RoslynTestHelper.AssertFails("class C { int; }");
    }

    [TestMethod]
    public void Field_MissingType_Fails()
    {
        Cs1RoslynTestHelper.AssertFails("class C { = 5; }");
    }

    [TestMethod]
    public void Interface_Field_Fails()
    {
        // C# 1.0 interfaces have no fields (progressT2.1.1 D1).
        Cs1RoslynTestHelper.AssertFails("interface I { int x; }");
    }

    [TestMethod]
    public void Property_NotMemberYet_Fails()
    {
        // Property is not a member yet (T2.1.2); `int P { get; }` matches no ClassMember.
        Cs1RoslynTestHelper.AssertFails("class C { int P { get; } }");
    }

    [TestMethod]
    public void AutoProperty_NotMemberYet_Fails()
    {
        // Auto-property (CS3) and Property not a member yet (T2.1.2).
        Cs1RoslynTestHelper.AssertFails("class C { int P { get; set; } }");
    }

    [TestMethod]
    public void Field_MissingSemicolon_Fails()
    {
        Cs1RoslynTestHelper.AssertFails("class C { int x }");
    }

    [TestMethod]
    public void Field_AsyncModifier_Fails()
    {
        // async is CS5, not a C# 1.0 field modifier.
        Cs1RoslynTestHelper.AssertFails("class C { async int x; }");
    }

    [TestMethod]
    public void Field_PartialModifier_Fails()
    {
        // partial is CS2, not a C# 1.0 field modifier.
        Cs1RoslynTestHelper.AssertFails("class C { partial int x; }");
    }

    [TestMethod]
    public void Field_VirtualModifier_Fails()
    {
        // virtual is a method/property modifier, not a field modifier.
        Cs1RoslynTestHelper.AssertFails("class C { virtual int x; }");
    }

    [TestMethod]
    public void Field_KeywordName_Fails()
    {
        // A field name cannot be a reserved keyword.
        Cs1RoslynTestHelper.AssertFails("struct S { int class; }");
    }
}
