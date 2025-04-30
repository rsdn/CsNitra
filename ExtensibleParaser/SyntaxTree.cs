namespace ExtensibleParaser;


public interface ISyntaxNode
{
    string Kind { get; }
    int StartPos { get; }
    int EndPos { get; }
    void Accept(ISyntaxVisitor visitor);
    ReadOnlySpan<char> AsSpan(string input);
    string ToString(string input);
}


public abstract record Node(string Kind, int StartPos, int EndPos) : ISyntaxNode
{
    public virtual ReadOnlySpan<char> AsSpan(string input) => input.AsSpan(StartPos, EndPos - StartPos);
    public virtual string ToString(string input) => input[StartPos..EndPos];
    public abstract void Accept(ISyntaxVisitor visitor);
}

public interface ISyntaxVisitor
{
    void Visit(TerminalNode node);
    void Visit(SeqNode node);
    void Visit(ChoiceNode node);
    void Visit(RefNode node);
    void Visit(ReqRefNode node);
    void Visit(SomeNode node);
    void Visit(NoneNode node);
}

public record TerminalNode(string Kind, int StartPos, int EndPos, int ContentLength) : Node(Kind, StartPos, EndPos)
{
    public int Length => EndPos - StartPos;
    public override ReadOnlySpan<char> AsSpan(string input) => input.AsSpan(StartPos, ContentLength);
    public override string ToString(string input) => input.Substring(StartPos, ContentLength);
    public override void Accept(ISyntaxVisitor visitor) => visitor.Visit(this);
    public override string ToString() => $"TerminalNode([{StartPos},{StartPos + ContentLength}), «{Kind}»)";
}

public record SeqNode(string Kind, IReadOnlyList<ISyntaxNode> Elements, int StartPos, int EndPos) : Node(Kind, StartPos, EndPos)
{
    public override void Accept(ISyntaxVisitor visitor) => visitor.Visit(this);
}

public record ChoiceNode(string Kind, IReadOnlyList<ISyntaxNode> Alternatives, int StartPos, int EndPos) : Node(Kind, StartPos, EndPos)
{
    public override void Accept(ISyntaxVisitor visitor) => visitor.Visit(this);
}

public abstract record OptionalNode(string Kind, int StartPos, int EndPos) : Node(Kind, StartPos, EndPos);

public record SomeNode(string Kind, ISyntaxNode Value, int StartPos, int EndPos)
    : OptionalNode(Kind, StartPos, EndPos)
{
    public override void Accept(ISyntaxVisitor visitor) => Value.Accept(visitor);
}

public record NoneNode(string Kind, int StartPos, int EndPos)
    : OptionalNode(Kind, StartPos, EndPos)
{
    public override void Accept(ISyntaxVisitor visitor) { }
}

public record RefNode(string Kind, string RuleName, ISyntaxNode Inner, int StartPos, int EndPos) : Node(Kind, StartPos, EndPos)
{
    public override void Accept(ISyntaxVisitor visitor) => visitor.Visit(this);
}

public record ReqRefNode(string Kind, string RuleName, int Precedence, bool LeftAssoc, ISyntaxNode Inner, int StartPos, int EndPos) : Node(Kind, StartPos, EndPos)
{
    public override void Accept(ISyntaxVisitor visitor) => visitor.Visit(this);
}