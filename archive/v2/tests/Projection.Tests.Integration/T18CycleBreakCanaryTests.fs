module Projection.Tests.T18CycleBreakCanaryTests

// T18 — cycle-break factorization (DECISIONS 2026-07-18; the v7 arc's
// live witness). A symmetric weak 2-cycle's rows load through V2's
// composed two-phase form (global Phase-1 MERGEs, then Phase-2 UPDATEs)
// against real SQL Server, and the FINAL STATE is row-equal to the
// source — `A ⊕ δ_load = (A ⊕ δ_phase1) ⊕ δ_phase2` with zero residue —
// while every foreign key finishes TRUSTED (the deferral never bought
// admissibility with integrity).
//
// The schema here is hand-authored (tables + ALTER-added cyclic FKs —
// the standard shape for mutually-referencing tables); the law under
// test is the DATA factorization, not cyclic-DDL emission.

open System.Threading.Tasks
open Microsoft.Data.SqlClient
open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Targets.Data

module private T18Fixtures =

    let skipIfNoDocker (label: string) : bool =
        if Deploy.Docker.ensureRunning () then true
        else
            printfn "SKIP %s: Docker daemon not reachable." label
            false

    let mustOk r = match r with Ok v -> v | Error e -> failwithf "T18 fixture: %A" e
    let mkKey (parts: string list) : SsKey = SsKey.synthesizedComposite "OS_T18" parts |> mustOk
    let mkName (s: string) : Name = Name.create s |> mustOk

    let aKey = mkKey ["Alpha"]
    let bKey = mkKey ["Beta"]

    let mkAttr (owner: string) (name: string) (col: string) (isPk: bool) (nullable: bool) : Attribute =
        { Attribute.create (mkKey [owner; name]) (mkName name) Integer with
            Column       = ColumnRealization.create col nullable |> Result.value
            IsPrimaryKey = isPk
            IsMandatory  = not nullable }

    let rowOf (owner: string) (ident: string) (cells: (string * string) list) : StaticRow =
        { Identifier = mkKey [owner; ident]
          Values     = StaticRow.presentValues (cells |> List.map (fun (n, v) -> mkName n, v)) }

    /// Alpha(1) → Beta(1) → Alpha(1): a genuine mutual reference. Both FK
    /// columns nullable, so the resolver breaks exactly one edge and the
    /// broken side's FK lands via Phase-2.
    let cyclicCatalog : Catalog =
        let alphaRows = [ rowOf "Alpha" "a1" [ "Id", "1"; "BetaId", "1" ] ]
        let betaRows  = [ rowOf "Beta"  "b1" [ "Id", "1"; "AlphaId", "1" ] ]
        let alpha : Kind =
            { SsKey = aKey; Name = mkName "Alpha"; Origin = Native
              Modality = [ Static alphaRows ]
              Physical = TableId.create "dbo" "T18_ALPHA" |> mustOk
              Attributes = [ mkAttr "Alpha" "Id" "ID" true false
                             mkAttr "Alpha" "BetaId" "BETAID" false true ]
              References = [ Reference.create (mkKey ["Alpha"; "ToBeta"]) (mkName "ToBeta") (mkKey ["Alpha"; "BetaId"]) bKey ]
              Indexes = []; Description = None; IsActive = true; Triggers = []; ColumnChecks = []; ExtendedProperties = [] }
        let beta : Kind =
            { SsKey = bKey; Name = mkName "Beta"; Origin = Native
              Modality = [ Static betaRows ]
              Physical = TableId.create "dbo" "T18_BETA" |> mustOk
              Attributes = [ mkAttr "Beta" "Id" "ID" true false
                             mkAttr "Beta" "AlphaId" "ALPHAID" false true ]
              References = [ Reference.create (mkKey ["Beta"; "ToAlpha"]) (mkName "ToAlpha") (mkKey ["Beta"; "AlphaId"]) aKey ]
              Indexes = []; Description = None; IsActive = true; Triggers = []; ColumnChecks = []; ExtendedProperties = [] }
        IRBuilders.mkCatalog [ IRBuilders.mkModule (mkKey ["T18Mod"]) (mkName "T18Mod") [ alpha; beta ] ]

    let schemaSql = """
CREATE TABLE [dbo].[T18_ALPHA] ([ID] INT NOT NULL PRIMARY KEY, [BETAID] INT NULL);
CREATE TABLE [dbo].[T18_BETA]  ([ID] INT NOT NULL PRIMARY KEY, [ALPHAID] INT NULL);
ALTER TABLE [dbo].[T18_ALPHA] ADD CONSTRAINT [FK_T18_ALPHA_BETA] FOREIGN KEY ([BETAID]) REFERENCES [dbo].[T18_BETA] ([ID]);
ALTER TABLE [dbo].[T18_BETA]  ADD CONSTRAINT [FK_T18_BETA_ALPHA] FOREIGN KEY ([ALPHAID]) REFERENCES [dbo].[T18_ALPHA] ([ID]);
"""

    let scalarInt (cnn: SqlConnection) (sql: string) : Task<int> =
        task {
            use cmd = cnn.CreateCommand()
            cmd.CommandText <- sql
            let! r = cmd.ExecuteScalarAsync()
            return System.Convert.ToInt32 r
        }

open T18Fixtures

[<Xunit.Collection("Docker-SqlServer")>]
type T18CycleBreakCanaryTests(fixture: EphemeralContainerFixture) =
    interface IClassFixture<EphemeralContainerFixture>

    [<Fact>]
    member _.``T18 canary: a weak 2-cycle loads two-phase on SQL Server and ends row-equal to the source, every FK trusted`` () =
        if not (skipIfNoDocker "t18-cycle-break") then () else
        TaskSync.run (fun () ->
            fixture.WithEphemeralDatabase "T18Cycle" (fun cnn _ -> task {
                do! Deploy.executeBatch cnn schemaSql
                // V2's composed data artifact — the two-phase factorization
                // under the v7 exact resolver (one broken edge; Phase-2
                // carries exactly the repair set).
                let composed =
                    DataEmissionComposer.composeRendered
                        Policy.empty cyclicCatalog Profile.empty
                    |> mustOk
                do! Deploy.executeBatch cnn composed
                // Row equality: both rows exist with BOTH mutual FK values
                // populated — the factorization is residue-free.
                let! alphaFk = scalarInt cnn "SELECT COUNT(*) FROM [dbo].[T18_ALPHA] WHERE [ID] = 1 AND [BETAID] = 1"
                let! betaFk  = scalarInt cnn "SELECT COUNT(*) FROM [dbo].[T18_BETA] WHERE [ID] = 1 AND [ALPHAID] = 1"
                Assert.Equal(1, alphaFk)
                Assert.Equal(1, betaFk)
                // And the deferral never bought admissibility with
                // integrity: every FK enabled and TRUSTED at the end.
                let! untrusted = scalarInt cnn "SELECT COUNT(*) FROM sys.foreign_keys WHERE is_not_trusted = 1 OR is_disabled = 1"
                Assert.Equal(0, untrusted)
            }))
