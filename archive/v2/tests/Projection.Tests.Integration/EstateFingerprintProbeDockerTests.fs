namespace Projection.Tests

// The estate fingerprint probe's live witness (wave A2.5 — DECISIONS
// 2026-07-15, the estate chapter opens, entry 4): ONE batched round-trip
// answers the whole staleness question — each kind's exact `COUNT_BIG` and
// its canonical `MAX(pk)`. The laws pinned here:
//   - the MAX is typed-before-cast (a numeric PK's maximum is 12, never the
//     lexicographic "2");
//   - a kind without a single-column PK reads `MaxPk = None`;
//   - an empty table reads zero rows and `MaxPk = None` (MAX over nothing
//     is NULL, surfaced as absence — never a fabricated value).
// Serial via the Docker-SqlServer collection.

open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Adapters.Sql
open Projection.Tests.Fixtures

[<Xunit.Collection("Docker-SqlServer")>]
module EstateFingerprintProbeDockerTests =

    let private skipIfNoDocker (label: string) : bool =
        if Deploy.Docker.ensureRunning () then true
        else printfn "SKIP %s: Docker daemon not reachable." label; false

    /// A probe-facing kind: only the physical table, the attribute columns,
    /// and the PK marking matter to the fingerprint.
    let private probeKind (label: string) (table: string) (attrs: (string * string * PrimitiveType * bool) list) : Kind =
        Kind.create
            (kindKey [ label ])
            (mkName label)
            (mkTableId "dbo" table)
            (attrs
             |> List.map (fun (logical, column, ptype, isPk) ->
                 { Attribute.create (attrKey [ label; logical ]) (mkName logical) ptype with
                     Column = ColumnRealization.create column false |> Result.value
                     IsPrimaryKey = isPk }))

    [<Fact>]
    let ``fingerprint probe: one batch answers exact counts and the typed-before-cast MAX(pk); composite-PK and empty kinds read as absence`` () =
        let label = "EstateFpProbe"
        if not (skipIfNoDocker label) then () else
        TaskSync.run (fun () ->
            task {
                let seed =
                    "CREATE TABLE [dbo].[FP_ALPHA] ([ID] INT NOT NULL PRIMARY KEY, [NAME] NVARCHAR(50) NULL); \
                     CREATE TABLE [dbo].[FP_BETA] ([A] INT NOT NULL, [B] INT NOT NULL, PRIMARY KEY ([A],[B])); \
                     CREATE TABLE [dbo].[FP_EMPTY] ([ID] INT NOT NULL PRIMARY KEY); \
                     INSERT INTO [dbo].[FP_ALPHA] ([ID],[NAME]) VALUES (1, N'one'), (2, N'two'), (12, N'twelve'); \
                     INSERT INTO [dbo].[FP_BETA] ([A],[B]) VALUES (1, 1), (1, 2);"
                let alpha = probeKind "Alpha" "FP_ALPHA" [ "Id", "ID", Integer, true; "Name", "NAME", Text, false ]
                let beta  = probeKind "Beta"  "FP_BETA"  [ "A", "A", Integer, true; "B", "B", Integer, true ]
                let empty = probeKind "Empty" "FP_EMPTY" [ "Id", "ID", Integer, true ]
                let! result =
                    Deploy.withBootstrappedDatabase label seed (fun cnn ->
                        EvidenceFingerprint.probe cnn [ alpha; beta; empty ])
                match result with
                | Error es -> Assert.True(false, sprintf "the probe failed: %A" es)
                | Ok readings ->
                    let byKind = readings |> List.map (fun r -> r.Kind, r) |> Map.ofList
                    // Alpha: three rows; the MAX is numeric (12), never the
                    // lexicographic string maximum ("2") — typed before cast.
                    Assert.Equal(3L, byKind.[alpha.SsKey].RowCount)
                    Assert.Equal(Some "12", byKind.[alpha.SsKey].MaxPk)
                    // Beta: a composite PK carries no single MAX — absence, named.
                    Assert.Equal(2L, byKind.[beta.SsKey].RowCount)
                    Assert.Equal(None, byKind.[beta.SsKey].MaxPk)
                    // Empty: zero rows; MAX over nothing is NULL → absence.
                    Assert.Equal(0L, byKind.[empty.SsKey].RowCount)
                    Assert.Equal(None, byKind.[empty.SsKey].MaxPk)
            })

    [<Fact>]
    let ``fingerprint probe: an in-place UPDATE moves the content hash while the row count and MAX(pk) hold (survival rule 14)`` () =
        // The blindness the content term closes: a row changed but not
        // added/removed keeps COUNT_BIG and MAX(pk) identical. The
        // CHECKSUM_AGG(BINARY_CHECKSUM(...)) term moves, so the store re-profiles.
        let label = "EstateFpProbeUpdate"
        if not (skipIfNoDocker label) then () else
        TaskSync.run (fun () ->
            task {
                let seed =
                    "CREATE TABLE [dbo].[FP_UPD] ([ID] INT NOT NULL PRIMARY KEY, [NAME] NVARCHAR(50) NULL); \
                     INSERT INTO [dbo].[FP_UPD] ([ID],[NAME]) VALUES (1, N'one'), (2, N'two');"
                let k = probeKind "Upd" "FP_UPD" [ "Id", "ID", Integer, true; "Name", "NAME", Text, false ]
                let! outcome =
                    Deploy.withBootstrappedDatabase label seed (fun cnn ->
                        task {
                            let! before = EvidenceFingerprint.probe cnn [ k ]
                            use upd = cnn.CreateCommand()
                            // Same row count, same MAX(pk) — only a value changes
                            // (a case flip, which BINARY_CHECKSUM sees).
                            upd.CommandText <- "UPDATE [dbo].[FP_UPD] SET [NAME] = N'ONE' WHERE [ID] = 1;"
                            let! _ = upd.ExecuteNonQueryAsync()
                            let! after = EvidenceFingerprint.probe cnn [ k ]
                            return (before, after)
                        })
                let before, after = outcome
                match before, after with
                | Ok bs, Ok afs ->
                    let b = List.head bs
                    let a = List.head afs
                    Assert.Equal(b.RowCount, a.RowCount)
                    Assert.Equal(b.MaxPk, a.MaxPk)
                    Assert.True(a.Content.IsSome, "the content hash is present for a checksummable table")
                    Assert.NotEqual<string option>(b.Content, a.Content)
                | _ -> Assert.True(false, sprintf "a probe failed: before=%A after=%A" before after)
            })

    [<Fact>]
    let ``fingerprint probe: an empty kind list answers without a round-trip`` () =
        let label = "EstateFpProbeEmpty"
        if not (skipIfNoDocker label) then () else
        TaskSync.run (fun () ->
            task {
                let! result =
                    Deploy.withBootstrappedDatabase label "SELECT 1;" (fun cnn ->
                        EvidenceFingerprint.probe cnn [])
                match result with
                | Ok readings -> Assert.Empty readings
                | Error es -> Assert.True(false, sprintf "the empty probe failed: %A" es)
            })
