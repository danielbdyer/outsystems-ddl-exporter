namespace Projection.Core

/// The estate finding vocabulary — the typed spine of `check estate`
/// (`CHAPTER_ESTATE_OPEN.md`; `DECISIONS 2026-07-15 — The estate chapter
/// opens`). Core-resident so every downstream projection of one finding
/// (the report, the remediation blocks, the posture overlay, the reopen
/// probes — the π-coherence family) keys on the same identity: the
/// emitters in `Projection.Targets.*` compile below `Projection.Pipeline`
/// and can only consume Core types.
///
/// The presentation contract (Appendix A of the chapter open) is held to
/// the code by totality: `EstateFindingKind` is a closed DU; `laneOf` /
/// `planeOf` are total matches (a new kind fires the exhaustiveness check
/// here, and the `finding ⇔ presentation` test walks `EstateFindingKind.all`).

/// The disposition lane a finding presents in — the operator's primary
/// grouping ("what needs a ruling, what runs itself, what waits"):
/// DECIDE → REPAIR → RELAX → WATCH, in board order.
[<RequireQualifiedAccess>]
type EstateLane =
    /// The ruling queue — findings no mechanism resolves; each ends on the
    /// ruling the operator makes.
    | Decide
    /// Prepared repairs — mechanical paths; each ends on one review lever
    /// once the remediation artifact carries its block.
    | Repair
    /// The interim posture — proposed and active relaxations, each carrying
    /// the reopen probe that retires it. (Populated from the posture wave;
    /// the lane exists from birth so the board's shape is stable.)
    | Relax
    /// Advisories — capped, impact-ranked rollups; no lever, by design.
    | Watch

/// The plane a finding lives on — the secondary axis (inside lanes and the
/// environment × plane matrix).
[<RequireQualifiedAccess>]
type EstatePlane =
    | Schema
    | Data
    | Identity
    | Operational

/// The closed set of finding kinds the estate check produces today. Grows
/// with the detector waves (each new kind lands WITH its contract row —
/// statement pattern, lane, lever — per the chapter-open appendix); the
/// closed-DU expansion check then fires every total match in this module.
[<RequireQualifiedAccess>]
type EstateFindingKind =
    /// A kind an environment carries BEYOND the target shape — deployed-ahead
    /// drift; no promotion explains it (the direction classifier's DECIDE arm).
    | SchemaPresence
    /// A kind the target declares that an environment does not yet carry —
    /// promotion lag; the ordinary publish resolves it (the direction
    /// classifier's WATCH arm, wave A3).
    | SchemaLag
    /// A kind carrying a different logical name than the target shape declares.
    | SchemaRename
    /// A kind whose attribute channel (columns) differs from the target shape.
    | SchemaAttributes
    /// A kind whose reference channel (relationships) differs from the target shape.
    | SchemaReferences
    /// A kind whose index channel differs from the target shape.
    | SchemaIndexes
    /// A kind whose own facets (modality / activity / triggers / checks)
    /// differ from the target shape.
    | SchemaFacets
    /// A relationship enforced WITH NOCHECK in an environment — the
    /// constraint exists untrusted; the re-trust is a preparable repair
    /// (S7/O3, wave A3).
    | SchemaTrust
    /// A NOT-NULL (or PK) declaration the environment's data contradicts.
    | DataNotNull
    /// A UNIQUE/PK declaration the environment's data contradicts.
    | DataUnique
    /// A relationship whose source rows reference absent target records.
    | DataOrphans
    /// Values past a column's declared length or type envelope.
    | DataOverflow
    /// A kind whose row counts diverge across environments past the
    /// asymmetry factor — verdicts drawn on the small side are advisory
    /// (D12, wave A3; no lever, by design).
    | DataAsymmetry
    /// A column distinct in every observed row of every evidenced
    /// environment — a natural-key candidate (D15, wave A3; advisory).
    | DataUniquenessCandidate
    /// A primary key consuming a named share of its declared storage
    /// ceiling (D13, wave A4; advisory until the widening is scheduled).
    | DataHeadroom
    /// A date column carrying the platform's empty-date convention
    /// (1900-01-01) — satisfied NOT NULL, empty of meaning (D8, wave A4).
    | DataDateSentinel
    /// Values a unique declaration keeps distinct that a case-insensitive
    /// collation collapses into duplicates (D6, wave A4).
    | DataCollationCollision
    /// Kinds whose identity provenance differs from the target's for the
    /// same name (synthesized against native) — renames across that pair
    /// are unstable until the identity anchors (I3, wave A4).
    | IdentitySynthesized
    /// CDC tracks a kind in some environments and not others — a cutover
    /// write feeds live consumers unevenly (O1, wave A4).
    | OperationalCdc

