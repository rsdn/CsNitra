#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using ExtensibleParaser;
using Dot;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
                    Class: (INamedTypeSymbol)ctx.TargetSymbol,
                    DotFileName: ctx.Attributes[0].ConstructorArguments[0].Value!.ToString()
                ))
            .Where(t => t.DotFileName != null);

        var eventRecords = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "Workflow.WorkflowEventAttribute",
                (node, _) => node is RecordDeclarationSyntax,
                (ctx, _) => (Record: (INamedTypeSymbol)ctx.TargetSymbol, ctx));

        var additionalTexts = context.AdditionalTextsProvider.Collect();

        context.RegisterSourceOutput(workflowClasses.Combine(additionalTexts),
            (spc, source) => ProcessWorkflow(spc, source.Left, source.Right));

        context.RegisterSourceOutput(eventRecords.Combine(additionalTexts),
            (spc, source) => ProcessEvents(spc, source.Left, source.Right));
    }

    private void ProcessWorkflow(
        SourceProductionContext context,
        (INamedTypeSymbol Class, string DotFileName) workflow,
        System.Collections.Immutable.ImmutableArray<AdditionalText> additionalTexts)
    {
        var dotFile = additionalTexts.FirstOrDefault(f =>
            Path.GetFileName(f.Path) == workflow.DotFileName);

        if (dotFile == null) return;

        try
        {
            var parser = new DotParser();
            var graph = parser.ParseDotGraph(dotFile.GetText()!.ToString());
            GenerateStateMachine(context, workflow.Class, graph);
        }
        catch (Exception ex)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor("WF002", "Error", ex.Message, "Workflow", DiagnosticSeverity.Error, true),
                Location.None));
        }
    }

    private void ProcessEvents(
        SourceProductionContext context,
        (INamedTypeSymbol Record, GeneratorAttributeSyntaxContext Context) eventRecord,
        System.Collections.Immutable.ImmutableArray<AdditionalText> additionalTexts)
    {
        var dotFile = additionalTexts.FirstOrDefault(f =>
            Path.GetFileName(f.Path) == "wf.dot");

        if (dotFile == null) return;

        try
        {
            var parser = new DotParser();
            var graph = parser.ParseDotGraph(dotFile.GetText()!.ToString());
            GenerateEventRecords(context, eventRecord.Record, graph);
        }
        catch (Exception ex)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor("WF002", "Error", ex.Message, "Workflow", DiagnosticSeverity.Error, true),
                Location.None));
        }
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
        var eventMethods = new StringBuilder();
        foreach (var t in transitions)
        {
            var eventName = SanitizeName(t.Event);
            var eventType = $"WfEvent.{eventName}";

            switchCases.AppendLine($$"""
                        case (WfState.{{t.From}}, {{eventType}} e):
                            On{{eventName}}(e, WfState.{{t.From}}, WfState.{{t.To}}, ref isAccepted);
                            return isAccepted ? WfState.{{t.To}} : WfState.{{t.From}};
            """);

            eventMethods.AppendLine($$"""
                partial void On{{eventName}}({{eventType}} @event, WfState oldState, WfState newState, ref bool isAccepted);
            """);
        }

        var automaton = $$"""
            {{accessibility}} sealed partial class {{workflowClass.Name}}
            {
                private WfState _currentState = WfState.{{states.First()}};
                
                public event Action<WfState, WfState, WfEvent>? StateChanged;
                
                public WfState CurrentState => _currentState;

                private WfState Transition(WfState current, WfEvent @event)
                {
                    bool isAccepted = true;
                    
                    switch (current, @event)
                    {
            {{switchCases}}
                        default:
                            OnNoTransition(ref current, @event);
                            return current;
                    }
                }

                public bool ProcessEvent(WfEvent @event)
                {
                    var oldState = _currentState;
                    var newState = Transition(oldState, @event);
                    
                    if (newState == oldState)
                        return false;

                    _currentState = newState;
                    StateChanged?.Invoke(oldState, newState, @event);
                    return true;
                }

                // Event handlers
            {{eventMethods}}
                
                partial void OnNoTransition(ref WfState currentState, WfEvent @event);
            }
            """;

        var fullCode = $$"""
            // <auto-generated/>
            #nullable enable
            
            namespace {{workflowClass.ContainingNamespace.ToDisplayString()}};
            
            {{stateEnum}}
            
            {{automaton}}
            """;

        context.AddSource($"{workflowClass.Name}.g.cs", SourceText.From(fullCode, Encoding.UTF8));
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
