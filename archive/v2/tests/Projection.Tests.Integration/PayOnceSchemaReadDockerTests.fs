namespace Projection.Tests

open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Adapters.Sql

// ---------------------------------------------------------------------------
// PL-7 (PAY_ONCE_PLAN) — schema-only reads where rows were never the point.
//
// The marking-semantics pin (survival rule 8's seam): `ReadSide.readSchema`
// must equal `ReadSide.read >> Catalog.stripStaticPopulations` — the SAME
// schema projection with the Static mark (and ONLY the Static mark) absent —
// over a real estate whose `read` genuinely mints Static (content-bearing,
// not vacuous). The consumers that switched (transfer contract S01,
// slice-apply S09, profile-capture S02) each consumed exactly that
// composition or a mark-blind projection of it, so this ONE equality is the
// identity gate for all three; the capture verb is additionally gated
// end-to-end both ways below. The preflight's scoped probe (S10) gets its
// own verdict-identity fact on a NULL-bearing fixture.
// ---------------------------------------------------------------------------

[<Xunit.Collection("Docker-SqlServer")>]
type PayOnceSchemaReadDockerTests(fixture: EphemeralContainerFixture) =

    let mustOk (r: Result<'a>) : 'a =
        match r with
        | Ok v -> v
        | Error es ->
            let detail =
                es
                |> List.map (fun e -> System.String.Concat(e.Code, ": ", e.Message))
                |> String.concat " | "
            invalidOp (System.String.Concat("expected Ok; got: ", detail))

    let isStaticMarked (k: Kind) : bool =
        k.Modality |> List.exists (function Static _ -> true | _ -> false)

    /// The PL-1/PL-2 edge-case estate + deterministic rows: City + Customer
    /// carry rows (so `read` mints Static), Customer.LEGACYCODE carries ONE
    /// NULL (row 1) — the tightening-violation seam the S10 fact probes.
    let seedEstate (cnn: Microsoft.Data.SqlClient.SqlConnection) =
        task {
            do! Deploy.executeBatch cnn (Projection.Adapters.OssysSql.MetadataExtractionSql.readEdgeCaseSeed ())
            do!
                Deploy.executeBatch cnn
                    ("SET IDENTITY_INSERT [dbo].[OSUSR_DEF_CITY] ON; " +
                     "INSERT INTO [dbo].[OSUSR_DEF_CITY] ([ID],[NAME],[ISACTIVE]) " +
                     "VALUES (1, N'Springfield', 1), (2, N'Shelbyville', 1); " +
                     "SET IDENTITY_INSERT [dbo].[OSUSR_DEF_CITY] OFF;")
            do!
                Deploy.executeBatch cnn
                    ("SET IDENTITY_INSERT [dbo].[OSUSR_ABC_CUSTOMER] ON; " +
                     "INSERT INTO [dbo].[OSUSR_ABC_CUSTOMER] ([ID],[EMAIL],[FIRSTNAME],[LASTNAME],[CITYID],[LEGACYCODE]) " +
                     "VALUES (1, N'alice@example.com', N'Alice', N'Amber', 1, NULL), " +
                     "(2, N'bob@example.com', N'Bob', N'Blue', 2, N'BC-7'); " +
                     "SET IDENTITY_INSERT [dbo].[OSUSR_ABC_CUSTOMER] OFF;")
            do!
                Deploy.executeBatch cnn
                    ("SET IDENTITY_INSERT [dbo].[OSUSR_REF_COUNTRY] ON; " +
                     "INSERT INTO [dbo].[OSUSR_REF_COUNTRY] ([ID],[CODE],[NAME]) " +
                     "VALUES (1, N'ATL', N'Atlantis'); " +
                     "SET IDENTITY_INSERT [dbo].[OSUSR_REF_COUNTRY] OFF;")
        }

    /// Resolve a reconstructed attribute's SsKey by physical table + column.
    let attrKeyOf (catalog: Catalog) (table: string) (column: string) : SsKey =
        Catalog.allKinds catalog
        |> List.pick (fun k ->
            if TableId.tableText k.Physical = table then
                k.Attributes
                |> List.tryFind (fun a -> ColumnRealization.columnNameText a.Column = column)
                |> Option.map (fun a -> a.SsKey)
            else None)

    interface IClassFixture<EphemeralContainerFixture>

    [<Fact>]
    member _.``PL-7: readSchema equals read-then-strip (read still mints Static), and the capture verb is value-identical both ways`` () =
        if not (Deploy.Docker.ensureRunning ()) then
            printfn "SKIP pay-once-schema-read: Docker daemon not reachable."
        else
        (fixture.WithEphemeralDatabase "PayOnceSchemaRead" (fun cnn connStr ->
            task {
                do! seedEstate cnn

                // --- The marking-semantics pin. `read` MUST still mint
                // Static on the row-carrying kinds (a vacuously mark-free
                // estate would prove nothing) and `readSchema` must be the
                // strip composition exactly — same schema plane (kinds, FKs,
                // defaults, computed, annotations, indexes, sequences), no
                // Static mark, authored marks untouched.
                let! fullR = ReadSide.read cnn
                let full = mustOk fullR
                let! schemaOnlyR = ReadSide.readSchema cnn
                let schemaOnly = mustOk schemaOnlyR
                Assert.True(
                    Catalog.allKinds full |> List.exists isStaticMarked,
                    "the seeded estate must make ReadSide.read mint Static marks (non-vacuous gate)")
                Assert.False(
                    Catalog.allKinds schemaOnly |> List.exists isStaticMarked,
                    "readSchema must mint NO Static marks")
                Assert.Equal<Catalog>(Catalog.stripStaticPopulations full, schemaOnly)

                // --- S02 both ways, end-to-end: the capture verb over the
                // schema-only read equals the incumbent composition
                // (read → strip → attach) over the same estate.
                let! incumbentR = LiveProfiler.attach cnn (Catalog.stripStaticPopulations full) Profile.empty
                let incumbent = mustOk incumbentR
                let! capturedR = ProfileCaptureRun.capture connStr
                let captured = mustOk capturedR
                Assert.Equal<Profile>(incumbent, captured)
                return ()
            })).GetAwaiter().GetResult()

    [<Fact>]
    member _.``PL-7 (S10): the scoped null-count probe yields the SAME preflight verdict as the full EvidenceCache capture`` () =
        if not (Deploy.Docker.ensureRunning ()) then
            printfn "SKIP pay-once-preflight-probe: Docker daemon not reachable."
        else
        (fixture.WithEphemeralDatabase "PayOncePreflight" (fun cnn _connStr ->
            task {
                do! seedEstate cnn
                // The catalog exactly as the migrate verbs hand it to the
                // gate: the marked ReadSide.read contract (the gate strips
                // internally — the 4.4-trap seam stays covered).
                let! catalogR = ReadSide.read cnn
                let catalog = mustOk catalogR
                // Tighten a NULL-bearing column (LEGACYCODE: one NULL) AND a
                // clean one (EMAIL: none) — the verdict must name exactly the
                // former, and the zero-count filter parity is exercised.
                let legacyKey = attrKeyOf catalog "OSUSR_ABC_CUSTOMER" "LEGACYCODE"
                let emailKey = attrKeyOf catalog "OSUSR_ABC_CUSTOMER" "EMAIL"
                let overlay =
                    { DecisionOverlay.empty with
                        EnforceNotNull = Set.ofList [ legacyKey; emailKey ] }

                // Incumbent arm: the FULL EvidenceCache capture feeding the
                // pure gate (what tighteningViolations did before PL-7).
                let profileCatalog = Catalog.stripStaticPopulations catalog
                let! cacheR = LiveProfiler.captureEvidenceCache cnn profileCatalog
                let cache = mustOk cacheR
                let incumbent = Preflight.dataViolatesTightening cache overlay

                // The wired gate (now the scoped probe underneath).
                let! scopedR = Preflight.tighteningViolations cnn catalog overlay
                let scoped = mustOk scopedR

                Assert.Equal<Preflight.TighteningViolation list>(incumbent, scoped)
                // Content-bearing: exactly the NULL-carrying tightened column,
                // with its exact count.
                let violation = Assert.Single scoped
                Assert.Equal(legacyKey, violation.AttributeKey)
                Assert.Equal(1L, violation.NullCount)
                return ()
            })).GetAwaiter().GetResult()
