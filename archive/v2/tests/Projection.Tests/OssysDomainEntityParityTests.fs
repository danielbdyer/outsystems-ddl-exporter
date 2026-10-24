module Projection.Tests.OssysDomainEntityParityTests

// V1 parity audit — slice 5.2.α.entity. Reserves matrix rows 45–47
// (V1's EntityModel vs V2's Kind record).

open Xunit

[<Fact(Skip = "Matrix row 45 — 🔵 V2-EXTENSION. V1's `EntityModel` carries dual identity — `EntityId : int` (local to EspaceId context; SQL Server transaction-scope durability) + `EntitySsKey : Guid?` (optional sourced identity). V2's `Kind` carries `SsKey : SsKey` — a closed 4-variant DU (`OssysOriginal of guid | Synthesized of source × basis | Derived of original × reason | V1Mapped of v1SsKey × v2Namespace`). V2's `SsKey` is type-witnessed identity (compiler refuses to confuse with strings); the `V1Mapped` variant explicitly carries cross-version threading. V1's `EntityId` is discarded (transaction-local; not durable identity); V1's `EntitySsKey` becomes `OssysOriginal` or `V1Mapped` per provenance. No DECISIONS needed — covered by A1 (identity is structural) + A2 (identity-survives-rename through JSON path).")>]
let ``5.2.α row 45: V1 EntityModel int+Guid identity vs V2 SsKey closed DU (V2 stronger)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 45"

[<Fact(Skip = "Matrix row 46 — 🔵 V2-EXTENSION. V1's `EntityModel` carries kind/origin via two binary booleans (`IsSystemEntity: bool`, `IsExternalEntity: bool`) — a 2-bit encoding with implicit 4 states. V2's `Kind` decomposes the axis into (a) closed-DU `Origin = Native | ExternalIndirect | ExternalDirect` (3 explicit states) + (b) `ModalityMark list` with payload-free `SystemOwned` variant (sibling to `TenantScoped`, `SoftDeletable`, `Temporal`, `Static`). V2's encoding makes the orthogonal axes (origin vs. ownership) type-distinct rather than convention-distinct. Pillar 9 (DataIntent dichotomy) classifies these as DataIntent evidence (sourced from V1 rowsets; no operator opinion). No DECISIONS needed.")>]
let ``5.2.α row 46: V1 IsSystem+IsExternal bool flags vs V2 Origin DU + Modality marks (V2 stronger)`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 46"

[<Fact(Skip = "Matrix row 47 — 🟡 DIVERGENCE. V1's `EntityModel` carries per-entity `Catalog : string?` field — the database name where the entity's table resides (`[Catalog].[Schema].[Table]` qualified-name rendering at V1's SMO emitter). V2's `Kind` has no equivalent field. Same axis as matrix row 29 (`OutsystemsMetadataSnapshot.DatabaseName` envelope field): V2 treats database identity as a realization-time concern, not threaded through IR. See `DECISIONS 2026-05-17 (slice 5.1.α) — Database identity is a realization-time concern, not an IR field`. Re-open trigger: a V2 emission consumer demands per-Kind database threading (unlikely; the Catalog stays deployment-agnostic by design).")>]
let ``5.2.α row 47: V1 EntityModel.Catalog database-name field vs V2 deployment-agnostic Catalog`` () : unit =
    failwith "deferred — see V1_PARITY_MATRIX.md row 47 + DECISIONS 2026-05-17 (slice 5.1.α)"

[<Fact>]
let ``5.2.α.entity: domain-entity parity file present`` () =
    Assert.True(true)
