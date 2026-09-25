using System;
using System.Linq;

namespace Estate.Kernel;

/// <summary>
/// What the engine could not do or will not do, said so that a person can act on it: a <see cref="Code"/> that records cite
/// and tests assert (a category and a detail in lowercase words, such as <c>name.too-long</c>; the category chooses the exit), a
/// <see cref="Message"/> saying what went wrong, and a <see cref="Remedy"/> saying what to do instead. An error without a
/// remedy cannot be constructed: the constructor throws on a blank one, and <c>with</c> reaches no property. It is a class
/// rather than a struct because a struct's default would be exactly that error.
/// </summary>
public sealed record Error
{
    public Error(string code, string message, string remedy)
    {
        Code = IsCode(code)
            ? code
            : throw new ArgumentException(
                $"'{code}' is not an error code: a category and a detail in lowercase words, such as name.too-long.",
                nameof(code));
        Message = Present(message, nameof(message));
        Remedy = Present(remedy, nameof(remedy));
    }

    public string Code { get; }

    public string Message { get; }

    public string Remedy { get; }

    // Two or more dot-separated words; a word is lowercase ASCII letters and digits, hyphen-joined.
    private static bool IsCode(string? code) =>
        code?.Split('.') is { Length: >= 2 } words
        && words.All(word => word.Split('-').All(
            piece => piece.Length > 0 && piece.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c))));

    private static string Present(string? text, string name) =>
        string.IsNullOrWhiteSpace(text) ? throw new ArgumentException($"An error needs a {name}.", name) : text;
}
