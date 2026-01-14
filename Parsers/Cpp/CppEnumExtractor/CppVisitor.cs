using ExtensibleParaser;

namespace CppEnumExtractor;

public class CppVisitor : ISyntaxVisitor
{
    private CppAst? _currentResult;
    private readonly string _input;

    public CppProgram Result { get; private set; } = new CppProgram(new List<CppAst>());

    public CppVisitor(string input)
    {
        _input = input;
    }

    public void Visit(TerminalNode node)
    {
        var value = node.ToString(_input);
        _currentResult = node.Kind switch
        {
            "Identifier" => new IdentifierValue(value),
            "EnumExpression" => new EnumExpressionValue(value.Trim()),
            "AnyLine" => new SkippedLine(),
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
            {
                children.Add(_currentResult);
            }
        }

        _currentResult = node.Kind switch
        {
            "ProgramItems" => ProcessProgramItems(children),
            "NamespaceDecl" => ProcessNamespaceDecl(children),
            "AnonymousNamespaceDecl" => ProcessAnonymousNamespaceDecl(children), // Добавляем обработку анонимных пространств имен
            "EnumDecl" => ProcessEnumDecl(children),
            "EnumMember" => ProcessEnumMember(children),
            "EnumValue" => ProcessEnumValue(children),
            "NamespaceStart" => null, // Игнорируем, нужно только для предиката
            "EnumStart" => null, // Игнорируем, нужно только для предиката
            "Braces" => null, // Игнорируем закрывающие скобки
            "SkipLine" => ProcessSkipLine(children),
            _ => throw new InvalidOperationException($"Unknown SeqNode kind: {node.Kind}")
        };

        // Если это корневой Program, сохраняем результат
        if (node.Kind == "ProgramItems" && _currentResult is CppProgram program)
        {
            Result = program;
        }
    }

    private CppAst ProcessSkipLine(List<CppAst> children)
    {
        // Пропускаем строку
        return new SkippedLine();
    }

    private CppAst ProcessProgramItems(List<CppAst> children)
    {
        var items = new List<CppAst>();

        foreach (var child in children)
        {
            if (child is SkippedLine) continue; // Игнорируем пропущенные строки

            if (child is CppProgram program)
            {
                // Извлекаем элементы из вложенной программы
                items.AddRange(program.Items);
            }
            else if (child != null)
            {
                items.Add(child);
            }
        }

        return new CppProgram(items);
    }

    private CppAst ProcessNamespaceDecl(List<CppAst> children)
    {
        if (children.Count == 2 && children[0] is IdentifierValue namespaceName && children[1] is CppProgram body)
            return new NamespaceDeclaration(namespaceName.Name, body);

        throw new InvalidOperationException("Invalid namespace declaration");
    }

    // Добавляем обработку анонимного пространства имен
    private CppAst ProcessAnonymousNamespaceDecl(List<CppAst> children)
    {
        if (children.Count == 1 && children[0] is CppProgram body)
            return new AnonymousNamespaceDeclaration(body);

        throw new InvalidOperationException("Invalid anonymous namespace declaration");
    }

    private CppAst ProcessEnumDecl(List<CppAst> children)
    {
        if (children.Count >= 2 && children[0] is IdentifierValue enumName && children[1] is EnumMemberListValue members)
            return new EnumDeclaration(enumName.Name, members.Members);

        throw new InvalidOperationException("Invalid enum declaration");
    }

    private CppAst ProcessEnumMember(List<CppAst> children)
    {
        if (children.Count >= 1 && children[0] is IdentifierValue memberName)
        {
            string? value = null;
            if (children.Count > 1 && children[1] is EnumExpressionValue expressionValue)
            {
                value = expressionValue.Value;
            }
            return new EnumMember(memberName.Name, value);
        }
        throw new InvalidOperationException("Invalid enum member");
    }

    private CppAst ProcessEnumValue(List<CppAst> children)
    {
        if (children.Count == 1 && children[0] is EnumExpressionValue expressionValue)
            return expressionValue;

        throw new InvalidOperationException("Invalid enum value");
    }

    public void Visit(ListNode node)
    {
        var members = new List<EnumMember>();

        foreach (var element in node.Elements)
        {
            element.Accept(this);
            if (_currentResult is EnumMember member)
            {
                members.Add(member);
            }
        }

        _currentResult = new EnumMemberListValue(members);
    }

    public void Visit(SomeNode node)
    {
        node.Value.Accept(this);
    }

    public void Visit(NoneNode node)
    {
        _currentResult = null;
    }

    // Вспомогательные классы для значений
    private record IdentifierValue(string Name) : CppAst;
    private record EnumExpressionValue(string Value) : CppAst;
    private record EnumMemberListValue(List<EnumMember> Members) : CppAst;
}
