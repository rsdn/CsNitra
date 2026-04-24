using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace ExtensibleParser;

[DebuggerTypeProxy(typeof(DebugView))]
public readonly record struct Result
{
    public enum Kind { Success, Partial, Failure }

    public readonly Kind ResultKind;
    public readonly int NewPos;
    public readonly int MaxFailPos;
    internal readonly ISyntaxNode? Node;

    public bool IsSuccess => ResultKind == Kind.Success;

    public readonly bool TryGetSuccess([MaybeNullWhen(false)] out ISyntaxNode node, out int newPos)
    {
        if (ResultKind == Kind.Success)
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
        if (ResultKind == Kind.Success)
        {
            success = (Node!, NewPos);
            return true;
        }

        success = default;
        return false;
    }

    public readonly bool TryGetPartial([MaybeNullWhen(false)] out ISyntaxNode node, out int newPos)
    {
        if (ResultKind == Kind.Partial)
        {
            node = Node!;
            newPos = NewPos;
            return true;
        }

        node = null;
        newPos = -1;
        return false;
    }

    public readonly string? GetErrorOrDefault() => ResultKind == Kind.Success ? null : "Error";

    public readonly string GetError() => ResultKind == Kind.Success ? throw new InvalidCastException("Result is Success") : "Error";

    public readonly bool TryGetFailed([MaybeNullWhen(false)] out string error)
    {
        if (ResultKind == Kind.Failure)
        {
            error = "Error";
            return true;
        }

        error = null;
        return false;
    }

    public Result WithPrefixOnly(Result result) => new(result.ResultKind, result.Node, result.NewPos, result.MaxFailPos);

    public static Result Success(ISyntaxNode result, int newPos, int maxFailPos) => new(Kind.Success, result, newPos, maxFailPos);
    public static Result Failure(int failPos) => new(Kind.Failure, null, newPos: -1, maxFailPos: failPos);
    public static Result Partial(ISyntaxNode partialTree, int parsedUpTo, int maxFailPos) => new(Kind.Partial, partialTree, parsedUpTo, maxFailPos);

    private Result(Kind kind, ISyntaxNode? node, int newPos, int maxFailPos)
    {
        ResultKind = kind;
        Node = node;
        NewPos = newPos;
        MaxFailPos = maxFailPos;
    }

#pragma warning disable CS0618 // Type or member is obsolete
    public override string ToString()
    {
        if (Parser.Input == null)
        {
            if (ResultKind == Kind.Failure)
                return "Failure(" + ~NewPos + ")";
            return "Success(NewPos=" + NewPos + ", " + Node + ")";
        }

        var node = (Node)Node!;
        if (ResultKind == Kind.Failure)
            return "Failure(" + ~NewPos + ")";
        return "Success([" + node.StartPos + "-" + node.EndPos + "), " + node.Debug() + ")";
    }
#pragma warning restore CS0618 // Type or member is obsolete

    private sealed class DebugView(Result result)
    {
        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public object Elements => Parser.Input == null || result.ResultKind != Kind.Success
            ? new Tree[0]
            : new Tree(Parser.Input, result.Node!).Elements;
    }
}
