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
                  IsPrimaryKey = true; IsMandatory = true; Length = None; Precision = None; Scale = None; IsIdentity = false; Description = None }
            ]
        References = []
        Indexes    = []
        Description = None
    }

let private mkCatalog (kinds: Kind list) : Catalog =
    let m : Module =
        { SsKey = mkKey ["TestModule"]
          Name  = mkName "TestModule"
          Kinds = kinds }
    { Modules = [ m ] }

// ---------------------------------------------------------------------------
// UserRemapContext — chapter 4.2 slice γ shape.
//
// The slice ζ placeholder (`Map<SsKey, Map<int64, int64>>`) was refined
// at chapter 4.2 slice γ to a typed record (`{ Mapping; Unmatched;
// Diagnostics }`) living in `Projection.Core/UserRemap.fs`. The Bootstrap
// emitter still consumes the type at the same composer integration
// point; the slice ζ MVP behavior (every kind a no-op artifact under
// `UserRemapContext.empty`) is preserved.
//
// Slice-γ smart-constructor invariants are tested in
// `UserRemapContextTests.fs`; this file covers the BootstrapEmitter's
// consumption of the new shape.
// ---------------------------------------------------------------------------

[<Fact>]
let ``UserRemapContext.empty (slice γ shape) has empty Mapping + Unmatched + Diagnostics`` () =
    let ctx = UserRemapContext.empty
    Assert.True (Map.isEmpty ctx.Mapping)
    Assert.True (Set.isEmpty ctx.Unmatched)
    Assert.Empty ctx.Diagnostics

[<Fact>]
let ``UserRemapContext.empty is fully-mapped (no unmatched users) and unmatchedCount = 0`` () =
    Assert.True (UserRemapContext.isFullyMapped UserRemapContext.empty)
    Assert.Equal (0, UserRemapContext.unmatchedCount UserRemapContext.empty)

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
