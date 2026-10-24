namespace Projection.Pipeline

open Microsoft.Data.SqlClient

/// The closed vocabulary of SINK CAPABILITIES whose refusal the reverse leg
/// descends on (recon #4). The capability-descent doctrine — "descend only on the
/// NAMED capability error; every descent on the report" — was honored at two
/// reverse-leg sites by hand, each with its own SQL-error-number predicate that the
/// code itself flagged as siblings (`TransferRun.isAlterCapabilityRefusal` "mirrors
/// `SurrogateCapture.isCapabilityRefusal`"). The `(SqlErrorNumber → Capability)`
/// registry lives here ONCE and names each capability; a data error (a constraint
/// conflict, a conversion, a deadlock) is NOT a capability refusal — it maps to
/// `None` and PROPAGATES (degrading on it would mask corruption).
///
/// (The two descent SHAPES stay their own — `SurrogateCapture` is a multi-rung
/// lane LADDER, `TransferRun.restoreFkTrust` is a single attempt-or-skip — as do
/// their report records: a `LaneDescent` carries from/to rungs, the FK-trust skip
/// names a `ToleratedDivergence`. Only the RECOGNITION is shared here.)
[<RequireQualifiedAccess>]
type Capability =
    /// SQL 334 — `OUTPUT` without `INTO` is refused on a target carrying enabled
    /// triggers. The surrogate-capture ladder descends to the trigger-proof
    /// `OUTPUT … INTO` rung, then the rowwise `SCOPE_IDENTITY()` floor.
    | OutputWithoutIntoOnTriggeredTarget
    /// SQL 1088 / 4902 / 229 — `ALTER TABLE … {WITH|NO}CHECK CONSTRAINT` is refused
    /// (object visibility / ALTER permission). The bulk-load FK-trust restore is
    /// skipped to the named `ToleratedDivergence.FkTrustNotRestoredOnBulkLoad`.
    | AlterConstraintTrust

[<RequireQualifiedAccess>]
module CapabilityRefusal =

    /// The closed registry — which SQL error numbers name which sink capability.
    /// Total: an unlisted number is a data error (`None`), not a capability refusal.
    let ofErrorNumber (errorNumber: int) : Capability option =
        match errorNumber with
        | 334               -> Some Capability.OutputWithoutIntoOnTriggeredTarget
        | 1088 | 4902 | 229 -> Some Capability.AlterConstraintTrust
        | _                 -> None

    /// True iff the SQL exception names a refusal of THIS capability — the shared
    /// recognizer the two reverse-leg descent sites route through (replacing their
    /// hand-rolled `ex.Number = …` predicates).
    let isRefusal (capability: Capability) (ex: SqlException) : bool =
        ofErrorNumber ex.Number = Some capability
