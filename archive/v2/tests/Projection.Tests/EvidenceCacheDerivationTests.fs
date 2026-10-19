module Projection.Tests.EvidenceCacheDerivationTests

open Xunit
open Projection.Core
open Projection.Tests.Fixtures

// ---------------------------------------------------------------------------
// Single-scan evidence derivation — the pure half of the discover-once /
// derive-pure pattern applied to the evidence cache itself.
//
// Two laws under test:
//
//   1. `CachedValue.ofRaw` ≡ `CachedValue.ofReaderValue` across the
//      `RawValueCodec` raw-form contract: projecting a cell through
//      (format → raw string → ofRaw) yields the SAME `CachedValue` the
//      live profiler builds from the boxed reader value. This equivalence
//      is what lets an evidence cache derive from already-hydrated
//      `StaticRow`s instead of a second full SQL scan per kind.
//
//   2. `EvidenceCache.cachedKindOfRows` mirrors the live 3-query
//      discovery's output shape exactly for a fully-hydrated kind:
//      exact RowCount, exact per-attribute NullCounts, columns in
//      catalog attribute order, `ColumnsByKey` in exact correspondence
//      with `Columns`, nullability from the reflection slice with a
//      `false` default for unreflected columns, and the attribute-less
//      refusal (`None`) the live path also takes.
//
// The integration twin (corpus harness, `PERF_CORPUS=1`) asserts the
// composed law end-to-end: derived cache ≡ live-scan cache over a
// 480k-row estate. These tests pin the per-arm semantics so a codec or
// derivation regression is caught in the pure pool, not six minutes
// into a corpus run.
// ---------------------------------------------------------------------------

let private mustOk r =
    match r with
    | Ok v -> v
    | Error es ->
        let codes = es |> List.map (fun e -> e.Code) |> String.concat ", "
        invalidOp (sprintf "fixture: %s" codes)

let private mkKey (parts: string list) : SsKey =
    SsKey.synthesizedComposite "OS_TEST" parts |> mustOk

let private inv = System.Globalization.CultureInfo.InvariantCulture

