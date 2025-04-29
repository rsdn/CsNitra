#nullable enable

using System.Runtime.CompilerServices;

namespace ExtensibleParaser;

public static class Guard
{
    public static T AssertIsNonNull<T>(this T? vale, [CallerArgumentExpression(nameof(vale))] string? expr = null)
        => vale ?? throw new ArgumentNullException(expr);

    public static void IsTrue(this bool vale, [CallerArgumentExpression(nameof(vale))] string? expr = null)
    {
        if (!vale)
            throw new InvalidOperationException($"Assertion failed. Expression [{expr}] bust be true!");
    }
}
