using System;
using System.Text.RegularExpressions;

namespace Estate.Kernel;

/// <summary>
/// What the engine could not do or will not do, said so that a person can act on it: a <see cref="Code"/> that records cite
/// and tests assert (a category and a detail in lowercase words, such as <c>name.too-long</c>; the category chooses the exit), a
/// <see cref="Message"/> saying what went wrong, and a <see cref="Remedy"/> saying what to do instead. An error without a
/// remedy cannot be constructed: the constructor throws on a blank one, and <c>with</c> reaches no property. Its category is a
/// member of the closed <see cref="ErrorCategory"/>, so a code whose first word names no member cannot be constructed either. It is
/// a class rather than a struct because a struct's default would be exactly that error.
/// </summary>
public sealed record Error
{
    private static readonly Regex CodeForm = new(ErrorCode.Pattern, RegexOptions.CultureInvariant);

    public Error(string code, string message, string remedy)
    {
        Code = code is not null && CodeForm.IsMatch(code)
            ? code
            : throw new ArgumentException(
                $"'{code}' is not an error code: a category and a detail in lowercase words, such as name.too-long.",
                nameof(code));
        var word = code[..code.IndexOf('.', StringComparison.Ordinal)];
        Category = ErrorCode.Parse(word) ?? throw new ArgumentException(
            $"'{code}' names the category '{word}', which ErrorCategory does not hold; add the member and its exit in cli/Contract.cs, or use a category it holds.",
            nameof(code));
        Message = Present(message, nameof(message));
        Remedy = Present(remedy, nameof(remedy));
    }

    public string Code { get; }

    /// <summary>The code's first word, as a member of the closed set: what cli/Contract.cs maps to the exit.</summary>
    public ErrorCategory Category { get; }

    public string Message { get; }

    public string Remedy { get; }

    private static string Present(string? text, string name) =>
        string.IsNullOrWhiteSpace(text) ? throw new ArgumentException($"An error needs a {name}.", name) : text;
}

/// <summary>
/// The category of an error: the word before the first dot of its code, one member per category the kernel, io or cli constructs
/// an error of, and no other. The set is closed so that cli/Contract.cs maps each member to its exit in one switch with no discard
/// arm: a member added here without an arm there fails the build (CS8509 under warnings as errors), and an error whose code names
/// a word outside the set cannot be constructed.
/// </summary>
public enum ErrorCategory
{
    AggregateQuery,
    Arguments,
    Build,
    Change,
    Connection,
    Copy,
    DacFx,
    Element,
    File,
    Fingerprint,
    Git,
    GitBranch,
    Internal,
    Lock,
    Model,
    Name,
    Origin,
    Package,
    Plan,
    Posture,
    Profile,
    Ref,
    Refactorlog,
    Reference,
    Registry,
    LocalServer,
    Sdk,
    Server,
    Sqlcmd,
    SyntheticCopy,
    Target,
    Tool,
    Toolchain,
    Verb,
}

/// <summary>The form of an error code, and each category as a code writes it: the one grammar the kernel applies and cli/Render.cs writes into every schema as a finding's code.</summary>
public static class ErrorCode
{
    /// <summary>
    /// The form of every code, as a regular expression that .NET and JSON Schema's ECMA-262 dialect read alike: two or more words
    /// joined by dots, each word runs of lowercase ASCII letters and digits joined by single hyphens, such as <c>name.too-long</c>
    /// or <c>local-server.missing</c>. The first word is the code's category. It ends in <c>(?![\s\S])</c>, the end of the text in
    /// both dialects, where .NET's <c>$</c> would also admit a final line break.
    /// </summary>
    public const string Pattern = @"^[a-z0-9]+(-[a-z0-9]+)*(\.[a-z0-9]+(-[a-z0-9]+)*)+(?![\s\S])";

    /// <summary>A category as a code writes it: lowercase, a hyphen between the words of a two-word category (local-server, git-branch).</summary>
    // CS8524 (an enum value no member names) is disabled for this switch alone; CS8509, a named member without an arm, stays an error.
#pragma warning disable CS8524
    public static string Text(ErrorCategory category) => category switch
    {
        ErrorCategory.AggregateQuery => "aggregate-query",
        ErrorCategory.Arguments => "arguments",
        ErrorCategory.Build => "build",
        ErrorCategory.Change => "change",
        ErrorCategory.Connection => "connection",
        ErrorCategory.Copy => "copy",
        ErrorCategory.DacFx => "dacfx",
        ErrorCategory.Element => "element",
        ErrorCategory.File => "file",
        ErrorCategory.Fingerprint => "fingerprint",
        ErrorCategory.Git => "git",
        ErrorCategory.GitBranch => "git-branch",
        ErrorCategory.Internal => "internal",
        ErrorCategory.Lock => "lock",
        ErrorCategory.Model => "model",
        ErrorCategory.Name => "name",
        ErrorCategory.Origin => "origin",
        ErrorCategory.Package => "package",
        ErrorCategory.Plan => "plan",
        ErrorCategory.Posture => "posture",
        ErrorCategory.Profile => "profile",
        ErrorCategory.Ref => "ref",
        ErrorCategory.Refactorlog => "refactorlog",
        ErrorCategory.Reference => "reference",
        ErrorCategory.Registry => "registry",
        ErrorCategory.LocalServer => "local-server",
        ErrorCategory.Sdk => "sdk",
        ErrorCategory.Server => "server",
        ErrorCategory.Sqlcmd => "sqlcmd",
        ErrorCategory.SyntheticCopy => "synthetic-copy",
        ErrorCategory.Target => "target",
        ErrorCategory.Tool => "tool",
        ErrorCategory.Toolchain => "toolchain",
        ErrorCategory.Verb => "verb",
#pragma warning restore CS8524
    };

    /// <summary>The member a code's first word names, or null when no member writes as that word.</summary>
    public static ErrorCategory? Parse(string word)
    {
        foreach (var category in Enum.GetValues<ErrorCategory>())
        {
            if (Text(category) == word)
            {
                return category;
            }
        }

        return null;
    }
}
