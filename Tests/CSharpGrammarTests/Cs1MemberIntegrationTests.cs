using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T2.1.5: FINALIZATION of the C# 1.0 type-member grammar. Integration, nested types, base lists,
// version-purity, and full verification (NOT new member kinds — those are T2.1.1–T2.1.4). Parsed
// from the Grammar start rule (a full `class C { ... }` / `struct S { ... }` / `interface I { ... }`).
//
// Task 1 — nested types as members: `TypeDeclaration` is already in the `ClassMember`/`StructMember`/
//   `InterfaceMember` unions (T2.1.1). A nested `ClassDeclaration`/`StructDeclaration`/... is a
//   `TypeDeclaration` member whose own body is a member-list, so nesting is naturally recursive and a
//   nested type can contain ANY member kind (field/property/method/constructor/destructor/operator/
//   indexer/event + deeper nested types).
// Task 2 — base lists: `BaseList = ":" (Type; ",")+` (T1.3) = Roslyn ParseBaseList (LanguageParser.cs:
//   2170-2232): `:` + at least one base Type + (`,` Type)*. Works with the new member bodies.
// Task 3 — version-purity: C# 1.0 only. CS2+ feature forms (generics, `int?`, `async`/`await`,
//   expression-bodied members, class auto-properties, object/collection initializers, lambdas) must
//   REJECT. `var`/`dynamic` as TYPE NAMES parse (correct C# 1.0 — they are identifiers); the CS3/CS4
//   FEATURE forms are syntactically indistinguishable from a type-name declaration, so they parse too
//   (boundary decision, see progressT2.1.5).
[TestClass]
public class Cs1MemberIntegrationTests
{
    // === Task 1: nested types as members (all member kinds inside nested types) ===

    [TestMethod]
    public void NestedClass_WithPropertyAndMethod_Succeeds()
    {
        // A nested class containing a property (block accessors) and a method.
        Cs1RoslynTestHelper.AssertParses("class Outer { class Inner { int X { get { return 0; } set { } } void M() { } } }");
    }

    [TestMethod]
    public void Nested_Struct_Enum_Interface_Delegate_Succeeds()
    {
        // A class nesting a struct (with a field), an enum, an interface (with a method), and a delegate.
        Cs1RoslynTestHelper.AssertParses("class Outer { struct S { int F; } enum E { A, B } interface I { void M(); } delegate void D(); }");
    }

    [TestMethod]
    public void Nested_ThreeLevels_Succeeds()
    {
        // 3-level nesting: A > B > C, with a field at the innermost level.
        Cs1RoslynTestHelper.AssertParses("class A { class B { class C { int x; } } }");
    }

    [TestMethod]
    public void NestedClass_WithOperatorIndexerEvent_Succeeds()
    {
        // A nested type that itself has an operator, an indexer, and an event (T2.1.4 kinds inside nesting).
        Cs1RoslynTestHelper.AssertParses(
            "class Outer { class Inner { public static Inner operator +(Inner a, Inner b) { return a; }"
            + " public int this[int i] { get { return 0; } set { } } public event EventHandler Changed; } }");
    }

    [TestMethod]
    public void NestedClass_WithAllMemberKinds_Succeeds()
    {
        // A nested class with field + property + method + constructor + operator + indexer + event.
        Cs1RoslynTestHelper.AssertParses(
            "class Outer { class Inner { int f; int P { get { return 0; } set { } } void M() { }"
            + " Inner() { } public static Inner operator +(Inner a, Inner b) { return a; }"
            + " public int this[int i] { get { return 0; } } public event EventHandler Changed; } }");
    }

    [TestMethod]
    public void NestedStruct_WithMembers_Succeeds()
    {
        // A struct nested in a struct, with a field and a method.
        Cs1RoslynTestHelper.AssertParses("struct Outer { struct Inner { int F; void M() { } } }");
    }

    [TestMethod]
    public void NestedInterface_WithMembers_Succeeds()
    {
        // An interface nested in an interface, with a method.
        Cs1RoslynTestHelper.AssertParses("interface Outer { interface Inner { void M(); } }");
    }

    [TestMethod]
    public void NestedClass_WithBaseList_Succeeds()
    {
        // A nested type that itself has a base list (task 1 + task 2 combined).
        Cs1RoslynTestHelper.AssertParses("class Outer { class Inner : Base { int x; } }");
    }

    [TestMethod]
    public void NestedType_InStructWithMembers_Succeeds()
    {
        // A class nested in a struct that also has its own field (member + nested type in the same body).
        Cs1RoslynTestHelper.AssertParses("struct Outer { int F; class Inner { int x; } }");
    }

