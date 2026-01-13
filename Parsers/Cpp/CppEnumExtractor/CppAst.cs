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

public sealed record EnumDeclaration(string Name, List<EnumMember> Members) : CppAst
{
    public override string ToString() =>
        $"enum {Name} {{\n{string.Join(",\n", Members)}\n}}";
}

public sealed record EnumMember(string Name, string? Value) : CppAst
{
    public override string ToString() => Value != null ? $"{Name} = {Value}" : Name;
}

public sealed record SkippedLine() : CppAst
{
    public override string ToString() => string.Empty;
}
