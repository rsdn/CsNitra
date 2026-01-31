#nullable enable 

using Diagnostics;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

using Regex;

using System.Collections.Immutable;
using System.Text;

namespace TerminalGenerator
{
    [Generator]
    public class TerminalGenerator : IIncrementalGenerator
    {
        private const string RegexAttrName = "Regex";
        private const string RegexAttrFullName = RegexAttrName + "Attribute";
        private const string Any = "true /* . (RegexAnyChar) */";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Создаем провайдер для классов с атрибутом TerminalMatcher
            var classProvider = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => node is ClassDeclarationSyntax c && c.AttributeLists.Count > 0,
                    transform: static (ctx, _) => (Class: ctx.Node as ClassDeclarationSyntax, ctx.SemanticModel))
                .Where(t => t.Class?.AttributeLists.Any(a =>
                    a.Attributes.Any(attr => attr.Name.ToString() is "TerminalMatcher" or "TerminalMatcherAttribute")) ?? false);

            // Создаем провайдер для методов с атрибутом Regex
            var methodProvider = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => node is MethodDeclarationSyntax m && m.AttributeLists.Count > 0,
                    transform: static (ctx, _) => (Method: ctx.Node as MethodDeclarationSyntax, ctx.SemanticModel))
                .Where(t => t.Method?.AttributeLists.Any(a =>
                    a.Attributes.Any(attr => attr.Name.ToString() is RegexAttrName or RegexAttrFullName)) ?? false);

            // Комбинируем провайдеры
            var combinedProvider = classProvider.Combine(methodProvider.Collect());

            context.RegisterSourceOutput(combinedProvider, (ctx, pair) =>
            {
                var ((classSyntax, classSemanticModel), methods) = pair;

                try
                {
                    if (classSyntax == null || classSemanticModel == null)
                        return;

                    var classSymbol = classSemanticModel.GetDeclaredSymbol(classSyntax);
                    if (classSymbol == null)
                        return;

                    // Получаем все необходимые данные о классе
                    var namespaceName = classSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;
                    var className = classSymbol.Name;
                    var classModifiers = classSyntax.Modifiers.ToString(); // "public partial" и т.д.
                    var methodImplementations = generateMethods(classSyntax, methods);

                    if (methodImplementations.Length == 0)
                        return;

                    var source = generateClass(namespaceName, className, classModifiers, methodImplementations);

                    ctx.AddSource($"{namespaceName}.{className}.g.cs", SourceText.From(source, Encoding.UTF8));
                }
                catch (Exception ex)
                {
                    ctx.ReportDiagnostic(Diagnostic.Create(
                        new DiagnosticDescriptor(
                            "TG005",
                            "Generator Error",
                            $"Error generating code: {ex.Message}",
                            "Error",
                            DiagnosticSeverity.Error,
                            isEnabledByDefault: true),
                        Location.None));
                }
            });

            string generateMethods(ClassDeclarationSyntax classSyntax, ImmutableArray<(MethodDeclarationSyntax? Method, SemanticModel SemanticModel)> methods)
            {
                // Собираем реализации для всех методов
                var methodImplementations = new StringBuilder();
                foreach (var (methodSyntax, methodSemanticModel) in methods)
                {
                    if (methodSyntax == null || methodSemanticModel == null || methodSyntax.Parent != classSyntax)
                        continue;

                    var regexAttr = methodSyntax.AttributeLists
                        .SelectMany(a => a.Attributes)
                        .FirstOrDefault(a => a.Name.ToString() is RegexAttrName or RegexAttrFullName);

                    if (regexAttr?.ArgumentList?.Arguments.Count == 0)
                        continue;

                    var regexArgument = regexAttr!.ArgumentList!.Arguments[0];
                    var regexPattern = getLiteralValue(regexArgument, methodSemanticModel);

                    if (string.IsNullOrEmpty(regexPattern))
                        continue;

                    Log? log = null;
                    var methodName = methodSyntax.Identifier.Text;
                    var methodModifiers = methodSyntax.Modifiers.ToString(); // "public" и т.д.
                    var returnType = methodSyntax.ReturnType;
                    var parser = new RegexParser(regexPattern);
                    var regexNode = parser.Parse();
                    var (StartState, _) = new NfaBuilder().Build(regexNode);
                    var dfa = new DfaBuilder(log).Build(StartState);
                    var code = generateDfaCode(dfa, startIndent: 3);

                    // Генерируем реализацию для одного метода
                    methodImplementations.AppendLine($$"""
                                private sealed record {{methodName}}Matcher() : {{returnType}}(Kind: "{{methodName}}")
                                {
                                    public override int TryMatch(string input, int startPos)
                                    {
                                        // Code generated for regular expression: {{regexPattern}}
                                        {{code}}
                                    }

                                    public override string ToString() => @"{{methodName}}";
                                }

                                private static readonly {{returnType}} {{toFieldName(methodName)}} = new {{methodName}}Matcher();
                                
                                {{methodModifiers}} {{returnType}} {{methodName}}() => {{toFieldName(methodName)}};
                            """);
                }

                return methodImplementations.ToString();
            }

            static string generateClass(string namespaceName, string className, string classModifiers, string methodImplementations)
            {
                // Генерируем полный код класса
                return $$"""
                        using ExtensibleParser;
                        
                        namespace {{namespaceName}};

                        {{classModifiers}} class {{className}}
                        {
                        {{methodImplementations}}
                        }
                        """;
            }

            string generateDfaCode(DfaState start, int startIndent)
            {
                var sb = new StringBuilder();
                var visited = new HashSet<DfaState>();
                var states = new List<DfaState>();
                var indent = new IndentHelper().Increase(startIndent);

                // Collect all states using BFS
                var queue = new Queue<DfaState>();
                queue.Enqueue(start);
                visited.Add(start);
                while (queue.Count > 0)
                {
                    var state = queue.Dequeue();
                    states.Add(state);
                    foreach (var transition in state.Transitions)
                    {
                        if (!visited.Contains(transition.Target))
                        {
                            visited.Add(transition.Target);
                            queue.Enqueue(transition.Target);
                        }
                    }
                }

                sb.AppendLine($$"""
                var currentPos = startPos;
                var length = input.Length;
                var currentState = {{start.Id}};
                var lastAccept = -1;
                {{(start.IsFinal ? "lastAccept = currentPos;" : "")}}

                while (currentPos <= length)
                {
                    var c = currentPos < length ? input[currentPos] : '\0';
                    var transitionFound = false;
                    switch (currentState)
                    {
                """);

                // Generate state transitions
                foreach (var state in states)
                    sb.AppendLine($$"""
                    case {{state.Id}}:
                        {{generateStateTransitions(state, indent)}}
            """);

                sb.AppendLine($$"""
                    }
                    if (!transitionFound)
                        break;
                }

                if (lastAccept != -1)
                    return lastAccept - startPos;

                return {{(start.IsFinal ? $"currentState == {start.Id} /* start.Id && start.IsFinal */ ? 0 : -1;" : "-1;")}}
                """);

                return indent.Apply(sb).ToString();

                static string generateStateTransitions(DfaState state, IndentHelper indent)
                {
                    var sb = new StringBuilder();
                    var hasAny = false;
                    foreach (var transition in state.Transitions)
                    {
                        var condition = generateTransitionCondition(transition.Condition);
                        hasAny |= condition == Any;
                        sb.AppendLine($$"""
                        if ({{condition}})
                        {
                            currentState = {{transition.Target.Id}};
                            {{(transition.Condition is RegexEndOfLine ? "" : "currentPos++;")}}
                            {{(transition.Target.IsFinal ? "lastAccept = currentPos;" : "")}}
                            transitionFound = true;
                            continue;
                        }
                        """);
                    }

                    if (!hasAny)
                    {
                        sb.Append("""
                        transitionFound = false;
                        break;
                        """);
                    }

                    return indent.Apply(sb).ToString();

                }

                static string generateTransitionCondition(RegexNode node)
                {
                    return node switch
                    {
                        RegexEndOfLine x => $@"c == '\0' /* {x} */",
                        RegexChar rc => $"""c == '{escapeChar(rc.Value)}'  /* {rc} */""",
                        RegexAnyChar => Any,
                        NegatedCharClassGroup rcc when rcc.ToString() == $@"[^[\n]]" => $@"c is not '\n' and not '\0' /* {rcc} */",
                        RangesCharClass rcc => generateRangeCondition(rcc),
                        WordCharClass wcc => $"""{(wcc.Negated ? "!" : "")}(char.IsLetterOrDigit(c) || c == '_')""",
                        DigitCharClass dcc => $"""{(dcc.Negated ? "!" : "")}char.IsDigit(c)""",
                        WhitespaceCharClass scc => $"""{(scc.Negated ? "!" : "")}char.IsWhiteSpace(c)""",
                        LetterCharClass lcc => $"""{(lcc.Negated ? "!" : "")}char.IsLetter(c)""",
                        NegatedCharClassGroup x => $@"!({string.Join(" || ", x.Classes.Select(generateTransitionCondition))}) && c != '\0'",
                        _ => $"false /* {node} ({node.GetType().Name}) */"
                    };
                }

                static string generateRangeCondition(RangesCharClass rcc)
                {
                    if (rcc.ToString() == @"[^[\n]]") // Специальный случай для [^\n]
                        return rcc.Negated ? @"c == '\n'" : @"c != '\n'";

                    var conditions = rcc.Ranges
                        .Select(r => r.From == r.To
                            ? $"""c == '{escapeChar(r.From)}'"""
                            : $"""(c >= '{escapeChar(r.From)}' && c <= '{escapeChar(r.To)}')""")
                        .ToList();

                    var combined = string.Join(" || ", conditions);
                    return rcc.Negated ? $"!({combined})" : combined;
                }

                static string escapeChar(char c) => c switch
                {
                    '\'' => @"\'",
                    '\\' => @"\\",
                    '\t' => @"\t",
                    '\r' => @"\r",
                    '\n' => @"\n",
                    _ when char.IsControl(c) => $@"\u{(int)c:X4}",
                    _ => c.ToString()
                };
            }

            static string getLiteralValue(AttributeArgumentSyntax argument, SemanticModel semanticModel)
            {
                try
                {
                    var expression = argument.Expression;

                    if (expression is LiteralExpressionSyntax literal)
                    {
                        if (literal.IsKind(SyntaxKind.StringLiteralExpression))
                            return literal.Token.ValueText;

                        if (literal.IsKind(SyntaxKind.NumericLiteralExpression))
                            return literal.Token.Value?.ToString() ?? string.Empty;
                    }
                    else if (expression is InvocationExpressionSyntax invocation && invocation.Expression.ToString() == "nameof")
                    {
                        return invocation.ArgumentList.Arguments[0].ToString();
                    }

                    var constantValue = semanticModel.GetConstantValue(expression);
                    if (constantValue.HasValue && constantValue.Value is { } value)
                        return value.ToString() ?? "";

                    return expression.ToString().Trim('"', '\'');
                }
                catch
                {
                    return string.Empty;
                }
            }

            static string toFieldName(string name) =>
                string.IsNullOrWhiteSpace(name) ? name : $"_{name[..1].ToLowerInvariant()}{name[1..]}";
        }

        private sealed class IndentHelper
        {
            private const int SpacesPerLevel = 4;

            private int _level;

            public IndentHelper Increase(int count = 1) => new() { _level = _level + count };

            public override string ToString() => new(' ', _level * SpacesPerLevel);

            public string Apply(string input) => input.Replace("\n", '\n' + ToString());

            public StringBuilder Apply(StringBuilder input) => input.Replace("\n", '\n' + ToString());
        }
    }
}
