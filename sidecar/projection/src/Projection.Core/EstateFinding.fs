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
    /// SSDT emission fidelity — whether the schema this estate would publish
    /// faithfully models database reality, and whether it would deploy at all
    /// (the #669 audit dimension; a property of the target shape, not a
    /// cross-environment divergence).
    | Emission

/// The lever discipline a finding kind speaks (Appendix A — one lever per
/// line; WATCH lines carry no lever, by design). The form is per-kind and
/// total: the board mints the actual imperative from it — a ruling carries
/// its own text; the block and overlay forms compose the artifact
/// reference from the finding's key, so the lever and the artifact cannot
/// drift.
[<RequireQualifiedAccess>]
type EstateLeverForm =
    /// The DECIDE lane's ending: the ruling the operator makes, stated as
    /// a complete imperative sentence.
    | Ruling of imperative: string
    /// The REPAIR lane's ending: "Review the block for <subject> (<phrase>)
    /// in estate.remediation.<env>.sql." — minted beside the artifact; the
    /// same readable label leads the block in the file.
    | ReviewBlock
    /// The RELAX lane's PROPOSED ending: "Merge the config edit for <subject>
    /// (<phrase>) in estate.overlay.json." — the merge is an operator edit;
    /// the engine never applies it.
    | MergeOverlayEntry
    /// No lever — WATCH advisories, and the ACTIVE posture lines whose
    /// next move is the probe's meter, not an imperative.
    | NoLever

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
    /// A kind whose trigger set differs from the target shape.
    | SchemaTrigger
    /// A kind whose check constraints differ from the target shape.
    | SchemaCheck
    /// A kind that is a static entity in one shape and a regular entity in
    /// the other — seeding and identity behave differently.
    | SchemaModality
    /// A kind active in one shape and inactive in the other.
    | SchemaActivity
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
    /// A relationship whose TRUE orphan count (past the sentinel-zero
    /// split) exceeds the repair band — the prepared repair defers to the
    /// interim relaxation: keep it untracked, and the reopen probe retires
    /// it at zero (D3′, wave A6).
    | DataOrphansPastBand
    /// A NOT-NULL declaration whose contradicting row count exceeds the
    /// repair band — the interim relaxation keeps the column nullable
    /// until the backfill; the reopen probe retires it at zero (the D1
    /// relax arm, wave A6).
    | DataNotNullPastBand
    /// An ACTIVE interim relaxation (carried by the loaded config's
    /// tightening posture) whose reopen probe still reads above zero —
    /// the posture's meter, not a new divergence (wave A6).
    | PostureActive
    /// An ACTIVE interim relaxation whose reopen probe reads zero in
    /// every evidenced environment — the relaxation is retirable; the
    /// re-tightening is a preparable repair (wave A6).
    | PostureRetirable
    /// Kinds whose identity provenance differs from the target's for the
    /// same name (synthesized against native) — renames across that pair
    /// are unstable until the identity anchors (I3, wave A4).
    | IdentitySynthesized
    /// CDC tracks a kind in some environments and not others — a cutover
    /// write feeds live consumers unevenly (O1, wave A4).
    | OperationalCdc
    /// A relationship whose target entity has a composite primary key — the
    /// emitted single-column foreign key would reference only the first key
    /// column, which SQL Server rejects (the #669 WP-12 emission hazard).
    | EmissionCompositePkFk
    /// Two entities in different modules that emit to the same table name —
    /// they would collide in one published schema (#669 WP-16).
    | EmissionDuplicateName
    /// An identifier longer than SQL Server's 128-character limit — it would
    /// be rejected at deploy (#669 WP-11).
    | EmissionLongName
    /// An entity with no primary key — it emits as a heap (no clustered key),
    /// which affects replication and performance (#669 §9 heap audit).
    | EmissionNoPrimaryKey
    /// A column whose concrete type (float / real / datetimeoffset / xml) the
    /// data-transfer plane carries through a coarser form — the inventory that
    /// scopes #669 WP-17 (surfaced now; the faithful carriage is WP-17).
    | EmissionLossyScalar
    /// A relationship carrying a non-default ON UPDATE action — unusual, worth
    /// confirming (#669 §9 audit; most references leave it at the default).
    | EmissionNonDefaultOnUpdate
    /// An authored default whose raw text does not parse as a value of the
    /// column's type — it deploys and then fails at the first insert that
    /// relies on it (the #669 M-1 residue after the classification lift;
    /// DECISIONS 2026-07-18).
    | EmissionAuthoredDefault
    /// A computed column whose expression references an identifier that
    /// resolves to no column of the entity — the logical rewrite cannot
    /// complete, and a case-sensitive target rejects the physical form at
    /// deploy (#669 M-8 / EF-19).
    | EmissionComputedExprIdentifiers
    /// A trigger whose target or body references a physical name with no
    /// logical rewrite — the emitted trigger would name objects absent from
    /// the published schema (#669 EF-7; operator decision 9).
    | EmissionTriggerUnrewritten
    /// An index option the source carries that the emission does not
    /// (data compression) — the deployed copy silently stores without it
    /// (#669 EF-15 / M-2).
    | EmissionIndexOptionDropped
    /// A column NOT NULL in the deployed database and nullable in the model
    /// — publishing the model's shape drops the constraint (#669 M-3 /
    /// EF-18; deployed-schema over model, operator decision 2).
    | EmissionDeployedNotNullLoosened
    /// Entities that reference each other in a cycle — the data load defers
    /// enforcement inside the cycle and re-checks after the members load
    /// (#669 B-1; advisory once the acyclic-majority ordering ships).
    | EmissionDataLaneOrder
    /// A system-versioned (temporal) source table the emission carries as a
    /// plain table — period columns, versioning, and the history table are
    /// absent from the published schema (#669 EF-20; operator decision 12:
    /// present means red until hand-authored).
    | EmissionTemporalDropped
    /// A PERSISTED computed column the emission carries as non-persisted —
    /// storage and indexability change (#669 EF-21; operator decision 12).
    | EmissionPersistedDropped
    /// A sequence object at the source that the published schema does not
    /// carry — the first insert that calls NEXT VALUE FOR fails (#669
    /// EF-22; operator decision 12).
    | EmissionSequenceDropped

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
          EstateFindingKind.SchemaTrigger
          EstateFindingKind.SchemaCheck
          EstateFindingKind.SchemaModality
          EstateFindingKind.SchemaActivity
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
          EstateFindingKind.DataOrphansPastBand
          EstateFindingKind.DataNotNullPastBand
          EstateFindingKind.PostureActive
          EstateFindingKind.PostureRetirable
          EstateFindingKind.IdentitySynthesized
          EstateFindingKind.OperationalCdc
          EstateFindingKind.EmissionCompositePkFk
          EstateFindingKind.EmissionDuplicateName
          EstateFindingKind.EmissionLongName
          EstateFindingKind.EmissionNoPrimaryKey
          EstateFindingKind.EmissionLossyScalar
          EstateFindingKind.EmissionNonDefaultOnUpdate
          EstateFindingKind.EmissionAuthoredDefault
          EstateFindingKind.EmissionComputedExprIdentifiers
          EstateFindingKind.EmissionTriggerUnrewritten
          EstateFindingKind.EmissionIndexOptionDropped
          EstateFindingKind.EmissionDeployedNotNullLoosened
          EstateFindingKind.EmissionDataLaneOrder
          EstateFindingKind.EmissionTemporalDropped
          EstateFindingKind.EmissionPersistedDropped
          EstateFindingKind.EmissionSequenceDropped ]

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
        | EstateFindingKind.SchemaTrigger            -> "schema.trigger"
        | EstateFindingKind.SchemaCheck              -> "schema.check"
        | EstateFindingKind.SchemaModality           -> "schema.modality"
        | EstateFindingKind.SchemaActivity           -> "schema.activity"
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
        | EstateFindingKind.DataOrphansPastBand      -> "data.orphansPastBand"
        | EstateFindingKind.DataNotNullPastBand      -> "data.notNullPastBand"
        | EstateFindingKind.PostureActive            -> "posture.active"
        | EstateFindingKind.PostureRetirable         -> "posture.retirable"
        | EstateFindingKind.IdentitySynthesized      -> "identity.synthesized"
        | EstateFindingKind.OperationalCdc           -> "operational.cdc"
        | EstateFindingKind.EmissionCompositePkFk    -> "emission.compositePkFk"
        | EstateFindingKind.EmissionDuplicateName    -> "emission.duplicateName"
        | EstateFindingKind.EmissionLongName         -> "emission.longName"
        | EstateFindingKind.EmissionNoPrimaryKey     -> "emission.noPrimaryKey"
        | EstateFindingKind.EmissionLossyScalar      -> "emission.lossyScalar"
        | EstateFindingKind.EmissionNonDefaultOnUpdate -> "emission.nonDefaultOnUpdate"
        | EstateFindingKind.EmissionAuthoredDefault  -> "emission.authoredDefault"
        | EstateFindingKind.EmissionComputedExprIdentifiers -> "emission.computedExprIdentifiers"
        | EstateFindingKind.EmissionTriggerUnrewritten -> "emission.triggerUnrewritten"
        | EstateFindingKind.EmissionIndexOptionDropped -> "emission.indexOptionDropped"
        | EstateFindingKind.EmissionDeployedNotNullLoosened -> "emission.deployedNotNullLoosened"
        | EstateFindingKind.EmissionDataLaneOrder    -> "emission.dataLaneOrder"
        | EstateFindingKind.EmissionTemporalDropped  -> "emission.temporalDropped"
        | EstateFindingKind.EmissionPersistedDropped -> "emission.persistedDropped"
        | EstateFindingKind.EmissionSequenceDropped  -> "emission.sequenceDropped"

    /// The machine token's inverse — the kind a stored `token` names, or
    /// `None` for an unknown token. Derived from `all`, so it cannot drift
    /// from `token` (one closed set, one round-trip).
    let ofToken (t: string) : EstateFindingKind option =
        all |> List.tryFind (fun k -> token k = t)

    /// A short, plain noun phrase naming the kind — the readable face of the
    /// machine token. The board lever, the ACTION line, the remediation
    /// block header, and the overlay entry lead with `<subject> (<phrase>)`
    /// instead of the raw `<token>:<subject>` key (THE_VOICE §2.2: the
    /// machine token never leads the operator's line; it stays the
    /// cross-artifact key, demoted to a searchable comment). Total by
    /// construction, so a new kind cannot land without its readable phrase.
    let phrase (kind: EstateFindingKind) : string =
        match kind with
        | EstateFindingKind.SchemaPresence           -> "an extra object"
        | EstateFindingKind.SchemaLag                -> "not yet promoted here"
        | EstateFindingKind.SchemaRename             -> "name differs"
        | EstateFindingKind.SchemaAttributes         -> "columns differ"
        | EstateFindingKind.SchemaReferences         -> "relationships differ"
        | EstateFindingKind.SchemaIndexes            -> "indexes differ"
        | EstateFindingKind.SchemaTrigger            -> "a trigger differs"
        | EstateFindingKind.SchemaCheck              -> "a check constraint differs"
        | EstateFindingKind.SchemaModality           -> "static-vs-regular entity differs"
        | EstateFindingKind.SchemaActivity           -> "active-vs-inactive differs"
        | EstateFindingKind.SchemaTrust              -> "an untrusted relationship (NOCHECK)"
        | EstateFindingKind.DataNotNull              -> "NULLs under NOT NULL"
        | EstateFindingKind.DataUnique               -> "duplicates under UNIQUE"
        | EstateFindingKind.DataOrphans              -> "orphan references"
        | EstateFindingKind.DataOverflow             -> "values that exceed the column length setting"
        | EstateFindingKind.DataAsymmetry            -> "very different row counts"
        | EstateFindingKind.DataUniquenessCandidate  -> "unique everywhere — a possible business key"
        | EstateFindingKind.DataHeadroom             -> "the ID is nearing its limit"
        | EstateFindingKind.DataDateSentinel         -> "1900-01-01 placeholder dates"
        | EstateFindingKind.DataCollationCollision   -> "case-only differences that become duplicates"
        | EstateFindingKind.DataOrphansPastBand      -> "leave the relationship unenforced for now"
        | EstateFindingKind.DataNotNullPastBand      -> "leave the column nullable for now"
        | EstateFindingKind.PostureActive            -> "not clean yet — the interim change stands"
        | EstateFindingKind.PostureRetirable         -> "clean now — the interim change is removable"
        | EstateFindingKind.IdentitySynthesized      -> "key generated differently across environments"
        | EstateFindingKind.OperationalCdc           -> "CDC tracks this table unevenly"
        | EstateFindingKind.EmissionCompositePkFk    -> "targets a composite primary key"
        | EstateFindingKind.EmissionDuplicateName    -> "a table name shared across modules"
        | EstateFindingKind.EmissionLongName         -> "an over-long identifier"
        | EstateFindingKind.EmissionNoPrimaryKey     -> "no primary key — emits as a heap"
        | EstateFindingKind.EmissionLossyScalar      -> "a type the data plane carries coarsely"
        | EstateFindingKind.EmissionNonDefaultOnUpdate -> "a non-default ON UPDATE"
        | EstateFindingKind.EmissionAuthoredDefault  -> "an authored default that does not parse"
        | EstateFindingKind.EmissionComputedExprIdentifiers -> "a computed expression that cannot be rewritten"
        | EstateFindingKind.EmissionTriggerUnrewritten -> "a trigger that cannot be rewritten"
        | EstateFindingKind.EmissionIndexOptionDropped -> "an index setting the emission does not carry"
        | EstateFindingKind.EmissionDeployedNotNullLoosened -> "deployed NOT NULL, model nullable"
        | EstateFindingKind.EmissionDataLaneOrder    -> "a reference cycle in the load order"
        | EstateFindingKind.EmissionTemporalDropped  -> "system-versioning the emission does not carry"
        | EstateFindingKind.EmissionPersistedDropped -> "a PERSISTED marking the emission does not carry"
        | EstateFindingKind.EmissionSequenceDropped  -> "a sequence the emission does not carry"

    /// The disposition lane a kind presents in (Appendix A). The direction
    /// classifier (wave A3) splits presence: a kind an environment carries
    /// BEYOND the target is deployed-ahead drift (DECIDE); a kind the target
    /// declares that an environment has not yet received is promotion lag
    /// (WATCH — the ordinary publish resolves it). The untrusted-constraint
    /// census is a preparable repair; the asymmetry and candidacy advisories
    /// are watchable by design (no lever). Overflow is a RULING (wave A6,
    /// aligning the code to the contract's D4 row): widen or truncate is
    /// the operator's call — a prepared truncation would read as a default
    /// path, and there is none. The band splits (wave A6) send past-band
    /// violations to the interim posture; the posture's own lines split
    /// active (the meter) from retirable (a preparable repair).
    let laneOf (kind: EstateFindingKind) : EstateLane =
        match kind with
        | EstateFindingKind.SchemaPresence
        | EstateFindingKind.SchemaRename
        | EstateFindingKind.SchemaAttributes
        | EstateFindingKind.SchemaReferences
        | EstateFindingKind.SchemaIndexes
        | EstateFindingKind.SchemaTrigger
        | EstateFindingKind.SchemaCheck
        | EstateFindingKind.SchemaModality
        | EstateFindingKind.SchemaActivity          -> EstateLane.Decide
        | EstateFindingKind.SchemaLag               -> EstateLane.Watch
        | EstateFindingKind.SchemaTrust             -> EstateLane.Repair
        | EstateFindingKind.DataNotNull
        | EstateFindingKind.DataUnique
        | EstateFindingKind.DataOrphans             -> EstateLane.Repair
        | EstateFindingKind.DataOverflow            -> EstateLane.Decide
        | EstateFindingKind.DataAsymmetry
        | EstateFindingKind.DataUniquenessCandidate
        | EstateFindingKind.DataHeadroom
        | EstateFindingKind.DataDateSentinel
        | EstateFindingKind.IdentitySynthesized
        | EstateFindingKind.EmissionNoPrimaryKey
        | EstateFindingKind.EmissionLossyScalar
        | EstateFindingKind.EmissionNonDefaultOnUpdate -> EstateLane.Watch
        | EstateFindingKind.DataCollationCollision  -> EstateLane.Repair
        | EstateFindingKind.DataOrphansPastBand
        | EstateFindingKind.DataNotNullPastBand
        | EstateFindingKind.PostureActive           -> EstateLane.Relax
        | EstateFindingKind.PostureRetirable        -> EstateLane.Repair
        | EstateFindingKind.OperationalCdc
        | EstateFindingKind.EmissionCompositePkFk
        | EstateFindingKind.EmissionDuplicateName
        | EstateFindingKind.EmissionLongName        -> EstateLane.Decide
        // The cutover-board population wave (DECISIONS 2026-07-18; the #669
        // audit → CUTOVER_BOARD_POPULATION_PLAN.md §3). Deploy-blocking or
        // intent-dropping emission facts are rulings; the two advisories
        // (an index option the emission does not carry; a handled reference
        // cycle) watch, by design.
        | EstateFindingKind.EmissionAuthoredDefault
        | EstateFindingKind.EmissionComputedExprIdentifiers
        | EstateFindingKind.EmissionTriggerUnrewritten
        | EstateFindingKind.EmissionDeployedNotNullLoosened
        | EstateFindingKind.EmissionTemporalDropped
        | EstateFindingKind.EmissionPersistedDropped
        | EstateFindingKind.EmissionSequenceDropped -> EstateLane.Decide
        | EstateFindingKind.EmissionIndexOptionDropped
        | EstateFindingKind.EmissionDataLaneOrder   -> EstateLane.Watch

    /// The plane a kind lives on.
    let planeOf (kind: EstateFindingKind) : EstatePlane =
        match kind with
        | EstateFindingKind.SchemaPresence
        | EstateFindingKind.SchemaLag
        | EstateFindingKind.SchemaRename
        | EstateFindingKind.SchemaAttributes
        | EstateFindingKind.SchemaReferences
        | EstateFindingKind.SchemaIndexes
        | EstateFindingKind.SchemaTrigger
        | EstateFindingKind.SchemaCheck
        | EstateFindingKind.SchemaModality
        | EstateFindingKind.SchemaActivity
        | EstateFindingKind.SchemaTrust             -> EstatePlane.Schema
        | EstateFindingKind.DataNotNull
        | EstateFindingKind.DataUnique
        | EstateFindingKind.DataOrphans
        | EstateFindingKind.DataOverflow
        | EstateFindingKind.DataAsymmetry
        | EstateFindingKind.DataUniquenessCandidate
        | EstateFindingKind.DataHeadroom
        | EstateFindingKind.DataDateSentinel
        | EstateFindingKind.DataCollationCollision
        | EstateFindingKind.DataOrphansPastBand
        | EstateFindingKind.DataNotNullPastBand
        | EstateFindingKind.PostureActive
        | EstateFindingKind.PostureRetirable        -> EstatePlane.Data
        | EstateFindingKind.IdentitySynthesized     -> EstatePlane.Identity
        | EstateFindingKind.OperationalCdc          -> EstatePlane.Operational
        | EstateFindingKind.EmissionCompositePkFk
        | EstateFindingKind.EmissionDuplicateName
        | EstateFindingKind.EmissionLongName
        | EstateFindingKind.EmissionNoPrimaryKey
        | EstateFindingKind.EmissionLossyScalar
        | EstateFindingKind.EmissionNonDefaultOnUpdate
        | EstateFindingKind.EmissionAuthoredDefault
        | EstateFindingKind.EmissionComputedExprIdentifiers
        | EstateFindingKind.EmissionTriggerUnrewritten
        | EstateFindingKind.EmissionIndexOptionDropped
        | EstateFindingKind.EmissionDeployedNotNullLoosened
        | EstateFindingKind.EmissionDataLaneOrder
        | EstateFindingKind.EmissionTemporalDropped
        | EstateFindingKind.EmissionPersistedDropped
        | EstateFindingKind.EmissionSequenceDropped -> EstatePlane.Emission

    /// The presentation contract's lever form per kind (Appendix A, wave
    /// A6 — the contract table held to the code). The board mints the
    /// rendered lever from the form: a ruling carries its own imperative
    /// here; the block and overlay forms compose the artifact reference
    /// from the finding's key at mint time. The coherence with `laneOf` is
    /// the `finding ⇔ presentation` totality test's law: DECIDE ⇔ a
    /// ruling; REPAIR ⇔ a block review; RELAX ⇒ the overlay merge or —
    /// for the ACTIVE posture, whose next move is the probe's meter — no
    /// lever; WATCH ⇔ no lever, by design.
    let leverFormOf (kind: EstateFindingKind) : EstateLeverForm =
        match kind with
        | EstateFindingKind.SchemaPresence ->
            EstateLeverForm.Ruling "Rule the kind: adopt it into the model, or schedule its removal."
        | EstateFindingKind.SchemaRename ->
            EstateLeverForm.Ruling "Rule the declared name, then re-run."
        | EstateFindingKind.SchemaAttributes ->
            EstateLeverForm.Ruling "Rule the declared attributes — the repair or the relaxation follows the ruling."
        | EstateFindingKind.SchemaReferences ->
            EstateLeverForm.Ruling "Rule the declared relationships and their delete behavior."
        | EstateFindingKind.SchemaIndexes ->
            EstateLeverForm.Ruling "Rule the declared indexes, then re-run."
        | EstateFindingKind.SchemaTrigger ->
            EstateLeverForm.Ruling "Rule the trigger: adopt it into the model, or schedule its removal."
        | EstateFindingKind.SchemaCheck ->
            EstateLeverForm.Ruling "Rule the check constraint: adopt it into the model, or schedule its removal."
        | EstateFindingKind.SchemaModality ->
            EstateLeverForm.Ruling "Rule the entity's kind — static or regular, per the model."
        | EstateFindingKind.SchemaActivity ->
            EstateLeverForm.Ruling "Rule the entity's active state, per the model."
        | EstateFindingKind.DataOverflow ->
            EstateLeverForm.Ruling "Rule the width: declare the wider envelope in the model, or truncate to the declaration — the ruling precedes any repair."
        | EstateFindingKind.OperationalCdc ->
            EstateLeverForm.Ruling "Rule the CDC plan for the tracked kinds."
        | EstateFindingKind.EmissionCompositePkFk ->
            EstateLeverForm.Ruling "Rule the relationship: model the target's full composite key, or drop the foreign key before publishing."
        | EstateFindingKind.EmissionDuplicateName ->
            EstateLeverForm.Ruling "Rule the collision: rename or remap one entity so the emitted table names are distinct."
        | EstateFindingKind.EmissionLongName ->
            EstateLeverForm.Ruling "Rule the name: shorten it to 128 characters or fewer before publishing."
        | EstateFindingKind.EmissionAuthoredDefault ->
            EstateLeverForm.Ruling "Rule the default: correct it to a value the column's type accepts, or clear it, then republish."
        | EstateFindingKind.EmissionComputedExprIdentifiers ->
            EstateLeverForm.Ruling "Rule the expression: reference only the entity's own columns, then republish."
        | EstateFindingKind.EmissionTriggerUnrewritten ->
            EstateLeverForm.Ruling "Rule the trigger: adopt a logical-name rewrite, or hand-author it in the deploy repository."
        | EstateFindingKind.EmissionDeployedNotNullLoosened ->
            EstateLeverForm.Ruling "Rule the column: declare it mandatory in the model, or approve the loosening before publishing."
        | EstateFindingKind.EmissionTemporalDropped ->
            EstateLeverForm.Ruling "Rule the table: hand-author its system-versioning in the deploy repository, or approve the plain-table emission."
        | EstateFindingKind.EmissionPersistedDropped ->
            EstateLeverForm.Ruling "Rule the column: hand-author the PERSISTED marking in the deploy repository, or approve its removal."
        | EstateFindingKind.EmissionSequenceDropped ->
            EstateLeverForm.Ruling "Rule the sequence: hand-author it in the deploy repository, or approve its absence."
        | EstateFindingKind.SchemaTrust
        | EstateFindingKind.DataNotNull
        | EstateFindingKind.DataUnique
        | EstateFindingKind.DataOrphans
        | EstateFindingKind.DataCollationCollision
        | EstateFindingKind.PostureRetirable ->
            EstateLeverForm.ReviewBlock
        | EstateFindingKind.DataOrphansPastBand
        | EstateFindingKind.DataNotNullPastBand ->
            EstateLeverForm.MergeOverlayEntry
        | EstateFindingKind.SchemaLag
        | EstateFindingKind.DataAsymmetry
        | EstateFindingKind.DataUniquenessCandidate
        | EstateFindingKind.DataHeadroom
        | EstateFindingKind.DataDateSentinel
        | EstateFindingKind.PostureActive
        | EstateFindingKind.IdentitySynthesized
        | EstateFindingKind.EmissionNoPrimaryKey
        | EstateFindingKind.EmissionLossyScalar
        | EstateFindingKind.EmissionNonDefaultOnUpdate
        | EstateFindingKind.EmissionIndexOptionDropped
        | EstateFindingKind.EmissionDataLaneOrder ->
            EstateLeverForm.NoLever

    /// The presentation contract's statement specimen per kind (Appendix
    /// A, wave A6): the register anchor every composed statement of the
    /// kind follows — stative, agentless, complete sentences, humane
    /// numbers, concrete subjects. The specimen is documentation held to
    /// the code: a kind cannot land without one (this match is total),
    /// and the totality test reads every specimen against the mechanical
    /// register laws (a complete sentence; the banned list).
    let specimenOf (kind: EstateFindingKind) : string =
        match kind with
        | EstateFindingKind.SchemaPresence ->
            "Customer.TaxCode exists in cloud-uat but is missing from cloud-dev, the promotion source — no promotion added it."
        | EstateFindingKind.SchemaLag ->
            "cloud-dev declares Invoice.ExternalRef and cloud-qa has not received it — the ordinary publish promotes it."
        | EstateFindingKind.SchemaRename ->
            "Customer is named CustomerAccount in cloud-uat."
        | EstateFindingKind.SchemaAttributes ->
            "cloud-uat's Customer has 2 column(s) that differ from cloud-dev."
        | EstateFindingKind.SchemaReferences ->
            "cloud-qa's Order has 1 relationship that differs from cloud-dev."
        | EstateFindingKind.SchemaIndexes ->
            "cloud-uat's Customer has 1 index that differs from cloud-dev."
        | EstateFindingKind.SchemaTrigger ->
            "cloud-uat's Country has a trigger cloud-dev does not."
        | EstateFindingKind.SchemaCheck ->
            "cloud-uat's Order has a check constraint cloud-dev does not."
        | EstateFindingKind.SchemaModality ->
            "Country is a static entity in cloud-uat and a regular entity in cloud-dev — seeding and identity behave differently."
        | EstateFindingKind.SchemaActivity ->
            "Customer is active in cloud-dev and inactive in cloud-uat — the two shapes disagree about its life."
        | EstateFindingKind.SchemaTrust ->
            "The relationship Order.CustomerId → Customer is enforced WITH NOCHECK in cloud-qa (untrusted) — re-trusting scans 12,400,000 row(s)."
        | EstateFindingKind.DataNotNull ->
            "Customer.Email is required (NOT NULL); cloud-uat holds 4,120 row(s) that are NULL."
        | EstateFindingKind.DataUnique ->
            "Customer.Code must be unique; cloud-dev holds duplicate values."
        | EstateFindingKind.DataOrphans ->
            "Order.CustomerId in cloud-uat has 3,214,000 row(s) that reference a Customer that does not exist, of which 3,101,000 use the unset value 0."
        | EstateFindingKind.DataOverflow ->
            "Customer.Notes holds values up to 4,812 characters in cloud-uat, but its column length setting is 2,000."
        | EstateFindingKind.DataAsymmetry ->
            "cloud-uat's OrderLine holds 10,400,000 row(s) and cloud-dev's holds 12,000 — findings drawn from cloud-dev's smaller sample are advisory."
        | EstateFindingKind.DataUniquenessCandidate ->
            "Customer.LegacyCode has no duplicate in any environment — every one of 214,000 row(s) is distinct, so it could serve as a business key for matching."
        | EstateFindingKind.DataHeadroom ->
            "Order.Id has reached 1,340,000,000 of the 2,147,483,647 its INT column allows in cloud-uat — 62% of the limit is used."
        | EstateFindingKind.DataDateSentinel ->
            "Order.ShippedOn holds 812,000 row(s) set to 1900-01-01 in cloud-uat — the platform's stand-in for an empty date; a required-column reading is satisfied, but the dates carry no real value."
        | EstateFindingKind.DataCollationCollision ->
            "Under cloud-dev's text sorting, Customer.Code has 240 value(s) that differ only by letter case and would become duplicates — the unique index fails on unification."
        | EstateFindingKind.DataOrphansPastBand ->
            "Order.CustomerId has 113,000 reference(s) to missing Customer rows in cloud-uat — too many to clear before cutover; leave the relationship unenforced until they clear."
        | EstateFindingKind.DataNotNullPastBand ->
            "Customer.Email has 4,200,000 NULL row(s) in cloud-uat — too many to fill before cutover; leave the column nullable until they are backfilled."
        | EstateFindingKind.PostureActive ->
            "Order.CustomerId → Customer is left unenforced for now; 113,000 reference(s) still point to missing rows in cloud-uat."
        | EstateFindingKind.PostureRetirable ->
            "Order.CustomerId → Customer has zero missing reference(s) in cloud-uat — the relationship can be enforced again."
        | EstateFindingKind.IdentitySynthesized ->
            "Status.Id is generated by the database in cloud-qa and carried as a fixed value in cloud-dev — the same table numbers its rows differently, so renames stay unstable until the identity is anchored."
        | EstateFindingKind.OperationalCdc ->
            "Change tracking is on for Order in cloud-uat and off in cloud-dev — a cutover write feeds live consumers in cloud-uat alone."
        | EstateFindingKind.EmissionCompositePkFk ->
            "Order.CustomerId → Customer targets a composite primary key — the emitted foreign key would reference only its first column, which SQL Server rejects at deploy."
        | EstateFindingKind.EmissionDuplicateName ->
            "2 entities are named 'Customer' (in modules Sales and Billing) — they would collide as one emitted table."
        | EstateFindingKind.EmissionLongName ->
            "A column name on Customer is 140 characters — SQL Server rejects identifiers over 128 characters at deploy."
        | EstateFindingKind.EmissionNoPrimaryKey ->
            "AuditLog has no primary key — it would emit as a heap, with no clustered key for replication or lookups."
        | EstateFindingKind.EmissionLossyScalar ->
            "Order.Amount is float — the data-transfer plane carries it through a coarser form that can lose precision (WP-17); this scopes how carefully the transfer must handle it."
        | EstateFindingKind.EmissionNonDefaultOnUpdate ->
            "Order.CustomerId → Customer carries ON UPDATE CASCADE — an unusual rule; confirm it is intended, since most references leave ON UPDATE at the default."
        | EstateFindingKind.EmissionAuthoredDefault ->
            "Order.CreatedOn carries the authored default 'tomorrow' — it does not parse as a date-time value, and the first insert that relies on it fails."
        | EstateFindingKind.EmissionComputedExprIdentifiers ->
            "Product.DisplayLabel is computed from [SKU_OLD], which resolves to no column of Product — the expression cannot be rewritten to logical names, and a case-sensitive database rejects it at deploy."
        | EstateFindingKind.EmissionTriggerUnrewritten ->
            "The trigger TRG_ORDER_AUDIT on Order references a table with no logical rewrite — the emitted trigger would name an object absent from the published schema."
        | EstateFindingKind.EmissionIndexOptionDropped ->
            "The index IX_Order_PlacedAt on Order is compressed (PAGE) at the source — the emitted index carries no compression setting, so the deployed copy stores uncompressed."
        | EstateFindingKind.EmissionDeployedNotNullLoosened ->
            "Customer.Email is NOT NULL in the deployed database and nullable in the model — publishing the model's shape drops the constraint."
        | EstateFindingKind.EmissionDataLaneOrder ->
            "Customer and Order reference each other in a cycle with no deferrable relationship — every other table keeps its dependency position, and the cycle's own rows cannot load in one pass while its relationships are enforced."
        | EstateFindingKind.EmissionTemporalDropped ->
            "Order is system-versioned at the source, with history in Order_History — the emitted table carries no system-versioning, so version history stops at cutover."
        | EstateFindingKind.EmissionPersistedDropped ->
            "Product.TotalWithTax is a PERSISTED computed column at the source — the emitted column is not persisted, which changes storage and indexability."
        | EstateFindingKind.EmissionSequenceDropped ->
            "The sequence OrderNumberSeq exists at the source — the published schema does not carry it, and the first insert that calls NEXT VALUE FOR fails."

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

    /// The logical subject the key names (the `Entity` or `Entity.Column`
    /// after the kind discriminator) — the plain half an operator reads.
    let subject (FindingKey t) : string =
        let i = t.IndexOf ':'
        if i < 0 then t else t.Substring(i + 1)

    /// The readable face of the key: `<subject> (<phrase>)` — e.g.
    /// `Order.CustomerId (orphan references)`. The board lever, the ACTION
    /// line, the remediation block header, and the overlay entry lead with
    /// this; the raw `text` key stays the cross-artifact machine token,
    /// carried beside it as a searchable comment (THE_VOICE §2.2 — no
    /// leaked token on the operator's statement line).
    let readableLabel (key: FindingKey) : string =
        let subj = subject key
        match EstateFindingKind.ofToken ((text key).Substring(0, max 0 ((text key).IndexOf ':'))) with
        | Some kind -> System.String.Concat(subj, " (", EstateFindingKind.phrase kind, ")") // LINT-ALLOW: readable operator label composed once at the key boundary — plain subject + parenthesized kind phrase; the machine token stays the cross-artifact key
        | None -> subj

