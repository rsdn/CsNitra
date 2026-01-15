using ExtensibleParaser;

namespace CppEnumExtractor;

public class CppVisitor(string input) : ISyntaxVisitor
{
    private CppAst? _currentResult;

    public CppProgram Result { get; private set; } = new CppProgram(new List<CppAst>());

    public void Visit(TerminalNode node)
    {
        var value = node.ToString(input);

        _currentResult = node.Kind switch
        {
            "Identifier" => new Identifier(value),
            "EnumExpression" => new EnumExpr(value.Trim()),
            "AnyLine" => new SkippedLine(),
            "class" => new CppEnumExtractor.Keyword("class"),
            _ => null
        };
    }

    public void Visit(SeqNode node)
    {
        var children = new List<CppAst>();

        foreach (var element in node.Elements)
        {
            element.Accept(this);
            if (_currentResult != null)
                children.Add(_currentResult);

            _currentResult = null;
        }

        _currentResult = node.Kind switch
        {
            "ProgramItems" => ProcessProgramItems(children),
            "NamespaceDecl" => ProcessNamespaceDecl(children),
            "AnonymousNamespaceDecl" => ProcessAnonymousNamespaceDecl(children),
            "EnumDecl" => ProcessEnumDecl(children),
            "EnumMember" => ProcessEnumMember(children),
            "EnumValue" => ProcessEnumValue(children),
            "EnumType" => ProcessEnumType(children),
            "Braces" => null,
            _ => throw new InvalidOperationException($"Unknown SeqNode kind: {node.Kind}")
        };

        if (node.Kind == "ProgramItems" && _currentResult is CppProgram program)
            Result = program;
    }

    private CppAst ProcessProgramItems(List<CppAst> children)
    {
        var items = new List<CppAst>();

        foreach (var child in children)
        {
            if (child is SkippedLine)
                continue;

            if (child is CppProgram program)
                items.AddRange(program.Items);
            else if (child != null)
                items.Add(child);
        }

        return new CppProgram(items);
    }

    private CppAst ProcessNamespaceDecl(List<CppAst> children) => children switch
    {
        [Identifier namespaceName, CppProgram body] => new NamespaceDeclaration(namespaceName.Name, body),
        _ => throw new InvalidOperationException("Invalid namespace declaration")
    };

    private CppAst ProcessAnonymousNamespaceDecl(List<CppAst> children) => children switch
    {
        [CppProgram body] => new AnonymousNamespaceDeclaration(body),
        _ => throw new InvalidOperationException("Invalid anonymous namespace declaration")
    };

    private CppAst ProcessEnumDecl(List<CppAst> children) => children switch
    {
        [Option cls, Identifier(var name), Option type, EnumMembers(var members2)] =>
            new EnumDeclaration(name, IsClass: cls.IsSome, type switch { Some<CppAst>(EnumType(var t)) => t, _ => null }, members2),
        _ => throw new InvalidOperationException($"Invalid enum declaration: {string.Join(" ", children)}"),
    };

    private CppAst ProcessEnumMember(List<CppAst> children) => children switch
    {
        [Identifier(var name), Some<CppAst>(EnumExpr(var expr))] => new EnumMember(name, Value: expr),
        [Identifier(var name), None] => new EnumMember(name, Value: null),
        _ => throw new InvalidOperationException($"Invalid enum member: {string.Join(" ", children)}")
    };

    private CppAst ProcessEnumValue(List<CppAst> children) => children switch
    {
        [EnumExpr expressionValue] => expressionValue,
        _ => throw new InvalidOperationException("Invalid enum value")
    };

    private CppAst ProcessEnumType(List<CppAst> children) => children switch
    {
        [Identifier(var name)] => new EnumType(name),
        _ => throw new InvalidOperationException("Invalid enum type")
    };

    public void Visit(ListNode node)
    {
        var members = new List<EnumMember>();

        foreach (var element in node.Elements)
        {
            element.Accept(this);
            if (_currentResult is EnumMember member)
                members.Add(member);
        }

        _currentResult = new EnumMembers(members);
    }

    public void Visit(SomeNode node)
    {
        node.Value.Accept(this);
        _currentResult = Option.Some(_currentResult.AssertIsNonNull());
    }

    public void Visit(NoneNode node) => _currentResult = Option.None;

    private record Identifier(string Name) : CppAst;
    private record Keyword(string Value) : CppAst;
    private record EnumExpr(string Value) : CppAst;
    private record EnumType(string Value) : CppAst;
    private record EnumMembers(List<EnumMember> Members) : CppAst;
}
