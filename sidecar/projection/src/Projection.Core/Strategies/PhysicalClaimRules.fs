namespace Projection.Core

/// Physical-claim adjudication (the data-sink chapter, S11) — the "latest
/// edition" question made a total, pure strategy. One physical table; N
/// metadata claims on it (an eSpace entity, an extension's external
/// re-registration, a tombstone whose table survived — the post-cutover
/// retrieval scenario THE_DATA_SINK.md opens with). The ossys plane exposes
/// no registration timeline, so the SINK supplies the temporal dimension:
/// each claim carries the journal sync at which it was first witnessed, and
/// the adjudication is total over every claim set the journal can assemble.
///
/// The ladder (each layer total; the NullabilityRules layered shape):
///   1. Partition live vs tombstone (a tombstone never outranks a live claim).
///   2. Exactly ONE live claim ⇒ `Adopted` — the cutover's happy path: the
///      re-registration is live, the old registration is a tombstone riding
///      along as `outranked` lineage (the operator's "latest edition").
///   3. TWO OR MORE live claims ⇒ `Contested`, ALWAYS — two live writers on
///      one table are never silently ranked into an adoption (the exact
///      silent-pick the house forbids; Contested participates in Fork →
///      exit 5 at the estate). The ladder still ORDERS the rivals inside
///      the payload — an active eSpace entity before an active extension
///      re-registration (the platform's ownership order), then the LATER
///      first-witnessed sync (the sink's temporal dimension), then the row
///      id — so the finding leads with the recommendation.
///   4. No live claim: tombstones only ⇒ `TombstoneOnly` (the estate is
///      recoverable from the witnessed editions — the original incident);
///      no claims at all ⇒ `Unclaimed` (S12's residue sweep detects these
///      live against sys.tables).
[<RequireQualifiedAccess>]
module PhysicalClaimRules =

    /// One metadata claim on a physical table, as the journal assembles it.
    /// align-II.10 (E3; audit a1) — WHEN the journal first witnessed a
    /// claim, as a value: from the first witnessed edition, at a later
    /// named sync, or UNKNOWN — the journal carries no appearance line (a
    /// gappy or reconciled ledger). Unknown was previously fabricated as
    /// sync 1; the trichotomy keeps the ladder conservative (missing
    /// provenance never outranks a known-recent claim) while the rendering
    /// says "?" instead of lying.
    [<RequireQualifiedAccess>]
    type FirstWitnessedSync =
        | SinceGenesis
        | AtSync of syncId: SyncOrdinal
        | Unknown

    [<RequireQualifiedAccess>]
    module FirstWitnessedSync =

        /// The one classifier: the journal's appearance sync, when found.
        /// The genesis ordinal IS the genesis witness (the first edition's
        /// diff from the empty state).
        let ofAppearance (found: SyncOrdinal option) : FirstWitnessedSync =
            match found with
            | Some o when o = SyncOrdinal.genesis -> FirstWitnessedSync.SinceGenesis
            | Some o -> FirstWitnessedSync.AtSync o
            | None -> FirstWitnessedSync.Unknown

        /// The ladder's recency rank — SinceGenesis and Unknown both rank
        /// earliest (1): recency never rewards missing provenance (the
        /// retired fabrication ranked identically, so healthy AND gappy
        /// ladders are order-stable).
        let rank (f: FirstWitnessedSync) : int =
            match f with
            | FirstWitnessedSync.SinceGenesis -> 1
            | FirstWitnessedSync.AtSync o -> SyncOrdinal.value o
            | FirstWitnessedSync.Unknown -> 1

        /// The rendered sync token — Unknown says "?" (never a
        /// fabricated ordinal); the healthy readings render the ordinal
        /// they always did (byte-identical).
        let text (f: FirstWitnessedSync) : string =
            match f with
            | FirstWitnessedSync.SinceGenesis -> "1"
            | FirstWitnessedSync.AtSync o -> SyncOrdinal.text o
            | FirstWitnessedSync.Unknown -> "?"

    type PhysicalClaim = {
        /// The claimant's ossys row id — the in-set discriminator (always
        /// present; key-basis honesty: the positional identity).
        EntityId : int
        /// The claimant's native identity (espace-safe) when the source
        /// supplied its SS_Key GUID; `None` = positional-only claimant.
        EntityKey : SsKey option
        EntityName : string
        ModuleName : string
        /// Lifecycle: false = a tombstone (`Is_Active = 0`; the physical
        /// table survives until DbCleaner).
        IsActive : bool
        /// The claim rides an extension module's external re-registration
        /// (`EspaceKind = 'Extension'`, `Is_External = 1` — the
        /// Integration-Studio path; `Origin = ExternalIndirect`).
        IsExternalRegistration : bool
        /// WHEN the journal first witnessed this claim — the temporal
        /// dimension ossys does not expose (align-II.10: a value, never a
        /// fabricated ordinal).
        FirstWitnessedSync : FirstWitnessedSync
    }

    /// The epistemic standing of a schema reading (align-III.13): OBSERVED
    /// schemas come from the estate's own metadata (the physical-table
    /// rowset, an INFORMATION_SCHEMA probe); ASSUMED schemas are defaults
    /// supplied where nothing was read (the OutSystems-standard `dbo` for
    /// an entity whose snapshot carries no physical-table row). The old
    /// shape DECLARED `"dbo"` as a constant at the same grain as the
    /// residue sweep's observed schema — one type, two epistemic standings,
    /// unmarked.
    [<RequireQualifiedAccess>]
    type SchemaBasis =
        | Observed of schema: string
        | Assumed of schema: string

    [<RequireQualifiedAccess>]
    module SchemaBasis =
        let schema (b: SchemaBasis) : string =
            match b with
            | SchemaBasis.Observed s | SchemaBasis.Assumed s -> s

        let isObserved (b: SchemaBasis) : bool =
            match b with SchemaBasis.Observed _ -> true | SchemaBasis.Assumed _ -> false

    /// The physical address at the grain reality has — the containment
    /// tower's lower floors (environment ⊃ database ⊃ schema ⊃ table).
    /// `Catalog` is `None` inside the connection's own database (the
    /// standing single-catalog posture; the field exists so a multi-catalog
    /// address is EXPRESSIBLE, not so it is common).
    type PhysicalTableRef = {
        Catalog : string option
        Schema  : SchemaBasis
        Table   : string
    }

    [<RequireQualifiedAccess>]
    module PhysicalTableRef =
        /// The grouping/subtraction identity: catalog + schema TEXT + table,
        /// case-folded the way SQL Server resolves names. The BASIS does not
        /// enter the identity — an observed and an assumed reading of one
        /// address are the SAME address; the basis is epistemic standing,
        /// not location.
        let key (r: PhysicalTableRef) : string * string * string =
            ((r.Catalog |> Option.defaultValue "").ToUpperInvariant(),
             (SchemaBasis.schema r.Schema).ToUpperInvariant(),
             r.Table.ToUpperInvariant())

        /// The display/diagnostic form — `schema.table`, catalog-prefixed
        /// only when a catalog is present (byte-identical to the prior
        /// `Schema.Table` rendering for the standing posture).
        let text (r: PhysicalTableRef) : string =
            match r.Catalog with
            | Some c -> System.String.Concat(c, ".", SchemaBasis.schema r.Schema, ".", r.Table) // LINT-ALLOW: terminal diagnostic projection at the rendering boundary; the typed ref IS the structure
            | None -> System.String.Concat(SchemaBasis.schema r.Schema, ".", r.Table) // LINT-ALLOW: terminal diagnostic projection at the rendering boundary; the typed ref IS the structure

        /// The observed-basis constructor the probes use; `assumedDbo` is
        /// the honest name for the old fabricated constant.
        let observed (schema: string) (table: string) : PhysicalTableRef =
            { Catalog = None; Schema = SchemaBasis.Observed schema; Table = table }

        let assumedDbo (table: string) : PhysicalTableRef =
            { Catalog = None; Schema = SchemaBasis.Assumed "dbo"; Table = table }

    /// One physical table and every claim the journal can assemble on it.
    type ClaimSet = {
        Ref : PhysicalTableRef
        Claims : PhysicalClaim list
    }

    /// The adjudication — closed, total. `Adopted` is lineage-only (the
    /// healthy case is not a finding); `Contested` and `TombstoneOnly` are
    /// DECIDE findings; `Unclaimed` is S12's detector.
    [<RequireQualifiedAccess>]
    type PhysicalClaimOutcome =
        /// One claim wins; the outranked rivals ride along for the lineage
        /// annotation (superseded editions, tombstones).
        | Adopted of winner: PhysicalClaim * outranked: PhysicalClaim list
        /// Two or more LIVE claims — the operator rules; never a silent
        /// pick. The rivals arrive LADDER-ORDERED (eSpace before extension,
        /// then latest first-witnessed sync, then row id): the head is the
        /// recommendation the finding leads with.
        | Contested of rivals: PhysicalClaim list
        /// Only tombstones claim the table — the estate the operator
        /// deleted is still addressable through the witnessed editions.
        | TombstoneOnly of tombstones: PhysicalClaim list
        /// No metadata claim at all (assembled-empty; live detection is
        /// the S12 residue sweep against sys.tables).
        | Unclaimed

    /// The live-claim tier: lower outranks. An active eSpace entity (0)
    /// outranks an active extension re-registration (1).
    let private tierOf (c: PhysicalClaim) : int =
        if c.IsExternalRegistration then 1 else 0

    /// The rival ordering inside a contest: tier, then the LATER
    /// first-witnessed sync, then the row id (deterministic).
    let private rank (c: PhysicalClaim) : int * int * int =
        tierOf c, -(FirstWitnessedSync.rank c.FirstWitnessedSync), c.EntityId

    /// Adjudicate one claim set. Total — every input shape lands on one
    /// closed outcome; the ladder's order IS the semantics (layer comments
    /// in the module docstring).
    let adjudicate (set: ClaimSet) : PhysicalClaimOutcome =
        match set.Claims with
        | [] -> PhysicalClaimOutcome.Unclaimed
        | claims ->
            let live, tombstones = claims |> List.partition (fun c -> c.IsActive)
            match live |> List.sortBy rank with
            | [] -> PhysicalClaimOutcome.TombstoneOnly tombstones
            | [ sole ] ->
                PhysicalClaimOutcome.Adopted (sole, claims |> List.filter (fun c -> c.EntityId <> sole.EntityId))
            | rivals ->
                // Two or more LIVE writers: never a silent pick — the
                // ordered rivals carry the recommendation at their head.
                PhysicalClaimOutcome.Contested rivals

    /// The outcome's stable machine token (finding keys, lineage payloads).
    let token (outcome: PhysicalClaimOutcome) : string =
        match outcome with
        | PhysicalClaimOutcome.Adopted _ -> "adopted"
        | PhysicalClaimOutcome.Contested _ -> "contested"
        | PhysicalClaimOutcome.TombstoneOnly _ -> "tombstoneOnly"
        | PhysicalClaimOutcome.Unclaimed -> "unclaimed"

    /// One claim's diagnostic clause — the ONE claim renderer both the
    /// outcome clauses and the correspondence clauses (S14) speak through.
    let private claimText (c: PhysicalClaim) =
        System.String.Concat( // LINT-ALLOW: terminal diagnostic projection at the rendering boundary; the typed PhysicalClaim IS the structure
            c.ModuleName, ".", c.EntityName,
            (if c.IsExternalRegistration then " (external re-registration)" else ""),
            (if c.IsActive then "" else " (tombstone)"),
            " @sync ", FirstWitnessedSync.text c.FirstWitnessedSync)

    /// The structured rendering — typed payload → the diagnostic clause a
    /// boundary consumer prints. Strings emerge only here (the
    /// `RemovalReason.toDiagnosticString` convention).
    let toStructured (set: ClaimSet) (outcome: PhysicalClaimOutcome) : (string * string) list =
        let table = PhysicalTableRef.text set.Ref
        [ "table", table
          "outcome", token outcome
          match outcome with
          | PhysicalClaimOutcome.Adopted (winner, outranked) ->
              "winner", claimText winner
              if not (List.isEmpty outranked) then
                  "outranked", (outranked |> List.map claimText |> String.concat "; ") // LINT-ALLOW: terminal diagnostic projection at the rendering boundary — joining already-rendered claim clauses for one structured field; the typed claim list IS the structure
          | PhysicalClaimOutcome.Contested rivals ->
              "rivals", (rivals |> List.map claimText |> String.concat "; ") // LINT-ALLOW: terminal diagnostic projection at the rendering boundary — joining already-rendered claim clauses for one structured field; the typed claim list IS the structure
          | PhysicalClaimOutcome.TombstoneOnly tombstones ->
              "tombstones", (tombstones |> List.map claimText |> String.concat "; ") // LINT-ALLOW: terminal diagnostic projection at the rendering boundary — joining already-rendered claim clauses for one structured field; the typed claim list IS the structure
          | PhysicalClaimOutcome.Unclaimed -> () ]

    // -- the cross-cutover identity correspondence (S14) ---------------------

    /// A cross-cutover identity correspondence PROPOSAL: the sole live
    /// claim on a table whose lineage carries tombstones reads as ONE
    /// identity continuing across a delete-then-re-register cutover (the
    /// External-Entities path the chapter opens with). A proposal is
    /// EVIDENCE for the operator's ruling — NEVER an adoption: the type
    /// carries the two claims and their native keys and can write nothing
    /// (no catalog passes through the proposer; no `SsKey` is minted —
    /// a ruled continuity would thread `SsKey.derivedFrom`/`V1Mapped` at
    /// the ruling's own hands, and the closed `DerivationReason` set
    /// widens THEN, not here).
    type CorrespondenceProposal = {
        Ref : PhysicalTableRef
        /// The tombstoned prior edition proposed as the same identity —
        /// the LATEST-witnessed tombstone when several ride the lineage
        /// (the edition nearest the cutover).
        From : PhysicalClaim
        /// The continuing (sole live) registration.
        To : PhysicalClaim
        /// The names agree case-insensitively (a re-import keeps the
        /// entity's name) — the corroborating signal; the shared physical
        /// table is the primary continuity carrier either way.
        SameName : bool
    }

    /// Propose at most one correspondence per adjudicated set — total.
    /// ONLY the Adopted-over-tombstones shape proposes: a contested table
    /// has no adjudicated continuation (the contest is its own finding),
    /// a tombstone-only table has nothing live to continue INTO, an
    /// unclaimed table has no claims at all, and a clean sole adoption
    /// (no tombstones) has no cutover to correspond across.
    let proposeCorrespondence (set: ClaimSet) (outcome: PhysicalClaimOutcome) : CorrespondenceProposal option =
        match outcome with
        | PhysicalClaimOutcome.Adopted (winner, outranked) ->
            outranked
            |> List.filter (fun c -> not c.IsActive)
            |> List.sortByDescending (fun c -> FirstWitnessedSync.rank c.FirstWitnessedSync, c.EntityId)
            |> List.tryHead
            |> Option.map (fun from ->
                { Ref = set.Ref
                  From = from
                  To = winner
                  SameName = System.String.Equals(from.EntityName, winner.EntityName, System.StringComparison.OrdinalIgnoreCase) })
        | PhysicalClaimOutcome.Contested _
        | PhysicalClaimOutcome.TombstoneOnly _
        | PhysicalClaimOutcome.Unclaimed -> None

    /// The proposal's structured rendering — the same
    /// typed-payload→clauses convention as `toStructured`.
    let correspondenceClauses (p: CorrespondenceProposal) : (string * string) list =
        [ "table", PhysicalTableRef.text p.Ref
          "from", claimText p.From
          "to", claimText p.To
          "sameName", (if p.SameName then "true" else "false") ]
