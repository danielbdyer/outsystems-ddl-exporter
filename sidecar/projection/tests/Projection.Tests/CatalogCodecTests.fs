module Projection.Tests.CatalogCodecTests

open System.Text.Json.Nodes
open Xunit
open Projection.Core
open Projection.Targets.Json
open FsCheck

// ============================================================================
// CatalogCodec round-trip tests — the persistence-boundary `realize`/`ingest`
// pair. The keystone law is `deserialize (serialize c) = Ok c` (the adjunction
// applied to durability). These tests prove TOTALITY: every IR field and DU
// variant reachable from `Catalog` round-trips structurally. A missed variant
// is a silent-drop bug the codec must not have; the worked examples below
// exercise each one explicitly so a regression pinpoints the offending case.
// ============================================================================

// -- fixture builders --------------------------------------------------------

let private nm (s: string) : Name = Name.create s |> Result.value

/// Distinct `OssysOriginal` key from a small int (deterministic GUID).
let private key (n: int) : SsKey =
    SsKey.ossysOriginal (System.Guid(n, 0s, 0s, 0uy, 0uy, 0uy, 0uy, 0uy, 0uy, 0uy, 0uy))

let private tableId (schema: string) (table: string) : TableId =
    TableId.create schema table |> Result.value

/// A minimal valid single-kind catalog wrapping a caller-supplied kind, so a
/// focused worked example can vary one axis and round-trip the whole aggregate
/// (the codec's only public surface is the `Catalog`-level serialize/deserialize).
let private catalogOf (k: Kind) : Catalog =
    let m =
        { SsKey = key 1000; Name = nm "M"; Kinds = [ k ]; IsActive = true; ExtendedProperties = [] }
    Catalog.create [ m ] [] |> Result.value

/// Build a single-attribute kind given an attribute, for per-variant probes.
let private kindOfAttr (a: Attribute) : Kind =
    Kind.create (key 1) (nm "K") (tableId "dbo" "K") [ a ]

let private baseAttr () : Attribute =
    Attribute.create (key 1) (nm "Col") PrimitiveType.Text

let private roundTrip (c: Catalog) : Catalog =
    match CatalogCodec.serialize c |> CatalogCodec.deserialize with
    | Ok back -> back
    | Error e -> failwithf "deserialize failed: %A" e

let private expectError (label: string) (json: string) : unit =
    match CatalogCodec.deserialize json with
    | Error _ -> ()
    | Ok _ -> Assert.True(false, sprintf "expected Error for %s" label)

/// Non-null view of a DOM node; navigation past a missing node is a test bug.
let private req (n: JsonNode | null) : JsonNode =
    match n with
    | null -> failwith "unexpected null JSON node during navigation"
    | v -> v

/// Pipeable, null-safe DOM navigation — `n |> field "modules" |> item 0`.
let private field (k: string) (n: JsonNode) : JsonNode = req n.[k]
let private item (i: int) (n: JsonNode) : JsonNode = req n.[i]

/// Parse the codec's own valid output into the JSON DOM, apply a structural
/// edit, and re-serialize. All wire-format knowledge stays inside the codec —
/// failure tests express themselves as declarative edits of a valid document
/// ("the same catalog, minus its name"), never as hand-authored JSON that would
/// silently drift from the codec's actual field shapes.
let private editing (edit: JsonNode -> unit) (c: Catalog) : string =
    let node = req (JsonNode.Parse(CatalogCodec.serialize c))
    edit node
    node.ToJsonString()

/// A minimal two-kind catalog with `Src -> Target` FK, used to manufacture a
/// dangling-FK document by deleting the target kind from the serialized form.
let private twoKindCatalog () : Catalog =
    let targetKey = key 70
    let srcKey = key 71
    let fkAttr = key 72
    let target =
        Kind.create targetKey (nm "Target") (tableId "dbo" "Target") [ Attribute.create (key 73) (nm "Id") PrimitiveType.Integer ]
    let src =
        { Kind.create srcKey (nm "Src") (tableId "dbo" "Src") [ Attribute.create fkAttr (nm "TargetId") PrimitiveType.Integer ] with
            References = [ Reference.create (key 74) (nm "TargetFk") fkAttr targetKey ] }
    let m = { SsKey = key 1000; Name = nm "M"; Kinds = [ target; src ]; IsActive = true; ExtendedProperties = [] }
    Catalog.create [ m ] [] |> Result.value

