module Projection.Tests.SinkFreshnessTests

open System
open Xunit
open Projection.Core
open Projection.Pipeline
open Projection.Adapters.Sql

// The sink freshness axis (the data-sink chapter, S8): the pure decision
// table over `off | auto | pinned` × `--refresh` × recorded × probed, with
// its CLOSED miss taxonomy — every wire-pay is named, and every miss the
// type can express is pinned here. R2 rides beneath every row: no decision
// in this table touches witnessing.

let private manifest (fingerprints: (string * string) list) : SinkStore.Manifest =
    { ConnDigest = "abcd1234abcd1234"
      LatestSyncId = 3
      EnvLabel = Some "uat"
      SourceDataSource = "server"
      SourceDatabase = "db"
      CapturedAtUtc = DateTimeOffset.Parse("2026-08-15T12:00:00Z", Globalization.CultureInfo.InvariantCulture)
      SnapshotSha256 = "sha"
      SourceFingerprints = fingerprints }

let private recorded =
    [ "dbo.ossys_Espace", "2|900|-123"
      "dbo.ossys_Entity", "8|9003|456"
      "dbo.ossys_Entity_Attr", "40|80031|789" ]

[<Fact>]
let ``refresh is the operator's one-run override — it names the decision under every policy, even a pin`` () =
    for policy in Config.SinkPolicy.all do
        match SinkFreshness.decide policy true (Some (manifest recorded)) (Some recorded) with
        | SinkFreshness.Decision.ReadLive SinkFreshness.Miss.RefreshForced -> ()
        | other -> Assert.Fail (sprintf "expected RefreshForced under %A, got %A" policy other)

[<Fact>]
let ``off pays the wire by name; pinned reuses without probing; both are total over an absent store`` () =
    match SinkFreshness.decide Config.SinkPolicy.Off false (Some (manifest recorded)) (Some recorded) with
    | SinkFreshness.Decision.ReadLive SinkFreshness.Miss.PolicyOff -> ()
    | other -> Assert.Fail (sprintf "expected PolicyOff, got %A" other)
    match SinkFreshness.decide Config.SinkPolicy.Pinned false (Some (manifest recorded)) None with
    | SinkFreshness.Decision.ReuseSink 3 -> ()
    | other -> Assert.Fail (sprintf "expected the pin to reuse without probing, got %A" other)
    for policy in [ Config.SinkPolicy.Pinned; Config.SinkPolicy.Auto ] do
        match SinkFreshness.decide policy false None None with
        | SinkFreshness.Decision.ReadLive SinkFreshness.Miss.NoWitnessedState -> ()
        | other -> Assert.Fail (sprintf "expected NoWitnessedState under %A, got %A" policy other)

[<Fact>]
let ``auto reuses only a fingerprint-confirmed state; every miss shape is its own name`` () =
    // Confirmed → reuse the latest witnessed sync.
    match SinkFreshness.decide Config.SinkPolicy.Auto false (Some (manifest recorded)) (Some recorded) with
    | SinkFreshness.Decision.ReuseSink 3 -> ()
    | other -> Assert.Fail (sprintf "expected confirmed reuse, got %A" other)
    // No recording (pre-S8 witness or capture-time probe failure) → live.
    match SinkFreshness.decide Config.SinkPolicy.Auto false (Some (manifest [])) (Some recorded) with
    | SinkFreshness.Decision.ReadLive SinkFreshness.Miss.FingerprintAbsent -> ()
    | other -> Assert.Fail (sprintf "expected FingerprintAbsent, got %A" other)
    // The probe failed NOW → live (freshness cannot be confirmed).
    match SinkFreshness.decide Config.SinkPolicy.Auto false (Some (manifest recorded)) None with
    | SinkFreshness.Decision.ReadLive SinkFreshness.Miss.ProbeFailed -> ()
    | other -> Assert.Fail (sprintf "expected ProbeFailed, got %A" other)
    // Movement names the targets that moved (a missing target is movement).
    let movedEntity = recorded |> List.map (fun (t, f) -> if t = "dbo.ossys_Entity" then t, "9|9004|999" else t, f)
    match SinkFreshness.decide Config.SinkPolicy.Auto false (Some (manifest recorded)) (Some movedEntity) with
    | SinkFreshness.Decision.ReadLive (SinkFreshness.Miss.FingerprintMoved [ "dbo.ossys_Entity" ]) -> ()
    | other -> Assert.Fail (sprintf "expected the entity bellwether to name itself, got %A" other)
    match SinkFreshness.decide Config.SinkPolicy.Auto false (Some (manifest recorded)) (Some (List.tail recorded)) with
    | SinkFreshness.Decision.ReadLive (SinkFreshness.Miss.FingerprintMoved [ "dbo.ossys_Espace" ]) -> ()
    | other -> Assert.Fail (sprintf "expected the absent espace probe to read as movement, got %A" other)

