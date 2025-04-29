using System.Diagnostics;

namespace Regex;

public static class Dot
{
    public static void GenerateSvg(string dotFilePath, string outputSvgPath)
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = "dot.exe",
            Arguments = $"-Tsvg \"{dotFilePath}\" -o \"{outputSvgPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(processStartInfo) ?? throw new InvalidOperationException("Can't start dot.exe");
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"Graphviz failed: {error}");
        }
    }

    public static string EscapeLabel(string input) => input
        .Replace("\\", "\\\\")
        .Replace("\"", "\\\"")
        .Replace("\n", "\\n")
        .Replace("\r", "\\r");
}
