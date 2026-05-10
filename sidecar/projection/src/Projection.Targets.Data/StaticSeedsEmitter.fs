namespace Projection.Targets.Data

open Projection.Core
open Projection.Targets.SSDT  // LINT-ALLOW: cross-target dependency for ScriptDom MERGE typed-AST construction (`ScriptDomBuild.buildMergeStatement` + `buildSqlLiteral`) per the Tier-1 #1 transition (RawTextEmitter retirement arc cash-out); the typed AST flows through `ScriptDomGenerate.generateOne` for canonical SQL-text rendering; same architectural shape that SsdtDdlEmitter uses (chapter 4.1.A)

/// Π_StaticSeeds — chapter 4.1.B slice α emitter for static-modality
/// kinds. Consumes the `Catalog`'s `Modality.Static` populations and
/// produces idempotent MERGE statements per V1 trunk's `StaticSeed
/// SqlBuilder.cs:211-260` shape (V1 parity at slice α; the change-
/// detection predicate that closes CDC-noise lands at slice β).
///
/// **A18 amended.** The signature carries `Catalog × Profile`; Profile
/// is reserved for the slice-β `CdcAwareness` field consumption. No
/// `Policy` parameter — DataComposition dispatch happens in the
/// composer (slice η), not here.
///
/// **T11 sibling-Π commutativity.** The emitter produces an
/// `ArtifactByKind<DataInsertScript>` keyed by every catalog kind.
/// Kinds without `Modality.Static` produce a script with empty
/// `Phase1Merges` (no-op artifact) — per the strict-equality T11
/// invariant: every kind appears, no kind is silently absent.
[<RequireQualifiedAccess>]
module StaticSeedsEmitter =

    [<Literal>]
    let version : int = 1

    /// Collect the `StaticRow list` from a kind's `Modality` marks.
    /// A kind may carry multiple `ModalityMark` variants; only `Static`
    /// is consumed here. Returns `[]` for kinds without static
    /// populations.
    let private staticPopulations (k: Kind) : StaticRow list =
        k.Modality
        |> List.tryPick (fun m ->
            match m with
            | Static populations -> Some populations
            | _                  -> None)
        |> Option.defaultValue []

    /// Type-resolution lookup for a kind's columns. Returns the
    /// (column-name, primitive-type) pair for each attribute, so the
    /// renderer can format raw IR values as SQL literals.
    let private columnTypeLookup (k: Kind) : Map<Name, PrimitiveType> =
        k.Attributes
        |> List.map (fun a -> a.Name, a.Type)
        |> Map.ofList

    /// Order columns deterministically (matches V1 + the SSDT emitter).
    /// Per A33 (deterministic-ordered schema emission), sort by the
    /// kind's declared attribute order — which is itself canonical
    /// after `CanonicalizeIdentity`.
    let private orderedColumnNames (k: Kind) : string list =
        k.Attributes |> List.map (fun a -> a.Column.ColumnName)

    /// Primary-key column names in the kind's declared order. The
    /// MERGE's ON-clause joins on these; the WHEN-NOT-MATCHED INSERT
    /// includes them; the WHEN-MATCHED UPDATE excludes them (PK is
    /// stable per row identity).
    let private pkColumnNames (k: Kind) : string list =
        k.Attributes
        |> List.filter (fun a -> a.IsPrimaryKey)
        |> List.map (fun a -> a.Column.ColumnName)

    /// Non-PK column names (the MERGE's UPDATE-target columns).
    let private updatableColumnNames (k: Kind) : string list =
        k.Attributes
        |> List.filter (fun a -> not a.IsPrimaryKey)
        |> List.map (fun a -> a.Column.ColumnName)

    /// Project one StaticRow into the typed `SqlLiteral list` form
    /// that `MergeBuildArgs.Rows` expects. Iterates the kind's
    /// attributes in declared order; missing values default to NULL
    /// (V2 IR's empty-raw sentinel per `RawValueCodec`).
    let private rowToSqlLiterals
        (typeLookup: Map<Name, PrimitiveType>)
        (attributes: Attribute list)
        (row: StaticRow)
        : SqlLiteral list =
        attributes
        |> List.map (fun a ->
            let raw =
                Map.tryFind a.Name row.Values
                |> Option.defaultValue ""
            let typ =
                Map.tryFind a.Name typeLookup
                |> Option.defaultValue PrimitiveType.Text
            SqlLiteral.ofRaw typ raw)

    /// Render the MERGE statement for a kind with its static populations
    /// via ScriptDom's typed-AST + `Sql160ScriptGenerator` pipeline.
    /// Per Tier-1 #1 (RawTextEmitter retirement arc cash-out): the
    /// hand-rolled StringBuilder MERGE construction (with 6 LINT-ALLOWs)
    /// retires in favor of `ScriptDomBuild.buildMergeStatement` —
    /// every node typed, every literal flowing through `SqlLiteral`,
    /// no terminal text composition until the writer boundary.
    ///
    /// Mirrors V1's `StaticSeedSqlBuilder.AppendMergeStatement`
    /// (`StaticSeedSqlBuilder.cs:211-260`) modulo ScriptDom's canonical
    /// formatting (newlines / wrapping). The change-detection predicate
    /// per chapter 4.1.B slice β + pre-scope §6 lands as typed
    /// `BooleanBinaryExpression` / `BooleanIsNullExpression` /
    /// `BooleanComparisonExpression` AST nodes.
    let private renderMerge
        (cdcAware: bool)
        (k: Kind)
        (rows: StaticRow list)
        : string =
        use _ = Bench.scope "emit.staticSeeds.renderMerge"
        let table : TableId =
            { Schema = k.Physical.Schema
              Table  = k.Physical.Table }
        let typeLookup = columnTypeLookup k
        let args : ScriptDomBuild.MergeBuildArgs =
            {
                Target     = table
                AllColumns = orderedColumnNames k
                PkColumns  = pkColumnNames k
                UpdColumns = updatableColumnNames k
                Rows       = rows |> List.map (rowToSqlLiterals typeLookup k.Attributes)
                CdcAware   = cdcAware
            }
        let mergeStmt = ScriptDomBuild.buildMergeStatement args
        // ScriptDomGenerate.generateOne emits the canonical
        // Sql160ScriptGenerator output; trailing GO batches the
        // statement per V1 deploy convention.
        // ScriptDomGenerate.generateOne emits the MERGE without a
        // trailing `;` (semicolons appear between statements in a
        // batch, not after a single-statement render). SQL Server
        // REQUIRES MERGE to terminate with `;` (SqlException: "A
        // MERGE statement must be terminated by a semi-colon (;)").
        // The terminal-text boundary appends `;` + `GO`.
        System.String.Concat(  // LINT-ALLOW: terminal MERGE statement-terminator + GO-batch suffix on the rendered MERGE; segments are typed (output of `ScriptDomGenerate.generateOne` from typed AST + SQL Server's required MERGE statement-terminator + V1 batch-separator literal); BCL `String.Concat` is the right primitive at this terminal-text boundary
            ScriptDomGenerate.generateOne (mergeStmt :> Microsoft.SqlServer.TransactSql.ScriptDom.TSqlStatement),
            ";\nGO\n")

    /// Build one `DataInsertScript` for a kind. Empty-population kinds
    /// produce a no-op script (empty Phase1Merges, empty Rendered);
    /// per T11 strict-equality keyset, the script is still keyed in
    /// the artifact map. CDC-aware dispatch per slice β: the kind's
    /// `Profile.CdcAwareness.CdcEnabled` membership selects the
    /// change-detection-predicate variant.
    let private kindToScript (cdc: CdcAwareness) (k: Kind) : DataInsertScript =
        let populations = staticPopulations k
        if List.isEmpty populations then
            { Phase1Merges = []; Phase2Updates = []; Rendered = "" }
        else
            let cdcAware = CdcAwareness.isEnabled k.SsKey cdc
            let rendered = renderMerge cdcAware k populations
            let rows =
                populations
                |> List.map (fun row ->
                    { KindKey    = k.SsKey
                      Identifier = row.Identifier
                      Values     = row.Values })
            { Phase1Merges = rows
              Phase2Updates = []
              Rendered     = rendered }

    /// Π_StaticSeeds emit. Per A18 amended (Catalog × Profile, never
    /// Policy) and T11 (every kind in the keyset). Slice β consumes
    /// `Profile.CdcAwareness` for per-kind change-detection-predicate
    /// dispatch (the load-bearing semantic addition that closes
    /// CDC-noise on idempotent redeploys per `V2_DRIVER.md`).
    let emit
        (catalog: Catalog)
        (profile: Profile)
        : Result<ArtifactByKind<DataInsertScript>, EmitError> =
        use _ = Bench.scope "emit.staticSeeds.emit"
        let cdc = profile.CdcAwareness
        let allKinds = Catalog.allKinds catalog
        let slices =
            allKinds
            |> List.map (fun k -> k.SsKey, kindToScript cdc k)
            |> Map.ofList
        ArtifactByKind.create catalog slices
