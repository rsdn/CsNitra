using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
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

public abstract record DotTerminalNode(string Kind, int StartPos, int EndPos) : DotAst;

public record DotIdentifier(string Value, int StartPos, int EndPos) : DotTerminalNode("Identifier", StartPos, EndPos)
{
    public override string ToString() => Value;
}

public record DotQuotedString(string Value, string RawValue, int StartPos, int EndPos)
    : DotTerminalNode("QuotedString", StartPos, EndPos)
{
    public DotQuotedString(ReadOnlySpan<char> span, int startPos, int endPos)
        : this(ProcessQuotedString(span), span.Slice(1, span.Length - 2).ToString(), startPos, endPos)
    {
    }

    private static string ProcessQuotedString(ReadOnlySpan<char> span)
    {
        var content = span.Slice(1, span.Length - 2);
        var result = new StringBuilder();

        for (int i = 0; i < content.Length; i++)
        {
            if (content[i] == '\\' && i + 1 < content.Length)
            {
                var nextChar = content[i + 1];
                switch (nextChar)
                {
                    case 'n':
                        result.Append('\n');
                        i++;
                        break;
                    case 'r':
                        result.Append('\r');
                        i++;
                        break;
                    case 't':
                        result.Append('\t');
                        i++;
                        break;
                    case '\\':
                        result.Append('\\');
                        i++;
                        break;
                    case '"':
                        result.Append('"');
                        i++;
                        break;
                    case 'L':
                    case 'G':
                    case 'l':
                    case 'N':
                    case 'T':
                        result.Append('\\').Append(nextChar);
                        i++;
                        break;
                    default:
                        result.Append(nextChar);
                        i++;
                        break;
                }
            }
            else
            {
                result.Append(content[i]);
            }
        }

        return result.ToString();
    }

    public override string ToString() => $"\"{RawValue}\"";
}

public record DotNumber(int Value, int StartPos, int EndPos) : DotTerminalNode("Number", StartPos, EndPos)
{
    public override string ToString() => Value.ToString();
}

public record DotLiteral(string Value, int StartPos, int EndPos) : DotTerminalNode("Literal", StartPos, EndPos)
{
    public override string ToString() => Value;
}

public record DotAttributeList(IReadOnlyList<DotAttribute> Attributes) : DotAst;
public record DotAttributeRest(DotAttribute Attribute) : DotAst;


[TerminalMatcher]
public sealed partial class DotTerminals
{
    [Regex(@"[\l_]\w*")]
    public static partial Terminal Identifier();

    //[Regex(@"""[^""]*""")]
    //public static partial Terminal QuotedString();
    public static Terminal QuotedString() => _quotedString;

    [Regex(@"\d+")]
    public static partial Terminal Number();

    //[Regex(@"(\s*(\/\/[^\n]*)|\s+)*")]
    //public static partial Terminal Trivia();
    public static Terminal Trivia() => _trivia;

    private sealed record QuotedStringMatcher() : Terminal(Kind: "QuotedString")
    {
        public override int TryMatch(string input, int startPos)
        {
            if (startPos >= input.Length)
                return -1;

            var i = startPos;
            var c = input[i];

            if (c != '\"')
                return -1;

            for (i++; i < input.Length; i++)
            {
                c = input[i];

                if (c == '\\' && peek() != '\0')
                    i++;
                else if (c == '\n')
                    return -1;
                else if (c == '\"')
                {
                    i++;
                    break;
                }
            }

            return i - startPos;
            char peek() => i + 1 < input.Length ? input[i + 1] : '\0';
        }

        public override string ToString() => @"QuotedString";
    }

    private static readonly Terminal _quotedString = new QuotedStringMatcher();

    private sealed record TriviaMatcher() : Terminal(Kind: "Trivia")
    {
        public override int TryMatch(string input, int startPos)
        {
            var i = startPos;
            for (; i < input.Length; i++)
            {
                var c = input[i];

                if (char.IsWhiteSpace(c))
                    continue;

                if (c == '/' && peek() == '/')
                {
                    for (i += 2; i < input.Length && (c = input[i]) != '\n'; i++)
                        ;
                    i--;
                }
                else
                    return i - startPos;
            }

            return i - startPos;
            char peek() => i + 1 < input.Length ? input[i + 1] : '\0';
        }

        public override string ToString() => @"Trivia";
    }

    private static readonly Terminal _trivia = new TriviaMatcher();
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

