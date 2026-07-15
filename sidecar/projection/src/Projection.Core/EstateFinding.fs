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
    /// A kind present in some environments and absent from the target shape
    /// (or declared by the target and absent from an environment).
    | SchemaPresence
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
    /// A NOT-NULL (or PK) declaration the environment's data contradicts.
    | DataNotNull
    /// A UNIQUE/PK declaration the environment's data contradicts.
    | DataUnique
    /// A relationship whose source rows reference absent target records.
    | DataOrphans
    /// Values past a column's declared length or type envelope.
    | DataOverflow

[<RequireQualifiedAccess>]
module EstateFindingKind =

    /// Every kind, enumerated — the walkable half of the
    /// `finding ⇔ presentation` totality (the DU itself is the closed half;
    /// a variant added without a row here fails the coverage test).
    let all : EstateFindingKind list =
        [ EstateFindingKind.SchemaPresence
          EstateFindingKind.SchemaRename
          EstateFindingKind.SchemaAttributes
          EstateFindingKind.SchemaReferences
          EstateFindingKind.SchemaIndexes
          EstateFindingKind.SchemaFacets
          EstateFindingKind.DataNotNull
          EstateFindingKind.DataUnique
          EstateFindingKind.DataOrphans
          EstateFindingKind.DataOverflow ]

    /// The stable machine token (the `FindingKey` prefix and the
    /// `estate.json` discriminator). Never operator-facing on its own.
    let token (kind: EstateFindingKind) : string =
        match kind with
        | EstateFindingKind.SchemaPresence   -> "schema.presence"
        | EstateFindingKind.SchemaRename     -> "schema.rename"
        | EstateFindingKind.SchemaAttributes -> "schema.attributes"
        | EstateFindingKind.SchemaReferences -> "schema.references"
        | EstateFindingKind.SchemaIndexes    -> "schema.indexes"
        | EstateFindingKind.SchemaFacets     -> "schema.facets"
        | EstateFindingKind.DataNotNull      -> "data.notNull"
        | EstateFindingKind.DataUnique       -> "data.unique"
        | EstateFindingKind.DataOrphans      -> "data.orphans"
        | EstateFindingKind.DataOverflow     -> "data.overflow"

    /// The disposition lane a kind presents in (Appendix A). Schema kinds
    /// open in DECIDE: until the direction classifier (lag / fork / drift)
    /// lands, a shape divergence conservatively needs a ruling — the report's
    /// method note names the coming refinement, and the classifier wave
    /// re-lanes lag to WATCH without touching this vocabulary's shape.
    let laneOf (kind: EstateFindingKind) : EstateLane =
        match kind with
        | EstateFindingKind.SchemaPresence
        | EstateFindingKind.SchemaRename
        | EstateFindingKind.SchemaAttributes
        | EstateFindingKind.SchemaReferences
        | EstateFindingKind.SchemaIndexes
        | EstateFindingKind.SchemaFacets     -> EstateLane.Decide
        | EstateFindingKind.DataNotNull
        | EstateFindingKind.DataUnique
        | EstateFindingKind.DataOrphans
        | EstateFindingKind.DataOverflow     -> EstateLane.Repair

    /// The plane a kind lives on.
    let planeOf (kind: EstateFindingKind) : EstatePlane =
        match kind with
        | EstateFindingKind.SchemaPresence
        | EstateFindingKind.SchemaRename
        | EstateFindingKind.SchemaAttributes
        | EstateFindingKind.SchemaReferences
        | EstateFindingKind.SchemaIndexes
        | EstateFindingKind.SchemaFacets     -> EstatePlane.Schema
        | EstateFindingKind.DataNotNull
        | EstateFindingKind.DataUnique
        | EstateFindingKind.DataOrphans
        | EstateFindingKind.DataOverflow     -> EstatePlane.Data

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
