namespace ExtensibleParaser;

[AttributeUsage(AttributeTargets.Class)]
public class TerminalMatcherAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Method)]
public class RegexAttribute(string pattern) : Attribute
{
    public string Pattern { get; } = pattern;
}
