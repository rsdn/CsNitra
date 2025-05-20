using System.Diagnostics;

namespace Regex;

public static class Dot
{
    public static string Dot2Svg(string dotType, string dotContent, string outputPath)
    {
        var fullOutputPath = Path.GetFullPath(outputPath);
        var dotFile = Path.ChangeExtension(fullOutputPath, ".dot");

        File.WriteAllText(dotFile, dotContent);
        Dot.GenerateSvg(dotFile, fullOutputPath);
        Trace.TraceInformation($"{dotType} diagram writen into: {fullOutputPath}");
        return fullOutputPath;
    }

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
        .Replace(@"\", @"\\")
        .Replace("\"", "\\\"")
        .Replace("\n", @"\n")
        .Replace("$", @"\$")
        .Replace("\r", "\\r");
}
