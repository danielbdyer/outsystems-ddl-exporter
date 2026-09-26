namespace Projection.Pipeline

// LINT-ALLOW-FILE: estate posture shaping (wave A6) — terminal SQL-text
//   composition for the reopen probes (COUNT_BIG SELECTs from typed
//   coordinates via `SqlIdentifier`, the EstateRemediation precedent) and
//   the three-part operator-readable ref tokens the overlay's suggested
//   config edits carry.

open Projection.Core

/// The interim posture's typed carriers for `check estate` (wave A6):
/// every RELAX-lane PROPOSED finding (past-band orphans / past-band
/// NOT-NULL contradictions) resolves to one `Relaxation` — the suggested
/// config edit, the evidence that forced it, and the reopen probe that
/// retires it. π-coherence by construction: the relaxation carries the
/// finding's key, and the overlay emitter projects entries and probes
/// from THIS list, so a proposed relaxation appears in the report, the
/// overlay, and the probes together — or not at all.
[<RequireQualifiedAccess>]
module EstatePosture =

    let private tableOf (kind: Kind) : string =
        SqlIdentifier.qualified (TableId.schemaText kind.Physical) (TableId.tableText kind.Physical)

    let private columnOf (a: Attribute) : string =
        SqlIdentifier.quote (ColumnRealization.columnNameText a.Column)

    let private coordinateOf (subject: string) : (string * string) option =
        match subject.Split('.') with
        | [| entity; col |] -> Some (entity, col)
        | _ -> None

    /// The operator-readable three-part ref (`Module.Entity.Attribute`)
    /// the overlay's config edit names — resolved from the logical target
    /// shape, whose names are espace-stable; `TighteningBinding` resolves
    /// the same token back to a typed key at merge time (the A44 circle).
    let private refTokenOf (catalog: Catalog) (kind: Kind) (a: Attribute) : string option =
        catalog.Modules
        |> List.tryFind (fun m -> m.Kinds |> List.exists (fun k -> k.SsKey = kind.SsKey))
        |> Option.map (fun m ->
            sprintf "%s.%s.%s" (Name.value m.Name) (Name.value kind.Name) (Name.value a.Name))

    let private resolveCoordinate (catalog: Catalog) (subject: string) : (Kind * Attribute) option =
        coordinateOf subject
        |> Option.bind (fun (entity, col) ->
            Catalog.allKinds catalog
            |> List.tryFind (fun k -> Name.value k.Name = entity)
            |> Option.bind (fun k ->
                k.Attributes
                |> List.tryFind (fun a -> Name.value a.Name = col)
                |> Option.map (fun a -> k, a)))

    /// One proposed finding's relaxation, resolved against the logical
    /// target shape (physical realizations retained through the
    /// normalization, so the probe locates real tables). `None` when the
    /// coordinates do not resolve — an entry is never fabricated past its
    /// finding.
    let private relaxationFor (logicalTarget: Catalog) (finding: Estate.Finding) : Relaxation option =
        let keyText = FindingKey.text finding.Key
        let subject = keyText.Substring(keyText.IndexOf ':' + 1)
        match finding.Kind with
        | EstateFindingKind.DataOrphansPastBand ->
            resolveCoordinate logicalTarget subject
            |> Option.bind (fun (k, a) ->
                k.References
                |> List.tryFind (fun r -> r.SourceAttribute = a.SsKey)
                |> Option.bind (fun r -> Catalog.tryFindKind r.TargetKind logicalTarget)
                |> Option.bind (fun targetKind ->
                    match Kind.primaryKey targetKind with
                    | [ targetPk ] ->
                        refTokenOf logicalTarget k a
                        |> Option.map (fun refToken ->
                            { Scope = finding.Key
                              Action = RelaxationAction.KeepUntracked refToken
                              Evidence = finding.Envs
                              // The probe counts EVERY orphan, sentinel
                              // zeros included — the band split measured
                              // repair effort, but the relationship cannot
                              // track WITH CHECK until all of them clear.
                              ReopenProbe =
                                sprintf "SELECT COUNT_BIG(*) AS [reopen] FROM %s WHERE %s IS NOT NULL AND %s NOT IN (SELECT %s FROM %s); -- %s retires at zero"
                                    (tableOf k) (columnOf a) (columnOf a)
                                    (columnOf targetPk) (tableOf targetKind) keyText })
                    | _ -> None))
        | EstateFindingKind.DataNotNullPastBand ->
            resolveCoordinate logicalTarget subject
            |> Option.bind (fun (k, a) ->
                refTokenOf logicalTarget k a
                |> Option.map (fun refToken ->
                    { Scope = finding.Key
                      Action = RelaxationAction.KeepNullable refToken
                      Evidence = finding.Envs
                      ReopenProbe =
                        sprintf "SELECT COUNT_BIG(*) AS [reopen] FROM %s WHERE %s IS NULL; -- %s retires at zero"
                            (tableOf k) (columnOf a) keyText }))
        | _ -> None

    /// Every proposed relaxation of one report, resolved against the
    /// logical target shape — the overlay emitter's one input.
    let relaxationsFor (logicalTarget: Catalog) (report: Estate.EstateReport) : Relaxation list =
        report.Findings
        |> List.filter (fun f ->
            match f.Kind with
            | EstateFindingKind.DataOrphansPastBand
            | EstateFindingKind.DataNotNullPastBand -> true
            | _ -> false)
        |> List.choose (relaxationFor logicalTarget)

    /// The active posture, read from the BOUND tightening policy (the
    /// loaded config resolved against the target catalog): the relaxation
    /// keys the estate's meter lines stand on. One reading for both the
    /// board and the retirement notices.
    let activeOf (policy: TighteningPolicy) : Set<SsKey> * Set<SsKey> =
        let relaxedRefs =
            policy.Interventions
            |> List.collect (function
                | TighteningIntervention.ForeignKey (_, cfg) ->
                    cfg.Overrides
                    |> List.filter (fun o -> o.Action = ForeignKeyOverrideAction.KeepUntracked)
                    |> List.map (fun o -> o.ReferenceKey)
                | _ -> [])
            |> Set.ofList
        let relaxedAttrs =
            policy.Interventions
            |> List.collect (function
                | TighteningIntervention.Nullability (_, cfg) when cfg.Direction = TighteningDirection.RelaxationOnly ->
                    cfg.Overrides
                    |> List.filter (fun o -> o.Action = OverrideAction.KeepNullable)
                    |> List.map (fun o -> o.AttributeKey)
                | _ -> [])
            |> Set.ofList
        relaxedRefs, relaxedAttrs
