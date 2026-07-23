namespace Projection.Core

/// The **durable value vocabulary** for approved inline data corrections — the
/// part that must sit ahead of `Episode.fs` in the Core compile order so an
/// `Episode` can carry a `DataCorrectionReceipt` provenance plane beside its
/// `Tolerances` and `AppliedTransforms`. The config-authored `ApprovedDataCorrection`
/// and the pure correction ENGINE live later (`ApprovedDataCorrections.fs`, after
/// `SliceSpec.fs`), because they lean on `SliceSpec.Predicate` (a NULL-aware row
/// predicate defined further down the chain).
///
/// This is the PUBLISH-side sibling of the SYNTHETIC-side `Correction`
/// (`SyntheticCorrection.fs`, the σ override layer): both are "named, blessed
/// divergences," but a synthetic correction reshapes generated data (`π ∘ σ`),
/// whereas an approved data correction transforms ACQUIRED source rows in flight
/// before emission/load — and, unlike a `Tolerance` (a passive, symmetric
/// representation-equivalence the canary accepts), a data correction is an
/// ACTIVE, asymmetric row change with provenance: who changed what, why, and how
/// many rows. Receipts are that provenance; they are NEVER tolerances.

/// The row derivation an approved correction applies — the "how the value or row
/// membership is produced" axis. Closed; grows under evidence.
[<RequireQualifiedAccess>]
type DataCorrectionDerivation =
    /// Populate the subject cell from another attribute on the SAME row.
    | SameRowAttribute
    /// Populate the subject cell from a PARENT row's attribute, joined through a
    /// declared relationship — the ONE cross-kind derivation (it needs another
    /// kind's rows, so it forces the two-phase publish schedule).
    | ParentAttribute
    /// Populate the subject cell with a typed configured literal (a sentinel
    /// user / timestamp / any constant).
    | ConstantLiteral
    /// Remove matching rows from the emitted/loaded row set.
    | ExcludeRows

[<RequireQualifiedAccess>]
module DataCorrectionDerivation =

    let name (d: DataCorrectionDerivation) : string =
        match d with
        | DataCorrectionDerivation.SameRowAttribute -> "sameRowAttribute"
        | DataCorrectionDerivation.ParentAttribute  -> "parentAttribute"
        | DataCorrectionDerivation.ConstantLiteral  -> "constantLiteral"
        | DataCorrectionDerivation.ExcludeRows      -> "excludeRows"

    let parse (s: string) : Result<DataCorrectionDerivation> =
        match s with
        | "sameRowAttribute" -> Result.success DataCorrectionDerivation.SameRowAttribute
        | "parentAttribute"  -> Result.success DataCorrectionDerivation.ParentAttribute
        | "constantLiteral"  -> Result.success DataCorrectionDerivation.ConstantLiteral
        | "excludeRows"      -> Result.success DataCorrectionDerivation.ExcludeRows
        | other ->
            Result.failureOf
                (ValidationError.create "dataCorrection.derivation.unknown"
                    (String.concat "" [ "unknown data-correction derivation '"; other; "'" ]))

    /// Whether this derivation needs another kind's rows (a parent/lookup map).
    /// The pipelined publish path drains and drops rows per-kind and never
    /// materializes a cross-kind map, so a cross-kind correction names a
    /// deliberate two-phase fallback rather than a silent miss. Only
    /// `ParentAttribute` is cross-kind today.
    let isCrossKind (d: DataCorrectionDerivation) : bool =
        match d with
        | DataCorrectionDerivation.ParentAttribute -> true
        | DataCorrectionDerivation.SameRowAttribute
        | DataCorrectionDerivation.ConstantLiteral
        | DataCorrectionDerivation.ExcludeRows -> false

