using ExtensibleParser;

namespace CppSimplifiedParser;

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
            "class" => new CppSimplifiedParser.Keyword("class"),
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
            "NamespaceDecl" => children switch
            {
                [Identifier namespaceName, CppProgram body] => 
                    new NamespaceDeclaration(namespaceName.Name, body),
                _ => throw new InvalidOperationException("Invalid namespace declaration")
            },
            "AnonymousNamespaceDecl" => children switch
            {
                [CppProgram body] => new AnonymousNamespaceDeclaration(body),
                _ => throw new InvalidOperationException("Invalid anonymous namespace declaration")
            },
            "EnumDecl" => children switch
            {
                [Option cls, Identifier(var name), Option type, EnumMembers(var members)] =>
                    new EnumDeclaration(name, IsClass: cls.IsSome, 
                        type switch { Some<CppAst>(EnumType(var t)) => t, _ => null }, members),
                _ => throw new InvalidOperationException($"Invalid enum declaration: {string.Join(" ", children)}")
            },
            "EnumMember" => children switch
            {
                [Identifier(var name), Some<CppAst>(EnumExpr(var expr))] => 
                    new EnumMember(name, Value: expr),
                [Identifier(var name), None] => new EnumMember(name, Value: null),
                _ => throw new InvalidOperationException($"Invalid enum member: {string.Join(" ", children)}")
            },
            "EnumValue" => children switch
            {
                [EnumExpr expr] => expr,
                _ => throw new InvalidOperationException("Invalid enum value")
            },
            "EnumType" => children switch
            {
                [Identifier(var name)] => new EnumType(name),
                _ => throw new InvalidOperationException("Invalid enum type")
            },
            "Braces" => null,
            "SkipLine" => children switch
            {
                [SkippedLine] => new SkippedLine(),
                [Identifier, SkippedLine] => new SkippedLine(),
                _ => new SkippedLine()
            },
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
