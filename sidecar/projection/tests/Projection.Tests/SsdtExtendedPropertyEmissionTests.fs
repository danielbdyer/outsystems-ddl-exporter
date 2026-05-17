module Projection.Tests.SsdtExtendedPropertyEmissionTests

open Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Targets.SSDT
open Projection.Tests.Fixtures
open Projection.Tests.IRBuilders

// ---------------------------------------------------------------------------
// Chapter 4.1.A slice 8 — SSDT emission of `EXEC sys.sp_addextendedproperty`
// calls consuming chapter A.0' slice α's `Description` fields (Kind +
// Attribute) and slice ζ's `ExtendedProperties` lists (Kind / Attribute /
// Index). Retires the `Tolerance.CommentMetadataUnreflected` deferral.
//
// V1 form per `ExtendedPropertyScriptBuilder.cs:91-95`:
//   EXEC sys.sp_addextendedproperty
//     @name=N'MS_Description', @value=N'<desc>',
//     @level0type=N'SCHEMA', @level0name=N'<schema>',
//     @level1type=N'TABLE',  @level1name=N'<table>'
//     [, @level2type=N'COLUMN'|N'INDEX', @level2name=N'<col-or-idx>']
//
// V2 emits via ScriptDom's typed-AST path (`ScriptDomBuild
// .buildSetExtendedProperty`); the terminal-text boundary is
// `Sql160ScriptGenerator.GenerateScript` per pillar 1.
// ---------------------------------------------------------------------------

type private FsResult<'a, 'b> = Microsoft.FSharp.Core.Result<'a, 'b>

let private enrich (c: Catalog) : Catalog =
    (CanonicalizeIdentity.run c).Value