let private assertRoundTrips (label: string) (c: Catalog) : unit =
    let back = roundTrip c
    Assert.True((back = c), sprintf "round-trip not structurally equal for %s" label)
    // Byte-determinism (T1): re-serializing the decoded value is byte-identical.
    Assert.Equal(CatalogCodec.serialize c, CatalogCodec.serialize back)

// -- a comprehensive catalog covering records + structural DUs ----------------

/// Catalog exercising: both modules' fields; Kind with all record fields set
/// off-default; modality marks Static + Temporal (Limited retention) + the
/// three nullary marks; references with OnDelete/OnUpdate/flags; indexes with
/// every WITH-option, a Filtered predicate, included columns, both DataSpace
/// shapes and all DataCompression levels; triggers; column checks; extended
/// properties at module/kind/attribute/index level; sequences with every
/// CacheMode; SsKey across all four variants; TableId with Some catalog.
let private richCatalog () : Catalog =
    let patronKey = key 10
    let visitKey = key 20
    let idAttr = key 11
    let nameAttr = key 12
    let selfFkAttr = key 13
    let visitPatronAttr = key 21

    let patronId =
        { Attribute.create idAttr (nm "Id") PrimitiveType.Integer with
            IsPrimaryKey = true
            IsMandatory = true
            IsIdentity = true
            IsActive = true
            SqlStorage = Some SqlStorageType.Int
            Description = Some "primary key"
            ExtendedProperties = [ { Name = "MS_Description"; Value = Some "the id" } ] }

    let patronName =
        { Attribute.create nameAttr (nm "FullName") PrimitiveType.Text with
            IsMandatory = true
            Length = Some 256
            SqlStorage = Some (SqlStorageType.NVarChar (SqlLength.Bounded 256))
            DefaultValue = Some (SqlLiteral.TextLit "'unknown'")
            DefaultName = Some (nm "DF_Patron_FullName")
            OriginalName = Some "Name"
            ExternalDatabaseType = Some "nvarchar(256)" }

    let patronSelfFk =
        { Attribute.create selfFkAttr (nm "SponsorId") PrimitiveType.Integer with
            Computed = Some { Expression = "[Id] + 1"; IsPersisted = true }
            SqlStorage = Some SqlStorageType.BigInt }

    let temporalConfig : TemporalConfig =
        { HistorySchema = Some "history"
          HistoryTable = Some "PatronHistory"
          PeriodStart = Some (nm "ValidFrom")
          PeriodEnd = Some (nm "ValidTo")
          Retention = TemporalRetention.Limited (7, TemporalRetentionUnit.Years) }

    let staticPop : StaticRow =
        { Identifier = key 99
          Values = StaticRow.presentValues [ nm "Id", "1"; nm "FullName", "seed" ] }

    let selfRef =
        { Reference.create (key 14) (nm "SponsorFk") selfFkAttr patronKey with
            OnDelete = ReferenceAction.SetNull
            OnUpdate = Some ReferenceAction.Cascade
            IsUserFk = false
            ConstraintState = ConstraintState.UntrustedConstraint }

    let richIndex =
        { Index.create (key 15) (nm "IX_Patron_Name") [ { Attribute = nameAttr; Direction = IndexColumnDirection.Descending } ] with
            Uniqueness = Unique
            ExtendedProperties = [ { Name = "MS_Description"; Value = Some "name index" } ]
            Filter = Some "([FullName] IS NOT NULL)"
            IncludedColumns = [ idAttr ]
            IsPlatformAuto = false
            FillFactor = Some 80
            IsPadded = true
            AllowRowLocks = false
            AllowPageLocks = false
            NoRecomputeStatistics = true
            IgnoreDuplicateKey = true
            IsDisabled = true
            DataCompression = Some DataCompressionLevel.Page
            DataSpace = Some (DataSpace.PartitionScheme ("ps_patron", [ "Id" ])) }

    let pkIndex =
        { Index.create (key 16) (nm "PK_Patron") [ { Attribute = idAttr; Direction = IndexColumnDirection.Ascending } ] with
            Uniqueness = PrimaryKey
            DataCompression = Some DataCompressionLevel.Row
            DataSpace = Some (DataSpace.Filegroup "PRIMARY") }

    let patron =
        { Kind.create patronKey (nm "Patron") (TableId.createWithCatalog "appdb" "dbo" "Patron" |> Result.value) [ patronId; patronName; patronSelfFk ] with
            Origin = Origin.ExternalIndirect
            Modality =
                [ ModalityMark.Static [ staticPop ]
                  ModalityMark.TenantScoped
                  ModalityMark.SoftDeletable
                  ModalityMark.SystemOwned
                  ModalityMark.Temporal temporalConfig ]
            References = [ selfRef ]
            Indexes = [ pkIndex; richIndex ]
            Description = Some "patron entity"
            IsActive = true
            Triggers = [ Trigger.create (key 17) (nm "trg_Patron") true "CREATE TRIGGER trg_Patron ON dbo.Patron AFTER INSERT AS SELECT 1" |> Result.value ]
            ColumnChecks = [ ColumnCheck.create (key 18) (Some (nm "CK_Patron")) "[Id] > 0" true |> Result.value ]
            ExtendedProperties = [ { Name = "MS_Description"; Value = Some "patron table" } ] }

    let visitPatron =
        { Attribute.create visitPatronAttr (nm "PatronId") PrimitiveType.Integer with
            IsMandatory = true }

    let visitRef =
        { Reference.create (key 22) (nm "PatronFk") visitPatronAttr patronKey with
            OnDelete = ReferenceAction.Restrict
            OnUpdate = Some ReferenceAction.NoAction
            ConstraintState = ConstraintState.TrustedConstraint }

    let visit =
        { Kind.create visitKey (nm "Visit") (tableId "dbo" "Visit") [ visitPatron ] with
            Origin = Origin.ExternalDirect
            References = [ visitRef ] }

    let module1 =
        { SsKey = key 100
          Name = nm "Core"
          Kinds = [ patron; visit ]
          IsActive = true
          ExtendedProperties = [ { Name = "Owner"; Value = Some "team-a" } ] }

    let seqUnspecified =
        Sequence.create (key 200) (nm "SeqA") "dbo" "bigint" (Some 1m) (Some 1m) (Some 0m) (Some 9999m) false SequenceCacheMode.Unspecified None |> Result.value
    let seqCache =
        Sequence.create (key 201) (nm "SeqB") "dbo" "int" (Some 100m) (Some 5m) None None true SequenceCacheMode.Cache (Some 50) |> Result.value
    let seqNoCache =
        Sequence.create (key 202) (nm "SeqC") "dbo" "int" None None None None false SequenceCacheMode.NoCache None |> Result.value

    Catalog.create [ module1 ] [ seqUnspecified; seqCache; seqNoCache ] |> Result.value

