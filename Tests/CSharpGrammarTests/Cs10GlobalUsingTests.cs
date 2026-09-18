using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CSharpGrammarTests;

// T3.10.2 — C# 10.0 global using directive (Cs10.grammar). Positives use CreateParser(10) (Cs1+...+
// Cs10 merged); version-purity negatives use CreateParser(9) (Cs1+...+Cs9, no CS10). A global using is
// a using directive with a leading "global" keyword that applies to the entire assembly:
//   global using System;
//   global using static System.Console;
// It is a NEW NamespaceMember alternative (T0.3 append-merge) mirroring the Cs1 UsingDirective
// (Cs1.grammar:11-13) with a "global" prefix and an added `static` form. Roslyn: ParseUsingDirective
// (LanguageParser.cs:942-1010), UsingDirectiveSyntax (UsingDirectiveSyntax.cs:21-22). See
// docs/CSharpParserPlan-progressT3.10.2.md.
[TestClass]
public class Cs10GlobalUsingTests
{
    // === POSITIVE (version 10): a simple global using directive. ===
    // Roslyn ParseUsingDirective (LanguageParser.cs:949-951 global, 955 using, 1000 name, 1006 ;).

    [TestMethod]
    public void GlobalUsingSimple_Succeeds()
        => AssertParsesV10("global using System;");

    // === POSITIVE (version 10): a global static using directive. ===
    // The `static` keyword (LanguageParser.cs:956) is a reserved keyword (Cs1.grammar:619), so only the
    // GlobalStaticUsingDirective alternative matches (the open/alias alternatives cannot consume it).

    [TestMethod]
    public void GlobalUsingStatic_Succeeds()
        => AssertParsesV10("global using static System.Console;");

    // === POSITIVE (version 10): a global using with a dotted (qualified) name. ===
    // QualifiedName = TypeName NamespaceSegment* (Cs1.grammar:21, :25).

    [TestMethod]
    public void GlobalUsingDottedName_Succeeds()
        => AssertParsesV10("global using A.B.C;");

    // === POSITIVE (version 10): a global static using with a dotted name. ===

    [TestMethod]
    public void GlobalUsingStaticDottedName_Succeeds()
        => AssertParsesV10("global using static A.B.C.D;");

    // === POSITIVE (version 10): a global using followed by a type declaration. ===
    // Both are NamespaceMembers (Cs1.grammar:5-9); the global using is a top-level member.

    [TestMethod]
    public void GlobalUsingFollowedByType_Succeeds()
        => AssertParsesV10("global using System; class C { }");

    // === POSITIVE (version 10): multiple global using directives. ===

    [TestMethod]
    public void GlobalUsingMultiple_Succeeds()
        => AssertParsesV10("global using System; global using static System.Console;");

    // === POSITIVE (version 10): a global using ALIAS (mirrors the Cs1 AliasUsingDirective). ===
    // Roslyn: alias = IsNamedAssignment() ? ParseNameEquals() : null (LanguageParser.cs:967).

    [TestMethod]
    public void GlobalUsingAlias_Succeeds()
        => AssertParsesV10("global using X = System.Console;");

    // === POSITIVE (version 10): a NON-global using directive still parses (regression). ===
    // The Cs1 UsingDirective (Cs1.grammar:11-13) is untouched; it starts with "using", this one with
    // "global", so the two never overlap.

    [TestMethod]
    public void NonGlobalUsing_StillSucceeds()
        => AssertParsesV10("using System;");

    // === NEGATIVE (version 9, version-purity): global using is not available at v9. ===
    // At v9 the GlobalUsingDirective alternative is ABSENT (Cs10-only). `global` is not a reserved
    // keyword (absent from Cs1.grammar:556-637) but it is a bare Identifier that no NamespaceMember
    // alternative consumes -> `NamespaceMember*` matches zero members, `global using ...` is
    // unconsumed -> REJECTS. At v10 it PARSES.

    [TestMethod]
    public void GlobalUsingSimple_RejectedAtV9()
        => AssertFailsV9("global using System;");

    [TestMethod]
    public void GlobalUsingStatic_RejectedAtV9()
        => AssertFailsV9("global using static System.Console;");

    // === helpers ===

    private static void AssertParsesV10(string input) => AssertParses(CSharpVersionTestHelper.CreateParser(10), input);

    private static void AssertFailsV9(string input) => AssertFails(CSharpVersionTestHelper.CreateParser(9), input);

    private static void AssertParses(CSharpParser parser, string input)
    {
        var result = parser.Parse(input, "Grammar", out _);
        var success = result.TryGetSuccess(out var node, out var end)
            && end == input.Length
            && parser.Parser.ErrorInfo is null
            && parser.Parser.RecoveryDiagnostics.Count == 0;

        Assert.IsTrue(success,
            $"Expected «{input}» to fully parse (end={end}/{input.Length}, errorPos={parser.Parser.ErrorPos})");
        Assert.IsNotNull(node);
    }

    private static void AssertFails(CSharpParser parser, string input)
    {
        var result = parser.Parse(input, "Grammar", out _);
        Assert.IsFalse(
            result.TryGetSuccess(out _, out var end) && end == input.Length
                && parser.Parser.RecoveryDiagnostics.Count == 0,
            $"Expected parse failure or recovery diagnostics for: {input}");
    }
}