[<RequireQualifiedAccess>]
module EstateFindingKind =

    /// Every kind, enumerated — the walkable half of the
    /// `finding ⇔ presentation` totality (the DU itself is the closed half;
    /// a variant added without a row here fails the coverage test).
    let all : EstateFindingKind list =
        [ EstateFindingKind.SchemaPresence
          EstateFindingKind.SchemaLag
          EstateFindingKind.SchemaRename
          EstateFindingKind.SchemaAttributes
          EstateFindingKind.SchemaReferences
          EstateFindingKind.SchemaIndexes
          EstateFindingKind.SchemaFacets
          EstateFindingKind.SchemaTrust
          EstateFindingKind.DataNotNull
          EstateFindingKind.DataUnique
          EstateFindingKind.DataOrphans
          EstateFindingKind.DataOverflow
          EstateFindingKind.DataAsymmetry
          EstateFindingKind.DataUniquenessCandidate
          EstateFindingKind.DataHeadroom
          EstateFindingKind.DataDateSentinel
          EstateFindingKind.DataCollationCollision
          EstateFindingKind.IdentitySynthesized
          EstateFindingKind.OperationalCdc ]

    /// The stable machine token (the `FindingKey` prefix and the
    /// `estate.json` discriminator). Never operator-facing on its own.
    let token (kind: EstateFindingKind) : string =
        match kind with
        | EstateFindingKind.SchemaPresence           -> "schema.presence"
        | EstateFindingKind.SchemaLag                -> "schema.lag"
        | EstateFindingKind.SchemaRename             -> "schema.rename"
        | EstateFindingKind.SchemaAttributes         -> "schema.attributes"
        | EstateFindingKind.SchemaReferences         -> "schema.references"
        | EstateFindingKind.SchemaIndexes            -> "schema.indexes"
        | EstateFindingKind.SchemaFacets             -> "schema.facets"
        | EstateFindingKind.SchemaTrust              -> "schema.trust"
        | EstateFindingKind.DataNotNull              -> "data.notNull"
        | EstateFindingKind.DataUnique               -> "data.unique"
        | EstateFindingKind.DataOrphans              -> "data.orphans"
        | EstateFindingKind.DataOverflow             -> "data.overflow"
        | EstateFindingKind.DataAsymmetry            -> "data.asymmetry"
        | EstateFindingKind.DataUniquenessCandidate  -> "data.uniquenessCandidate"
        | EstateFindingKind.DataHeadroom             -> "data.headroom"
        | EstateFindingKind.DataDateSentinel         -> "data.dateSentinel"
        | EstateFindingKind.DataCollationCollision   -> "data.collation"
        | EstateFindingKind.IdentitySynthesized      -> "identity.synthesized"
        | EstateFindingKind.OperationalCdc           -> "operational.cdc"

    /// The disposition lane a kind presents in (Appendix A). The direction
    /// classifier (wave A3) splits presence: a kind an environment carries
    /// BEYOND the target is deployed-ahead drift (DECIDE); a kind the target
    /// declares that an environment has not yet received is promotion lag
    /// (WATCH — the ordinary publish resolves it). The untrusted-constraint
    /// census is a preparable repair; the asymmetry and candidacy advisories
    /// are watchable by design (no lever).
    let laneOf (kind: EstateFindingKind) : EstateLane =
        match kind with
        | EstateFindingKind.SchemaPresence
        | EstateFindingKind.SchemaRename
        | EstateFindingKind.SchemaAttributes
        | EstateFindingKind.SchemaReferences
        | EstateFindingKind.SchemaIndexes
        | EstateFindingKind.SchemaFacets            -> EstateLane.Decide
        | EstateFindingKind.SchemaLag               -> EstateLane.Watch
        | EstateFindingKind.SchemaTrust             -> EstateLane.Repair
        | EstateFindingKind.DataNotNull
        | EstateFindingKind.DataUnique
        | EstateFindingKind.DataOrphans
        | EstateFindingKind.DataOverflow            -> EstateLane.Repair
        | EstateFindingKind.DataAsymmetry
        | EstateFindingKind.DataUniquenessCandidate
        | EstateFindingKind.DataHeadroom
        | EstateFindingKind.DataDateSentinel
        | EstateFindingKind.IdentitySynthesized     -> EstateLane.Watch
        | EstateFindingKind.DataCollationCollision  -> EstateLane.Repair
        | EstateFindingKind.OperationalCdc          -> EstateLane.Decide

    /// The plane a kind lives on.
    let planeOf (kind: EstateFindingKind) : EstatePlane =
        match kind with
        | EstateFindingKind.SchemaPresence
        | EstateFindingKind.SchemaLag
        | EstateFindingKind.SchemaRename
        | EstateFindingKind.SchemaAttributes
        | EstateFindingKind.SchemaReferences
        | EstateFindingKind.SchemaIndexes
        | EstateFindingKind.SchemaFacets
        | EstateFindingKind.SchemaTrust             -> EstatePlane.Schema
        | EstateFindingKind.DataNotNull
        | EstateFindingKind.DataUnique
        | EstateFindingKind.DataOrphans
        | EstateFindingKind.DataOverflow
        | EstateFindingKind.DataAsymmetry
        | EstateFindingKind.DataUniquenessCandidate
        | EstateFindingKind.DataHeadroom
        | EstateFindingKind.DataDateSentinel
        | EstateFindingKind.DataCollationCollision  -> EstatePlane.Data
        | EstateFindingKind.IdentitySynthesized     -> EstatePlane.Identity
        | EstateFindingKind.OperationalCdc          -> EstatePlane.Operational

