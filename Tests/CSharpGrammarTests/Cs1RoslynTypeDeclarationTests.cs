using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

[TestClass]
public class Cs1RoslynTypeDeclarationTests
{
    [TestMethod]
    public void Class_Basic_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestClass
        Cs1RoslynTestHelper.AssertParses("class a { }");
    }

    [TestMethod]
    public void Class_Public_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestClassWithPublic
        Cs1RoslynTestHelper.AssertParses("public class a { }");
    }

    [TestMethod]
    public void Class_Internal_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestClassWithInternal
        Cs1RoslynTestHelper.AssertParses("internal class a { }");
    }

    [TestMethod]
    public void Class_Private_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Semantic/Semantics/ScriptSemanticsTests.cs PrivateNested
        Cs1RoslynTestHelper.AssertParses("private class A { }");
    }

    [TestMethod]
    public void Class_Protected_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Semantic/Semantics/ScriptSemanticsTests.cs PrivateNested
        Cs1RoslynTestHelper.AssertParses("protected class B { }");
    }

    [TestMethod]
    public void Class_Sealed_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestClassWithSealed
        Cs1RoslynTestHelper.AssertParses("sealed class a { }");
    }

    [TestMethod]
    public void Class_Abstract_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestClassWithAbstract
        Cs1RoslynTestHelper.AssertParses("abstract class a { }");
    }

    [TestMethod]
    public void Class_Unsafe_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Emit/CodeGen/CodeGenFunctionPointersTests.cs Attribute_ObjectDefault_Enum_ConstructorArgument_WithUnsafeContext
        Cs1RoslynTestHelper.AssertParses("unsafe class C { }");
    }

    [TestMethod]
    public void Class_MultiModifierWithBases_Succeeds()
    {
        // Roslyn: src/RoslynSdk/Samples/CSharp/CSharpToVisualBasicConverter/CSharpToVisualBasicConverter.Test/TestFiles/AllConstructs.cs
        // Adapted: 'partial' modifier removed (not in Cs1 scope).
        Cs1RoslynTestHelper.AssertParses("public unsafe class A : C, I\n{\n}");
    }

    [TestMethod]
    public void Class_Attribute_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestClassWithAttribute
        Cs1RoslynTestHelper.AssertParses("[attr] class a { }");
    }

    [TestMethod]
    public void Class_MultipleAttributeLists_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestClassWithMultipleAttributes
        Cs1RoslynTestHelper.AssertParses("[attr1] [attr2] class a { }");
    }

    [TestMethod]
    public void Class_AttributeList_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestClassWithMultipleAttributesInAList
        Cs1RoslynTestHelper.AssertParses("[attr1, attr2] class a { }");
    }

    [TestMethod]
    public void Class_BaseType_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestClassWithBaseType
        Cs1RoslynTestHelper.AssertParses("class a : b { }");
    }

    [TestMethod]
    public void Class_MultipleBases_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestClassWithMultipleBases
        Cs1RoslynTestHelper.AssertParses("class a : b, c { }");
    }

    [TestMethod]
    public void Class_TrailingSemicolon_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs Class_SemicolonAfterBlock
        Cs1RoslynTestHelper.AssertParses("class C { };");
    }

    [TestMethod]
    public void Class_ContextualKeywordName_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/AccessorDeclarationParsingTests.cs RefReturningMembersWithAccessorKeywordType
        Cs1RoslynTestHelper.AssertParses("class get { }");
    }

    [TestMethod]
    public void Class_StaticModifier_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestClassWithStatic ('static' types are CS2)
        Cs1RoslynTestHelper.AssertFails("static class a { }");
    }

    [TestMethod]
    public void Class_PartialModifier_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestClassWithPartial ('partial' is CS2)
        Cs1RoslynTestHelper.AssertFails("partial class a { }");
    }

    [TestMethod]
    public void Class_KeywordName_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/NameParsingTests.cs TestMissingNameDueToKeyword
        Cs1RoslynTestHelper.AssertFails("class int { }");
    }

    [TestMethod]
    public void Class_KeywordInBaseList_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ParsingErrorRecoveryTests.cs TestClassAfterStartOfBaseTypeList
        Cs1RoslynTestHelper.AssertFails("class c : class b { }");
    }

    [TestMethod]
    public void Class_MissingCommaInBaseList_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ParsingErrorRecoveryTests.cs TestClassAfterBaseType
        Cs1RoslynTestHelper.AssertFails("class c : t class b { }");
    }

    [TestMethod]
    public void Class_KeywordAfterBaseComma_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ParsingErrorRecoveryTests.cs TestClassAfterBaseTypeAndComma
        Cs1RoslynTestHelper.AssertFails("class c : t, class b { }");
    }

    [TestMethod]
    public void Class_BaseTypesMissingComma_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ParsingErrorRecoveryTests.cs TestClassAfterBaseTypesWithMissingComma
        Cs1RoslynTestHelper.AssertFails("class c : x y class b { }");
    }

    [TestMethod]
    public void Class_GarbageAfterBaseColon_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ParsingErrorRecoveryTests.cs TestGarbageAfterStartOfBaseTypeList
        Cs1RoslynTestHelper.AssertFails("class c : $ { }");
    }

    [TestMethod]
    public void Class_GarbageAfterBaseType_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ParsingErrorRecoveryTests.cs TestGarbageAfterBaseType
        Cs1RoslynTestHelper.AssertFails("class c : t $ { }");
    }

    [TestMethod]
    public void Class_GarbageAfterBaseComma_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ParsingErrorRecoveryTests.cs TestGarbageAfterBaseTypeAndComma
        Cs1RoslynTestHelper.AssertFails("class c : t, $ { }");
    }

    [TestMethod]
    public void Class_GarbageAfterBaseTypes_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ParsingErrorRecoveryTests.cs TestGarbageAfterBaseTypesWithMissingComma
        Cs1RoslynTestHelper.AssertFails("class c : x y $ { }");
    }

    [TestMethod]
    public void Class_ExtraColonInBaseList_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestExtraneousColonInBaseList
        Cs1RoslynTestHelper.AssertFails("class A : B : C\n{\n}");
    }

    [TestMethod]
    public void Class_TrailingCommaInBaseList_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ParsingErrorRecoveryTests.cs TestOpenBraceAfterBaseTypeComma (adapted: generics removed)
        Cs1RoslynTestHelper.AssertFails("class c : x, { }");
    }

    [TestMethod]
    public void Struct_Basic_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestStruct
        Cs1RoslynTestHelper.AssertParses("struct a { }");
    }

    [TestMethod]
    public void Struct_Unsafe_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Semantic/Semantics/UnsafeTests.cs UnsafeModifier
        Cs1RoslynTestHelper.AssertParses("unsafe struct Inner { }");
    }

    [TestMethod]
    public void Struct_BaseList_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Semantic/Semantics/SemanticErrorTests.cs CS0313ERR_GenericConstraintNotSatisfiedNullableInterface
        Cs1RoslynTestHelper.AssertParses("struct S : I { }");
    }

    [TestMethod]
    public void Struct_TrailingSemicolon_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs Class_SemicolonAfterBlock
        Cs1RoslynTestHelper.AssertParses("struct C { };");
    }

    [TestMethod]
    public void Struct_KeywordName_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/NameParsingTests.cs TestMissingNameDueToKeyword
        Cs1RoslynTestHelper.AssertFails("struct int { }");
    }

    [TestMethod]
    public void Interface_Basic_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestInterface
        Cs1RoslynTestHelper.AssertParses("interface a { }");
    }

    [TestMethod]
    public void Interface_BaseList_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Semantic/Semantics/MethodBodyModelTests.cs PropertyAmbiguity
        Cs1RoslynTestHelper.AssertParses("interface IC : IA, IB { }");
    }

    [TestMethod]
    public void Interface_TrailingSemicolon_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs Class_SemicolonAfterBlock
        Cs1RoslynTestHelper.AssertParses("interface C { };");
    }

    [TestMethod]
    public void Interface_KeywordName_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/NameParsingTests.cs TestMissingNameDueToKeyword
        Cs1RoslynTestHelper.AssertFails("interface int { }");
    }

    [TestMethod]
    public void Enum_EmptyBody_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Symbol/Symbols/Source/EnumTests.cs NullEnumBody
        Cs1RoslynTestHelper.AssertParses("enum Figure { }");
    }

    [TestMethod]
    public void Enum_MixedMembers_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Symbol/Symbols/Source/EnumTests.cs MixedInit
        Cs1RoslynTestHelper.AssertParses(
            "public enum Suits\n{\n    ValueA,\n    ValueB = 10,\n    ValueC,\n    ValueD,\n};");
    }

    [TestMethod]
    public void Enum_UnaryMinusValues_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Symbol/Symbols/Source/EnumTests.cs ExplicateInit
        Cs1RoslynTestHelper.AssertParses(
            "public enum Suits\n{\n    ValueA = -1,\n    ValueB = 2,\n    ValueC = 3,\n    ValueD = 4,\n};");
    }

    [TestMethod]
    public void Enum_UnderlyingTypeNegativeValue_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Symbol/Symbols/Source/EnumTests.cs OutOfUnderlyingRange
        Cs1RoslynTestHelper.AssertParses("enum Suits : short { a, b, c, d = -65536, e, f }");
    }

    [TestMethod]
    public void Enum_HexValueTrailingComma_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Symbol/Symbols/Retargeting/RetargetingTests.cs RetargetedUnmanagedCallersOnlyData
        Cs1RoslynTestHelper.AssertParses("public enum AttributeTargets { Method = 0x0040, }");
    }

    [TestMethod]
    public void Enum_QualifiedAttribute_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Symbol/Symbols/Source/EnumTests.cs FlagOnEnum
        // Adapted: 'Combi = ValueA | ValueB' expression value removed.
        Cs1RoslynTestHelper.AssertParses(
            "[System.Flags]\npublic enum Suits\n{\n    ValueA = 1,\n    ValueB = 2,\n    ValueC = 4,\n    ValueD = 8,\n}");
    }

    [TestMethod]
    public void Enum_AttributeTrailingSemicolon_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Symbol/Symbols/Source/EnumTests.cs AttributeOnEnum
        Cs1RoslynTestHelper.AssertParses("[Attr1]\nenum Figure { One, Two, Three };");
    }

    [TestMethod]
    public void Enum_KeywordMemberName_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Symbol/Symbols/Source/EnumTests.cs CS1041ERR_IdentifierExpectedKW_ModifiersForEnumMember
        Cs1RoslynTestHelper.AssertFails("enum ColorA\n{\n    public Red\n}");
    }

    [TestMethod]
    public void Enum_MissingIdentifier_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Symbol/Symbols/Source/EnumTests.cs CS1001ERR_IdentifierExpected_NoIDForEnum
        Cs1RoslynTestHelper.AssertFails("enum { One, Two, Three };");
    }

    [TestMethod]
    public void Enum_MissingIdentifierWithMembers_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests_MissingIdentifiers.cs TypeMissingIdentifier_Enum01
        // Adapted: 'D = C + 2' expression value removed.
        Cs1RoslynTestHelper.AssertFails(
            "public enum\n{\n    A,\n    B,\n    C = 1,\n    E = 0,\n}");
    }

    [TestMethod]
    public void Enum_MissingIdentifierWithBase_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests_MissingIdentifiers.cs TypeMissingIdentifier_Enum02
        // Adapted: 'D = C + 2' expression value removed.
        Cs1RoslynTestHelper.AssertFails(
            "public enum : uint\n{\n    A,\n    B,\n    C = 1,\n    E = 0,\n}");
    }

    [TestMethod]
    public void Enum_EofBeforeBody_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Symbol/Symbols/Source/EnumTests.cs EnumEOFBeforeMembers
        Cs1RoslynTestHelper.AssertFails("enum E");
    }

    [TestMethod]
    public void Enum_EofAfterStart_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ParsingErrorRecoveryTests.cs TestEOFAfterEnumStart
        Cs1RoslynTestHelper.AssertFails("enum e { ");
    }

    [TestMethod]
    public void Enum_EofAfterMember_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ParsingErrorRecoveryTests.cs TestEOFAfterEnumName
        Cs1RoslynTestHelper.AssertFails("enum e { n ");
    }

    [TestMethod]
    public void Enum_EofAfterMemberComma_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ParsingErrorRecoveryTests.cs TestEOFAfterEnumNameAndComma
        Cs1RoslynTestHelper.AssertFails("enum e { n, ");
    }

    [TestMethod]
    public void Enum_GarbageAfterStart_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ParsingErrorRecoveryTests.cs TestGarbageAfterEnumStart
        Cs1RoslynTestHelper.AssertFails("enum e { $ }");
    }

    [TestMethod]
    public void Enum_GarbageAfterMember_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ParsingErrorRecoveryTests.cs TestGarbageAfterEnumName
        Cs1RoslynTestHelper.AssertFails("enum e { n $ }");
    }

    [TestMethod]
    public void Enum_GarbageBeforeMember_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ParsingErrorRecoveryTests.cs TestGarbageBeforeEnumName
        Cs1RoslynTestHelper.AssertFails("enum e { $ n }");
    }

    [TestMethod]
    public void Enum_GarbageAfterMemberComma_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ParsingErrorRecoveryTests.cs TestGarbageAferEnumNameAndComma
        Cs1RoslynTestHelper.AssertFails("enum e { n, $ }");
    }

    [TestMethod]
    public void Enum_GarbageBetweenMembers_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ParsingErrorRecoveryTests.cs TestGarbageBetweenEnumNamesWithMissingComma
        Cs1RoslynTestHelper.AssertFails("enum e { n $ n }");
    }

    [TestMethod]
    public void Enum_GarbageAfterEquals_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ParsingErrorRecoveryTests.cs TestGarbageAferEnumNameAndEquals
        Cs1RoslynTestHelper.AssertFails("enum e { n = $ }");
    }

    [TestMethod]
    public void Enum_MemberCommaThenClass_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ParsingErrorRecoveryTests.cs TestClassAfterEnumNameAndComma
        Cs1RoslynTestHelper.AssertFails("enum e { n, class c { }");
    }

    [TestMethod]
    public void Enum_MissingBodySemicolonOnly_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Symbol/Symbols/Source/EnumTests.cs NoEnumBody_01 (Cs1 grammar requires an empty body)
        Cs1RoslynTestHelper.AssertFails("enum Figure ;");
    }

    [TestMethod]
    public void Delegate_Basic_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestDelegate
        Cs1RoslynTestHelper.AssertParses("delegate a b();");
    }

    [TestMethod]
    public void Delegate_Parameter_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestDelegateWithParameter
        Cs1RoslynTestHelper.AssertParses("delegate a b(c d);");
    }

    [TestMethod]
    public void Delegate_MultipleParameters_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestDelegateWithMultipleParameters
        Cs1RoslynTestHelper.AssertParses("delegate a b(c d, e f);");
    }

    [TestMethod]
    public void Delegate_RefParameter_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestDelegateWithRefParameter
        Cs1RoslynTestHelper.AssertParses("delegate a b(ref c d);");
    }

    [TestMethod]
    public void Delegate_OutParameter_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestDelegateWithOutParameter
        Cs1RoslynTestHelper.AssertParses("delegate a b(out c d);");
    }

    [TestMethod]
    public void Delegate_ParamsParameter_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestDelegateWithParamsParameter
        Cs1RoslynTestHelper.AssertParses("delegate a b(params c d);");
    }

    [TestMethod]
    public void Delegate_AttributeParameter_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestDelegateWithParameterAttribute
        Cs1RoslynTestHelper.AssertParses("delegate a b([attr] c d);");
    }

    [TestMethod]
    public void Delegate_BuiltInReturnType_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestDelegateWithBuiltInReturnTypes
        Cs1RoslynTestHelper.AssertParses("delegate int b();");
    }

    [TestMethod]
    public void Delegate_BuiltInParameterType_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestDelegateWithBuiltInParameterTypes
        Cs1RoslynTestHelper.AssertParses("delegate a b(int c);");
    }

    [TestMethod]
    public void Delegate_MixedModifiersAndAttribute_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestDelegateWithParameterAttribute + TestDelegateWithRefParameter + TestDelegateWithOutParameter + TestDelegateWithParamsParameter
        Cs1RoslynTestHelper.AssertParses("delegate a b([A] ref int x, out string y, params c z);");
    }

    [TestMethod]
    public void Delegate_RefReturnType_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestDelegateWithRefReturnType (ref return types are CS7.2)
        Cs1RoslynTestHelper.AssertFails("delegate ref a b();");
    }

    [TestMethod]
    public void Delegate_MissingSemicolon_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ParsingErrorRecoveryTests.cs TestEOFAfterDelegateParameterList
        Cs1RoslynTestHelper.AssertFails("delegate void d(t n)");
    }

    [TestMethod]
    public void Delegate_EofAfterParamComma_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ParsingErrorRecoveryTests.cs TestEOFAfterDelegateParameterTypeNameAndComma
        Cs1RoslynTestHelper.AssertFails("delegate void d(t n, ");
    }

    [TestMethod]
    public void Delegate_MissingCloseParen_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ParsingErrorRecoveryTests.cs TestClassAfterDelegateParameterStart
        Cs1RoslynTestHelper.AssertFails("delegate void d( class c { }");
    }

    [TestMethod]
    public void Delegate_MissingParamName_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ParsingErrorRecoveryTests.cs TestClassAfterDelegateParameterType
        Cs1RoslynTestHelper.AssertFails("delegate void d(t class c { }");
    }

    [TestMethod]
    public void Delegate_MissingSemicolonBeforeClass_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ParsingErrorRecoveryTests.cs TestClassAfterDelegateParameterList
        Cs1RoslynTestHelper.AssertFails("delegate void d(t n) class c { }");
    }

    [TestMethod]
    public void Delegate_KeywordName_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/NameParsingTests.cs TestMissingNameDueToKeyword
        Cs1RoslynTestHelper.AssertFails("delegate a int();");
    }
}
