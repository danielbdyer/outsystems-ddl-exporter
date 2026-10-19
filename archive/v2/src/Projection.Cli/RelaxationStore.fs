module Projection.Cli.RelaxationStore
// LINT-ALLOW-FILE-MUTATION: sealed function-local mutable accumulator in the relaxation-store merge; returned immutably

open System.IO
open System.Text.Json
open System.Text.Json.Nodes

/// The durable home of operator-blessed tightening relaxations: a
/// `tighteningRelaxations` string array in `projection.json` (the `kind.column`
/// keys from `Preflight.violationKey`). Relax-ALWAYS writes here; a later
/// HEADLESS migrate reads here to honor the blessing without prompting (the
/// A44-reachable equivalent of the interactive choice — a future run is total
/// because the override is now a named, persisted exception).
///
/// Written by a SURGICAL JSON merge: every other key in `projection.json` is
/// preserved byte-for-byte. The config doctrine is that the parser ignores
/// unknown keys, so this rides ALONGSIDE the movement vocabulary without
/// touching `renderConfig` or its `parse ∘ render = id` round-trip (A44).
/// (Making `renderConfig` itself preserve it is the broader audit-F7 fix.)

[<Literal>]
let private key = "tighteningRelaxations"

/// The configured `projection.json` path (`PROJECTION_CONFIG`, else the default).
let configPath () : string =
    match System.Environment.GetEnvironmentVariable "PROJECTION_CONFIG" with
    | null | "" -> "projection.json"
    | p -> p

let private rootObjectOf (path: string) : JsonObject option =
    if not (File.Exists path) then None
    else
        try
            match JsonNode.Parse(File.ReadAllText path) with
            | :? JsonObject as o -> Some o
            | _ -> None
        // Malformed JSON or a file-read failure → no blessing; a fatal / an
        // unexpected bug propagates rather than masquerading as "no blessing".
        with
        | :? System.Text.Json.JsonException
        | :? System.IO.IOException
        | :? System.UnauthorizedAccessException -> None

/// The blessed relaxation keys recorded in `path` — empty when the file or the
/// section is absent / malformed (a missing blessing is simply no blessing).
let read (path: string) : Set<string> =
    match rootObjectOf path with
    | None -> Set.empty
    | Some o ->
        match o.TryGetPropertyValue key with
        | true, arrNode ->
            match arrNode with
            | :? JsonArray as a ->
                a
                |> Seq.choose (fun n ->
                    match n with
                    | :? JsonValue as v ->
                        match v.TryGetValue<string>() with
                        | true, s -> Option.ofObj s
                        | _ -> None
                    | _ -> None)
                |> Set.ofSeq
            | _ -> Set.empty
        | _ -> Set.empty

/// Add `keys` to `path`'s relaxation section (deduped + sorted), preserving every
/// other key byte-for-byte. Creates the file as a minimal object only if absent.
/// `Ok ()` on a successful write; `Error cause` names the failure rather than
/// swallowing it to a bare `false` ("downgrades never silent" — recon #25).
let persist (path: string) (keys: string seq) : Result<unit, string> =
    try
        let root =
            match rootObjectOf path with
            | Some o -> o
            | None when File.Exists path -> JsonObject()   // present but not an object — start fresh
            | None -> JsonObject()
        let merged = Set.union (read path) (Set.ofSeq keys) |> Set.toList |> List.sort
        let arr = JsonArray()
        for k in merged do arr.Add(JsonValue.Create k)
        root.[key] <- arr
        File.WriteAllText(path, root.ToJsonString(JsonSerializerOptions(WriteIndented = true)))
        Ok ()
    with ex -> Error ex.Message

/// Surgically set a flow's SCALAR string field under `flows.<flow>.<field>` in
/// `projection.json`, preserving every other key byte-for-byte (2026-07-08, the
/// guided-plan wizard's persist). This is the A44 move the Migrate relaxation gate
/// pioneered: an interactive choice becomes a durable, hand-reachable config edit,
/// so a future headless run honors it without prompting. `Ok ()` on a successful
/// write; `Error cause` names the failure (downgrades never silent).
let setFlowString (path: string) (flow: string) (field: string) (value: string) : Result<unit, string> =
    let childObject (parent: JsonObject) (name: string) : JsonObject =
        match parent.TryGetPropertyValue name with
        | true, (:? JsonObject as o) -> o
        | _ ->
            let o = JsonObject()
            parent.[name] <- o
            o
    try
        let root =
            match rootObjectOf path with
            | Some o -> o
            | None   -> JsonObject()
        let flowObj = childObject (childObject root "flows") flow
        flowObj.[field] <- JsonValue.Create value
        File.WriteAllText(path, root.ToJsonString(JsonSerializerOptions(WriteIndented = true)))
        Ok ()
    with ex -> Error ex.Message

