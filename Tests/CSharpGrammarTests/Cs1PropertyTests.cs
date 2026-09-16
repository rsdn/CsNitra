using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T2.1.2: properties (class/struct + interface). Parsed from the Grammar start rule (a full
// `class C { ... }` / `struct S { ... }` / `interface I { ... }`).
//
// Concrete class/struct property: accessors REQUIRE a block body (C# 1.0 has NO auto-properties —
// `int P { get; set; }` in a class is CS3 and rejects). Accessor = ("get" | "set") Block.
// Abstract class property: accessors are semicolon-terminated (no body), requires `abstract`.
// Interface property: accessors are semicolon-terminated (no body) — valid C# 1.0.
// `value` (implicit setter parameter) is NOT a reserved keyword, so it is already a true identifier
// in expression position: `set { _x = value; }` parses with no change.
[TestClass]
public class Cs1PropertyTests
{
    // === Concrete class property (block-body accessors) ===

    [TestMethod]
    public void Property_GetSet_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { int X { get { return _x; } set { _x = value; } } int _x; }");
    }

    [TestMethod]
    public void Property_GetOnly_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { public string Name { get { return _name; } } string _name; }");
    }

    [TestMethod]
    public void Property_Static_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { static int Count { get { return _c; } set { _c = value; } } static int _c; }");
    }

    [TestMethod]
    public void Property_ThisInGetter_Succeeds()
    {
        // `this` is a Primary alternative (Cs1.grammar); `this._x` = Primary + MemberAccess postfix.
        Cs1RoslynTestHelper.AssertParses("class C { int X { get { return this._x; } set { _x = value; } } int _x; }");
    }

    [TestMethod]
    public void Property_Public_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { public int X { get { return _x; } set { _x = value; } } int _x; }");
    }

    [TestMethod]
    public void Property_Private_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { private int X { get { return _x; } set { _x = value; } } int _x; }");
    }

    [TestMethod]
    public void Property_Protected_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { protected int X { get { return _x; } set { _x = value; } } int _x; }");
    }

    [TestMethod]
    public void Property_Internal_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { internal int X { get { return _x; } set { _x = value; } } int _x; }");
    }

    [TestMethod]
    public void Property_Virtual_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { virtual int X { get { return _x; } set { _x = value; } } int _x; }");
    }

    [TestMethod]
    public void Property_Override_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { override int X { get { return _x; } set { _x = value; } } int _x; }");
    }

    [TestMethod]
    public void Property_SealedOverride_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { sealed override int X { get { return _x; } set { _x = value; } } int _x; }");
    }

    [TestMethod]
    public void Property_New_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { new int X { get { return _x; } set { _x = value; } } int _x; }");
    }

    [TestMethod]
    public void Property_Extern_Succeeds()
    {
        // extern is a C# 1.0 property modifier (parser-level; the binder rejects a body on extern).
        Cs1RoslynTestHelper.AssertParses("class C { extern int X { get { return _x; } set { _x = value; } } int _x; }");
    }

    [TestMethod]
    public void Property_MultiModifier_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { public static int X { get { return _x; } set { _x = value; } } static int _x; }");
    }

    [TestMethod]
    public void Property_OutOfOrderModifiers_Succeeds()
    {
        // Roslyn does NOT enforce property-modifier order (ParseModifiers is a generic loop;
        // SafeModifierParsingTests.Property uses any order). `static public` parses.
        Cs1RoslynTestHelper.AssertParses("class C { static public int X { get { return _x; } set { _x = value; } } int _x; }");
    }

    [TestMethod]
    public void Property_Attributed_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { [Attr] int X { get { return _x; } set { _x = value; } } int _x; }");
    }

    [TestMethod]
    public void Property_ArrayType_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { int[] X { get { return _x; } set { _x = value; } } int[] _x; }");
    }

    [TestMethod]
    public void Property_QualifiedType_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { A.B X { get { return _x; } set { _x = value; } } A.B _x; }");
    }

    [TestMethod]
    public void Property_FieldBeforeProperty_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { int _x; int X { get { return _x; } set { _x = value; } } }");
    }

    [TestMethod]
    public void Property_Struct_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("struct S { int X { get { return _x; } set { _x = value; } } int _x; }");
    }

    [TestMethod]
    public void Property_StructMultiModifier_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("struct S { public static int X { get { return _x; } } static int _x; }");
    }

    // === Abstract class property (semicolon accessors, requires `abstract`) ===

    [TestMethod]
    public void AbstractProperty_GetSet_Succeeds()
    {
        // Roslyn: an abstract property's accessors are semicolon-terminated (no body).
        Cs1RoslynTestHelper.AssertParses("abstract class A { abstract int X { get; set; } }");
    }

    [TestMethod]
    public void AbstractProperty_GetOnly_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("abstract class A { abstract int X { get; } }");
    }

    [TestMethod]
    public void AbstractProperty_WithAccessModifier_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("abstract class A { public abstract int X { get; set; } }");
    }

    [TestMethod]
    public void AbstractProperty_OutOfOrder_Succeeds()
    {
        // `abstract` before an access modifier (out-of-order) — ParseModifiers is order-agnostic.
        Cs1RoslynTestHelper.AssertParses("abstract class A { abstract public int X { get; set; } }");
    }

    [TestMethod]
    public void AbstractProperty_ProtectedAbstract_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("abstract class A { protected abstract int X { get; set; } }");
    }

    // === Interface property (semicolon accessors, no body) ===

    [TestMethod]
    public void InterfaceProperty_GetSet_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("interface I { int Value { get; set; } }");
    }

    [TestMethod]
    public void InterfaceProperty_GetOnly_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("interface I { string Name { get; } }");
    }

    [TestMethod]
    public void InterfaceProperty_MixedWithNestedType_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("interface I { int Value { get; set; } class D { } }");
    }

    [TestMethod]
    public void InterfaceProperty_Multiple_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("interface I { int Value { get; set; } string Name { get; } }");
    }

    // === Roslyn-derived ===

    [TestMethod]
    public void Roslyn_SafeModifierParsing_Property_Succeeds()
    {
        // Roslyn SafeModifierParsingTests.Property (SafeModifierParsingTests.cs:105-137) proves
        // property modifiers parse in any order (e.g. `public extern safe int P { get; }`).
        // Adapted: C# 13 `safe` replaced with C# 1.0 `static`; accessor given a block body (C# 1.0
        // concrete property).
        Cs1RoslynTestHelper.AssertParses("class C { public static extern int P { get { return _p; } set { _p = value; } } int _p; }");
    }

    [TestMethod]
    public void Roslyn_AbstractProperty_PublicAbstractGetOnly_Succeeds()
    {
        // Roslyn AccessorOverriddenOrHiddenMembersTests.cs:247 (`public abstract int P2 { get; }`).
        Cs1RoslynTestHelper.AssertParses("abstract class A { public abstract int P2 { get; } }");
    }

    [TestMethod]
    public void Roslyn_AbstractProperty_AbstractProtectedGetOnly_Succeeds()
    {
        // Roslyn SemanticErrorTests.cs:8036 (`abstract protected object P { get; }`) — `abstract`
        // before an access modifier (out-of-order) parses.
        Cs1RoslynTestHelper.AssertParses("abstract class A { abstract protected object P { get; } }");
    }

    // === Negatives ===

    [TestMethod]
    public void Property_AutoPropertyInClass_Fails()
    {
        // Auto-property in a class (CS3) — class/struct accessors require block bodies.
        Cs1RoslynTestHelper.AssertFails("class C { int P { get; set; } }");
    }

    [TestMethod]
    public void Property_AutoGetterInClass_Fails()
    {
        // Auto-getter in a class (CS3) — a class accessor requires a block body.
        Cs1RoslynTestHelper.AssertFails("class C { int X { get; } }");
    }

    [TestMethod]
    public void Property_AutoSetterInClass_Fails()
    {
        // Auto-setter in a class (CS3) — a class accessor requires a block body.
        Cs1RoslynTestHelper.AssertFails("class C { int X { set; } }");
    }

    [TestMethod]
    public void InterfaceProperty_WithBody_Fails()
    {
        // Interface accessors have no body in C# 1.0 (semicolon only).
        Cs1RoslynTestHelper.AssertFails("interface I { int X { get { return 1; } } }");
    }

    [TestMethod]
    public void Property_Generic_Fails()
    {
        // Generic property (CS2) — version purity. `int X<T> { ... }`: no rule matches the `<`
        // after the name.
        Cs1RoslynTestHelper.AssertFails("class C { int X<T> { get { return _x; } } int _x; }");
    }

    [TestMethod]
    public void Property_EmptyAccessorList_Fails()
    {
        // A property requires at least one accessor (`AccessorDeclaration+` / `InterfaceAccessor+`).
        Cs1RoslynTestHelper.AssertFails("class C { int X { } }");
    }

    [TestMethod]
    public void Property_KeywordName_Fails()
    {
        // A property name is a true identifier (TypeName = !ReservedKeyword Identifier).
        Cs1RoslynTestHelper.AssertFails("class C { int class { get { return 1; } } }");
    }

    [TestMethod]
    public void Property_AbstractWithBlockBody_Fails()
    {
        // An abstract property cannot have a block body (its accessors are semicolon-terminated);
        // a concrete property cannot be `abstract`. Neither rule matches.
        Cs1RoslynTestHelper.AssertFails("abstract class A { abstract int X { get { return 1; } } }");
    }
}