[<Fact>]
let ``deserialize(serialize c) = Ok c for a comprehensive catalog`` () =
    assertRoundTrips "richCatalog" (richCatalog ())

[<Fact>]
let ``PL-6 S30: serializeUtf8 carries the SAME bytes as serialize (the lifecycle store's raw-embed contract)`` () =
    let c = richCatalog ()
    Assert.Equal<byte[]>(
        System.Text.Encoding.UTF8.GetBytes(CatalogCodec.serialize c),
        CatalogCodec.serializeUtf8 c)

[<Fact>]
let ``empty catalog round-trips`` () =
    assertRoundTrips "empty" (Catalog.create [] [] |> Result.value)

[<Fact>]
let ``minimal single-kind catalog round-trips`` () =
    assertRoundTrips "minimal" (catalogOf (kindOfAttr (baseAttr ())))

// -- per-variant enumeration: every leaf DU through the full codec ------------

let private allPrimitiveTypes : PrimitiveType list =
    [ PrimitiveType.Integer; PrimitiveType.Decimal; PrimitiveType.Text; PrimitiveType.Boolean
      PrimitiveType.DateTime; PrimitiveType.Date; PrimitiveType.Time; PrimitiveType.Binary; PrimitiveType.Guid ]

[<Fact>]
let ``every PrimitiveType round-trips`` () =
    for t in allPrimitiveTypes do
        let a = { baseAttr () with Type = t }
        assertRoundTrips (sprintf "PrimitiveType %A" t) (catalogOf (kindOfAttr a))

let private allStorageTypes : SqlStorageType list =
    [ SqlStorageType.BigInt; SqlStorageType.Int; SqlStorageType.SmallInt; SqlStorageType.TinyInt
      SqlStorageType.Bit; SqlStorageType.Decimal (18, 4); SqlStorageType.Numeric (10, 2)
      SqlStorageType.Money; SqlStorageType.SmallMoney; SqlStorageType.Float; SqlStorageType.Real
      SqlStorageType.NVarChar (SqlLength.Bounded 50); SqlStorageType.NVarChar SqlLength.Max
      SqlStorageType.VarChar (SqlLength.Bounded 100); SqlStorageType.VarChar SqlLength.Max
      SqlStorageType.NChar 10; SqlStorageType.Char 5; SqlStorageType.NText; SqlStorageType.Text
      SqlStorageType.DateTime; SqlStorageType.DateTime2 (Some 7); SqlStorageType.DateTime2 None
      SqlStorageType.DateTimeOffset (Some 3); SqlStorageType.DateTimeOffset None
      SqlStorageType.SmallDateTime; SqlStorageType.Date; SqlStorageType.Time (Some 2); SqlStorageType.Time None
      SqlStorageType.VarBinary (SqlLength.Bounded 16); SqlStorageType.VarBinary SqlLength.Max
      SqlStorageType.Binary 8; SqlStorageType.Image; SqlStorageType.UniqueIdentifier; SqlStorageType.Xml ]

[<Fact>]
let ``every SqlStorageType (incl. SqlLength Bounded/Max) round-trips`` () =
    for s in allStorageTypes do
        // NM-14 — the attribute's semantic Type must agree with its concrete
        // SqlStorage (`SqlStorageType.toPrimitiveType s = Type`), now enforced
        // at `Catalog.create`. Derive Type from the storage so the round-trip
        // fixture is a consistent attribute, not a mismatched one.
        let a =
            { baseAttr () with
                Type = SqlStorageType.toPrimitiveType s
                SqlStorage = Some s }
        assertRoundTrips (sprintf "SqlStorageType %A" s) (catalogOf (kindOfAttr a))

let private allLiterals : SqlLiteral list =
    [ SqlLiteral.NullLit
      SqlLiteral.IntegerLit "42"
      SqlLiteral.DecimalLit "3.14"
      SqlLiteral.BooleanLit true
      SqlLiteral.BooleanLit false
      SqlLiteral.TextLit "'hello'"
      SqlLiteral.TemporalLit "'2026-01-01'"
      SqlLiteral.GuidLit "'00000000-0000-0000-0000-000000000000'"
      SqlLiteral.BinaryLit "0xDEADBEEF" ]

[<Fact>]
let ``every SqlLiteral round-trips as a DEFAULT`` () =
    for lit in allLiterals do
        let a = { baseAttr () with DefaultValue = Some lit }
        assertRoundTrips (sprintf "SqlLiteral %A" lit) (catalogOf (kindOfAttr a))

let private allModalityMarks : ModalityMark list =
    [ ModalityMark.Static [ { Identifier = key 5; Values = StaticRow.presentValues [ nm "Col", "v" ] } ]
      ModalityMark.Static []
      ModalityMark.TenantScoped
      ModalityMark.SoftDeletable
      ModalityMark.SystemOwned
      ModalityMark.Temporal
          { HistorySchema = None; HistoryTable = None; PeriodStart = None; PeriodEnd = None
            Retention = TemporalRetention.Infinite }
      ModalityMark.Temporal
          { HistorySchema = Some "h"; HistoryTable = Some "t"; PeriodStart = Some (nm "f"); PeriodEnd = Some (nm "e")
            Retention = TemporalRetention.Limited (30, TemporalRetentionUnit.Days) } ]

[<Fact>]
let ``every ModalityMark (incl. both TemporalRetention shapes) round-trips`` () =
    for m in allModalityMarks do
        let k = { kindOfAttr (baseAttr ()) with Modality = [ m ] }
        assertRoundTrips (sprintf "ModalityMark %A" m) (catalogOf k)

let private allRetentionUnits : TemporalRetentionUnit list =
    [ TemporalRetentionUnit.Days; TemporalRetentionUnit.Weeks; TemporalRetentionUnit.Months; TemporalRetentionUnit.Years ]

[<Fact>]
let ``every TemporalRetentionUnit round-trips`` () =
    for u in allRetentionUnits do
        let m =
            ModalityMark.Temporal
                { HistorySchema = None; HistoryTable = None; PeriodStart = None; PeriodEnd = None
                  Retention = TemporalRetention.Limited (1, u) }
        let k = { kindOfAttr (baseAttr ()) with Modality = [ m ] }
        assertRoundTrips (sprintf "TemporalRetentionUnit %A" u) (catalogOf k)

[<Fact>]
let ``every Origin round-trips`` () =
    for o in [ Origin.Native; Origin.ExternalIndirect; Origin.ExternalDirect ] do
        let k = { kindOfAttr (baseAttr ()) with Origin = o }
        assertRoundTrips (sprintf "Origin %A" o) (catalogOf k)

[<Fact>]
let ``every ReferenceAction round-trips (OnDelete and OnUpdate)`` () =
    let actions = [ ReferenceAction.NoAction; ReferenceAction.Cascade; ReferenceAction.SetNull; ReferenceAction.Restrict ]
    for onDelete in actions do
        for onUpdate in (None :: List.map Some actions) do
            let fkAttr = key 30
            let a = Attribute.create fkAttr (nm "Fk") PrimitiveType.Integer
            let r =
                { Reference.create (key 31) (nm "SelfFk") fkAttr (key 1) with
                    OnDelete = onDelete; OnUpdate = onUpdate }
            let k = { Kind.create (key 1) (nm "K") (tableId "dbo" "K") [ a ] with References = [ r ] }
            assertRoundTrips (sprintf "OnDelete %A OnUpdate %A" onDelete onUpdate) (catalogOf k)

[<Fact>]
let ``every IndexColumnDirection + DataCompressionLevel + DataSpace round-trips`` () =
    let dirs = [ IndexColumnDirection.Ascending; IndexColumnDirection.Descending ]
    let comps = [ None; Some DataCompressionLevel.None; Some DataCompressionLevel.Row; Some DataCompressionLevel.Page ]
    let spaces =
        [ None
          Some (DataSpace.Filegroup "PRIMARY")
          Some (DataSpace.PartitionScheme ("ps", [ "a"; "b" ]))
          Some (DataSpace.PartitionScheme ("ps2", [])) ]
    let attrKey = key 1
    let a = Attribute.create attrKey (nm "Col") PrimitiveType.Text
    for dir in dirs do
        for comp in comps do
            for sp in spaces do
                let idx =
                    { Index.create (key 40) (nm "IX") [ { Attribute = attrKey; Direction = dir } ] with
                        DataCompression = comp; DataSpace = sp }
                let k = { Kind.create (key 1) (nm "K") (tableId "dbo" "K") [ a ] with Indexes = [ idx ] }
                assertRoundTrips (sprintf "dir %A comp %A space %A" dir comp sp) (catalogOf k)

[<Fact>]
let ``every SequenceCacheMode round-trips`` () =
    for mode in [ SequenceCacheMode.Unspecified; SequenceCacheMode.Cache; SequenceCacheMode.NoCache ] do
        let s = Sequence.create (key 50) (nm "S") "dbo" "int" (Some 1m) (Some 1m) None None false mode (Some 10) |> Result.value
        let c = Catalog.create [ { SsKey = key 1000; Name = nm "M"; Kinds = [ kindOfAttr (baseAttr ()) ]; IsActive = true; ExtendedProperties = [] } ] [ s ] |> Result.value
        assertRoundTrips (sprintf "SequenceCacheMode %A" mode) c

[<Fact>]
let ``all four SsKey variants round-trip (identity, designation, realization)`` () =
    let parent = SsKey.ossysOriginal (System.Guid("11111111-1111-1111-1111-111111111111"))
    let variants : SsKey list =
        [ SsKey.ossysOriginal (System.Guid("22222222-2222-2222-2222-222222222222"))
          SsKey.synthesized "OS_KIND" "AppCore_User" |> Result.value
          SsKey.derivedFrom parent DerivationReason.Inverse
          SsKey.fromV1 (System.Guid("33333333-3333-3333-3333-333333333333")) (System.Guid("44444444-4444-4444-4444-444444444444")) ]
    for sk in variants do
        let a = Attribute.create sk (nm "Col") PrimitiveType.Text
        let k = Kind.create sk (nm "K") (tableId "dbo" "K") [ a ]
        assertRoundTrips (sprintf "SsKey %A" sk) (catalogOf k)

[<Fact>]
let ``TableId with and without an explicit catalog round-trips`` () =
    let withCat = { kindOfAttr (baseAttr ()) with Physical = (TableId.createWithCatalog "appdb" "dbo" "K" |> Result.value) }
    let noCat = { kindOfAttr (baseAttr ()) with Physical = (TableId.create "dbo" "K" |> Result.value) }
    assertRoundTrips "TableId Some catalog" (catalogOf withCat)
    assertRoundTrips "TableId None catalog" (catalogOf noCat)

[<Fact>]
let ``ColumnCheck with and without a name round-trips`` () =
    let named = ColumnCheck.create (key 60) (Some (nm "CK_X")) "[Col] > 0" false |> Result.value
    let unnamed = ColumnCheck.create (key 61) None "[Col] < 100" true |> Result.value
    let k = { kindOfAttr (baseAttr ()) with ColumnChecks = [ named; unnamed ] }
    assertRoundTrips "ColumnCheck named+unnamed" (catalogOf k)

// -- determinism (T1) --------------------------------------------------------

[<Fact>]
let ``serialize is deterministic across repeated calls`` () =
    let c = richCatalog ()
    let a = CatalogCodec.serialize c
    let b = CatalogCodec.serialize c
    Assert.Equal(a, b)

[<Fact>]
let ``serialize emits the codec version`` () =
    let json = CatalogCodec.serialize (Catalog.create [] [] |> Result.value)
    Assert.Contains("\"codecVersion\"", json)
    Assert.Contains(sprintf "\"codecVersion\": %d" CatalogCodec.version, json)

// -- failure paths: malformed input is a structured Error, never an exception -

[<Fact>]
let ``malformed JSON deserializes to Error (no exception)`` () =
    // Genuinely-not-JSON input — no codec wire knowledge to express declaratively.
    expectError "malformed JSON" "{ this is not json"

[<Fact>]
let ``non-object JSON root deserializes to Error`` () =
    expectError "array root" ((JsonArray(JsonValue.Create 1, JsonValue.Create 2)).ToJsonString())

[<Fact>]
let ``unknown DU tag deserializes to Error`` () =
    // Take a valid catalog carrying a modality mark and retag it to a kind the
    // codec does not recognize — a declarative edit, not a hand-authored doc.
    let k = { kindOfAttr (baseAttr ()) with Modality = [ ModalityMark.SoftDeletable ] }
    catalogOf k
    |> editing (fun n ->
        let modality0 = n |> field "modules" |> item 0 |> field "kinds" |> item 0 |> field "modality" |> item 0
        modality0.["kind"] <- JsonValue.Create("Bogus"))
    |> expectError "unknown ModalityMark tag"

[<Fact>]
let ``missing required field deserializes to Error`` () =
    // A valid catalog whose first module has lost its required "name".
    catalogOf (kindOfAttr (baseAttr ()))
    |> editing (fun n -> (n |> field "modules" |> item 0).AsObject().Remove("name") |> ignore)
    |> expectError "missing required field 'name'"

[<Fact>]
let ``decode re-proves the A39 aggregate invariant (dangling FK target)`` () =
    // Delete the FK's target kind from an otherwise-valid Src->Target document;
    // the decode funnels through Catalog.create, which must reject the now-
    // dangling reference (A39 re-validation at the persistence boundary).
    twoKindCatalog ()
    |> editing (fun n -> (n |> field "modules" |> item 0 |> field "kinds").AsArray().RemoveAt(0))
    |> expectError "dangling FK target — A39 re-validation on decode"

[<Fact>]
let ``NM-12 / M4: the illegal constraint-state quadrant is unrepresentable — the legacy (false,false) pair normalizes to NoDbConstraint and the catalog accepts it`` () =
    // Pre-M4 this quadrant (constraint-absent ∧ untrusted) was rejected at runtime
    // by Catalog.create. Since M4 it is UNREPRESENTABLE — `ConstraintState` is a
    // closed 3-variant DU with no illegal member. The only ingest path for the
    // legacy boolean pair is `withConstraintState` / `ofLegacyBooleans`, which
    // normalizes `(false, false)` to `NoDbConstraint` (vacuous trust). The catalog
    // therefore accepts the normalized reference — there is no illegal state left
    // to reject (the runtime check became dead code and was retired).
    let attr = Attribute.create (key 1) (nm "Col") PrimitiveType.Text
    let normalizedRef =
        Reference.create (key 2) (nm "FK_self") attr.SsKey (key 3)
        |> Reference.withConstraintState false false
    Assert.Equal(ConstraintState.NoDbConstraint, normalizedRef.ConstraintState)
    let kind = { Kind.create (key 3) (nm "K") (tableId "dbo" "K") [ attr ] with References = [ normalizedRef ] }
    let m = { SsKey = key 1000; Name = nm "M"; Kinds = [ kind ]; IsActive = true; ExtendedProperties = [] }
    match Catalog.create [ m ] [] with
    | Ok _ -> ()
    | Error es -> Assert.True(false, sprintf "Catalog.create must accept the normalized reference; got %A" es)

[<Fact>]
let ``NM-14: Catalog.create rejects a (Type, SqlStorage) mismatch`` () =
    // An attribute typed Text but carrying concrete BigInt storage evidence, set via
    // a raw record-`with`. The two disagree (`toPrimitiveType BigInt = Integer ≠ Text`):
    // the emitter would render BIGINT while every type-driven decision treated the
    // column as text. The aggregate root must refuse it (NM-14), so the disagreeing
    // pair cannot enter the IR by any path.
    let attr =
        { Attribute.create (key 1) (nm "Col") PrimitiveType.Text with
            SqlStorage = Some SqlStorageType.BigInt }
    let kind = { Kind.create (key 3) (nm "K") (tableId "dbo" "K") [ attr ] with References = [] }
    let m = { SsKey = key 1000; Name = nm "M"; Kinds = [ kind ]; IsActive = true; ExtendedProperties = [] }
    match Catalog.create [ m ] [] with
    | Ok _ -> Assert.True(false, "expected Catalog.create to reject the (Type, SqlStorage) mismatch")
    | Error es -> Assert.Contains(es, fun (e: ValidationError) -> e.Code = "catalog.attribute.storageTypeMismatch")

[<Fact>]
let ``NM-14: Catalog.create accepts an agreeing (Type, SqlStorage) pair`` () =
    // The consistent companion of the reject above: Integer + BigInt agree
    // (`toPrimitiveType BigInt = Integer`), so construction succeeds.
    let attr =
        { Attribute.create (key 1) (nm "Col") PrimitiveType.Integer with
            SqlStorage = Some SqlStorageType.BigInt }
    let kind = { Kind.create (key 3) (nm "K") (tableId "dbo" "K") [ attr ] with References = [] }
    let m = { SsKey = key 1000; Name = nm "M"; Kinds = [ kind ]; IsActive = true; ExtendedProperties = [] }
    match Catalog.create [ m ] [] with
    | Ok _ -> ()
    | Error es -> Assert.True(false, sprintf "expected Catalog.create to accept the agreeing pair; got %A" es)

// ============================================================================
// The round-trip LAW, universally quantified (FsCheck). The enumerated tests
// above prove totality per variant; this proves the law over random *combina-
// tions and nesting depth*. The generator builds referentially-valid catalogs
// by CONSTRUCTION (FK targets and index columns are drawn from already-chosen
// keys — "validity is constructed, not validated", the codebase's posture), and
// reuses the per-variant alphabet lists above as its `Gen.elements` sources, so
// there is no second enumeration of the variant space to drift out of sync.
// ============================================================================

let rec private genAll (gs: Gen<'a> list) : Gen<'a list> =
    match gs with
    | [] -> Gen.constant []
    | g :: rest -> gen { let! x = g in let! xs = genAll rest in return x :: xs }

let private genBool : Gen<bool> = Gen.elements [ true; false ]

let private genOpt (g: Gen<'a>) : Gen<'a option> =
    Gen.frequency [ 1, Gen.constant None; 2, Gen.map Some g ]

let private kKind (i: int) : SsKey = key (1000 + i)
let private kAttr (kindIx: int) (attrIx: int) : SsKey = key (10000 + kindIx * 100 + attrIx)

let private genAttr (sk: SsKey) (name: Name) : Gen<Attribute> =
    gen {
        let! ptypeChosen = Gen.elements allPrimitiveTypes
        let! isPk = genBool
        let! isMand = genBool
        let! isIdentity = genBool
        let! isActive = genBool
        let! len = genOpt (Gen.choose (1, 4000))
        let! storage = genOpt (Gen.elements allStorageTypes)
        // NM-14 — `(Type, SqlStorage)` agreement is enforced at
        // `Catalog.create`. Validity is CONSTRUCTED here, not validated:
        // when the generator draws concrete storage, derive the semantic
        // Type from it (`SqlStorageType.toPrimitiveType`) so the pair always
        // agrees; otherwise keep the freely-chosen Type.
        let ptype =
            match storage with
            | Some s -> SqlStorageType.toPrimitiveType s
            | None -> ptypeChosen
        let! def = genOpt (Gen.elements allLiterals)
        let! descr = genOpt (Gen.constant "doc")
        return
            { Attribute.create sk name ptype with
                IsPrimaryKey = isPk
                IsMandatory = isMand
                IsIdentity = isIdentity
                IsActive = isActive
                Length = len
                SqlStorage = storage
                DefaultValue = def
                Description = descr }
    }

let private genKind (kindIx: int) (allKindKeys: SsKey list) : Gen<Kind> =
    gen {
        let! nAttrs = Gen.choose (1, 4)
        let attrSpecs = [ for j in 0 .. nAttrs - 1 -> kAttr kindIx j, nm (sprintf "A%d_%d" kindIx j) ]
        let attrKeys = attrSpecs |> List.map fst
        let! attrs = attrSpecs |> List.map (fun (sk, n) -> genAttr sk n) |> genAll

        let! nRefs = Gen.choose (0, 2)
        let! refs =
            [ for r in 0 .. nRefs - 1 ->
                gen {
                    let! srcA = Gen.elements attrKeys
                    let! tgt = Gen.elements allKindKeys
                    let! onDel = Gen.elements [ ReferenceAction.NoAction; ReferenceAction.Cascade; ReferenceAction.SetNull; ReferenceAction.Restrict ]
                    let! onUpd = genOpt (Gen.elements [ ReferenceAction.NoAction; ReferenceAction.Cascade; ReferenceAction.SetNull; ReferenceAction.Restrict ])
                    let! userFk = genBool
                    let! hasDb = genBool
                    let! trusted = genBool
                    return
                        // NM-12 — route the constraint-state pair through the
                        // sanctioned normalizer so the generator never produces
                        // the illegal quadrant that `Catalog.create` now rejects.
                        { Reference.create (key (20000 + kindIx * 100 + r)) (nm (sprintf "R%d_%d" kindIx r)) srcA tgt with
                            OnDelete = onDel; OnUpdate = onUpd; IsUserFk = userFk }
                        |> Reference.withConstraintState hasDb trusted } ]
            |> genAll

        let! nIdx = Gen.choose (0, 2)
        let! idxs =
            [ for ix in 0 .. nIdx - 1 ->
                gen {
                    let! colKey = Gen.elements attrKeys
                    let! dir = Gen.elements [ IndexColumnDirection.Ascending; IndexColumnDirection.Descending ]
                    let! uniqueness = Gen.elements [ IndexUniqueness.NotUnique; IndexUniqueness.Unique; IndexUniqueness.PrimaryKey ]
                    let! comp = genOpt (Gen.elements [ DataCompressionLevel.None; DataCompressionLevel.Row; DataCompressionLevel.Page ])
                    let! space = genOpt (Gen.elements [ DataSpace.Filegroup "PRIMARY"; DataSpace.PartitionScheme ("ps", [ "a" ]) ])
                    let! filt = genOpt (Gen.constant "([x] IS NOT NULL)")
                    return
                        { Index.create (key (30000 + kindIx * 100 + ix)) (nm (sprintf "IX%d_%d" kindIx ix)) [ { Attribute = colKey; Direction = dir } ] with
                            Uniqueness = uniqueness; DataCompression = comp; DataSpace = space; Filter = filt } } ]
            |> genAll

        let! nMarks = Gen.choose (0, 3)
        let! modality = genAll (List.replicate nMarks (Gen.elements allModalityMarks))
        let! origin = Gen.elements [ Origin.Native; Origin.ExternalIndirect; Origin.ExternalDirect ]
        let! isActive = genBool
        let! descr = genOpt (Gen.constant "kind doc")
        return
            { Kind.create (kKind kindIx) (nm (sprintf "K%d" kindIx)) (tableId "dbo" (sprintf "T%d" kindIx)) attrs with
                Origin = origin; Modality = modality; References = refs; Indexes = idxs; IsActive = isActive; Description = descr }
    }

let private catalogGen : Gen<Catalog> =
    gen {
        let! nKinds = Gen.choose (1, 4)
        let allKeys = [ for i in 0 .. nKinds - 1 -> kKind i ]
        let! kinds = [ for i in 0 .. nKinds - 1 -> genKind i allKeys ] |> genAll
        let m = { SsKey = key 90000; Name = nm "Mod"; Kinds = kinds; IsActive = true; ExtendedProperties = [] }
        let! nSeq = Gen.choose (0, 3)
        let! seqs =
            [ for s in 0 .. nSeq - 1 ->
                gen {
                    let! mode = Gen.elements [ SequenceCacheMode.Unspecified; SequenceCacheMode.Cache; SequenceCacheMode.NoCache ]
                    let! cs = genOpt (Gen.choose (1, 100))
                    return Sequence.create (key (80000 + s)) (nm (sprintf "S%d" s)) "dbo" "int" (Some 1m) (Some 1m) None None false mode cs |> Result.value } ]
            |> genAll
        return Catalog.create [ m ] seqs |> Result.value
    }

[<Fact>]
let ``round-trip law holds for arbitrary valid catalogs`` () =
    Prop.forAll (Arb.fromGen catalogGen) (fun c ->
        match CatalogCodec.serialize c |> CatalogCodec.deserialize with
        | Ok back -> back = c
        | Error _ -> false)
    |> Check.QuickThrowOnFailure

[<Fact>]
let ``serialize is byte-deterministic for arbitrary valid catalogs`` () =
    Prop.forAll (Arb.fromGen catalogGen) (fun c ->
        let once = CatalogCodec.serialize c
        let twice = CatalogCodec.serialize c
        let viaDecode = CatalogCodec.serialize (CatalogCodec.deserialize once |> Result.value)
        once = twice && once = viaDecode)
    |> Check.QuickThrowOnFailure
