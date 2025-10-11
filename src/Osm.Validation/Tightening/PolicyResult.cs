using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Osm.Validation.Tightening;

public enum PolicyResultKind
{
    Decision,
    Warning,
    Error
}

public sealed class PolicyResult<T>
{
    private readonly T? _decision;

    private PolicyResult(
        PolicyResultKind kind,
        T? decision,
        ImmutableArray<PolicyWarning> warnings,
        PolicyError? error)
    {
        Kind = kind;
        _decision = decision;
        Warnings = warnings;
        Error = error;
    }

    public PolicyResultKind Kind { get; }

    public bool HasDecision => Kind == PolicyResultKind.Decision;

    public T Decision => HasDecision
        ? _decision!
        : throw new InvalidOperationException("Policy result does not contain a decision.");

    public ImmutableArray<PolicyWarning> Warnings { get; }

    public PolicyError? Error { get; }

    public static PolicyResult<T> FromDecision(T decision, IEnumerable<PolicyWarning>? warnings = null)
    {
        if (decision is null)
        {
            throw new ArgumentNullException(nameof(decision));
        }

        var warningArray = warnings is null
            ? ImmutableArray<PolicyWarning>.Empty
            : warnings.ToImmutableArray();

        return new PolicyResult<T>(PolicyResultKind.Decision, decision, warningArray, null);
    }

    public static PolicyResult<T> FromWarnings(IEnumerable<PolicyWarning> warnings)
    {
        if (warnings is null)
        {
            throw new ArgumentNullException(nameof(warnings));
        }

        var warningArray = warnings.ToImmutableArray();
        if (warningArray.IsDefaultOrEmpty)
        {
            throw new ArgumentException("At least one warning must be provided.", nameof(warnings));
        }

        return new PolicyResult<T>(PolicyResultKind.Warning, default, warningArray, null);
    }

    public static PolicyResult<T> FromError(PolicyError error)
    {
        if (error is null)
        {
            throw new ArgumentNullException(nameof(error));
        }

        return new PolicyResult<T>(PolicyResultKind.Error, default, ImmutableArray<PolicyWarning>.Empty, error);
    }
}

public sealed record PolicyWarning(string Code, string Message, ImmutableArray<PolicyEvidenceLink> Evidence)
{
    public static PolicyWarning Create(string code, string message, IEnumerable<PolicyEvidenceLink>? evidence = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Warning code must be provided.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Warning message must be provided.", nameof(message));
        }

        var evidenceArray = evidence is null
            ? ImmutableArray<PolicyEvidenceLink>.Empty
            : evidence.ToImmutableArray();

        return new PolicyWarning(code, message, evidenceArray);
    }
}

public sealed record PolicyError(string Code, string Message, ImmutableArray<PolicyEvidenceLink> Evidence)
{
    public static PolicyError Create(string code, string message, IEnumerable<PolicyEvidenceLink>? evidence = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Error code must be provided.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Error message must be provided.", nameof(message));
        }

        var evidenceArray = evidence is null
            ? ImmutableArray<PolicyEvidenceLink>.Empty
            : evidence.ToImmutableArray();

        return new PolicyError(code, message, evidenceArray);
    }
}
