using CsNitra.Ast;
using ExtensibleParser;

namespace CsNitra.TypeChecking;

/// <summary>
/// Grammar type checker
/// </summary>
public sealed partial record TypeChecker(Source source, IEnumerable<Terminal> terminals)
{
    private readonly TypeCheckingContext _context = new(source, terminals);
    private readonly List<PrecedenceDependency> _precedenceDependencies = new();

    public (IReadOnlyList<Diagnostic> Diagnostics, Scope GlobalScope) CheckGrammar(GrammarAst grammar)
    {
        CollectDeclarations(grammar);
        ResolveAndCheck(grammar);
        return (_context.Diagnostics, _context.GlobalScope);
    }

    private void CollectDeclarations(GrammarAst grammar)
    {
        var collector = new DeclarationCollectorVisitor(_context, _precedenceDependencies);
        foreach (var statement in grammar.Statements)
            statement.Accept(collector);
    }

    private void ResolveAndCheck(GrammarAst grammar)
    {
        ResolvePrecedenceDependencies();

        var checker = new TypeCheckerVisitor(_context);
        grammar.Accept(checker);

        ResolveSymbolReferences(grammar);
    }

    private void ResolvePrecedenceDependencies()
    {
        var orderedPrecedences = new List<Identifier>();
        var allIdentifiers = new HashSet<string>();

        foreach (var dependency in _precedenceDependencies)
        {
            if (!TryMergePrecedenceList(dependency.Identifiers, orderedPrecedences, allIdentifiers))
            {
                _context.ReportError(
                    $"Cannot merge precedence list '{string.Join(", ", dependency.Identifiers.Select(i => i.Value))}' " +
                    $"with existing precedence order",
                    dependency.Location
                );
                return;
            }
        }

        for (int i = 0, bp = orderedPrecedences.Count; i < orderedPrecedences.Count; i++, bp--)
        {
            var symbol = new PrecedenceSymbol(
                orderedPrecedences[i],
                _context.Source,
                BindingPower: bp
            );
            _context.GlobalScope.AddSymbol(symbol);
        }
    }

    private bool TryMergePrecedenceList(
        IReadOnlyList<Identifier> newList,
        List<Identifier> existingList,
        HashSet<string> allIdentifiers)
    {
        if (existingList.Count == 0)
        {
            existingList.AddRange(newList);
            allIdentifiers.UnionWith(newList.Select(i => i.Value));
            return true;
        }

        var intersection = newList.FirstOrDefault(id => allIdentifiers.Contains(id.Value));
        if (intersection == null)
            return false;

        var existingPos = existingList.FindIndex(id => id.Value == intersection.Value);
        var insertIndex = existingPos;
        for (int i = 0; i < newList.Count; i++)
        {
            if (newList[i].Value == intersection.Value)
                break;

            if (!allIdentifiers.Contains(newList[i].Value))
            {
                existingList.Insert(insertIndex++, newList[i]);
                allIdentifiers.Add(newList[i].Value);
            }
            else
                return false;
        }

        var foundIntersection = false;
        insertIndex = existingPos + 1;
        for (int i = 0; i < newList.Count; i++)
        {
            if (newList[i].Value == intersection.Value)
            {
                foundIntersection = true;
                continue;
            }

            if (foundIntersection)
            {
                if (!allIdentifiers.Contains(newList[i].Value))
                {
                    existingList.Insert(insertIndex++, newList[i]);
                    allIdentifiers.Add(newList[i].Value);
                }
                else
                    return false;
            }
        }

        return true;
    }

    private void ResolveSymbolReferences(GrammarAst grammar) =>
        grammar.Accept(new SymbolReferenceResolver(_context));
}

