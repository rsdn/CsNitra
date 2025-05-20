using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

using ExtensibleParaser;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DotParser;

public abstract record DotAst
{
    public abstract override string ToString();
}

public record DotGraph(string Name, IReadOnlyList<DotStatement> Statements) : DotAst
{
    public override string ToString() => $"digraph {Name} {{\n{string.Join("\n", Statements)}\n}}";
}

public abstract record DotStatement : DotAst;

public record DotNodeStatement(string NodeId, IReadOnlyList<DotAttribute> Attributes) : DotStatement
{
    public override string ToString() => $"{NodeId} {AttributesToString()};";
    private string AttributesToString() => Attributes.Count > 0
        ? $"[{string.Join(", ", Attributes)}]"
        : "";
}

public record DotEdgeStatement(string FromNode, string ToNode, IReadOnlyList<DotAttribute> Attributes) : DotStatement
{
    public override string ToString() => $"{FromNode} -> {ToNode} {AttributesToString()};";
    private string AttributesToString() => Attributes.Count > 0
        ? $"[{string.Join(", ", Attributes)}]"
        : "";
}

public record DotSubgraph(string Name, IReadOnlyList<DotStatement> Statements) : DotStatement
{
    public override string ToString() => $"subgraph {Name} {{\n{string.Join("\n", Statements)}\n}}";
}

public record DotAttribute(string Name, string Value) : DotAst
{
    public override string ToString() => $"{Name}={Value}";
}

public record DotAssignment(string Name, string Value) : DotStatement
{
    public override string ToString() => $"{Name}={Value};";
}

[TerminalMatcher]
public sealed partial class DotTerminals
{
    [Regex(@"[\l_]\w*")]
    public static partial Terminal Identifier();

    [Regex(@"""[^""]*""")]
    public static partial Terminal QuotedString();

    [Regex(@"\d+")]
    public static partial Terminal Number();

    [Regex(@"(\s*(\/\/[^\n]*)|\s+)*")]
    public static partial Terminal Trivia();
}

public class DotParser
{
    private readonly Parser _parser;

    public DotParser()
    {
        _parser = new Parser(DotTerminals.Trivia());

        _parser.Rules["Graph"] = new Rule[]
        {
            new Seq([
                new Literal("digraph"),
                DotTerminals.Identifier(),
                new Literal("{"),
                new ZeroOrMany(new Ref("Statement")),
                new Literal("}")
            ], "Graph")
        };

        _parser.Rules["Statement"] = new Rule[]
        {
            new Ref("NodeStatement"),
            new Ref("EdgeStatement"),
            new Ref("Subgraph"),
            new Ref("Assignment"),
            new Ref("AttributeList")
        };

        _parser.Rules["NodeStatement"] = new Rule[]
        {
            new Seq([
                DotTerminals.Identifier(),
                new Optional(new Ref("AttributeList")),
                new Literal(";")
            ], "NodeStatement")
        };

        _parser.Rules["EdgeStatement"] = new Rule[]
        {
            new Seq([
                DotTerminals.Identifier(),
                new Literal("->"),
                DotTerminals.Identifier(),
                new Optional(new Ref("AttributeList")),
                new Literal(";")
            ], "EdgeStatement")
        };

        _parser.Rules["Subgraph"] = new Rule[]
        {
            new Seq([
                new Literal("subgraph"),
                DotTerminals.Identifier(),
                new Literal("{"),
                new ZeroOrMany(new Ref("Statement")),
                new Literal("}")
            ], "Subgraph")
        };

        _parser.Rules["Assignment"] = new Rule[]
        {
            new Seq([
                DotTerminals.Identifier(),
                new Literal("="),
                new Ref("Value"),
                new Literal(";")
            ], "Assignment")
        };

        _parser.Rules["AttributeList"] = new Rule[]
        {
            new Seq([
                new Literal("["),
                new Ref("Attribute"),
                new ZeroOrMany(new Seq([
                    new Literal(","),
                    new Ref("Attribute")
                ], "AttributeRest")),
                new Literal("]")
            ], "AttributeList")
        };

        _parser.Rules["Attribute"] = new Rule[]
        {
            new Seq([
                DotTerminals.Identifier(),
                new Literal("="),
                new Ref("Value")
            ], "Attribute")
        };

        _parser.Rules["Value"] = new Rule[]
        {
            DotTerminals.Identifier(),
            DotTerminals.QuotedString(),
            DotTerminals.Number()
        };

        _parser.BuildTdoppRules();
    }

