module Projection.Tests.DockerImageEmitterTests

open System.IO
open Xunit
open Microsoft.SqlServer.Dac.Model
open Projection.Core
open Projection.Core.Passes
open Projection.Targets.SSDT
open Projection.Tests.Fixtures

// Chapter A.4.7' slice η — `CanonicalizeIdentity.run` is private; the
// canonical surface is `.registered.Run` returning
// `Lineage<Diagnostics<Catalog>>`. This per-file shim restores the
// `Lineage<Catalog>` shape so existing assertions keep reading.
let private ciRun (c: Catalog) : Lineage<Catalog> =
    CanonicalizeIdentity.registered.Run c |> Lineage.map (fun d -> d.Value)

// ---------------------------------------------------------------------------
// Chapter 3.x slice δ_dock — DockerImageEmitter (dev-tooling Docker context).
//
// Per `DECISIONS 2026-05-11 — Chapter 3.x DacpacEmitter open` + operator
// directive (slice δ_dock reframe of pre-scope §5 slice δ): the emitter
// produces a Docker build context (Dockerfile + dacpac + entrypoint +
// README) the dev team consumes via CI/CD-built + registry-published
// images. No source checkout required for the dev consumer.
// ---------------------------------------------------------------------------

let private enrich (c: Catalog) : Catalog =
    (ciRun c).Value

let private mustOk (r: Result<'a>) : 'a =
    match r with
    | Ok v -> v
    | Error errs ->
        Assert.Fail (sprintf "expected Ok; got %A" errs)
        Unchecked.defaultof<'a>

// ---------------------------------------------------------------------------
// Slice δ_dock acceptance — emit returns a build context with all four
// expected fields populated and shape-valid.
// ---------------------------------------------------------------------------

[<Fact>]
let ``DockerImageEmitter.emit produces a Dockerfile starting with FROM mcr.microsoft.com/mssql/server`` () =
    let enriched = enrich sampleCatalog
    let ctx = DockerImageEmitter.emit enriched |> mustOk
    Assert.Contains ("FROM mcr.microsoft.com/mssql/server", ctx.Dockerfile)

[<Fact>]
let ``DockerImageEmitter.emit produces a Dockerfile that COPYs catalog.dacpac and entrypoint.sh`` () =
    let enriched = enrich sampleCatalog
    let ctx = DockerImageEmitter.emit enriched |> mustOk
    // Both files must be COPY'd into the image at the canonical paths
    // the entrypoint script references.
    Assert.Contains ("COPY catalog.dacpac", ctx.Dockerfile)
    Assert.Contains ("COPY entrypoint.sh",  ctx.Dockerfile)

[<Fact>]
let ``DockerImageEmitter.emit produces an entrypoint script with bash shebang and sqlpackage publish`` () =
    let enriched = enrich sampleCatalog
    let ctx = DockerImageEmitter.emit enriched |> mustOk
    Assert.StartsWith ("#!/bin/bash", ctx.EntrypointScript)
    // sqlpackage /Action:Publish is the deployment surface (per pre-scope
    // §6.5 / Microsoft's canonical DACPAC deploy tool).
    Assert.Contains ("/opt/sqlpackage/sqlpackage", ctx.EntrypointScript)
    Assert.Contains ("/Action:Publish", ctx.EntrypointScript)

[<Fact>]
let ``DockerImageEmitter.emit produces a README naming docker build and docker run`` () =
    let enriched = enrich sampleCatalog
    let ctx = DockerImageEmitter.emit enriched |> mustOk
    // Operator-facing instructions: build + run + connect.
    Assert.Contains ("docker build", ctx.Readme)
    Assert.Contains ("docker run",   ctx.Readme)

// ---------------------------------------------------------------------------
// The bundled DacpacBytes round-trip through DacFx exactly as
// `DacpacEmitter.emit` produced them — the Docker emitter does not
// transform the bytes, only wraps them in the build context.
// ---------------------------------------------------------------------------

[<Fact>]
let ``DockerImageEmitter.emit embeds dacpac bytes that load through DacFx with the right table count`` () =
    let enriched = enrich sampleCatalog
    let ctx = DockerImageEmitter.emit enriched |> mustOk
    Assert.NotEmpty ctx.DacpacBytes
    use stream = new MemoryStream(ctx.DacpacBytes)
    use model = TSqlModel.LoadFromDacpac(stream, ModelLoadOptions())
    let tables =
        model.GetObjects(DacQueryScopes.UserDefined, Table.TypeClass)
        |> Seq.toList
    let expected = Catalog.allKinds enriched |> List.length
    Assert.Equal (expected, List.length tables)

// ---------------------------------------------------------------------------
// T1 determinism — the static-template fields (Dockerfile, entrypoint,
// README) are byte-identical across emit calls; DacpacBytes is
// content-identical via DacFx round-trip (per the chapter-open T1
// amendment for binary emitters).
// ---------------------------------------------------------------------------

[<Fact>]
let ``T1: DockerImageEmitter Dockerfile + entrypoint + README are byte-identical across emits`` () =
    let enriched = enrich sampleCatalog
    let a = DockerImageEmitter.emit enriched |> mustOk
    let b = DockerImageEmitter.emit enriched |> mustOk
    Assert.Equal<string> (a.Dockerfile,       b.Dockerfile)
    Assert.Equal<string> (a.EntrypointScript, b.EntrypointScript)
    Assert.Equal<string> (a.Readme,           b.Readme)
