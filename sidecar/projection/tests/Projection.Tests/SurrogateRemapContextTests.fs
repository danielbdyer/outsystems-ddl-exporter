module Projection.Tests.SurrogateRemapContextTests

open Xunit
open Projection.Core

// ---------------------------------------------------------------------------
// Transfer prescope (`PRESCOPE_REVERSE_IMPORT.md`) — the bidirectional
// control-plane vocabulary reified in Core: SubstrateRole, the orientation-
// typed surrogate keys (SourceKey / AssignedKey), IdentityDisposition
// (per-kind, derived from the PK's IDENTITY property), and
// SurrogateRemapContext (the per-kind generalization of UserRemapContext).
//
// Smart-constructor invariant: within a kind, a SourceKey maps to at most
// one AssignedKey — `capture` rejects a second capture of the same source
// surrogate (a phase-1 double-insert).
// ---------------------------------------------------------------------------

let private mustOk r =
    match r with
    | Ok v -> v
    | Error es ->
        let codes = es |> List.map (fun e -> e.Code) |> String.concat ", "
        invalidOp (sprintf "fixture: %s" codes)

let private mkSsKey (parts: string list) : SsKey =
    SsKey.synthesizedComposite "OS_TEST" parts |> mustOk

let private mkName (s: string) : Name =
    Name.create s |> mustOk

let private physical (table: string) : PhysicalRealization =
    { Schema = "dbo"; Table = table; Catalog = None }

let private kindWithPk (table: string) (isPkIdentity: bool) : Kind =
    let pk =
        { Attribute.create (mkSsKey [ table; "Id" ]) (mkName "Id") Integer with
            IsPrimaryKey = true
            IsIdentity   = isPkIdentity }
    Kind.create (mkSsKey [ table ]) (mkName table) (physical table) [ pk ]

// ---------------------------------------------------------------------------
// SubstrateRole — flow-relative role DU.
// ---------------------------------------------------------------------------

[<Fact>]
let ``SubstrateRole has Source and Sink variants`` () =
    let label (r: SubstrateRole) : string =
        match r with
        | SubstrateRole.Source -> "Source"
        | SubstrateRole.Sink   -> "Sink"
    Assert.Equal<string> ("Source", label SubstrateRole.Source)
    Assert.Equal<string> ("Sink",   label SubstrateRole.Sink)

// ---------------------------------------------------------------------------
// Orientation-typed surrogate keys round-trip.
// ---------------------------------------------------------------------------

[<Fact>]
let ``SourceKey and AssignedKey round-trip their raw value`` () =
    Assert.Equal<string> ("42", SourceKey.ofString "42" |> SourceKey.value)
    Assert.Equal<string> ("99", AssignedKey.ofString "99" |> AssignedKey.value)

// ---------------------------------------------------------------------------
// IdentityDisposition.ofKind — derived from the PK's IDENTITY property.
// ---------------------------------------------------------------------------

[<Fact>]
let ``IdentityDisposition.ofKind is AssignedBySink when the PK is an identity column`` () =
    let k = kindWithPk "OSUSR_AUTONUMBER" true
    Assert.Equal (IdentityDisposition.AssignedBySink, IdentityDisposition.ofKind k)

[<Fact>]
let ``IdentityDisposition.ofKind is PreservedFromSource when the PK is not an identity column`` () =
    let k = kindWithPk "OSUSR_BUSINESSKEY" false
    Assert.Equal (IdentityDisposition.PreservedFromSource, IdentityDisposition.ofKind k)

[<Fact>]
let ``IdentityDisposition.ofKind is PreservedFromSource when an identity column is not the PK`` () =
    // A non-PK identity column does not make the kind sink-assigned —
    // only the primary key's disposition governs the remap.
    let nonPkIdentity =
        { Attribute.create (mkSsKey [ "K"; "Seq" ]) (mkName "Seq") Integer with
            IsPrimaryKey = false
            IsIdentity   = true }
    let pk =
        { Attribute.create (mkSsKey [ "K"; "Code" ]) (mkName "Code") Text with
            IsPrimaryKey = true
            IsIdentity   = false }
    let k = Kind.create (mkSsKey [ "K" ]) (mkName "K") (physical "OSUSR_K") [ pk; nonPkIdentity ]
    Assert.Equal (IdentityDisposition.PreservedFromSource, IdentityDisposition.ofKind k)

