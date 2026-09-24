namespace Projection.Pipeline

open System.IO
open System.Text.Json
open Projection.Core
open FsToolkit.ErrorHandling

// `BridgeRetargetEvidence` — the DATA-half readiness facts for one retarget —
// moved to Core (`BridgeRetarget.fs`) when the dynamic bridge-row staging
// companion became a second producer: the pure delta kernel (`BridgeRowDelta`)
// derives the same record from live snapshots that the operator supplement file
// supplies here, and both feed `applyEvidence` below.

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
                        (String.concat "" [ "bridge retarget evidence field '"; key; "' must be a non-negative integer count" ])  // LINT-ALLOW: terminal refusal-text composition at the ValidationError boundary; segments are typed values, String.concat is the irreducible primitive for this free-text operator message — the TransformRegistry precedent
                | false, _ ->
                    err "pipeline.config.bridgeRetargetEvidence.nonIntegerCount"
                        (String.concat "" [ "bridge retarget evidence field '"; key; "' must be an integer count" ])  // LINT-ALLOW: terminal refusal-text composition at the ValidationError boundary; segments are typed values, String.concat is the irreducible primitive for this free-text operator message — the TransformRegistry precedent
            | _ ->
                err "pipeline.config.bridgeRetargetEvidence.countNotNumber"
                    (String.concat "" [ "bridge retarget evidence field '"; key; "' must be a JSON number" ])  // LINT-ALLOW: terminal refusal-text composition at the ValidationError boundary; segments are typed values, String.concat is the irreducible primitive for this free-text operator message — the TransformRegistry precedent

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
                            (String.concat "" [ "bridge retarget evidence 'identityEvidence' value '"; raw; "' is not recognized. Known: present | missing | ambiguous" ])  // LINT-ALLOW: terminal refusal-text composition at the ValidationError boundary; segments are typed values, String.concat is the irreducible primitive for this free-text operator message — the TransformRegistry precedent
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
                                    (String.concat "" [ "bridge retarget evidence has duplicate id(s): "; String.concat ", " dups ]))  // LINT-ALLOW: terminal refusal-text composition at the ValidationError boundary; segments are typed values, String.concat is the irreducible primitive for this free-text operator message — the TransformRegistry precedent
                    | _ ->
                        err "pipeline.config.bridgeRetargetEvidence.retargetsNotArray"
                            "the bridge retarget evidence file's 'retargets' must be an array"
        with :? JsonException as ex ->
            err "pipeline.config.bridgeRetargetEvidence.malformedJson"
                (String.concat "" [ "could not parse the bridge retarget evidence file as JSON: "; ex.Message ])  // LINT-ALLOW: terminal refusal-text composition at the ValidationError boundary; segments are typed values, String.concat is the irreducible primitive for this free-text operator message — the TransformRegistry precedent

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
                        (String.concat "" [ "could not read the bridge retarget evidence file at '"; o.Path; "': "; ex.Message ])  // LINT-ALLOW: terminal refusal-text composition at the ValidationError boundary; segments are typed values, String.concat is the irreducible primitive for this free-text operator message — the TransformRegistry precedent
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
    /// The synthesized retarget id for a DISCOVERED reference — the declared id
    /// plus the reference's own coordinate, so every expanded plan is separately
    /// addressable (evidence keying, artifacts, lineage) and the operator can read
    /// which FK each verdict belongs to. Deterministic: same model ⇒ same ids.
    let discoveredIdOf (declaredId: string) (moduleName: string) (kind: Kind) (reference: Reference) : string =
        String.concat "" [ declaredId; "/"; moduleName; "."; Name.value kind.Name; "."; Name.value reference.Name ]  // LINT-ALLOW: terminal identity composition for an operator-facing retarget id; no typed AST applies

    /// Resolve an entry's `reference` to the (retargetId, owning kind, reference)
    /// triples it names. `Explicit` yields exactly one (fail-closed: an entity or
    /// relationship not in the model is a NAMED refusal). `AllReferencesTo` is
    /// DISCOVERY: every reference in the resolved model scope whose target kind is
    /// the named entity — so "retarget every FK pointing at this table" needs no
    /// hand enumeration. Discovery that matches NOTHING is a refusal, not a silent
    /// no-op: the operator asked for all references to a table and there are none,
    /// which means the coordinate is wrong or the scope excludes them.
    let private resolveReferences
        (catalog: Catalog)
        (entry: Config.BridgeRetargetEntry)
        : Result<(string * Kind * Reference) list> =
        let kindsMatching (coord: EntityCoordinate) =
            Catalog.allModulesKinds catalog
            |> List.filter (fun (m, k) ->
                (coord.Module = "" || ciEq (Name.value m.Name) coord.Module)
                && ciEq (Name.value k.Name) coord.Entity)
            |> List.distinctBy (fun (_, k) -> k.SsKey)
        match entry.Reference with
        | Config.BridgeRetargetReference.Explicit (coord, relationship) ->
            match kindsMatching coord with
            | [] ->
                err "pipeline.config.bridgeRetargets.entity.notFound"
                    (String.concat "" [ "bridge retarget '"; entry.Id; "': entity "; coord.Module; "/"; coord.Entity; " is not in the model" ])  // LINT-ALLOW: terminal refusal-text composition at the ValidationError boundary; segments are typed values, String.concat is the irreducible primitive for this free-text operator message — the TransformRegistry precedent
            | _ :: _ :: _ ->
                err "pipeline.config.bridgeRetargets.entity.ambiguous"
                    (String.concat "" [ "bridge retarget '"; entry.Id; "': entity "; coord.Module; "/"; coord.Entity; " is ambiguous across the resolved scope" ])  // LINT-ALLOW: terminal refusal-text composition at the ValidationError boundary; segments are typed values, String.concat is the irreducible primitive for this free-text operator message — the TransformRegistry precedent
            | [ (_, kind) ] ->
                match kind.References |> List.tryFind (fun r -> ciEq (Name.value r.Name) relationship) with
                | None ->
                    err "pipeline.config.bridgeRetargets.relationship.notFound"
                        (String.concat "" [ "bridge retarget '"; entry.Id; "': relationship '"; relationship; "' is not a reference on "; coord.Entity ])  // LINT-ALLOW: terminal refusal-text composition at the ValidationError boundary; segments are typed values, String.concat is the irreducible primitive for this free-text operator message — the TransformRegistry precedent
                | Some reference -> Result.success [ entry.Id, kind, reference ]
        | Config.BridgeRetargetReference.AllReferencesTo target ->
            match kindsMatching target with
            | [] ->
                err "pipeline.config.bridgeRetargets.entity.notFound"
                    (String.concat "" [ "bridge retarget '"; entry.Id; "': reference.allReferencesTo entity "; target.Module; "/"; target.Entity; " is not in the model" ])  // LINT-ALLOW: terminal refusal-text composition at the ValidationError boundary; segments are typed values, String.concat is the irreducible primitive for this free-text operator message — the TransformRegistry precedent
            | _ :: _ :: _ ->
                err "pipeline.config.bridgeRetargets.entity.ambiguous"
                    (String.concat "" [ "bridge retarget '"; entry.Id; "': reference.allReferencesTo entity "; target.Module; "/"; target.Entity; " is ambiguous across the resolved scope" ])  // LINT-ALLOW: terminal refusal-text composition at the ValidationError boundary; segments are typed values, String.concat is the irreducible primitive for this free-text operator message — the TransformRegistry precedent
            | [ (_, targetKind) ] ->
                // Every reference in the RESOLVED scope (the catalog is already
                // narrowed by `model.modules`) whose target is that kind. Ordered by
                // the child kind's SsKey then the reference's, so expansion is
                // deterministic (the determinism-is-constructed discipline).
                let discovered =
                    Catalog.allModulesKinds catalog
                    |> List.collect (fun (m, childKind) ->
                        childKind.References
                        |> List.filter (fun r -> r.TargetKind = targetKind.SsKey)
                        |> List.map (fun r -> Name.value m.Name, childKind, r))
                    |> List.sortBy (fun (_, k, r) -> k.SsKey, r.SsKey)
                if List.isEmpty discovered then
                    err "pipeline.config.bridgeRetargets.discoveryEmpty"
                        (String.concat "" [ "bridge retarget '"; entry.Id; "': reference.allReferencesTo "; target.Module; "/"; target.Entity; " matched no references in the resolved model scope — check the coordinate and `model.modules`" ])  // LINT-ALLOW: terminal refusal-text composition at the ValidationError boundary; segments are typed values, String.concat is the irreducible primitive for this free-text operator message — the TransformRegistry precedent
                else
                    discovered
                    |> List.map (fun (moduleName, childKind, reference) ->
                        discoveredIdOf entry.Id moduleName childKind reference, childKind, reference)
                    |> Result.success

    /// Build the plan for ONE resolved (retargetId, kind, reference) triple. The
    /// profile is assembled from the catalog's STRUCTURAL facts plus the supplied
    /// `evidence` for the DATA facts (fail-closed defaults when no entry exists).
    let private buildPlan
        (catalog: Catalog)
        (evidence: Map<string, BridgeRetargetEvidence>)
        (entry: Config.BridgeRetargetEntry)
        (retargetId: string)
        (kind: Kind)
        (reference: Reference)
        : Result<BridgeRetargetPlan> =
        let sourceAttr = kind.Attributes |> List.tryFind (fun a -> a.SsKey = reference.SourceAttribute)
        match AttributeCoordinate.resolveFull catalog entry.Bridge with
        | Error _ ->
            err "pipeline.config.bridgeRetargets.bridge.notFound"
                (String.concat "" [ "bridge retarget '"; retargetId; "': bridge attribute "; entry.Bridge.Module; "/"; entry.Bridge.Entity; "/"; entry.Bridge.Attribute; " is not in the model" ])  // LINT-ALLOW: terminal refusal-text composition at the ValidationError boundary; segments are typed values, String.concat is the irreducible primitive for this free-text operator message — the TransformRegistry precedent
        | Ok (bridgeKindKey, _, bridgeAttrKey) ->
            let bridgeKind = Catalog.tryFindKind bridgeKindKey catalog
            let bridgeAttr =
                bridgeKind |> Option.bind (fun bk -> bk.Attributes |> List.tryFind (fun a -> a.SsKey = bridgeAttrKey))
            // Is a SINGLE-COLUMN unique index / constraint DECLARED on the bridge
            // key? SQL Server refuses `REFERENCES <bridge>(<col>)` without one, so
            // this gates the DEPLOY — a data-uniqueness count cannot substitute for
            // it. A COMPOSITE unique index does not qualify: a single-column FK
            // cannot target one member of a multi-column key.
            let declaredUnique =
                bridgeKind
                |> Option.map (fun bk ->
                    bk.Indexes
                    |> List.exists (fun ix ->
                        IndexUniqueness.isUnique ix.Uniqueness
                        && (match ix.Columns with
                            | [ single ] -> single.Attribute = bridgeAttrKey
                            | _ -> false)))
                |> Option.defaultValue false
            match sourceAttr, bridgeAttr with
            | Some sa, Some ba ->
                let existingConstraintTrusted =
                    if Reference.hasDbConstraint reference then Some (Reference.isConstraintTrusted reference)
                    else None
                let structural =
                    { BridgeRetargetProfile.unproven retargetId with
                        BridgeKeyPresent          = true
                        TargetsBridgePrimaryKey   = ba.IsPrimaryKey
                        KeyTypesMatch             = (sa.Type = ba.Type)
                        BridgeKeyDeclaredUnique   = declaredUnique
                        ExistingConstraintTrusted = existingConstraintTrusted }
                // The evidence supplement overrides the fail-closed DATA facts when
                // the operator supplied an entry for this id; absent, the retarget
                // stays blocked (byte-identical emission).
                let profile =
                    match Map.tryFind retargetId evidence with
                    | Some ev -> applyEvidence ev structural
                    | None    -> structural
                Result.success
                    { ReferenceKey       = reference.SsKey
                      BridgeAttributeKey = bridgeAttrKey
                      Profile            = profile }
            | None, _ ->
                err "pipeline.config.bridgeRetargets.sourceAttribute.missing"
                    (String.concat "" [ "bridge retarget '"; retargetId; "': the reference's source attribute is missing from its kind" ])  // LINT-ALLOW: terminal refusal-text composition at the ValidationError boundary; segments are typed values, String.concat is the irreducible primitive for this free-text operator message — the TransformRegistry precedent
            | _, None ->
                err "pipeline.config.bridgeRetargets.bridge.attributeMissing"
                    (String.concat "" [ "bridge retarget '"; retargetId; "': the resolved bridge attribute is missing from its kind" ])  // LINT-ALLOW: terminal refusal-text composition at the ValidationError boundary; segments are typed values, String.concat is the irreducible primitive for this free-text operator message — the TransformRegistry precedent

    /// Resolve one config entry to its plan(s) — one for an explicit reference, N
    /// for a discovery expansion.
    let private bindOne (catalog: Catalog) (evidence: Map<string, BridgeRetargetEvidence>) (entry: Config.BridgeRetargetEntry) : Result<BridgeRetargetPlan list> =
        resolveReferences catalog entry
        |> Result.bind (fun triples ->
            triples
            |> List.map (fun (rid, kind, reference) -> buildPlan catalog evidence entry rid kind reference)
            |> Result.aggregate)

    /// Bind every declared retarget against the supplied evidence map, accumulating
    /// named refusals. `[]` ⇒ `BridgeRetargetPolicy.empty` (byte-identical — the
    /// pass writes an empty map). Kept public so the pure binding (entries +
    /// evidence → policy) is unit-testable without the file-read boundary.
    let bindAll (catalog: Catalog) (evidence: Map<string, BridgeRetargetEvidence>) (entries: Config.BridgeRetargetEntry list) : Result<BridgeRetargetPolicy> =
        entries
        |> List.map (bindOne catalog evidence)
        |> Result.aggregate
        |> Result.map (fun perEntry -> { Plans = List.concat perEntry })

    /// Merge the two evidence sources — the operator FILE supplement and the
    /// staging companion's DERIVED planned-state evidence — refusing overlap: a
    /// retarget id claimed by BOTH would carry two competing accounts of the
    /// same data facts, and silently preferring either would be an unnamed
    /// downgrade. The operator resolves it by removing the file entry (the
    /// derived evidence is fresher) or the staging link. Public like `bindAll`
    /// — the pure merge is unit-testable without the file-read boundary.
    let mergeEvidence
        (fileEvidence: Map<string, BridgeRetargetEvidence>)
        (derived: Map<string, BridgeRetargetEvidence>)
        : Result<Map<string, BridgeRetargetEvidence>> =
        let overlap =
            derived
            |> Map.toList
            |> List.map fst
            |> List.filter (fun id -> Map.containsKey id fileEvidence)
        if List.isEmpty overlap then
            Result.success (Map.fold (fun m k v -> Map.add k v m) fileEvidence derived)
        else
            err "pipeline.config.bridgeRetargetEvidence.sourceConflict"
                (String.concat "" [ "retarget id(s) carry BOTH file evidence (`overrides.bridgeRetargetEvidence`) and staging-derived evidence (`overrides.bridgeRowStaging`): "; String.concat ", " overlap; " — remove one source; competing accounts of the same data facts are refused" ])  // LINT-ALLOW: terminal refusal-text composition at the ValidationError boundary; segments are typed values, String.concat is the irreducible primitive for this free-text operator message — the TransformRegistry precedent

    /// Bind `overrides.bridgeRetargets` into the typed `BridgeRetargetPolicy`,
    /// reading the DATA-half evidence from `overrides.bridgeRetargetEvidence.path`
    /// and merging the staging companion's DERIVED planned-state evidence
    /// (`derived` — per retarget id; overlap with the file is refused). A
    /// malformed / unreadable evidence file short-circuits before binding (a
    /// gate-level failure); otherwise each declared retarget is resolved against
    /// the catalog and the merged evidence. No entries + no evidence ⇒
    /// `BridgeRetargetPolicy.empty` (byte-identical emission).
    let fromConfigWith
        (derived: Map<string, BridgeRetargetEvidence>)
        (catalog: Catalog)
        (cfg: Config.Config)
        : Result<BridgeRetargetPolicy> =
        loadEvidence cfg.Overrides.BridgeRetargetEvidence
        |> Result.bind (fun fileEvidence -> mergeEvidence fileEvidence derived)
        |> Result.bind (fun evidence -> bindAll catalog evidence cfg.Overrides.BridgeRetargets)

    /// The no-derived-evidence form — callers with no staging companion in
    /// play (the `policy-diff` verb; the shaping-triple binder). Supplies the
    /// default the caller could not meaningfully compute (the F# default-
    /// argument idiom).
    let fromConfig (catalog: Catalog) (cfg: Config.Config) : Result<BridgeRetargetPolicy> =
        fromConfigWith Map.empty catalog cfg
