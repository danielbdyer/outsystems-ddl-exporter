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
    /// The REPAIR lane's ending: "Review block <key> of
    /// estate.remediation.<env>.sql." — minted beside the artifact.
    | ReviewBlock
    /// The RELAX lane's PROPOSED ending: "Merge overlay entry <key> of
    /// estate.overlay.json." — the merge is an operator edit; the engine
    /// never applies it.
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
          EstateFindingKind.DataOrphansPastBand
          EstateFindingKind.DataNotNullPastBand
          EstateFindingKind.PostureActive
          EstateFindingKind.PostureRetirable
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
        | EstateFindingKind.DataOrphansPastBand      -> "data.orphansPastBand"
        | EstateFindingKind.DataNotNullPastBand      -> "data.notNullPastBand"
        | EstateFindingKind.PostureActive            -> "posture.active"
        | EstateFindingKind.PostureRetirable         -> "posture.retirable"
        | EstateFindingKind.IdentitySynthesized      -> "identity.synthesized"
        | EstateFindingKind.OperationalCdc           -> "operational.cdc"

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
        | EstateFindingKind.SchemaFacets            -> EstateLane.Decide
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
        | EstateFindingKind.IdentitySynthesized     -> EstateLane.Watch
        | EstateFindingKind.DataCollationCollision  -> EstateLane.Repair
        | EstateFindingKind.DataOrphansPastBand
        | EstateFindingKind.DataNotNullPastBand
        | EstateFindingKind.PostureActive           -> EstateLane.Relax
        | EstateFindingKind.PostureRetirable        -> EstateLane.Repair
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
        | EstateFindingKind.DataCollationCollision
        | EstateFindingKind.DataOrphansPastBand
        | EstateFindingKind.DataNotNullPastBand
        | EstateFindingKind.PostureActive
        | EstateFindingKind.PostureRetirable        -> EstatePlane.Data
        | EstateFindingKind.IdentitySynthesized     -> EstatePlane.Identity
        | EstateFindingKind.OperationalCdc          -> EstatePlane.Operational

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
        | EstateFindingKind.SchemaFacets ->
            EstateLeverForm.Ruling "Rule the declared facets — modality and activity follow the model."
        | EstateFindingKind.DataOverflow ->
            EstateLeverForm.Ruling "Rule the width: declare the wider envelope in the model, or truncate to the declaration — the ruling precedes any repair."
        | EstateFindingKind.OperationalCdc ->
            EstateLeverForm.Ruling "Rule the CDC plan for the tracked kinds."
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
        | EstateFindingKind.IdentitySynthesized ->
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
            "Customer.TaxCode exists in cloud-uat and is absent from the target shape — deployed-ahead drift; no promotion explains it."
        | EstateFindingKind.SchemaLag ->
            "The target shape declares Invoice.ExternalRef and cloud-qa does not carry it — promotion lag; the ordinary publish resolves it."
        | EstateFindingKind.SchemaRename ->
            "Customer is named CustomerAccount in cloud-uat."
        | EstateFindingKind.SchemaAttributes ->
            "Customer's columns differ from the target shape in cloud-uat (2 difference(s))."
        | EstateFindingKind.SchemaReferences ->
            "Order's relationships differ from the target shape in cloud-qa (1 difference(s))."
        | EstateFindingKind.SchemaIndexes ->
            "Customer's indexes differ from the target shape in cloud-dev (1 difference(s))."
        | EstateFindingKind.SchemaFacets ->
            "Country's own facets differ from the target shape in cloud-uat (1 facet(s))."
        | EstateFindingKind.SchemaTrust ->
            "The relationship Order.CustomerId → Customer is enforced WITH NOCHECK in cloud-qa (untrusted) — re-trusting scans 12,400,000 row(s)."
        | EstateFindingKind.DataNotNull ->
            "Customer.Email declares NOT NULL; cloud-uat holds 4,120 NULL row(s)."
        | EstateFindingKind.DataUnique ->
            "Customer.Code declares unique; cloud-dev holds duplicate values."
        | EstateFindingKind.DataOrphans ->
            "Order.CustomerId: 3,214,000 row(s) in cloud-uat reference a record that does not exist, of which 3,101,000 reference the unset value 0."
        | EstateFindingKind.DataOverflow ->
            "Customer.Notes holds values to 4,812 characters against a declared 2,000 in cloud-uat."
        | EstateFindingKind.DataAsymmetry ->
            "OrderLine holds 10,400,000 row(s) in cloud-uat; OrderLine holds 12,000 row(s) in cloud-dev — verdicts drawn on this evidence are advisory at the asymmetry."
        | EstateFindingKind.DataUniquenessCandidate ->
            "Customer.LegacyCode is distinct in every observed row of cloud-uat (214,000 of 214,000 row(s))."
        | EstateFindingKind.DataHeadroom ->
            "Order.Id stands at 1,340,000,000 of int's 2,147,483,647 in cloud-uat — 62% of the ceiling is consumed."
        | EstateFindingKind.DataDateSentinel ->
            "Order.ShippedOn holds 812,000 row(s) at 1900-01-01 in cloud-uat — the platform's empty-date convention; a NOT NULL reading of the column is satisfied and empty of meaning."
        | EstateFindingKind.DataCollationCollision ->
            "Under a case-insensitive collation, Customer.Code collapses 240 case-distinct value(s) into duplicates in cloud-dev — the unique declaration fails on unification."
        | EstateFindingKind.DataOrphansPastBand ->
            "Order.CustomerId: 113,000 true orphan row(s) in cloud-uat exceed the repair band; the interim relaxation keeps the relationship untracked, and the reopen probe retires it at zero."
        | EstateFindingKind.DataNotNullPastBand ->
            "Customer.Email declares NOT NULL; 4,200,000 contradicting row(s) in cloud-uat exceed the repair band; the interim relaxation keeps the column nullable, and the reopen probe retires it at zero."
        | EstateFindingKind.PostureActive ->
            "The relationship Order.CustomerId → Customer is untracked by the interim posture; the reopen probe stands at 113,000 in cloud-uat."
        | EstateFindingKind.PostureRetirable ->
            "Order.CustomerId holds zero orphan row(s) in cloud-uat under the active relaxation — the relaxation is retirable; the relationship can track WITH CHECK."
        | EstateFindingKind.IdentitySynthesized ->
            "3 kind(s) in cloud-qa carry a different identity provenance than the target (synthesized against native) — renames across this pair are unstable until the identity anchors."
        | EstateFindingKind.OperationalCdc ->
            "CDC tracks Order in cloud-uat and not in cloud-dev — a cutover write to this kind feeds live consumers in cloud-uat alone."

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
