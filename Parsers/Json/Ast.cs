namespace Json;

public abstract record JsonAst(int StartPos, int EndPos)
{
    public abstract override string ToString();
}

public record JsonObject(IReadOnlyList<JsonProperty> Properties, int StartPos, int EndPos) : JsonAst(StartPos, EndPos)
{
    public override string ToString() => $"{{{string.Join(", ", Properties)}}}";
}

public record JsonArray(IReadOnlyList<JsonAst> Elements, int StartPos, int EndPos) : JsonAst(StartPos, EndPos)
{
    public override string ToString() => $"[{string.Join(", ", Elements)}]";
}

public record JsonString(string Value, int StartPos, int EndPos) : JsonAst(StartPos, EndPos)
{
    public override string ToString() => $"\"{Value}\"";
}

public record JsonNumber(double Value, int StartPos, int EndPos) : JsonAst(StartPos, EndPos)
{
    public override string ToString() => Value.ToString();
}

public record JsonBoolean(bool Value, int StartPos, int EndPos) : JsonAst(StartPos, EndPos)
{
    public override string ToString() => Value ? "true" : "false";
}

public record JsonNull(int StartPos, int EndPos) : JsonAst(StartPos, EndPos)
{
    public override string ToString() => "null";
}

public record JsonProperty(string Name, JsonAst Value, int StartPos, int EndPos) : JsonAst(StartPos, EndPos)
{
    public override string ToString() => $"\"{Name}\": {Value}";
}