    public DotGraph Parse(string input)
    {
        var result = _parser.Parse(input, "Graph", out _);
        if (!result.TryGetSuccess(out var node, out _))
        {

            throw new InternalTestFailureException($"❌ Parse FAILED. {_parser.ErrorInfo.GetErrorText()}");
        }

        var visitor = new DotVisitor(input);
        node.Accept(visitor);
        return visitor.Result.AssertIsNonNull();
    }

    private class DotVisitor(string input) : ISyntaxVisitor
    {
        public DotGraph? Result { get; private set; }

        public void Visit(TerminalNode _)
        {
            // Terminal nodes are handled in SeqNode visitor
        }

        public void Visit(SeqNode node)
        {
            var children = (List<DotAst>)node.Elements
                .Select(e =>
                {
                    e.Accept(this);
                    return CurrentResult;
                })
                .Where(r => r != null)
                .ToList()!;

            CurrentResult = node.Kind switch
            {
                "Graph" => new DotGraph(
                    children[1].ToString(),
                    children.Skip(3).Take(children.Count - 4).Cast<DotStatement>().ToList()
                ),
                "NodeStatement" => new DotNodeStatement(
                    children[0].ToString(),
                    children.Count > 1 ? ((DotAttributeList)children[1]).Attributes : new List<DotAttribute>()
                ),
                "EdgeStatement" => new DotEdgeStatement(
                    children[0].ToString(),
                    children[2].ToString(),
                    children.Count > 3 ? ((DotAttributeList)children[3]).Attributes : new List<DotAttribute>()
                ),
                "Subgraph" => new DotSubgraph(
                    children[1].ToString(),
                    children.Skip(3).Take(children.Count - 4).Cast<DotStatement>().ToList()
                ),
                "Assignment" => new DotAssignment(
                    children[0].ToString(),
                    children[2].ToString()
                ),
                "AttributeList" => new DotAttributeList(
                    GetAttributes(children)
                ),
                "Attribute" => new DotAttribute(
                    children[0].ToString(),
                    children[2].ToString()
                ),
                _ => new DotValue(node.AsSpan(input).ToString())
            };

            if (node.Kind == "Graph")
                Result = (DotGraph)CurrentResult;
        }

        private List<DotAttribute> GetAttributes(List<DotAst> children)
        {
            var attributes = new List<DotAttribute>();

            if (children.Count > 1 && children[1] is DotAttribute firstAttr)
                attributes.Add(firstAttr);

            for (int i = 2; i < children.Count; i++)
                if (children[i] is DotAttributeRest rest && rest.Attribute != null)
                    attributes.Add(rest.Attribute);

            return attributes;
        }

        public void Visit(SomeNode node)
        {
            node.Value.Accept(this);
            CurrentResult ??= new DotValue("null");
        }

        public void Visit(NoneNode _)
        {
            CurrentResult = null;
        }

        private DotAst? CurrentResult { get; set; }

        // Helper records for intermediate AST nodes
        private record DotAttributeList(IReadOnlyList<DotAttribute> Attributes) : DotAst;
        private record DotAttributeRest(DotAttribute Attribute) : DotAst;
        private record DotValue(string Value) : DotAst
        {
            public override string ToString() => Value;
        }
    }
}

public static class DotParserExtensions
{
    public static DotGraph ParseDotGraph(this string input)
    {
        var parser = new DotParser();
        return parser.Parse(input);
    }
}