/// A guard on an approved correction. The guards fall into TWO classes with
/// DIFFERENT semantics — read this before configuring:
///
/// * **Row-selector guards** NARROW the change set: `TargetIsNull`,
///   `SourceIsNotNull`, `ParentExists`, `ParentSourceIsNotNull`,
///   `SourceReferencesExistingTarget`. A matched row that fails a selector is
///   left UNCHANGED (partial coverage), not a refusal — this is the "only where
///   safe" intent ("copy only where the target is null AND the value resolves").
///   `RowsMatched` counts the predicate matches; `RowsChanged` counts the subset
///   that also passed every selector.
///
/// * **Assertion guards** are whole-set invariants that REFUSE the correction by
///   name (fail-closed) when violated: `SentinelExists`,
///   `NoFormalInboundReferences`, `NoConfiguredReferenceMatches`,
///   `ExpectedFindingCount`, and — the promoter — `ExpectedCoverage`.
///   **`ExpectedCoverage` turns the selector narrowing into a fail-closed
///   totality requirement**: it refuses unless `RowsChanged = RowsMatched`, i.e.
///   EVERY matched row passed every selector. Configure `ExpectedCoverage` on any
///   correction that must be all-or-nothing rather than best-effort; without it,
///   selectors are explicitly partial coverage.
[<RequireQualifiedAccess>]
type DataCorrectionGuard =
    /// The subject cell is NULL on the row (never overwrite a present value).
    | TargetIsNull
    /// The same-row source cell is non-NULL (there is a value to copy).
    | SourceIsNotNull
    /// The joined parent row exists.
    | ParentExists
    /// The joined parent's source cell is non-NULL.
    | ParentSourceIsNotNull
    /// The configured constant (sentinel) resolves to an existing row in the
    /// referenced target table (e.g. the sentinel user exists).
    | SentinelExists
    /// The copied source value exists in the referenced target key set.
    | SourceReferencesExistingTarget
    /// No RETAINED row formally references (inbound FK) the rows about to be
    /// excluded — a formal safety probe for `ExcludeRows`.
    | NoFormalInboundReferences
    /// No configured extra reference probe matches the rows about to be excluded
    /// — the operator-authored complement to the formal probe.
    | NoConfiguredReferenceMatches
    /// The matched-row count equals the operator's expected finding count
    /// (guards against evidence drift since the proposal was approved).
    | ExpectedFindingCount
    /// The correction's coverage over the affected population is complete (every
    /// affected row carries the evidence the derivation needs).
    | ExpectedCoverage

[<RequireQualifiedAccess>]
module DataCorrectionGuard =

    let name (g: DataCorrectionGuard) : string =
        match g with
        | DataCorrectionGuard.TargetIsNull                   -> "targetIsNull"
        | DataCorrectionGuard.SourceIsNotNull                -> "sourceIsNotNull"
        | DataCorrectionGuard.ParentExists                   -> "parentExists"
        | DataCorrectionGuard.ParentSourceIsNotNull          -> "parentSourceIsNotNull"
        | DataCorrectionGuard.SentinelExists                 -> "sentinelExists"
        | DataCorrectionGuard.SourceReferencesExistingTarget -> "sourceReferencesExistingTarget"
        | DataCorrectionGuard.NoFormalInboundReferences      -> "noFormalInboundReferences"
        | DataCorrectionGuard.NoConfiguredReferenceMatches   -> "noConfiguredReferenceMatches"
        | DataCorrectionGuard.ExpectedFindingCount           -> "expectedFindingCount"
        | DataCorrectionGuard.ExpectedCoverage               -> "expectedCoverage"

    let parse (s: string) : Result<DataCorrectionGuard> =
        match s with
        | "targetIsNull"                   -> Result.success DataCorrectionGuard.TargetIsNull
        | "sourceIsNotNull"                -> Result.success DataCorrectionGuard.SourceIsNotNull
        | "parentExists"                   -> Result.success DataCorrectionGuard.ParentExists
        | "parentSourceIsNotNull"          -> Result.success DataCorrectionGuard.ParentSourceIsNotNull
        | "sentinelExists"                 -> Result.success DataCorrectionGuard.SentinelExists
        | "sourceReferencesExistingTarget" -> Result.success DataCorrectionGuard.SourceReferencesExistingTarget
        | "noFormalInboundReferences"      -> Result.success DataCorrectionGuard.NoFormalInboundReferences
        | "noConfiguredReferenceMatches"   -> Result.success DataCorrectionGuard.NoConfiguredReferenceMatches
        | "expectedFindingCount"           -> Result.success DataCorrectionGuard.ExpectedFindingCount
        | "expectedCoverage"               -> Result.success DataCorrectionGuard.ExpectedCoverage
        | other ->
            Result.failureOf
                (ValidationError.create "dataCorrection.guard.unknown"
                    (String.concat "" [ "unknown data-correction guard '"; other; "'" ]))

