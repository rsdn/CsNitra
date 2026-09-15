using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

[TestClass]
public class Cs1RoslynDirectiveTests
{
    [TestMethod]
    public void ExternAlias_Basic_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestExternAlias
        Cs1RoslynTestHelper.AssertParses("extern alias a;");
    }

    [TestMethod]
    public void Using_OpenSimple_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestUsing
        Cs1RoslynTestHelper.AssertParses("using a;");
    }

    [TestMethod]
    public void Using_OpenDotted_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestUsingDottedName
        Cs1RoslynTestHelper.AssertParses("using a.b;");
    }

    [TestMethod]
    public void Using_AliasSimple_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestUsingAliasName
        Cs1RoslynTestHelper.AssertParses("using a = b;");
    }

    [TestMethod]
    public void Using_AliasQualified_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Symbol/SymbolDisplay/SymbolDisplayTests.cs TestBug2239
        Cs1RoslynTestHelper.AssertParses("using Goo = N1.N2.N3;");
    }

    [TestMethod]
    public void Using_AliasQualifiedDeep_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Symbol/SymbolDisplay/SymbolDisplayTests.cs TupleProperty
        Cs1RoslynTestHelper.AssertParses("using NAB = N.A.B;");
    }

    [TestMethod]
    public void Using_MultipleDirectives_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestUsing + TestUsingDottedName + TestUsingAliasName
        Cs1RoslynTestHelper.AssertParses(
            "using a;\nusing a.b;\nusing a = b;\n");
    }

    [TestMethod]
    public void Namespace_Basic_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestNamespace
        Cs1RoslynTestHelper.AssertParses("namespace a { }");
    }

    [TestMethod]
    public void Namespace_Dotted_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestNamespaceWithDottedName
        Cs1RoslynTestHelper.AssertParses("namespace a.b.c { }");
    }

    [TestMethod]
    public void Namespace_WithUsing_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestNamespaceWithUsing
        Cs1RoslynTestHelper.AssertParses("namespace a { using b.c; }");
    }

    [TestMethod]
    public void Namespace_WithExternAlias_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestNamespaceWithExternAlias
        Cs1RoslynTestHelper.AssertParses("namespace a { extern alias b; }");
    }

    [TestMethod]
    public void Namespace_Nested_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestNamespaceWithNestedNamespace
        Cs1RoslynTestHelper.AssertParses("namespace a { namespace b { } }");
    }

    [TestMethod]
    public void Namespace_UsingAfterExternAlias_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestNamespaceWithExternAliasFollowingUsingBad
        // Roslyn errors only from C# 2.0 on; C# 1.0 allowed interleaved directives and the Cs1 grammar is in scope.
        Cs1RoslynTestHelper.AssertParses("namespace a { using b; extern alias c; }");
    }

    [TestMethod]
    public void Namespace_TypeMembersInterleaved_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestNamespaceWithUsing + TestNamespaceWithNestedNamespace
        Cs1RoslynTestHelper.AssertParses(
            "namespace a\n{\n    using b;\n    namespace c\n    {\n        class D { }\n    }\n    extern alias e;\n}");
    }

    [TestMethod]
    public void Namespace_TrailingSemicolon_Succeeds()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestNamespace (trailing ';' is legal per the committed grammar)
        Cs1RoslynTestHelper.AssertParses("namespace a { };");
    }

    [TestMethod]
    public void CompilationUnit_InterleavedDirectivesAndTypes_Succeeds()
    {
        // Roslyn: src/RoslynSdk/Samples/CSharp/CSharpToVisualBasicConverter/CSharpToVisualBasicConverter.Test/TestFiles/AllConstructs.cs
        // Adapted: members, #directives, generics, expressions, and attribute targets removed.
        Cs1RoslynTestHelper.AssertParses(
            """
            extern alias Foo;
            using System;
            using M = System.Math;
            class TopLevelType : IDisposable
            {
            }
            namespace My
            {
                using A.B;
                public unsafe class A : C, I
                {
                }
                public enum E
                {
                    A,
                    B = 1,
                }
                public delegate void D(object P);
            }
            """);
    }

    [TestMethod]
    public void Using_KeywordName_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/UsingDirectiveParsingTests.cs SimpleUsingDirectivePredefinedType
        Cs1RoslynTestHelper.AssertFails("using int;");
    }

    [TestMethod]
    public void Using_AliasKeywordTarget_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/UsingDirectiveParsingTests.cs AliasUsingVoid1
        Cs1RoslynTestHelper.AssertFails("using V = void;");
    }

    [TestMethod]
    public void Using_KeywordAfterUsing_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestUsingNamespace
        Cs1RoslynTestHelper.AssertFails("using namespace a;");
    }

    [TestMethod]
    public void Using_KeywordBeforeUsing_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestUsingStaticInWrongOrder
        Cs1RoslynTestHelper.AssertFails("static using a;");
    }

    [TestMethod]
    public void Using_IdentifierPrefixIsNotDirective_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/NameParsingTests.cs TestMissingNameDueToKeyword (whole-word keyword semantics)
        Cs1RoslynTestHelper.AssertFails("usingx;");
    }

    [TestMethod]
    public void Using_KeywordAfterAliasTarget_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/NameParsingTests.cs TestMissingNameDueToKeyword (whole-word keyword semantics)
        Cs1RoslynTestHelper.AssertFails("using a = int;");
    }

    [TestMethod]
    public void Namespace_FileScopedSemicolon_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestFileScopedNamespace (file-scoped namespaces are C# 10)
        Cs1RoslynTestHelper.AssertFails("namespace a;");
    }

    [TestMethod]
    public void Namespace_FileScopedWithUsing_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestFileScopedNamespaceWithUsing
        Cs1RoslynTestHelper.AssertFails("namespace a; using b.c;");
    }

    [TestMethod]
    public void Namespace_FileScopedWithExternAlias_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestFileScopedNamespaceWithExternAlias
        Cs1RoslynTestHelper.AssertFails("namespace a; extern alias b;");
    }

    [TestMethod]
    public void Namespace_AliasQualified_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/DeclarationParsingTests.cs TestNamespaceDeclarationsBadNames1
        Cs1RoslynTestHelper.AssertFails("namespace A::B { }");
    }

    [TestMethod]
    public void Namespace_GarbageBefore_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ParsingErrorRecoveryTests.cs TestGarbageBeforeNamespace
        Cs1RoslynTestHelper.AssertFails("$ namespace n { }");
    }

    [TestMethod]
    public void Namespace_GarbageAfter_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ParsingErrorRecoveryTests.cs TestGarbageAfterNamespace
        Cs1RoslynTestHelper.AssertFails("namespace n { } $");
    }

    [TestMethod]
    public void Namespace_GarbageInside_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ParsingErrorRecoveryTests.cs TestGarbageInsideNamespace
        Cs1RoslynTestHelper.AssertFails("namespace n { $ }");
    }

    [TestMethod]
    public void Namespace_UnexpectedKeywordInside_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ParsingErrorRecoveryTests.cs TestNamespaceWithUnexpectedKeyword
        Cs1RoslynTestHelper.AssertFails("namespace n { int }");
    }

    [TestMethod]
    public void Namespace_UnexpectedBraceInside_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ParsingErrorRecoveryTests.cs TestNamespaceWithUnexpectedBracing
        Cs1RoslynTestHelper.AssertFails("namespace n { { }");
    }

    [TestMethod]
    public void Namespace_ExtraClosingBrace_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ParsingErrorRecoveryTests.cs TestGlobalNamespaceWithUnexpectedBracingAtEnd
        Cs1RoslynTestHelper.AssertFails("namespace n { } }");
    }

    [TestMethod]
    public void Namespace_ExtraClosingBraceBefore_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ParsingErrorRecoveryTests.cs TestGlobalNamespaceWithUnexpectedBracingAtStart
        Cs1RoslynTestHelper.AssertFails("} namespace n { }");
    }

    [TestMethod]
    public void Namespace_TrailingCommas_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ParsingErrorRecoveryTests.cs MultipleSubsequentMisplacedCharactersSingleError1
        Cs1RoslynTestHelper.AssertFails("namespace n { } ,,,,,,,,");
    }

    [TestMethod]
    public void Namespace_CommasAround_Fails()
    {
        // Roslyn: src/Compilers/CSharp/Test/Syntax/Parsing/ParsingErrorRecoveryTests.cs MultipleSubsequentMisplacedCharactersSingleError2
        Cs1RoslynTestHelper.AssertFails(",,,, namespace n { } ,,,,");
    }
}
