module Projection.Tests.SsdtExtendedPropertyEmissionTests

open Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Targets.SSDT
open Projection.Tests.Fixtures
open Projection.Tests.IRBuilders

// Chapter A.4.7' slice η — `CanonicalizeIdentity.run` is private; the
// canonical surface is `.registered.Run` returning
// `Lineage<Diagnostics<Catalog>>`. This per-file shim restores the
// `Lineage<Catalog>` shape so existing assertions keep reading.
let private ciRun (c: Catalog) : Lineage<Catalog> =
    CanonicalizeIdentity.registered.Run c |> Lineage.map (fun d -> d.Value)

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
    (ciRun c).Value

let private mustOk (r: FsResult<'a, EmitError>) : 'a =
    match r with
    | FsResult.Ok v -> v
    | FsResult.Error e -> failwithf "Emitter failed: %A" e

let private customerSsdtBody (catalog: Catalog) : string =
    let outputs = SsdtDdlEmitter.emitSlices (enrich catalog) |> mustOk
    match ArtifactByKind.tryFind customerKey outputs with
    | Some file -> file.Body
    | None -> failwithf "Customer kind missing from SSDT outputs"

/// NM-70 — render the Customer body with the identity-annotation gate
/// set explicitly. `emitIdentityAnnotations = false` is the omit
/// posture (the `Projection.*` properties are suppressed).
let private customerSsdtBodyWithIdentityAnnotations (emitIdentityAnnotations: bool) (catalog: Catalog) : string =
    let outputs =
        SsdtDdlEmitter.emitSlicesWithRendering
            ConstraintFormatter.Enabled emitIdentityAnnotations DecisionOverlay.empty (enrich catalog)
        |> mustOk
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
    // Slice D.1.b — every kind now emits column-level V2.LogicalName
    // properties (@level2type = N'COLUMN'); narrow the original
    // "table-level only" assertion to the specific MS_Description
    // emission. The MS_Description statement appears at table level
    // (no @level2type on that specific EXECUTE block); column-level
    // V2.LogicalName statements coexist below it.
    let descriptionStmtStart =
        body.IndexOf("@name = N'MS_Description'", System.StringComparison.Ordinal)
    let descriptionStmtEnd =
        body.IndexOf("EXECUTE", descriptionStmtStart + 1, System.StringComparison.Ordinal)
    let descriptionStmt =
        if descriptionStmtEnd < 0 then body.Substring descriptionStmtStart
        else body.Substring(descriptionStmtStart, descriptionStmtEnd - descriptionStmtStart)
    Assert.Contains("@level1type = N'TABLE'", descriptionStmt)
    Assert.DoesNotContain("@level2type", descriptionStmt)

[<Fact>]
let ``Chapter 4.1.A slice 8: Kind without Description emits no MS_Description`` () =
    let body = customerSsdtBody sampleCatalog
    Assert.DoesNotContain("MS_Description", body)
    // Slice D.1.b — V2.LogicalName extended properties emit
    // unconditionally per kind / attribute for roundtrip recovery;
    // the absence assertion narrows to MS_Description only.

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
        | ToleratedDivergence.IndexOptionsUnreflected           -> ()
        | ToleratedDivergence.StaticPopulationsUnreflected -> ()
        | ToleratedDivergence.EmptyTextNormalizedToNull    -> ()
        | ToleratedDivergence.CompositePkFkUnreflected     -> ()
        | ToleratedDivergence.CharAnsiPaddingTolerated     -> ()
        | ToleratedDivergence.DecimalScaleTolerated        -> ()
        | ToleratedDivergence.TriggerBodyUnparsedDropped   -> ()
    // AC-D6 (NEITHER→HELD) added the two representation-only tolerances;
    // NM-28 (2026-06-14) added CompositePkFkUnreflected; NM-17 (2026-06-14)
    // RETIRED the four NM-16 kind-facet diff-erasure tolerances (now a real
    // `KindFacet` diff channel), dropping the count from 12 to 8. M1′ + M2
    // (THE VECTOR, Wave 0, 2026-06-15) added the two Decision-axis tolerances
    // (FkTrustUnreflected / UniquePromotionUnreflected) + TriggerBodyUnparsedDropped,
    // raising the count to 11. **M1 (THE VECTOR, Wave 1, 2026-06-15) RETIRED the
    // two Decision-axis tolerances** — the round-trip now observes FK-trust /
    // unique-promotion through the general comparator — dropping the count to 9.
    Assert.Equal(9, Set.count ToleratedDivergence.allKnown)

// ---------------------------------------------------------------------------
// NM-70 (WP5) — the identity-annotation emit | omit gate.
//
// `emitIdentityAnnotations = true` (the default) emits the `Projection.*`
// identity extended properties unconditionally (byte-identical to
// pre-NM-70). `false` suppresses them; other extended properties
// (MS_Description, authored ExtendedProperties) still emit.
// ---------------------------------------------------------------------------

[<Fact>]
let ``NM-70: default (emit) renders the Projection.SsKey + Projection.LogicalName identity properties`` () =
    let body = customerSsdtBodyWithIdentityAnnotations true sampleCatalog
    Assert.Contains("@name = N'Projection.SsKey'", body)
    Assert.Contains("@name = N'Projection.LogicalName'", body)

[<Fact>]
let ``NM-70: emit-on path is byte-identical to the default emitSlices body`` () =
    // The gate's `true` branch must not perturb the default emission.
    let gated   = customerSsdtBodyWithIdentityAnnotations true sampleCatalog
    let default_ = customerSsdtBody sampleCatalog
    Assert.Equal<string>(default_, gated)

[<Fact>]
let ``NM-70: omit suppresses the Projection.* identity extended properties`` () =
    let body = customerSsdtBodyWithIdentityAnnotations false sampleCatalog
    Assert.DoesNotContain("Projection.SsKey", body)
    Assert.DoesNotContain("Projection.LogicalName", body)

[<Fact>]
let ``NM-70: omit still emits MS_Description and authored extended properties`` () =
    // Other extended properties survive the identity-annotation omit.
    let catalog =
        withCustomerExtendedProperty "OutSystems_EntityId" (Some "42")
        |> fun c ->
            { c with
                Modules =
                    c.Modules
                    |> List.map (fun m ->
                        { m with
                            Kinds =
                                m.Kinds
                                |> List.map (fun k ->
                                    if k.SsKey = customerKey then { k with Description = Some "Customer master" }
                                    else k) }) }
    let body = customerSsdtBodyWithIdentityAnnotations false catalog
    Assert.DoesNotContain("Projection.SsKey", body)
    Assert.Contains("@name = N'MS_Description'", body)
    Assert.Contains("@name = N'OutSystems_EntityId'", body)
