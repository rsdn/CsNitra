#nullable enable

using ExtensibleParser;
using ExtensibleParser.Recovery;
using System.Diagnostics;

namespace Recovery;

[TerminalMatcher]
public sealed partial class UsingStaticTerminals
{
    [Regex(@"[_\l]\w*")]
    public static partial Terminal Ident();

    [Regex(@"\s*")]
    public static partial Terminal Trivia();
}

// Мини-грамматика в духе Cs1..Cs14: она НЕ знает директиву `using static`.
// Разбор обязан споткнуться на слове `static`:TypeName = NotPredicate(ReservedKeyword) + Ident
// не пропускает зарезервированное слово в имя, поэтому `static` — барьер, а не имя.
// Recovery должен пересинхронизироваться на якоре `namespace N;`, и тогда абсорбер покрывает
// только участок `using static Foo.Bar;` (~22 символа из 60, доля ~37% → вердикт Recovered).
// Если же recovery сглотнет весь вход до EOF, доля станет 100% → вердикт FalseGreen → тест
// падает. Именно это и есть честный ответ на вопрос, решает ли ветка проблему `using static`
// на минимальном входе.
[TestClass]
public sealed class UsingStaticRecoveryTests
{
    // Module = CompilationUnit; CompilationUnit = ZeroOrMany(NamespaceMember);
    // NamespaceMember = using QualifiedName ; | namespace ident ; | class ident { }
    private static Parser NewParser()
    {
        var parser = new Parser(UsingStaticTerminals.Trivia());
        parser.Rules["Module"] = [new Ref("CompilationUnit")];
        parser.Rules["CompilationUnit"] = [new ZeroOrMany(new Ref("NamespaceMember"), "NamespaceMembers")];
        parser.Rules["NamespaceMember"] =
        [
            new Seq([new Literal("using"), new Ref("QualifiedName"), new Literal(";")], "UsingDirective"),
            new Seq([new Literal("namespace"), UsingStaticTerminals.Ident(), new Literal(";")], "NamespaceDeclaration"),
            new Seq([new Literal("class"), UsingStaticTerminals.Ident(), new Literal("{"), new Literal("}")], "ClassDeclaration"),
        ];
        parser.Rules["QualifiedName"] =
        [
            new Seq([new Ref("TypeName"), new ZeroOrMany(new Seq([new Literal("."), new Ref("TypeName")], "QualSegment"))], "QualifiedName"),
        ];
        parser.Rules["TypeName"] = [new Seq([new NotPredicate(new Ref("ReservedKeyword")), UsingStaticTerminals.Ident()], "TypeName")];
        parser.Rules["ReservedKeyword"] = [new Literal("static")];
        parser.BuildTdoppRules();
        return parser;
    }

    [TestMethod]
    public void Test_UsingStatic_GarbageNotAbsorbedToEof()
    {
        var parser = NewParser();
        var input = "namespace Pre; using static Foo.Bar; namespace N; class C {}";
        var result = parser.Parse(input, "Module", out _);

        Assert.IsTrue(result.TryGetSuccess(out var node, out var end) && end == input.Length,
            $"Expected Success@EOF, got {result.ResultKind}@{result.NewPos}/{result.MaxFailPos} len={input.Length} " +
            $"ErrorInfo={parser.ErrorInfo?.Pos} passes={parser.RecoveryPasses}");

        var health = ParseHealth.Analyze(node, input, end == input.Length, parser.Trivia);
        Assert.AreNotEqual(ParseVerdict.FalseGreen, health.Verdict, $"recovery absorbed the file: {health}");
    }

    // F9 fix C «в серии»: ДВА неграмматичных участка `using static`, разделённых валидными
    // объявлениями. Ожидание от fix C: recovery пересинхронизируется на каждом якоре
    // `namespace N;`, абсорберы остаются локальными (два маленьких), вердикт Recovered.
    // Тест — маркер границы возможностей: ассерты не ослаблять, значения фиксировать как есть.
    [TestMethod]
    public void Test_UsingStaticSeries_TwoGarbageRegions()
    {
        var parser = NewParser();
        var input = "namespace A; using static Foo; namespace B; using static Bar; namespace C; class D {}";
        var result = parser.Parse(input, "Module", out _);

        Assert.IsTrue(result.TryGetSuccess(out var node, out var end) && end == input.Length,
            $"Expected Success@EOF, got {result.ResultKind}@{result.NewPos}/{result.MaxFailPos} len={input.Length} " +
            $"ErrorInfo={parser.ErrorInfo?.Pos} passes={parser.RecoveryPasses}");

        var health = ParseHealth.Analyze(node, input, end == input.Length, parser.Trivia);
        Assert.IsTrue(health.CoversInput, $"tree does not cover the input (I4): {health}");
        Assert.AreNotEqual(ParseVerdict.FalseGreen, health.Verdict, $"recovery absorbed the file: {health}");

        Trace.WriteLine($"input=«{input}» len={input.Length} result={result.ResultKind}@{result.NewPos} " +
            $"maxFailPos={result.MaxFailPos} passes={parser.RecoveryPasses}");

        Trace.WriteLine("=== parse tree (all nodes) ===");
        if (node is null)
            Trace.WriteLine("(null tree)");
        else
            DumpTree(node, 0);

        Trace.WriteLine("=== recovery diagnostics ===");
        foreach (var diag in parser.RecoveryDiagnostics)
            Trace.WriteLine($"{diag.Kind} [{diag.StartPos}..{diag.EndPos}) terminal={diag.Terminal?.Kind ?? "-"} rule={diag.RuleName ?? "-"}");

        Trace.WriteLine($"health: verdict={health.Verdict} absorberBytes={health.AbsorberBytes} " +
            $"absorberFraction={health.AbsorberFraction}% coversInput={health.CoversInput} " +
            $"unionCoverage={health.UnionCoverage} recoveryNodes={health.RecoveryNodes}");

        void DumpTree(ISyntaxNode n, int depth)
        {
            var absorber = n is TerminalNode { IsAbsorber: true } ? " IsAbsorber=true" : "";
            Trace.WriteLine($"{new string(' ', depth * 2)}{n.Kind} [{n.StartPos}..{n.EndPos}){absorber}");
            switch (n)
            {
                case SeqNode seq:
                    foreach (var el in seq.RawElements)
                        DumpTree(el, depth + 1);
                    break;
                case ListNode list:
                    foreach (var el in list.RawElements)
                        DumpTree(el, depth + 1);
                    foreach (var delim in list.Delimiters)
                        DumpTree(delim, depth + 1);
                    break;
                case SomeNode some:
                    DumpTree(some.Value, depth + 1);
                    break;
            }
        }
    }
}
