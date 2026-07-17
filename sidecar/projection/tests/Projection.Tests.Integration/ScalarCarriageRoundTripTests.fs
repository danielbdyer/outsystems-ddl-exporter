module Projection.Tests.ScalarCarriageRoundTripTests

// ============================================================================
// WP-17(f) (DECISIONS 2026-07-16) — the scalar-carriage fixture backlog: the
// audit's UNWITNESSED concrete types round-trip on a REAL SQL Server, through
// the operator's actual artifacts (DacFx-published DDL + the composed
// static-seed MERGE). One gallery kind carries every contested column:
//
//   float / real          — WP-17(a): G17/G9 raws; the E-notation literal
//                            lands the exact IEEE value (incl. Double.Max —
//                            the pre-fix Decimal carrier OVERFLOWED).
//   datetimeoffset(7)     — WP-17(b): the offset-bearing raw → CAST(… AS
//                            datetimeoffset(7)); the offset survives.
//   xml                   — WP-17(c): content carriage; the CDC-aware MERGE
//                            COMPILES (pre-guard, `Target.[x] <> Source.[x]`
//                            on xml was a compile error — S5) and re-runs.
//   money / smalldatetime / image — the collapse-is-faithful verdicts of
//                            audit §4, now test-proven.
//   control-char text     — WP-17(e): the CHAR() splice evaluates to the
//                            identical stored value.
//
// The kind is CDC-ENABLED (Profile.CdcAwareness) so the change-detect
// predicate — the S5 hazard site — is IN the executed SQL, and the seed is
// executed TWICE (the idempotent re-run must compile and change nothing).
// ============================================================================

open System.IO
open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Microsoft.SqlServer.Dac
open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Targets.SSDT
open Projection.Targets.Data
open Projection.Tests.Fixtures   // mkName, mkTableId

[<RequireQualifiedAccess>]
module private ScalarGallery =

    let mkKey (parts: string list) : SsKey =
        SsKey.synthesizedComposite "OS_SCR" parts |> Result.value

    let col (physical: string) : ColumnRealization =
        ColumnRealization.create physical true |> Result.value

    let skipIfNoDocker (label: string) : bool =
        if Deploy.Docker.ensureRunning () then true
        else printfn "SKIP %s: Docker daemon not reachable." label; false

    let scalar (cnn: SqlConnection) (sql: string) : Task<string> =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- sql
            let! v = cmd.ExecuteScalarAsync()
            return string v
        }

    let galleryKey = mkKey [ "Scalars"; "Gallery" ]

    /// The contested raws, named once — the row AND the asserts share them.
    let floatRaw = "1.7976931348623157E+308"
    let realRaw = "3.4028235E+38"
    let dtoRaw = "2026-07-16 12:30:00.0000000 -03:00"
    let xmlRaw = "<a b=\"1\">x</a>"
    let moneyRaw = "922337203685477.5807"
    let sdtRaw = "2026-07-16 12:30:00.0000000"
    let imageRaw = "CAFEBABE"
    let ctrlRaw = "line1\r\nline2"

    let catalog () : Catalog =
        let attr (label: string) (physical: string) (t: PrimitiveType) (storage: SqlStorageType option) : Attribute =
            { Attribute.create (mkKey [ "Scalars"; "Gallery"; label ]) (mkName label) t with
                Column = col physical
                SqlStorage = storage }
        let row =
            { Identifier = mkKey [ "Scalars"; "Gallery"; "Row"; "1" ]
              Values =
                StaticRow.presentValues
                    [ mkName "Id",      "1"
                      mkName "FloatV",  floatRaw
                      mkName "RealV",   realRaw
                      mkName "DtoV",    dtoRaw
                      mkName "XmlV",    xmlRaw
                      mkName "MoneyV",  moneyRaw
                      mkName "SdtV",    sdtRaw
                      mkName "ImageV",  imageRaw
                      mkName "CtrlV",   ctrlRaw ] }
        let kind : Kind =
            { SsKey = galleryKey; Name = mkName "Gallery"; Origin = Native
              Modality = [ Static [ row ] ]
              Physical = mkTableId "dbo" "OSUSR_SCR_GALLERY"
              Attributes =
                [ { Attribute.create (mkKey [ "Scalars"; "Gallery"; "Id" ]) (mkName "Id") Integer with
                      Column = ColumnRealization.create "ID" false |> Result.value
                      IsPrimaryKey = true
                      IsMandatory = true }
                  attr "FloatV" "FLOATV" Decimal (Some SqlStorageType.Float)
                  attr "RealV"  "REALV"  Decimal (Some SqlStorageType.Real)
                  attr "DtoV"   "DTOV"   DateTime (Some (SqlStorageType.DateTimeOffset (Some 7)))
                  attr "XmlV"   "XMLV"   Text (Some SqlStorageType.Xml)
                  attr "MoneyV" "MONEYV" Decimal (Some SqlStorageType.Money)
                  attr "SdtV"   "SDTV"   DateTime (Some SqlStorageType.SmallDateTime)
                  attr "ImageV" "IMAGEV" Binary (Some SqlStorageType.Image)
                  attr "CtrlV"  "CTRLV"  Text None ]
              References = []; Indexes = []; Description = None; IsActive = true
              Triggers = []; ColumnChecks = []; ExtendedProperties = [] }
        { Modules =
            [ { SsKey = mkKey [ "Scalars" ]; Name = mkName "Scalars"; Kinds = [ kind ]; IsActive = true; ExtendedProperties = [] } ]
          Sequences = [] }

    /// CDC-aware profile — arms the change-detect predicate (the S5 site).
    let cdcProfile () : Profile =
        { Profile.empty with
            CdcAwareness = CdcAwareness.create (Set.singleton galleryKey) Map.empty }

    let deploySchema (connStr: string) (catalog: Catalog) : unit =
        let dbName = SqlConnectionStringBuilder(connStr).InitialCatalog
        let bytes =
            match DacpacEmitter.emit catalog with
            | Ok b -> b
            | Error es -> failwithf "dacpac emit failed: %A" es
        use stream = new MemoryStream(bytes)
        use package = DacPackage.Load stream
        (DacServices connStr).Deploy(package, dbName, true, DacDeployOptions())

    let staticSeeds (catalog: Catalog) : string =
        let policy = { Policy.empty with Emission = EmissionPolicy.combined }
        match
            DataEmissionComposer.composeRenderedBundleWithBootstrap
                policy catalog (cdcProfile ()) MigrationDependencyContext.empty Map.empty UserRemapContext.empty
        with
        | Ok b -> b.StaticSeeds
        | Error e -> failwithf "data compose failed: %A" e

