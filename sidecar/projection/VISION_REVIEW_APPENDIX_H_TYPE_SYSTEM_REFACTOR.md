# Appendix H — Type-System Refactor for T11 and A1

**Date:** 2026-05-08
**Reviewing:** VISION.md @ commit `2fb51ef`; VISION_REVIEW.md
**Brief:** Audit current F# emitter signatures and identity-construction patterns; propose a concrete type refactor that turns T11 (sibling-Π commutativity) and A1 (identity-survives-rename) into structural properties enforced by the type system, not code-style discipline.
**Synthesis location:** `VISION_REVIEW.md`, `VISION_REVISION_2.md`

---

# Emitter & Identity Refactor Audit

## Q1 — Current emitter signatures

All three are `Catalog -> string` (or `Catalog -> Profile -> string`), no `Result`, no `Map`:

- `RawTextEmitter.emit : Catalog -> string` — `RawTextEmitter.fs:183`. Iterates `catalog.Modules` into a single `StringBuilder`.
- `JsonEmitter.emit : Catalog -> string` — `JsonEmitter.fs:140`. Single `Utf8JsonWriter` document.
- `DistributionsEmitter.emit : Catalog -> Profile -> string` — `DistributionsEmitter.fs:201`. Wide signature; a curried `emitFromInput : ProjectionInput -> string` at `:227`.

**T11 enforcement today is discipline.** The actual property tests are substring searches: `JsonEmitterTests.fs:96-105` does `for k in Catalog.allKinds enriched do Assert.Contains(root, ssdt); Assert.Contains(root, json)`. `RichProfilingEndToEndTests.fs:280` does the same across three Π's. There is nothing in the type system that would prevent an emitter from dropping a kind; only the test's `Assert.Contains` catches it, and only if the test's enriched fixture contains every shape in production.

The `DistributionsEmitter` doc comment at `DistributionsEmitter.fs:26-32` claims T11 commutativity as a property; the implementation enforces it by `for m in catalog.Modules` discipline.

## Q2 — Current SsKey construction

`Identity.fs:16-18`:
```fsharp
type SsKey =
    | Original of string
    | Derived of original: SsKey * reason: string
```

Smart constructors at `Identity.fs:32` (`SsKey.original`) and `:38` (`SsKey.derived`) return `Result<SsKey>`, rejecting blank input. **No type-level distinction exists between an *OutSystems-rowset SSKey GUID* and a *JSON-path-synthesized SsKey*.** Both are `Original of string`. The bound documented in `AXIOMS.md:47-72` (the JSON adapter strips SSKey columns and synthesizes from name fields) is a runtime fact about the adapter's call site, invisible to the compiler. A property test on a renamed kind that synthesized its key from `Name` cannot be distinguished from one that received an original GUID — both look like `Original "..."`.

## Q3 — Current rename handling

`NamingMorphism.fs` is the only rename code path. It threads a `Name -> Name` morphism across the catalog (`:30`) and emits `Renamed` lineage events (`:32-36`); critically, `SsKey` fields are not touched (the doc comment at `:7-10` claims this; the implementation at `:46-78` only assigns to `Name`, never `SsKey`). The single rename test, `CatalogTests.fs:104` "A4: Catalog.tryFindKind survives a rename", is a one-shot example — no FsCheck property over arbitrary morphisms.

**There is no `RenameContext`, `RefactorLog`, `CatalogDiff`, or `MapEmitter` type in the codebase** (verified by grep over `src/`). VISION.md:58 and VISION_REVIEW.md:51 describe `RefactorLogEmitter` + UUIDv5 as forthcoming; nothing has shipped. A1's bound through `SnapshotJson` (`CatalogReader.fs:75`) is precisely that synthesized-from-name keys *don't* survive renames; the bound resolves only when `SnapshotRowsets` lands.

## Q4 — T11 as a structural type

```fsharp
namespace Projection.Core

type EmitError =
    | KindNotProduced of SsKey
    | RenderFailed of SsKey * reason: string

/// Slice keyed by SsKey root. Smart constructor below enforces
/// "every Catalog kind is present" — emitters cannot return this
/// type without populating the keyset.
type ArtifactByKind<'element> = private ArtifactByKind of Map<SsKey, 'element>

[<RequireQualifiedAccess>]
module ArtifactByKind =
    let create (catalog: Catalog) (slices: Map<SsKey, 'a>) : Result<ArtifactByKind<'a>, EmitError> =
        let required = Catalog.allKinds catalog |> Seq.map (fun k -> k.SsKey) |> Set.ofSeq
        let provided = slices |> Map.toSeq |> Seq.map fst |> Set.ofSeq
        match Set.toList (Set.difference required provided) with
        | []      -> Ok (ArtifactByKind slices)
        | missing -> Error (KindNotProduced (List.head missing))
    let toMap (ArtifactByKind m) = m

type Emitter<'element> = Catalog -> Result<ArtifactByKind<'element>, EmitError>
```