/// Surgically set a flow's `signoff` array under `flows.<flow>.signoff` (2026-07-09,
/// the guided-plan wizard's greenlight-write). The A44 move applied to the write-signoff:
/// after the wizard flips a flow to a destructive strategy, the interactive greenlight
/// becomes a durable, hand-reachable config edit, so the go board goes GREEN and a later
/// headless Execute honors it without re-declaring. Every OTHER key is preserved
/// byte-for-byte; the field order mirrors `renderFlow`'s signoff block so a subsequent
/// `parse ∘ render` is stable. `Ok ()` on a successful write; `Error cause` names the
/// failure (downgrades never silent).
let setFlowSignoff (path: string) (flow: string) (approvals: Projection.Pipeline.WriteSignoff.WriteApproval list) : Result<unit, string> =
    let childObject (parent: JsonObject) (name: string) : JsonObject =
        match parent.TryGetPropertyValue name with
        | true, (:? JsonObject as o) -> o
        | _ ->
            let o = JsonObject()
            parent.[name] <- o
            o
    let objOf (a: Projection.Pipeline.WriteSignoff.WriteApproval) : JsonObject =
        let o = JsonObject()
        o.["mode"] <- JsonValue.Create (Projection.Pipeline.WriteSignoff.modeLabel a.Mode)
        if not (List.isEmpty a.Tables) then
            let arr = JsonArray()
            for t in a.Tables do arr.Add(JsonValue.Create t)
            o.["tables"] <- arr
        a.AcknowledgedImpact |> Option.iter (fun v -> o.["acknowledgedImpact"] <- JsonValue.Create v)
        a.ApprovedBy         |> Option.iter (fun v -> o.["approvedBy"]         <- JsonValue.Create v)
        a.Date               |> Option.iter (fun v -> o.["date"]               <- JsonValue.Create v)
        o
    try
        let root =
            match rootObjectOf path with
            | Some o -> o
            | None   -> JsonObject()
        let flowObj = childObject (childObject root "flows") flow
        let arr = JsonArray()
        // 2026-07-10 (slice 4b) — the signoff array now carries TWO entry
        // shapes (mode approvals and act blessings). This writer sets the
        // named MODES; every act blessing on file, and every mode this call
        // does not name, is PRESERVED — the wizard's greenlight can no longer
        // silently erase a blessing the operator wrote in the workbench.
        let writtenModes =
            approvals |> List.map (fun a -> Projection.Pipeline.WriteSignoff.modeLabel a.Mode) |> Set.ofList
        (match flowObj.TryGetPropertyValue "signoff" with
         | true, (:? JsonArray as existing) ->
             for node in existing do
                 match node with
                 | :? JsonObject as o ->
                     let keep =
                         match o.TryGetPropertyValue "mode" with
                         | true, (:? JsonValue as v) ->
                             (match v.TryGetValue<string>() with
                              | true, m -> not (Set.contains m writtenModes)
                              | _ -> true)
                         | _ -> true   // an act blessing (or anything else) is preserved
                     if keep then arr.Add(o.DeepClone())
                 | _ -> ()
         | _ -> ())
        for a in approvals do arr.Add(objOf a)
        flowObj.["signoff"] <- arr
        File.WriteAllText(path, root.ToJsonString(JsonSerializerOptions(WriteIndented = true)))
        Ok ()
    with ex -> Error ex.Message

/// Surgically set a flow's WHOLE `signoff` array — mode approvals first, then
/// act blessings sorted by token (2026-07-10, slice 4a: the workbench's bless
/// gesture writes through immediately — a blessing is commitment). Field names
/// and order mirror `renderFlow`'s signoff block (`mode`/`act`, `tables`/
/// `fingerprint`, then the audit fields), so a subsequent `parse ∘ render` is
/// stable. Every other key is preserved byte-for-byte. `Ok ()` on a successful
/// write; `Error cause` names the failure (downgrades never silent).
let setFlowSignoffEntries
    (path: string)
    (flow: string)
    (approvals: Projection.Pipeline.WriteSignoff.WriteApproval list)
    (blessings: Projection.Pipeline.WriteSignoff.ActBlessing list)
    : Result<unit, string> =
    let childObject (parent: JsonObject) (name: string) : JsonObject =
        match parent.TryGetPropertyValue name with
        | true, (:? JsonObject as o) -> o
        | _ ->
            let o = JsonObject()
            parent.[name] <- o
            o
    let modeObj (a: Projection.Pipeline.WriteSignoff.WriteApproval) : JsonObject =
        let o = JsonObject()
        o.["mode"] <- JsonValue.Create (Projection.Pipeline.WriteSignoff.modeLabel a.Mode)
        if not (List.isEmpty a.Tables) then
            let arr = JsonArray()
            for t in a.Tables do arr.Add(JsonValue.Create t)
            o.["tables"] <- arr
        a.AcknowledgedImpact |> Option.iter (fun v -> o.["acknowledgedImpact"] <- JsonValue.Create v)
        a.ApprovedBy         |> Option.iter (fun v -> o.["approvedBy"]         <- JsonValue.Create v)
        a.Date               |> Option.iter (fun v -> o.["date"]               <- JsonValue.Create v)
        o
    let actObj (b: Projection.Pipeline.WriteSignoff.ActBlessing) : JsonObject =
        let o = JsonObject()
        o.["act"] <- JsonValue.Create b.Act
        o.["fingerprint"] <- JsonValue.Create (Projection.Core.ActConsent.fingerprintText b.Fingerprint)
        b.AcknowledgedImpact |> Option.iter (fun v -> o.["acknowledgedImpact"] <- JsonValue.Create v)
        b.ApprovedBy         |> Option.iter (fun v -> o.["approvedBy"]         <- JsonValue.Create v)
        b.Date               |> Option.iter (fun v -> o.["date"]               <- JsonValue.Create v)
        o
    try
        let root =
            match rootObjectOf path with
            | Some o -> o
            | None   -> JsonObject()
        let flowObj = childObject (childObject root "flows") flow
        let arr = JsonArray()
        for a in approvals do arr.Add(modeObj a)
        for b in blessings |> List.sortBy (fun b -> b.Act) do arr.Add(actObj b)
        flowObj.["signoff"] <- arr
        File.WriteAllText(path, root.ToJsonString(JsonSerializerOptions(WriteIndented = true)))
        Ok ()
    with ex -> Error ex.Message

