using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T2.1.4: operators (user-defined + conversion), indexers, events (class/struct/interface). Parsed
// from the Grammar start rule (a full `class C { ... }` / `struct S { ... }` / `interface I { ... }`).
//
// Operator = Attributes? OperatorModifier* Type "operator" OperatorSymbol "(" ParameterList ")" MethodBody
//   (the ret-type comes BEFORE `operator`; Roslyn ParseOperatorDeclaration 3992-4190).
// ConversionOperator = Attributes? OperatorModifier* (implicit|explicit) "operator" Type "(" ParameterList ")" MethodBody
//   (implicit/explicit comes BEFORE `operator`; the Type after `operator` is the TARGET type; Roslyn
//   TryParseConversionOperatorDeclaration 3760-3977, BetterCandidates.cs:885 `implicit operator`).
// Indexer = Attributes? IndexerModifier* Type "this" "[" ParameterList "]" "{" AccessorDeclaration+ "}"
//   (class/struct block accessors; `this` is reserved so Indexer vs Property disambiguate cleanly).
// InterfaceIndexer = ... "{" InterfaceAccessor+ "}" (semicolon accessors, valid C# 1.0).
// Event = Attributes? EventModifier* "event" Type TypeName ( ";" | "{" add { } remove { } "}" )
// InterfaceEvent = Attributes? EventModifier* "event" Type TypeName ";" (C# 1.0: no interface event bodies).
//
// Parser-vs-binder: a missing `static` on an operator is a BINDER concern (Roslyn ParseModifiers is a
// generic loop; SourceUserDefinedOperatorSymbol enforces static) -> `bool operator ==(a,b) { }` PARSes.
// `event C Changed;` parses for any Type (delegate-ness is a binder concern).
[TestClass]
public class Cs1OperatorIndexerEventTests
{
    // === User-defined operators (each of the 16 C# 1.0 overloadable symbols) ===

