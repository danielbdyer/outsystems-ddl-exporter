namespace Projection.Pipeline

open System
open Projection.Core
open Projection.Adapters.OssysSql

/// The sink's displacement algebra (the data-sink chapter, S4a 2026-08-15;
/// CHAPTER_SINK_OPEN.md) — the pure diff between two witnessed
/// `MetadataSnapshot`s, one record per row-identity delta, each carrying its
/// full before/after images so the journal's FTC holds:
///
///     applyAll (diff a b) (canonical a) = canonical b
///
/// **Totality (T19's precondition).** The row-grain carrier is total: every
/// row that appeared, vanished, or reshaped yields EXACTLY one displacement.
/// The domain vocabulary (EntityDeactivated, PhysicalTableSuperseded, …) is
/// a CLASSIFICATION over that carrier, never a filter — a delta no domain
/// transition names still rides the journal as its bare row change.
///
/// **Canonical order.** Within a rowset, source list order is not semantic
/// (authored order and index-column order are FIELDS — `Order`, `Ordinal`),
/// so the algebra works over key-sorted rowsets (`canonical`) and emits
/// displacements in (table, key) order — deterministic journals by
/// construction (T1 at this grain).
///
/// **Key honesty.** Every displacement carries its `KeyBasis` — a
/// name-derived key (`Composite`) is rename-fragile and says so on the
/// record, the way `CatalogDiff.SynthesizedRenameWarning` says it after the
/// fact.
[<RequireQualifiedAccess>]
module SinkDisplacement =

    /// What a row's identity stands on. `Native` survives every rename
    /// (the OSSYS SS_Key); `Positional` survives renames within one estate
    /// (the ossys integer id, or the sys object_id within one database);
    /// `Composite` is name-derived and rename-fragile — carried honestly.
    [<RequireQualifiedAccess>]
    type KeyBasis =
        | Native of Guid
        | Positional of int
        | Composite of string list

    /// The sixteen rowsets of the witnessed snapshot, as a closed table
    /// vocabulary for displacement records and journal lines.
    [<RequireQualifiedAccess>]
    type SinkTable =
        | Modules
        | Entities
        | Attributes
        | References
        | PhysicalTables
        | ColumnReality
        | ColumnChecks
        | PhysColsPresent
        | Indexes
        | IndexColumns
        | ForeignKeysReality
        | ForeignKeyColumns
        | Triggers
        | Sequences
        | Temporal
        | Capabilities

    module SinkTable =
        /// Stable journal token per table (round-trips in S4b's codec).
        let token (t: SinkTable) : string =
            match t with
            | SinkTable.Modules -> "modules"
            | SinkTable.Entities -> "entities"
            | SinkTable.Attributes -> "attributes"
            | SinkTable.References -> "references"
            | SinkTable.PhysicalTables -> "physicalTables"
            | SinkTable.ColumnReality -> "columnReality"
            | SinkTable.ColumnChecks -> "columnChecks"
            | SinkTable.PhysColsPresent -> "physColsPresent"
            | SinkTable.Indexes -> "indexes"
            | SinkTable.IndexColumns -> "indexColumns"
            | SinkTable.ForeignKeysReality -> "foreignKeysReality"
            | SinkTable.ForeignKeyColumns -> "foreignKeyColumns"
            | SinkTable.Triggers -> "triggers"
            | SinkTable.Sequences -> "sequences"
            | SinkTable.Temporal -> "temporal"
            | SinkTable.Capabilities -> "capabilities"

        let all : SinkTable list =
            [ SinkTable.Modules; SinkTable.Entities; SinkTable.Attributes
              SinkTable.References; SinkTable.PhysicalTables; SinkTable.ColumnReality
              SinkTable.ColumnChecks; SinkTable.PhysColsPresent; SinkTable.Indexes
              SinkTable.IndexColumns; SinkTable.ForeignKeysReality
              SinkTable.ForeignKeyColumns; SinkTable.Triggers; SinkTable.Sequences
              SinkTable.Temporal; SinkTable.Capabilities ]

    /// One row of any rowset — the displacement's before/after image
    /// carrier (a closed sum over the sixteen row records).
    [<RequireQualifiedAccess>]
    type SinkRow =
        | Module of MetadataSnapshotRunner.OssysModuleRow
        | Entity of MetadataSnapshotRunner.OssysEntityRow
        | Attribute of MetadataSnapshotRunner.OssysAttributeRow
        | Reference of MetadataSnapshotRunner.OssysReferenceRow
        | PhysicalTable of MetadataSnapshotRunner.OssysPhysicalTableRow
        | ColumnReality of MetadataSnapshotRunner.OssysColumnRealityRow
        | ColumnCheck of MetadataSnapshotRunner.OssysColumnCheckRow
        | PhysColsPresent of MetadataSnapshotRunner.OssysPhysColsPresentRow
        | Index of MetadataSnapshotRunner.OssysAllIdxRow
        | IndexColumn of MetadataSnapshotRunner.OssysIdxColMappedRow
        | FkReality of MetadataSnapshotRunner.OssysFkRealityRow
        | FkColumn of MetadataSnapshotRunner.OssysFkColumnRow
        | Trigger of MetadataSnapshotRunner.OssysTriggerRow
        | Sequence of MetadataSnapshotRunner.OssysSequenceRow
        | Temporal of MetadataSnapshotRunner.OssysTemporalRow
        | Capability of MetadataSnapshotRunner.OssysCapabilityRow

    /// The domain classification over a row change — the vocabulary the
    /// operator reads in the journal. Total carriers underneath mean an
    /// unclassified change is still journaled (Domain = None).
    [<RequireQualifiedAccess>]
    type DomainTransition =
        | EntityDeactivated
        | EntityReactivated
        | EntityRehomed of fromEspaceId: int * toEspaceId: int
        | EntityRegisteredExternal
        | PhysicalTableClaimChanged of fromTable: string * toTable: string
        | PhysicalTableSuperseded of table: string
        | AttributeRetired
        | AttributeReactivated
        | AttributeRetyped of facets: AttributeFacet list
        | ModuleRetired
        | ModuleReactivated
        | ShapeChanged

    /// One row-identity delta between two witnessed snapshots. Exactly one
    /// of the three shapes holds: appeared (Before=None), vanished
    /// (After=None), reshaped (both Some, unequal) — `changeOf` names it.
    type Displacement =
        {
            Table    : SinkTable
            /// Canonical key text (deterministic; the journal's key field).
            KeyText  : string
            KeyBasis : KeyBasis
            Before   : SinkRow option
            After    : SinkRow option
            Domain   : DomainTransition option
        }

    [<RequireQualifiedAccess>]
    type RowChange =
        | Appeared
        | Vanished
        | Reshaped

    let changeOf (d: Displacement) : RowChange =
        match d.Before, d.After with
        | None, Some _ -> RowChange.Appeared
        | Some _, None -> RowChange.Vanished
        | _ -> RowChange.Reshaped

    // ------------------------------------------------------------------
    // Per-rowset identity.
    // ------------------------------------------------------------------

    let private guidKey (g: Guid) = g.ToString("D")

    let private moduleKey (r: MetadataSnapshotRunner.OssysModuleRow) : string * KeyBasis =
        match r.EspaceSsKey with
        | Some g -> guidKey g, KeyBasis.Native g
        | None -> sprintf "espace:%d" r.EspaceId, KeyBasis.Positional r.EspaceId

    let private entityKey (r: MetadataSnapshotRunner.OssysEntityRow) : string * KeyBasis =
        match r.EntitySsKey with
        | Some g -> guidKey g, KeyBasis.Native g
        | None -> sprintf "entity:%d" r.EntityId, KeyBasis.Positional r.EntityId

    let private attributeKey (r: MetadataSnapshotRunner.OssysAttributeRow) : string * KeyBasis =
        match r.AttrSsKey with
        | Some g -> guidKey g, KeyBasis.Native g
        | None -> sprintf "attr:%d" r.AttrId, KeyBasis.Positional r.AttrId

    let private referenceKey (r: MetadataSnapshotRunner.OssysReferenceRow) : string * KeyBasis =
        // Defensively composite (R6): AttrId is one-FK-per-attribute by
        // the rowset's own docstring, but the target name rides the key so
        // a duplicate can never silently collapse; a retarget is therefore
        // a vanish+appear pair, adjudicated at the claims grain.
        let name = defaultArg r.RefEntityName ""
        sprintf "ref:%d:%s" r.AttrId name,
        KeyBasis.Composite [ string r.AttrId; name ]

    let private physicalTableKey (r: MetadataSnapshotRunner.OssysPhysicalTableRow) : string * KeyBasis =
        sprintf "phys:%d" r.EntityId, KeyBasis.Positional r.EntityId

    let private columnRealityKey (r: MetadataSnapshotRunner.OssysColumnRealityRow) : string * KeyBasis =
        sprintf "colreal:%d" r.AttrId, KeyBasis.Positional r.AttrId

    let private columnCheckKey (r: MetadataSnapshotRunner.OssysColumnCheckRow) : string * KeyBasis =
        sprintf "check:%d:%s" r.AttrId r.ConstraintName,
        KeyBasis.Composite [ string r.AttrId; r.ConstraintName ]

    let private physColsPresentKey (r: MetadataSnapshotRunner.OssysPhysColsPresentRow) : string * KeyBasis =
        sprintf "physcol:%d" r.AttrId, KeyBasis.Positional r.AttrId

    let private indexKey (r: MetadataSnapshotRunner.OssysAllIdxRow) : string * KeyBasis =
        sprintf "idx:%d:%s" r.EntityId r.IndexName,
        KeyBasis.Composite [ string r.EntityId; r.IndexName ]

    let private indexColumnKey (r: MetadataSnapshotRunner.OssysIdxColMappedRow) : string * KeyBasis =
        sprintf "idxcol:%d:%s:%d" r.EntityId r.IndexName r.Ordinal,
        KeyBasis.Composite [ string r.EntityId; r.IndexName; string r.Ordinal ]

    let private fkRealityKey (r: MetadataSnapshotRunner.OssysFkRealityRow) : string * KeyBasis =
        sprintf "fk:%d:%s" r.EntityId r.FkName,
        KeyBasis.Composite [ string r.EntityId; r.FkName ]

    let private fkColumnKey (r: MetadataSnapshotRunner.OssysFkColumnRow) : string * KeyBasis =
        sprintf "fkcol:%d:%d:%d" r.EntityId r.FkObjectId r.Ordinal,
        KeyBasis.Composite [ string r.EntityId; string r.FkObjectId; string r.Ordinal ]

    let private triggerKey (r: MetadataSnapshotRunner.OssysTriggerRow) : string * KeyBasis =
        sprintf "trg:%d:%s" r.EntityId r.TriggerName,
        KeyBasis.Composite [ string r.EntityId; r.TriggerName ]

    let private sequenceKey (r: MetadataSnapshotRunner.OssysSequenceRow) : string * KeyBasis =
        sprintf "seq:%s:%s" r.Schema r.Name,
        KeyBasis.Composite [ r.Schema; r.Name ]

    let private temporalKey (r: MetadataSnapshotRunner.OssysTemporalRow) : string * KeyBasis =
        sprintf "temporal:%d" r.EntityId, KeyBasis.Positional r.EntityId

    let private capabilityKey (_: MetadataSnapshotRunner.OssysCapabilityRow) : string * KeyBasis =
        // One row per snapshot by construction; its identity IS the table.
        "capabilities", KeyBasis.Composite [ "capabilities" ]

    /// The algebra's zero — the genesis every journal replays from
    /// (`diff emptySnapshot s1` is the first sync's appearances).
    let emptySnapshot : MetadataSnapshotRunner.MetadataSnapshot =
        { Modules = []
          Entities = []
          Attributes = []
          References = []
          PhysicalTables = []
          ColumnReality = []
          ColumnChecks = []
          PhysColsPresent = []
          Indexes = []
          IndexColumns = []
          ForeignKeysReality = []
          ForeignKeyColumns = []
          Triggers = []
          Sequences = []
          Temporal = []
          Capabilities = [] }

    // ------------------------------------------------------------------
    // Canonical form: every rowset key-sorted. The algebra's laws hold
    // over canonical snapshots; the witness hook canonicalizes before
    // persisting so stored snapshots, diffs, and replays share one order.
    // ------------------------------------------------------------------

    let canonical (s: MetadataSnapshotRunner.MetadataSnapshot) : MetadataSnapshotRunner.MetadataSnapshot =
        { s with
            Modules = s.Modules |> List.sortBy (moduleKey >> fst)
            Entities = s.Entities |> List.sortBy (entityKey >> fst)
            Attributes = s.Attributes |> List.sortBy (attributeKey >> fst)
            References = s.References |> List.sortBy (referenceKey >> fst)
            PhysicalTables = s.PhysicalTables |> List.sortBy (physicalTableKey >> fst)
            ColumnReality = s.ColumnReality |> List.sortBy (columnRealityKey >> fst)
            ColumnChecks = s.ColumnChecks |> List.sortBy (columnCheckKey >> fst)
            PhysColsPresent = s.PhysColsPresent |> List.sortBy (physColsPresentKey >> fst)
            Indexes = s.Indexes |> List.sortBy (indexKey >> fst)
            IndexColumns = s.IndexColumns |> List.sortBy (indexColumnKey >> fst)
            ForeignKeysReality = s.ForeignKeysReality |> List.sortBy (fkRealityKey >> fst)
            ForeignKeyColumns = s.ForeignKeyColumns |> List.sortBy (fkColumnKey >> fst)
            Triggers = s.Triggers |> List.sortBy (triggerKey >> fst)
            Sequences = s.Sequences |> List.sortBy (sequenceKey >> fst)
            Temporal = s.Temporal |> List.sortBy (temporalKey >> fst)
            Capabilities = s.Capabilities |> List.sortBy (capabilityKey >> fst) }

    // ------------------------------------------------------------------
    // The domain classifier — computed with both snapshots in scope.
    // ------------------------------------------------------------------

    let private isExtensionModule (s: MetadataSnapshotRunner.MetadataSnapshot) (espaceId: int) : bool =
        s.Modules
        |> List.exists (fun m ->
            m.EspaceId = espaceId
            && (match m.EspaceKind with
                | Some k -> String.Equals(k, "Extension", StringComparison.OrdinalIgnoreCase)
                | None -> false))

    let private tableClaimedByOther (s: MetadataSnapshotRunner.MetadataSnapshot) (row: MetadataSnapshotRunner.OssysEntityRow) : bool =
        s.Entities
        |> List.exists (fun e ->
            e.PhysicalTableName = row.PhysicalTableName
            && (fst (entityKey e)) <> (fst (entityKey row)))

    let private attributeFacetsOf
        (before: MetadataSnapshotRunner.OssysAttributeRow)
        (after: MetadataSnapshotRunner.OssysAttributeRow)
        : AttributeFacet list =
        [ if before.DataType <> after.DataType || before.ExternalDbType <> after.ExternalDbType then
              AttributeFacet.DataType
          if before.Length <> after.Length then AttributeFacet.Length
          if before.Precision <> after.Precision then AttributeFacet.Precision
          if before.Scale <> after.Scale then AttributeFacet.Scale
          if before.IsMandatory <> after.IsMandatory then AttributeFacet.Nullability
          if before.IsIdentifier <> after.IsIdentifier then AttributeFacet.PrimaryKey
          if before.IsAutoNumber <> after.IsAutoNumber then AttributeFacet.Identity
          if before.DefaultValue <> after.DefaultValue then AttributeFacet.DefaultValue ]

    let private classify
        (beforeSnapshot: MetadataSnapshotRunner.MetadataSnapshot)
        (afterSnapshot: MetadataSnapshotRunner.MetadataSnapshot)
        (table: SinkTable)
        (before: SinkRow option)
        (after: SinkRow option)
        : DomainTransition option =
        match table, before, after with
        | SinkTable.Entities, Some (SinkRow.Entity b), Some (SinkRow.Entity a) ->
            if b.IsActive && not a.IsActive then Some DomainTransition.EntityDeactivated
            elif not b.IsActive && a.IsActive then Some DomainTransition.EntityReactivated
            elif b.EspaceId <> a.EspaceId then Some (DomainTransition.EntityRehomed (b.EspaceId, a.EspaceId))
            elif b.PhysicalTableName <> a.PhysicalTableName then
                Some (DomainTransition.PhysicalTableClaimChanged (b.PhysicalTableName, a.PhysicalTableName))
            else None
        | SinkTable.Entities, None, Some (SinkRow.Entity a) ->
            if a.IsExternal && isExtensionModule afterSnapshot a.EspaceId then
                Some DomainTransition.EntityRegisteredExternal
            elif tableClaimedByOther beforeSnapshot a then
                Some (DomainTransition.PhysicalTableSuperseded a.PhysicalTableName)
            else None
        | SinkTable.Attributes, Some (SinkRow.Attribute b), Some (SinkRow.Attribute a) ->
            if b.IsActive && not a.IsActive then Some DomainTransition.AttributeRetired
            elif not b.IsActive && a.IsActive then Some DomainTransition.AttributeReactivated
            else
                match attributeFacetsOf b a with
                | [] -> None
                | facets -> Some (DomainTransition.AttributeRetyped facets)
        | SinkTable.Modules, Some (SinkRow.Module b), Some (SinkRow.Module a) ->
            if b.IsActive && not a.IsActive then Some DomainTransition.ModuleRetired
            elif not b.IsActive && a.IsActive then Some DomainTransition.ModuleReactivated
            else None
        | SinkTable.Capabilities, Some (SinkRow.Capability _), Some (SinkRow.Capability _) ->
            Some DomainTransition.ShapeChanged
        | _ -> None

    // ------------------------------------------------------------------
    // The diff itself.
    // ------------------------------------------------------------------

    let private diffRowset<'row when 'row : equality>
        (beforeSnapshot: MetadataSnapshotRunner.MetadataSnapshot)
        (afterSnapshot: MetadataSnapshotRunner.MetadataSnapshot)
        (table: SinkTable)
        (keyOf: 'row -> string * KeyBasis)
        (lift: 'row -> SinkRow)
        (beforeRows: 'row list)
        (afterRows: 'row list)
        : Displacement list =
        let indexRows rows =
            rows |> List.map (fun r -> fst (keyOf r), r) |> Map.ofList
        let beforeMap = indexRows beforeRows
        let afterMap = indexRows afterRows
        let keys =
            Set.union (beforeMap |> Map.keys |> Set.ofSeq) (afterMap |> Map.keys |> Set.ofSeq)
            |> Set.toList
            |> List.sort
        keys
        |> List.choose (fun key ->
            let before = beforeMap |> Map.tryFind key
            let after = afterMap |> Map.tryFind key
            match before, after with
            | Some b, Some a when b = a -> None
            | _ ->
                let basis =
                    match after, before with
                    | Some r, _ -> snd (keyOf r)
                    | _, Some r -> snd (keyOf r)
                    | None, None -> KeyBasis.Composite [ key ]
                let beforeRow = before |> Option.map lift
                let afterRow = after |> Option.map lift
                Some
                    { Table = table
                      KeyText = key
                      KeyBasis = basis
                      Before = beforeRow
                      After = afterRow
                      Domain = classify beforeSnapshot afterSnapshot table beforeRow afterRow })

    /// The total rowset diff: one displacement per row-identity delta, in
    /// (table, key) order. `diff a a = []` is the metadata plane's
    /// CDC-silence.
    let diff
        (a: MetadataSnapshotRunner.MetadataSnapshot)
        (b: MetadataSnapshotRunner.MetadataSnapshot)
        : Displacement list =
        [ diffRowset a b SinkTable.Modules moduleKey SinkRow.Module a.Modules b.Modules
          diffRowset a b SinkTable.Entities entityKey SinkRow.Entity a.Entities b.Entities
          diffRowset a b SinkTable.Attributes attributeKey SinkRow.Attribute a.Attributes b.Attributes
          diffRowset a b SinkTable.References referenceKey SinkRow.Reference a.References b.References
          diffRowset a b SinkTable.PhysicalTables physicalTableKey SinkRow.PhysicalTable a.PhysicalTables b.PhysicalTables
          diffRowset a b SinkTable.ColumnReality columnRealityKey SinkRow.ColumnReality a.ColumnReality b.ColumnReality
          diffRowset a b SinkTable.ColumnChecks columnCheckKey SinkRow.ColumnCheck a.ColumnChecks b.ColumnChecks
          diffRowset a b SinkTable.PhysColsPresent physColsPresentKey SinkRow.PhysColsPresent a.PhysColsPresent b.PhysColsPresent
          diffRowset a b SinkTable.Indexes indexKey SinkRow.Index a.Indexes b.Indexes
          diffRowset a b SinkTable.IndexColumns indexColumnKey SinkRow.IndexColumn a.IndexColumns b.IndexColumns
          diffRowset a b SinkTable.ForeignKeysReality fkRealityKey SinkRow.FkReality a.ForeignKeysReality b.ForeignKeysReality
          diffRowset a b SinkTable.ForeignKeyColumns fkColumnKey SinkRow.FkColumn a.ForeignKeyColumns b.ForeignKeyColumns
          diffRowset a b SinkTable.Triggers triggerKey SinkRow.Trigger a.Triggers b.Triggers
          diffRowset a b SinkTable.Sequences sequenceKey SinkRow.Sequence a.Sequences b.Sequences
          diffRowset a b SinkTable.Temporal temporalKey SinkRow.Temporal a.Temporal b.Temporal
          diffRowset a b SinkTable.Capabilities capabilityKey SinkRow.Capability a.Capabilities b.Capabilities ]
        |> List.concat

    /// The journal's norm at this grain (T15's reading: entry count).
    let norm (displacements: Displacement list) : int = List.length displacements

    // ------------------------------------------------------------------
    // Apply — ⊕ at the acquisition grain.
    // ------------------------------------------------------------------

    let private applyToRowset<'row when 'row : equality>
        (keyOf: 'row -> string * KeyBasis)
        (unlift: SinkRow -> 'row option)
        (d: Displacement)
        (rows: 'row list)
        : 'row list =
        let without = rows |> List.filter (fun r -> fst (keyOf r) <> d.KeyText)
        match d.After with
        | None -> without
        | Some after ->
            match unlift after with
            | Some row -> (row :: without) |> List.sortBy (keyOf >> fst)
            | None -> without

    /// Apply one displacement (replace-by-key: remove any row at the key,
    /// insert the after-image when present, keep canonical order).
    let applyOne
        (state: MetadataSnapshotRunner.MetadataSnapshot)
        (d: Displacement)
        : MetadataSnapshotRunner.MetadataSnapshot =
        match d.Table with
        | SinkTable.Modules ->
            { state with Modules = applyToRowset moduleKey (function SinkRow.Module r -> Some r | _ -> None) d state.Modules }
        | SinkTable.Entities ->
            { state with Entities = applyToRowset entityKey (function SinkRow.Entity r -> Some r | _ -> None) d state.Entities }
        | SinkTable.Attributes ->
            { state with Attributes = applyToRowset attributeKey (function SinkRow.Attribute r -> Some r | _ -> None) d state.Attributes }
        | SinkTable.References ->
            { state with References = applyToRowset referenceKey (function SinkRow.Reference r -> Some r | _ -> None) d state.References }
        | SinkTable.PhysicalTables ->
            { state with PhysicalTables = applyToRowset physicalTableKey (function SinkRow.PhysicalTable r -> Some r | _ -> None) d state.PhysicalTables }
        | SinkTable.ColumnReality ->
            { state with ColumnReality = applyToRowset columnRealityKey (function SinkRow.ColumnReality r -> Some r | _ -> None) d state.ColumnReality }
        | SinkTable.ColumnChecks ->
            { state with ColumnChecks = applyToRowset columnCheckKey (function SinkRow.ColumnCheck r -> Some r | _ -> None) d state.ColumnChecks }
        | SinkTable.PhysColsPresent ->
            { state with PhysColsPresent = applyToRowset physColsPresentKey (function SinkRow.PhysColsPresent r -> Some r | _ -> None) d state.PhysColsPresent }
        | SinkTable.Indexes ->
            { state with Indexes = applyToRowset indexKey (function SinkRow.Index r -> Some r | _ -> None) d state.Indexes }
        | SinkTable.IndexColumns ->
            { state with IndexColumns = applyToRowset indexColumnKey (function SinkRow.IndexColumn r -> Some r | _ -> None) d state.IndexColumns }
        | SinkTable.ForeignKeysReality ->
            { state with ForeignKeysReality = applyToRowset fkRealityKey (function SinkRow.FkReality r -> Some r | _ -> None) d state.ForeignKeysReality }
        | SinkTable.ForeignKeyColumns ->
            { state with ForeignKeyColumns = applyToRowset fkColumnKey (function SinkRow.FkColumn r -> Some r | _ -> None) d state.ForeignKeyColumns }
        | SinkTable.Triggers ->
            { state with Triggers = applyToRowset triggerKey (function SinkRow.Trigger r -> Some r | _ -> None) d state.Triggers }
        | SinkTable.Sequences ->
            { state with Sequences = applyToRowset sequenceKey (function SinkRow.Sequence r -> Some r | _ -> None) d state.Sequences }
        | SinkTable.Temporal ->
            { state with Temporal = applyToRowset temporalKey (function SinkRow.Temporal r -> Some r | _ -> None) d state.Temporal }
        | SinkTable.Capabilities ->
            { state with Capabilities = applyToRowset capabilityKey (function SinkRow.Capability r -> Some r | _ -> None) d state.Capabilities }

    /// Fold ⊕ — with `diff`, the FTC pair at the acquisition grain:
    /// `applyAll (diff a b) (canonical a) = canonical b`.
    let applyAll
        (displacements: Displacement list)
        (state: MetadataSnapshotRunner.MetadataSnapshot)
        : MetadataSnapshotRunner.MetadataSnapshot =
        displacements |> List.fold applyOne state
