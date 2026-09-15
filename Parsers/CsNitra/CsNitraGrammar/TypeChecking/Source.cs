using System.Text;

namespace CsNitra.TypeChecking;

// Abstract SourceText
public abstract class Source
{
    public abstract string Text { get; }
    public virtual string FormatPosition(int position) => position.ToString();
}

public sealed class SourceText(string text, string filePath) : Source
{
    public override string Text => text;
    public string FilePath => filePath;

    public override string FormatPosition(int position)
    {
        var line = 1;
        var column = 1;

        for (int j = 0; j < position && j < text.Length; j++)
            if (text[j] == '\n')
                line++;
            else
                column++;

        return $"{filePath}:{line}:{column}";
    }
}

public sealed class MultiFileSource : Source
{
    private sealed record Segment(string Path, string Text, int Offset);

    private readonly IReadOnlyList<Segment> _segments;

    public override string Text { get; }

    public MultiFileSource(IReadOnlyList<(string Text, string Path)> files)
    {
        if (files.Count == 0)
            throw new ArgumentException("At least one grammar file is required", nameof(files));

        var builder = new StringBuilder();
        var segments = new List<Segment>(files.Count);

        foreach (var (text, path) in files)
        {
            segments.Add(new Segment(path, text, builder.Length));
            builder.Append(text);
            if (text.Length == 0 || text[^1] is not ('\n' or '\r'))
                builder.Append('\n');
        }

        _segments = segments;
        Text = builder.ToString();
    }

    public (string Path, int Position) Resolve(int position)
    {
        for (int i = _segments.Count - 1; i >= 0; i--)
        {
            if (_segments[i].Offset <= position)
                return (_segments[i].Path, position - _segments[i].Offset);
        }

        return (_segments[0].Path, 0);
    }

    public override string FormatPosition(int position)
    {
        for (int i = _segments.Count - 1; i >= 0; i--)
        {
            var segment = _segments[i];
            if (segment.Offset <= position)
            {
                var local = position - segment.Offset;
                var line = 1;
                var column = 1;

                for (int j = 0; j < local && j < segment.Text.Length; j++)
                    if (segment.Text[j] == '\n')
                        line++;
                    else
                        column++;

                return $"{segment.Path}:{line}:{column}";
            }
        }

        return $"{_segments[0].Path}:1:1";
    }
}

public sealed partial record SourceSpan(int Start, int End)
{
    public int Length => End - Start;
}
