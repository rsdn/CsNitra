using System.Text;
using CsPreprocessor;
using CSharpGrammar;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CsPreprocessorTests;

// T5.1 — HARDENING. Runs the preprocessor (and, where the C# parser can handle the file, the real
// CSharpParser) over REAL .cs files from this repo and asserts:
//   (a) the preprocessor never crashes and preserves the source length (D2 same-length), and
//   (b) blanking does not corrupt the kept code (every char is either unchanged or blanked to a
//       space), and, for files the C# parser can parse, the preprocessed Text still parses cleanly.
//
// NOTE on the parseable subset: CSharpGrammarLoader.CreateCSharpParser() loads Cs1.grammar (C# 1.0).
// Every real .cs file in this repo is modern C# (file-scoped namespace, records, primary
// constructors, generics, string interpolation, lambdas, `global using`, NRT `?`) — all Cs2+
// constructs the C# 1.0 grammar rejects. So NO real file parses cleanly here; the parseable subset
// (ParseableFiles) is intentionally empty and the clean-parse assertion is a documented no-op. The
// strong real-file guarantee is the "does not corrupt kept code" invariant, which runs on ALL files.
// See docs/CsPreprocessor-progressT5.1.md for the per-file exclusion notes.
[TestClass]
public sealed class RealFileHardeningTests
{
    // Real repo .cs files (relative to the repo root). A representative mix of directive files
    // (#if/#else/#endif, #pragma) and plain code files (no preprocessor directives).
    private static readonly string[] AllFiles =
    [
        "ExtensibleParser/Extensions.cs",             // #if NETSTANDARD2_0 / #else / #endif
        "Shared/NetStandard2_0Support.cs",            // #if NATIVEAOT, #if !NETSTANDARD2_0, #pragma warning
        "ExtensibleParser/Parser.Recovery.cs",        // plain (recovery hooks)
        "Parsers/Json/Ast.cs",                        // plain (records)
        "Parsers/Json/JsonParser.cs",                 // plain
        "Parsers/Dot/DotParser/DotAst.cs",            // plain (records)
        "Parsers/Dot/DotParser/DotVisitor.cs",        // plain (modern)
        "Parsers/Cpp/CppSimplifiedParser/CppAst.cs",  // plain (records, raw string literals)
        "Parsers/Cpp/CppInteropGenerator/EnumGenerator.cs", // plain (modern)
    ];

    // Subset of AllFiles the C# 1.0 grammar (CSharpGrammarLoader.CreateCSharpParser) can parse
    // cleanly. EMPTY: every file above uses at least one Cs2+ construct the grammar rejects
    // (file-scoped `namespace X;` is the universal blocker, plus records/primary ctors/generics/
    // interpolation/lambdas). If a Cs1-parseable real file is ever added, list it here.
    private static readonly string[] ParseableFiles =
    [
    ];

    // File for the active/inactive sanity check: `#if NETSTANDARD2_0` is undefined by default, so
    // its branch is blanked and the `#else` branch is kept.
    private const string ActiveInactiveFile = "ExtensibleParser/Extensions.cs";

    private const string InactiveLine = "public static string Str(this CharsRef span) => span.ToString();";

    private const string ActiveLine = "public static ReadOnlySpan<char> Str(this ReadOnlySpan<char> span) => span;";

    // (a) No crash + same length: for every real file the preprocessor runs to completion and the
    //     preprocessed Text has the exact same length as the source (D2 same-length / identity map).
    [TestMethod]
    public void RealFile_DoesNotCrash_AndPreservesLength()
    {
        foreach (var file in AllFiles)
        {
            var content = ReadRealFile(file);
            var result = Preprocessor.Run(content, Array.Empty<string>());

            Assert.IsNotNull(result, $"{file}: Preprocessor.Run returned null");
            Assert.AreEqual(content.Length, result.Text.Length, $"{file}: Text.Length ({result.Text.Length}) != source.Length ({content.Length})");
        }
    }

