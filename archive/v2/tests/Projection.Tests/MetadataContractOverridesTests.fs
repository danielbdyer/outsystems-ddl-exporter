module Projection.Tests.MetadataContractOverridesTests

open Xunit
open Projection.Core
open Projection.Adapters.OssysSql

// ---------------------------------------------------------------------------
// Chapter B.4 slice 5 — MetadataContractOverrides carbon-copy from V1
// (`src/Osm.Pipeline/SqlExtraction/MetadataContractOverrides.cs`).
//
// Operator-supplied weakening of V2's strict metadata-contract enforcement
// at SQL extraction time. The slice ships the mechanism (data type + smart
// constructor + lookup); wiring into specific V2 mappers defers to slice 7
// where the `full-export` orchestrator resolves operator config + threads
// overrides through `MetadataSnapshotRunner`.
//
// V1 parity tests: each V1 `MetadataContractOverridesTests` scenario has a
// V2 mirror here adapted to V2's `Result<_>`-returning smart constructor +
// `Set<string>`-valued option lists + case-insensitive lowercase
// normalization with original-case label preservation.
// ---------------------------------------------------------------------------

let private mustOk r =
    match r with
    | Ok v -> v
    | Error es ->
        let codes = es |> List.map (fun e -> e.Code) |> String.concat ", "
        invalidOp (sprintf "fixture: %s" codes)

// ---------------------------------------------------------------------------
// `empty` and `hasOverrides`.
// ---------------------------------------------------------------------------

[<Fact>]
let ``MetadataContractOverrides.empty has no overrides (hasOverrides false)`` () =
    Assert.False(MetadataContractOverrides.hasOverrides MetadataContractOverrides.empty)

[<Fact>]
let ``MetadataContractOverrides.empty has empty OptionalColumns / ResultSetLabels / ColumnLabels`` () =
    let e = MetadataContractOverrides.empty
    Assert.True(Map.isEmpty e.OptionalColumns)
    Assert.True(Map.isEmpty e.ResultSetLabels)
    Assert.True(Map.isEmpty e.ColumnLabels)

[<Fact>]
let ``MetadataContractOverrides.isColumnOptional false on empty for any column`` () =
    Assert.False(
        MetadataContractOverrides.isColumnOptional
            "AttributeJson"
            "AttributesJson"
            MetadataContractOverrides.empty)

// ---------------------------------------------------------------------------
// `create` — operator-input parsing.
// ---------------------------------------------------------------------------

[<Fact>]
let ``MetadataContractOverrides.create populates single (resultSet, column) pair`` () =
    let o =
        MetadataContractOverrides.create
            [ "AttributeJson", seq { "AttributesJson" } ]
        |> mustOk
    Assert.True(MetadataContractOverrides.hasOverrides o)
    Assert.True(
        MetadataContractOverrides.isColumnOptional "AttributeJson" "AttributesJson" o)

[<Fact>]
let ``MetadataContractOverrides.create normalizes keys + columns to lowercase`` () =
    let o =
        MetadataContractOverrides.create
            [ "AttributeJson", seq { "AttributesJson" } ]
        |> mustOk
    // Keys + columns stored lowercase.
    Assert.True(Map.containsKey "attributejson" o.OptionalColumns)
    let columnsForKey = Map.find "attributejson" o.OptionalColumns
    Assert.True(Set.contains "attributesjson" columnsForKey)

[<Fact>]
let ``MetadataContractOverrides.create preserves original-case labels for diagnostics`` () =
    let o =
        MetadataContractOverrides.create
            [ "AttributeJson", seq { "AttributesJson" } ]
        |> mustOk
    Assert.Equal(Some "AttributeJson",
        MetadataContractOverrides.tryResultSetLabel "attributejson" o)
    Assert.Equal(Some "AttributesJson",
        MetadataContractOverrides.tryColumnLabel "attributejson" "attributesjson" o)

[<Fact>]
let ``MetadataContractOverrides.isColumnOptional is case-insensitive`` () =
    let o =
        MetadataContractOverrides.create
            [ "AttributeJson", seq { "AttributesJson" } ]
        |> mustOk
    Assert.True(
        MetadataContractOverrides.isColumnOptional
            "attributeJSON" "attributesJSON" o)
    Assert.True(
        MetadataContractOverrides.isColumnOptional
            "ATTRIBUTEJSON" "ATTRIBUTESJSON" o)

