module Projection.Tests.ProfileAnomalyPassTests

open Xunit
open Projection.Core
open Projection.Core.Passes
open Projection.Tests.IRBuilders

// ---------------------------------------------------------------------------
// H-073 — Profile anomaly detection (null-rate + CV)
// ---------------------------------------------------------------------------

let private synthKey (ns: string) (key: string) : SsKey =
    SsKey.synthesized ns key |> Result.value

let private physical (table: string) : PhysicalRealization =
    { Schema = "dbo"; Table = table; Catalog = None }

let private mkAttr (root: string) (name: string) (isNullable: bool) : Attribute =
    let key = synthKey root name
    { Attribute.create key (Name.create name |> Result.value) PrimitiveType.Integer with
        Column = { ColumnName = name; IsNullable = isNullable } }

let private attrKey root name = synthKey root name

let private mkColumnProfile (key: SsKey) (rowCount: int64) (nullCount: int64) : ColumnProfile =
    { AttributeKey         = key
      RowCount             = rowCount
      NullCount            = nullCount
      NullCountProbeStatus = ProbeStatus.noProbeRun }

// Build a one-Kind catalog with N+1 attributes (1 PK + N data columns).
// N=10 gives enough sample size for the 2σ rule to detect a single
// outlier when the rest of the population is tightly clustered.
let private dataAttrNames : string list =
    [ "A"; "B"; "C"; "D"; "E"; "F"; "G"; "H"; "I"; "J" ]

let private buildOneKindCatalog () : Catalog =
    let kindKey = synthKey "M" "T"
    let pkAttr =
        mkAttr "M" "Id" false |> fun a -> { a with IsPrimaryKey = true }
    let dataAttrs =
        dataAttrNames |> List.map (fun n -> mkAttr "M" n true)
    let attrs = pkAttr :: dataAttrs
    let k = Kind.create kindKey (Name.create "T" |> Result.value) (physical "T") attrs
    mkCatalog [ mkModule (synthKey "M" "M") (Name.create "M" |> Result.value) [ k ] ]

[<Fact>]
let ``empty catalog with empty profile produces empty anomaly report`` () =
    let catalog = mkCatalog []
    let result =
        ProfileAnomalyPass.run catalog Profile.empty
        |> LineageDiagnostics.payload
    Assert.Empty(result.HighNullRateColumns)
    Assert.Empty(result.HighCvColumns)

[<Fact>]
let ``catalog with no profile data produces empty anomaly report`` () =
    let catalog = buildOneKindCatalog ()
    let result =
        ProfileAnomalyPass.run catalog Profile.empty
        |> LineageDiagnostics.payload
    Assert.Empty(result.HighNullRateColumns)
    Assert.Empty(result.HighCvColumns)

[<Fact>]
let ``uniform null rates produce no anomalies`` () =
    // All attributes have the same null rate → σ = 0 → no anomaly.
    let catalog = buildOneKindCatalog ()
    let colKeys = [ "A"; "B"; "C"; "D" ] |> List.map (attrKey "M")
    let profile =
        { Profile.empty with
            Columns = colKeys |> List.map (fun k -> mkColumnProfile k 100L 10L) }
    let result =
        ProfileAnomalyPass.run catalog profile
        |> LineageDiagnostics.payload
    Assert.Empty(result.HighNullRateColumns)

// 10-column profile where 9 columns have 0 null rate (mean small,
// σ small) and 1 column has 100% null rate. Mean+2σ < 1.0 ⇒ flagged.
let private mkSkewedProfile (outlierKey: SsKey) : Profile =
    let colKeys = dataAttrNames |> List.map (attrKey "M")
    { Profile.empty with
        Columns =
            colKeys
            |> List.map (fun key ->
                let nullCount = if key = outlierKey then 100L else 0L
                mkColumnProfile key 100L nullCount) }

[<Fact>]
let ``one column with much higher null rate than peers is flagged`` () =
    let catalog = buildOneKindCatalog ()
    let outlier = attrKey "M" "J"
    let profile = mkSkewedProfile outlier
    let result =
        ProfileAnomalyPass.run catalog profile
        |> LineageDiagnostics.payload
    Assert.NotEmpty(result.HighNullRateColumns)
    let flaggedKeys = result.HighNullRateColumns |> List.map fst
    Assert.Contains(outlier, flaggedKeys)

[<Fact>]
let ``null rate anomaly produces Warning diagnostics with correct code`` () =
    let catalog = buildOneKindCatalog ()
    let profile = mkSkewedProfile (attrKey "M" "J")
    let diagnostics =
        ProfileAnomalyPass.run catalog profile
        |> LineageDiagnostics.entries
    let anomalyDiags =
        diagnostics |> List.filter (fun d -> d.Code = "profiling.anomaly.nullRate.high")
    Assert.NotEmpty(anomalyDiags)
    for d in anomalyDiags do
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity)

[<Fact>]
let ``high-null-rate columns are sorted by SsKey`` () =
    let catalog = buildOneKindCatalog ()
    // Multiple outliers (last 2 columns of 10) trigger detection;
    // sort assertion verifies output ordering.
    let colKeys = dataAttrNames |> List.map (attrKey "M")
    let outliers = Set.ofList [ attrKey "M" "I"; attrKey "M" "J" ]
    let profile =
        { Profile.empty with
            Columns =
                colKeys
                |> List.map (fun key ->
                    let nullCount = if Set.contains key outliers then 100L else 0L
                    mkColumnProfile key 100L nullCount) }
    let result =
        ProfileAnomalyPass.run catalog profile
        |> LineageDiagnostics.payload
    let keys = result.HighNullRateColumns |> List.map fst
    Assert.Equal<SsKey list>(List.sort keys, keys)
