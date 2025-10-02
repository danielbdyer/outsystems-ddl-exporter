using System.Collections.Immutable;

namespace Osm.Validation.Tightening;

public enum TighteningDiagnosticSeverity
{
    Info = 0,
    Warning = 1
}

public sealed record TighteningDuplicateCandidate(string Module, string Schema, string PhysicalName);

public sealed record TighteningDiagnostic(
    string Code,
    string Message,
    TighteningDiagnosticSeverity Severity,
    string LogicalName,
    string CanonicalModule,
    string CanonicalSchema,
    string CanonicalPhysicalName,
    ImmutableArray<TighteningDuplicateCandidate> Candidates,
    bool ResolvedByOverride);
