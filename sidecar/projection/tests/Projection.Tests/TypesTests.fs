module Projection.Tests.TypesTests

open System.Threading.Tasks
open Xunit
open Projection.Core

/// FSharp.Core's two-arity `Result<'a, 'b>` case constructors collide
/// with `Projection.Core.DiagnosticSeverity.Error` once `Projection.Core`
/// is opened; qualifying via a private type alias forces case access
/// to resolve to FSharp.Core's Result.Ok / Result.Error without
/// shadowing the single-arity `Result<'a>.Failure` case.
type private FsResult<'a, 'b> = Microsoft.FSharp.Core.Result<'a, 'b>

/// Stage 0 (S0.A per `STAGING.md`) lands the seven tessellating-pattern
/// type aliases in `Projection.Core/Types.fs`. The acceptance criterion
/// is "every chapter pre-scope's emitter signature matches `Emitter
/// <'element>` (or its variants) verbatim; F# compiler enforces."
///
/// These tests verify the contract by *inhabitation*: each alias is
/// assigned a trivial function of the alias's shape; if the alias
/// drifts out of step with the SPINE patterns, type inference here
/// breaks and the test file no longer compiles. The tests' value is
/// at compile time; `Assert.NotNull` is the runtime witness that
/// inhabitation succeeded.
///
/// Stage 0 (S0.B slice 5.1) makes `ArtifactByKind` constructor private
/// — emitter stubs return `Error` variants rather than constructing
/// directly. `ArtifactByKindTests` covers the smart constructor's
/// `Ok` path with a real Catalog.
///
/// Once chapter 3.1 ships the read-side adapter, chapter 3.3 ships
/// DacpacEmitter, etc., the chapter agents replace the trivial
/// inhabitants below with the real consumer references — at that
/// point the test asserts that the chapter's substantive surface
/// matches the aliased contract by typing.

let private stubKey () =
    SsKey.original "stub" |> Result.value

[<Fact>]
let ``S0.A: Emitter<'element> is inhabited by Catalog -> Result<ArtifactByKind<'element>, EmitError>`` () =
    let stub : Emitter<string> =
        fun _catalog -> FsResult.Error (KindNotProduced (stubKey ()))
    Assert.NotNull(stub :> obj)

[<Fact>]
let ``S0.A: EmitterWithProfile<'element> is inhabited by Catalog -> Profile -> Result<ArtifactByKind<'element>, EmitError>`` () =
    let stub : EmitterWithProfile<string> =
        fun _catalog _profile -> FsResult.Error (KindNotProduced (stubKey ()))
    Assert.NotNull(stub :> obj)

[<Fact>]
let ``S0.A: EmitterOverDiff<'element> is inhabited by CatalogDiff -> Result<ArtifactByKind<'element>, EmitError>`` () =
    let stub : EmitterOverDiff<string> =
        fun _diff -> FsResult.Error (KindNotProduced (stubKey ()))
    Assert.NotNull(stub :> obj)

[<Fact>]
let ``S0.A: adapter shape 'source -> Task<Result<'inner>> is inhabited (Stage-0 reservation; alias retired session-36)`` () =
    // Per session-36 architecture audit (Agent 2 #2): the Stage-0
    // `Adapter<'source,'inner>` alias was retired from Core to keep
    // `System.Threading.Tasks` out of `Projection.Core`. The shape
    // is preserved here as an inlined-signature witness; adapters at
    // the boundary declare the task-shaped signature directly.
    let stub : string -> Task<Result<int>> =
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
        fun _order artifact ->
            artifact |> ArtifactByKind.toMap |> Map.toList |> List.map snd
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
let ``S0.A: DiffOf<'value> is inhabited by 'value -> 'value -> Result<CatalogDiff, EmitError>`` () =
    let stub : DiffOf<int> =
        fun _left _right -> FsResult.Ok CatalogDiff.Pending
    Assert.NotNull(stub :> obj)
