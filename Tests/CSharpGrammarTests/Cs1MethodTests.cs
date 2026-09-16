using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T2.1.3: methods, constructors, destructors (class/struct) + interface methods. Parsed from the
// Grammar start rule (a full `class C { ... }` / `struct S { ... }` / `interface I { ... }`).
//
// Method = Attributes? MethodModifier* Type TypeName "(" ParameterList? ")" MethodBody
//   where MethodBody = Block | ";" (a concrete `void Foo();` is a binder error, not a parse error).
// Constructor = Attributes? ConstructorModifier* TypeName "(" ParameterList? ")" ConstructorInitializer? Block
//   where ConstructorInitializer = ":" (base | this) ArgumentList (parens required, even when empty).
// Destructor = Attributes? MethodModifier* "~" TypeName "(" ")" Block
//   (C# destructors DO have empty parentheses — `~C() { }` is valid; `~C { }` is NOT — Roslyn
//   ParseDestructorDeclaration 3572-3587, DeclarationParsingTests.cs:3616).
// InterfaceMethod = Attributes? MethodModifier* Type TypeName "(" ParameterList? ")" ";"
//   (C# 1.0 interface methods have NO body; default interface methods are CS8).
//
// Disambiguation (longest-match, no ties):
//   Method vs Constructor: `Type TypeName (` (two names) vs `TypeName (` (one name).
//   Method vs Field: after `Type Name`, `(` -> Method, `;`/`=`/`,` -> Field.
// `base` in expressions: `BaseMember = "base" "." !ReservedKeyword Identifier` (mirrors
// PredefinedMember, T2.3.3) so `base.Foo()` in a body parses.
[TestClass]
public class Cs1MethodTests
{
    // === Methods (basic) ===

