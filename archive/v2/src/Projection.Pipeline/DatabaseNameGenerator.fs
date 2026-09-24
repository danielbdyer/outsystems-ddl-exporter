namespace Projection.Pipeline

open System

/// **First-class non-determinism boundary.** `DatabaseNameGenerator
/// = unit -> string` is the typed seam through which `Guid.NewGuid`
/// (or any test-pinned counter) enters Pipeline. Per
/// `DECISIONS 2026-05-09 — No-string-concatenation / no-regex
/// discipline` audit Tier 2: the leak from non-determinism into
/// the deploy boundary is reified as a parameter, not hidden in
/// a closure. Callers that need byte-determinism on database
/// names inject a counter-based generator; the default uses
/// `Guid.NewGuid` for unique-per-run isolation in shared
/// containers (the canary's de-facto requirement).
///
/// Re-exposed (type + module) under `Deploy.DatabaseNameGenerator`
/// via nested abbreviation so existing call sites are untouched.
type DatabaseNameGenerator = unit -> string

[<RequireQualifiedAccess>]
module DatabaseNameGenerator =

    /// Default `DatabaseNameGenerator` — uses `Guid.NewGuid`. The
    /// observable non-determinism is scoped to per-database
    /// names; T1 byte-determinism at the SQL emission layer is
    /// unaffected (V2's Π output is pure; only the deploy-host's
    /// per-database scoping is non-deterministic, and that's a
    /// Pipeline concern). Per-segment formatting goes through
    /// `String.Concat` rather than `sprintf`.
    /// Length of the GUID suffix used in ephemeral database
    /// names. 12 chars of N-format GUID is ~48 bits of entropy
    /// — sufficient for per-run uniqueness across concurrent
    /// canary processes; short enough to fit `Source_<suffix>`
    /// under SQL Server's 128-char identifier limit with
    /// generous prefix headroom.
    [<Literal>]
    let private GuidSuffixLength : int = 12

    let guidBased : DatabaseNameGenerator =
        // The `guidBased` binding IS the sanctioned `Guid.NewGuid`
        // site — the reified non-determinism boundary. Audit
        // Lens-2 Tier-1 discharged: Guid.NewGuid is now type-
        // visible through the seam, not hidden inside a private
        // function.
        fun () ->
            let suffix = Guid.NewGuid().ToString("N").Substring(0, GuidSuffixLength)  // LINT-ALLOW: reified non-determinism boundary
            suffix
