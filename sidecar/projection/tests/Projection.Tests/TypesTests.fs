module Projection.Tests.TypesTests

open System.Threading.Tasks
open Xunit
open Projection.Core

/// Stage 0 (S0.A per `STAGING.md`) lands the seven tessellating-pattern
/// type aliases in `Projection.Core/Types.fs`. The acceptance criterion
/// is "every chapter pre-scope's emitter signature matches `Emitter
/// <'element>` (or its variants) verbatim; F# compiler enforces."
///
/// These tests verify the contract by *inhabitation*: each alias is
/// assigned a trivial function of the alias's shape; if the alias
/// drifts out of step with the SPINE patterns, type inference here
/// breaks and the test file no longer compiles. The tests' value is
/// at compile time, not runtime — `Assert.True true` is the runtime
/// witness that the inhabitation succeeded.
///
/// Once chapter 3.1 ships the read-side adapter, chapter 3.3 ships
/// DacpacEmitter, etc., the chapter agents replace the trivial
/// inhabitants below with the real consumer references — at that
/// point the test asserts that the chapter's substantive surface
/// matches the aliased contract by typing.

[<Fact>]
let ``S0.A: Emitter<'element> is inhabited by Catalog -> Result<ArtifactByKind<'element>>`` () =
    let stub : Emitter<string> =
        fun _catalog -> Result.success (ArtifactByKind Map.empty)
    Assert.NotNull(stub :> obj)

[<Fact>]
let ``S0.A: EmitterWithProfile<'element> is inhabited by Catalog -> Profile -> Result<ArtifactByKind<'element>>`` () =
    let stub : EmitterWithProfile<string> =
        fun _catalog _profile -> Result.success (ArtifactByKind Map.empty)
    Assert.NotNull(stub :> obj)

[<Fact>]
let ``S0.A: EmitterOverDiff<'element> is inhabited by CatalogDiff -> Result<ArtifactByKind<'element>>`` () =
    let stub : EmitterOverDiff<string> =
        fun _diff -> Result.success (ArtifactByKind Map.empty)
    Assert.NotNull(stub :> obj)

[<Fact>]
let ``S0.A: Adapter<'source, 'inner> is inhabited by 'source -> Task<Result<'inner>>`` () =
    let stub : Adapter<string, int> =
        fun _source -> Task.FromResult(Result.success 0)
    Assert.NotNull(stub :> obj)

[<Fact>]
let ``S0.A: Pass<'output> is inhabited by Catalog -> Policy -> Profile -> Lineage<'output>`` () =
    let stub : Pass<unit> =
        fun _catalog _policy _profile -> Lineage.ofValue ()
    Assert.NotNull(stub :> obj)

[<Fact>]
let ``S0.A: PassWithDiagnostics<'output> is inhabited by Catalog -> Policy -> Profile -> Lineage<Diagnostics<'output>>`` () =
    let stub : PassWithDiagnostics<unit> =
        fun _catalog _policy _profile ->
            Lineage.ofValue (Diagnostics.ofValue ())
    Assert.NotNull(stub :> obj)

[<Fact>]
let ``S0.A: Render<'element, 'output> is inhabited by SsKey list -> ArtifactByKind<'element> -> 'output`` () =
    let stub : Render<string, string list> =
        fun _order (ArtifactByKind m) ->
            m |> Map.toList |> List.map snd
    Assert.NotNull(stub :> obj)

[<Fact>]
let ``S0.A: Compare<'tolerance> is inhabited by 'tolerance -> Catalog -> Catalog -> Diff`` () =
    let stub : Compare<unit> =
        fun _tolerance _left _right -> Diff.Pending
    Assert.NotNull(stub :> obj)

[<Fact>]
let ``S0.A: Property is inhabited by Catalog -> bool`` () =
    let stub : Property = fun _catalog -> true
    Assert.NotNull(stub :> obj)

[<Fact>]
let ``S0.A: RelationalProperty is inhabited by Catalog -> Catalog -> bool`` () =
    let stub : RelationalProperty = fun _left _right -> true
    Assert.NotNull(stub :> obj)

[<Fact>]
let ``S0.A: DiffOf<'value> is inhabited by 'value -> 'value -> Result<CatalogDiff>`` () =
    let stub : DiffOf<int> =
        fun _left _right -> Result.success CatalogDiff.Pending
    Assert.NotNull(stub :> obj)

[<Fact>]
let ``S0.A: ArtifactByKind<'a> wraps Map<SsKey, 'a> transparently (smart constructor lands at S0.B)`` () =
    let key = SsKey.original "Customer" |> Result.value
    let artifact = ArtifactByKind (Map.ofList [ key, "CREATE TABLE Customer ..." ])
    let (ArtifactByKind m) = artifact
    Assert.Single(m) |> ignore
    Assert.True(m.ContainsKey key)
