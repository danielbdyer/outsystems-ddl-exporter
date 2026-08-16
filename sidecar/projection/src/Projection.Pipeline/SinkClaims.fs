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

    /// The sync at which the journal first witnessed this entity claiming
    /// this physical table. Total: an entity present in the edition always
    /// has an appearance line (the journal is total from genesis); the
    /// defensive fallback reads as "since the beginning".
    let private firstWitnessedSync
        (journal: SinkJournal.JournalLine list)
        (entityId: int)
        (table: string)
        : int =
        journal
        |> List.tryPick (fun l ->
            match l.Displacement.After with
            | Some (SinkDisplacement.SinkRow.Entity e) when
                e.EntityId = entityId
                && System.String.Equals(e.PhysicalTableName, table, System.StringComparison.OrdinalIgnoreCase) ->
                Some l.SyncId
            | _ -> None)
        |> Option.defaultValue 1

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
        snapshot.Entities
        |> List.groupBy (fun e -> e.PhysicalTableName.ToUpperInvariant())
        |> List.map (fun (_, entities) ->
            let table = (List.head entities).PhysicalTableName
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
            ({ Schema = "dbo"
               Table = table
               Claims = claims } : PhysicalClaimRules.ClaimSet))
        |> List.sortBy (fun s -> s.Table.ToUpperInvariant())

    /// Assemble and adjudicate in one motion — the estate-side consumer's
    /// shape: every table's outcome, in stable table order.
    let adjudicateAll
        (snapshot: MetadataSnapshotRunner.MetadataSnapshot)
        (journal: SinkJournal.JournalLine list)
        : (PhysicalClaimRules.ClaimSet * PhysicalClaimRules.PhysicalClaimOutcome) list =
        assemble snapshot journal
        |> List.map (fun set -> set, PhysicalClaimRules.adjudicate set)
