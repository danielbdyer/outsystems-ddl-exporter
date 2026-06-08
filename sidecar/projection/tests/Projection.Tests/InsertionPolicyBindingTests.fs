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
            ValidationOverrides    = { AllowMissingSchema = [] }
        }
        Profile     = { Path = None }
        Cache       = { Root = ""; Refresh = false; TtlSeconds = 0 }
        Profiler    = { Provider = "fixture"; MockFolder = None }
        TypeMapping = { Path = None; Default = None; Overrides = Map.empty }
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
            DecisionLog = true; Opportunities = true; Validations = true
        }
        Policy      = {
            Selection       = "IncludeAll"
            Insertion       = insertion
            UserMatching    = { Strategy = "ByEmail"; Fallback = "NoFallback" }
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
