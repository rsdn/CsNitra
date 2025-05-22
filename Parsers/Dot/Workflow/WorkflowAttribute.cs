namespace Workflow;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class WorkflowAttribute(string dotFileName) : Attribute
{
    public string DotFileName { get; } = dotFileName;
}
