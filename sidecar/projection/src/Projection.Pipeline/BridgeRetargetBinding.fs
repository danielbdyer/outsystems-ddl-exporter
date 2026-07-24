namespace Projection.Pipeline

open System.IO
open System.Text.Json
open Projection.Core
open FsToolkit.ErrorHandling

/// Operator-supplied profiling evidence for ONE bridge retarget — the DATA-derived
/// readiness facts a catalog inspection cannot know (they need the actual data:
/// how values resolve through the bridge, the real uniqueness / null counts,
/// orphans, payload conflicts, and identity provenance). Read from
/// `overrides.bridgeRetargetEvidence.path`; the binder overrides a retarget's
/// fail-closed `unproven` data facts with these so the retarget can CLEAR. The
/// Graph auto-supplement (real tenant data) is a future source that would write
/// this same file.
type BridgeRetargetEvidence =
    { UnresolvedThroughBridge : int64
      BrokenOriginalParent    : int64
      OrphanedBridgeRows      : int64
      PayloadConflicts        : int64
      BridgeKeyDuplicates     : int64
      BridgeKeyNulls          : int64
      IdentityEvidence        : BridgeIdentityEvidence }

/// Binds `overrides.bridgeRetargets` (textual config) into the typed
/// `BridgeRetargetPolicy` the decision pass reads off `Policy`. Fail-closed: an
/// entity / relationship / bridge attribute the operator names that is not in the
/// model is a NAMED refusal (`pipeline.config.bridgeRetargets.*`), never a silent
/// skip.
///
/// **The evidence boundary (two lanes, one profile).** A retarget's readiness
/// profile has two halves:
///
///   * the STRUCTURAL half — the bridge attribute is present, whether it is the
///     bridge's primary key (a hazard), whether the source and bridge key TYPES
///     match, the reference's existing constraint trust — which the binder computes
///     directly from the catalog; and
///   * the DATA half — resolution coverage, actual uniqueness / nullness, orphans,
///     payload conflicts, identity evidence — which a catalog inspection CANNOT
///     know, because it needs the live data.
///
/// The DATA half starts at its FAIL-CLOSED default (`BridgeRetargetProfile
/// .unproven`), so a configured retarget is BLOCKED — `RetargetFk` stays empty,
/// emission byte-identical — until evidence proves it. The evidence arrives as an
/// operator-supplied SUPPLEMENT file (`overrides.bridgeRetargetEvidence.path`, the
/// `migrationDependencies.path` idiom): `loadEvidence` reads it into a per-id map,
/// and `applyEvidence` overrides each retarget's fail-closed data facts with the
/// supplied ones, so a retarget can CLEAR. A retarget with no matching evidence
/// entry stays blocked — the safe posture: a retarget never lands on the strength
/// of the catalog declaration alone. The Graph auto-supplement (real tenant data)
/// is a future source that would write this same file.
///
/// **Evidence file format (JSON, id-keyed).** Each entry's `id` matches a declared
/// retarget's `id`; every count field is optional and defaults FAIL-CLOSED (the
/// blocking counts default to `1`, so an omitted fact keeps the retarget blocked;
/// the warning-only counts default to `0`; `identityEvidence` defaults to
/// `missing`):
///
/// ```json
/// {
///   "retargets": [
///     {
///       "id": "user-createdby",
///       "unresolvedThroughBridge": 0,
///       "brokenOriginalParent": 0,
///       "orphanedBridgeRows": 0,
///       "payloadConflicts": 0,
///       "bridgeKeyDuplicates": 0,
///       "bridgeKeyNulls": 0,
///       "identityEvidence": "present"
///     }
///   ]
/// }
/// ```
///
/// **Fail loud, never silent (standing law §4).** No path ⇒ the empty evidence map
/// (every retarget stays blocked; byte-identical). A path that is set but
/// unreadable / malformed / carries a non-numeric count / an unrecognized identity
/// / a duplicate id is a NAMED failure (`pipeline.config.bridgeRetargetEvidence.*`)
/// — the operator declared the file, so we honor it strictly.
[<RequireQualifiedAccess>]
module BridgeRetargetBinding =

    let private err (code: string) (message: string) : Result<'a> =
        Result.failureOf (ValidationError.create code message)

    let private ciEq (a: string) (b: string) : bool =
        System.String.Equals(a, b, System.StringComparison.OrdinalIgnoreCase)

    // ------------------------------------------------------------------
    // The evidence supplement — file → per-id evidence map (fail-closed).
    // ------------------------------------------------------------------

    /// Read a required non-blank string property, or `None` (absent, wrong kind,
    /// JSON `null`, or whitespace). Mirrors `MigrationDependenciesBinding`'s
    /// null-safe reader under `<Nullable>enable</Nullable>`.
    let private tryNonBlankString (element: JsonElement) (key: string) : string option =
        match element.TryGetProperty(key) with
        | true, v when v.ValueKind = JsonValueKind.String ->
            match v.GetString() with
            | null -> None
            | s when System.String.IsNullOrWhiteSpace s -> None
            | s -> Some s
        | _ -> None

    /// Read one optional non-negative integer count. Absent / JSON `null` ⇒ the
    /// supplied fail-closed `fallback`; a present-but-non-integer / negative /
    /// non-number value is a NAMED refusal (the operator wrote a bad cell).
    let private readCount (element: JsonElement) (key: string) (fallback: int64) : Result<int64> =
        match element.TryGetProperty(key) with
        | false, _ -> Result.success fallback
        | true, v ->
            match v.ValueKind with
            | JsonValueKind.Null | JsonValueKind.Undefined -> Result.success fallback
            | JsonValueKind.Number ->
                match v.TryGetInt64() with
                | true, n when n >= 0L -> Result.success n
                | true, _ ->
                    err "pipeline.config.bridgeRetargetEvidence.negativeCount"
                        (String.concat "" [ "bridge retarget evidence field '"; key; "' must be a non-negative integer count" ])
                | false, _ ->
                    err "pipeline.config.bridgeRetargetEvidence.nonIntegerCount"
                        (String.concat "" [ "bridge retarget evidence field '"; key; "' must be an integer count" ])
            | _ ->
                err "pipeline.config.bridgeRetargetEvidence.countNotNumber"
                    (String.concat "" [ "bridge retarget evidence field '"; key; "' must be a JSON number" ])

    /// Read the optional `identityEvidence` tag. Absent / JSON `null` ⇒ the
    /// fail-closed `Missing` (a warning, never a block). A present value must be one
    /// of `present | missing | ambiguous` (case-insensitive); anything else is a
    /// NAMED refusal rather than a silent fallback.
    let private readIdentity (element: JsonElement) : Result<BridgeIdentityEvidence> =
        match element.TryGetProperty("identityEvidence") with
        | false, _ -> Result.success BridgeIdentityEvidence.Missing
        | true, v ->
            match v.ValueKind with
            | JsonValueKind.Null | JsonValueKind.Undefined -> Result.success BridgeIdentityEvidence.Missing
            | JsonValueKind.String ->
                match v.GetString() with
                | null -> Result.success BridgeIdentityEvidence.Missing
                | raw ->
                    let s = raw.Trim()
                    if   ciEq s "present"   then Result.success BridgeIdentityEvidence.Present
                    elif ciEq s "missing"   then Result.success BridgeIdentityEvidence.Missing
                    elif ciEq s "ambiguous" then Result.success BridgeIdentityEvidence.Ambiguous
                    else
                        err "pipeline.config.bridgeRetargetEvidence.identityUnrecognized"
                            (String.concat "" [ "bridge retarget evidence 'identityEvidence' value '"; raw; "' is not recognized. Known: present | missing | ambiguous" ])
            | _ ->
                err "pipeline.config.bridgeRetargetEvidence.identityNotString"
                    "bridge retarget evidence 'identityEvidence' must be a string (present | missing | ambiguous)"

    /// Parse one evidence entry to its `(id, evidence)` pair. Every count field is
    /// optional; the blocking counts (`unresolvedThroughBridge` /
    /// `brokenOriginalParent` / `bridgeKeyDuplicates` / `bridgeKeyNulls`) default to
    /// `1` so an OMITTED fact keeps the retarget blocked (fail-closed); the
    /// warning-only counts (`orphanedBridgeRows` / `payloadConflicts`) default to
    /// `0`; `identityEvidence` defaults to `missing`.
    let private parseEvidenceEntry (element: JsonElement) : Result<string * BridgeRetargetEvidence> =
        match tryNonBlankString element "id" with
        | None ->
            err "pipeline.config.bridgeRetargetEvidence.entryMissingId"
                "every bridge retarget evidence entry needs a non-blank string 'id' matching a declared retarget"
        | Some id ->
            validation {
                let! unresolved   = readCount element "unresolvedThroughBridge" 1L
                and! brokenParent = readCount element "brokenOriginalParent" 1L
                and! orphaned     = readCount element "orphanedBridgeRows" 0L
                and! conflicts    = readCount element "payloadConflicts" 0L
                and! dupes        = readCount element "bridgeKeyDuplicates" 1L
                and! nulls        = readCount element "bridgeKeyNulls" 1L
                and! identity     = readIdentity element
                return
                    id,
                    { UnresolvedThroughBridge = unresolved
                      BrokenOriginalParent    = brokenParent
                      OrphanedBridgeRows      = orphaned
                      PayloadConflicts        = conflicts
                      BridgeKeyDuplicates     = dupes
                      BridgeKeyNulls          = nulls
                      IdentityEvidence        = identity }
            }

    /// Parse the document text into the per-id evidence map. Root must be an object
    /// with an optional `retargets` array; a duplicate id is a NAMED refusal (a
    /// silent last-wins override of one retarget's evidence would be a footgun).
    let private parseEvidenceDocument (text: string) : Result<Map<string, BridgeRetargetEvidence>> =
        try
            use doc = JsonDocument.Parse(text)
            let root = doc.RootElement
            if root.ValueKind <> JsonValueKind.Object then
                err "pipeline.config.bridgeRetargetEvidence.rootNotObject"
                    "the bridge retarget evidence file must be a JSON object with a 'retargets' array"
            else
                match root.TryGetProperty("retargets") with
                | false, _ -> Result.success Map.empty
                | true, v ->
                    match v.ValueKind with
                    | JsonValueKind.Null | JsonValueKind.Undefined -> Result.success Map.empty
                    | JsonValueKind.Array ->
                        v.EnumerateArray()
                        |> Seq.map parseEvidenceEntry
                        |> Result.aggregate
                        |> Result.bind (fun pairs ->
                            let dups =
                                pairs
                                |> List.countBy fst
                                |> List.filter (fun (_, n) -> n > 1)
                                |> List.map fst
                            if List.isEmpty dups then Result.success (Map.ofList pairs)
                            else
                                err "pipeline.config.bridgeRetargetEvidence.duplicateId"
                                    (String.concat "" [ "bridge retarget evidence has duplicate id(s): "; String.concat ", " dups ]))
                    | _ ->
                        err "pipeline.config.bridgeRetargetEvidence.retargetsNotArray"
                            "the bridge retarget evidence file's 'retargets' must be an array"
        with :? JsonException as ex ->
            err "pipeline.config.bridgeRetargetEvidence.malformedJson"
                (String.concat "" [ "could not parse the bridge retarget evidence file as JSON: "; ex.Message ])

    /// Read `overrides.bridgeRetargetEvidence.path` into the per-id evidence map.
    /// No path ⇒ the empty map (every retarget stays blocked; byte-identical). A
    /// present-but-unreadable path fails loud (`pipeline.config
    /// .bridgeRetargetEvidence.readFailed`).
    let loadEvidence (over: Config.FilePathOverride option) : Result<Map<string, BridgeRetargetEvidence>> =
        match over with
        | None -> Result.success Map.empty
        | Some o ->
            let textR =
                try Result.success (File.ReadAllText o.Path)
                with ex ->
                    err "pipeline.config.bridgeRetargetEvidence.readFailed"
                        (String.concat "" [ "could not read the bridge retarget evidence file at '"; o.Path; "': "; ex.Message ])
            textR |> Result.bind parseEvidenceDocument

    /// Override a profile's FAIL-CLOSED data facts with the supplied evidence — the
    /// DATA half of the profile the binder cannot compute from the catalog. The
    /// STRUCTURAL facts (`BridgeKeyPresent` / `TargetsBridgePrimaryKey` /
    /// `KeyTypesMatch` / `ExistingConstraintTrusted`) are left untouched; only the
    /// data-derived counts + identity move. `TrustedConstraintPossible` is DERIVED
    /// from the evidence (a trusted constraint is achievable exactly when every
    /// in-scope value resolves and no bridge key is NULL — the two data conditions
    /// that would otherwise force a permanent `WITH NOCHECK`), so it can never
    /// contradict the coverage/nullness facts it is computed from.
    let applyEvidence (evidence: BridgeRetargetEvidence) (profile: BridgeRetargetProfile) : BridgeRetargetProfile =
        { profile with
            UnresolvedThroughBridgeCount = evidence.UnresolvedThroughBridge
            BrokenOriginalParentCount    = evidence.BrokenOriginalParent
            OrphanedBridgeRowCount       = evidence.OrphanedBridgeRows
            PayloadConflictCount         = evidence.PayloadConflicts
            BridgeKeyDuplicateCount      = evidence.BridgeKeyDuplicates
            BridgeKeyNullCount           = evidence.BridgeKeyNulls
            IdentityEvidence             = evidence.IdentityEvidence
            TrustedConstraintPossible    = (evidence.UnresolvedThroughBridge = 0L && evidence.BridgeKeyNulls = 0L) }

    // ------------------------------------------------------------------
    // The config binding — declared retarget → resolved plan (fail-closed).
    // ------------------------------------------------------------------

    /// Resolve one config entry to a `BridgeRetargetPlan` (fail-closed). The profile
    /// is assembled from the catalog's STRUCTURAL facts plus the supplied `evidence`
    /// for the DATA facts (its fail-closed defaults when no matching entry exists).
    let private bindOne (catalog: Catalog) (evidence: Map<string, BridgeRetargetEvidence>) (entry: Config.BridgeRetargetEntry) : Result<BridgeRetargetPlan> =
        // Resolve the FK to retarget: the owning kind (entity coordinate) + the
        // reference (relationship name) on it.
        let kindMatches =
            Catalog.allModulesKinds catalog
            |> List.filter (fun (m, k) ->
                (entry.Entity.Module = "" || ciEq (Name.value m.Name) entry.Entity.Module)
                && ciEq (Name.value k.Name) entry.Entity.Entity)
            |> List.map snd
            |> List.distinctBy (fun k -> k.SsKey)
        match kindMatches with
        | [] ->
            err "pipeline.config.bridgeRetargets.entity.notFound"
                (String.concat "" [ "bridge retarget '"; entry.Id; "': entity "; entry.Entity.Module; "/"; entry.Entity.Entity; " is not in the model" ])
        | _ :: _ :: _ ->
            err "pipeline.config.bridgeRetargets.entity.ambiguous"
                (String.concat "" [ "bridge retarget '"; entry.Id; "': entity "; entry.Entity.Module; "/"; entry.Entity.Entity; " is ambiguous across the resolved scope" ])
        | [ kind ] ->
            match kind.References |> List.tryFind (fun r -> ciEq (Name.value r.Name) entry.Relationship) with
            | None ->
                err "pipeline.config.bridgeRetargets.relationship.notFound"
                    (String.concat "" [ "bridge retarget '"; entry.Id; "': relationship '"; entry.Relationship; "' is not a reference on "; entry.Entity.Entity ])
            | Some reference ->
                let sourceAttr = kind.Attributes |> List.tryFind (fun a -> a.SsKey = reference.SourceAttribute)
                match AttributeCoordinate.resolveFull catalog entry.Bridge with
                | Error _ ->
                    err "pipeline.config.bridgeRetargets.bridge.notFound"
                        (String.concat "" [ "bridge retarget '"; entry.Id; "': bridge attribute "; entry.Bridge.Module; "/"; entry.Bridge.Entity; "/"; entry.Bridge.Attribute; " is not in the model" ])
                | Ok (bridgeKindKey, _, bridgeAttrKey) ->
                    let bridgeAttr =
                        Catalog.tryFindKind bridgeKindKey catalog
                        |> Option.bind (fun bk -> bk.Attributes |> List.tryFind (fun a -> a.SsKey = bridgeAttrKey))
                    match sourceAttr, bridgeAttr with
                    | Some sa, Some ba ->
                        // Structural catalog facts. The data facts come from the
                        // supplied evidence (below) or stay fail-closed (unproven).
                        let existingConstraintTrusted =
                            if Reference.hasDbConstraint reference then Some (Reference.isConstraintTrusted reference)
                            else None
                        let structural =
                            { BridgeRetargetProfile.unproven entry.Id with
                                BridgeKeyPresent          = true
                                TargetsBridgePrimaryKey   = ba.IsPrimaryKey
                                KeyTypesMatch             = (sa.Type = ba.Type)
                                ExistingConstraintTrusted = existingConstraintTrusted }
                        // The evidence supplement overrides the fail-closed DATA
                        // facts when the operator supplied an entry for this id;
                        // absent, the retarget stays blocked (byte-identical).
                        let profile =
                            match Map.tryFind entry.Id evidence with
                            | Some ev -> applyEvidence ev structural
                            | None    -> structural
                        Result.success
                            { ReferenceKey       = reference.SsKey
                              BridgeAttributeKey = bridgeAttrKey
                              Profile            = profile }
                    | None, _ ->
                        err "pipeline.config.bridgeRetargets.sourceAttribute.missing"
                            (String.concat "" [ "bridge retarget '"; entry.Id; "': the reference's source attribute is missing from its kind" ])
                    | _, None ->
                        err "pipeline.config.bridgeRetargets.bridge.attributeMissing"
                            (String.concat "" [ "bridge retarget '"; entry.Id; "': the resolved bridge attribute is missing from its kind" ])

    /// Bind every declared retarget against the supplied evidence map, accumulating
    /// named refusals. `[]` ⇒ `BridgeRetargetPolicy.empty` (byte-identical — the
    /// pass writes an empty map). Kept public so the pure binding (entries +
    /// evidence → policy) is unit-testable without the file-read boundary.
    let bindAll (catalog: Catalog) (evidence: Map<string, BridgeRetargetEvidence>) (entries: Config.BridgeRetargetEntry list) : Result<BridgeRetargetPolicy> =
        entries
        |> List.map (bindOne catalog evidence)
        |> Result.aggregate
        |> Result.map (fun plans -> { Plans = plans })

    /// Bind `overrides.bridgeRetargets` into the typed `BridgeRetargetPolicy`,
    /// reading the DATA-half evidence from `overrides.bridgeRetargetEvidence.path`
    /// first. A malformed / unreadable evidence file short-circuits before binding
    /// (a gate-level failure); otherwise each declared retarget is resolved against
    /// the catalog and the supplied evidence. No entries + no evidence ⇒
    /// `BridgeRetargetPolicy.empty` (byte-identical emission).
    let fromConfig (catalog: Catalog) (cfg: Config.Config) : Result<BridgeRetargetPolicy> =
        loadEvidence cfg.Overrides.BridgeRetargetEvidence
        |> Result.bind (fun evidence -> bindAll catalog evidence cfg.Overrides.BridgeRetargets)
