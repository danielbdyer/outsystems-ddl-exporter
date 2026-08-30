namespace Projection.Pipeline

open Projection.Core
open Projection.Adapters.OssysSql

/// Claim-set assembly (the data-sink chapter, S11) — FROM THE JOURNAL, not
/// the Catalog: by the time a catalog exists, the losing claims are already
/// gone (`CatalogReader` keeps one kind per table), so adjudication evidence
/// must be assembled at the acquisition grain. One claim per entity row in
/// the WITNESSED edition (active AND tombstoned — the sink is total), grouped
/// by physical table; each claim carries the journal sync at which that
/// entity's claim on that table was first witnessed — the temporal dimension
/// the adjudication ladder tie-breaks on.
[<RequireQualifiedAccess>]
module SinkClaims =

    /// A claimant's native identity when the source supplied its SS_Key
    /// GUID; `None` = positional-only (key-basis honesty — the displacement
    /// algebra carries the same split; the claim's `EntityId` is always the
    /// in-set discriminator).
    let private keyOf (e: MetadataSnapshotRunner.OssysEntityRow) : SsKey option =
        e.EntitySsKey |> Option.map SsKey.OssysOriginal

    /// WHEN the journal first witnessed this entity claiming this physical
    /// table (align-II.10): the appearance line's sync when found; UNKNOWN
    /// when the journal carries no such line (a gappy or reconciled ledger
    /// — previously fabricated as sync 1, an instant nothing witnessed).
    let private firstWitnessedSync
        (journal: SinkJournal.JournalLine list)
        (entityId: int)
        (table: string)
        : PhysicalClaimRules.FirstWitnessedSync =
        journal
        |> List.tryPick (fun l ->
            match l.Displacement.After with
            | Some (SinkDisplacement.WitnessedRow.Entity e) when
                e.EntityId = entityId
                && System.String.Equals(e.PhysicalTableName, table, System.StringComparison.OrdinalIgnoreCase) ->
                Some l.SyncId
            | _ -> None)
        |> PhysicalClaimRules.FirstWitnessedSync.ofAppearance

    /// Assemble every physical table's claim set from a witnessed edition +
    /// its journal. The edition supplies WHO claims (total — tombstones
    /// included); the journal supplies WHEN each claim first appeared.
    let assemble
        (snapshot: MetadataSnapshotRunner.MetadataSnapshot)
        (journal: SinkJournal.JournalLine list)
        : PhysicalClaimRules.ClaimSet list =
        let extensionEspaces =
            snapshot.Modules
            |> List.choose (fun m ->
                // align-I.4: the Espace_Kind marker reads through the one
                // Core classifier (Trim + OrdinalIgnoreCase) — this lane's
                // former trim-and-lowercase idiom generalized to all three.
                if EspaceKindReading.isExtension m.EspaceKind then Some m.EspaceId else None)
            |> Set.ofList
        let moduleNames =
            snapshot.Modules
            |> List.map (fun m -> m.EspaceId, m.EspaceName)
            |> Map.ofList
        // align-III.13: the snapshot OBSERVES each entity's schema through
        // the physical-table rowset (joined by entity id); an entity with no
        // physical row reads the ASSUMED OutSystems default — the honest
        // form of the constant `"dbo"` this replaced. Grouping keys on the
        // FULL address (schema + table), so a multi-schema estate no longer
        // mis-groups rival claims that merely share a table name.
        let observedSchemas : Map<int, string> =
            snapshot.PhysicalTables
            |> List.map (fun pt -> pt.EntityId, pt.SchemaName)
            |> Map.ofList
        let refOf (e: MetadataSnapshotRunner.OssysEntityRow) : PhysicalClaimRules.PhysicalTableRef =
            match Map.tryFind e.EntityId observedSchemas with
            | Some schema -> PhysicalClaimRules.PhysicalTableRef.observed schema e.PhysicalTableName
            | None -> PhysicalClaimRules.PhysicalTableRef.assumedDbo e.PhysicalTableName
        snapshot.Entities
        |> List.groupBy (fun e -> PhysicalClaimRules.PhysicalTableRef.key (refOf e))
        |> List.map (fun (_, entities) ->
            let refs = entities |> List.map refOf
            // One address, possibly mixed bases: any OBSERVATION upgrades
            // the set's standing (the assumed reading of the same address
            // carries no extra information).
            let setRef =
                refs
                |> List.tryFind (fun r -> PhysicalClaimRules.SchemaBasis.isObserved r.Schema)
                |> Option.defaultValue (List.head refs)
            let table = setRef.Table
            let claims =
                entities
                |> List.map (fun e ->
                    ({ EntityId = e.EntityId
                       EntityKey = keyOf e
                       EntityName = e.EntityName
                       ModuleName = moduleNames |> Map.tryFind e.EspaceId |> Option.defaultValue (string e.EspaceId)
                       IsActive = e.IsActive
                       IsExternalRegistration = e.IsExternal && Set.contains e.EspaceId extensionEspaces
                       FirstWitnessedSync = firstWitnessedSync journal e.EntityId table } : PhysicalClaimRules.PhysicalClaim))
            ({ Ref = setRef
               Claims = claims } : PhysicalClaimRules.ClaimSet))
        |> List.sortBy (fun s -> PhysicalClaimRules.PhysicalTableRef.key s.Ref)

    /// Assemble and adjudicate in one motion — the estate-side consumer's
    /// shape: every table's outcome, in stable table order.
    let adjudicateAll
        (snapshot: MetadataSnapshotRunner.MetadataSnapshot)
        (journal: SinkJournal.JournalLine list)
        : (PhysicalClaimRules.ClaimSet * PhysicalClaimRules.PhysicalClaimOutcome) list =
        assemble snapshot journal
        |> List.map (fun set -> set, PhysicalClaimRules.adjudicate set)