/// The stable cross-artifact identity of one finding — the board, the
/// burndown, the remediation block IDs, the overlay entries, and the reopen
/// probes all say this key and mean one thing across runs. Composed of the
/// kind's machine token and a logical-name subject (espace-safe: logical
/// names are identical across environment cells of one model), so the key
/// survives re-runs, re-profiles, and physical renames.
type FindingKey = private FindingKey of string

[<RequireQualifiedAccess>]
module FindingKey =

    /// Mint the key for a finding kind + its logical subject (an entity name,
    /// or an `Entity.Column` reference — a validated non-empty token). The
    /// subject is internally derived from Catalog/fidelity identities that are
    /// non-empty by their own smart constructors, so an empty subject is an
    /// impossible-state guard, not a refusal channel.
    let create (kind: EstateFindingKind) (subject: string) : FindingKey =
        if System.String.IsNullOrWhiteSpace subject then
            invalidArg (nameof subject) "FindingKey subject must be a non-empty logical token"
        FindingKey (System.String.Concat(EstateFindingKind.token kind, ":", subject.Trim())) // LINT-ALLOW: stable cross-artifact identity token minted once at the key boundary — two validated opaque segments joined by the ':' discriminator; no BCL or typed structure composes an identity string, and every downstream artifact consumes the token verbatim

    /// The key's stable text — the token every artifact carries verbatim.
    let text (FindingKey t) : string = t