[<Fact>]
let ``MetadataContractOverrides.create trims surrounding whitespace`` () =
    let o =
        MetadataContractOverrides.create
            [ "  AttributeJson  ", seq { "  AttributesJson  " } ]
        |> mustOk
    Assert.True(
        MetadataContractOverrides.isColumnOptional
            "AttributeJson" "AttributesJson" o)

[<Fact>]
let ``MetadataContractOverrides.create supports multiple result sets`` () =
    let o =
        MetadataContractOverrides.create
            [ "AttributeJson", seq { "AttributesJson" }
              "ModuleJson", seq { "EspaceJson"; "ManifestJson" } ]
        |> mustOk
    Assert.True(
        MetadataContractOverrides.isColumnOptional "AttributeJson" "AttributesJson" o)
    Assert.True(
        MetadataContractOverrides.isColumnOptional "ModuleJson" "EspaceJson" o)
    Assert.True(
        MetadataContractOverrides.isColumnOptional "ModuleJson" "ManifestJson" o)
    Assert.False(
        MetadataContractOverrides.isColumnOptional "AttributeJson" "EspaceJson" o)

[<Fact>]
let ``MetadataContractOverrides.create merges columns when same result set appears twice`` () =
    let o =
        MetadataContractOverrides.create
            [ "AttributeJson", seq { "AttributesJson" }
              "AttributeJson", seq { "ExtraJson" } ]
        |> mustOk
    Assert.True(
        MetadataContractOverrides.isColumnOptional "AttributeJson" "AttributesJson" o)
    Assert.True(
        MetadataContractOverrides.isColumnOptional "AttributeJson" "ExtraJson" o)

[<Fact>]
let ``MetadataContractOverrides.create silently skips blank column entries (V1 parity)`` () =
    let o =
        MetadataContractOverrides.create
            [ "AttributeJson", seq { "AttributesJson"; "   "; "" } ]
        |> mustOk
    Assert.True(
        MetadataContractOverrides.isColumnOptional "AttributeJson" "AttributesJson" o)
    let columns = Map.find "attributejson" o.OptionalColumns
    Assert.Equal(1, Set.count columns)

[<Fact>]
let ``MetadataContractOverrides.create drops result sets with no retained columns (V1 parity)`` () =
    let o =
        MetadataContractOverrides.create
            [ "EmptySet", seq { ""; "   " }
              "RealSet", seq { "RealColumn" } ]
        |> mustOk
    Assert.False(Map.containsKey "emptyset" o.OptionalColumns)
    Assert.True(Map.containsKey "realset" o.OptionalColumns)

[<Fact>]
let ``MetadataContractOverrides.create returns empty when input sequence is null`` () =
    let r =
        MetadataContractOverrides.create (Unchecked.defaultof<(string * string seq) seq>)
        |> mustOk
    Assert.False(MetadataContractOverrides.hasOverrides r)

[<Fact>]
let ``MetadataContractOverrides.create with blank columns sequence yields no entry`` () =
    let o =
        MetadataContractOverrides.create
            [ "OnlyBlanks", Seq.empty ]
        |> mustOk
    Assert.False(MetadataContractOverrides.hasOverrides o)

[<Fact>]
let ``MetadataContractOverrides.create rejects null result-set name`` () =
    let entries : (string * string seq) seq =
        seq { "Valid", seq { "C1" }
              Unchecked.defaultof<string>, seq { "C2" } }
    let r = MetadataContractOverrides.create entries
    match r with
    | Ok _ -> Assert.Fail("expected validation failure")
    | Error es ->
        Assert.Contains(es, fun e -> e.Code = "metadataContract.resultSet.null")

[<Fact>]
let ``MetadataContractOverrides.create rejects whitespace result-set name`` () =
    let r =
        MetadataContractOverrides.create
            [ "  ", seq { "C1" } ]
    match r with
    | Ok _ -> Assert.Fail("expected validation failure")
    | Error es ->
        Assert.Contains(es, fun e -> e.Code = "metadataContract.resultSet.empty")

[<Fact>]
let ``MetadataContractOverrides.create accumulates errors across entries`` () =
    let r =
        MetadataContractOverrides.create
            [ "", seq { "C1" }
              "  ", seq { "C2" } ]
    match r with
    | Ok _ -> Assert.Fail("expected accumulated failures")
    | Error es ->
        let codes = es |> List.map (fun e -> e.Code)
        let expected : string list = [ "metadataContract.resultSet.empty"
                                       "metadataContract.resultSet.empty" ]
        Assert.Equal<string list>(expected, codes)