Concrete signatures become:
```fsharp
let rawTextEmitter   : Emitter<string>           = ...
let jsonEmitter      : Emitter<JsonElement>      = ...
let dacpacEmitter    : Emitter<TSqlObjectScript> = ...
let distributionsEmitter : Catalog -> Profile -> Result<ArtifactByKind<DistributionSlice>, EmitError> = ...
```

T11 becomes a type theorem, not an `Assert.Contains`. The substring tests in `JsonEmitterTests.fs:96` collapse to `Map.dom == Map.dom`, which is true by construction.

**Composition layer** (one per target shape):
```fsharp
[<RequireQualifiedAccess>]
module Render =
    let concatSql (order: SsKey list) (a: ArtifactByKind<string>) : string =
        let m = ArtifactByKind.toMap a
        order |> List.choose (fun k -> Map.tryFind k m) |> String.concat "\n\n"

    let toJsonDocument (a: ArtifactByKind<JsonElement>) : JsonDocument = ...
    let toDacpac (order: SsKey list) (a: ArtifactByKind<TSqlObjectScript>) : DacPackage = ...
```

Per-kind sliceability is independently useful: it enables incremental emit (rebuild only changed kinds), drift detection (pointwise diff of two `ArtifactByKind` maps), partial remediation (R5 in VISION_REVIEW), and structural snapshot caching keyed by `SsKey × content-hash`.

## Q5 — A1 as a structural type

```fsharp
type SsKey =
    | OssysOriginal of System.Guid              // from rowsets adapter; A1 holds
    | Synthesized   of source: string * basis: string  // JSON path; A1 bounded
    | DerivedFrom   of parent: SsKey * reason: string  // pass-introduced
```

Now the JSON-path bound at `AXIOMS.md:47-72` is **type-visible**: a property test claiming `rename(n).key = n.key` accepts only `OssysOriginal` (or `DerivedFrom` rooted in one); on `Synthesized`, the same property is the *negation* — it documents the bound. FsCheck generators stratify naturally:

```fsharp
let ``A1: rename preserves OssysOriginal SsKey`` (k: Kind) =
    match k.SsKey with
    | OssysOriginal _ -> rename k = k.SsKey      // property holds
    | Synthesized _   -> ()                       // bounded; not asserted
    | DerivedFrom _   -> rename k = k.SsKey      // inherits root
```

`Identity.fs:32`'s `SsKey.original : string -> Result<SsKey>` becomes two constructors with distinct call sites — the rowsets adapter (`Projection.Adapters.Osm.CatalogReader.parse` when `SnapshotRowsets` lands) calls `SsKey.ossysOriginal : Guid -> SsKey` (no Result; Guid construction is total); the JSON adapter calls `SsKey.synthesized : source:string -> basis:string -> Result<SsKey>` (Result; basis must be non-blank).

**V1→V2 UUIDv5 mapping.** Add a fourth constructor scoped to cross-version threading:
```fsharp
| V1Mapped of v1Sskey: System.Guid * v2Namespace: System.Guid
```
with a smart constructor `SsKey.fromV1 : v1: Guid -> v2Namespace: Guid -> SsKey` that produces a deterministic UUIDv5 *and tags the value as cross-version*. Pattern-matching consumers can distinguish "this identity originated in V1's space" from "this identity is V2-native" — load-bearing for the `RefactorLogEmitter` audit trail and for the cutover risk surfaced in `VISION_REVIEW_APPENDIX_B_CUTOVER_RISK_AUDIT.md:48-52`.

## Q6 — Refactor-log emission as Π over a diff

```fsharp
type RenameRecord = { OldName: Name; NewName: Name; PassVersion: int }

type CatalogDiff = {
    Source   : Catalog            // the diff is itself Catalog-typed
    Target   : Catalog
    Renamed  : Map<SsKey, RenameRecord>
    Added    : Set<SsKey>
    Removed  : Set<SsKey>
}

[<RequireQualifiedAccess>]
module CatalogDiff =
    /// Total: every SsKey in source ∪ target is in exactly one of
    /// Renamed / Added / Removed / Unchanged. Smart-constructor enforces.
    let between (a: Catalog) (b: Catalog) : Result<CatalogDiff, EmitError> = ...

type RefactorLogEmitter = CatalogDiff -> Result<ArtifactByKind<RefactorLogEntry>, EmitError>
```

The diff is a **Catalog-typed value**, so `ArtifactByKind` over it satisfies T11 by the same theorem — every `SsKey` in the diff's scope appears in the output. `RefactorLogEmitter` stops being "the special emitter that takes a separate rename log"; it's just another Π whose evidence happens to be a diff. This dissolves the asymmetry that VISION.md:58 currently has to narrate prose-style.

## Q7 — Migration strategy

**Incremental, behind a discriminator.** The current `Catalog -> string` shape is too pervasive to flip atomically — `JsonEmitterTests.fs`, `RawTextEmitterTests.fs`, `DistributionsEmitterTests.fs`, `RichProfilingEndToEndTests.fs:280-433`, and `EndToEndDifferentialTests.fs` all consume `string`.

