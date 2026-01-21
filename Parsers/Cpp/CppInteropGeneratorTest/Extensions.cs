namespace EnumGenerator;

internal static class Extensions
{
    internal static IEnumerable<TSource> DistinctBy<TSource>(
        this IEnumerable<TSource> source,
        Func<TSource, string> keySelector)
    {
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in source)
            if (seenKeys.Add(keySelector(element)))
                yield return element;
    }
}
