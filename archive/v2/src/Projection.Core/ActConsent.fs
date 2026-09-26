namespace Projection.Core

// LINT-ALLOW-FILE: the act-consent vocabulary composes operator-facing
//   statements of what each destructive / creative act DOES (the board's
//   consent axis and the workbench's bless surface read them), exactly as
//   `WriteSignoff.impactOf` / `SupportingScope.guaranteeOf` do. THE_VOICE
//   register: complete sentences, the precise mechanism named, evidence
//   beneath. The module is pure — callers supply the plan and the substrate.

/// THE ACT CONSENT ALPHABET (2026-07-10, the transfer-manifest program,
/// slice 4a — THE_TRANSFER_MANIFEST.md §6): the CLOSED set of destructive and
/// creative acts a peer transfer performs, each individually nameable,
/// fingerprintable, and blessable. The mode-level `WriteSignoff` greenlights a
/// CLASS of act ("replace is approved"); this vocabulary names each INSTANCE
/// ("the wipe of Sales.Customer, at this exact population") so consent attaches
/// to what the run will actually do, not to a category.
///
/// Two-traversal discipline: `actsOf` is THE derivation — the go board's
/// consent axis and the engine's execute gate (slice 4b) both call it over the
/// same plan, so the blessed set and the performed set cannot drift.
///
/// Crypto-in-Core precedent: `RowDigester.hashRowBytes` (PhysicalSchema.fs) —
/// deterministic SHA-256 over a canonical separator-joined stream.
[<RequireQualifiedAccess>]
module ActConsent =

    // -- the closed act alphabet ------------------------------------------------

    /// One destructive or creative act a transfer performs. Closed so
    /// enumerate + bless + verify stay total — a new act kind is a compiler
    /// event that forces its token arm, its statement arm, and its
    /// fingerprint derivation.
    [<RequireQualifiedAccess>]
    type Act =
        /// Child-first DELETE of one table's rows before the reload (WipeAndLoad).
        | Wipe of table: SsKey
        /// Explicit source primary keys written under SET IDENTITY_INSERT.
        | IdentityInsert of table: SsKey
        /// A convergent MERGE arm deletes target rows absent from the source.
        /// In the DU for closure; the peer materialized path never emits it —
        /// `actsOf` returns none (the emission lane owns delete-scope).
        | DeleteScope of table: SsKey
        /// The target mints fresh surrogate keys for the rows inserted.
        | Mint of table: SsKey
        /// One FK column re-points from source keys to the keys the target
        /// minted (or matched) for the same parent rows.
        | Rekey of owner: SsKey * fkColumn: Name
        /// A reconciled kind: no rows written; each source row matches a row
        /// the target already holds, and references re-key onto the target's own keys.
        | Match of target: SsKey
        /// An escaping-reference decision recorded for a target outside the
        /// transfer — the resolution identity (the selected answer) is the act.
        | Resolve of target: SsKey * resolution: string
        /// Rows whose reference resolves to no target row are left out of the load.
        | Drop of owner: SsKey * fkColumn: Name

    /// The canonical config/report token for an act. `nameOf` renders a kind's
    /// legible label (the face passes `Module.Entity`; Core stays pure and
    /// name-policy-free). Total; deterministic; the sort key for every act list.
    let tokenOf (nameOf: SsKey -> string) (act: Act) : string =
        match act with
        | Act.Wipe t            -> "wipe:" + nameOf t
        | Act.IdentityInsert t  -> "identity-insert:" + nameOf t
        | Act.DeleteScope t     -> "delete-scope:" + nameOf t
        | Act.Mint t            -> "mint:" + nameOf t
        | Act.Rekey (o, c)      -> "rekey:" + nameOf o + "." + Name.value c
        | Act.Match t           -> "match:" + nameOf t
        | Act.Resolve (t, res)  -> "resolve:" + nameOf t + "=" + res
        | Act.Drop (o, c)       -> "drop:" + nameOf o + "." + Name.value c

    /// The severity ORDER of the act alphabet — the refusal's "most
    /// consequential" lever and the ledger's sort tiebreak. Lower is more
    /// severe: irreversible row loss first (the wipe), then explicit-key
    /// collision, convergent deletion, dropped rows, and only then the
    /// creative / re-pointing acts. Total over the closed DU.
    let severity (act: Act) : int =
        match act with
        | Act.Wipe _           -> 0
        | Act.IdentityInsert _ -> 1
        | Act.DeleteScope _    -> 2
        | Act.Drop _           -> 3
        | Act.Mint _           -> 4
        | Act.Rekey _          -> 5
        | Act.Resolve _        -> 6
        | Act.Match _          -> 7

    /// The operator-facing statement of what one act DOES — a complete
    /// sentence naming the precise mechanism (THE_VOICE). The board's consent
    /// axis and the workbench's bless surface echo this beside the token, so
    /// the operator reads exactly what a blessing approves.
    let describe (nameOf: SsKey -> string) (act: Act) : string =
        match act with
        | Act.Wipe t ->
            sprintf "Every row of %s on the target is deleted child-first before the reload — a target row absent from the source is removed, not preserved." (nameOf t)
        | Act.IdentityInsert t ->
            sprintf "Source primary-key values are written directly into %s's identity column under SET IDENTITY_INSERT — a key the target already minted for its own row can collide." (nameOf t)
        | Act.DeleteScope t ->
            sprintf "A convergent MERGE arm deletes every %s row on the target that the source does not carry." (nameOf t)
        | Act.Mint t ->
            sprintf "The target mints a new primary key for every %s row inserted — the transferred rows arrive under fresh identities, and every reference to them re-points." (nameOf t)
        | Act.Rekey (o, c) ->
            sprintf "%s.%s is re-pointed row by row: the source's key value is replaced with the key the target holds for the same parent row." (nameOf o) (Name.value c)
        | Act.Match t ->
            sprintf "No %s rows are written — each source row is matched to a row the target already holds, and references re-key onto the target's own keys." (nameOf t)
        | Act.Resolve (t, res) ->
            sprintf "References that point outside the transfer into %s follow the recorded decision %s." (nameOf t) res
        | Act.Drop (o, c) ->
            sprintf "Every %s row whose %s value matches no target row is left out of the load — the row does not arrive, and the count is reported." (nameOf o) (Name.value c)

    // -- fingerprints -------------------------------------------------------------

    /// What a blessing binds to. `Population` pins the exact target population
    /// an act consumes (a wipe: the rows that will be deleted); `Effect` pins
    /// the exact computed outcome (a match/re-key: the SHA-256 of the canonical
    /// effect stream). Either way a substrate change re-opens the act — a
    /// blessing can never rubber-stamp a different reality than the one read.
    [<RequireQualifiedAccess>]
    type ActFingerprint =
        /// The population's boundary and count: first primary key, last
        /// primary key (ordinal order), raw row count.
        | Population of firstKey: string * lastKey: string * rowCount: int
        /// Lowercase 64-hex SHA-256 of the act's canonical effect stream.
        | Effect of sha256Hex: string

    /// Render a fingerprint to its one config/report text. Population key
    /// values are percent-escaped so the `:`-separated form stays parseable
    /// under any key content; `parseFingerprint` is the exact inverse.
    let fingerprintText (fp: ActFingerprint) : string =
        match fp with
        | ActFingerprint.Population (first, last, count) ->
            sprintf "population:%s:%s:%d"
                (System.Uri.EscapeDataString first) (System.Uri.EscapeDataString last) count
        | ActFingerprint.Effect hex -> "effect:" + hex

    let private isHex64 (s: string) : bool =
        s.Length = 64
        && s |> Seq.forall (fun ch -> (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f'))

    /// Parse the one fingerprint text form. Total: anything outside the two
    /// closed shapes (`population:<first>:<last>:<count>` / `effect:<hex64>`,
    /// lowercase hex only — a blessing is copied, never re-cased) is `None`.
    let parseFingerprint (s: string) : ActFingerprint option =
        match s with
        | _ when s.StartsWith "effect:" ->
            let hex = s.Substring "effect:".Length
            if isHex64 hex then Some (ActFingerprint.Effect hex) else None
        | _ when s.StartsWith "population:" ->
            let rest = s.Substring "population:".Length
            match rest.Split ':' with
            | [| first; last; countText |] ->
                match System.Int32.TryParse(countText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture) with
                | true, count ->
                    Some (ActFingerprint.Population
                            (System.Uri.UnescapeDataString first, System.Uri.UnescapeDataString last, count))
                | _ -> None
            | _ -> None
        | _ -> None

    // -- the canonical effect stream ------------------------------------------------

    /// THE canonical rendering of one substrate value inside the effect
    /// stream — one renderer, property-tested alone (manifest §5.2). A string
    /// is itself, byte-for-byte: no case folding, no trimming, no culture —
    /// the substrate readers already render SQL NULL as "" upstream, so every
    /// value here is a total string and the identity IS the canonical form.
    let canonicalValue (v: string) : string = v

    /// The out-of-band ABSENT marker: stands where an effect record has no
    /// resolved target (an unmatched value). It embeds NUL, which no SQL-read
    /// string carries, so it aliases no real `canonicalValue` output.
    let absent : string = "\u0000"

    /// The typed effect substrate one act hashes — constructed by the caller
    /// from the evidence cache's ONE authoritative pass (`EvidenceCache
    /// .AnswerEvidence`: the matched pairs, the unmatched values, the exact
    /// sink-uniqueness counts). Never re-derived at hash time: the fingerprint
    /// is a projection of the same read the forecast is.
    type EffectSubstrate =
        { /// The act's canonical token — the stream header's identity.
          Token           : string
          /// The resolution identity (the selected answer, e.g. "reconcile:NAME").
          Resolution      : string
          /// (business-key value, resolved target identity) per matched row.
          MatchedPairs    : (string * string) list
          /// Business-key values with no target match — each one a reference
          /// that cannot re-key. Part of the effect: a new unmatched value is
          /// a different outcome and must re-open the act.
          UnmatchedValues : string list
          /// The target's exact (total, distinct) non-null counts on the match
          /// column — a duplicate appearing on the target re-opens the act.
          SinkTotal       : int64
          SinkDistinct    : int64
          /// The rows the plan writes / re-keys under this act.
          PlannedCount    : int }

    [<Literal>]
    let private us = "\u001f"   // unit separator: fields within a record

    [<Literal>]
    let private rs = "\u001e"   // record separator: records within the stream

    /// The deterministic effect hash: header (token, resolution), one record
    /// per matched pair (`bizKey US resolvedTarget`) and one per unmatched
    /// value (`bizKey US NUL`), records sorted ordinally; trailer (sink total,
    /// sink distinct, planned count); RS-joined, UTF-8, SHA-256, lowercase
    /// hex. Same substrate → same hash; any changed pair, new unmatched value,
    /// duplicate on the target, or count drift → a different hash, so the
    /// blessing re-opens (`RowDigester.hashRowBytes` is the house precedent).
    let effectFingerprint (s: EffectSubstrate) : ActFingerprint =
        let records =
            (s.MatchedPairs |> List.map (fun (k, v) -> canonicalValue k + us + canonicalValue v))
            @ (s.UnmatchedValues |> List.map (fun k -> canonicalValue k + us + absent))
            |> List.sortWith (fun a b -> System.String.CompareOrdinal(a, b))
        let header = s.Token + us + s.Resolution
        let trailer =
            string s.SinkTotal + us + string s.SinkDistinct + us
            + s.PlannedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
        let stream = String.concat rs (header :: records @ [ trailer ])
        let bytes = System.Text.Encoding.UTF8.GetBytes stream
        let hash = System.Security.Cryptography.SHA256.HashData(System.ReadOnlySpan<byte>(bytes))
        ActFingerprint.Effect ((System.Convert.ToHexString hash).ToLowerInvariant())

    /// A population fingerprint from the probed boundary facts (the face's
    /// MIN/MAX-pk + COUNT probe on the target table the act consumes).
    let populationFingerprint (firstKey: string) (lastKey: string) (rowCount: int) : ActFingerprint =
        ActFingerprint.Population (firstKey, lastKey, rowCount)

    // -- the one act derivation -----------------------------------------------------

    /// The kinds a WipeAndLoad DELETEs, child-first — THE derivation
    /// (relocated from `TransferResume.wipeTargets` 2026-07-10, slice 4a, so
    /// the act alphabet and the wipe realization share one body; the old name
    /// delegates). The wipe never touches (1) a `ReconciledByRule` kind — its
    /// sink rows are the sink's OWN, matched by business key — or (2) a kind
    /// outside `loadSet`. `loadSet = None` wipes every non-reconciled loaded kind.
    let wipeTargets (plan: DataLoadPlan) (topo: TopologicalOrder) (loadSet: Set<SsKey> option) : SsKey list =
        let loaded =
            plan.Loads
            |> List.filter (fun l -> l.Disposition <> IdentityDisposition.ReconciledByRule)
            |> List.map (fun l -> l.Kind)
            |> Set.ofList
        let inScope =
            match loadSet with
            | Some ls -> Set.intersect loaded ls
            | None    -> loaded
        List.rev topo.Order |> List.filter (fun k -> Set.contains k inScope)

    /// The kinds a load writes with explicit source keys under SET
    /// IDENTITY_INSERT — a `PreservedFromSource` load onto an IDENTITY-PK kind
    /// (relocated from `TransferRun.identityInsertTables` 2026-07-10; the old
    /// name delegates through `identityInsertTables` below).
    let identityInsertKinds (catalog: Catalog) (plan: DataLoadPlan) : SsKey list =
        plan.Loads
        |> List.choose (fun load ->
            if load.Disposition = IdentityDisposition.PreservedFromSource then
                match Catalog.tryFindKind load.Kind catalog with
                | Some kind when kind.Attributes |> List.exists (fun a -> a.IsPrimaryKey && a.IsIdentity) ->
                    Some kind.SsKey
                | _ -> None
            else None)
        |> List.distinct

    /// The IDENTITY-INSERT logical table names, sorted — byte-identical to the
    /// pre-relocation `TransferRun.identityInsertTables` (its delegation body).
    let identityInsertTables (catalog: Catalog) (plan: DataLoadPlan) : string list =
        identityInsertKinds catalog plan
        |> List.choose (fun k -> Catalog.tryFindKind k catalog |> Option.map (fun kind -> Name.value kind.Name))
        |> List.distinct
        |> List.sort

    /// THE act derivation (§6.2): every destructive / creative act one
    /// materialized peer run performs, from the same plan the run executes.
    /// Pure, total, sorted by canonical token — the go board's consent axis
    /// and the engine's execute gate (slice 4b) read the identical list.
    ///
    /// Arms and their sources — each plan-derived, never re-probed:
    ///  Wipe            — `wipeTargets` under WipeAndLoad (none on Incremental);
    ///  IdentityInsert  — `identityInsertKinds` (PreservedFromSource × IDENTITY PK);
    ///  Mint            — an `AssignedBySink` load with rows to insert;
    ///  Rekey           — a loaded kind's FK column whose target kind is minted
    ///                    (the write re-points it through the capture), plus the
    ///                    cycle-deferred columns phase 2 re-points;
    ///  Match           — each reconciled kind (rows matched, never written);
    ///  Drop            — each (owner, column) the plan build dropped rows on
    ///                    (`SkippedReferences`);
    ///  DeleteScope     — never on this path (the emission lane owns it);
    ///  Resolve         — recorded decisions, appended by the caller that holds
    ///                    them (the workbench); the plan does not carry them.
    let actsOf
        (nameOf: SsKey -> string)
        (catalog: Catalog)
        (plan: DataLoadPlan)
        (topo: TopologicalOrder)
        (loadSet: Set<SsKey> option)
        (reconciled: Set<SsKey>)
        (emission: EmissionMode)
        : Act list =
        let wipes =
            match emission with
            | EmissionMode.WipeAndLoad -> wipeTargets plan topo loadSet |> List.map Act.Wipe
            | _ -> []
        let identityInserts =
            identityInsertKinds catalog plan |> List.map Act.IdentityInsert
        let minted =
            plan.Loads
            |> List.filter (fun l -> l.Disposition = IdentityDisposition.AssignedBySink && not (List.isEmpty l.Rows))
            |> List.map (fun l -> l.Kind)
        let mintedSet = Set.ofList minted
        let mints = minted |> List.map Act.Mint
        let rekeys =
            plan.Loads
            |> List.collect (fun l ->
                let deferred = l.DeferredFkColumns |> Set.toList |> List.map (fun c -> Act.Rekey (l.Kind, c))
                let repointed =
                    match Catalog.tryFindKind l.Kind catalog with
                    | Some kind when not (List.isEmpty l.Rows) ->
                        kind.References
                        |> List.filter (fun r -> Set.contains r.TargetKind mintedSet && r.TargetKind <> l.Kind)
                        |> List.map (fun r -> Act.Rekey (l.Kind, r.Name))
                    | _ -> []
                deferred @ repointed)
        let matches =
            reconciled |> Set.toList |> List.map Act.Match
        let drops =
            plan.SkippedReferences
            |> List.map (fun (owner, r) -> Act.Drop (owner, r.Column))
        (wipes @ identityInserts @ mints @ rekeys @ matches @ drops)
        |> List.distinct
        |> List.sortWith (fun a b -> System.String.CompareOrdinal(tokenOf nameOf a, tokenOf nameOf b))
