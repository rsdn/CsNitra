using BugWatcher.Employees.Interfaces;
using System.Runtime.CompilerServices;
using Workflow;

namespace WiWorkflow;

[Workflow("wf.dot")]
internal sealed partial class WiWorkflow : WiWorkflowBase
{
    public override IEmployee Responsible { get; } = new Employee();

    public override DateTime ResponsibilityTransferTime { get; }

    protected override ISchedulingService Scheduler => throw new NotImplementedException();

    protected override void LogInfo(string message, [CallerMemberName] string? member = null)
    {
    }

    // Логика Workflow генерируются автоматически по файлу указанному в атрибуте.
    // Перейдите на определение, чтобы увидить сгенерированаю часть этого тип.
    // Если в этом классе объявить метод OnИмяСобытия(WfEvent.AssignTriage @event, WfState oldState, WfState newState, ref bool isAccepted)
    // В нем можно будет обработать переход из состояния в состояние по этому событию.

    protected override Task<bool> OnAcceptTask(WfEvent.AcceptTask @event, WfState oldState, WfState newState)
    {
        return base.OnAcceptTask(@event, oldState, newState);
    }

    protected override void OnAllTransition(WfState oldState, WfEvent @event, WfState newState)
    {
        base.OnAllTransition(oldState, @event, newState);
    }

    protected override Task AfterTriggerDevTimeout(WfEvent.TriggerDevTimeout @event, WfState oldState, WfState newState)
    {
        return base.AfterTriggerDevTimeout(@event, oldState, newState);
    }
}

[WorkflowEvent("wf.dot")]
internal abstract partial record WfEvent
{
    // record-ы, наслидники WfEvent генерируются автоматически по файлу указанному в атрибуте.
    // Перейдите на определение, чтобы увидить сгенерированаю часть этого тип.
}

internal static class Program
{
    public static void Main()
    {
        Console.WriteLine("Hello, World!");
    }
}