    [TestMethod]
    public void Operator_Plus_Binary_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { public static C operator +(C a, C b) { return a; } }");
    }

    [TestMethod]
    public void Operator_Minus_Binary_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { public static C operator -(C a, C b) { return a; } }");
    }

    [TestMethod]
    public void Operator_Multiply_Binary_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { public static C operator *(C a, C b) { return a; } }");
    }

    [TestMethod]
    public void Operator_Divide_Binary_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { public static C operator /(C a, C b) { return a; } }");
    }

    [TestMethod]
    public void Operator_Modulo_Binary_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { public static C operator %(C a, C b) { return a; } }");
    }

    [TestMethod]
    public void Operator_BitAnd_Binary_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { public static C operator &(C a, C b) { return a; } }");
    }

    [TestMethod]
    public void Operator_BitOr_Binary_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { public static C operator |(C a, C b) { return a; } }");
    }

    [TestMethod]
    public void Operator_BitXor_Binary_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { public static C operator ^(C a, C b) { return a; } }");
    }

    [TestMethod]
    public void Operator_BitNot_Unary_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { public static C operator ~(C a) { return a; } }");
    }

    [TestMethod]
    public void Operator_Less_Binary_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { public static bool operator <(C a, C b) { return true; } }");
    }

    [TestMethod]
    public void Operator_Greater_Binary_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { public static bool operator >(C a, C b) { return true; } }");
    }

    [TestMethod]
    public void Operator_Equal_Binary_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { public static bool operator ==(C a, C b) { return true; } }");
    }

    [TestMethod]
    public void Operator_NotEqual_Binary_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { public static bool operator !=(C a, C b) { return true; } }");
    }

    [TestMethod]
    public void Operator_Increment_Unary_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { public static C operator ++(C a) { return a; } }");
    }

    [TestMethod]
    public void Operator_Decrement_Unary_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { public static C operator --(C a) { return a; } }");
    }

    // === User-defined operators (body forms / contexts) ===

    [TestMethod]
    public void Operator_SemicolonBody_Succeeds()
    {
        // A semicolon body (extern/abstract form) is a binder error, not a parse error (MethodBody).
        Cs1RoslynTestHelper.AssertParses("class C { public static extern C operator +(C a, C b); }");
    }

    [TestMethod]
    public void Operator_Struct_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("struct S { public static S operator +(S a, S b) { return a; } }");
    }

    [TestMethod]
    public void Operator_MissingStatic_Parses()
    {
        // Parser-vs-binder: `static` is REQUIRED by the binder (SourceUserDefinedOperatorSymbol) but
        // NOT by the parser (Roslyn ParseModifiers is a generic loop). So a `static`-less operator
        // with a valid ret-type PARSes (the binder rejects it).
        Cs1RoslynTestHelper.AssertParses("class C { bool operator ==(C a, C b) { return true; } }");
    }

    // === Conversion operators (implicit/explicit) ===

    [TestMethod]
    public void Conversion_Implicit_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { public static implicit operator int(C c) { return 0; } }");
    }

    [TestMethod]
    public void Conversion_Explicit_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { public static explicit operator C(int i) { return new C(); } }");
    }

    [TestMethod]
    public void Conversion_Struct_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("struct S { public static implicit operator long(S s) { return 0; } }");
    }

    // === Indexers (class/struct block + interface semicolon) ===

    [TestMethod]
    public void Indexer_Class_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { public int this[int i] { get { return 0; } set { } } }");
    }

    [TestMethod]
    public void Indexer_Interface_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("interface I { int this[int i] { get; set; } }");
    }

    [TestMethod]
    public void Indexer_MultiParam_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { public int this[int i, int j] { get { return 0; } } }");
    }

    [TestMethod]
    public void Indexer_ValueInSetter_Succeeds()
    {
        // `value` is a true identifier (not reserved, T2.1.2) -> usable in the setter body.
        Cs1RoslynTestHelper.AssertParses("class C { int[] _a; public int this[int i] { get { return _a[i]; } set { _a[i] = value; } } }");
    }

    [TestMethod]
    public void Indexer_Struct_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("struct S { public int this[int i] { get { return 0; } } }");
    }

    // === Events (class/struct simple + accessors, interface simple) ===

    [TestMethod]
    public void Event_Simple_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { public event EventHandler Changed; }");
    }

    [TestMethod]
    public void Event_WithAccessors_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("class C { public event EventHandler Changed { add { } remove { } } }");
    }

    [TestMethod]
    public void Event_Interface_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("interface I { event EventHandler Changed; }");
    }

    [TestMethod]
    public void Event_ValueInBodies_Succeeds()
    {
        // `value` is a true identifier (T2.1.2) -> usable in the add/remove bodies.
        Cs1RoslynTestHelper.AssertParses("class C { public event EventHandler Changed { add { value = null; } remove { value = null; } } }");
    }

    [TestMethod]
    public void Event_Struct_Succeeds()
    {
        Cs1RoslynTestHelper.AssertParses("struct S { public event EventHandler Changed; }");
    }

    [TestMethod]
    public void Event_NonDelegateType_Parses()
    {
        // Parser-vs-binder: `event Type Name;` parses for ANY Type; the delegate-ness of the type is
        // a binder concern (CS0039). So a non-delegate event type PARSes.
        Cs1RoslynTestHelper.AssertParses("class C { event C Changed; }");
    }

    // === Roslyn-derived + disambiguation ===

    [TestMethod]
    public void Roslyn_ExplicitConversion_Succeeds()
    {
        // Roslyn Test/Semantic/Semantics (conversion operators): `public static explicit operator A(int i)`.
        Cs1RoslynTestHelper.AssertParses("class A { public static explicit operator A(int i) { return new A(); } }");
    }

    [TestMethod]
    public void OperatorVsMethod_Mixed_Succeeds()
    {
        // After `Type`, the `operator` keyword -> Operator; a TypeName -> Method. Both parse in the
        // same body without cross-matching.
        Cs1RoslynTestHelper.AssertParses("class C { int Foo() { return 0; } public static C operator +(C a, C b) { return a; } }");
    }

    // === Negatives ===

    [TestMethod]
    public void Operator_MissingReturnType_Fails()
    {
        // A user-defined operator requires a ret-type BEFORE `operator` (Roslyn ParseReturnType 3314).
        // `operator + (C a) { }` has no ret-type -> the `operator` keyword cannot be a Type, so no
        // rule matches. (It is also missing `static`, but the ret-type is the parse-level issue.)
        Cs1RoslynTestHelper.AssertFails("class C { operator + (C a) { } }");
    }

    [TestMethod]
    public void Indexer_MissingParams_Fails()
    {
        // An indexer requires a bracketed parameter list: `public int this { get; }` (no `[...]`)
        // is invalid (Roslyn ParseIndexerDeclaration 4209 requires the bracketed list).
        Cs1RoslynTestHelper.AssertFails("class C { public int this { get; } }");
    }

    [TestMethod]
    public void GenericOperator_Fails()
    {
        // Version purity (generics CS2): a generic operator has a type-parameter list `<T>` after the
        // operator symbol. After `operator <` (the OperatorSymbol), the grammar expects `(` but sees
        // `T` -> no rule matches. (The task's `operator <(T a, T b)` is actually a valid `<` operator;
        // this is the corrected generic-operator form.)
        Cs1RoslynTestHelper.AssertFails("class C { public static bool operator <T>(T a, T b) { return true; } }");
    }

    [TestMethod]
    public void InterfaceIndexer_WithBody_Fails()
    {
        // C# 1.0 interface indexers have semicolon accessors (no body). `get { ... }` (a block body)
        // is not an InterfaceAccessor -> no rule matches.
        Cs1RoslynTestHelper.AssertFails("interface I { int this[int i] { get { return 0; } } }");
    }

    [TestMethod]
    public void InterfaceEvent_WithAccessors_Fails()
    {
        // C# 1.0 interface events are `;`-only (no add/remove accessors with bodies).
        Cs1RoslynTestHelper.AssertFails("interface I { event EventHandler Changed { add { } remove { } } }");
    }

    [TestMethod]
    public void EventAccessor_WrongName_Fails()
    {
        // Event accessors are add/remove (not get/set). `get { }` is not an EventAccessorName ->
        // the EventTail accessor-list branch fails, and the `;` branch fails (sees `{`).
        Cs1RoslynTestHelper.AssertFails("class C { event EventHandler Changed { get { } } }");
    }

    [TestMethod]
    public void Conversion_MissingTargetType_Fails()
    {
        // A conversion operator requires a target Type after implicit/explicit: `operator implicit
        // (C c) { }` has no type -> the Type rule fails on `(`.
        Cs1RoslynTestHelper.AssertFails("class C { public static implicit operator (C c) { return 0; } }");
    }
}
