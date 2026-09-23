namespace Projection.Pipeline

open Projection.Core
open FsToolkit.ErrorHandling

/// Binds `overrides.bridgeRowStaging` (textual config) into the RESOLVED
/// staging declarations the dynamic staging companion executes. Fail-closed:
/// every coordinate the operator names — the source/bridge tables (logical OR
/// physical form), their key/identity attributes, the mapped/constant insert
/// attributes — must resolve against the catalog, or the declaration is a
/// NAMED refusal (`pipeline.config.bridgeRowStaging.*`), never a silent skip.
///
/// **The auto-linked scope.** A staging declaration serves the declared
/// `overrides.bridgeRetargets` whose reference PARENT is the staging source
/// kind and whose bridge attribute IS the staging bridge KEY attribute — that
/// linked set defines the referenced-key derivation scope (each link's FK
/// column supplies referenced values) and receives the derived planned-state
/// evidence. A staging declaration that links ZERO retargets is inert config
/// and refused (A44: expressible ⇔ reachable); a retarget claimed by TWO
/// staging declarations is ambiguous evidence and refused.
///
/// **The mandatory-coverage guard.** A staged INSERT must be able to produce a
/// complete row: every MANDATORY bridge attribute that is neither
/// database-supplied (identity) nor defaulted must be covered by the key, the
/// identity, a mapping, or a constant — proven HERE, at bind time, before any
/// SQL runs. And the safety boundaries are structural: a mapping/constant may
/// not name the key or identity attributes (those cells belong to the
/// invariant), the source and bridge key types must match (the staged key cell
/// is the source key value), and duplicate coverage of one bridge attribute is
/// refused.
[<RequireQualifiedAccess>]
module BridgeRowStagingBinding =

    let private err (code: string) (message: string) : Result<'a> =
        Result.failureOf (ValidationError.create code message)

    let private ciEq (a: string) (b: string) : bool =
        System.String.Equals(a, b, System.StringComparison.OrdinalIgnoreCase)

    /// One auto-linked retarget: the declared retarget's id, the kind owning
    /// the retargeting FK, the FK column attribute (whose distinct non-null
    /// values ARE the referenced keys), and the reference's SsKey.
    type ResolvedRetargetLink =
        { RetargetId   : string
          ChildKind    : Kind
          FkAttribute  : Attribute
          ReferenceKey : SsKey }

    /// One fully-resolved staging declaration — typed kinds/attributes plus the
    /// linked retargets. `toSpec` projects the pure-kernel spec from it.
    type ResolvedStaging =
        { StagingId      : string
          /// `referenced` (default) or `allSourceRows` — which source rows the
          /// companion derives bridge rows for.
          Scope          : Config.BridgeStagingScope
          /// The acquisition cache posture (`off` default | `auto` | `pinned`).
          Cache          : Config.BridgeStagingCachePolicy
          SourceKind     : Kind
          SourceKey      : Attribute
          SourceIdentity : Attribute
          BridgeKind     : Kind
          BridgeKey      : Attribute
          BridgeIdentity : Attribute
          /// (bridge attribute, source attribute) insert mappings.
          Mappings       : (Attribute * Attribute) list
          /// (bridge attribute, raw value) insert constants.
          Constants      : (Attribute * string option) list
          Links          : ResolvedRetargetLink list }

    /// The pure-kernel spec projection (`BridgeRowDelta.compute` input).
    let toSpec (r: ResolvedStaging) : BridgeRowStagingSpec =
        { StagingId               = r.StagingId
          BridgeKeyAttribute      = r.BridgeKey.Name
          BridgeIdentityAttribute = r.BridgeIdentity.Name
          Mappings                =
            r.Mappings |> List.map (fun (b, s) -> { BridgeAttribute = b.Name; SourceAttribute = s.Name })
          Constants               =
            r.Constants |> List.map (fun (b, v) -> { BridgeAttribute = b.Name; Value = v }) }

    /// Resolve an either/or table coordinate to exactly one catalog kind.
    let private resolveKind (catalog: Catalog) (entryId: string) (side: string) (source: Config.RenameSource) : Result<Kind> =
        let matches =
            match source with
            | Config.LogicalSource l ->
                Catalog.allModulesKinds catalog
                |> List.filter (fun (m, k) ->
                    ciEq (Name.value m.Name) l.Module && ciEq (Name.value k.Name) l.Entity)
                |> List.map snd
            | Config.PhysicalSource p ->
                Catalog.allModulesKinds catalog
                |> List.filter (fun (_, k) ->
                    ciEq (TableId.schemaText k.Physical) p.Schema && ciEq (TableId.tableText k.Physical) p.Table)
                |> List.map snd
        match matches |> List.distinctBy (fun k -> k.SsKey) with
        | [ kind ] -> Result.success kind
        | [] ->
            err "pipeline.config.bridgeRowStaging.kind.notFound"
                (String.concat "" [ "bridge row staging '"; entryId; "': the "; side; " table is not in the model" ])  // LINT-ALLOW: terminal refusal-text composition at the ValidationError boundary; segments are typed values, String.concat is the irreducible primitive for this free-text operator message — the TransformRegistry precedent
        | _ ->
            err "pipeline.config.bridgeRowStaging.kind.ambiguous"
                (String.concat "" [ "bridge row staging '"; entryId; "': the "; side; " table is ambiguous across the resolved scope" ])  // LINT-ALLOW: terminal refusal-text composition at the ValidationError boundary; segments are typed values, String.concat is the irreducible primitive for this free-text operator message — the TransformRegistry precedent

    /// Resolve a logical attribute name on a kind to exactly one attribute.
    let private resolveAttr (entryId: string) (side: string) (kind: Kind) (name: string) : Result<Attribute> =
        match kind.Attributes |> List.filter (fun a -> ciEq (Name.value a.Name) name) with
        | [ attr ] -> Result.success attr
        | [] ->
            err "pipeline.config.bridgeRowStaging.attribute.notFound"
                (String.concat "" [ "bridge row staging '"; entryId; "': "; side; " attribute '"; name; "' is not on the resolved kind" ])  // LINT-ALLOW: terminal refusal-text composition at the ValidationError boundary; segments are typed values, String.concat is the irreducible primitive for this free-text operator message — the TransformRegistry precedent
        | _ ->
            err "pipeline.config.bridgeRowStaging.attribute.ambiguous"
                (String.concat "" [ "bridge row staging '"; entryId; "': "; side; " attribute '"; name; "' is ambiguous on the resolved kind" ])  // LINT-ALLOW: terminal refusal-text composition at the ValidationError boundary; segments are typed values, String.concat is the irreducible primitive for this free-text operator message — the TransformRegistry precedent

    /// Auto-link the declared retargets this staging declaration serves: the
    /// retarget's reference PARENT is the source kind AND its bridge attribute
    /// is the staging bridge KEY. Resolution mirrors `BridgeRetargetBinding`'s
    /// (entity + relationship + bridge coordinate, fail-closed) — but here an
    /// entry that does not MATCH is simply not linked; only a matching entry
    /// that cannot RESOLVE is a refusal (the retarget binder owns its own
    /// resolution refusals).
    let private linkRetargets
        (catalog: Catalog)
        (sourceKind: Kind)
        (bridgeKey: Attribute)
        (retargets: Config.BridgeRetargetEntry list)
        : ResolvedRetargetLink list =
        // A link exists when the retarget's FK parent IS this staging source AND the
        // retarget resolves through exactly this staging bridge key. Both reference
        // forms are handled: an EXPLICIT reference contributes at most one link; a
        // DISCOVERY entry (`allReferencesTo`) contributes one per matched reference,
        // so "retarget every FK to this table" feeds one staging lane.
        let bridgeMatches (entry: Config.BridgeRetargetEntry) =
            match AttributeCoordinate.resolveFull catalog entry.Bridge with
            | Ok (_, _, bridgeAttrKey) -> bridgeAttrKey = bridgeKey.SsKey
            | Error _ -> false
        let linkOf (declaredId: string) (moduleName: string) (childKind: Kind) (reference: Reference) =
            if reference.TargetKind <> sourceKind.SsKey then None
            else
                childKind.Attributes
                |> List.tryFind (fun a -> a.SsKey = reference.SourceAttribute)
                |> Option.map (fun fkAttr ->
                    { RetargetId   = declaredId
                      ChildKind    = childKind
                      FkAttribute  = fkAttr
                      ReferenceKey = reference.SsKey })
        retargets
        |> List.filter bridgeMatches
        |> List.collect (fun entry ->
            match entry.Reference with
            | Config.BridgeRetargetReference.Explicit (coord, relationship) ->
                let kindMatches =
                    Catalog.allModulesKinds catalog
                    |> List.filter (fun (m, k) ->
                        (coord.Module = "" || ciEq (Name.value m.Name) coord.Module)
                        && ciEq (Name.value k.Name) coord.Entity)
                    |> List.distinctBy (fun (_, k) -> k.SsKey)
                match kindMatches with
                | [ (m, childKind) ] ->
                    childKind.References
                    |> List.tryFind (fun r -> ciEq (Name.value r.Name) relationship)
                    |> Option.bind (linkOf entry.Id (Name.value m.Name) childKind)
                    |> Option.toList
                | _ -> []
            | Config.BridgeRetargetReference.AllReferencesTo _ ->
                // The retarget binder owns the discovery expansion + its refusals;
                // here we mirror it so the staging scope covers every discovered FK.
                Catalog.allModulesKinds catalog
                |> List.collect (fun (m, childKind) ->
                    childKind.References
                    |> List.filter (fun r -> r.TargetKind = sourceKind.SsKey)
                    |> List.choose (fun r ->
                        linkOf (BridgeRetargetBinding.discoveredIdOf entry.Id (Name.value m.Name) childKind r) (Name.value m.Name) childKind r))
                |> List.sortBy (fun l -> l.ChildKind.SsKey, l.ReferenceKey))

    /// Resolve one staging declaration (fail-closed; see the module doc for
    /// the guards).
    let private bindOne
        (catalog: Catalog)
        (retargets: Config.BridgeRetargetEntry list)
        (entry: Config.BridgeRowStagingEntry)
        : Result<ResolvedStaging> =
        result {
            // The staging id names the artifact directory (`BridgeStaging/<id>/`),
            // so it must be a path-safe token — refused otherwise, before any
            // acquisition or file write.
            let! () =
                let idSafe =
                    entry.Id <> ""
                    && entry.Id |> Seq.forall (fun c ->
                        System.Char.IsAsciiLetterOrDigit c || c = '-' || c = '_')
                if idSafe then Result.success ()
                else
                    err "pipeline.config.bridgeRowStaging.idShape"
                        (String.concat "" [ "bridge row staging id '"; entry.Id; "' must be a non-empty token of letters, digits, '-' or '_' (it names the artifact folder)" ])  // LINT-ALLOW: terminal refusal-text composition at the ValidationError boundary; segments are typed values, String.concat is the irreducible primitive for this free-text operator message — the TransformRegistry precedent
            let! sourceKind = resolveKind catalog entry.Id "source" entry.Source
            let! bridgeKind = resolveKind catalog entry.Id "bridge" entry.Bridge
            let! sourceKey = resolveAttr entry.Id "source key" sourceKind entry.SourceKey
            let! sourceIdentity = resolveAttr entry.Id "source identity" sourceKind entry.SourceIdentity
            let! bridgeKey = resolveAttr entry.Id "bridge key" bridgeKind entry.BridgeKey
            let! bridgeIdentity = resolveAttr entry.Id "bridge identity" bridgeKind entry.BridgeIdentity
            let! () =
                if sourceKey.Type = bridgeKey.Type then Result.success ()
                else
                    err "pipeline.config.bridgeRowStaging.keyTypesDiffer"
                        (String.concat "" [ "bridge row staging '"; entry.Id; "': the source and bridge key types differ — the staged key cell is the source key value, so the types must match" ])  // LINT-ALLOW: terminal refusal-text composition at the ValidationError boundary; segments are typed values, String.concat is the irreducible primitive for this free-text operator message — the TransformRegistry precedent
            let! mappings =
                entry.InsertMappings
                |> List.map (fun m ->
                    result {
                        let! b = resolveAttr entry.Id "mapping bridge" bridgeKind m.Bridge
                        let! s = resolveAttr entry.Id "mapping source" sourceKind m.From
                        return (b, s)
                    })
                |> Result.aggregate
            let! constants =
                entry.InsertConstants
                |> List.map (fun c ->
                    resolveAttr entry.Id "constant bridge" bridgeKind c.Bridge
                    |> Result.map (fun b -> (b, c.Value)))
                |> Result.aggregate
            // The invariant cells are not coverable by mappings/constants.
            let reserved = Set.ofList [ bridgeKey.SsKey; bridgeIdentity.SsKey ]
            let covered = (mappings |> List.map fst) @ (constants |> List.map fst)
            let! () =
                match covered |> List.filter (fun a -> Set.contains a.SsKey reserved) with
                | [] -> Result.success ()
                | offending ->
                    err "pipeline.config.bridgeRowStaging.reservedAttribute"
                        (String.concat "" [ "bridge row staging '"; entry.Id; "': mappings/constants may not name the bridge key or identity attribute ("; offending |> List.map (fun a -> Name.value a.Name) |> String.concat ", "; ")" ])  // LINT-ALLOW: terminal refusal-text composition at the ValidationError boundary; segments are typed values, String.concat is the irreducible primitive for this free-text operator message — the TransformRegistry precedent
            let! () =
                let dups =
                    covered
                    |> List.countBy (fun a -> a.SsKey)
                    |> List.filter (fun (_, n) -> n > 1)
                if List.isEmpty dups then Result.success ()
                else
                    err "pipeline.config.bridgeRowStaging.duplicateCoverage"
                        (String.concat "" [ "bridge row staging '"; entry.Id; "': a bridge attribute is covered by more than one mapping/constant" ])  // LINT-ALLOW: terminal refusal-text composition at the ValidationError boundary; segments are typed values, String.concat is the irreducible primitive for this free-text operator message — the TransformRegistry precedent
            // Mandatory-coverage: every mandatory, non-identity, non-defaulted
            // bridge attribute must be covered by key ∪ identity ∪ mappings ∪
            // constants — a staged INSERT must be able to produce a complete row.
            let coveredKeys =
                Set.ofList (bridgeKey.SsKey :: bridgeIdentity.SsKey :: (covered |> List.map (fun a -> a.SsKey)))
            let! () =
                let uncovered =
                    bridgeKind.Attributes
                    |> List.filter (fun a ->
                        a.IsMandatory
                        && not a.IsIdentity
                        && a.DefaultValue.IsNone
                        && not (Set.contains a.SsKey coveredKeys))
                if List.isEmpty uncovered then Result.success ()
                else
                    err "pipeline.config.bridgeRowStaging.mandatoryUncovered"
                        (String.concat "" [ "bridge row staging '"; entry.Id; "': mandatory bridge attributes not covered by key/identity/mappings/constants: "; uncovered |> List.map (fun a -> Name.value a.Name) |> String.concat ", "; " — a staged insert could not produce a complete row" ])  // LINT-ALLOW: terminal refusal-text composition at the ValidationError boundary; segments are typed values, String.concat is the irreducible primitive for this free-text operator message — the TransformRegistry precedent
            let links = linkRetargets catalog sourceKind bridgeKey retargets
            let! () =
                if List.isEmpty links then
                    err "pipeline.config.bridgeRowStaging.linksNone"
                        (String.concat "" [ "bridge row staging '"; entry.Id; "': no declared bridge retarget links to it (reference parent = source kind, bridge attribute = the staging bridge key) — the declaration would be inert" ])  // LINT-ALLOW: terminal refusal-text composition at the ValidationError boundary; segments are typed values, String.concat is the irreducible primitive for this free-text operator message — the TransformRegistry precedent
                else Result.success ()
            return
                { StagingId      = entry.Id
                  Scope          = entry.Scope
                  Cache          = entry.Cache
                  SourceKind     = sourceKind
                  SourceKey      = sourceKey
                  SourceIdentity = sourceIdentity
                  BridgeKind     = bridgeKind
                  BridgeKey      = bridgeKey
                  BridgeIdentity = bridgeIdentity
                  Mappings       = mappings
                  Constants      = constants
                  Links          = links }
        }

    /// Bind every staging declaration, accumulating named refusals. `[]` ⇒ `[]`
    /// (the companion runs nothing; byte-identical). Cross-entry guards: unique
    /// staging ids; a retarget id claimed by at most ONE staging declaration
    /// (two would be ambiguous evidence sources).
    let fromConfig (catalog: Catalog) (cfg: Config.Config) : Result<ResolvedStaging list> =
        let entries = cfg.Overrides.BridgeRowStaging
        let dupIds =
            entries |> List.countBy (fun e -> e.Id) |> List.filter (fun (_, n) -> n > 1) |> List.map fst
        if not (List.isEmpty dupIds) then
            err "pipeline.config.bridgeRowStaging.duplicateId"
                (String.concat "" [ "overrides.bridgeRowStaging has duplicate ids: "; String.concat ", " dupIds ])  // LINT-ALLOW: terminal refusal-text composition at the ValidationError boundary; segments are typed values, String.concat is the irreducible primitive for this free-text operator message — the TransformRegistry precedent
        else
            entries
            |> List.map (bindOne catalog cfg.Overrides.BridgeRetargets)
            |> Result.aggregate
            |> Result.bind (fun resolved ->
                let claimedTwice =
                    resolved
                    |> List.collect (fun r -> r.Links |> List.map (fun l -> l.RetargetId))
                    |> List.countBy id
                    |> List.filter (fun (_, n) -> n > 1)
                    |> List.map fst
                if List.isEmpty claimedTwice then Result.success resolved
                else
                    err "pipeline.config.bridgeRowStaging.retargetClaimedTwice"
                        (String.concat "" [ "bridge retargets claimed by more than one staging declaration: "; String.concat ", " claimedTwice ]))  // LINT-ALLOW: terminal refusal-text composition at the ValidationError boundary; segments are typed values, String.concat is the irreducible primitive for this free-text operator message — the TransformRegistry precedent
