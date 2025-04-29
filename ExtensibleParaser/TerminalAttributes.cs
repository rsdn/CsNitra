using System;

namespace ExtensibleParaser;

[AttributeUsage(AttributeTargets.Class)]
public class TerminalMatcherAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Method)]
public class RegexAttribute : Attribute
{
    public string Pattern { get; }

    public RegexAttribute(string pattern)
    {
        Pattern = pattern;
    }
}