        _parser.Rules["Assignment"] =
        [
            new Seq([
                DotTerminals.Identifier(),
                new Literal("="),
                new Ref("Value"),
                new Literal(";")
            ], "Assignment")
        ];

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
        private DotAst? CurrentResult { get; set; }

        public void Visit(TerminalNode node)
        {
            var span = node.AsSpan(input);
            CurrentResult = node.Kind switch
            {
                "Identifier" => new DotIdentifier(span.ToString(), node.StartPos, node.EndPos),
                "QuotedString" => new DotQuotedString(span, node.StartPos, node.EndPos),
                "Number" => new DotNumber(int.Parse(span), node.StartPos, node.EndPos),
                _ => new DotLiteral(span.ToString(), node.StartPos, node.EndPos)
            };
        }

        public void Visit(SeqNode node)
        {
            var children = new List<DotAst>();

            foreach (var element in node.Elements)
            {
                element.Accept(this);
                if (CurrentResult != null)
                    children.Add(CurrentResult);
            }

            CurrentResult = node.Kind switch
            {
                "Graph" => new DotGraph(
                    ((DotIdentifier)children[1]).Value,
                    FlattenStatements(children.Skip(3).Take(children.Count - 4))
                ),
                "NodeStatement" => new DotNodeStatement(
                    ((DotIdentifier)children[0]).Value,
                    children.Count > 1 ? ((DotAttributeList)children[1]).Attributes : new List<DotAttribute>()
                ),
                "EdgeStatement" => new DotEdgeStatement(
                    ((DotIdentifier)children[0]).Value,
                    ((DotIdentifier)children[2]).Value,
                    children.Count > 3 ? ((DotAttributeList)children[3]).Attributes : new List<DotAttribute>()
                ),
                "Subgraph" => new DotSubgraph(
                    ((DotIdentifier)children[1]).Value,
                    FlattenStatements(children.Skip(3).Take(children.Count - 4))
                ),
                "Assignment" => new DotAssignment(
                    ((DotIdentifier)children[0]).Value,
                    children[2] is DotQuotedString qs ? qs.Value : children[2].ToString()
                ),
                "AttributeList" => new DotAttributeList(
                    GetAttributes(children)
                ),
                "ZeroOrMany" => processZeroOrMany(children),
                "Attribute" => new DotAttribute(
                    ((DotIdentifier)children[0]).Value,
                    children[2] is DotQuotedString qs ? qs.Value : children[2].ToString()
                ),
                "AttributeRest" => new DotAttributeRest(
                    (DotAttribute)children[1]
                ),
                _ => throw new InvalidOperationException($"Unknown node kind: {node.Kind}")
            };

            if (node.Kind == "Graph")
                Result = (DotGraph)CurrentResult;
            return;
            DotStatementList processZeroOrMany(List<DotAst> children)
            {
                var statements = new List<DotStatement>();
                foreach (var child in children)
                {
                    if (child is DotStatement stmt)
                        statements.Add(stmt);
                    else if (child is DotStatementList list)
                        statements.AddRange(list.Statements);
                }
                return new DotStatementList(statements);
            }
        }

        private List<DotAttribute> GetAttributes(List<DotAst> children)
        {
            var attributes = new List<DotAttribute>();

            if (children.Count > 1 && children[1] is DotAttribute firstAttr)
                attributes.Add(firstAttr);

            for (int i = 2; i < children.Count; i++)
                if (children[i] is DotAttributeRest rest)
                    attributes.Add(rest.Attribute);

            return attributes;
        }

        private List<DotStatement> FlattenStatements(IEnumerable<DotAst> nodes)
        {
            var result = new List<DotStatement>();
            foreach (var node in nodes)
            {
                if (node is DotStatementList list)
                    result.AddRange(list.Statements);
                else if (node is DotStatement statement)
                    result.Add(statement);
            }
            return result;
        }

        public void Visit(SomeNode node)
        {
            node.Value.Accept(this);
            if (CurrentResult == null)
                throw new InvalidOperationException("Optional node has no value");
        }

        public void Visit(NoneNode _)
        {
            CurrentResult = null;
        }