// ---------------------------------------------------------------------------
// `isColumnOptional` — runtime lookup.
// ---------------------------------------------------------------------------

[<Fact>]
let ``MetadataContractOverrides.isColumnOptional false for unknown result set`` () =
    let o =
        MetadataContractOverrides.create
            [ "AttributeJson", seq { "AttributesJson" } ]
        |> mustOk
    Assert.False(
        MetadataContractOverrides.isColumnOptional "OtherResultSet" "AttributesJson" o)

[<Fact>]
let ``MetadataContractOverrides.isColumnOptional false for unknown column in known set`` () =
    let o =
        MetadataContractOverrides.create
            [ "AttributeJson", seq { "AttributesJson" } ]
        |> mustOk
    Assert.False(
        MetadataContractOverrides.isColumnOptional "AttributeJson" "OtherColumn" o)

[<Fact>]
let ``MetadataContractOverrides.isColumnOptional false on blank inputs (V2 safety)`` () =
    let o =
        MetadataContractOverrides.create
            [ "AttributeJson", seq { "AttributesJson" } ]
        |> mustOk
    // V1 would throw ArgumentException; V2 returns the safe "strict" default.
    Assert.False(MetadataContractOverrides.isColumnOptional "" "AttributesJson" o)
    Assert.False(MetadataContractOverrides.isColumnOptional "AttributeJson" "" o)
    Assert.False(MetadataContractOverrides.isColumnOptional "   " "AttributesJson" o)

// ---------------------------------------------------------------------------
// `withOptional` — fluent additive builder (used by slice 7 config resolve).
// ---------------------------------------------------------------------------

[<Fact>]
let ``MetadataContractOverrides.withOptional adds a column to empty`` () =
    let o =
        MetadataContractOverrides.empty
        |> MetadataContractOverrides.withOptional "AttributeJson" "AttributesJson"
    Assert.True(MetadataContractOverrides.hasOverrides o)
    Assert.True(
        MetadataContractOverrides.isColumnOptional "AttributeJson" "AttributesJson" o)

[<Fact>]
let ``MetadataContractOverrides.withOptional is idempotent`` () =
    let once =
        MetadataContractOverrides.empty
        |> MetadataContractOverrides.withOptional "RS" "C1"
    let twice =
        once
        |> MetadataContractOverrides.withOptional "RS" "C1"
    Assert.Equal<Map<string, Set<string>>>(once.OptionalColumns, twice.OptionalColumns)
    Assert.Equal<Map<string, string>>(once.ResultSetLabels, twice.ResultSetLabels)

[<Fact>]
let ``MetadataContractOverrides.withOptional accumulates multiple columns`` () =
    let o =
        MetadataContractOverrides.empty
        |> MetadataContractOverrides.withOptional "RS" "C1"
        |> MetadataContractOverrides.withOptional "RS" "C2"
        |> MetadataContractOverrides.withOptional "OtherRS" "X"
    Assert.True(MetadataContractOverrides.isColumnOptional "RS" "C1" o)
    Assert.True(MetadataContractOverrides.isColumnOptional "RS" "C2" o)
    Assert.True(MetadataContractOverrides.isColumnOptional "OtherRS" "X" o)

[<Fact>]
let ``MetadataContractOverrides.withOptional is a no-op on blank inputs`` () =
    let baseOverride =
        MetadataContractOverrides.empty
        |> MetadataContractOverrides.withOptional "RS" "C1"
    let after =
        baseOverride
        |> MetadataContractOverrides.withOptional "" "C2"
        |> MetadataContractOverrides.withOptional "RS" ""
        |> MetadataContractOverrides.withOptional "   " "   "
    Assert.Equal<Map<string, Set<string>>>(
        baseOverride.OptionalColumns,
        after.OptionalColumns)

[<Fact>]
let ``MetadataContractOverrides.withOptional preserves original-case label on first sight`` () =
    let o =
        MetadataContractOverrides.empty
        |> MetadataContractOverrides.withOptional "AttributeJson" "AttributesJson"
        |> MetadataContractOverrides.withOptional "attributejson" "AnotherCol"
    Assert.Equal(Some "AttributeJson",
        MetadataContractOverrides.tryResultSetLabel "attributejson" o)