[<Fact>]
let ``IdentityDisposition.ofKind is PreservedFromSource for a kind with no primary key`` () =
    let attr = Attribute.create (mkSsKey [ "K"; "A" ]) (mkName "A") Integer
    let k = Kind.create (mkSsKey [ "K" ]) (mkName "K") (physical "OSUSR_K") [ attr ]
    Assert.Equal (IdentityDisposition.PreservedFromSource, IdentityDisposition.ofKind k)

// ---------------------------------------------------------------------------
// SurrogateRemapContext.empty + accessors.
// ---------------------------------------------------------------------------

[<Fact>]
let ``SurrogateRemapContext.empty has no assignments`` () =
    let ctx = SurrogateRemapContext.empty
    Assert.True (Map.isEmpty ctx.Assignments)
    Assert.Equal (0, SurrogateRemapContext.assignmentCount ctx)
    Assert.Equal (0, SurrogateRemapContext.kindCount ctx)

[<Fact>]
let ``SurrogateRemapContext.capture then tryFindAssigned resolves the assigned surrogate`` () =
    let kind = mkSsKey [ "Order" ]
    let ctx =
        SurrogateRemapContext.empty
        |> SurrogateRemapContext.capture kind (SourceKey.ofString "7") (AssignedKey.ofString "1001")
        |> mustOk
    Assert.Equal<AssignedKey option>
        (Some (AssignedKey.ofString "1001"),
         SurrogateRemapContext.tryFindAssigned kind (SourceKey.ofString "7") ctx)

[<Fact>]
let ``SurrogateRemapContext.tryFindAssigned is None for an uncaptured source surrogate`` () =
    let kind = mkSsKey [ "Order" ]
    Assert.Equal<AssignedKey option>
        (None,
         SurrogateRemapContext.tryFindAssigned kind (SourceKey.ofString "7") SurrogateRemapContext.empty)

[<Fact>]
let ``SurrogateRemapContext.capture rejects a second capture of the same source surrogate for a kind`` () =
    let kind = mkSsKey [ "Order" ]
    let ctx =
        SurrogateRemapContext.empty
        |> SurrogateRemapContext.capture kind (SourceKey.ofString "7") (AssignedKey.ofString "1001")
        |> mustOk
    match SurrogateRemapContext.capture kind (SourceKey.ofString "7") (AssignedKey.ofString "2002") ctx with
    | Error es ->
        Assert.NotEmpty es
        Assert.Equal<string> ("surrogateRemap.duplicateSource", (List.head es).Code)
    | Ok _ -> Assert.Fail "expected Error for duplicate source surrogate"

[<Fact>]
let ``SurrogateRemapContext.capture isolates the same source surrogate across distinct kinds`` () =
    // Two kinds can each carry source surrogate "7" independently — the
    // mapping is per-kind, so this is not a double-insert.
    let orderKind = mkSsKey [ "Order" ]
    let lineKind = mkSsKey [ "OrderLine" ]
    let ctx =
        SurrogateRemapContext.empty
        |> SurrogateRemapContext.capture orderKind (SourceKey.ofString "7") (AssignedKey.ofString "1001")
        |> Result.bind (SurrogateRemapContext.capture lineKind (SourceKey.ofString "7") (AssignedKey.ofString "5005"))
        |> mustOk
    Assert.Equal (2, SurrogateRemapContext.assignmentCount ctx)
    Assert.Equal (2, SurrogateRemapContext.kindCount ctx)
    Assert.Equal<AssignedKey option>
        (Some (AssignedKey.ofString "1001"),
         SurrogateRemapContext.tryFindAssigned orderKind (SourceKey.ofString "7") ctx)
    Assert.Equal<AssignedKey option>
        (Some (AssignedKey.ofString "5005"),
         SurrogateRemapContext.tryFindAssigned lineKind (SourceKey.ofString "7") ctx)
