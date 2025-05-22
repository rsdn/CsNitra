namespace Workflow;

public sealed class WorkflowEventAttribute(string dotFileName) : Attribute
{
    public string DotFileName { get; } = dotFileName;
}