/// The recorded outcome of evaluating one guard while applying a correction. A
/// receipt only exists on a SUCCESSFUL apply, so every result on a persisted
/// receipt passed — but the observed count (`Observed`) and detail carry the
/// evidence the guard stood on, so a reviewer can audit why the correction was
/// accepted.
type DataCorrectionGuardResult =
    { Guard    : DataCorrectionGuard
      Passed   : bool
      Observed : int64 option
      Detail   : string option }

[<RequireQualifiedAccess>]
module DataCorrectionGuardResult =

    let create (guard: DataCorrectionGuard) (passed: bool) (observed: int64 option) (detail: string option) : DataCorrectionGuardResult =
        { Guard = guard; Passed = passed; Observed = observed; Detail = detail }

    let passed (guard: DataCorrectionGuard) (observed: int64 option) : DataCorrectionGuardResult =
        create guard true observed None

/// The durable, count-bearing provenance of ONE applied approved correction — a
/// first-class intervention ledger entry. For same-row / parent-derived /
/// constant derivations, `RowsChanged` counts the transformed rows; for an
/// exclusion, `RowsExcluded` counts the removed rows. `BeforeDigest` /
/// `AfterDigest` are deterministic content hashes over the affected subject
/// cells, so a row-fidelity proof can bound the replay: a receipt claiming N
/// changed rows that replays to a different N fails the proof BY NAME.
///
/// `Subject` reuses the operator-authorable `AttributeCoordinate`
/// (`(module, entity, attribute)` by name) — the same logical address the
/// synthetic-side blessed corrections use — rather than a new ref type.
type DataCorrectionReceipt =
    { CorrectionId        : string
      SourceRemediationId : string option
      Subject             : AttributeCoordinate
      Derivation          : DataCorrectionDerivation
      GuardResults        : DataCorrectionGuardResult list
      RowsMatched         : int64
      RowsChanged         : int64
      RowsExcluded        : int64
      BeforeDigest        : string option
      AfterDigest         : string option
      /// The supporting evidence columns the correction was configured with
      /// (`evidenceColumns`) — recorded so a reviewer can audit WHY the
      /// derivation was accepted (the context-recovery requirement: a derivation
      /// is accepted on the strength of the metadata columns it stands on).
      EvidenceColumns     : AttributeCoordinate list
      /// A deterministic SHA-256 digest over the evidence columns' cells on the
      /// changed rows — the audit anchor for the evidence the receipt names.
      /// `None` when no evidence columns were configured.
      EvidenceDigest      : string option
      ApprovedBy          : string option
      ApprovedAt          : string option }

[<RequireQualifiedAccess>]
module DataCorrectionReceipt =

    /// A deterministic sort key: `(CorrectionId, module, entity, attribute)`.
    /// The manifest / lifecycle artifact sorts by this so the persisted receipt
    /// stream is byte-deterministic across runs.
    let sortKey (r: DataCorrectionReceipt) : string * string * string * string =
        (r.CorrectionId, r.Subject.Module, r.Subject.Entity, r.Subject.Attribute)

    /// Order a receipt list deterministically by `sortKey`.
    let sorted (receipts: DataCorrectionReceipt list) : DataCorrectionReceipt list =
        receipts |> List.sortBy sortKey