    // (b) Blanking did not corrupt the kept code: the preprocessor's only mutation is blanking a
    //     char to a space, so at every index the Text is either byte-identical to the source or a
    //     space. This is asserted on ALL real files (independent of parser support). Then, for the
    //     parseable subset, the preprocessed Text must parse cleanly with the real C# parser.
    [TestMethod]
    public void RealFile_PreprocessedText_DoesNotCorruptAndParsesWhenSupported()
    {
        foreach (var file in AllFiles)
        {
            var content = ReadRealFile(file);
            var result = Preprocessor.Run(content, Array.Empty<string>());

            Assert.AreEqual(content.Length, result.Text.Length, $"{file}: length mismatch");
            var badIndex = FirstChangedKeptChar(content, result.Text);
            if (badIndex >= 0)
                Assert.Fail($"{file}: blanking changed a kept char at {badIndex} (source='{content[badIndex]}' text='{result.Text[badIndex]}')");
        }

        foreach (var file in ParseableFiles)
        {
            var content = ReadRealFile(file);
            var result = Preprocessor.Run(content, Array.Empty<string>());
            var parser = CSharpGrammarLoader.CreateCSharpParser();
            var parse = parser.Parse(result.Text, "Grammar", out _);

            Assert.IsTrue(parse.TryGetSuccess(out var node, out var end), $"{file}: parse failed at {parser.Parser.ErrorPos}");
            Assert.IsNotNull(node, $"{file}: parse returned null node");
            Assert.AreEqual(result.Text.Length, end, $"{file}: end {end} != Text.Length {result.Text.Length}");
            Assert.IsNull(parser.Parser.ErrorInfo, $"{file}: unexpected ErrorInfo at {parser.Parser.ErrorPos}");
            Assert.AreEqual(0, parser.Parser.RecoveryDiagnostics.Count, $"{file}: unexpected recovery diagnostics");
        }
    }

    // (c) Active/inactive sanity on a real #if file: with NETSTANDARD2_0 undefined, the inactive
    //     #if branch is blanked (Text differs from source there) and the active #else branch is
    //     kept byte-for-byte.
    [TestMethod]
    public void RealFile_InactiveIf_RegionIsBlanked_ActiveKept()
    {
        var content = ReadRealFile(ActiveInactiveFile);
        var result = Preprocessor.Run(content, Array.Empty<string>());

        Assert.AreEqual(content.Length, result.Text.Length, "length mismatch");
        Assert.IsTrue(IsBlanked(result.Text, content, InactiveLine),
            $"{ActiveInactiveFile}: inactive #if branch was not blanked");
        Assert.IsTrue(IsKept(result.Text, content, ActiveLine),
            $"{ActiveInactiveFile}: active #else branch was not kept");
    }

    private static string ReadRealFile(string relativePath)
    {
        var full = Path.Combine(FindRepoRoot(), relativePath);
        Assert.IsTrue(File.Exists(full), $"real file not found: {full}");
        return File.ReadAllText(full, Encoding.UTF8);
    }

    private static int FirstChangedKeptChar(string source, string text)
    {
        for (var i = 0; i < source.Length; i++)
        {
            var c = text[i];
            if (c != source[i] && c != ' ')
                return i;
        }

        return -1;
    }

    private static bool IsBlanked(string text, string source, string lineText)
    {
        var idx = source.IndexOf(lineText, StringComparison.Ordinal);
        if (idx < 0)
            return false;
        for (var i = idx; i < idx + lineText.Length; i++)
            if (text[i] != ' ')
                return false;
        return true;
    }

    private static bool IsKept(string text, string source, string lineText)
    {
        var idx = source.IndexOf(lineText, StringComparison.Ordinal);
        if (idx < 0)
            return false;
        for (var i = idx; i < idx + lineText.Length; i++)
            if (text[i] != source[i])
                return false;
        return true;
    }

    // Resolves the repo root robustly (the test working directory is not guaranteed to be the repo
    // root): walk up from the current directory and the test binary directory to the folder that
    // contains Nitra.sln.
    private static string FindRepoRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Nitra.sln")))
                    return dir.FullName;
                dir = dir.Parent;
            }
        }

        throw new DirectoryNotFoundException("Nitra.sln not found (cannot resolve repo root)");
    }
}
