using System.Runtime.CompilerServices;
using System.Text;

namespace Dot;

[TestClass]
public class DotTests
{
    [TestMethod]
    public void TriviaMatcherTest()
    {
        var matcher = DotTerminals.Trivia();

        test(startPos: 0, expectedLen: 19, "  /// Top to Bottom");
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
    public void QuotedStringMatcherTest()
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
    public void ParseDotGraph()
    {
        var parser = new DotParser();
        var graph = parser.Parse(ParseComplexGraph_DotText);

        // Проверка количества узлов в основном графе (исключая управляющие конструкции)
        var mainNodes = graph.Statements
            .OfType<DotNodeStatement>()
            .Where(n => !n.NodeId.StartsWith("node") && !n.NodeId.StartsWith("edge"))
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
        checkAttributes(
            targetEdge.Attributes is
            [
                { Name: "label", Value: DotQuotedString { Value: "PR master прошёл успешно" } },
                { Name: "ТестовыйАтрибут", Value: DotNumber { Value: 42 } }
            ],
            targetEdge.Attributes,
            "Переход PRInMaster -> AutoCherrypick");

        // Проверка атрибутов узла Closed
        var closedNode = mainNodes.FirstOrDefault(n => n.NodeId == "Closed");
        Assert.IsNotNull(closedNode, "Не найден узел Closed");

        checkAttributes(
            closedNode.Attributes is 
            [
                { Name: "label", Value: DotQuotedString { Value: "Закрыто" } },
                { Name: "fillcolor", Value: DotQuotedString { Value: "lightgreen" } },
                { Name: "style", Value: DotQuotedString { Value: "filled" } }
            ],
            closedNode.Attributes,
            "Узел Closed");

        return;

        void checkAttributes(
            bool condition,
            IReadOnlyList<DotAttribute> actualAttributes,
            string elementDescription,
            [CallerArgumentExpression(nameof(condition))] string? expression = null)
        {
            if (!condition)
            {
                Assert.Fail($"""
                Проверка не пройдена для {elementDescription}.
                Выражение: {expression}
                Фактические атрибуты: {string.Join(", ", actualAttributes)}
                """);
            }
        }
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
