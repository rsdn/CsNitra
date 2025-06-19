using Dot;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;

namespace WorkflowGenerator;

[Generator]
public class WfGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var workflowClasses = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "Workflow.WorkflowAttribute",
                (node, _) => node is ClassDeclarationSyntax,
                (ctx, _) => (
                    Symbol: (INamedTypeSymbol)ctx.TargetSymbol,
                    FileName: GetAttributeFileName(ctx.Attributes, "WorkflowAttribute"),
                    Syntax: (ClassDeclarationSyntax)ctx.TargetNode
                ))
            .Where(t => t.FileName != null);

        var eventRecords = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "Workflow.WorkflowEventAttribute",
                (node, _) => node is RecordDeclarationSyntax,
                (ctx, _) => (
                    Symbol: (INamedTypeSymbol)ctx.TargetSymbol,
                    FileName: GetAttributeFileName(ctx.Attributes, "WorkflowEventAttribute"),
                    Syntax: (RecordDeclarationSyntax)ctx.TargetNode,
                    Context: ctx
                ));

        var additionalTexts = context.AdditionalTextsProvider.Collect();

        context.RegisterSourceOutput(workflowClasses.Combine(additionalTexts),
            (spc, source) => ProcessSymbolWithDotFile(
                spc,
                source.Left.Symbol,
                source.Left.FileName!,
                source.Right,
                source.Left.Syntax,
                GenerateStateMachine));

        context.RegisterSourceOutput(eventRecords.Combine(additionalTexts),
            (spc, source) => ProcessSymbolWithDotFile(
                spc,
                source.Left.Symbol,
                source.Left.FileName!,
                source.Right,
                source.Left.Syntax,
                (ctx, symbol, graph) => GenerateEventRecords(ctx, symbol, graph)));
    }

    private static string? GetAttributeFileName(ImmutableArray<AttributeData> attributes, string attributeName)
    {
        if (attributes.Length == 0)
            return null;

        var attribute = attributes[0];
        if (attribute.ConstructorArguments.Length != 1)
            return null;

        return attribute.ConstructorArguments[0].Value?.ToString();
    }

    private void ProcessSymbolWithDotFile(
        SourceProductionContext context,
        INamedTypeSymbol symbol,
        string dotFileName,
        ImmutableArray<AdditionalText> additionalTexts,
        SyntaxNode syntaxNode,
        Action<SourceProductionContext, INamedTypeSymbol, DotGraph> generateAction)
    {
        if (!ValidateFileName(context, dotFileName, syntaxNode))
            return;

        var dotFile = FindDotFile(context, dotFileName, additionalTexts, syntaxNode);
        if (dotFile == null)
            return;

        try
        {
            //Debugger.Launch();
            var parser = new DotParser();
            var graph = parser.ParseDotGraph(dotFile.GetText()!.ToString());
            generateAction(context, symbol, graph);
        }
        catch (Exception ex)
        {
            ReportError(context, "WF002", $"Error processing DOT file: {ex.Message}", syntaxNode);
        }
    }

    private static bool ValidateFileName(
        SourceProductionContext context,
        string fileName,
        SyntaxNode syntaxNode)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            ReportError(context, "WF003",
                "File name argument cannot be null or empty",
                syntaxNode);
            return false;
        }
        return true;
    }

    private static AdditionalText? FindDotFile(
        SourceProductionContext context,
        string dotFileName,
        ImmutableArray<AdditionalText> additionalTexts,
        SyntaxNode syntaxNode)
    {
        var dotFile = additionalTexts.FirstOrDefault(f =>
            Path.GetFileName(f.Path) == dotFileName);

        if (dotFile == null)
        {
            var attributeSyntax = syntaxNode.DescendantNodes()
                .OfType<AttributeSyntax>()
                .FirstOrDefault();

            ReportError(context, "WF001",
                $"Workflow definition file '{dotFileName}' not found. If the file exists, " +
                "ensure its 'Build Action' is set to 'C# analyzer additional file'",
                attributeSyntax?.ArgumentList?.Arguments.FirstOrDefault() ?? syntaxNode);
        }

        return dotFile;
    }

    private static void ReportError(
        SourceProductionContext context,
        string id,
        string message,
        SyntaxNode? syntaxNode)
    {
        var location = syntaxNode != null
            ? syntaxNode.GetLocation()
            : Location.None;

        context.ReportDiagnostic(Diagnostic.Create(
            new DiagnosticDescriptor(
                id,
                "Error",
                message,
                "Workflow",
                DiagnosticSeverity.Error,
                true),
            location));
    }

    private void GenerateStateMachine(
        SourceProductionContext context,
        INamedTypeSymbol workflowClass,
        DotGraph graph)
    {
        var states = graph.Statements
            .OfType<DotNodeStatement>()
            .Select(n => n.Name)
            .Concat(graph.Statements
                .OfType<DotSubgraph>()
                .SelectMany(s => s.Statements.OfType<DotNodeStatement>().Select(n => n.Name)))
            .Distinct()
            .OrderBy(s => s)
            .ToList();

        var transitions = graph.Statements
            .OfType<DotEdgeStatement>()
            .Select(e => (
                From: e.FromNode,
                To: e.ToNode,
                Event: e.Attributes.FirstOrDefault(a => a.Name == "event")?.Value.ToString().Trim('"')!
            ))
            .Where(t => t.Event != null)
            .ToList();

        var accessibility = workflowClass.DeclaredAccessibility.ToString().ToLower();

        var stateEnum = $$"""
            {{accessibility}} enum WfState
            {
                {{string.Join(",\n    ", states)}}
            }
            """;

        var switchCases = new StringBuilder();
        var afterSwitchCases = new StringBuilder();
        var eventMethodMap = new Dictionary<string, string>();
        var afterEventMethodMap = new Dictionary<string, string>();

        foreach (var t in transitions)
        {
            var eventName = SanitizeName(t.Event);
            var eventType = $"WfEvent.{eventName}";

            switchCases.AppendLine($$"""
                        case (WfState.{{t.From}}, {{eventType}} e):
                            isAccepted = await On{{eventName}}(e, WfState.{{t.From}}, WfState.{{t.To}});
                            return isAccepted ? WfState.{{t.To}} : WfState.{{t.From}};
            """);

            afterSwitchCases.AppendLine($$"""
                        case (WfState.{{t.From}}, {{eventType}} e) when newState == WfState.{{t.To}}:
                            await After{{eventName}}(e, WfState.{{t.From}}, WfState.{{t.To}});
                            break;
            """);

            if (!eventMethodMap.ContainsKey(eventName))
            {
                eventMethodMap.Add(eventName, $$"""
                        protected virtual Task<bool> On{{eventName}}({{eventType}} @event, WfState oldState, WfState newState) => Task.FromResult(true);
                    """);

                afterEventMethodMap.Add(eventName, $$"""
                        protected virtual async Task After{{eventName}}({{eventType}} @event, WfState oldState, WfState newState) => await AfterUnprocessedTransition(oldState, newState, @event);
                    """);
            }
        }

        var eventMethods = string.Join("\n", eventMethodMap.OrderBy(x => x.Key).Select(x => x.Value));
        var afterEventMethods = string.Join("\n", afterEventMethodMap.OrderBy(x => x.Key).Select(x => x.Value));

        var automaton = $$"""
            {{accessibility}} abstract partial class {{workflowClass.Name}}Base
            {
                protected WfState _currentState = WfState.{{states.First()}};
                
                public event Action<WfState, WfState, WfEvent>? StateChanged;
                
                public WfState CurrentState => _currentState;

                private async Task<WfState> Transition(WfState current, WfEvent @event)
                {
                    bool isAccepted = true;
                    
                    switch (current, @event)
                    {
            {{switchCases}}
                        default:
                            await OnNoTransition(current, @event);
                            return current;
                    }
                }

                private async Task AfterTransition(WfState oldState, WfState newState, WfEvent @event)
                {
                    switch (oldState, @event)
                    {
            {{afterSwitchCases}}
                        default:
                            break;
                    }
                }

                protected virtual Task AfterUnprocessedTransition(WfState oldState, WfState newState, WfEvent @event) => Task.CompletedTask;

                public async Task<bool> ProcessEvent(WfEvent @event)
                {
                    var oldState = _currentState;
                    var newState = await Transition(oldState, @event);

                    OnAllTransition(oldState, @event, newState);

                    if (newState == oldState)
                        return false;

                    _currentState = newState;
                    StateChanged?.Invoke(oldState, newState, @event);
                    
                    await AfterTransition(oldState, newState, @event);
                    return true;
                }

                // Event handlers
            {{eventMethods}}
                
                // After event handlers
            {{afterEventMethods}}
                
                protected virtual Task OnNoTransition(WfState currentState, WfEvent @event) => Task.CompletedTask;
                protected virtual void OnAllTransition(WfState oldState, WfEvent @event, WfState newState) { }
            }
            """;

        var fullCode = $$"""
            // <auto-generated/>
            #nullable enable
            
            namespace {{workflowClass.ContainingNamespace.ToDisplayString()}};
            
            {{stateEnum}}
            
            {{automaton}}
            """;
        context.AddSource($"{workflowClass.Name}Base.g.cs", SourceText.From(fullCode, Encoding.UTF8));
    }

    private void GenerateEventRecords(
        SourceProductionContext context,
        INamedTypeSymbol baseRecord,
        DotGraph graph)
    {
        var events = graph.Statements
            .OfType<DotEdgeStatement>()
            .SelectMany(e => e.Attributes
                .Where(a => a.Name == "event")
                .Select(a => a.Value.ToString().Trim('"')!))
            .Distinct();

        var accessibility = baseRecord.DeclaredAccessibility.ToString().ToLower();

        var records = string.Join("\n    ", events.Select(e =>
            $"public sealed partial record {SanitizeName(e)} : {baseRecord.Name};"));

        var code = $$"""
            // <auto-generated/>
            #nullable enable
            
            namespace {{baseRecord.ContainingNamespace.ToDisplayString()}};
            
            {{accessibility}} abstract partial record {{baseRecord.Name}}
            {
                {{records}}
            }
            """;

        context.AddSource($"{baseRecord.Name}.g.cs", SourceText.From(code, Encoding.UTF8));
    }

    private static string SanitizeName(string name) =>
        new string(name.Where(c => char.IsLetterOrDigit(c)).ToArray());
}
