namespace Projection.Targets.OperationalDiagnostics

// LINT-ALLOW-FILE: terminal SQL-identifier text emission. Bracket-quoted `[schema].[table]`
//   names are composed from typed V2 IR (Name.value / Physical.Schema/Table) at
//   the absolute terminal SQL-text boundary, with function-local StringBuilder
//   accumulation; `String.concat` IS the use-case-specific primitive here.

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
    // recon #8 — the `]`-doubling quoter promoted into Core's `SqlIdentifier`
    // (this Core-only project cannot reach ScriptDom's `Identifier.EncodeIdentifier`;
    // `SqlIdentifier.quote` is byte-verified against it). `brackets` stays as a
    // thin local alias so the call sites below read unchanged.
    let private brackets (s: string) : string = SqlIdentifier.quote s

    let private qualifiedTable (kind: Kind) : string =
        SqlIdentifier.qualified (TableId.schemaText kind.Physical) (TableId.tableText kind.Physical)

    /// Build the SsKey → Kind index ONCE up-front. Per Big-O audit
    /// discipline (`DECISIONS 2026-05-19 (slice B.3.6b)`): the
    /// remediation projection looks up the owning Kind for each
    /// decision; cross-derivation shared state lands as a precomputed
    /// index at construction time, not as repeated `List.tryFind`.
    let private kindByAttributeKey (catalog: Catalog) : Map<SsKey, Kind * Attribute> =
        Catalog.allKinds catalog
        |> List.collect (fun k ->
            k.Attributes |> List.map (fun a -> a.SsKey, (k, a)))
        |> Map.ofList

    let private kindByReferenceKey (catalog: Catalog) : Map<SsKey, Kind * Reference> =
        Catalog.allKinds catalog
        |> List.collect (fun k ->
            k.References |> List.map (fun r -> r.SsKey, (k, r)))
        |> Map.ofList

    let private kindByIndexKey (catalog: Catalog) : Map<SsKey, Kind * Index> =
        Catalog.allKinds catalog
        |> List.collect (fun k ->
            k.Indexes |> List.map (fun i -> i.SsKey, (k, i)))
        |> Map.ofList

    // -- Data-reality findings (2026-07-06) --------------------------------
    //
    // The fidelity report's data violations, mapped by the pipeline into this
    // neutral shape (this Targets-layer emitter compiles below the Pipeline
    // layer, so it cannot consume `ModelFidelity.DataViolation` directly).
    // The axis vocabulary mirrors `ModelFidelity.ViolationKind` one-to-one.
    // These cover the source data contradicting the DECLARED model — including
    // the load-blocking case the tightening DecisionSets never reach: nulls in
    // a column the model already declares NOT NULL.

    type DataRealityKind =
        | NullsInNotNullColumn of nullCount: int64
        | DuplicatesInUniqueColumn
        | OrphanedReference of orphanCount: int64
        | ValueOverflow of observed: string * declared: string

    /// One data-reality finding to remediate. `Entity` / `Column` are the
    /// operator-facing logical names the fidelity report carries; they resolve
    /// back to the catalog's `Kind` / `Attribute` by `Name` at render time.
    type DataRealityFinding =
        {
            Entity : string
            Column : string
            Kind   : DataRealityKind
        }

    /// The dedup axis a finding occupies — a decision block already rendered
    /// for the same (entity, column, axis) makes the fidelity sibling
    /// redundant (the two projections overlap on orphans / duplicates).
    let private axisOf (kind: DataRealityKind) : string =
        match kind with
        | NullsInNotNullColumn _   -> "nulls"
        | DuplicatesInUniqueColumn -> "duplicates"
        | OrphanedReference _      -> "orphans"
        | ValueOverflow _          -> "overflow"

    let private kindByEntityName (catalog: Catalog) : Map<string, Kind> =
        Catalog.allKinds catalog
        |> List.map (fun k -> Name.value k.Name, k)
        |> Map.ofList

    let private kindByKey (catalog: Catalog) : Map<SsKey, Kind> =
        Catalog.allKinds catalog
        |> List.map (fun k -> k.SsKey, k)
        |> Map.ofList

    let private attributeByName (kind: Kind) (column: string) : Attribute option =
        kind.Attributes |> List.tryFind (fun a -> Name.value a.Name = column)

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

    let private writeOptionsLabeled
        (sb: StringBuilder)
        (option2Label: string)
        (selectStmt: string)
        (updateStmt: string)
        (deleteStmt: string)
        : unit =
        sb.AppendLine("-- OPTION 1 (active): inspect the offending rows") |> ignore
        sb.AppendLine(selectStmt) |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine(sprintf "-- OPTION 2: %s" option2Label) |> ignore
        sb.AppendLine(sprintf "-- %s" updateStmt) |> ignore
        sb.AppendLine() |> ignore
        sb.AppendLine("-- OPTION 3: delete the offending rows (operator: confirm row removal)") |> ignore
        sb.AppendLine(sprintf "-- %s" deleteStmt) |> ignore
        sb.AppendLine() |> ignore

    let private writeOptions
        (sb: StringBuilder)
        (selectStmt: string)
        (updateStmt: string)
        (deleteStmt: string)
        : unit =
        writeOptionsLabeled sb "set to default (operator: confirm the default value)" selectStmt updateStmt deleteStmt

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

    // -- Data-reality renderers (2026-07-06) --------------------------------

    /// The reality-block header — the sibling of `writeHeader` for a finding
    /// that has NO intervention behind it (the fidelity report measured the
    /// source data against the declared model; no tightening decision fired).
    let private writeRealityHeader
        (sb: StringBuilder)
        (subject: string)
        (axis: string)
        (reason: string)
        : unit =
        sb.AppendLine("-- =================================================================") |> ignore
        sb.AppendLine(sprintf "-- Remediation: %s (data reality: %s)" subject axis) |> ignore
        sb.AppendLine(sprintf "-- Reason: %s" reason) |> ignore
        sb.AppendLine("-- =================================================================") |> ignore

    let private subjectOf (kind: Kind) (attr: Attribute) : string =
        sprintf "%s.%s" (Name.value kind.Name) (Name.value attr.Name)

    /// Nulls observed in a column the model already declares NOT NULL — the
    /// load-blocking case (an INSERT of the source rows fails the constraint).
    let private renderRealityNulls
        (sb: StringBuilder)
        (kind: Kind)
        (attr: Attribute)
        (nullCount: int64)
        : unit =
        let table  = qualifiedTable kind
        let column = brackets (ColumnRealization.columnNameText attr.Column)
        let reason =
            if nullCount > 0L then
                sprintf "Declared NOT NULL; %d null value(s) observed in the source data. A data load fails on this column until the nulls are repaired." nullCount
            else
                "Declared NOT NULL; null values observed in the source data (count unknown). A data load fails on this column until the nulls are repaired."
        writeRealityHeader sb (subjectOf kind attr) "nulls in a NOT NULL column" reason
        let selectStmt = sprintf "SELECT * FROM %s WHERE %s IS NULL;" table column
        let updateStmt = sprintf "UPDATE %s SET %s = <DEFAULT> WHERE %s IS NULL;" table column column
        let deleteStmt = sprintf "DELETE FROM %s WHERE %s IS NULL;" table column
        writeOptions sb selectStmt updateStmt deleteStmt

    /// Duplicates observed under a UNIQUE / PK-backed declaration.
    let private renderRealityDuplicates
        (sb: StringBuilder)
        (kind: Kind)
        (attr: Attribute)
        : unit =
        let table  = qualifiedTable kind
        let column = brackets (ColumnRealization.columnNameText attr.Column)
        writeRealityHeader sb (subjectOf kind attr) "duplicates in a unique column"
            "Declared unique (a unique index or primary key backs the column); duplicate values observed in the source data."
        let selectStmt =
            sprintf "SELECT %s, COUNT(*) AS RowCount FROM %s GROUP BY %s HAVING COUNT(*) > 1;" column table column
        let updateStmt =
            "Cannot UPDATE blindly; identify a canonical row per group and re-key the duplicates."
        let deleteStmt =
            sprintf
                "DELETE FROM (SELECT *, ROW_NUMBER() OVER (PARTITION BY %s ORDER BY <CanonicalKey>) AS rn FROM %s) WHERE rn > 1;"
                column table
        writeOptionsLabeled sb "re-key the duplicates to distinct values (operator: choose the canonical row)"
            selectStmt updateStmt deleteStmt

    /// Source rows whose relationship value points at a target record that
    /// does not exist. When the reference and its target's single-column
    /// primary key resolve through the catalog, the statements are concrete;
    /// otherwise the target placeholders remain for the operator to fill.
    let private renderRealityOrphans
        (sb: StringBuilder)
        (kindsByKey: Map<SsKey, Kind>)
        (kind: Kind)
        (attr: Attribute)
        (orphanCount: int64)
        : unit =
        let table  = qualifiedTable kind
        let column = brackets (ColumnRealization.columnNameText attr.Column)
        let target =
            kind.References
            |> List.tryFind (fun r -> r.SourceAttribute = attr.SsKey)
            |> Option.bind (fun r -> Map.tryFind r.TargetKind kindsByKey)
            |> Option.bind (fun targetKind ->
                match targetKind.Attributes |> List.filter (fun a -> a.IsPrimaryKey) with
                | [ pk ] -> Some (qualifiedTable targetKind, brackets (ColumnRealization.columnNameText pk.Column))
                | _      -> None)
        let reason =
            sprintf "%d source row(s) reference a target record that does not exist. A data load fails on the relationship until the rows are re-pointed or removed." orphanCount
        writeRealityHeader sb (subjectOf kind attr) "relationship values without a matching record" reason
        let selectStmt, updateStmt, deleteStmt =
            match target with
            | Some (targetTable, targetPk) ->
                sprintf "SELECT * FROM %s WHERE %s IS NOT NULL AND %s NOT IN (SELECT %s FROM %s);" table column column targetPk targetTable,
                sprintf "UPDATE %s SET %s = <VALID_TARGET_KEY> WHERE %s IS NOT NULL AND %s NOT IN (SELECT %s FROM %s);" table column column column targetPk targetTable,
                sprintf "DELETE FROM %s WHERE %s IS NOT NULL AND %s NOT IN (SELECT %s FROM %s);" table column column targetPk targetTable
            | None ->
                sprintf "SELECT * FROM %s WHERE %s IS NOT NULL;" table column,
                sprintf "UPDATE %s SET %s = <VALID_TARGET_KEY> WHERE %s NOT IN (SELECT <TargetPK> FROM <TargetTable>);" table column column,
                sprintf "DELETE FROM %s WHERE %s NOT IN (SELECT <TargetPK> FROM <TargetTable>);" table column
        writeOptionsLabeled sb "re-point the rows at a valid target record (operator: confirm the key)"
            selectStmt updateStmt deleteStmt

    /// Values longer than the column's declared cap — the load truncates or
    /// fails until the values fit (or the declaration widens).
    let private renderRealityOverflow
        (sb: StringBuilder)
        (kind: Kind)
        (attr: Attribute)
        (observed: string)
        (declared: string)
        : unit =
        let table  = qualifiedTable kind
        let column = brackets (ColumnRealization.columnNameText attr.Column)
        let reason =
            sprintf "Values up to %s character(s) observed; the declared cap is %s. A data load truncates or fails until the values fit (or the declared length widens)." observed declared
        writeRealityHeader sb (subjectOf kind attr) "values past the declared length" reason
        let selectStmt = sprintf "SELECT * FROM %s WHERE LEN(%s) > %s;" table column declared
        let updateStmt = sprintf "UPDATE %s SET %s = LEFT(%s, %s) WHERE LEN(%s) > %s;" table column column declared column declared
        let deleteStmt = sprintf "DELETE FROM %s WHERE LEN(%s) > %s;" table column declared
        writeOptionsLabeled sb "truncate to the declared cap (operator: confirm the loss of the overflow)"
            selectStmt updateStmt deleteStmt

    /// Build the manifest.remediation.sql text from the three DecisionSets
    /// plus the fidelity report's data-reality findings (2026-07-06).
    /// Deterministic ordering: per-axis decisions are emitted in their stored
    /// chronological order (the writer preserves A24 — earliest-first under
    /// One estate remediation block (`check estate`'s per-environment
    /// artifact, wave A5): the block id IS the finding's cross-artifact key
    /// (`FindingKey.text` — the board's lever, the burndown, and this block
    /// say one token and mean one thing), the finding statement rides as
    /// context, the locating SELECT is active, and every repair candidate is
    /// commented out. Core-only inputs by construction — the pipeline
    /// resolves coordinates and shapes the SQL; this module owns the block
    /// grammar and the artifact stitching.
    type EstateBlock =
        {
            /// The readable label the block leads with — `<subject> (<phrase>)`,
            /// e.g. `Order.CustomerId (orphan references)` (the board lever
            /// names the same label, so the two locate each other in plain
            /// words).
            Title     : string
            /// The cross-artifact machine key (the finding's token on the
            /// board and in estate.json) — carried as a searchable comment
            /// beneath the label, never the operator's headline.
            BlockId   : string
            Statement : string
            Locate    : string
            Repairs   : string list
        }

    /// Stitch one environment's estate remediation artifact: the provenance
    /// header lines (RT-12 — the wrong-environment mistake is structurally
    /// detectable), the reading rule, then one block per finding. An empty
    /// block list renders the empty-surface note (never a zero-byte file).
    let emitEstate (headerLines: string list) (blocks: EstateBlock list) : string =
        use _ = Bench.scope "emit.remediation.estate"
        let sb = StringBuilder()
        for line in headerLines do
            sb.AppendLine(line) |> ignore
        sb.AppendLine("-- The locating SELECT in each block is active; every repair is commented out.") |> ignore
        sb.AppendLine("-- Read each block before uncommenting any destructive action. Each block leads") |> ignore
        sb.AppendLine("-- with its readable label; the key beneath it is the finding's token on the") |> ignore
        sb.AppendLine("-- estate board and in estate.json.") |> ignore
        sb.AppendLine() |> ignore
        match blocks with
        | [] ->
            sb.AppendLine("-- No prepared repairs for this environment this run.") |> ignore
        | _ ->
            for block in blocks do
                sb.AppendLine(System.String.Concat("-- Block: ", block.Title)) |> ignore
                sb.AppendLine(System.String.Concat("-- key: ", block.BlockId)) |> ignore
                sb.AppendLine(System.String.Concat("-- ", block.Statement)) |> ignore
                sb.AppendLine(block.Locate) |> ignore
                for repair in block.Repairs do
                    sb.AppendLine(System.String.Concat("-- ", repair)) |> ignore
                sb.AppendLine() |> ignore
        sb.ToString()

    /// bind); axes themselves emit in fixed Nullability → ForeignKey →
    /// UniqueIndex order; the data-reality section follows in the fidelity
    /// report's sorted order, deduplicated against the decision blocks on
    /// (entity, column, axis).
    let emitWith
        (catalog: Catalog)
        (nullability: NullabilityDecisionSet)
        (uniqueIndex: UniqueIndexDecisionSet)
        (foreignKey: ForeignKeyDecisionSet)
        (findings: DataRealityFinding list)
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
        // The decision blocks' (entity, column, axis) coverage — a fidelity
        // finding on the same coordinate is the same repair, stated twice.
        let covered = System.Collections.Generic.HashSet<string * string * string>()

        for decision in nullability.Decisions do
            match decision.Outcome with
            | NullabilityOutcome.RequireOperatorApproval
                  (MandatoryButHasNullsBeyondBudget (nullCount, rowCount, budget)) ->
                match Map.tryFind decision.AttributeKey attrIndex with
                | Some (kind, attr) ->
                    renderNullabilityConflict
                        sb kind attr decision.InterventionId
                        nullCount rowCount budget
                    covered.Add(Name.value kind.Name, Name.value attr.Name, "nulls") |> ignore
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
                    (match Map.tryFind reference.SourceAttribute attrIndex with
                     | Some (_, attr) -> covered.Add(Name.value kind.Name, Name.value attr.Name, "orphans") |> ignore
                     | None -> ())
                    count <- count + 1
                | None -> ()
            | _ -> ()

        for decision in uniqueIndex.Decisions do
            match decision.Outcome with
            | UniqueIndexOutcome.DoNotEnforce DataHasDuplicates ->
                match Map.tryFind decision.IndexKey idxIndex with
                | Some (kind, idx) ->
                    renderUniqueIndexDuplicates sb kind idx decision.InterventionId attrIndex
                    for ic in idx.Columns do
                        match Map.tryFind ic.Attribute attrIndex with
                        | Some (_, attr) -> covered.Add(Name.value kind.Name, Name.value attr.Name, "duplicates") |> ignore
                        | None -> ()
                    count <- count + 1
                | None -> ()
            | _ -> ()

        // -- the data-reality section (the fidelity report's findings) ------
        let fresh =
            findings
            |> List.filter (fun f -> not (covered.Contains(f.Entity, f.Column, axisOf f.Kind)))
        if not (List.isEmpty fresh) then
            let kindsByName = kindByEntityName catalog
            let kindsByKey  = kindByKey catalog
            sb.AppendLine("-- =================================================================") |> ignore
            sb.AppendLine("-- Data reality — the source data measured against the declared model") |> ignore
            sb.AppendLine("-- (the findings fidelity.json counts). Each block locates the offending") |> ignore
            sb.AppendLine("-- rows; a data load can fail until they are repaired.") |> ignore
            sb.AppendLine("-- =================================================================") |> ignore
            sb.AppendLine() |> ignore
            for finding in fresh do
                match Map.tryFind finding.Entity kindsByName with
                | None -> ()
                | Some kind ->
                    match attributeByName kind finding.Column with
                    | None -> ()
                    | Some attr ->
                        (match finding.Kind with
                         | NullsInNotNullColumn nullCount   -> renderRealityNulls sb kind attr nullCount
                         | DuplicatesInUniqueColumn         -> renderRealityDuplicates sb kind attr
                         | OrphanedReference orphanCount    -> renderRealityOrphans sb kindsByKey kind attr orphanCount
                         | ValueOverflow (observed, declared) -> renderRealityOverflow sb kind attr observed declared)
                        count <- count + 1

        if count = 0 then
            sb.AppendLine("-- No remediation candidates surfaced. All decisions either tightened cleanly") |> ignore
            sb.AppendLine("-- or kept the prior state without operator-attention findings, and the") |> ignore
            sb.AppendLine("-- profiled source data carries no violation of the declared model.") |> ignore

        sb.ToString()

    /// The DecisionSets-only projection (the pre-2026-07-06 surface, kept for
    /// callers without a fidelity report in hand).
    let emit
        (catalog: Catalog)
        (nullability: NullabilityDecisionSet)
        (uniqueIndex: UniqueIndexDecisionSet)
        (foreignKey: ForeignKeyDecisionSet)
        : string =
        emitWith catalog nullability uniqueIndex foreignKey []

    /// `RegisteredTransform` metadata view per the pillar 9 +
    /// L3-CC-Transform-Totality discipline. Classifies as
    /// `DataIntent` — the remediation projection is a structural
    /// view of empirical evidence (`DecisionSet` outcomes carry the
    /// observed null / orphan / duplicate counts).
    let registeredMetadata : RegisteredTransformMetadata =
        RegisteredTransformMetadata.emitter "remediationEmitter" Diagnostics
            [ TransformSite.dataIntent "remediationOptions"
                "Project Nullability/ForeignKey/UniqueIndex DecisionSet outcomes carrying operator-attention findings (RequireOperatorApproval / DataHasOrphans / DataHasDuplicates), plus the fidelity report's data-reality violations (nulls in NOT NULL columns / duplicates / orphans / length overflow, deduplicated against the decision blocks), into per-finding UPDATE/DELETE/SELECT options in manifest.remediation.sql. Operator-safety contract: only SELECT active; UPDATE + DELETE commented-out by default." ]