[<Xunit.Collection("Docker-SqlServer")>]
type ScalarCarriageRoundTripTests(fixture: EphemeralContainerFixture) =
    interface IClassFixture<EphemeralContainerFixture>

    [<Fact>]
    member _.``WP-17f: the contested scalar gallery round-trips through DacFx DDL + the CDC-aware seed MERGE — twice`` () =
        if not (ScalarGallery.skipIfNoDocker "ScalarCarriage") then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "ScalarCarriage" (fun cnn connStr ->
                task {
                    let catalog = ScalarGallery.catalog ()
                    // DDL leg: the storage-evidence lane deploys the concrete
                    // types through DacFx (FLOAT/REAL/DATETIMEOFFSET(7)/XML/
                    // MONEY/SMALLDATETIME/IMAGE on a real server).
                    ScalarGallery.deploySchema connStr catalog
                    // Data leg: the composed CDC-aware seed MERGE — the xml
                    // change-detect guard (S5) is in this SQL; executing it IS
                    // the compile proof.
                    let seeds = ScalarGallery.staticSeeds catalog
                    do! Deploy.executeBatch cnn seeds
                    let probe (label: string) (sql: string) : Task<unit> =
                        task {
                            let! verdict = ScalarGallery.scalar cnn sql
                            Assert.True(("ok" = verdict), sprintf "%s: server-side compare failed (got %s)" label verdict)
                        }
                    let assertAll () : Task<unit> =
                        task {
                            // WP-17(a) — the exact IEEE values landed (the pre-fix
                            // Decimal carrier overflowed on Double.Max).
                            do! probe "float" "SELECT CASE WHEN [FloatV] = 1.7976931348623157E+308 THEN 'ok' ELSE 'lost' END FROM [dbo].[OSUSR_SCR_GALLERY]"
                            do! probe "real" "SELECT CASE WHEN [RealV] = CAST(3.4028235E+38 AS REAL) THEN 'ok' ELSE 'lost' END FROM [dbo].[OSUSR_SCR_GALLERY]"
                            // WP-17(b) — the offset survived, verbatim (style 121).
                            do! probe "datetimeoffset" "SELECT CASE WHEN CONVERT(VARCHAR(40), [DtoV], 121) = '2026-07-16 12:30:00.0000000 -03:00' THEN 'ok' ELSE 'lost' END FROM [dbo].[OSUSR_SCR_GALLERY]"
                            // WP-17(c) — xml content carriage (server re-serializes;
                            // content equality is the deliberate semantic).
                            do! probe "xml" "SELECT CASE WHEN [XmlV].value('(/a/@b)[1]','INT') = 1 AND [XmlV].value('(/a/text())[1]','NVARCHAR(10)') = 'x' THEN 'ok' ELSE 'lost' END FROM [dbo].[OSUSR_SCR_GALLERY]"
                            do! probe "money" "SELECT CASE WHEN [MoneyV] = 922337203685477.5807 THEN 'ok' ELSE 'lost' END FROM [dbo].[OSUSR_SCR_GALLERY]"
                            do! probe "smalldatetime" "SELECT CASE WHEN [SdtV] = '2026-07-16 12:30:00' THEN 'ok' ELSE 'lost' END FROM [dbo].[OSUSR_SCR_GALLERY]"
                            do! probe "image" "SELECT CASE WHEN CONVERT(VARCHAR(20), CAST([ImageV] AS VARBINARY(20)), 1) = '0xCAFEBABE' THEN 'ok' ELSE 'lost' END FROM [dbo].[OSUSR_SCR_GALLERY]"
                            // WP-17(e) — the CHAR() splice evaluated to the identical
                            // stored value (raw CR/LF round-trip).
                            do! probe "control-chars" "SELECT CASE WHEN [CtrlV] = N'line1' + CHAR(13) + CHAR(10) + N'line2' THEN 'ok' ELSE 'lost' END FROM [dbo].[OSUSR_SCR_GALLERY]"
                        }
                    do! assertAll ()
                    // The idempotent re-run: compiles (the S5 proof lives here),
                    // inserts nothing, changes nothing.
                    do! Deploy.executeBatch cnn seeds
                    let! count = ScalarGallery.scalar cnn "SELECT COUNT(*) FROM [dbo].[OSUSR_SCR_GALLERY]"
                    Assert.Equal("1", count)
                    do! assertAll ()
                }))
