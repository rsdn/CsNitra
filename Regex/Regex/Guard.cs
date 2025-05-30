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

    public static void AreEqual<T>(T? expected, T? actual, [CallerArgumentExpression(nameof(actual))] string? actualExpr = null)
    {
        if (!Equals(expected, actual))
        {
            throw new InvalidOperationException($"Assertion failed for: «{actualExpr}». Expected «{expected}» Actual: «{actual}».");
        }
    }

    public static void Fail(string message) => throw new InvalidOperationException(message);
}
