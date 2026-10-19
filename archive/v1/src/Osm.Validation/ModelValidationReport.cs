using System.Collections.Immutable;
using System.Linq;

namespace Osm.Validation;

public sealed record ModelValidationReport(ImmutableArray<ModelValidationMessage> Messages)
{
    public static ModelValidationReport Empty { get; } = new(ImmutableArray<ModelValidationMessage>.Empty);

    public bool IsValid => Messages.IsDefaultOrEmpty || Messages.All(m => m.Severity != ValidationSeverity.Error);
}

public sealed record ModelValidationMessage(string Code, string Message, string Path, ValidationSeverity Severity)
{
    public static ModelValidationMessage Error(string code, string message, string path)
        => new(code, message, path, ValidationSeverity.Error);

    public static ModelValidationMessage Warning(string code, string message, string path)
        => new(code, message, path, ValidationSeverity.Warning);
}

public enum ValidationSeverity
{
    Error = 0,
    Warning = 1
}
