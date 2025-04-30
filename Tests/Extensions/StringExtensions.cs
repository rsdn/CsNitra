namespace Tests.Extensions;

public static class StringExtensions
{
    public static string NormalizeEol(this string text) => text.Replace("\r\n", "\n").Replace("\r", "\n");
}
