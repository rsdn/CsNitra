using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;

namespace ExtensibleParaser;

[DebuggerTypeProxy(typeof(DebugView))]
public readonly record struct Result
{
    public readonly int NewPos;
    private readonly ISyntaxNode? Node;
    public readonly bool IsPrefixOnly;

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

    public Result WithPrefixOnly(Result result) =>
        new(result.Node, result.NewPos, isPrefixOnly: true);

#pragma warning disable CS0618 // Type or member is obsolete
    public override string ToString() => Parser.Input == null
        ? NewPos < 0 ? "Failure" : $"Success(NewPos={NewPos}, {Node})"
        : ToString(Parser.Input);
#pragma warning disable CS0618 // Type or member is obsolete

    public string ToString(string input)
    {
        return NewPos < 0 ? "Failure" : success((Node)Node!);
        string success(Node node) => $"Success([{node.StartPos}-{node.EndPos}), {node.Debug()})";
    }

    public static Result Success(ISyntaxNode result, int newPos, bool isPrefixOnly = false) => new(result, newPos, isPrefixOnly);
    public static Result Failure() => new(null, -1, isPrefixOnly: false);

    private Result(ISyntaxNode? node, int newPos, bool isPrefixOnly)
    {
        Node = node;
        NewPos = newPos;
        IsPrefixOnly = isPrefixOnly;
    }

    private sealed class DebugView(Result result)
    {
        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public Tree[] Elements => Parser.Input == null || !result.IsSuccess
            ? []
            : new Tree(Parser.Input, result.Node!).Elements;
    }
}