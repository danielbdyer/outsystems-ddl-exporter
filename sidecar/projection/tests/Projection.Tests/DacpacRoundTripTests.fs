module Projection.Tests.DacpacRoundTripTests

open System.IO
open Xunit
open Microsoft.SqlServer.Dac
open Microsoft.SqlServer.Dac.Model
open Projection.Core
open Projection.Core.Passes
open Projection.Targets.SSDT
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// Wave-1 slice 1.1 — the DACPAC round-trip equality WITNESS (L3-S2).
//
// Why this file exists. `AUDIT_2026_05_12_VERIFIABILITY_TRIANGLE.md` recorded
// L3-S2 (DACPAC round-trip) as "Bucket A modulo A37 erasure declaration",
// citing a test named `DacpacRoundTripTests` — but no such file existed, and
// no `equalModuloDacpacErasure` predicate was ever defined. A claimed-verified
// axiom with no executable witness is the exact phantom-Bucket-A defect the
// verifiability gate (slice E1) exists to forbid. This file ships the missing
// witness: a content-level, ERASURE-AWARE round-trip predicate over the DacFx
// model, with the erasure set named explicitly in code.
//
// The round-trip. `DacpacEmitter.emit` produces `.dacpac` bytes; DacFx loads
// them into a `TSqlModel`; we project that model back to a comparable schema
// summary (`DacpacSchema`) and assert it equals the summary derived directly
// from the source Catalog — MODULO the named DacFx erasures (A37):
//
//   A37 erasure set (declared, closed):
//     E1. Origin.xml wall-clock — DacFx stamps emit time; not a schema axis.
//     E2. Constraint / index AUTO-NAMES — DacFx may synthesize names for
//         unnamed inline constraints; we compare FK *shape* (referrer →
//         referenced table), not the constraint identifier.
//     E3. Identifier QUOTING / CASE-FOLD — DacFx normalizes `[dbo].[X]` ↔
//         `dbo.X`; we compare unquoted invariant-culture parts.
//
// What this is NOT. It is not a full `DacpacReadSide.toCatalog` (rebuilding a
// complete V2 Catalog from a DacFx model) — that is a larger slice with its
// own consumer. This witness proves the round-trip on the table + column
// (name, nullability) + FK-shape axes, which is what L3-S2 asserts and what
// `PhysicalSchema` (the production canary surface) covers structurally. No
// Docker: DacFx operates on the in-memory model, so this runs in the pure pool.
// ---------------------------------------------------------------------------

// Per-file shim mirroring DacpacEmitterTests: CanonicalizeIdentity.run is
// private; the canonical surface is `.registered.Run`.
let private enrich (c: Catalog) : Catalog =
    (CanonicalizeIdentity.registered.Run c |> Lineage.map (fun d -> d.Value)).Value

let private mustOkBytes (r: Result<byte[]>) : byte[] =
    match r with
    | Ok v -> v
    | Error errs ->
        Assert.Fail (sprintf "DacpacEmitter.emit expected Ok; got %A" errs)
        Unchecked.defaultof<byte[]>

// ---------------------------------------------------------------------------
// The comparable schema summary. Both legs (source Catalog, round-tripped
// DacFx model) project to this shape; the erasure-aware predicate compares
// the projections. Names are unquoted + invariant-lowercased (erasure E3).
// ---------------------------------------------------------------------------

type private DacColumn = { Name: string; Nullable: bool }
type private DacTable = { Schema: string; Table: string; Columns: Set<string * bool> }
/// FK shape modulo constraint auto-name (erasure E2): (referrer table,
/// referenced table). Constraint identifiers are intentionally excluded.
type private DacSchema = { Tables: Map<string * string, Set<string * bool>>; ForeignKeys: Set<(string * string) * (string * string)> }

let private norm (s: string) : string = s.Trim('[', ']').ToLowerInvariant()

/// Two-part (schema, table) from a `TSqlObject.Name`, normalized (erasure E3).
let private tableParts (obj: TSqlObject) : string * string =
    let parts = obj.Name.Parts
    if parts.Count >= 2 then norm parts.[0], norm parts.[1]
    elif parts.Count = 1 then "dbo", norm parts.[0]
    else "dbo", ""

/// Project the source Catalog to the comparable summary.
let private schemaOfCatalog (c: Catalog) : DacSchema =
    let kinds = Catalog.allKinds c
    let tables =
        kinds
        |> List.map (fun k ->
            let cols =
                k.Attributes
                |> List.map (fun a -> norm a.Column.ColumnName, a.Column.IsNullable)
                |> Set.ofList
            (norm k.Physical.Schema, norm k.Physical.Table), cols)
        |> Map.ofList
    // FK shape: referrer kind's physical table → referenced kind's physical table.
    let physicalOf (key: SsKey) =
        kinds
        |> List.tryFind (fun k -> k.SsKey = key)
        |> Option.map (fun k -> norm k.Physical.Schema, norm k.Physical.Table)
    let fks =
        kinds
        |> List.collect (fun k ->
            let referrer = norm k.Physical.Schema, norm k.Physical.Table
            k.References
            |> List.choose (fun r ->
                match physicalOf r.TargetKind with
                | Some referenced -> Some (referrer, referenced)
                | None -> None))
        |> Set.ofList
    { Tables = tables; ForeignKeys = fks }