Sequence:

1. **Land `ArtifactByKind<'a>` and `Emitter<'a>` types in `Projection.Core`** alongside (not replacing) existing emitters. No consumer change.
2. **Add `RawTextEmitter.emitSlices : Emitter<string>`** next to `emit : Catalog -> string`. The existing `emit` becomes `Render.concatSql topoOrder (emitSlices catalog |> Result.value)`. One new property test: `T11 by type` (`Set.equal (Map.keys (ArtifactByKind.toMap result)) (Set.ofSeq (Catalog.allKinds c |> Seq.map _.SsKey))`).
3. **Same for JSON, then Distributions** — one emitter per chapter slice.
4. **Once all three carry `emitSlices`**, deprecate the old `emit`. Substring `Assert.Contains` tests at `JsonEmitterTests.fs:96-105` and `RichProfilingEndToEndTests.fs:280` retire (the type now proves what they assert).
5. **`SsKey` DU split is a separate refactor.** Big-bang within `Projection.Core` (closed-DU expansion empirical-test discipline at CLAUDE.md — exhaustiveness errors must light up only at match sites). The two adapters (`Static.fs` calling `SsKey.original`, `CatalogReader.fs` likewise) update; the rest of the codebase pattern-matches on the new variants only at the rename property tests and at `RefactorLogEmitter`.
6. **`CatalogDiff` + `RefactorLogEmitter` is the chapter-3-tail slice** that VISION_REVIEW_APPENDIX_D names; it lands after `SnapshotRowsets` because the diff needs `OssysOriginal` SsKeys to be honest about A1.

Not one chapter. Three: emitter shape (chapter 4); `SsKey` DU split (chapter 5, gated on `SnapshotRowsets`); diff + `RefactorLogEmitter` (chapter 5 tail).

## Q8 — What this enables

- **T11 property test trivializes** to `Set.equal (Map.keys a) (Map.keys b)` across two `ArtifactByKind<_>` results — a one-line theorem, not `for k in allKinds; Assert.Contains`.
- **A1 rename property is type-stratified** — FsCheck generators over `OssysOriginal` assert preservation; over `Synthesized` document the bound. The session-23 prose at `AXIOMS.md:47-72` becomes a type-level fact.
- **Drift detection becomes pointwise.** `ArtifactByKind.compareWith eq deployed target` returns `Map<SsKey, DriftKind>` — sliceable and routable to specific kinds. The current `string`-shaped emitters force whole-document diffing.
- **Partial-state remediation (R5)** becomes `dacpacEmitter (CatalogDiff.between deployed target) |> Render.toDacpac topoOrder` — exactly the per-SsKey shape the production-trustworthy story needs.
- **GraphQL emitter as `Emitter<GraphqlTypeDef>`** drops in trivially T11-compliant — no test rewrite, no commutativity audit; the type carries the obligation.
- **Cutover risk in Appendix B (V1 vs. V2 disagreement on shared SsKeys)** becomes detectable structurally: `Set.equal (v1ArtifactByKind |> Map.keys) (v2ArtifactByKind |> Map.keys)` with per-key value comparison — split-brain shows up as a typed `Map<SsKey, (V1Output * V2Output)>` not as a string-diff.
- **Lineage events on emit failure are pre-routed.** `EmitError.KindNotProduced sskey` carries the identity directly; current emitters can only fail by exception or silently truncate.
- **Snapshot caching** keyed by `(SsKey, contentHash element)` becomes natural; the current monolithic `string` output is opaque to per-kind cache lookup.

## Files cited

- `sidecar/projection/src/Projection.Core/Identity.fs:16-60`
- `sidecar/projection/src/Projection.Core/Catalog.fs:119-195`
- `sidecar/projection/src/Projection.Core/Passes/NamingMorphism.fs:30-90`
- `sidecar/projection/src/Projection.Targets.SSDT/RawTextEmitter.fs:183-191`
- `sidecar/projection/src/Projection.Targets.Json/JsonEmitter.fs:140-153`
- `sidecar/projection/src/Projection.Targets.Distributions/DistributionsEmitter.fs:201-228`
- `sidecar/projection/tests/Projection.Tests/JsonEmitterTests.fs:87-113`
- `sidecar/projection/tests/Projection.Tests/CatalogTests.fs:104-116`
- `sidecar/projection/tests/Projection.Tests/RichProfilingEndToEndTests.fs:280-433`
- `sidecar/projection/AXIOMS.md:40-72` (A1 + bound), `:533-542` (T11), `:546-590` (A18 amended)
- `sidecar/projection/VISION.md:58`, `VISION_REVIEW.md:51`, `VISION_REVIEW_APPENDIX_B_CUTOVER_RISK_AUDIT.md:48-52` (RefactorLogEmitter + UUIDv5 as forthcoming)