/// Surgically set a flow's STRING-ARRAY field (`reconcile`, `tables`) under
/// `flows.<flow>.<field>` (2026-07-10, the review workbench's write-decisions
/// gesture). Same A44 move as `setFlowString`: the interactive choice becomes
/// a durable, hand-reachable config edit a later headless run honors. Every
/// other key is preserved byte-for-byte. `Ok ()` on a successful write;
/// `Error cause` names the failure (downgrades never silent).
let setFlowStrings (path: string) (flow: string) (field: string) (values: string list) : Result<unit, string> =
    let childObject (parent: JsonObject) (name: string) : JsonObject =
        match parent.TryGetPropertyValue name with
        | true, (:? JsonObject as o) -> o
        | _ ->
            let o = JsonObject()
            parent.[name] <- o
            o
    try
        let root =
            match rootObjectOf path with
            | Some o -> o
            | None   -> JsonObject()
        let flowObj = childObject (childObject root "flows") flow
        let arr = JsonArray()
        for v in values do arr.Add(JsonValue.Create v)
        flowObj.[field] <- arr
        File.WriteAllText(path, root.ToJsonString(JsonSerializerOptions(WriteIndented = true)))
        Ok ()
    with ex -> Error ex.Message

/// Surgically set a flow's `supportingScope` array (2026-07-10, the review
/// workbench's write-decisions gesture). Field names and order mirror
/// `parseSupportingScope` / `renderFlow` (`relationship`, `table`,
/// `key`/`anchor`/`of` per relationship, `reason`), so a subsequent
/// `parse ∘ render` is stable.
let setFlowSupportingScope (path: string) (flow: string) (entries: Projection.Pipeline.SupportingScope.SupportingScopeEntry list) : Result<unit, string> =
    let childObject (parent: JsonObject) (name: string) : JsonObject =
        match parent.TryGetPropertyValue name with
        | true, (:? JsonObject as o) -> o
        | _ ->
            let o = JsonObject()
            parent.[name] <- o
            o
    let objOf (e: Projection.Pipeline.SupportingScope.SupportingScopeEntry) : JsonObject =
        let o = JsonObject()
        let rel (label: string) = o.["relationship"] <- JsonValue.Create label
        o.["table"] <- JsonValue.Create e.Table
        (match e.Relationship with
         | Projection.Pipeline.SupportingScope.SupportingRelationship.ExistingReference keyName ->
             rel "existing-reference"; o.["key"] <- JsonValue.Create keyName
         | Projection.Pipeline.SupportingScope.SupportingRelationship.StaticLookup keyName ->
             rel "static-lookup"; o.["key"] <- JsonValue.Create keyName
         | Projection.Pipeline.SupportingScope.SupportingRelationship.ReferenceSeed ->
             rel "reference-seed"
         | Projection.Pipeline.SupportingScope.SupportingRelationship.SharedAnchor (anchor, keyName) ->
             rel "shared-anchor"; o.["anchor"] <- JsonValue.Create anchor
             keyName |> Option.iter (fun k -> o.["key"] <- JsonValue.Create k)
         | Projection.Pipeline.SupportingScope.SupportingRelationship.OwnedChild parent ->
             rel "owned-child"; o.["of"] <- JsonValue.Create parent
         | Projection.Pipeline.SupportingScope.SupportingRelationship.BlockedDependent parent ->
             rel "blocked-dependent"; o.["of"] <- JsonValue.Create parent)
        o.["reason"] <- JsonValue.Create e.Reason
        o
    try
        let root =
            match rootObjectOf path with
            | Some o -> o
            | None   -> JsonObject()
        let flowObj = childObject (childObject root "flows") flow
        let arr = JsonArray()
        for e in entries do arr.Add(objOf e)
        flowObj.["supportingScope"] <- arr
        File.WriteAllText(path, root.ToJsonString(JsonSerializerOptions(WriteIndented = true)))
        Ok ()
    with ex -> Error ex.Message
