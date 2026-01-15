namespace CppEnumExtractor;

public abstract record CppAst;

public sealed record CppProgram(List<CppAst> Items) : CppAst
{
    public override string ToString() => string.Join("\n", Items);
}

public sealed record NamespaceDeclaration(string Name, CppProgram Body) : CppAst
{
    public override string ToString() => $"namespace {Name} {{\n{Body}\n}}";
}

public sealed record AnonymousNamespaceDeclaration(CppProgram Body) : CppAst
{
    public override string ToString() => $"namespace {{\n{Body}\n}}";
}

public sealed record EnumDeclaration(string Name, bool IsClass, string? UnderlyingType, List<EnumMember> Members) : CppAst
{
    public override string ToString()
    {
        var classPart = IsClass ? "class " : "";
        var typePart = UnderlyingType != null ? $" : {UnderlyingType}" : "";
        return $$"""
            enum {{classPart}}{{Name}}{{typePart}} {
            {{string.Join(",\n", Members)}}
            }
            """;
    }
}

public sealed record EnumMember(string Name, string? Value) : CppAst
{
    public override string ToString() => Value != null ? $"{Name} = {Value}" : Name;
}

public sealed record SkippedLine() : CppAst
{
    public override string ToString() => string.Empty;
}

public abstract record Option() : CppAst
{
    public abstract bool IsSome { get; }
    public bool IsNone => !IsSome;

    public static Some<T> Some<T>(T value) where T : notnull => new(value);
    public static readonly None None = None.Create();
}

public sealed record Some<T>(T Value) : Option where T : notnull
{
    public override bool IsSome => true;
    public override string ToString() => $"Some({Value})";
}

public sealed record None : Option
{
    public override bool IsSome => false;
    public override string ToString() => "None()";

    internal static None Create() => new();
    private None() { }
}

public sealed record Keyword(string Value) : CppAst
{
    public override string ToString() => Value;
}
