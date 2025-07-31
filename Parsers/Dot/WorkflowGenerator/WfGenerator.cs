using Dot;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace WorkflowGenerator;

#pragma warning disable RS2008 // Enable analyzer release tracking

[Generator]
public class WfGenerator : IIncrementalGenerator
{
    private const string ReplayColor = "deeppink";

    private static readonly DiagnosticDescriptor MissingTimeoutHandlerDescriptor =
        new(
            "WF010",
            "Missing timeout handlers",
            "Missing handler for timeout event: {0}",
            "Workflow",
            DiagnosticSeverity.Warning,
            true);

    private static readonly DiagnosticDescriptor DuplicateTimeoutForStateDescriptor =
        new(
            "WF011",
            "Duplicate timeout for state",
            "State '{0}' has multiple timeout transitions (events: {1}). Only one timeout per state is supported. Please check transitions from state '{0}' in your workflow definition.",
            "Workflow",
            DiagnosticSeverity.Error,
            true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var workflowClasses = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "Workflow.WorkflowAttribute",
                (node, _) => node is ClassDeclarationSyntax,
                (ctx, _) => (
                    Symbol: (INamedTypeSymbol)ctx.TargetSymbol,
                    FileName: GetAttributeFileName(ctx.Attributes),
                    Syntax: (ClassDeclarationSyntax)ctx.TargetNode,
                    Context: ctx
                ))
            .Where(t => t.FileName != null);

