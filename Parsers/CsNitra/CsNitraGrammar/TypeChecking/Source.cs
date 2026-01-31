namespace CsNitra.TypeChecking;

// Abstract SourceText
public abstract class Source
{
    public abstract string Text { get; }
}

public sealed class SourceText(string text, string filePath) : Source
{
    public override string Text => text;
    public string FilePath => filePath;
}

public sealed partial record SourceSpan(int Start, int End)
{
    public int Length => End - Start;
}
