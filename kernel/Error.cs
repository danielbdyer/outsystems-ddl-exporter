using System;
using System.Text.RegularExpressions;

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
    /// <summary>
    /// The form of every code, as a regular expression that .NET and JSON Schema's ECMA-262 dialect read alike: two or more words
    /// joined by dots, each word runs of lowercase ASCII letters and digits joined by single hyphens, such as <c>name.too-long</c>
    /// or <c>scratch-server.missing</c>. The first word is the code's category. The constructor applies it, and cli/Render.cs writes
    /// it into every schema as a finding's code. It ends in <c>(?![\s\S])</c>, the end of the text in both dialects, where .NET's
    /// <c>$</c> would also admit a final line break.
    /// </summary>
    public const string CodePattern = @"^[a-z0-9]+(-[a-z0-9]+)*(\.[a-z0-9]+(-[a-z0-9]+)*)+(?![\s\S])";

    private static readonly Regex CodeForm = new(CodePattern, RegexOptions.CultureInvariant);

    public Error(string code, string message, string remedy)
    {
        Code = code is not null && CodeForm.IsMatch(code)
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

    private static string Present(string? text, string name) =>
        string.IsNullOrWhiteSpace(text) ? throw new ArgumentException($"An error needs a {name}.", name) : text;
}
