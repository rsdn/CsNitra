using Workflow;

namespace WiWorkflow;

[Workflow("wf.dot")]
internal sealed partial class WiWorkflow
{
    // Логика Workflow генерируются автоматически по файлу указанному в атрибуте.
    // Перейдите на определение, чтобы увидить сгенерированаю часть этого тип.
    // Если в этом классе объявить метод OnИмяСобытия(WfEvent.AssignTriage @event, WfState oldState, WfState newState, ref bool isAccepted)
    // В нем можно будет обработать переход из состояния в состояние по этому событию.
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