/// Non-null upcast to `obj` — what a `SqlDataReader.GetValue` hands
/// `ofReaderValue` (FS3261: `box` types as `objnull`, the reader value
/// contract is non-null; DBNull is the null carrier, not CLR null).
let private ov (x: 'a) : obj = upcast x

// ---------------------------------------------------------------------------
// Law 1 — ofRaw ≡ ofReaderValue over the raw-form codec, per type arm.
// ---------------------------------------------------------------------------

[<Fact>]
let ``ofRaw: None is NULL for every primitive type (WP-3: NULL is out-of-band)`` () =
    let allTypes = [ Integer; Decimal; Text; Boolean; DateTime; Date; Time; Binary; Guid ]
    for t in allTypes do
        Assert.Equal(NullValue, CachedValue.ofRaw t None)

[<Fact>]
let ``ofRaw ≡ ofReaderValue: Integer raw forms parse to the reader's IntValue`` () =
    Assert.Equal(CachedValue.ofReaderValue (ov 42), CachedValue.ofRaw Integer (Some "42"))
    Assert.Equal(CachedValue.ofReaderValue (ov -7L), CachedValue.ofRaw Integer (Some "-7"))
    // bigint beyond int32 range survives the int64 parse
    Assert.Equal(CachedValue.ofReaderValue (ov 2147483648L), CachedValue.ofRaw Integer (Some "2147483648"))

[<Fact>]
let ``ofRaw ≡ ofReaderValue: Boolean raw forms project to IntValue 0/1 (bit profiles as int)`` () =
    Assert.Equal(CachedValue.ofReaderValue (ov true),  CachedValue.ofRaw Boolean (Some (RawValueCodec.formatBoolean true)))
    Assert.Equal(CachedValue.ofReaderValue (ov false), CachedValue.ofRaw Boolean (Some (RawValueCodec.formatBoolean false)))
    // parseBoolean's numeric raw forms land on the same values
    Assert.Equal(IntValue 1L, CachedValue.ofRaw Boolean (Some "1"))
    Assert.Equal(IntValue 0L, CachedValue.ofRaw Boolean (Some "0"))

[<Fact>]
let ``ofRaw ≡ ofReaderValue: Decimal invariant raw forms round-trip scale and sign`` () =
    for d in [ 12.34m; -0.5m; 0.0000001m; 99999999999999.99m; 0m ] do
        Assert.Equal(CachedValue.ofReaderValue (ov d), CachedValue.ofRaw Decimal (Some (d.ToString(inv))))

[<Fact>]
let ``ofRaw ≡ ofReaderValue: DateTime raw form round-trips to tick precision, offset Zero`` () =
    // sub-millisecond ticks — the 7-f format carries full tick precision
    let dt = System.DateTime(2026, 7, 2, 13, 45, 30).AddTicks(1234567L)
    Assert.Equal(CachedValue.ofReaderValue (ov dt), CachedValue.ofRaw DateTime (Some (RawValueCodec.formatDateTime dt)))

[<Fact>]
let ``ofRaw ≡ ofReaderValue: Date raw form lands on midnight, offset Zero`` () =
    let d = System.DateTime(2026, 7, 2)
    Assert.Equal(CachedValue.ofReaderValue (ov d), CachedValue.ofRaw Date (Some (RawValueCodec.formatDate d)))

[<Fact>]
let ``ofRaw ≡ ofReaderValue: Time raw form equals the reader TimeSpan's ToString fallback`` () =
    let ts = System.TimeSpan(0, 13, 45, 30, 123)
    Assert.Equal(CachedValue.ofReaderValue (ov ts), CachedValue.ofRaw Time (Some (RawValueCodec.formatTime ts)))

[<Fact>]
let ``ofRaw ≡ ofReaderValue: Guid raw form equals the reader Guid's ToString fallback`` () =
    let g = System.Guid("6f9619ff-8b86-d011-b42d-00c04fc964ff")
    Assert.Equal(CachedValue.ofReaderValue (ov g), CachedValue.ofRaw Guid (Some (RawValueCodec.formatGuid g)))

[<Fact>]
let ``ofRaw ≡ ofReaderValue: Text raw form is the string itself`` () =
    Assert.Equal(CachedValue.ofReaderValue (ov "hello"), CachedValue.ofRaw Text (Some "hello"))

[<Fact>]
let ``ofRaw ≡ ofReaderValue: Binary hex raw form round-trips byte-for-byte`` () =
    let bytes = [| 0xDEuy; 0xADuy; 0xBEuy; 0xEFuy |]
    let raw = System.Convert.ToHexString bytes
    Assert.Equal(CachedValue.ofReaderValue (ov bytes), CachedValue.ofRaw Binary (Some raw))

[<Fact>]
let ``ofRaw and ofReaderValue agree on the empty Text string: a VALUE, not NULL (F11)`` () =
    // WP-3 retired the NM-18 universal `""`-as-NULL sentinel: the raw
    // plane now carries NULL out-of-band (`None`), so a genuine empty
    // string profiles as `StringValue ""` on BOTH constructors — the
    // derived evidence finally matches what a live reader observes
    // (`COUNT(*) WHERE col IS NULL` never counted `''`).
    Assert.Equal(StringValue "", CachedValue.ofRaw Text (Some ""))
    Assert.Equal(StringValue "", CachedValue.ofReaderValue (ov ""))

// ---------------------------------------------------------------------------
// Law 2 — cachedKindOfRows mirrors the live discovery's output shape.
// ---------------------------------------------------------------------------

/// Four-typed kind: Id INT PK, Name TEXT (nullable in DB), Flag BOOLEAN,
/// Amount DECIMAL (nullable in DB, absent from the reflection slice).
let private mkAccountKind () : Kind =
    let kindKey  = mkKey ["TestModule"; "Account"]
    let idKey    = mkKey ["TestModule"; "Account"; "Id"]
    let nameKey  = mkKey ["TestModule"; "Account"; "Name"]
    let flagKey  = mkKey ["TestModule"; "Account"; "Flag"]
    let amtKey   = mkKey ["TestModule"; "Account"; "Amount"]
    {
        SsKey    = kindKey
        Name     = mkName "Account"
        Origin   = Native
        Modality = []
        Physical = mkTableId "dbo" "OSUSR_TEST_ACCOUNT"
        Attributes =
            [
                { Attribute.create idKey (mkName "Id") Integer with Column = ColumnRealization.create "ID" false |> Result.value; IsPrimaryKey = true; IsMandatory = true }
                { Attribute.create nameKey (mkName "Name") Text with Column = ColumnRealization.create "NAME" true |> Result.value }
                { Attribute.create flagKey (mkName "Flag") Boolean with Column = ColumnRealization.create "FLAG" false |> Result.value; IsMandatory = true }
                { Attribute.create amtKey (mkName "Amount") Decimal with Column = ColumnRealization.create "AMOUNT" true |> Result.value }
            ]
        References = []
        Indexes    = []
        Description = None
        IsActive = true
        Triggers = []
        ColumnChecks = []
        ExtendedProperties = []
    }

let private mkRow (kind: Kind) (idx: int) (cells: (string * string) list) : StaticRow =
    { Identifier = mkKey ["TestModule"; Name.value kind.Name; "Row"; string idx]
      Values     = cells |> List.map (fun (k, v) -> mkName k, Some v) |> Map.ofList }

/// Option-grain sibling — for rows that author an explicit NULL cell
/// (`None`) distinctly from an absent key (WP-3).
let private mkRowOpt (kind: Kind) (idx: int) (cells: (string * string option) list) : StaticRow =
    { Identifier = mkKey ["TestModule"; Name.value kind.Name; "Row"; string idx]
      Values     = cells |> List.map (fun (k, v) -> mkName k, v) |> Map.ofList }

[<Fact>]
let ``cachedKindOfRows refuses an attribute-less kind with None (the live discovery's refusal shape)`` () =
    let kind = { mkAccountKind () with Attributes = [] }
    Assert.Equal(None, EvidenceCache.cachedKindOfRows Map.empty kind [])

[<Fact>]
let ``cachedKindOfRows: exact RowCount and exact per-attribute NullCounts, missing cell keys counted as NULL`` () =
    let kind = mkAccountKind ()
    let rows =
        [
            mkRow kind 1 [ "Id", "1"; "Name", "alpha"; "Flag", "true";  "Amount", "10.50" ]
            // "Name" carries an EXPLICIT NULL cell (WP-3: `None`, no longer
            // the `""` sentinel).
            mkRowOpt kind 2 [ "Id", Some "2"; "Name", None; "Flag", Some "false"; "Amount", Some "0.25" ]
            // "Amount" key absent entirely — a missing map key reads as
            // no-value (`StaticRow.value` → None), so it counts as NULL.
            mkRow kind 3 [ "Id", "3"; "Name", "gamma"; "Flag", "true" ]
        ]
    let ck =
        EvidenceCache.cachedKindOfRows Map.empty kind rows
        |> Option.defaultWith (fun () -> invalidOp "expected Some CachedKind")
    Assert.Equal(3L, ck.RowCount)
    let nullsOf name =
        let a = kind.Attributes |> List.find (fun a -> a.Name = mkName name)
        Map.find a.SsKey ck.NullCounts
    Assert.Equal(0L, nullsOf "Id")
    Assert.Equal(1L, nullsOf "Name")
    Assert.Equal(0L, nullsOf "Flag")
    Assert.Equal(1L, nullsOf "Amount")

[<Fact>]
let ``cachedKindOfRows: Columns follow catalog attribute order and Values align positionally by row`` () =
    let kind = mkAccountKind ()
    let rows =
        [
            mkRow kind 1 [ "Id", "1"; "Name", "alpha"; "Flag", "true";  "Amount", "10.50" ]
            mkRowOpt kind 2 [ "Id", Some "2"; "Name", Some "beta"; "Flag", Some "false"; "Amount", None ]
        ]
    let ck =
        EvidenceCache.cachedKindOfRows Map.empty kind rows
        |> Option.defaultWith (fun () -> invalidOp "expected Some CachedKind")
    Assert.Equal<SsKey list>(
        kind.Attributes |> List.map (fun a -> a.SsKey),
        ck.Columns |> List.map (fun c -> c.AttributeKey))
    let col name =
        let a = kind.Attributes |> List.find (fun a -> a.Name = mkName name)
        Map.find a.SsKey ck.ColumnsByKey
    // row 0 across columns
    Assert.Equal<CachedValue>(IntValue 1L,                (col "Id").Values.[0])
    Assert.Equal<CachedValue>(StringValue "alpha",        (col "Name").Values.[0])
    Assert.Equal<CachedValue>(IntValue 1L,                (col "Flag").Values.[0])
    Assert.Equal<CachedValue>(DecimalValue 10.50m,        (col "Amount").Values.[0])
    // row 1 across columns
    Assert.Equal<CachedValue>(IntValue 2L,                (col "Id").Values.[1])
    Assert.Equal<CachedValue>(StringValue "beta",         (col "Name").Values.[1])
    Assert.Equal<CachedValue>(IntValue 0L,                (col "Flag").Values.[1])
    Assert.Equal<CachedValue>(NullValue,                  (col "Amount").Values.[1])

[<Fact>]
let ``cachedKindOfRows: nullability keys by physical column name; unreflected columns default to false`` () =
    let kind = mkAccountKind ()
    // reflection slice covers NAME (nullable) and ID (not) — FLAG and
    // AMOUNT are absent, e.g. dropped from the deployed schema
    let nullability = Map.ofList [ "NAME", true; "ID", false ]
    let ck =
        EvidenceCache.cachedKindOfRows nullability kind [ mkRow kind 1 [ "Id", "1" ] ]
        |> Option.defaultWith (fun () -> invalidOp "expected Some CachedKind")
    let col name =
        let a = kind.Attributes |> List.find (fun a -> a.Name = mkName name)
        Map.find a.SsKey ck.ColumnsByKey
    Assert.False((col "Id").IsNullableInDatabase)
    Assert.True ((col "Name").IsNullableInDatabase)
    Assert.False((col "Flag").IsNullableInDatabase)
    Assert.False((col "Amount").IsNullableInDatabase)

[<Fact>]
let ``cachedKindOfRows: ColumnsByKey corresponds exactly to Columns (the discovery-primitive discipline)`` () =
    let kind = mkAccountKind ()
    let rows = [ mkRow kind 1 [ "Id", "1"; "Name", "n"; "Flag", "1"; "Amount", "1" ] ]
    let ck =
        EvidenceCache.cachedKindOfRows Map.empty kind rows
        |> Option.defaultWith (fun () -> invalidOp "expected Some CachedKind")
    Assert.Equal(List.length ck.Columns, Map.count ck.ColumnsByKey)
    for c in ck.Columns do
        Assert.Same(c.Values, (Map.find c.AttributeKey ck.ColumnsByKey).Values)

[<Fact>]
let ``cachedKindOfRows: zero rows yields an exact-zero CachedKind, not a refusal`` () =
    // An empty deployed table is evidence (RowCount 0), not an absence
    // of evidence — matching the live aggregate query's verdict.
    let kind = mkAccountKind ()
    let ck =
        EvidenceCache.cachedKindOfRows Map.empty kind []
        |> Option.defaultWith (fun () -> invalidOp "expected Some CachedKind")
    Assert.Equal(0L, ck.RowCount)
    for c in ck.Columns do
        Assert.Empty c.Values
    for KeyValue(_, n) in ck.NullCounts do
        Assert.Equal(0L, n)

// ---------------------------------------------------------------------------
// Positional-carrier law (row-carrier slimming) — the quanta entry equals
// the named-row entry over IR-materialized rows, and the shared row
// identity mints identically on both paths.
// ---------------------------------------------------------------------------

[<Fact>]
let ``cachedKindOfQuanta ≡ cachedKindOfRows over materialized rows (the positional-carrier law)`` () =
    let kind = mkAccountKind ()
    let basis = Kind.rowBasis kind
    let quanta : RowQuantum list =
        [ { Cells = [| ValueSome "1"; ValueSome "alpha"; ValueSome "true";  ValueSome "10.50" |] }
          { Cells = [| ValueSome "2"; ValueNone;         ValueSome "false"; ValueNone         |] }
          { Cells = [| ValueSome "3"; ValueSome "gamma"; ValueSome "1";     ValueSome "0.25"  |] } ]
    let nullability = Map.ofList [ "NAME", true ]
    let rows =
        quanta
        |> List.mapi (fun i q ->
            StaticRow.ofQuantum basis (StaticRow.readsideIdentity "dbo" "OSUSR_TEST_ACCOUNT" i) q)
    Assert.Equal<CachedKind option>(
        EvidenceCache.cachedKindOfRows nullability kind rows,
        EvidenceCache.cachedKindOfQuanta nullability kind quanta)

[<Fact>]
let ``cachedKindOfQuanta: a short row's missing tail reads as the empty raw (NULL)`` () =
    let kind = mkAccountKind ()
    let quanta : RowQuantum list = [ { Cells = [| ValueSome "1"; ValueSome "alpha" |] } ]
    let ck =
        EvidenceCache.cachedKindOfQuanta Map.empty kind quanta
        |> Option.defaultWith (fun () -> invalidOp "expected Some CachedKind")
    let amount = kind.Attributes |> List.find (fun a -> a.Name = mkName "Amount")
    Assert.Equal<CachedValue>(NullValue, (Map.find amount.SsKey ck.ColumnsByKey).Values.[0])
    Assert.Equal(1L, Map.find amount.SsKey ck.NullCounts)

[<Fact>]
let ``StaticRow.readsideIdentity mints the exact READSIDE_ROW identity the IR boundary synthesizes`` () =
    let expected = SsKey.synthesized "READSIDE_ROW" "dbo.OSUSR_X.7" |> mustOk
    Assert.Equal(expected, StaticRow.readsideIdentity "dbo" "OSUSR_X" 7)
