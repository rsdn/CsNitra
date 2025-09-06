using System.Text;

using ExtensibleParaser;

namespace Json;

public class JsonVisitor(string input) : ISyntaxVisitor
{
    public JsonAst? Result { get; private set; }

    public void Visit(TerminalNode node)
    {
        var span = node.AsSpan(input).ToString();
        Result = node.Kind switch
        {
            "String" => new JsonString(ProcessJsonString(span[1..^1]), node.StartPos, node.EndPos),
            "Number" => new JsonNumber(double.Parse(span), node.StartPos, node.EndPos),
            "True" => new JsonBoolean(true, node.StartPos, node.EndPos),
            "False" => new JsonBoolean(false, node.StartPos, node.EndPos),
            "Null" => new JsonNull(node.StartPos, node.EndPos),
            "{" or "}" or "[" or "]" or ":" or "," => null, // Игнорируем литералы-символы
            _ => throw new InvalidOperationException($"Unknown terminal: {node.Kind}")
        };
    }

    public void Visit(SeqNode node)
    {
        var children = new List<JsonAst>();
        foreach (var element in node.Elements)
        {
            element.Accept(this);
            if (Result != null)
                children.Add(Result);
        }

        Result = node.Kind switch
        {
            "Object" => HandleObjectNode(children, node),
            "Property" => HandlePropertyNode(children, node),
            "Array" => HandleArrayNode(children, node),
            _ => throw new InvalidOperationException($"Unknown sequence: {node.Kind}")
        };
    }

    public void Visit(ListNode node)
    {
        var elements = new List<JsonAst>();
        foreach (var element in node.Elements)
        {
            element.Accept(this);
            if (Result != null)
                elements.Add(Result);
        }

        Result = node.Kind switch
        {
            "Properties" => new JsonObject(elements.OfType<JsonProperty>().ToList(), node.StartPos, node.EndPos),
            "Elements" => new JsonArray(elements, node.StartPos, node.EndPos),
            _ => throw new InvalidOperationException($"Unknown list: {node.Kind}")
        };
    }

    public void Visit(SomeNode node) => node.Value.Accept(this);

    public void Visit(NoneNode _) => Result = null;

    private JsonObject HandleObjectNode(List<JsonAst> children, SeqNode node)
    {
        if (children.Count == 1 && children[0] is JsonObject propertiesObject)
            return new JsonObject(propertiesObject.Properties, node.StartPos, node.EndPos);
        else
            return new JsonObject(children.OfType<JsonProperty>().ToArray(), node.StartPos, node.EndPos);
    }

    private JsonProperty HandlePropertyNode(List<JsonAst> children, SeqNode node)
    {
        if (children is [JsonString key, JsonAst value])
            return new JsonProperty(key.Value, value, node.StartPos, node.EndPos);

        throw new InvalidOperationException($"Invalid property structure. Expected [String, Value], got [{string.Join(", ", children.Select(c => c.GetType().Name))}]");
    }

    private JsonArray HandleArrayNode(List<JsonAst> children, SeqNode node)
    {
        if (children.Count == 1 && children[0] is JsonArray elementsArray)
            return new JsonArray(elementsArray.Elements, node.StartPos, node.EndPos);
        else
            return new JsonArray(children.Where(c => c is not JsonObject).ToArray(), node.StartPos, node.EndPos);
    }

    private static string ProcessJsonString(string input)
    {
        var result = new StringBuilder();
        for (int i = 0; i < input.Length; i++)
        {
            if (input[i] == '\\' && i + 1 < input.Length)
            {
                switch (input[i + 1])
                {
                    case '"': result.Append('"'); i++; break;
                    case '\\': result.Append('\\'); i++; break;
                    case '/': result.Append('/'); i++; break;
                    case 'b': result.Append('\b'); i++; break;
                    case 'f': result.Append('\f'); i++; break;
                    case 'n': result.Append('\n'); i++; break;
                    case 'r': result.Append('\r'); i++; break;
                    case 't': result.Append('\t'); i++; break;
                    case 'u' when i + 5 < input.Length:
                        var hex = input.Substring(i + 2, 4);
                        result.Append((char)Convert.ToInt32(hex, 16));
                        i += 5;
                        break;
                    default: result.Append(input[i + 1]); i++; break;
                }
            }
            else
                result.Append(input[i]);
        }

        return result.ToString();
    }
}
