module Twin.Tests.VolumeShellTests

open Xunit
open Projection.Core
open Projection.Tests.Fixtures
open Projection.Tests.IRBuilders
open Twin.Core
open Twin.Runtime

// ---------------------------------------------------------------------------
// The volume shell (F4): a capped mint amplifies back toward the recorded
// volumes by deterministic doubling; legality is planned per kind and named;
// the emission mutates exactly what uniqueness requires and nothing else.
// ---------------------------------------------------------------------------

let private name (s: string) : Name = Name.create s |> Result.value

let private attrOf (key: SsKey) (logical: string) (column: string) (ptype: PrimitiveType) (isPk: bool) (length: int option) (identity: bool) : Attribute =
    { Attribute.create key (name logical) ptype with
        Column       = ColumnRealization.create column false |> Result.value
        IsPrimaryKey = isPk
        Length       = length
        IsIdentity   = identity }

let private identityKind : Kind =
    { Kind.create (kindKey ["VS"]) (name "Customer") (mkTableId "dbo" "Customer")
        [ attrOf (attrKey ["VS"; "Id"]) "Id" "Id" Integer true None true
          attrOf (attrKey ["VS"; "Name"]) "Name" "Name" Text false (Some 100) false
          attrOf (attrKey ["VS"; "Score"]) "Score" "Score" Integer false None false ] with
        Modality = [] }

/// Region-like: a plain (non-identity) integer key, offset per round.
let private plainKeyKind : Kind =
    { Kind.create (kindKey ["VR"]) (name "Region") (mkTableId "dbo" "Region")
        [ attrOf (attrKey ["VR"; "Id"]) "Id" "Id" Integer true None false
          attrOf (attrKey ["VR"; "Name"]) "Name" "Name" Text false (Some 50) false ] with
        Modality = [] }

let private withUnique (uniqueColumn: SsKey) (kind: Kind) : Kind =
    { kind with
        Indexes =
            [ { Index.create (attrKey ["VS"; "UX"]) (name "UX_Email") (IndexColumn.ascendingList [ uniqueColumn ]) with
                  Uniqueness = IndexUniqueness.Unique } ] }

let private uniqueWideKind : Kind =
    { Kind.create (kindKey ["VW"]) (name "Person") (mkTableId "dbo" "Person")
        [ attrOf (attrKey ["VW"; "Id"]) "Id" "Id" Integer true None true
          attrOf (attrKey ["VW"; "Email"]) "Email" "Email" Text false (Some 100) false ] with
        Modality = [] }
    |> withUnique (attrKey ["VW"; "Email"])

let private uniqueNarrowKind : Kind =
    { Kind.create (kindKey ["VN"]) (name "Tag") (mkTableId "dbo" "Tag")
        [ attrOf (attrKey ["VN"; "Id"]) "Id" "Id" Integer true None true
          attrOf (attrKey ["VN"; "Code"]) "Code" "Code" Text false (Some 10) false ] with
        Modality = [] }
    |> withUnique (attrKey ["VN"; "Code"])

[<Fact>]
let ``legality is planned per kind and named`` () =
    Assert.Equal(VolumeShell.Amplifiable, VolumeShell.decide identityKind)
    Assert.Equal(VolumeShell.Amplifiable, VolumeShell.decide plainKeyKind)
    Assert.Equal(VolumeShell.Amplifiable, VolumeShell.decide uniqueWideKind)
    // A unique index whose only member cannot carry the key-stamped
    // suffix makes the whole kind unamplifiable, by name.
    Assert.Equal(VolumeShell.Skip "uniqueUnsupported", VolumeShell.decide uniqueNarrowKind)
    // No single-column key, no shell.
    let compositeKey =
        { Kind.create (kindKey ["VC"]) (name "Link") (mkTableId "dbo" "Link")
            [ attrOf (attrKey ["VC"; "A"]) "A" "A" Integer true None false
              attrOf (attrKey ["VC"; "B"]) "B" "B" Integer true None false ] with
            Modality = [] }
    Assert.Equal(VolumeShell.Skip "keyUnsupported", VolumeShell.decide compositeKey)

[<Fact>]
let ``the round omits identity keys, copies verbatim, and is key-ordered`` () =
    let sql = VolumeShell.emitRound identityKind "Id" 0L 1L 5L
    Assert.Contains("INSERT INTO [dbo].[Customer] ([Name], [Score])", sql)
    Assert.Contains("SELECT TOP (5) src.[Name], src.[Score]", sql)
    Assert.Contains("ORDER BY src.[Id];", sql)
    Assert.DoesNotContain("[Id],", sql)

[<Fact>]
let ``a plain integer key is offset past everything the induction produced`` () =
    let sql = VolumeShell.emitRound plainKeyKind "Id" 40L 2L 3L
    Assert.Contains("INSERT INTO [dbo].[Region] ([Id], [Name])", sql)
    Assert.Contains("src.[Id] + 40", sql)
    Assert.Contains("TOP (3)", sql)

[<Fact>]
let ``a unique text member is key-stamped inside its declared width, NULLs preserved`` () =
    let sql = VolumeShell.emitRound uniqueWideKind "Id" 0L 2L 4L
    // 100 − 24 reserved characters for '~' + key + '~' + round.
    Assert.Contains("CASE WHEN src.[Email] IS NULL THEN NULL ELSE CONCAT(LEFT(src.[Email], 76), N'~', src.[Id], N'~', 2) END", sql)
