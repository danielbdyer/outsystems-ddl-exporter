module Projection.Tests.BootstrapEmitterTests

open Xunit
open Projection.Core
open Projection.Targets.Data

// ---------------------------------------------------------------------------
// Chapter 4.1.B slice ζ — BootstrapEmitter v0 (UserRemapContext = empty
// pass-through stub).
//
// Per pre-scope §2.3: Bootstrap emits "inserts for system users, default
// policies, and any remaining-by-policy kinds whose data is not in
// StaticSeeds or MigrationDependencies." Until chapter 4.2 ships
// `UserFkReflowPass`, this emitter is a structural stub — empty no-op
// artifact for every kind, T11 keyset preserved.
//
// The slice ζ MVP tests cover the structural hook (signature, T11,
// composer integration) so the chapter-4.2 / 4.3 row-source consumers
// have a fixed insertion point.
// ---------------------------------------------------------------------------

let private mustOk r =
    match r with
    | Ok v -> v
    | Error es ->
        let codes = es |> List.map (fun e -> e.Code) |> String.concat ", "
        invalidOp (sprintf "fixture: %s" codes)

let private mkKey (parts: string list) : SsKey =
    SsKey.synthesizedComposite "OS_TEST" parts |> mustOk

let private mkName (s: string) : Name =
    Name.create s |> mustOk

let private mustOkEmit (r: Result<'a, EmitError>) : 'a =
    match r with
    | Ok v -> v
    | Error e -> Assert.Fail (sprintf "expected Ok, got %A" e); Unchecked.defaultof<_>

let private mkKind (name: string) : Kind =
    let kindKey = mkKey ["TestModule"; name]
    let idKey = mkKey ["TestModule"; name; "Id"]
    {
        SsKey    = kindKey
        Name     = mkName name
        Origin   = OsNative
        Modality = []
        Physical = { Schema = "dbo"; Table = sprintf "OSUSR_TEST_%s" (name.ToUpperInvariant()) }
        Attributes =
            [
                { SsKey = idKey; Name = mkName "Id"; Type = Integer
                  Column = { ColumnName = "ID"; IsNullable = false }
                  IsPrimaryKey = true; IsMandatory = true; Length = None; Precision = None; Scale = None; IsIdentity = false }
            ]
        References = []
        Indexes    = []
    }

let private mkCatalog (kinds: Kind list) : Catalog =
    let m : Module =
        { SsKey = mkKey ["TestModule"]
          Name  = mkName "TestModule"
          Kinds = kinds }
    { Modules = [ m ] }

// ---------------------------------------------------------------------------
// UserRemapContext — slice ζ MVP shape.
// ---------------------------------------------------------------------------

[<Fact>]
let ``UserRemapContext.empty has no per-kind entries`` () =
    Assert.True (Map.isEmpty UserRemapContext.empty)

[<Fact>]
let ``UserRemapContext.tryFindKindRemap returns None for unmapped kind`` () =
    let kindKey = mkKey ["TestModule"; "User"]
    Assert.Equal<Map<int64, int64> option> (None, UserRemapContext.tryFindKindRemap kindKey UserRemapContext.empty)

[<Fact>]
let ``UserRemapContext.tryFindKindRemap returns Some for mapped kind`` () =
    let kindKey = mkKey ["TestModule"; "User"]
    let ctx : UserRemapContext = Map.ofList [ kindKey, Map.ofList [ 1L, 100L ] ]
    let found = UserRemapContext.tryFindKindRemap kindKey ctx
    Assert.NotEqual<Map<int64, int64> option> (None, found)

// ---------------------------------------------------------------------------
// BootstrapEmitter — T11 keyset + slice ζ MVP shape.
// ---------------------------------------------------------------------------

[<Fact>]
let ``BootstrapEmitter.emit produces one DataInsertScript per kind (T11 keyset)`` () =
    let catalog = mkCatalog [ mkKind "Customer"; mkKind "Order" ]
    let artifact = BootstrapEmitter.emit catalog Profile.empty UserRemapContext.empty |> mustOkEmit
    let map = ArtifactByKind.toMap artifact
    Assert.Equal (2, Map.count map)

[<Fact>]
let ``Slice ζ MVP: BootstrapEmitter.emit returns empty no-op for every kind (UserRemap.empty pass-through)`` () =
    let customer = mkKind "Customer"
    let catalog = mkCatalog [ customer ]
    let artifact = BootstrapEmitter.emit catalog Profile.empty UserRemapContext.empty |> mustOkEmit
    let script = ArtifactByKind.toMap artifact |> Map.find customer.SsKey
    Assert.Empty script.Phase1Merges
    Assert.Empty script.Phase2Updates
    Assert.Equal<string> ("", script.Rendered)

[<Fact>]
let ``T1: BootstrapEmitter.emit is byte-deterministic across repeat invocations`` () =
    let catalog = mkCatalog [ mkKind "Customer" ]
    let r1 = BootstrapEmitter.emit catalog Profile.empty UserRemapContext.empty |> mustOkEmit
    let r2 = BootstrapEmitter.emit catalog Profile.empty UserRemapContext.empty |> mustOkEmit
    let s1 = ArtifactByKind.toMap r1
    let s2 = ArtifactByKind.toMap r2
    Assert.Equal<Map<SsKey, DataInsertScript>> (s1, s2)