        var eventRecords = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "Workflow.WorkflowEventAttribute",
                (node, _) => node is RecordDeclarationSyntax,
                (ctx, _) => (
                    Symbol: (INamedTypeSymbol)ctx.TargetSymbol,
                    FileName: GetAttributeFileName(ctx.Attributes),
                    Syntax: (RecordDeclarationSyntax)ctx.TargetNode,
                    Context: ctx
                ));

        var additionalTexts = context.AdditionalTextsProvider.Collect();

        context.RegisterSourceOutput(workflowClasses.Combine(additionalTexts),
            (spc, source) =>
            {
                var (left, right) = source;
                ProcessSymbolWithDotFile(
                    spc,
                    left.Symbol,
                    left.FileName!,
                    right,
                    left.Syntax,
                    (ctx, symbol, graph) =>
                    {
                        GenerateStateMachine(ctx, symbol, graph);
                        CheckMissingTimeoutHandlers(ctx, symbol, graph, left.Context);
                    });
            });

        context.RegisterSourceOutput(eventRecords.Combine(additionalTexts),
            (spc, source) => ProcessSymbolWithDotFile(
                spc,
                source.Left.Symbol,
                source.Left.FileName!,
                source.Right,
                source.Left.Syntax,
                GenerateEventRecords));
    }

    private void CheckMissingTimeoutHandlers(
        SourceProductionContext context,
        INamedTypeSymbol workflowClass,
        DotGraph graph,
        GeneratorAttributeSyntaxContext syntaxContext)
    {
        var timeoutTransitions = graph.Statements
            .OfType<DotEdgeStatement>()
            .Where(e => e.Attributes.Any(a => a.Name == "timeout"))
            .Select(e => e.Attributes.First(a => a.Name == "event").Value.ToString().Trim('"'))
            .Distinct()
            .ToList();

        if (timeoutTransitions.Count == 0)
            return;

        var handlerNames = new HashSet<string>();
        foreach (var member in workflowClass.GetMembers())
        {
            if (member is IMethodSymbol method)
            {
                if (method.Name.StartsWith("After"))
                {
                    handlerNames.Add(method.Name.Substring("After".Length));
                }
                else if (method.Name.StartsWith("On"))
                {
                    handlerNames.Add(method.Name.Substring("On".Length));
                }
            }
        }

        foreach (var eventName in timeoutTransitions)
        {
            var sanitized = SanitizeName(eventName);
            if (!handlerNames.Contains(sanitized))
            {
                var location = syntaxContext.TargetNode.GetLocation();
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        MissingTimeoutHandlerDescriptor,
                        location,
                        eventName));
            }
        }
    }

    private static string? GetAttributeFileName(ImmutableArray<AttributeData> attributes)
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
        var nodeStatements = graph.Statements
            .OfType<DotNodeStatement>()
            .Concat(graph.Statements
                .OfType<DotSubgraph>()
                .SelectMany(s => s.Statements.OfType<DotNodeStatement>()))
            .Where(x => !char.IsLower(x.Name.First()))
            .ToList();

        if (hasErrors(context, nodeStatements))
            return;

        var states = nodeStatements
            .Select(n => (n.Name, Id: ((DotNumber)n.Attributes.First(a => a.Name == "id").Value).Value))
            .OrderBy(x => x.Id)
            .ToArray();

        var transitions = graph.Statements
                    .OfType<DotEdgeStatement>()
                    .Select(e => (
                        From: e.FromNode,
                        To: e.ToNode,
                        Event: e.Attributes.FirstOrDefault(a => a.Name == "event")?.Value.ToString().Trim('"')!,
                        Color: e.Attributes.FirstOrDefault(a => a.Name == "color" && a.Value.ToString() == ReplayColor)?.Value.ToString().Trim('"'),
                        Timeout: e.Attributes.FirstOrDefault(a => a.Name == "timeout")?.Value.ToString().Trim('"')
                    ))
                    .Where(t => t.Event != null)
                    .ToList();

        // Check for duplicate timeouts per state
        var timeoutTransitionsByState = transitions
            .Where(t => t.Timeout != null)
            .GroupBy(t => t.From)
            .ToList();

        foreach (var group in timeoutTransitionsByState)
        {
            if (group.Count() > 1)
            {
                var eventNames = string.Join(", ", group.Select(t => t.Event));
                context.ReportDiagnostic(Diagnostic.Create(
                    DuplicateTimeoutForStateDescriptor,
                    Location.None,
                    group.Key,
                    eventNames));
                return; // Stop generation if duplicate timeouts found
            }
        }

        var timeoutTransitions = timeoutTransitionsByState
            .ToDictionary(
                g => g.Key,
                g => g.Select(t => (t.Event, Timeout: t.Timeout!)).First());

        var accessibility = workflowClass.DeclaredAccessibility.ToString().ToLower();
        var schedulingServiceInterface = generateSchedulingServiceInterface(timeoutTransitions);
        var schedulingServiceClass = generateSchedulingServiceClass(timeoutTransitions);
        var schedulingMethod = generateSchedulingMethod(timeoutTransitions);

        var stateEnum = $$"""
            {{accessibility}} enum WfState
            {
                {{string.Join(",\n    ", states.Select(x => $"{x.Name} = {x.Id}"))}}
            }
            """;

        var replyEvents = transitions
            .Where(t => t.Color == ReplayColor)
            .Select(t => t.Event)
            .Distinct()
            .OrderBy(e => e)
            .ToList();

        var replyEnum = $$"""
            {{accessibility}} enum Reply
            {
                {{string.Join(",\n    ", replyEvents.Select(SanitizeName))}}
            }
            """;

        var handleReplyMethod = new StringBuilder();
        handleReplyMethod.AppendLine($$"""
            public virtual async Task HandleReply(Reply reply)
            {
                switch (reply)
                {
        """);

        foreach (var replyEvent in replyEvents)
        {
            var eventName = SanitizeName(replyEvent);
            handleReplyMethod.AppendLine($$"""
                    case Reply.{{eventName}}:
                        await ProcessEvent(new WfEvent.{{eventName}}(IsReply: true));
                        break;
        """);
        }

        handleReplyMethod.AppendLine($$"""
                    default:
                        throw new ArgumentOutOfRangeException($"{reply} в состоянии {CurrentState} не обработан");
                }
            }
        """);

        var switchCases = new StringBuilder();
        var afterSwitchCases = new StringBuilder();
        var eventMethodMap = new Dictionary<string, string>();
        var afterEventMethodMap = new Dictionary<string, string>();

        foreach (var (from, to, e, _, _) in transitions)
        {
            var eventName = SanitizeName(e);
            var eventType = $"WfEvent.{eventName}";

            switchCases.AppendLine($$"""
                        case (WfState.{{from}}, {{eventType}} e):
                            isAccepted = await On{{eventName}}(e, WfState.{{from}}, WfState.{{to}});
                            return isAccepted ? WfState.{{to}} : WfState.{{from}};
            """);

            afterSwitchCases.AppendLine($$"""
                        case (WfState.{{from}}, {{eventType}} e) when newState == WfState.{{to}}:
                            await After{{eventName}}(e, WfState.{{from}}, WfState.{{to}});
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

        var canProcessEventMethod = new StringBuilder();

        canProcessEventMethod.AppendLine($$"""
        /// <summary>
        /// Checks if the specified event can be processed in the current state
        /// </summary>
        /// <param name="event">The event to check</param>
        /// <returns>True if the event can be processed in current state, false otherwise</returns>
        public bool CanProcessEvent(WfEvent @event)
        {
            switch (CurrentState, @event)
            {
    """);

        foreach (var (from, to, e, _, _) in transitions)
        {
            var eventName = SanitizeName(e);
            var eventType = $"WfEvent.{eventName}";

            canProcessEventMethod.AppendLine($$"""
                case (WfState.{{from}}, {{eventType}} _):
                    return true;
        """);
        }

        canProcessEventMethod.AppendLine($$"""
                default:
                    return false;
            }
        }
    """);

        var eventMethods = string.Join("\n", eventMethodMap.OrderBy(x => x.Key).Select(x => x.Value));
        var afterEventMethods = string.Join("\n", afterEventMethodMap.OrderBy(x => x.Key).Select(x => x.Value));

        var automaton = $$"""
            {{accessibility}} abstract partial class {{workflowClass.Name}}Base
            {
                protected WfState _currentState = WfState.{{states.First().Name}};
                
                public WfState CurrentState => _currentState;
                public abstract IEmployee Responsible { get; }
                public abstract DateTime ResponsibilityTransferTime { get; }
            
                protected abstract ISchedulingService Scheduler { get; }
            
            {{schedulingMethod}}

            {{canProcessEventMethod}}

            {{handleReplyMethod}}

                public async Task<bool> ProcessEvent(WfEvent @event)
                {
                    var oldState = _currentState;
                    var newState = await Transition(oldState, @event);

                    OnAllTransition(oldState, @event, newState);

                    _currentState = newState;
                    
                    await AfterTransition(oldState, newState, @event);
                    return newState != oldState;
                }

                protected virtual Task AfterUnprocessedTransition(WfState oldState, WfState newState, WfEvent @event) => Task.CompletedTask;

            {{eventMethods}}
                
            {{afterEventMethods}}
                
                protected abstract void LogInfo(string message, [CallerMemberName] string? member = null);
                protected virtual Task OnNoTransition(WfState currentState, WfEvent @event) => Task.CompletedTask;
                protected virtual void OnAllTransition(WfState oldState, WfEvent @event, WfState newState) { }

                protected virtual async Task AfterTransition(WfState oldState, WfState newState, WfEvent @event)
                {
                    switch (oldState, @event)
                    {
            {{afterSwitchCases}}
                        default:
                            break;
                    }
                }

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
            }
            """;

        var fullCode = $$"""
            // <auto-generated/>
            #nullable enable
            
            using BugWatcher.Common;
            using BugWatcher.Employees.Interfaces;
            using System.Globalization;
            using System.Runtime.CompilerServices;

            namespace {{workflowClass.ContainingNamespace.ToDisplayString()}};
            
            {{stateEnum}}
            
            {{replyEnum}}

            {{schedulingServiceInterface}}
            
            {{schedulingServiceClass}}

            {{automaton}}
            """;
        context.AddSource($"{workflowClass.Name}Base.g.cs", SourceText.From(fullCode, Encoding.UTF8));

        static bool hasErrors(SourceProductionContext context, List<DotNodeStatement> nodeStatements)
        {
            var hasError = false;
            var idMap = new Dictionary<int, string>();

            foreach (var node in nodeStatements)
            {
                var idAttr = node.Attributes.FirstOrDefault(a => a.Name == "id");
                if (idAttr == null)
                {
                    ReportError(context, "WF004",
                        $"Node '{node.Name}' is missing required 'id' attribute",
                        null);
                    hasError = true;
                    continue;
                }

                if (idAttr.Value is not DotNumber number)
                {
                    ReportError(context, "WF005",
                        $"Invalid id value for node '{node.Name}'. Must be an integer without quotes.",
                        null);
                    hasError = true;
                    continue;
                }

                var id = number.Value;

                if (idMap.ContainsKey(id))
                {
                    ReportError(context, "WF006",
                        $"Duplicate id {id} found for nodes '{idMap[id]}' and '{node.Name}'.",
                        null);
                    hasError = true;
                    continue;
                }

                idMap[id] = node.Name;
            }

            return hasError;
        }
        string generateSchedulingServiceInterface(Dictionary<string, (string Event, string Timeout)> timeoutTransitions)
        {
            var methods = new StringBuilder();

            foreach (var state in timeoutTransitions.Keys)
            {
                var methodName = $"Is{SanitizeName(state)}TimedOut";
                methods.AppendLine($"    bool {methodName}(DateTime responsibilityTransferTime);");
            }

            // Добавляем стандартные методы таймаутов
            methods.AppendLine("""
                bool IsReplyTimedOut(DateTime responsibilityTransferTime);
            """);

            return $$"""
            public interface ISchedulingService
            {
            {{methods}}
            }
            """;
        }
        string generateSchedulingServiceClass(Dictionary<string, (string Event, string Timeout)> timeoutTransitions)
        {
            var fields = new StringBuilder();
            var methods = new StringBuilder();

            // Добавляем стандартные методы таймаутов
            methods.AppendLine("""
                public bool IsReplyTimedOut(DateTime responsibilityTransferTime) => Now - responsibilityTransferTime > ReplyTimeout;
                private DateTime Now => Clock.Now;
            """);

            // Генерируем константы для таймаутов
            foreach (var (state, (eventName, timeout)) in timeoutTransitions)
            {
                var fieldName = $"{SanitizeName(state)}Timeout";
                var builder = new StringBuilder();
                var timeSpan = TimeSpan.Parse(timeout, CultureInfo.InvariantCulture);
                var days = timeSpan.Days > 0 ? $"days: {timeSpan.Days}, " : null;

                fields.AppendLine($"""
                    public static readonly TimeSpan {fieldName} = new TimeSpan({days}hours: {timeSpan.Hours}, minutes: {timeSpan.Minutes}, seconds: {timeSpan.Seconds});
                """);

                var methodName = $"Is{SanitizeName(state)}TimedOut";
                methods.AppendLine($$"""
                public bool {{methodName}}(DateTime responsibilityTransferTime) => Now - responsibilityTransferTime > {{fieldName}};
            """);
            }

            return $$"""
            public sealed class SchedulingService(IClock Clock) : ISchedulingService
            {
                public static readonly TimeSpan ReplyTimeout = TimeSpan.FromMinutes(10);
                        
            {{fields}}
            
            {{methods}}
            }
            """;
        }
        string generateSchedulingMethod(Dictionary<string, (string Event, string Timeout)> timeoutTransitions)
        {
            var cases = new StringBuilder();

            foreach (var (state, (eventName, timeout)) in timeoutTransitions)
            {
                var methodName = $"Is{SanitizeName(state)}TimedOut";
                var eventType = $"WfEvent.{SanitizeName(eventName)}";

                cases.AppendLine($$"""
                    case WfState.{{state}} when Scheduler.{{methodName}}(ResponsibilityTransferTime):
                        LogInfo($"Планировщик инициировал событие {{eventName}} так как {Responsible.DisplayName} не успел обработать состояние {{state}} в отведенное время.");
                        await ProcessEvent(new {{eventType}}());
                        break;
            """);
            }

            return $$"""
                public async Task Scheduling()
                {
                    switch (CurrentState)
                    {
                {{cases}}
                    }
                }
            """;
        }
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

        // Получаем все уже определенные record'ы в базовом классе
        var existingRecords = baseRecord.GetMembers()
            .OfType<INamedTypeSymbol>()
            .Where(s => s.IsRecord && s.BaseType?.Equals(baseRecord, SymbolEqualityComparer.Default) == true)
            .Select(s => s.Name)
            .ToDictionary(name => name, StringComparer.Ordinal);

        var records = new StringBuilder();

        foreach (var e in events)
        {
            var recordName = SanitizeName(e);

            if (existingRecords.ContainsKey(recordName))
                records.AppendLine($"    public sealed partial record {recordName};");
            else
                records.AppendLine($"    public sealed partial record {recordName}(bool IsReply = false) : {baseRecord.Name}(IsReply);");
        }

        var code = $$"""
        // <auto-generated/>
        #nullable enable
        
        namespace {{baseRecord.ContainingNamespace.ToDisplayString()}};
        
        {{accessibility}} abstract partial record {{baseRecord.Name}}(bool IsReply)
        {
        {{records}}
        }
        """;

        context.AddSource($"{baseRecord.Name}.g.cs", SourceText.From(code, Encoding.UTF8));
    }

    private static string SanitizeName(string name) =>
        new(name.Where(c => char.IsLetterOrDigit(c)).ToArray());
}
