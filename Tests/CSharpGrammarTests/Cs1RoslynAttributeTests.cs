using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

[TestClass]
public class Cs1RoslynAttributeTests
{
    [TestMethod]
    public void Attribute_EmptyArgs_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestGlobalAttributeWithParentheses (adapted: attribute target removed)
        Cs1RoslynTestHelper.AssertParses("[a()] class C { }");
    }

    [TestMethod]
    public void Attribute_MultipleArgs_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestGlobalAttributeWithMultipleArguments (adapted: target removed, constant args)
        Cs1RoslynTestHelper.AssertParses("[a(1, 2)] class C { }");
    }

    [TestMethod]
    public void Attribute_NamedArg_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestGlobalAttributeWithNamedArguments (adapted: target removed, constant value)
        Cs1RoslynTestHelper.AssertParses("[a(b = 1)] class C { }");
    }

    [TestMethod]
    public void Attribute_MixedArgs_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestGlobalAttributeWithMultipleArguments + TestGlobalAttributeWithNamedArguments (adapted)
        Cs1RoslynTestHelper.AssertParses("[a(1, \"s\", n = 2)] class C { }");
    }

    [TestMethod]
    public void Attribute_BoolArg_Succeeds()
    {
        // Roslyn: src/RoslynSdk/Samples/CSharp/CSharpToVisualBasicConverter/CSharpToVisualBasicConverter.Test/TestFiles/AllConstructs.cs ([CLSCompliant(false)], adapted to class level)
        Cs1RoslynTestHelper.AssertParses("[CLSCompliant(false)] class C { }");
    }

    [TestMethod]
    public void Attribute_StringAndNamedBoolArgs_Succeeds()
    {
        // Roslyn: src/RoslynSdk/Samples/CSharp/CSharpToVisualBasicConverter/CSharpToVisualBasicConverter.Test/TestFiles/AllConstructs.cs ([Obsolete(\"Name\", error = false)], adapted to class level)
        Cs1RoslynTestHelper.AssertParses("[Obsolete(\"Name\", error = false)] class C { }");
    }

    [TestMethod]
    public void Attribute_CharArg_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Emit3/Semantics/ParamsCollectionTests.cs String_InAttribute
        Cs1RoslynTestHelper.AssertParses("[Test('1')] class C { }");
    }

    [TestMethod]
    public void Attribute_VerbatimStringArgQualifiedName_Succeeds()
    {
        // Roslyn: src/RoslynSdk/Samples/CSharp/CSharpToVisualBasicConverter/CSharpToVisualBasicConverter.Test/TestFiles/AllConstructs.cs ([assembly: System.Copyright(@\"(C) 2014\")], adapted)
        Cs1RoslynTestHelper.AssertParses("[System.Copyright(@\"(C) 2014\")] class C { }");
    }

    [TestMethod]
    public void Attribute_UnaryMinusArg_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Emit3/Semantics/ExtensionTests2.cs MarkerTypeRawName_25 ([My(BoolProperty = false, SByteProperty = -1, …)], adapted)
        Cs1RoslynTestHelper.AssertParses("[My(SByteProperty = -1, IntProperty = -3)] class C { }");
    }

    [TestMethod]
    public void Attribute_UnaryPlusArg_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Symbol/Symbols/Source/EnumTests.cs ExplicateInit (unary sign constants; no direct Roslyn attr-arg test found)
        Cs1RoslynTestHelper.AssertParses("[a(+1)] class C { }");
    }

    [TestMethod]
    public void Attribute_HexArg_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Emit3/Attributes/AttributeTests_MarshalAs.cs ComInterfaces (IidParameterIndex = 0x1FFFFFFF, adapted)
        Cs1RoslynTestHelper.AssertParses("[a(IidParameterIndex = 0x1FFFFFFF)] class C { }");
    }

    [TestMethod]
    public void Attribute_SuffixedArg_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Emit3/Attributes/AttributeTests_CallerInfoAttributes.cs TestConversionForCallerLineNumber (DefaultParameterValue(5u), adapted to class level)
        Cs1RoslynTestHelper.AssertParses("[DefaultParameterValue(5u)] class C { }");
    }

    [TestMethod]
    public void Attribute_QualifiedName_Succeeds()
    {
        // Roslyn: src/RoslynSdk/Samples/CSharp/CSharpToVisualBasicConverter/CSharpToVisualBasicConverter.Test/TestFiles/AllConstructs.cs (System.NonSerialized, adapted to class level)
        Cs1RoslynTestHelper.AssertParses("[System.NonSerialized] class C { }");
    }

    [TestMethod]
    public void Attribute_MultipleLists_Succeeds()
    {
        // Roslyn: src/RoslynSdk/Samples/CSharp/CSharpToVisualBasicConverter/CSharpToVisualBasicConverter.Test/TestFiles/AllConstructs.cs ([Obsolete] [NonExisting], adapted to class level)
        Cs1RoslynTestHelper.AssertParses("[Obsolete] [NonExisting] class C { }");
    }

    [TestMethod]
    public void Attribute_ListTrailingComma_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestClassWithMultipleAttributesInAList (trailing comma is legal in Cs1 attribute lists; no direct Roslyn test found)
        Cs1RoslynTestHelper.AssertParses("[a, b,] class C { }");
    }

    [TestMethod]
    public void Attribute_MissingCloseBracket_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ParsingErrorRecoveryTests.cs TestAttributeWithClassAfterName
        Cs1RoslynTestHelper.AssertFails("[a class c { }");
    }

    [TestMethod]
    public void Attribute_GarbageAfterStart_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ParsingErrorRecoveryTests.cs TestAttributeWithGarbageAfterStart
        Cs1RoslynTestHelper.AssertFails("[ $");
    }

    [TestMethod]
    public void Attribute_GarbageAfterName_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ParsingErrorRecoveryTests.cs TestAttributeWithGarbageAfterName
        Cs1RoslynTestHelper.AssertFails("[a $");
    }

    [TestMethod]
    public void Attribute_GarbageAfterParamStart_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ParsingErrorRecoveryTests.cs TestAttributeWithClassAfterParameterStart
        Cs1RoslynTestHelper.AssertFails("[a( class c { }");
    }

    [TestMethod]
    public void Attribute_MissingCommaBetweenParams_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ParsingErrorRecoveryTests.cs TestAttributeWithClassAfterParameter
        Cs1RoslynTestHelper.AssertFails("[a(b class c { }");
    }

    [TestMethod]
    public void Attribute_MissingSecondParam_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ParsingErrorRecoveryTests.cs TestAttributeWithClassAfterParameterAndComma
        Cs1RoslynTestHelper.AssertFails("[a(b, class c { }");
    }

    [TestMethod]
    public void Attribute_LeadingCommaInArgs_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ParsingErrorRecoveryTests.cs TestAttributeWithCommaAfterParameterStart
        Cs1RoslynTestHelper.AssertFails("[a(, class c { }");
    }

    [TestMethod]
    public void Attribute_MissingFirstParam_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ParsingErrorRecoveryTests.cs TestAttributeWithMissingFirstParameter
        Cs1RoslynTestHelper.AssertFails("[a(, b class c { }");
    }

    [TestMethod]
    public void Attribute_ArgTrailingComma_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestGlobalAttributeWithMultipleArguments (trailing comma is illegal in Cs1 argument lists; no direct Roslyn test found)
        Cs1RoslynTestHelper.AssertFails("[a(1,)] class C { }");
    }

    [TestMethod]
    public void Attribute_NamedArgNonConstantValue_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestGlobalAttributeWithNamedArguments (Cs1 attribute args are constants only, so the identifier value is out of scope)
        Cs1RoslynTestHelper.AssertFails("[a(b = c)] class C { }");
    }
}