[<Fact>]
let ``the three bellwethers are the metadata plane's lifecycle tables, and rendering is one form both sides share`` () =
    Assert.Equal<string list>(
        [ "dbo.ossys_Espace"; "dbo.ossys_Entity"; "dbo.ossys_Entity_Attr" ],
        SinkFreshness.targets |> List.map SinkFreshness.nameOf)
    // Star-form checksum (no column inventory assumed for a platform table);
    // single-column ID max on every target.
    Assert.All(SinkFreshness.targets, fun t ->
        Assert.Equal(None, t.ChecksumColumns)
        Assert.Equal(Some "ID", t.MaxPkColumn))
    let reading : TableFingerprint.TableReading =
        { Target = SinkFreshness.targets.Head; RowCount = 42L; MaxPk = Some "900"; Content = None }
    Assert.Equal("42|900|-", SinkFreshness.render reading)

[<Fact>]
let ``the estate probe's SQL is byte-identical through the TableFingerprint extraction (per-form pin)`` () =
    // The estate caller (EvidenceFingerprint) renders explicit checksum
    // columns and a single-column MAX; the sink renders the star form. One
    // builder, both forms — pinned at the SQL text so the extraction cannot
    // have changed what the estate emits.
    let explicit' : TableFingerprint.TableTarget =
        { Schema = "dbo"; Table = "OSUSR_X_ORDER"; MaxPkColumn = Some "ID"; ChecksumColumns = Some [ "ID"; "NAME" ] }
    Assert.Equal(
        "SELECT 0 AS [i], COUNT_BIG(*) AS [c], CAST(MAX([ID]) AS NVARCHAR(128)) AS [m], CAST(CHECKSUM_AGG(BINARY_CHECKSUM([ID], [NAME])) AS NVARCHAR(32)) AS [h] FROM [dbo].[OSUSR_X_ORDER]",
        TableFingerprint.probeSql [ explicit' ])
    let noPkNoCols : TableFingerprint.TableTarget =
        { Schema = "dbo"; Table = "T"; MaxPkColumn = None; ChecksumColumns = Some [] }
    Assert.Equal(
        "SELECT 0 AS [i], COUNT_BIG(*) AS [c], CAST(NULL AS NVARCHAR(128)) AS [m], CAST(NULL AS NVARCHAR(32)) AS [h] FROM [dbo].[T]",
        TableFingerprint.probeSql [ noPkNoCols ])
    let star : TableFingerprint.TableTarget =
        { Schema = "dbo"; Table = "ossys_Entity"; MaxPkColumn = Some "ID"; ChecksumColumns = None }
    Assert.Equal(
        "SELECT 0 AS [i], COUNT_BIG(*) AS [c], CAST(MAX([ID]) AS NVARCHAR(128)) AS [m], CAST(CHECKSUM_AGG(BINARY_CHECKSUM(*)) AS NVARCHAR(32)) AS [h] FROM [dbo].[ossys_Entity]",
        TableFingerprint.probeSql [ star ])
