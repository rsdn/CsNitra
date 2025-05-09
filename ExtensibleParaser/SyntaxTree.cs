using System.Diagnostics;
using System.Collections.Generic;

namespace ExtensibleParaser;

public interface ISyntaxNode
{
    string Kind { get; }
    int StartPos { get; }
    int EndPos { get; }
    bool IsRecovery { get; }
    void Accept(ISyntaxVisitor visitor);
    ReadOnlySpan<char> AsSpan(string input);
    string ToString(string input);
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

[DebuggerDisplay("{Kind}: [{StartPos}-{EndPos}) {Length} {Debug()} ({GetType().Name})")]
[DebuggerTypeProxy(typeof(DebugView))]
public abstract record Node(string Kind, int StartPos, int EndPos, bool IsRecovery = false) : ISyntaxNode
{
    public int Length => EndPos - StartPos;
    public virtual ReadOnlySpan<char> AsSpan(string input) => input.AsSpan(StartPos, EndPos - StartPos);
    public virtual string ToString(string input) => input[StartPos..EndPos];
    public abstract void Accept(ISyntaxVisitor visitor);

#pragma warning disable CS0618 // Type or member is obsolete
    public string? Debug()
    {
        var input = Parser.Input;

        if (input == null)
            return null;

        var start = Math.Max(StartPos - 10, 0);
        var end = Math.Min(EndPos + 10, input.Length);

        return $"«{input[start..StartPos]}»  «{input[StartPos..EndPos]}»  «{input[EndPos..end]}»";
    }

    public string? DebugContent()
    {
        var input = Parser.Input;

        if (input == null)
            return null;

        return input[StartPos..EndPos];
    }

    private sealed class DebugView(Node node)
    {
        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public Tree[] Elements => Parser.Input == null ? [] : new Tree(Parser.Input, node).Elements;
    }
#pragma warning restore CS0618 // Type or member is obsolete
}

public record TerminalNode(string Kind, int StartPos, int EndPos, int ContentLength, bool IsRecovery = false) : Node(Kind, StartPos, EndPos, IsRecovery)
{
    public override ReadOnlySpan<char> AsSpan(string input) => input.AsSpan(StartPos, ContentLength);
    public override string ToString(string input) => input.Substring(StartPos, ContentLength);
    public override void Accept(ISyntaxVisitor visitor) => visitor.Visit(this);
    public override string ToString() => $"{Kind}Terminal([{StartPos},{StartPos + ContentLength}), «{DebugContent() ?? Kind}»)";
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