let private mustOk (r: FsResult<'a, EmitError>) : 'a =
    match r with
    | FsResult.Ok v -> v
    | FsResult.Error e -> failwithf "Emitter failed: %A" e

let private customerSsdtBody (catalog: Catalog) : string =
    let outputs = SsdtDdlEmitter.emitSlices (enrich catalog) |> mustOk
    match ArtifactByKind.tryFind customerKey outputs with
    | Some file -> file.Body
    | None -> failwithf "Customer kind missing from SSDT outputs"

/// Build a fixture-shaped sampleCatalog with one Kind carrying a
/// table-level Description.
let private withCustomerDescription (desc: string) : Catalog =
    let kinds =
        salesModule.Kinds
        |> List.map (fun k ->
            if k.SsKey = customerKey then { k with Description = Some desc }
            else k)
    let m = { salesModule with Kinds = kinds }
    { sampleCatalog with Modules = [ m ] }

/// Build a fixture catalog with the Customer kind's Id attribute
/// carrying a column-level Description.
let private withIdColumnDescription (desc: string) : Catalog =
    let updateAttr (a: Attribute) : Attribute =
        if a.SsKey = customerIdAttrKey then { a with Description = Some desc }
        else a
    let updateKind (k: Kind) : Kind =
        if k.SsKey = customerKey then
            { k with Attributes = k.Attributes |> List.map updateAttr }
        else k
    let m = { salesModule with Kinds = salesModule.Kinds |> List.map updateKind }
    { sampleCatalog with Modules = [ m ] }

/// Build a fixture catalog with the Customer kind carrying a table-
/// level `ExtendedProperty` (not MS_Description).
let private withCustomerExtendedProperty (name: string) (value: string option) : Catalog =
    let ep = ExtendedProperty.create name value |> Result.value
    let updateKind (k: Kind) : Kind =
        if k.SsKey = customerKey then { k with ExtendedProperties = [ ep ] }
        else k
    let m = { salesModule with Kinds = salesModule.Kinds |> List.map updateKind }
    { sampleCatalog with Modules = [ m ] }

// ---------------------------------------------------------------------------
// Table-level descriptions emit MS_Description extended properties.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Chapter 4.1.A slice 8: Kind.Description emits a table-level MS_Description extended property`` () =
    let catalog = withCustomerDescription "Customer master record"
    let body = customerSsdtBody catalog
    // ScriptDom canonicalizes EXEC → EXECUTE + bracket-quotes the
    // schema-qualified sproc identifier. V2 deviates textually from
    // V1's EXEC sys.sp_addextendedproperty shorthand; the schema
    // effect is identical.
    Assert.Contains("EXECUTE [sys].[sp_addextendedproperty]", body)
    Assert.Contains("@name = N'MS_Description'", body)
    Assert.Contains("@value = N'Customer master record'", body)
    Assert.Contains("@level0type = N'SCHEMA'", body)
    Assert.Contains("@level1type = N'TABLE'", body)
    // Table-level extended properties carry no level2 args.
    Assert.DoesNotContain("@level2type", body)

[<Fact>]
let ``Chapter 4.1.A slice 8: Kind without Description emits no MS_Description`` () =
    let body = customerSsdtBody sampleCatalog
    Assert.DoesNotContain("MS_Description", body)
    Assert.DoesNotContain("sp_addextendedproperty", body)

// ---------------------------------------------------------------------------
// Column-level descriptions emit column-scoped extended properties.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Chapter 4.1.A slice 8: Attribute.Description emits a column-level MS_Description`` () =
    let catalog = withIdColumnDescription "Surrogate primary key (identity)"
    let body = customerSsdtBody catalog
    Assert.Contains("EXECUTE [sys].[sp_addextendedproperty]", body)
    Assert.Contains("@value = N'Surrogate primary key (identity)'", body)
    Assert.Contains("@level2type = N'COLUMN'", body)

// ---------------------------------------------------------------------------
// ExtendedProperty lists emit non-MS_Description properties.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Chapter 4.1.A slice 8: Kind.ExtendedProperties emits one EXEC per entry with the entry's name + value`` () =
    let catalog = withCustomerExtendedProperty "OutSystems_EntityId" (Some "42")
    let body = customerSsdtBody catalog
    Assert.Contains("@name = N'OutSystems_EntityId'", body)
    Assert.Contains("@value = N'42'", body)

[<Fact>]
let ``Chapter 4.1.A slice 8: ExtendedProperty with None value emits NULL`` () =
    let catalog = withCustomerExtendedProperty "Marker" None
    let body = customerSsdtBody catalog
    Assert.Contains("@name = N'Marker'", body)
    Assert.Contains("@value = NULL", body)

// ---------------------------------------------------------------------------
// Determinism witness — same Catalog produces byte-identical body
// across repeat invocations (A33 + T1).
// ---------------------------------------------------------------------------

[<Fact>]
let ``T1: SsdtDdlEmitter sp_addextendedproperty emission is byte-deterministic across repeat invocations`` () =
    let catalog = withCustomerDescription "Customer master record"
    let body1 = customerSsdtBody catalog
    let body2 = customerSsdtBody catalog
    Assert.Equal<string>(body1, body2)

// ---------------------------------------------------------------------------
// Closed-DU coverage witness — Tolerance.CommentMetadataUnreflected
// no longer appears in `ToleratedDivergence.allKnown`. Slice 8 retires
// the deferral; the variant is removed from the DU.
// ---------------------------------------------------------------------------

[<Fact>]
let ``Chapter 4.1.A slice 8: Tolerance.CommentMetadataUnreflected variant retired`` () =
    // Pattern-match against the surviving four variants; if a future
    // chapter adds a fifth, this match site lights up. The named
    // failure mode: CommentMetadataUnreflected would carry an
    // active deferral here if slice 8's emitter retirement hadn't
    // landed. Witnesses the structural retirement.
    for variant in ToleratedDivergence.allKnown do
        match variant with
        | ToleratedDivergence.HeaderCommentsOmitted        -> ()
        | ToleratedDivergence.PostDeployForeignKeysSplit   -> ()
        | ToleratedDivergence.IndexesUnreflected           -> ()
        | ToleratedDivergence.StaticPopulationsUnreflected -> ()
    Assert.Equal(4, Set.count ToleratedDivergence.allKnown)
