using Estate.Kernel;
using Xunit.Sdk;

namespace Estate.Tests;

/// <summary>
/// A Result's value or its error, as a test states which it expects; the failure message names the other, so a test reads the
/// value it asked for and never unwraps a result by hand. Linked into the three test projects from tests/Shared/.
/// </summary>
internal static class Expect
{
    /// <summary>The value; fails naming the error's code and message.</summary>
    public static T Value<T>(Result<T> result) =>
        result.Match(value => value, error => throw new XunitException("failed where a value was due: " + error.Code + ": " + error.Message));

    /// <summary>The error; fails naming the value.</summary>
    public static Error Failed<T>(Result<T> result) =>
        result.Match(value => throw new XunitException("succeeded where an error was due, with " + value), error => error);

    /// <summary>The error, which carries <paramref name="code"/>; fails naming the value, or the code the error carries instead.</summary>
    public static Error Failed<T>(Result<T> result, string code)
    {
        var error = Failed(result);
        return error.Code == code ? error : throw new XunitException("the error is " + error.Code + " where " + code + " was due: " + error.Message);
    }
}
