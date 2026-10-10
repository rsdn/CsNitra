using CSharpGrammar;
using ExtensibleParser;
using ExtensibleParser.Recovery;
using System.Diagnostics;

namespace CSharpGrammarTests;

/// <summary>
/// Ручная диагностика: измеряет, как грамматика Cs1..Cs14 вместе с механизмом
/// восстановления ведёт себя на реальных файлах Roslyn. Успех на EOF сам по себе
/// ещё не является успехом — вердикты <see cref="ParseHealth"/> обнажают FalseGreen,
/// когда большая часть входа поглощена как мусор. Ассертов у зонда нет намеренно:
/// он помечен [Ignore] и выполняется только при заданной CSNITRA_ROSLYN_ROOT.
/// </summary>
[TestClass]
public sealed class CSharpCorpusHealthProbeTests
{
    [TestMethod]
    [TestCategory("Probe")]
    [Ignore("Manual diagnostic: require CSNITRA_ROSLYN_ROOT; not part of the normal suite")]
    public void Test_CSharp_Corpus_Health_Probe()
    {
        var root = Environment.GetEnvironmentVariable("CSNITRA_ROSLYN_ROOT");
        if (string.IsNullOrEmpty(root))
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
                {
                    root = Path.Combine(directory.FullName, "roslyn");
                    break;
                }
                directory = directory.Parent;
            }
        }

        if (root is null || !Directory.Exists(root))
        {
            Trace.WriteLine("Roslyn root not found, skipping");
            return;
        }

        var grammars = EmbeddedGrammar.LoadGrammarUpTo(14);
        var corpus = new[]
        {
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
        };

        var processed = 0;
        var clean = 0;
        var recovered = 0;
        var falseGreen = 0;
        var uncovered = 0;
        var failed = 0;

        foreach (var rel in corpus)
        {
            var path = Path.Combine(root, rel);
            if (!File.Exists(path))
            {
                Trace.WriteLine($"{rel}: file not found, skipping");
                continue;
            }

            var name = Path.GetFileName(path);
            var input = File.ReadAllText(path);
            var parser = new CSharpParser(grammars, CSharpTerminals.Trivia(), CSharpTerminals.GetAll());

            var sw = Stopwatch.StartNew();
            var task = Task.Run(() => parser.Parse(input, "Grammar", out _));
            if (!task.Wait(TimeSpan.FromSeconds(60)))
            {
                Trace.WriteLine($"{name}: parse > 60s, aborted");
                continue;
            }

            var result = task.Result;
            var success = result.TryGetSuccess(out var node, out var end) && end == input.Length;
            var health = ParseHealth.Analyze(success ? node : null, input, success, parser.Parser.Trivia);

            processed++;
            switch (health.Verdict)
            {
                case ParseVerdict.Clean:
                    clean++;
                    break;
                case ParseVerdict.Recovered:
                    recovered++;
                    break;
                case ParseVerdict.FalseGreen:
                    falseGreen++;
                    break;
                case ParseVerdict.Uncovered:
                    uncovered++;
                    break;
                case ParseVerdict.Failed:
                    failed++;
                    break;
            }

            Trace.WriteLine($"{name} size={input.Length} elapsedMs={sw.ElapsedMilliseconds} verdict={health.Verdict} coversInput={health.CoversInput} absorberFraction={health.AbsorberFraction}% absorberBytes={health.AbsorberBytes} unionCoverage={health.UnionCoverage} recoveryNodes={health.RecoveryNodes} success={health.SuccessAtEof} passes={parser.Parser.RecoveryPasses} memo={parser.Parser.Memo.Count}");
        }

        Trace.WriteLine($"processed={processed} clean={clean} recovered={recovered} falseGreen={falseGreen} uncovered={uncovered} failed={failed}");
    }
}
