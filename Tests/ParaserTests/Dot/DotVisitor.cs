using System.Collections.Generic;
using ExtensibleParaser;

namespace Dot;

public class DotVisitor(string input) : ISyntaxVisitor
{
    public DotGraph? Result { get; private set; }
    private DotAst? CurrentResult { get; set; }

    public void Visit(TerminalNode node)
    {
        var span = node.AsSpan(input);
        CurrentResult = node.Kind switch
        {
            "Identifier" => new DotIdentifier(span.ToString(), node.StartPos, node.EndPos),
            "QuotedString" => new DotQuotedString(span, node.StartPos, node.EndPos),
            "Number" => new DotNumber(int.Parse(span), node.StartPos, node.EndPos),
            _ => new DotLiteral(span.ToString(), node.StartPos, node.EndPos)
        };
    }

    public void Visit(SeqNode node)
    {
        var children = new List<DotAst>();

        foreach (var element in node.Elements)
        {
            element.Accept(this);
            if (CurrentResult != null)
                children.Add(CurrentResult);
        }

        CurrentResult = node.Kind switch
        {
            "Graph" => new DotGraph(
                ((DotIdentifier)children[1]).Value,
                FlattenStatements(children.Skip(3).Take(children.Count - 4))
            ),
            "NodeStatement" => new DotNodeStatement(
                ((DotIdentifier)children[0]).Value,
                children.Count > 1 ? ((DotAttributeList)children[1]).Attributes : new List<DotAttribute>()
            ),
            "EdgeStatement" => new DotEdgeStatement(
                ((DotIdentifier)children[0]).Value,
                ((DotIdentifier)children[2]).Value,
                children.Count > 3 ? ((DotAttributeList)children[3]).Attributes : new List<DotAttribute>()
            ),
            "Subgraph" => new DotSubgraph(
                ((DotIdentifier)children[1]).Value,
                FlattenStatements(children.Skip(3).Take(children.Count - 4))
            ),
            "Assignment" => new DotAssignment(
                ((DotIdentifier)children[0]).Value,
                children[2] is DotQuotedString qs ? qs.Value : children[2].ToString()
            ),
            "AttributeList" => new DotAttributeList(getAttributes(children)),
            "Statements" => processZeroOrMany(children),
            "Attribute" => new DotAttribute(
                ((DotIdentifier)children[0]).Value,
                children[2] is DotQuotedString qs ? qs.Value : children[2].ToString()
            ),
            "AttributeRest" => (DotAttribute)children[1],
            "AttributeRestList" => children.Count switch
                {
                    0 => new DotAttributeList([]),
                    1 => (DotAttribute)children[0],
                    _ => new DotAttributeList(children.Cast<DotAttribute>().ToArray())
                },
            _ => throw new InvalidOperationException($"Unknown node kind: {node.Kind}")
        };

        if (node.Kind == "Graph")
            Result = (DotGraph)CurrentResult;
        return;
        DotAttribute[] getAttributes(List<DotAst> children)
        {
            if (children is [DotLiteral { Value: "[" }, .. var attributes, DotLiteral { Value: "]" }])
                return attributes.SelectMany(x => x switch
                {
                    DotAttribute a => [a],
                    DotAttributeList xs => xs.Attributes,
                    _ => throw new ArgumentException($"Unexpected: {x} ({x.GetType().Name})")
                }).ToArray();

            throw new ArgumentException($"Unexpected: {string.Join(", ", children)} ({string.Join(", ", children.Select(x => x.GetType().Name))})");

        }
        DotStatementList processZeroOrMany(List<DotAst> children)
        {
            var statements = new List<DotStatement>();
            foreach (var child in children)
            {
                if (child is DotStatement stmt)
                    statements.Add(stmt);
                else if (child is DotStatementList list)
                    statements.AddRange(list.Statements);
            }
            return new DotStatementList(statements);
        }
    }

    private List<DotStatement> FlattenStatements(IEnumerable<DotAst> nodes)
    {
        var result = new List<DotStatement>();
        foreach (var node in nodes)
        {
            if (node is DotStatementList list)
                result.AddRange(list.Statements);
            else if (node is DotStatement statement)
                result.Add(statement);
        }
        return result;
    }

    public void Visit(SomeNode node)
    {
        node.Value.Accept(this);
        if (CurrentResult == null)
            throw new InvalidOperationException("Optional node has no value");
    }

    public void Visit(NoneNode _)
    {
        CurrentResult = null;
    }

    private record DotStatementList(IReadOnlyList<DotStatement> Statements) : DotAst
    {
        public override string ToString() => string.Join("\n", Statements);
    }
}
