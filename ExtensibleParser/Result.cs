using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace ExtensibleParser;

[DebuggerTypeProxy(typeof(DebugView))]
public readonly record struct Result
{
    public readonly int NewPos;
    public readonly int MaxFailPos;
    private readonly ISyntaxNode? Node;

    public bool IsSuccess => NewPos >= 0;

    public readonly bool TryGetSuccess([MaybeNullWhen(false)] out ISyntaxNode node, out int newPos)
    {
        if (NewPos >= 0)
        {
            node = Node!;
            newPos = NewPos;
            return true;
        }

        node = null;
        newPos = -1;
        return false;
    }

    public readonly bool TryGetSuccess([MaybeNullWhen(false)] out (ISyntaxNode Node, int NewPos) success)
    {
        if (NewPos >= 0)
        {
            success = (Node!, NewPos);
            return true;
        }

        success = default;
        return false;
    }

    public readonly string? GetErrorOrDefault() => NewPos >= 0 ? null : "Error";

    public readonly string GetError() => NewPos >= 0 ? throw new InvalidCastException("Result is Success") : "Error";

    public readonly bool TryGetFailed([MaybeNullWhen(false)] out string error)
    {
        if (NewPos < 0)
        {
            error = "Error";
            return true;
        }

        error = null;
        return false;
    }

    public Result WithPrefixOnly(Result result) => new(result.Node, result.NewPos, result.MaxFailPos);

#pragma warning disable CS0618 // Type or member is obsolete
    public override string ToString() => Parser.Input == null
        ? NewPos < 0 ? $"Failure({~NewPos})" : $"Success(NewPos={NewPos}, {Node})"
        : ToString(Parser.Input);
#pragma warning disable CS0618 // Type or member is obsolete

    public string ToString(string input)
    {
        return NewPos < 0 ? $"Failure({~NewPos})" : success((Node)Node!);
        string success(Node node) => $"Success([{node.StartPos}-{node.EndPos}), {node.Debug()})";
    }

    public static Result Success(ISyntaxNode result, int newPos, int maxFailPos) => new(result, newPos, maxFailPos);
    public static Result Failure(int failPos) => new(null, newPos: -1, maxFailPos: failPos);

    private Result(ISyntaxNode? node, int newPos, int maxFailPos)
    {
        Node = node;
        NewPos = newPos;
        MaxFailPos = maxFailPos;
    }

    private sealed class DebugView(Result result)
    {
        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public object Elements => Parser.Input == null || !result.IsSuccess
            ? new Tree[0]
            : new Tree(Parser.Input, result.Node!).Elements;
    }
}
