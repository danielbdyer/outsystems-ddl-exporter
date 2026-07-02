module Projection.Tests.IndexPhysicalMetadataDeployE2ETests

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Targets.SSDT
open Projection.Tests.Fixtures   // mkName, mkTableId

// ============================================================================
// E2E (Docker): index PHYSICAL metadata, proven on a REAL SQL Server against
// the catalog views — not a substring of the generated DDL.
//
// The whole index storage surface (DATA_COMPRESSION / FILLFACTOR / PAD_INDEX /
// ALLOW_ROW_LOCKS / ALLOW_PAGE_LOCKS / STATISTICS_NORECOMPUTE / IGNORE_DUP_KEY /
// disabled / filtered / INCLUDE columns) was verified ONLY by asserting a
// substring appears in `SsdtDdlEmitter.statements |> Render.toText`
// (IndexOnDiskMetadataTests / SsdtDdlEmitterTests). A malformed `WITH (...)`
// clause — or `DATA_COMPRESSION = PAGE` appearing in a COMMENT, or a `DISABLE`
// that no-ops — passes every one of those. And the PhysicalSchema round-trip
// canary is structurally blind to all of it (it compares only owner / name /
// uniqueness / key columns; the storage options are the named
// `ToleratedDivergence.IndexOptionsUnreflected` residual).
//
// This deploys the REAL emitted schema DDL and reads each option back from
// sys.indexes / sys.index_columns / sys.partitions / sys.stats — the deployed
// truth. Rides the `Docker-SqlServer` pool; soft-skips without Docker.
// ============================================================================

let private mkKey (parts: string list) : SsKey =
    SsKey.synthesizedComposite "OS_IDXE2E" parts |> Result.value

let private col (physical: string) : ColumnRealization =
    ColumnRealization.create physical false |> Result.value

let private scalar (cnn: SqlConnection) (sql: string) : Task<string> =
    task {
        use cmd = cnn.CreateCommand()
        cmd.CommandText <- sql
        let! v = cmd.ExecuteScalarAsync()
        return (if isNull v then "NULL" else string v)
    }

let private tableName = "OSUSR_IDXE2E_T"
let private kindKey = mkKey [ "T" ]
let private attrKey (c: string) = mkKey [ "T"; c ]

/// One index keyed on `keyCol`, named `name`, transformed by `f` (the option
/// under test). All share the one table below.
let private idx (name: string) (keyCol: string) (f: Index -> Index) : Index =
    Index.create (mkKey [ "T"; "IX"; name ]) (mkName name) (IndexColumn.ascendingList [ attrKey keyCol ])
    |> f

// A single table (Id PK identity; Code/Label text; Status int) carrying one
// index per physical option — each non-default so its WITH clause / disable /
// filter / INCLUDE actually emits and must survive the deploy.
let private indexCatalog () : Catalog =
    let attr (c: string) (physical: string) (ty: PrimitiveType) (extra: Attribute -> Attribute) : Attribute =
        { Attribute.create (attrKey c) (mkName c) ty with Column = col physical } |> extra
    let kind : Kind =
        { SsKey = kindKey; Name = mkName "T"; Origin = Native; Modality = []
          Physical = mkTableId "dbo" tableName
          Attributes =
            // Index KEYS must be int (a Text/NVARCHAR(MAX) column is invalid as a
            // key column); Status/Rank are the indexable keys, Rank also the
            // INCLUDE column. Code/Label stay as realistic non-indexed columns.
            [ attr "Id"     "ID"     Integer (fun a -> { a with IsPrimaryKey = true; IsMandatory = true; IsIdentity = true })
              attr "Code"   "CODE"   Text    (fun a -> { a with IsMandatory = true })
              attr "Label"  "LABEL"  Text    id
              attr "Status" "STATUS" Integer (fun a -> { a with IsMandatory = true })
              attr "Rank"   "RANK"   Integer (fun a -> { a with IsMandatory = true }) ]
          References = []
          Indexes =
            [ idx "IX_COMPRESS"    "Status" (fun i -> { i with DataCompression = Some DataCompressionLevel.Page })
              idx "IX_FILL"        "Status" (fun i -> { i with FillFactor = Some 70 })
              idx "IX_PAD"         "Status" (fun i -> { i with IsPadded = true; FillFactor = Some 60 })
              idx "IX_NOROWLOCK"   "Status" (fun i -> { i with AllowRowLocks = false })
              idx "IX_NOPAGELOCK"  "Status" (fun i -> { i with AllowPageLocks = false })
              idx "IX_NORECOMPUTE" "Status" (fun i -> { i with NoRecomputeStatistics = true })
              idx "IX_IGNOREDUP"   "Status" (fun i -> { i with Uniqueness = Unique; IgnoreDuplicateKey = true })
              idx "IX_DISABLED"    "Status" (fun i -> { i with IsDisabled = true })
              idx "IX_FILTERED"    "Status" (fun i -> { i with Filter = Some "([STATUS] = 1)" })
              idx "IX_INCLUDED"    "Status" (fun i -> { i with IncludedColumns = [ attrKey "Rank" ] }) ]
          Description = None; IsActive = true; Triggers = []; ColumnChecks = []; ExtendedProperties = [] }
    { Modules = [ { SsKey = mkKey [ "Mod" ]; Name = mkName "IdxMod"; Kinds = [ kind ]; IsActive = true; ExtendedProperties = [] } ]
      Sequences = [] }

