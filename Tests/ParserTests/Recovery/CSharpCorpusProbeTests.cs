#nullable enable

using CSharpGrammar;
using CsPreprocessor;
using ExtensibleParser;
using ExtensibleParser.Recovery;
using System.Diagnostics;
using System.Reflection;

namespace Recovery;

[TestClass]
public sealed class CSharpCorpusProbeTests
{
    [TestMethod]
    [TestCategory("Probe")]
    [Ignore("Manual diagnostic: require CSNITRA_ROSLYN_ROOT; not part of the normal suite")]
    public void Test_CSharp_Corpus_Probe()
    {
        var root = ResolveRoslynRoot();
        if (root is null)
        {
            Trace.WriteLine("Roslyn root not found, skipping V1");
            return;
        }

        string[] corpus =
        [
            @"src\Compilers\CSharp\Test\Semantic\Semantics\NullableReferenceTypesTests.cs",
            @"src\Compilers\CSharp\Test\Symbol\Symbols\DefaultInterfaceImplementationTests.cs",
            @"src\Compilers\CSharp\Test\Emit3\Semantics\CollectionExpressionTests.cs",
            @"src\Compilers\CSharp\Test\Emit3\Semantics\ExtensionTests.cs",
            @"src\Compilers\CSharp\Portable\Generated\CSharpSyntaxGenerator\CSharpSyntaxGenerator.SourceGenerator\Syntax.xml.Internal.Generated.cs",
            @"src\Compilers\CSharp\Test\CSharp15\UnionsTests.cs",
            @"src\Compilers\CSharp\Test\Symbol\Symbols\StaticAbstractMembersInInterfacesTests.cs",
            @"src\Compilers\CSharp\Test\Emit3\Semantics\OutVarTests.cs",
            @"src\Compilers\CSharp\Test\Semantic\Semantics\RefFieldTests.cs",
            @"src\Compilers\CSharp\Test\CSharp15\ExtensionIndexersTests.cs",
        ];

        string[] symbols = ["NET", "NETCOREAPP", "DEBUG", "TRACE", "NET8_0_OR_GREATER"];

        var processed = 0;
        var skipped = 0;
        var timedOut = 0;

        foreach (var relativePath in corpus)
        {
            var fileInfo = new FileInfo(Path.Combine(root, relativePath));
            if (!fileInfo.Exists)
            {
                Trace.WriteLine($"{relativePath}: not found, skipping");
                skipped++;
                continue;
            }

            var input = File.ReadAllText(fileInfo.FullName);
            var pp = Preprocessor.Run(input, symbols);
            var parser = CreateParser();
            var memBefore = GC.GetTotalMemory(true);

            var stopwatch = Stopwatch.StartNew();
            var task = Task.Run(() => parser.Parse(pp.Text, "Grammar", out _));
            var completed = task.Wait(TimeSpan.FromSeconds(60));
            stopwatch.Stop();

            if (!completed)
            {
                Trace.WriteLine($"{fileInfo.Name}: parse > 60s, aborted");
                timedOut++;
                continue;
            }

            var memAfter = GC.GetTotalMemory(true);
            var success = task.Result.TryGetSuccess(out var rootNode, out var end);
            var successAtEof = success && end == pp.Text.Length;
            var recoveryNodes = success ? CostCalculator.CountRecoveryNodes(rootNode!) : 0;
            var ppDiag = pp.Diagnostics.Count > 0 ? $" ppDiag={pp.Diagnostics.Count}" : "";
            var unrecovered = parser.Parser.RecoveryDiagnostics.Count(d => d.Kind == RecoveryKind.Unrecovered);
            var skippedDiag = parser.Parser.RecoveryDiagnostics.Count(d => d.Kind == RecoveryKind.Skipped);
            var absorberBytes = success ? AbsorberBytes(rootNode!) : 0;
            var absorberFraction = absorberBytes / (double)pp.Text.Length;
            var depth = success ? TreeDepth(rootNode!) : 0;
            Trace.WriteLine(
                $"{fileInfo.Name} sizeBytes={fileInfo.Length} elapsedMs={stopwatch.ElapsedMilliseconds} " +
                $"SuccessAtEof={successAtEof} RecoveryPasses={parser.Parser.RecoveryPasses} " +
                $"EngineGenerateCalls={parser.Parser.EngineGenerateCalls} Memo={parser.Parser.Memo.Count} " +
                $"RecoveryNodes={recoveryNodes}{ppDiag} " +
                $"unrecovered={unrecovered} skipped={skippedDiag} " +
                $"S3ScanPositions={parser.Parser.S3ScanPositions} " +
                $"absorberBytes={absorberBytes} absorberFraction={absorberFraction:P0} " +
                $"depth={depth} " +
                $"memBefore={memBefore} memAfter={memAfter} delta={memAfter - memBefore}");
            processed++;
        }

        Trace.WriteLine($"summary: processed={processed} skipped={skipped} timedOut={timedOut}");
    }

    private static int AbsorberBytes(ISyntaxNode node)
    {
        var bytes = node is TerminalNode { IsAbsorber: true } t ? t.EndPos - t.StartPos : 0;
        switch (node)
        {
            case SeqNode seq:
                foreach (var el in seq.RawElements)
                    bytes += AbsorberBytes(el);
                break;
            case ListNode list:
                foreach (var el in list.RawElements)
                    bytes += AbsorberBytes(el);
                foreach (var d in list.Delimiters)
                    bytes += AbsorberBytes(d);
                break;
            case SomeNode some:
                bytes += AbsorberBytes(some.Value);
                break;
        }
        return bytes;
    }

    private static int TreeDepth(ISyntaxNode node)
    {
        var maxChild = 0;
        switch (node)
        {
            case SeqNode seq:
                foreach (var el in seq.RawElements)
                    maxChild = Math.Max(maxChild, TreeDepth(el));
                break;
            case ListNode list:
                foreach (var el in list.RawElements)
                    maxChild = Math.Max(maxChild, TreeDepth(el));
                foreach (var d in list.Delimiters)
                    maxChild = Math.Max(maxChild, TreeDepth(d));
                break;
            case SomeNode some:
                maxChild = Math.Max(maxChild, TreeDepth(some.Value));
                break;
        }
        return 1 + maxChild;
    }

    private static CSharpParser CreateParser()
    {
        var assembly = typeof(CSharpParser).Assembly;
        var grammars = Enumerable.Range(1, 14)
            .Select(version => (Text: LoadGrammar(assembly, $"Cs{version}.grammar"), Path: $"Cs{version}.grammar"))
            .ToArray();
        return new CSharpParser(grammars, CSharpTerminals.Trivia(), CSharpTerminals.GetAll());
    }

    private static string LoadGrammar(Assembly assembly, string suffix)
    {
        var resourceName = assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found in {assembly.FullName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string? ResolveRoslynRoot()
    {
        var fromEnv = Environment.GetEnvironmentVariable("CSNITRA_ROSLYN_ROOT");
        if (!string.IsNullOrWhiteSpace(fromEnv) && Directory.Exists(fromEnv))
            return fromEnv;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
            {
                var sibling = dir.Parent is null
                    ? null
                    : Path.Combine(dir.Parent.FullName, "roslyn");
                return sibling is not null && Directory.Exists(sibling)
                    ? sibling
                    : null;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