[TestClass]
public class DotTests
{
    [TestMethod]
    public void ParseComplexGraph()
    {
        var graph = ParseComplexGraph_DotText.ParseDotGraph();

        // Проверяем количество узлов
        var nodeStatements = graph.Statements.OfType<DotNodeStatement>().Count();
        var subgraphs = graph.Statements.OfType<DotSubgraph>().ToList();
        var nodesInSubgraphs = subgraphs
            .SelectMany(sg => sg.Statements.OfType<DotNodeStatement>())
            .Count();

        Assert.AreEqual(2, nodeStatements, "Количество узлов в основном графе");
        Assert.AreEqual(5, subgraphs.Count, "Количество подграфов");
        Assert.AreEqual(17, nodesInSubgraphs, "Количество узлов в подграфах");

        // Проверяем количество переходов
        var edgeStatements = graph.Statements.OfType<DotEdgeStatement>().Count();
        var edgesInSubgraphs = subgraphs
            .SelectMany(sg => sg.Statements.OfType<DotEdgeStatement>())
            .Count();

        Assert.AreEqual(3, edgeStatements, "Количество переходов в основном графе");
        Assert.AreEqual(20, edgesInSubgraphs, "Количество переходов в подграфах");

        // Проверяем наличие атрибута ТестовыйАтрибут=42
        var allEdges = graph.Statements.OfType<DotEdgeStatement>()
            .Concat(subgraphs.SelectMany(sg => sg.Statements.OfType<DotEdgeStatement>()))
            .ToList();

        var targetEdge = allEdges.FirstOrDefault(e =>
            e.FromNode == "PRInMaster" &&
            e.ToNode == "AutoCherrypick");

        Assert.IsNotNull(targetEdge, "Не найден переход PRInMaster -> AutoCherrypick");
        Assert.IsTrue(targetEdge.Attributes.Any(a =>
            a.Name == "ТестовыйАтрибут" && a.Value == "42"),
            "Не найден атрибут ТестовыйАтрибут=42");
    }

