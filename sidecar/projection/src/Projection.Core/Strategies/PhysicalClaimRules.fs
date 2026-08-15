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
        /// The journal sync at which this claim was FIRST witnessed — the
        /// temporal dimension ossys does not expose. Higher = later edition.
        FirstWitnessedSync : int
    }

    /// One physical table and every claim the journal can assemble on it.
    type ClaimSet = {
        Schema : string
        Table : string
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
        tierOf c, -c.FirstWitnessedSync, c.EntityId

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

    /// The structured rendering — typed payload → the diagnostic clause a
    /// boundary consumer prints. Strings emerge only here (the
    /// `RemovalReason.toDiagnosticString` convention).
    let toStructured (set: ClaimSet) (outcome: PhysicalClaimOutcome) : (string * string) list =
        let table = System.String.Concat(set.Schema, ".", set.Table) // LINT-ALLOW: terminal diagnostic projection at the rendering boundary; the typed ClaimSet IS the structure
        let claimText (c: PhysicalClaim) =
            System.String.Concat( // LINT-ALLOW: terminal diagnostic projection at the rendering boundary; the typed PhysicalClaim IS the structure
                c.ModuleName, ".", c.EntityName,
                (if c.IsExternalRegistration then " (external re-registration)" else ""),
                (if c.IsActive then "" else " (tombstone)"),
                " @sync ", string c.FirstWitnessedSync)
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
