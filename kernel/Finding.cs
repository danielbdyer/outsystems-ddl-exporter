using System;

namespace DbChange.Kernel;

/// <summary>
/// How much a finding matters, closed: an error stops the verb and carries a remedy; a warning names something a person should
/// read, with a remedy when one exists; a note records a fact the answer stands on, such as an unpinned toolchain. The envelope
/// writes the three as error, warning and note.
/// </summary>
public enum Severity
{
    Error,
    Warning,
    Note,
}

/// <summary>
/// One thing an answer says about one object: a <see cref="Code"/> in the form of an error code (its category need not be an error
/// category: doctor, drift, diff, schema and run are findings' alone), a <see cref="Severity"/>, the <see cref="Subject"/> it is about,
/// the <see cref="Message"/>, and a <see cref="Remedy"/>, which a finding of severity error carries by construction: the factories are
/// the only way to make one, and <see cref="Error"/> refuses a blank remedy as the kernel's Error does. <see cref="Of"/> carries an
/// error into an answer as its one finding.
/// </summary>
public sealed record Finding
{
    private Finding(string code, Severity severity, string subject, string message, string? remedy)
    {
        Code = ErrorCode.IsCode(code) ? code
            : throw new ArgumentException($"'{code}' is not a finding code: a category and a detail in lowercase words, such as drift.alter.", nameof(code));
        Severity = severity;
        Subject = Present(subject, nameof(subject));
        Message = Present(message, nameof(message));
        Remedy = remedy is null ? null : Present(remedy, nameof(remedy));
    }

    public string Code { get; }

    public Severity Severity { get; }

    /// <summary>The object the finding is about: an element key, a file, a target, or the command when nothing narrower fits.</summary>
    public string Subject { get; }

    public string Message { get; }

    /// <summary>What to do about it: present on every error, on a warning when one exists, and on no note.</summary>
    public string? Remedy { get; }

    /// <summary>A finding of severity error, with the remedy every error carries.</summary>
    public static Finding Error(string code, string subject, string message, string remedy) =>
        new(code, Severity.Error, subject, message, Present(remedy, nameof(remedy)));

    public static Finding Warning(string code, string subject, string message, string? remedy = null) => new(code, Severity.Warning, subject, message, remedy);

    public static Finding Note(string code, string subject, string message) => new(code, Severity.Note, subject, message, null);

    /// <summary>An error as a finding: its code, message and remedy, at severity error, about <paramref name="subject"/>.</summary>
    public static Finding Of(Kernel.Error error, string subject) => new(error.Code, Severity.Error, subject, error.Message, error.Remedy);

    private static string Present(string? text, string name) =>
        string.IsNullOrWhiteSpace(text) ? throw new ArgumentException($"A finding needs a {name}.", name) : text;
}