    private const string ParseComplexGraph_DotText = """
digraph BugTrackingProcess
{
    rankdir=TB;  // Top to Bottom
    node [shape=box, style="rounded,filled"];
    edge [fontsize=10];
                
    // Состояния с цветами
    NewBug [label="Новый баг", fillcolor="white", style="filled"];
    Closed [label="Закрыто", fillcolor="lightgreen", style="filled"];

    subgraph cluster_management
    {
        label="Management Process";
        style=filled;
        UnderTriage [label="Анализ разгребальщиком"];
        AssignedToDev [label="Назначен разработчику"];
        Escalated [label="Эскалация"];
    }

    // Кластер для процесса разработки
    subgraph cluster_development
    {
        label="Development Process";
        style=filled;
        fillcolor=lightgrey;
        DevWorking [label="Разработчик разбирается с багом"];
        DevTimeout [label="Таймаут разработки"];
        PRInMaster [label="PR в master", fillcolor="lightyellow", style="filled"];
        PRInRelease [label="PR в релизную ветку", fillcolor="lightyellow", style="filled"];
    }

    // Кластер для Cherry-pick
    subgraph cluster_cherrypick
    {
        label="Cherry-pick Process (отдельный BUG)";
        style=filled;
        fillcolor=lightgrey;
        AutoCherrypick [label="Автоматический cherry-pick", fillcolor="lightyellow", style="filled"];
        ManualCherrypick [label="Ручной cherry-pick", fillcolor="lightyellow"];
        PRCherrypickInRelease [label="PR в релизную ветку", fillcolor="lightyellow", style="filled"];
        ManualCherrypickTimeout [label="Таймаут ручного cherry-pick", fillcolor="lightyellow"];
    }

    // Кластер для ожидания релизного билда
    subgraph cluster_build
    {
        label="Release Build Process";
        style=filled;
        fillcolor=lightgrey;
        BuildStarted [label="Сборка запущена"];
        BuildReady [label="Сборка готова"];
        ModuleReady [label="Модуль готов"];
    }

    // Кластер для тестирования
    subgraph cluster_testing
    {
        label="Testing Process";
        style=filled;
        fillcolor=lightgrey;
        QATesting [label="Тестирование"];
        QATimeout [label="Таймаут тестирования"];
    }

    // Основные переходы (черные)
    NewBug -> UnderTriage [label="Назначен разгребальщик"];
    UnderTriage -> AssignedToDev [label="Назначен разработчик"];
    AssignedToDev -> DevWorking [label="Разработчик принял задачу"];
                
    // Процесс разработки
    DevWorking -> DevTimeout [label="Таймаут 2 часа"];
    DevTimeout -> DevWorking [label="Запрошено доп. время"];
                
    // Результатом работы программиста может быть:
    DevWorking -> QATesting [label="Баг не требует исправления (ResolvedNotFixed)"];
    DevWorking -> PRInMaster [label="PR в master"];
    DevWorking -> PRInRelease [label="PR в релизную ветку"];
                
    // Процесс PR в master
    PRInMaster -> AutoCherrypick [label="PR master прошёл успешно", ТестовыйАтрибут=42];
    PRInMaster -> DevWorking [label="PR упал", fontcolor=goldenrod, color=goldenrod];
                
    // Процесс автоматического cherry-pick
    AutoCherrypick -> PRCherrypickInRelease [label="Cherry-pick успешен"];
    AutoCherrypick -> ManualCherrypick [label="Автоматический Cherry-pick упал"];
                
    // Процесс ручного cherry-pick
    ManualCherrypick -> ManualCherrypickTimeout [label="Таймаут 2 часа"];
    ManualCherrypickTimeout -> ManualCherrypick [label="Запрошено доп. время"];
    ManualCherrypickTimeout -> Escalated [label="Нет ответа (1 час)", color=red, fontcolor=red];
    ManualCherrypick -> PRCherrypickInRelease [label="Создан PR в релизную ветку"];
    PRCherrypickInRelease -> BuildStarted [label="Cherry-pick-PR прошёл успешно (ResolvedFixed)"];

                
    // Процесс PR в релизной ветке
    PRInRelease -> BuildStarted [label="PR прошёл успешно (ResolvedFixed)"];
    PRInRelease -> DevWorking [label="PR упал", fontcolor=goldenrod, color=goldenrod];
                
    // Процесс сборки
    BuildStarted -> BuildReady [label="Сборка успешна"];
    BuildStarted -> ModuleReady [label="Собрался нужный модуль"];
    BuildReady -> QATesting [label="Передача тестеру сборки", color=blue, fontcolor=blue];
    ModuleReady -> QATesting [label="Передача тестеру модуля", color=blue, fontcolor=blue];
                
    // Процесс тестирования
    QATesting -> QATimeout [label="Таймаут 30 мин"];
    QATimeout -> QATesting [label="Запрошено доп. время"];
    QATesting -> Closed [label="Закрыто", color=green, fontcolor=green];
    QATesting -> DevWorking [label="Отклонено (Вернули разработчику)", fontcolor=goldenrod, color=goldenrod];
                
    // Эскалации (все красные)
    DevTimeout -> Escalated [label="Нет ответа (1 час)", color=red, fontcolor=red];
    UnderTriage -> Escalated [label="Таймаут 30 мин", color=red, fontcolor=red];
    AssignedToDev -> Escalated [label="Таймаут 30 мин", color=red, fontcolor=red];
    BuildStarted -> Escalated [label="Таймаут 4 часа", color=red, fontcolor=red];
    QATimeout -> Escalated [label="Нет ответа (30 мин)", color=red, fontcolor=red];
                
    // Обработка эскалации
    Escalated -> UnderTriage [label="Решение руководителя"];
    Escalated -> DevWorking [label="Решение руководителя"];
    Escalated -> QATesting [label="Решение руководителя"];
    Escalated -> Closed [label="Руководитель закрыл баг", color=green, fontcolor=green];
}
""";
}
