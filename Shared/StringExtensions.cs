namespace Tests.Extensions;

internal static class StringExtensions
{
    public static string NormalizeEol(this string text) => text.Replace("\r\n", "\n").Replace("\r", "\n");
}
