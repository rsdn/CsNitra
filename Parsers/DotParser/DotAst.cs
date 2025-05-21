using System.Text;

namespace Dot;

public abstract record DotAst
{
    public abstract override string ToString();
}

public record DotGraph(string Name, IReadOnlyList<DotStatement> Statements) : DotAst
{
    public override string ToString() => $"digraph {Name} {{\n{string.Join("\n", Statements)}\n}}";
}

public abstract record DotStatement : DotAst;

public record DotNodeStatement(string NodeId, IReadOnlyList<DotAttribute> Attributes) : DotStatement
{
    public override string ToString() => $"{NodeId} {AttributesToString()};";
    private string AttributesToString() => Attributes.Count > 0
        ? $"[{string.Join(", ", Attributes)}]"
        : "";
}

public record DotEdgeStatement(string FromNode, string ToNode, IReadOnlyList<DotAttribute> Attributes) : DotStatement
{
    public override string ToString() => $"{FromNode} -> {ToNode} {AttributesToString()};";
    private string AttributesToString() => Attributes.Count > 0
        ? $"[{string.Join(", ", Attributes)}]"
        : "";
}

public record DotSubgraph(string Name, IReadOnlyList<DotStatement> Statements) : DotStatement
{
    public override string ToString() => $"subgraph {Name} {{\n{string.Join("\n", Statements)}\n}}";
}

public record DotAttribute(string Name, DotTerminalNode Value) : DotAst
{
    public override string ToString() => $"{Name}={Value}";
}

public record DotAssignment(string Name, string Value) : DotStatement
{
    public override string ToString() => $"{Name}={Value};";
}

public abstract record DotTerminalNode(string Kind, int StartPos, int EndPos) : DotAst;

public record DotIdentifier(string Value, int StartPos, int EndPos) : DotTerminalNode("Identifier", StartPos, EndPos)
{
    public override string ToString() => Value;
}

public record DotQuotedString(string Value, string RawValue, int StartPos, int EndPos)
    : DotTerminalNode(Kind: "QuotedString", StartPos: StartPos, EndPos: EndPos)
{
    public DotQuotedString(ReadOnlySpan<char> span, int startPos, int endPos)
        : this(Value: ProcessQuotedString(span), RawValue: span[1..^1].ToString(), StartPos: startPos, EndPos: endPos)
    {
    }

    private static string ProcessQuotedString(ReadOnlySpan<char> span)
    {
        var content = span[1..^1];
        var result = new StringBuilder();

        for (int i = 0; i < content.Length; i++)
        {
            if (content[i] == '\\' && i + 1 < content.Length)
            {
                var nextChar = content[i + 1];
                switch (nextChar)
                {
                    case 'n':
                        result.Append('\n');
                        i++;
                        break;
                    case 'r':
                        result.Append('\r');
                        i++;
                        break;
                    case 't':
                        result.Append('\t');
                        i++;
                        break;
                    case '\\':
                        result.Append('\\');
                        i++;
                        break;
                    case '"':
                        result.Append('"');
                        i++;
                        break;
                    case 'L':
                    case 'G':
                    case 'l':
                    case 'N':
                    case 'T':
                        result.Append('\\').Append(nextChar);
                        i++;
                        break;
                    default:
                        result.Append(nextChar);
                        i++;
                        break;
                }
            }
            else
                result.Append(content[i]);
        }

        return result.ToString();
    }

    public override string ToString() => $"\"{RawValue}\"";
}

public record DotNumber(int Value, int StartPos, int EndPos) : DotTerminalNode("Number", StartPos, EndPos)
{
    public override string ToString() => Value.ToString();
}

public record DotLiteral(string Value, int StartPos, int EndPos) : DotTerminalNode("Literal", StartPos, EndPos)
{
    public override string ToString() => Value;
}

public record DotAttributeList(IReadOnlyList<DotAttribute> Attributes) : DotAst
{
    public override string ToString() => $"[{string.Join(", ", Attributes)}]";
}
public record DotAttributeRest(DotAttribute Attribute) : DotAst;