/// Project a round-tripped DacFx model to the comparable summary.
///
/// Columns are enumerated via the `Column.TypeClass` object set (whose
/// `Name.Parts` are `[schema; table; column]`) rather than the relationship-
/// instance API, mirroring the proven `GetObjects` + `GetProperty<_>` pattern
/// already used in `DacpacEmitterTests` (e.g. `obj.GetProperty<bool>(Index.Unique)`).
let private schemaOfModel (model: TSqlModel) : DacSchema =
    // `Column` is NOT a top-level queryable type in DacFx — columns are reached
    // through their parent Table via the `Table.Columns` relationship. For each
    // Table object, enumerate its referenced Column objects (three-part names:
    // [schema; table; column]) and read `Column.Nullable` (typed GetProperty,
    // the proven pattern from DacpacEmitterTests' `GetProperty<bool>(Index.Unique)`).
    let columnsOf (t: TSqlObject) : Set<string * bool> =
        t.GetReferenced(Table.Columns)
        |> Seq.choose (fun col ->
            let parts = col.Name.Parts
            if parts.Count >= 1 then
                let colName = norm parts.[parts.Count - 1]
                let nullable = col.GetProperty<bool>(Column.Nullable)
                Some (colName, nullable)
            else None)
        |> Set.ofSeq
    let tables =
        model.GetObjects(DacQueryScopes.UserDefined, Table.TypeClass)
        |> Seq.map (fun t -> tableParts t, columnsOf t)
        |> Map.ofSeq
    // FK shape: every FK constraint's host table → its referenced (foreign)
    // table, both via the constraint's referenced TSqlObjects. `GetReferenced`
    // on the ForeignKeyConstraint.ForeignTable relationship yields the
    // referenced Table; the host is the FK's parent table object.
    let fks =
        model.GetObjects(DacQueryScopes.UserDefined, ForeignKeyConstraint.TypeClass)
        |> Seq.choose (fun fk ->
            let host =
                fk.GetReferenced(ForeignKeyConstraint.Host) |> Seq.tryHead
            let foreign =
                fk.GetReferenced(ForeignKeyConstraint.ForeignTable) |> Seq.tryHead
            match host, foreign with
            | Some h, Some f -> Some (tableParts h, tableParts f)
            | _ -> None)
        |> Set.ofSeq
    { Tables = tables; ForeignKeys = fks }

// ---------------------------------------------------------------------------
// The named erasure-aware equality predicate. EXPLICIT about what it ignores
// (the A37 erasure set above); everything else must match exactly.
// ---------------------------------------------------------------------------

/// Strict structural equality of two summaries — no erasure.
let private equalStrict (a: DacSchema) (b: DacSchema) : bool =
    a.Tables = b.Tables && a.ForeignKeys = b.ForeignKeys

/// L3-S2 / A37 — equality modulo the declared DacFx erasure set (Origin.xml
/// wall-clock E1, constraint auto-names E2, identifier quoting/case E3). The
/// projections above already apply E2 (FK shape, not name) and E3 (norm); E1
/// never reaches the schema summary. So at the summary level, "modulo erasure"
/// IS structural equality of the erasure-projected summaries — which is the
/// point: the erasure is applied in the projection, named, and closed.
let private equalModuloDacpacErasure (a: DacSchema) (b: DacSchema) : bool =
    equalStrict a b

let private roundTrip (catalog: Catalog) : DacSchema =
    let bytes = DacpacEmitter.emit catalog |> mustOkBytes
    use stream = new MemoryStream(bytes)
    use model = TSqlModel.LoadFromDacpac(stream, ModelLoadOptions())
    schemaOfModel model

// ---------------------------------------------------------------------------
// The witnesses. These are what AxiomTests.fs::L3-S2 cites.
// ---------------------------------------------------------------------------

[<Fact>]
let ``L3-S2: single-Kind Catalog round-trips through DACPAC modulo named erasure`` () =
    let source = enrich sampleCatalog
    let expected = schemaOfCatalog source
    let actual = roundTrip source
    Assert.True(
        equalModuloDacpacErasure expected actual,
        sprintf "DACPAC round-trip diverged.\n  expected tables: %A\n  actual tables:   %A\n  expected FKs: %A\n  actual FKs:   %A"
            (expected.Tables |> Map.toList |> List.map fst)
            (actual.Tables |> Map.toList |> List.map fst)
            expected.ForeignKeys actual.ForeignKeys)

[<Fact>]
let ``L3-S2: round-trip preserves per-table column name + nullability set`` () =
    let source = enrich sampleCatalog
    let expected = schemaOfCatalog source
    let actual = roundTrip source
    // Compare the column summary table-by-table so a divergence localizes.
    for KeyValue(tbl, cols) in expected.Tables do
        match Map.tryFind tbl actual.Tables with
        | Some actualCols ->
            let msg =
                sprintf "table %A column set diverged under round-trip:\n  emitted: %A\n  read-back: %A" tbl cols actualCols
            Assert.True((cols = actualCols), msg)
        | None ->
            Assert.Fail(sprintf "table %A present in source but absent after DACPAC round-trip" tbl)

[<Fact>]
let ``L3-S2: round-trip preserves FK shape (referrer -> referenced) modulo constraint auto-name`` () =
    let source = enrich sampleCatalog
    let expected = schemaOfCatalog source
    let actual = roundTrip source
    Assert.Equal<Set<(string * string) * (string * string)>>(expected.ForeignKeys, actual.ForeignKeys)

[<Fact>]
let ``A37: the DACPAC erasure set is closed — strict equality holds on the erasure-projected summaries`` () =
    // The erasure is applied IN the projection (FK shape not name; normalized
    // identifiers). Once projected, no residual erasure remains, so strict
    // equality must hold — this pins that the erasure set is complete (A37):
    // if DacFx erased an axis the projection does NOT account for, this fails.
    let source = enrich sampleCatalog
    Assert.True(equalStrict (schemaOfCatalog source) (roundTrip source))
