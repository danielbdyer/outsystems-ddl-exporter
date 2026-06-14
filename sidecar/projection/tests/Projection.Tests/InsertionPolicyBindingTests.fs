module Projection.Tests.InsertionPolicyBindingTests

open Xunit
open Projection.Core
open Projection.Pipeline

/// Chapter C slice C.5 — `InsertionPolicyBinding.fromString` +
/// `.fromConfig` coverage. The four recognized variants map to the
/// typed DU; unknown values surface
/// `pipeline.insertionPolicy.unknownVariant`; empty string falls back
/// to V2-driver neutral default `SchemaOnly`.

let private mkConfig (insertion: string) : Config.Config =
    {
        Model       = {
            Path                   = Some "ignored.json"
            Ossys                  = None
            Modules                = []
            IncludeSystemModules   = false
            IncludeInactiveModules = false
            OnlyActiveAttributes   = true
        }
        Profile     = { Path = None }
        Profiler    = { Provider = "fixture" }
        Overrides   = {
            TableRenames           = []
            MigrationDependencies  = None
            StaticData             = None
            CircularDependencies   = None
            AllowMissingPrimaryKey = []
            EmissionFolders        = []
        }
        Emission    = {
            Ssdt = true; Dacpac = true; Json = true; Distributions = true
            StaticSeeds = true; MigrationDependencies = true; Bootstrap = true
            DecisionLog = true; Opportunities = true; Validations = true; IncludePlatformAutoIndexes = true; DeleteScope = None; RenderConstraintsElegant = true; EmitIdentityAnnotations = true; DataVerification = Projection.Core.DataVerification.Standard
        }
        Policy      = {
            Insertion       = insertion
            Tightening      = None
            TransformGroups = []
        }
        Output      = { Dir = "out/" }
    }

let private hasErrorCode (code: string) (errs: ValidationError list) : bool =
    errs |> List.exists (fun e -> e.Code = code)

// ----------------------------------------------------------------------
// fromString — all four variants
// ----------------------------------------------------------------------

[<Fact>]
let ``C.5: SchemaOnly resolves to InsertionPolicy.SchemaOnly`` () =
    match InsertionPolicyBinding.fromString "SchemaOnly" with
    | Ok SchemaOnly -> ()
    | other -> Assert.Fail(sprintf "expected Ok SchemaOnly, got %A" other)

[<Fact>]
let ``C.5: InsertNew resolves to InsertionPolicy.InsertNew`` () =
    match InsertionPolicyBinding.fromString "InsertNew" with
    | Ok InsertNew -> ()
    | other -> Assert.Fail(sprintf "expected Ok InsertNew, got %A" other)

[<Fact>]
let ``C.5: Merge resolves to InsertionPolicy.Merge`` () =
    match InsertionPolicyBinding.fromString "Merge" with
    | Ok Merge -> ()
    | other -> Assert.Fail(sprintf "expected Ok Merge, got %A" other)

[<Fact>]
let ``C.5: TruncateAndInsert resolves to InsertionPolicy.TruncateAndInsert`` () =
    match InsertionPolicyBinding.fromString "TruncateAndInsert" with
    | Ok TruncateAndInsert -> ()
    | other -> Assert.Fail(sprintf "expected Ok TruncateAndInsert, got %A" other)

// ----------------------------------------------------------------------
// fromString — unknown variant
// ----------------------------------------------------------------------

[<Fact>]
let ``C.5: unknown variant surfaces structured error`` () =
    match InsertionPolicyBinding.fromString "UpsertWithMerge" with
    | Ok _ -> Assert.Fail("expected Error for unknown variant")
    | Error errs ->
        Assert.True(
            hasErrorCode "pipeline.insertionPolicy.unknownVariant" errs,
            sprintf "expected pipeline.insertionPolicy.unknownVariant; got %A" errs)

[<Fact>]
let ``C.5: case-sensitive matching — schemaOnly (lower-camel) rejected`` () =
    match InsertionPolicyBinding.fromString "schemaOnly" with
    | Ok _ -> Assert.Fail("expected Error for case-mismatched variant")
    | Error errs ->
        Assert.True(hasErrorCode "pipeline.insertionPolicy.unknownVariant" errs)

// ----------------------------------------------------------------------
// fromConfig — empty string falls back to SchemaOnly default
// ----------------------------------------------------------------------

[<Fact>]
let ``C.5: empty Insertion string falls back to default SchemaOnly`` () =
    let cfg = mkConfig ""
    match InsertionPolicyBinding.fromConfig cfg with
    | Ok SchemaOnly -> ()
    | other -> Assert.Fail(sprintf "expected Ok SchemaOnly, got %A" other)

[<Fact>]
let ``C.5: fromConfig threads through fromString for known values`` () =
    let cfg = mkConfig "Merge"
    match InsertionPolicyBinding.fromConfig cfg with
    | Ok Merge -> ()
    | other -> Assert.Fail(sprintf "expected Ok Merge, got %A" other)

[<Fact>]
let ``C.5: fromConfig surfaces unknownVariant error for invalid config string`` () =
    let cfg = mkConfig "NotAVariant"
    match InsertionPolicyBinding.fromConfig cfg with
    | Ok _ -> Assert.Fail("expected Error")
    | Error errs ->
        Assert.True(hasErrorCode "pipeline.insertionPolicy.unknownVariant" errs)

// ----------------------------------------------------------------------
// NM-70 (WP5) — emission.identityAnnotations threads to
// EmissionPolicy.EmitIdentityAnnotations via buildPolicyFromConfig.
// ----------------------------------------------------------------------

let private emptyCatalog : Catalog = { Modules = []; Sequences = [] }

let private withIdentityAnnotations (emit: bool) (cfg: Config.Config) : Config.Config =
    { cfg with Emission = { cfg.Emission with EmitIdentityAnnotations = emit } }

[<Fact>]
let ``NM-70: buildPolicyFromConfig threads emission.identityAnnotations = true (the emit default)`` () =
    let cfg = mkConfig "SchemaOnly" |> withIdentityAnnotations true
    match Compose.buildPolicyFromConfig cfg emptyCatalog with
    | Ok policy -> Assert.True(policy.Emission.EmitIdentityAnnotations)
    | Error errs -> Assert.Fail(sprintf "expected Ok policy, got %A" errs)

[<Fact>]
let ``NM-70: buildPolicyFromConfig threads emission.identityAnnotations = false (the named downgrade)`` () =
    let cfg = mkConfig "SchemaOnly" |> withIdentityAnnotations false
    match Compose.buildPolicyFromConfig cfg emptyCatalog with
    | Ok policy -> Assert.False(policy.Emission.EmitIdentityAnnotations)
    | Error errs -> Assert.Fail(sprintf "expected Ok policy, got %A" errs)
