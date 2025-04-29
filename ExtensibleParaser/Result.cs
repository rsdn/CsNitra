using System.Diagnostics.CodeAnalysis;

namespace ExtensibleParaser;

public readonly record struct Result
{
    private readonly object NodeOrError;
    private readonly int NewPos;

    public bool IsSuccess => NewPos >= 0;
    public int Length => NewPos >= 0 ? NewPos : 0;

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

    public override string ToString() => NewPos < 0 ? $"Failure: {NodeOrError}" : $"Success(NewPos={NewPos}, {NodeOrError})";
    public string ToString(string input) => NewPos < 0 ? $"Failure: {NodeOrError}" : ((ISyntaxNode)NodeOrError).ToString(input);

    public static Result Success(ISyntaxNode result, int newPos) => new(result, newPos);
    public static Result Failure(string error) => new(error, -1);

    private Result(object nodeOrError, int newPos)
    {
        NodeOrError = nodeOrError.AssertIsNonNull();
        NewPos = newPos;
    }
}