    // === Task 1: Roslyn-derived nested-type cases ===

    [TestMethod]
    public void Roslyn_NestedClass_Succeeds()
    {
        // Roslyn DeclarationParsingTests.TestNestedClass (DeclarationParsingTests.cs:1705).
        Cs1RoslynTestHelper.AssertParses("class a { class b { } }");
    }

    [TestMethod]
    public void Roslyn_NestedPrivateClass_Succeeds()
    {
        // Roslyn DeclarationParsingTests.TestNestedPrivateClass (DeclarationParsingTests.cs:1745).
        Cs1RoslynTestHelper.AssertParses("class a { private class b { } }");
    }

    [TestMethod]
    public void Roslyn_NestedPublicClass_Succeeds()
    {
        // Roslyn DeclarationParsingTests.TestNestedPublicClass (DeclarationParsingTests.cs:1911).
        Cs1RoslynTestHelper.AssertParses("class a { public class b { } }");
    }

    [TestMethod]
    public void Roslyn_NestedDelegate_Succeeds()
    {
        // Roslyn DeclarationParsingTests.TestNestedDelegate (DeclarationParsingTests.cs:2431):
        // a nested delegate with a qualified return type and an empty parameter list.
        Cs1RoslynTestHelper.AssertParses("class a { delegate b c(); }");
    }

    // === Task 2: base lists (BaseList = ":" (Type; ",")+; Roslyn ParseBaseList 2170-2232) ===

