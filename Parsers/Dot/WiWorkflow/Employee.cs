using BugWatcher.Employees.Interfaces;

namespace WiWorkflow;

public sealed class Employee : IEmployee
{
    public string DisplayName { get; } = "";
}

