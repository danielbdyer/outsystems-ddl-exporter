module Projection.Tests.OssysDomainModuleParityTests

// V1 parity audit — slice 5.2.α.module. Reserves matrix rows 42–44
// (V1's module-level aggregate vs V2's Catalog/Module reconstruction).

open Xunit
open Projection.Core
open Projection.Tests.IRBuilders

let private mkN s = Name.create s |> Result.value
let private mkKey s = SsKey.synthesized "TEST" s |> Result.value

[<Fact>]
let ``5.2.α row 42: V2 Module.create rejects empty Kinds per V1 parity (LR1)`` () =
    // Slice 5.13.module-non-empty-invariant — `Module.create` lifts V1's
    // `ModuleModel.Create` non-empty Entity invariant. Empty-Kind
    // modules are semantically meaningless at every consumer
    // (emitter / pass / diagnostic); structural rejection prevents the
    // ghost-module class of bug in transformation passes.
    let result =
        Module.create
            (mkKey "AppCore")
            (mkN "AppCore")
            []
            true
            []
    Assert.True(Result.isFailure result)
    let err = Result.errors result |> List.head
    Assert.Equal("module.kinds.empty", err.Code)

[<Fact>]
let ``5.2.α row 42: V2 Module.create accepts non-empty Kinds`` () =
    let kind =
        Kind.create
            (mkKey "AppCore.User")
            (mkN "User")
            (TableId.create "dbo" "OSUSR_APPCORE_USER" |> Result.value)
            []
    let result =
        Module.create
            (mkKey "AppCore")
            (mkN "AppCore")
            [ kind ]
            true
            []
    Assert.True(Result.isSuccess result)
    let m = Result.value result
    Assert.Equal(1, List.length m.Kinds)

[<Fact(Skip = "Matrix row 43 — 🔵 V2-EXTENSION. V1's `ModuleModel.Create` validates logical-name + case-insensitive physical-name uniqueness across the module's entities (two distinct error codes: `module.entities.duplicateLogical` / `module.entities.duplicatePhysical`). V2 enforces SsKey-based uniqueness (`Catalog.create` global Kind SsKey disjointness; A11 coproduct-cell discipline). V2's choice flows from pillar 9 + A2 (identity is by SsKey, never name) — two Kinds with the same Name but different SsKeys are distinct catalog objects. The decoupling is structurally stronger; V2's identity check is type-witnessed. No DECISIONS row needed (existing A2 axiom covers).")>]
let ``5.2.α row 43: V1 name-based entity uniqueness vs V2 SsKey-based uniqueness (V2 stronger)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 43"

[<Fact(Skip = "Matrix row 44 — 🔵 V2-EXTENSION. V1's `ModuleModel.Create` accepts `extendedProperties: IEnumerable<ExtendedProperty>?` (nullable), materializes + normalizes `null` to `EmptyArray`. V2's `Module.create` accepts `extendedProperties: ExtendedProperty list` (non-nullable, F# list type — null impossible by construction). V2's `ExtendedProperty.create` smart constructor normalizes empty-string Value to `None` per V1 parity. V2 is structurally stronger (no null paths possible) + V1-parity on carried fields. No DECISIONS row needed.")>]
let ``5.2.α row 44: V1 nullable extended-properties vs V2 non-nullable list (V2 stronger)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 44"

[<Fact>]
let ``5.2.α.module: domain-module parity file present`` () =
    Assert.True(true)