    [TestMethod]
    public void BaseList_SingleBase_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C : Base1 { }");
    }

    [TestMethod]
    public void BaseList_MultipleBases_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C : Base1, Base2 { }");
    }

    [TestMethod]
    public void BaseList_QualifiedBases_Succeeds()
    {
        // Qualified base types (System.Object, IDisposable) — each is a QualifiedName Type.
        Cs1RoslynTestHelper.AssertParses("class C : System.Object, IDisposable { }");
    }

    [TestMethod]
    public void BaseList_Struct_Succeeds()
    {
        // A struct can inherit from interfaces (C# 1.0).
        Cs1RoslynTestHelper.AssertParses("struct S : IDisposable { }");
    }

    [TestMethod]
    public void BaseList_Interface_Succeeds()
    {
        // An interface can inherit from multiple interfaces (C# 1.0).
        Cs1RoslynTestHelper.AssertParses("interface I : Base1, Base2 { }");
    }

    [TestMethod]
    public void BaseList_WithMembers_Succeeds()
    {
        // A class with a base list AND members (property + method) in the body.
        Cs1RoslynTestHelper.AssertParses("class C : Base { int X { get { return 0; } set { } } void M() { } }");
    }

    [TestMethod]
    public void BaseList_Enum_Succeeds()
    {
        // Enum base list (EnumBaseList = ":" Type): `enum E : int { A, B }`.
        Cs1RoslynTestHelper.AssertParses("enum E : int { A, B }");
    }

    [TestMethod]
    public void BaseList_ManyBases_Succeeds()
    {
        // Three or more base types in a single list.
        Cs1RoslynTestHelper.AssertParses("class C : Base1, Base2, Base3 { }");
    }

    [TestMethod]
    public void BaseList_ArrayBaseType_Succeeds()
    {
        // A base type that is an array type (the Type rule allows the array postfix in a base list).
        Cs1RoslynTestHelper.AssertParses("class C : int[] { }");
    }

    // === Kitchen sink: every member kind + a nested type in one class ===

    [TestMethod]
    public void KitchenSink_Succeeds()
    {
        // A single class with a field, a property, a method, a constructor, a destructor, an operator,
        // an indexer, an event, and a nested type — all member kinds from T2.1.1–T2.1.4 in one body.
        Cs1RoslynTestHelper.AssertParses(
            "class Kitchen { int f; int P { get { return 0; } set { } } void M() { } Kitchen() { }"
            + " ~Kitchen() { } public static Kitchen operator +(Kitchen a, Kitchen b) { return a; }"
            + " public int this[int i] { get { return 0; } set { } } public event EventHandler Changed;"
            + " class Nested { int x; } }");
    }

    // === Task 3: version-purity — CS2+ FEATURE forms must REJECT ===

    [TestMethod]
    public void Generics_Class_Fails()
    {
        // Generics (CS2): a class type-parameter list `<T>` after the name is not C# 1.0. After
        // `class C`, `BaseList?` (starts with `:`) does not match `<`, and `ClassBody` (starts with
        // `{`) does not match `<` -> no rule matches.
        Cs1RoslynTestHelper.AssertFails("class C<T> { }");
    }

    [TestMethod]
    public void Generics_Method_Fails()
    {
        // Generics (CS2): a method type-parameter list `<T>` after the name is not C# 1.0. After the
        // method name, `(` is required (ParameterList); `<` matches no member rule.
        Cs1RoslynTestHelper.AssertFails("class C { void M<T>() { } }");
    }

    [TestMethod]
    public void Generics_Type_Fails()
    {
        // Generics (CS2): a generic type `List<int>` is not C# 1.0. `List` parses as a Type name, but
        // `<` is not a Type postfix (no type-argument list), so `List<int>` is not a valid Type and the
        // following `x;` cannot be a VariableDeclarator -> the Field fails.
        Cs1RoslynTestHelper.AssertFails("class C { List<int> x; }");
    }

    [TestMethod]
    public void NullableValueType_Fails()
    {
        // Nullable value types (CS2): `int?` is not C# 1.0. `int` parses as a Type, but `?` is not a
        // Type postfix, so `int?` is not a valid Type and the following `x;` cannot be a
        // VariableDeclarator -> the Field fails.
        Cs1RoslynTestHelper.AssertFails("class C { int? x; }");
    }

    [TestMethod]
    public void AsyncModifier_Fails()
    {
        // async (CS5): `async` is not a C# 1.0 method modifier. It is parsed as a Type name, then
        // `void` (reserved) cannot be the method name -> no rule matches.
        Cs1RoslynTestHelper.AssertFails("class C { async void Foo() { } }");
    }

    [TestMethod]
    public void AwaitExpression_Fails()
    {
        // await (CS5): `await` is not a C# 1.0 expression operator. `return await x;` forces `await x`
        // to be an Expression; `await` is a Primary (identifier) but the following `x` is not a
        // PostfixOp, so `await x` is not a valid Expression -> the return statement fails. (A bare
        // `await x;` would parse as a local declaration of type `await` named `x` — valid C# 1.0 — so
        // this test uses `return` to force the expression reading.)
        Cs1RoslynTestHelper.AssertFails("class C { int M() { return await x; } }");
    }

    [TestMethod]
    public void ObjectInitializer_Fails()
    {
        // Object initializers (CS3): `new Foo { X = 1 }` is not C# 1.0. `NewBody` requires `[`
        // (ArrayCreation) or `(` (ObjectCreation) after the base type; `{` matches neither -> the
        // new-expression fails.
        Cs1RoslynTestHelper.AssertFails("class C { Foo f = new Foo { X = 1 }; }");
    }

    [TestMethod]
    public void CollectionInitializer_Fails()
    {
        // Collection initializers (CS3): `new Foo { 1, 2 }` is not C# 1.0 (only array initializers
        // `new T[] { ... }` are C# 1.0). `NewBody` requires `[` or `(` after the base type; `{`
        // matches neither -> the new-expression fails.
        Cs1RoslynTestHelper.AssertFails("class C { Foo f = new Foo { 1, 2 }; }");
    }

    [TestMethod]
    public void Lambda_Fails()
    {
        // Lambdas (CS3): `x => x + 1` is not C# 1.0. The `=>` token is not in the grammar; as an
        // argument to `N(...)`, `x` parses as the argument Expression and the following `=>` is not a
        // PostfixOp nor a `,`/`)` -> the invocation fails.
        Cs1RoslynTestHelper.AssertFails("class C { void M() { N(x => x + 1); } }");
    }

    // === Task 3: version-purity — `var`/`dynamic` as TYPE NAMES parse (boundary decision) ===

    [TestMethod]
    public void Var_AsTypeName_Parses()
    {
        // `var` is NOT a reserved keyword in C# 1.0 (it became a contextual keyword in C# 3), so it is
        // a valid identifier / type name. `var x = 1;` parses as a local declaration of type `var`
        // named `x` — valid C# 1.0. The CS3 type-inference FEATURE is syntactically identical to a
        // type-name declaration, so it cannot be rejected at the parse level (progressT2.1.5 D1).
        Cs1RoslynTestHelper.AssertParses("class C { void M() { var x = 1; } }");
    }

    [TestMethod]
    public void Dynamic_AsTypeName_Parses()
    {
        // `dynamic` is NOT a reserved keyword in C# 1.0 (it became a contextual keyword in C# 4), so it
        // is a valid identifier / type name. `dynamic x = 1;` parses as a local declaration of type
        // `dynamic` named `x` — valid C# 1.0. The CS4 dynamic-dispatch FEATURE is syntactically
        // identical to a type-name declaration, so it cannot be rejected at the parse level (D1).
        Cs1RoslynTestHelper.AssertParses("class C { void M() { dynamic x = 1; } }");
    }
}
