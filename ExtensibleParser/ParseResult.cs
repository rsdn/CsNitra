namespace ExtensibleParaser;

public abstract record ParseResult
{
    public abstract bool IsSuccess { get; }
    public bool IsFailed => !IsSuccess;
}

public sealed record Success<T>(T Program) : ParseResult
{
    public override bool IsSuccess => true;
}

public sealed record Failed(ExtensibleParaser.FatalError ErrorInfo) : ParseResult
{
    public override bool IsSuccess => false;
}