// The sys-catalog scalar probes — `i.<col>` for one named index on the table.
let private idxScalar (col: string) (indexName: string) : string =
    sprintf
        "SELECT CAST(%s AS NVARCHAR(128)) FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.%s') AND name = '%s';"
        col tableName indexName

// Deployed index names are the EMITTED logical form (`IndexNaming`), not the
// authored fixture names; resolve authored -> emitted through the same
// derivation the emitter uses (several fixture indexes share the Status key
// column, so the collision ordinals apply).
let private emittedNameOf (catalog: Catalog) (authored: string) : string =
    let kind = catalog.Modules |> List.collect (fun m -> m.Kinds) |> List.head
    let idx = kind.Indexes |> List.find (fun i -> Name.value i.Name = authored)
    IndexNaming.emittedNames DecisionOverlay.empty kind |> Map.find idx.SsKey

[<Xunit.Collection("Docker-SqlServer")>]
type IndexPhysicalMetadataDeployE2ETests(fixture: EphemeralContainerFixture) =
    interface IClassFixture<EphemeralContainerFixture>

    [<Fact>]
    member _.``E2E: every index physical option deploys to the real catalog-view state (not just a DDL substring)`` () =
        if not (Deploy.Docker.ensureRunning ()) then
            printfn "SKIP index physical-metadata E2E: Docker daemon not reachable."
        else
            let catalog = indexCatalog ()
            let schema = SsdtDdlEmitter.statements catalog |> Render.toText
            // Pre-flight: the options actually EMITTED (else the deploy is vacuous).
            Assert.Contains("DATA_COMPRESSION = PAGE", schema)
            Assert.Contains("FILLFACTOR = 70", schema)
            Assert.Contains("IGNORE_DUP_KEY = ON", schema)
            Assert.Contains("DISABLE", schema)
            TaskSync.run (fun () ->
                fixture.WithEphemeralDatabase "IndexPhysMeta" (fun cnn _ ->
                    task {
                        do! Deploy.executeBatch cnn schema

                        // FILLFACTOR / PAD_INDEX (PAD has its own FILLFACTOR=60).
                        let! fill = scalar cnn (idxScalar "fill_factor" (emittedNameOf catalog "IX_FILL"))
                        Assert.Equal("70", fill)
                        let! padded = scalar cnn (idxScalar "is_padded" (emittedNameOf catalog "IX_PAD"))
                        Assert.Equal("1", padded)

                        // Lock options OFF.
                        let! rowLocks = scalar cnn (idxScalar "allow_row_locks" (emittedNameOf catalog "IX_NOROWLOCK"))
                        Assert.Equal("0", rowLocks)
                        let! pageLocks = scalar cnn (idxScalar "allow_page_locks" (emittedNameOf catalog "IX_NOPAGELOCK"))
                        Assert.Equal("0", pageLocks)

                        // IGNORE_DUP_KEY ON (a unique index).
                        let! ignoreDup = scalar cnn (idxScalar "ignore_dup_key" (emittedNameOf catalog "IX_IGNOREDUP"))
                        Assert.Equal("1", ignoreDup)

                        // The disabled index actually deployed disabled.
                        let! disabled = scalar cnn (idxScalar "is_disabled" (emittedNameOf catalog "IX_DISABLED"))
                        Assert.Equal("1", disabled)

                        // Filtered index: has_filter + the predicate references STATUS.
                        let! hasFilter = scalar cnn (idxScalar "has_filter" (emittedNameOf catalog "IX_FILTERED"))
                        Assert.Equal("1", hasFilter)
                        let! filterDef = scalar cnn (idxScalar "filter_definition" (emittedNameOf catalog "IX_FILTERED"))
                        Assert.Contains("STATUS", filterDef)

                        // STATISTICS_NORECOMPUTE → the index's auto-stat carries no_recompute.
                        let! noRecompute =
                            scalar cnn
                                (sprintf
                                    "SELECT CAST(no_recompute AS NVARCHAR(8)) FROM sys.stats WHERE object_id = OBJECT_ID('dbo.%s') AND name = '%s';"
                                    tableName (emittedNameOf catalog "IX_NORECOMPUTE"))
                        Assert.Equal("1", noRecompute)

                        // DATA_COMPRESSION = PAGE → the index partition is PAGE-compressed.
                        let! compression =
                            scalar cnn
                                (sprintf
                                    "SELECT p.data_compression_desc FROM sys.partitions p JOIN sys.indexes i ON p.object_id = i.object_id AND p.index_id = i.index_id WHERE i.object_id = OBJECT_ID('dbo.%s') AND i.name = '%s';"
                                    tableName (emittedNameOf catalog "IX_COMPRESS"))
                        Assert.Equal("PAGE", compression)

                        // INCLUDE columns → exactly one included column on IX_INCLUDED (Label).
                        let! includedCount =
                            scalar cnn
                                (sprintf
                                    "SELECT COUNT(*) FROM sys.index_columns ic JOIN sys.indexes i ON ic.object_id = i.object_id AND ic.index_id = i.index_id WHERE i.object_id = OBJECT_ID('dbo.%s') AND i.name = '%s' AND ic.is_included_column = 1;"
                                    tableName (emittedNameOf catalog "IX_INCLUDED"))
                        Assert.Equal("1", includedCount)
                    }))