    [TestMethod]
    public void Method_VoidNoParams_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { void Foo() { } }");
    }

    [TestMethod]
    public void Method_WithParamsAndReturn_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { int Add(int a, int b) { return a + b; } }");
    }

    [TestMethod]
    public void Method_ReturnStatement_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { void Foo() { return; } }");
    }

    [TestMethod]
    public void Method_StaticMain_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { static void Main() { } }");
    }

    [TestMethod]
    public void Method_PublicOverride_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { public override int GetHashCode() { return 0; } }");
    }

    // === Methods (modifiers) ===

    [TestMethod]
    public void Method_Virtual_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { virtual int Foo() { return 0; } }");
    }

    [TestMethod]
    public void Method_SealedOverride_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { sealed override int Foo() { return 0; } }");
    }

    [TestMethod]
    public void Method_New_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { new int Foo() { return 0; } }");
    }

    [TestMethod]
    public void Method_Unsafe_Succeeds()
    {
        // unsafe is core C# 1.0 (no feature gate; pointer types since T1.4).
        Cs1RoslynTestHelper.AssertParses("class C { unsafe void Foo() { } }");
    }

    [TestMethod]
    public void Method_Attributed_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { [Attr] void Foo() { } }");
    }

    [TestMethod]
    public void Method_MultiModifier_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { public static void Foo() { } }");
    }

    [TestMethod]
    public void Method_OutOfOrderModifiers_Succeeds()
    {
        // Roslyn does NOT enforce method-modifier order (ParseModifiers is a generic loop).
        Cs1RoslynTestHelper.AssertParses("class C { static public void Foo() { } }");
    }

    // === Methods (return types / parameters) ===

    [TestMethod]
    public void Method_ArrayReturnType_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { int[] Foo() { return null; } }");
    }

    [TestMethod]
    public void Method_QualifiedReturnType_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { A.B Foo() { return null; } }");
    }

    [TestMethod]
    public void Method_PointerReturnType_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { unsafe int* Foo() { return null; } }");
    }

    [TestMethod]
    public void Method_ParamsArray_Succeeds()
    {
        // params is C# 1.0 (ParameterModifier already includes it).
        Cs1RoslynTestHelper.AssertParses("class C { void Foo(params int[] xs) { } }");
    }

    [TestMethod]
    public void Method_RefOutParams_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { void Foo(ref int a, out int b) { b = 0; } }");
    }

    // === Methods (bodies: this / base) ===

    [TestMethod]
    public void Method_ThisInBody_Succeeds()
    {
        // `this` is a Primary alternative; `this._x` = Primary + MemberAccess postfix.
        Cs1RoslynTestHelper.AssertParses("class C { int _x; void M() { return this._x; } }");
    }

    [TestMethod]
    public void Method_BaseCallInBody_Succeeds()
    {
        // `base` is reserved with no Primary alternative -> BaseMember (T2.1.3 D2) makes `base.Foo()`
        // parse. Roslyn: `base` is only valid as `base.Member`.
        Cs1RoslynTestHelper.AssertParses("class C : B { void M() { base.Foo(); } }");
    }

    // === Methods (struct) ===

    [TestMethod]
    public void Method_Struct_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("struct S { void Foo() { } }");
    }

    // === Method vs Field disambiguation ===

    [TestMethod]
    public void MethodAndField_Mixed_Succeeds()
    {
        // `int x = 5;` is a Field (name followed by "="); `int Foo() { ... }` is a Method (name
        // followed by "("). Both parse in the same body without cross-matching.
        Cs1RoslynTestHelper.AssertParses("class C { int x = 5; int Foo() { return x; } }");
    }

    // === Method vs Constructor disambiguation (task examples) ===

    [TestMethod]
    public void Method_IntReturnIsMethod_Succeeds()
    {
        // `int Foo() { }` is a METHOD (Type=int, Name=Foo); a Constructor fails because `int` is a
        // reserved keyword, not a `TypeName` (!ReservedKeyword Identifier).
        Cs1RoslynTestHelper.AssertParses("class C { int Foo() { return 0; } }");
    }

    [TestMethod]
    public void Method_QualifiedReturnIsMethod_Succeeds()
    {
        // `Point GetPoint() { }` is a METHOD (Type=Point, Name=GetPoint); a Constructor fails because
        // the name `Point` is followed by another name `GetPoint`, not by "(".
        Cs1RoslynTestHelper.AssertParses("class C { Point GetPoint() { return null; } }");
    }

    [TestMethod]
    public void Constructor_SingleNameIsConstructor_Succeeds()
    {
        // `Foo() { }` (a single name followed by "(") is a CONSTRUCTOR (Name=Foo); a Method fails
        // because it needs a return Type + a second name before "(" (only one name is present).
        // The name differing from the class name is a binder concern, not a parse concern.
        Cs1RoslynTestHelper.AssertParses("class C { Foo() { } }");
    }

    // === Constructors ===

    [TestMethod]
    public void Constructor_NoParams_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { C() { } }");
    }

    [TestMethod]
    public void Constructor_WithParams_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { C(int x, int y) { } }");
    }

    [TestMethod]
    public void Constructor_BaseInitializer_Succeeds()
    {
        // `: base()` — the argument list parens are required (even when empty).
        Cs1RoslynTestHelper.AssertParses("class C { C(int x) : base() { } }");
    }

    [TestMethod]
    public void Constructor_ThisInitializer_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { C(int x) : this(x) { } }");
    }

    [TestMethod]
    public void Constructor_InitializerWithArgs_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { C(int x, int y) : this(x, y) { } }");
    }

    [TestMethod]
    public void Constructor_Static_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { static C() { } }");
    }

    [TestMethod]
    public void Constructor_WithAccessModifier_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { public C() { } }");
    }

    [TestMethod]
    public void Constructor_NewModifier_Succeeds()
    {
        // `new` is a valid C# 1.0 constructor modifier (hides a base constructor).
        Cs1RoslynTestHelper.AssertParses("class C { new C() { } }");
    }

    [TestMethod]
    public void Constructor_Attributed_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { [Attr] C() { } }");
    }

    [TestMethod]
    public void Constructor_MixedWithField_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { int _x; C() { } }");
    }

    [TestMethod]
    public void Constructor_Struct_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("struct S { S() { } }");
    }

    // === Destructors (empty parens REQUIRED — Roslyn) ===

    [TestMethod]
    public void Destructor_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { ~C() { } }");
    }

    [TestMethod]
    public void Destructor_Unsafe_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { unsafe ~C() { } }");
    }

    [TestMethod]
    public void Destructor_Attributed_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { [Attr] ~C() { } }");
    }

    [TestMethod]
    public void Destructor_WithBody_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { ~C() { GC.SuppressFinalize(this); } }");
    }

    // === Interface methods (semicolon, no body) ===

    [TestMethod]
    public void InterfaceMethod_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("interface I { void Foo(); }");
    }

    [TestMethod]
    public void InterfaceMethod_WithParams_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("interface I { int Add(int a, int b); }");
    }

    [TestMethod]
    public void InterfaceMethod_Multiple_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("interface I { void Foo(); int Bar(); }");
    }

    [TestMethod]
    public void InterfaceMethod_MixedWithNestedType_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("interface I { void Foo(); class D { } }");
    }

    // === Abstract / extern methods (semicolon body) ===

    [TestMethod]
    public void AbstractMethod_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("abstract class A { abstract int Foo(); }");
    }

    [TestMethod]
    public void ExternMethod_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { extern void Foo(); }");
    }

    // === Roslyn-derived ===

    [TestMethod]
    public void Roslyn_Destructor_WithParens_Succeeds()
    {
        // Roslyn DeclarationParsingTests.cs:3616 (`"class a { ~a() { } }"`) — destructors have empty
        // parentheses (ParseDestructorDeclaration 3578-3581 eats "(" and ")").
        Cs1RoslynTestHelper.AssertParses("class a { ~a() { } }");
    }

    [TestMethod]
    public void Roslyn_SafeModifierParsing_Method_Succeeds()
    {
        // Roslyn SafeModifierParsingTests.Method (SafeModifierParsingTests.cs:14-43) proves method
        // modifiers parse in any order (e.g. `public extern safe void M();`). Adapted: C# 13 `safe`
        // replaced with C# 1.0 `static`; semicolon body (abstract/extern form).
        Cs1RoslynTestHelper.AssertParses("class C { public static extern void M(); }");
    }

    [TestMethod]
    public void Roslyn_StaticConstructorWithThisCall_Succeeds()
    {
        // Roslyn ParserErrorMessageTests.cs:986 (CS0514: static constructor cannot have an explicit
        // 'this'/'base' call) — the PARSER accepts `static C() : this() { }` (a constructor with a
        // this-initializer); it is a BINDER error. Verifies the parser-vs-binder split.
        Cs1RoslynTestHelper.AssertParses("class C { static C() : this() { } }");
    }

    // === Negatives ===

    [TestMethod]
    public void Destructor_NoParens_Fails()
    {
        // C# destructors REQUIRE empty parentheses (Roslyn ParseDestructorDeclaration 3578-3581).
        // `~C { }` (no parens) is invalid — the task's premise was corrected by Roslyn (D1).
        Cs1RoslynTestHelper.AssertFails("class C { ~C { } }");
    }

    [TestMethod]
    public void Method_ExpressionBodied_Fails()
    {
        // Expression-bodied member (CS6, not C# 1.0). `int P => _x;`: no rule matches the `=>`
        // after the name (Method needs "(", Property needs "{", Field needs ";"/"=").
        Cs1RoslynTestHelper.AssertFails("class C { int P => _x; }");
    }

    [TestMethod]
    public void Method_AsyncModifier_Fails()
    {
        // async is CS5, not a C# 1.0 method modifier. `async void Foo() { }`: `async` is not in
        // MethodModifier and not reserved, so it is parsed as a Type name; then `void` (reserved)
        // cannot be the method name -> no rule matches.
        Cs1RoslynTestHelper.AssertFails("class C { async void Foo() { } }");
    }

    [TestMethod]
    public void Method_MissingBody_Fails()
    {
        // A method requires a body (Block) or a semicolon: `void Foo() }` has neither.
        Cs1RoslynTestHelper.AssertFails("class C { void Foo() }");
    }

    [TestMethod]
    public void Method_Generic_Fails()
    {
        // Generic method (CS2, version purity). `void Foo<T>() { }`: no rule matches the `<`
        // after the name.
        Cs1RoslynTestHelper.AssertFails("class C { void Foo<T>() { } }");
    }

    [TestMethod]
    public void Method_PartialModifier_Fails()
    {
        // partial is CS2, not a C# 1.0 method modifier.
        Cs1RoslynTestHelper.AssertFails("class C { partial void Foo() { } }");
    }

    [TestMethod]
    public void InterfaceMethod_WithBody_Fails()
    {
        // C# 1.0 interface methods have no body (semicolon only; default interface methods are CS8).
        Cs1RoslynTestHelper.AssertFails("interface I { void Foo() { } }");
    }

    [TestMethod]
    public void Constructor_MissingBody_Fails()
    {
        // A constructor requires a block body: `C() }` has none.
        Cs1RoslynTestHelper.AssertFails("class C { C() }");
    }

    [TestMethod]
    public void Constructor_BaseNoParens_Fails()
    {
        // A constructor initializer requires a (possibly empty) argument list: `: base { }` (no
        // parens) is invalid (Roslyn ParseConstructorInitializer 3556-3561 eats the parens).
        Cs1RoslynTestHelper.AssertFails("class C { C() : base { } }");
    }

    [TestMethod]
    public void Method_ReservedName_Fails()
    {
        // A method name is a true identifier (TypeName = !ReservedKeyword Identifier); `void` is
        // reserved.
        Cs1RoslynTestHelper.AssertFails("class C { void void() { } }");
    }
}