        private record DotStatementList(IReadOnlyList<DotStatement> Statements) : DotAst
        {
            public override string ToString() => string.Join("\n", Statements);
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
    public void TriviaMatcherTest()
    {
        var matcher = DotTerminals.Trivia();

        test(startPos: 0, expectedLen: 18, "  // Top to Bottom");
        test(startPos: 1, expectedLen: 24, ";  // Top to Bottom\r\n    n");
        test(startPos: 0, expectedLen: 20, "  // Top to Bottom\r\n/ ");
        test(startPos: 0, expectedLen: 21, "  // Top to Bottom\r\n ");
        test(startPos: 0, expectedLen: 20, "  // Top to Bottom\n ");
        test(startPos: 0, expectedLen:  4, "  //");
        test(startPos: 0, expectedLen:  2, "  /");
        test(startPos: 0, expectedLen:  2, "  ");
        test(startPos: 1, expectedLen:  1, "  ");
        test(startPos: 2, expectedLen:  0, "  ");
        test(startPos: 4, expectedLen:  0, "  ");
        test(startPos: 0, expectedLen: 0, "we");
        test(startPos: 0, expectedLen: 1, "\rwe");
        test(startPos: 1, expectedLen: 0, "we");
        test(startPos: 0, expectedLen: 31, "  // 1212 \r\n // 534534\r\n// 1212");

        return;
        void test(int startPos, int expectedLen, string input)
        {
            Assert.AreEqual(expectedLen, matcher.TryMatch(input, startPos));
        }
    }

    [TestMethod]
    public void QuotedStringTest()
    {
        var matcher = DotTerminals.QuotedString();

        test(startPos: 0, expectedLen: -1, "123");
        test(startPos: 0, expectedLen: 10, "\"23456789\"");
        test(startPos: 1, expectedLen: 10, " \"23456789\"");
        test(startPos: 1, expectedLen: 12, """
             "23\"456789""23\"456789"
            """);
        test(startPos: 1, expectedLen: 16, """
            ="23\"\n4\r56789"
            """);

        return;
        void test(int startPos, int expectedLen, string input)
        {
            Assert.AreEqual(expectedLen, matcher.TryMatch(input, startPos));
        }
    }

    [TestMethod]
    public void ParseComplexGraph()
    {
        var graph = ParseComplexGraph_DotText.ParseDotGraph();

        // Проверка количества узлов в основном графе (исключая управляющие конструкции)
        var mainNodes = graph.Statements
            .OfType<DotNodeStatement>()
            .Where(n => !n.NodeId.StartsWith("node") &&
                       !n.NodeId.StartsWith("edge"))
            .ToList();

        assertStatementsCount(
            expected: 2,
            actual: mainNodes.Count,
            message: "Количество узлов в основном графе",
            statements: mainNodes);

        // Проверка подграфов
        var subgraphs = graph.Statements.OfType<DotSubgraph>().ToList();
        assertStatementsCount(
            expected: 5,
            actual: subgraphs.Count,
            message: "Количество подграфов",
            statements: subgraphs);

        // Проверка узлов в подграфах (только NodeStatement)
        var nodesInSubgraphs = subgraphs
            .SelectMany(sg => sg.Statements.OfType<DotNodeStatement>())
            .ToList();

        assertStatementsCount(
            expected: 16, // Исправлено с 17 на 16 после ручного подсчета
            actual: nodesInSubgraphs.Count,
            message: "Количество узлов в подграфах",
            statements: nodesInSubgraphs);

        // Проверка атрибута
        var allEdges = graph.Statements
            .OfType<DotEdgeStatement>()
            .Concat(subgraphs.SelectMany(sg => sg.Statements.OfType<DotEdgeStatement>()))
            .ToList();

        var targetEdge = allEdges
            .FirstOrDefault(e => e.FromNode == "PRInMaster" && e.ToNode == "AutoCherrypick");

        Assert.IsNotNull(targetEdge, "Не найден переход PRInMaster -> AutoCherrypick");
        Assert.IsTrue(targetEdge.Attributes.Any(a =>
            a.Name == "ТестовыйАтрибут" && a.Value == "42"),
            "Не найден атрибут ТестовыйАтрибут=42");
        return;

        void assertStatementsCount<T>(
            int expected,
            int actual,
            string message,
            IReadOnlyList<T> statements) where T : DotAst
        {
            if (expected != actual)
            {
                var details = new StringBuilder()
                    .AppendLine($"{message}. Ожидалось: {expected}, получено: {actual}")
                    .AppendLine("Список полученных элементов:");

                foreach (var stmt in statements)
                    details.AppendLine($"- {stmt.GetType().Name}: {stmt}");

                Assert.Fail(details.ToString());
            }
        }
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