/// What an interim relaxation does — closed, growing at consumers (the
/// ideation's `RelaxationAction`, wave A6). Each case carries the
/// operator-readable three-part ref (`Module.Entity.Attribute`) its
/// overlay entry's suggested config edit names; `TighteningBinding`
/// resolves the same ref back to a typed key at merge time — the A44
/// circle (expressible ⇔ reachable) closed in both directions.
[<RequireQualifiedAccess>]
type RelaxationAction =
    /// Keep the relationship untracked (`referenceOverrides` +
    /// `keepUntracked` on a foreignKey intervention).
    | KeepUntracked of referenceRef: string
    /// Keep the column nullable (`overrides` + `keepNullable` on a
    /// budget-less nullability intervention).
    | KeepNullable of attributeRef: string

/// One interim relaxation — the typed value behind an
/// `estate.overlay.json` entry and its `estate.probes.sql` probe (wave
/// A6). Core-resident so the OperationalDiagnostics emitter can consume
/// it; π-coherent by construction — the overlay entry, the reopen probe,
/// and the RELAX-lane finding all carry `Scope`. Expiry is deliberately
/// absent (DECISIONS 2026-07-15: retirement is probe-only until the
/// operator asks for calendar or run-count expiry).
type Relaxation =
    {
        /// The finding this relaxation covers — the cross-artifact key.
        Scope       : FindingKey
        /// The config edit the overlay suggests (never applies).
        Action      : RelaxationAction
        /// The per-environment counts that forced it (the finding's
        /// evidence, carried onto the overlay entry's note).
        Evidence    : (string * int64) list
        /// The reopen probe — one runnable SELECT whose zero retires the
        /// relaxation (the Active-deferrals discipline applied to data:
        /// every relaxation carries its re-tighten trigger, executably).
        ReopenProbe : string
    }
