using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;

namespace ExtensibleParaser;

[DebuggerTypeProxy(typeof(DebugView))]
public readonly record struct Result
{
    public readonly int NewPos;
    private readonly object NodeOrError;

    public bool IsSuccess => NewPos >= 0;

    public readonly bool TryGetSuccess([MaybeNullWhen(false)] out ISyntaxNode node, out int newPos)
    {
        if (NewPos >= 0)
        {
            node = (ISyntaxNode)NodeOrError;
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
            success = ((ISyntaxNode)NodeOrError, NewPos);
            return true;
        }

        success = default;
        return false;
    }

    public readonly string? GetErrorOrDefault() => NewPos >= 0 ? null : (string)NodeOrError;

    public readonly string GetError() => NewPos >= 0 ? throw new InvalidCastException("Result is Success") : (string)NodeOrError;

    public readonly bool TryGetFailed([MaybeNullWhen(false)] out string error)
    {
        if (NewPos < 0)
        {
            error = (string)NodeOrError;
            return true;
        }

        error = null;
        return false;
    }

#pragma warning disable CS0618 // Type or member is obsolete
    public override string ToString() => Parser.Input == null
        ? NewPos < 0 ? $"Failure: {NodeOrError}" : $"Success(NewPos={NewPos}, {NodeOrError})"
        : ToString(Parser.Input);
#pragma warning disable CS0618 // Type or member is obsolete

    public string ToString(string input)
    {
        return NewPos < 0 ? $"Failure: {NodeOrError}" : success((Node)NodeOrError);
        string success(Node node) => $"Success([{node.StartPos}-{node.EndPos}), {node.Debug()})";
    }

    public static Result Success(ISyntaxNode result, int newPos) => new(result, newPos);
    public static Result Failure(string error) => new(error, -1);

    private Result(object nodeOrError, int newPos)
    {
        NodeOrError = nodeOrError.AssertIsNonNull();
        NewPos = newPos;
    }

    private sealed class DebugView(Result result)
    {
        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public Tree[] Elements => Parser.Input == null || !result.IsSuccess
            ? []
            : new Tree(Parser.Input, ((ISyntaxNode)result.NodeOrError) ).Elements;
    }
}