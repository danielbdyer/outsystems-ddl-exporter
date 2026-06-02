namespace Projection.Targets.OperationalDiagnostics

open System.Text
open Projection.Core

/// Π_Remediation — chapter 5+ slice `5.13.remediation-emitter` (per
/// `V1_PARITY_MATRIX` row 83). Emits `manifest.remediation.sql`: a
/// per-decision SQL script offering 3 options (UPDATE / DELETE /
/// SELECT) operators can run to fix source data before re-tightening.
///
/// V1 source: `Osm.Validation/Tightening/RemediationQueryBuilder.cs`
/// (~73 LOC) embedded remediation SQL directly in V1's
/// `TighteningDiagnostic.CreateMandatoryNullConflict`. V2 decouples
/// the diagnostic-production layer from the remediation-emission
/// layer: per-pass diagnostics carry structured outcome DUs +
/// metadata; this emitter consumes the DecisionSets and projects the
/// remediation surface.
///
/// **Operator-safety contract.** Only the SELECT statement is
/// active; UPDATE + DELETE statements ship commented-out. The
/// operator must read each block, confirm the data shape, and
/// uncomment the chosen option. Per `DECISIONS 2026-05-09 — Audits
/// surface things not on the agenda`: V2 errs on the side of
/// requiring operator action rather than enabling destructive
/// defaults.
///
/// **Pillar 9 classification — `DataIntent`.** The emitter projects
/// evidence from the DecisionSets into SQL; no operator opinion
/// enters the projection (operator opinion entered upstream at the
/// Pass layer via `Policy.TighteningPolicy`).
[<RequireQualifiedAccess>]
module RemediationEmitter =

    [<Literal>]
    let version : int = 1

    /// Render one SQL identifier with bracket-quoting. Per pillar 1
    /// (data-structure-oriented over string-parsing) the projection
    /// is structural — V2 IR's `Name` + `Schema.Table` are typed
    /// values; bracket-quoting at the terminal text boundary is
    /// the SQL-correctness adapter, not an operator-visible choice.
    let private brackets (s: string) : string =
        System.String.Concat("[", s, "]")  // LINT-ALLOW: terminal SQL-identifier bracket-quoting; segments are typed (literal brackets + Name.value / Physical.Schema / Physical.Table from V2 IR); BCL `String.Concat` IS the use-case-specific library for two-segment terminal-text composition

    let private qualifiedTable (kind: Kind) : string =
        System.String.Concat(brackets (TableId.schemaText kind.Physical), ".", brackets (TableId.tableText kind.Physical))  // LINT-ALLOW: terminal SQL-qualified-name composition; segments are typed (bracket-quoted schema + literal dot + bracket-quoted table from V2 IR Physical); BCL `String.Concat` IS the use-case-specific library

    /// Build the SsKey → Kind index ONCE up-front. Per Big-O audit
    /// discipline (`DECISIONS 2026-05-19 (slice B.3.6b)`): the
    /// remediation projection looks up the owning Kind for each
    /// decision; cross-derivation shared state lands as a precomputed
    /// index at construction time, not as repeated `List.tryFind`.
    let private kindByAttributeKey (catalog: Catalog) : Map<SsKey, Kind * Attribute> =
        catalog.Modules
        |> List.collect (fun m -> m.Kinds)
        |> List.collect (fun k ->
            k.Attributes |> List.map (fun a -> a.SsKey, (k, a)))
        |> Map.ofList

    let private kindByReferenceKey (catalog: Catalog) : Map<SsKey, Kind * Reference> =
        catalog.Modules
        |> List.collect (fun m -> m.Kinds)
        |> List.collect (fun k ->
            k.References |> List.map (fun r -> r.SsKey, (k, r)))
        |> Map.ofList

    let private kindByIndexKey (catalog: Catalog) : Map<SsKey, Kind * Index> =
        catalog.Modules
        |> List.collect (fun m -> m.Kinds)
        |> List.collect (fun k ->
            k.Indexes |> List.map (fun i -> i.SsKey, (k, i)))
        |> Map.ofList

    let private writeHeader
        (sb: StringBuilder)
        (subject: string)
        (interventionId: string)
        (reason: string)
        : unit =
        sb.AppendLine("-- =================================================================") |> ignore
        sb.AppendLine(sprintf "-- Remediation: %s (intervention: %s)" subject interventionId) |> ignore
        sb.AppendLine(sprintf "-- Reason: %s" reason) |> ignore
        sb.AppendLine("-- =================================================================") |> ignore

    let private writeOptions
        (sb: StringBuilder)
        (selectStmt: string)
        (updateStmt: string)
        (deleteStmt: string)
        : unit =
        sb.AppendLine("-- OPTION 1 (active): inspect the offending rows") |> ignore
        sb.AppendLine(selectStmt) |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine("-- OPTION 2: set to default (operator: confirm the default value)") |> ignore
        sb.AppendLine(sprintf "-- %s" updateStmt) |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine("-- OPTION 3: delete the offending rows (operator: confirm row removal)") |> ignore
        sb.AppendLine(sprintf "-- %s" deleteStmt) |> ignore
        sb.AppendLine() |> ignore

    /// Render remediation for a nullability conflict
    /// (`RequireOperatorApproval (MandatoryButHasNullsBeyondBudget _)`).
    /// The operator must reconcile the mandatory declaration against
    /// observed nulls; 3 options span "investigate / repair / purge."
    let private renderNullabilityConflict
        (sb: StringBuilder)
        (kind: Kind)
        (attr: Attribute)
        (interventionId: string)
        (nullCount: int64)
        (rowCount: int64)
        (budget: decimal)
        : unit =
        let table   = qualifiedTable kind
        let column  = brackets (ColumnRealization.columnNameText attr.Column)
        let subject = sprintf "%s.%s" (Name.value kind.Name) (Name.value attr.Name)
        let reason  =
            sprintf
                "Mandatory column has %d null(s) in %d row(s); budget %M exceeded."
                nullCount rowCount budget
        writeHeader sb subject interventionId reason
        let selectStmt = sprintf "SELECT * FROM %s WHERE %s IS NULL;" table column
        let updateStmt = sprintf "UPDATE %s SET %s = <DEFAULT> WHERE %s IS NULL;" table column column
        let deleteStmt = sprintf "DELETE FROM %s WHERE %s IS NULL;" table column
        writeOptions sb selectStmt updateStmt deleteStmt

    /// Render remediation for an FK orphan finding (`DoNotEnforce
    /// DataHasOrphans`). Operator either re-points the orphan or
    /// deletes the orphaned rows. The reference's source attribute
    /// resolves through the catalog's per-attribute index.
    let private renderForeignKeyOrphans
        (sb: StringBuilder)
        (sourceKind: Kind)
        (reference: Reference)
        (interventionId: string)
        (orphanCount: int64)
        (attrIndex: Map<SsKey, Kind * Attribute>)
        : unit =
        let table   = qualifiedTable sourceKind
        let attr    =
            match Map.tryFind reference.SourceAttribute attrIndex with
            | Some (_, a) -> a
            | None        ->
                invalidOp (sprintf "RemediationEmitter: reference %A SourceAttribute not found" reference.SsKey)
        let column  = brackets (ColumnRealization.columnNameText attr.Column)
        let subject =
            sprintf "%s.%s (FK %s)"
                (Name.value sourceKind.Name) (Name.value attr.Name) (Name.value reference.Name)
        let reason  =
            sprintf
                "Reference has %d orphan row(s) — values without a matching target."
                orphanCount
        writeHeader sb subject interventionId reason
        let selectStmt = sprintf "SELECT * FROM %s WHERE %s IS NOT NULL;" table column
        let updateStmt = sprintf "UPDATE %s SET %s = <VALID_TARGET_KEY> WHERE %s NOT IN (SELECT <TargetPK> FROM <TargetTable>);" table column column
        let deleteStmt = sprintf "DELETE FROM %s WHERE %s NOT IN (SELECT <TargetPK> FROM <TargetTable>);" table column
        writeOptions sb selectStmt updateStmt deleteStmt

    /// Render remediation for a unique-index duplicate finding
    /// (`DoNotEnforce DataHasDuplicates`). Operator inspects + de-dups.
    /// Each key-column SsKey resolves through the kind's per-attribute
    /// lookup.
    let private renderUniqueIndexDuplicates
        (sb: StringBuilder)
        (kind: Kind)
        (idx: Index)
        (interventionId: string)
        (attrIndex: Map<SsKey, Kind * Attribute>)
        : unit =
        let table     = qualifiedTable kind
        let indexName = Name.value idx.Name
        let subject   = sprintf "%s.%s (unique index)" (Name.value kind.Name) indexName
        let reason    = sprintf "Unique-index candidate '%s' shows duplicate row(s)." indexName
        writeHeader sb subject interventionId reason
        let keyColumns =
            idx.Columns
            |> List.map (fun ic ->
                match Map.tryFind ic.Attribute attrIndex with
                | Some (_, a) -> brackets (ColumnRealization.columnNameText a.Column)
                | None        ->
                    invalidOp (sprintf "RemediationEmitter: index column attribute %A not found" ic.Attribute))
            |> String.concat ", "
        let selectStmt =
            sprintf
                "SELECT %s, COUNT(*) AS RowCount FROM %s GROUP BY %s HAVING COUNT(*) > 1;"
                keyColumns table keyColumns
        let updateStmt =
            "Cannot UPDATE blindly; identify a canonical row per group and re-key the duplicates."
        let deleteStmt =
            sprintf
                "DELETE FROM (SELECT *, ROW_NUMBER() OVER (PARTITION BY %s ORDER BY <CanonicalKey>) AS rn FROM %s) WHERE rn > 1;"
                keyColumns table
        writeOptions sb selectStmt updateStmt deleteStmt

    /// Build the manifest.remediation.sql text from the three
    /// DecisionSets. Deterministic ordering: per-axis decisions are
    /// emitted in their stored chronological order (the writer
    /// preserves A24 — earliest-first under bind); axes themselves
    /// emit in fixed Nullability → ForeignKey → UniqueIndex order.
    let emit
        (catalog: Catalog)
        (nullability: NullabilityDecisionSet)
        (uniqueIndex: UniqueIndexDecisionSet)
        (foreignKey: ForeignKeyDecisionSet)
        : string =
        use _ = Bench.scope "emit.remediation.emit"
        let sb = StringBuilder()
        sb.AppendLine("-- Projection V2 — manifest.remediation.sql") |> ignore
        sb.AppendLine("-- Generated by RemediationEmitter (chapter 5+ slice 5.13.remediation-emitter).") |> ignore
        sb.AppendLine("-- OPTION 1 (SELECT) is active; OPTION 2 (UPDATE) and OPTION 3 (DELETE) ship") |> ignore
        sb.AppendLine("-- commented-out. Read each block before uncommenting any destructive action.") |> ignore
        sb.AppendLine() |> ignore

        let attrIndex = kindByAttributeKey catalog
        let refIndex  = kindByReferenceKey catalog
        let idxIndex  = kindByIndexKey catalog

        let mutable count = 0

        for decision in nullability.Decisions do
            match decision.Outcome with
            | NullabilityOutcome.RequireOperatorApproval
                  (MandatoryButHasNullsBeyondBudget (nullCount, rowCount, budget)) ->
                match Map.tryFind decision.AttributeKey attrIndex with
                | Some (kind, attr) ->
                    renderNullabilityConflict
                        sb kind attr decision.InterventionId
                        nullCount rowCount budget
                    count <- count + 1
                | None -> ()
            | _ -> ()

        for decision in foreignKey.Decisions do
            match decision.Outcome with
            | ForeignKeyOutcome.DoNotEnforce (DataHasOrphans orphanCount) ->
                match Map.tryFind decision.ReferenceKey refIndex with
                | Some (kind, reference) ->
                    renderForeignKeyOrphans
                        sb kind reference decision.InterventionId orphanCount attrIndex
                    count <- count + 1
                | None -> ()
            | _ -> ()

        for decision in uniqueIndex.Decisions do
            match decision.Outcome with
            | UniqueIndexOutcome.DoNotEnforce DataHasDuplicates ->
                match Map.tryFind decision.IndexKey idxIndex with
                | Some (kind, idx) ->
                    renderUniqueIndexDuplicates sb kind idx decision.InterventionId attrIndex
                    count <- count + 1
                | None -> ()
            | _ -> ()

        if count = 0 then
            sb.AppendLine("-- No remediation candidates surfaced. All decisions either tightened cleanly") |> ignore
            sb.AppendLine("-- or kept the prior state without operator-attention findings.") |> ignore

        sb.ToString()

    /// `RegisteredTransform` metadata view per the pillar 9 +
    /// L3-CC-Transform-Totality discipline. Classifies as
    /// `DataIntent` — the remediation projection is a structural
    /// view of empirical evidence (`DecisionSet` outcomes carry the
    /// observed null / orphan / duplicate counts).
    let registeredMetadata : RegisteredTransformMetadata =
        RegisteredTransformMetadata.emitter "remediationEmitter" Diagnostics
            [ TransformSite.dataIntent "remediationOptions"
                "Project Nullability/ForeignKey/UniqueIndex DecisionSet outcomes carrying operator-attention findings (RequireOperatorApproval / DataHasOrphans / DataHasDuplicates) into per-decision UPDATE/DELETE/SELECT options in manifest.remediation.sql. Operator-safety contract: only SELECT active; UPDATE + DELETE commented-out by default." ]
