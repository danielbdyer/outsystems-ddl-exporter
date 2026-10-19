# Chapter 3 pre-scope — `ArtifactByKind` type refactor

**Subagent dispatched at session 25 close runway**, per the revision-2 VISION.md naming of the type-system refactor as the cross-cutting work that turns T11 (sibling-Π commutativity) and A1 (identity-survives-rename) into type theorems rather than disciplines. This document is the chapter-open input for the refactor and is intended to be consumed by the chapter-3 agent at chapter-open and by the maintainer reviewing the refactor against `AXIOMS.md`.

The design starting point is `VISION_REVIEW.md` Appendix H §H.1–§H.8. This document carries Appendix H to implementation grade: it specifies edge cases the appendix names but does not resolve, walks the existing call sites the migration must touch, identifies which existing tests retire (and which ones stay), and converts §H.7's six-step plan into slices with explicit pre/post-conditions and rollback moves.

---

## §1. Scope and motivation

This refactor delivers two type theorems and one supporting structural type:

1. **T11 becomes a type theorem.** Every emitter signature changes from `Catalog -> string` (or `Catalog -> Profile -> string`) to `Catalog -> Result<ArtifactByKind<'element>, EmitError>`. `ArtifactByKind` is a private DU whose smart constructor enforces "every Catalog kind is in the keyset." Sibling-Π commutativity becomes a one-line property: `Set.equal (Map.keys a) (Map.keys b)`. The substring `Assert.Contains` discipline currently enforcing T11 at `JsonEmitterTests.fs:96-105` and `RichProfilingEndToEndTests.fs:280` retires (per VISION.md acceptance criterion 4).
2. **A1 becomes type-stratified.** `SsKey` splits into a four-variant DU (`OssysOriginal | Synthesized | DerivedFrom | V1Mapped`) so the JSON-projection-lossiness bound documented at `AXIOMS.md:47-72` (session 23 amendment) is *type-visible*. Property tests claiming A1 accept only `OssysOriginal` (and roots of `DerivedFrom`); on `Synthesized`, the same property documents the bound. The bound stops being prose pinned at the bottom of A1 and becomes a compile-time fact.
3. **`CatalogDiff` is the new structural value** that `RefactorLogEmitter` consumes. The diff is itself Catalog-typed, so `RefactorLogEmitter : CatalogDiff -> Result<ArtifactByKind<RefactorLogEntry>, EmitError>` satisfies T11 by the same theorem.

**Why chapter 3 cross-cutting, not deferred.** Per Appendix H §H.8, every chapter-3+ deliverable depends on this refactor's payoffs:

- DacpacEmitter (3.3) drops in trivially T11-compliant — no commutativity audit, no test rewrite.
- Drift detection (free given read-side adapter at 3.1) becomes pointwise per-SsKey diff: `ArtifactByKind.compareWith eq deployed target : Map<SsKey, DriftKind>`.
- Partial-state remediation (R5; chapter 4.4) becomes `dacpacEmitter (CatalogDiff.between deployed target) |> Render.toDacpac topoOrder` — exactly the per-SsKey shape the production-trustworthy story needs.
- RefactorLogEmitter (3.5) is structurally the same Π as the others, where the evidence happens to be a diff; landing the type before 3.5 dissolves the "special-emitter" asymmetry VISION.md:58 currently has to narrate prose-style.
- Cutover risk (Appendix B §B.3 — V1 vs V2 disagreement on shared SsKeys) becomes detectable structurally as `Map<SsKey, V1Output * V2Output>`, not as a string-diff.

Deferring this past chapter 3 forces every downstream emitter to land twice: once on the legacy `Catalog -> string` shape, once after the flip. The flip is incremental (Appendix H §H.7 sequences it behind a discriminator), but the incrementality only pays back if it starts before the third and fourth emitters write themselves into the legacy shape.

---

## §2. Type design — `ArtifactByKind<'element>` and `Emitter<'element>`

```fsharp
namespace Projection.Core

type EmitError =
    | KindNotProduced of SsKey
    | UnexpectedKind  of SsKey                 // strict mode; see below
    | RenderFailed    of SsKey * reason: string

/// Slice keyed by SsKey root. Smart constructor enforces "every Catalog
/// kind is present" — emitters cannot return this type without populating
/// the keyset. T11 (sibling-Π commutativity) is a structural consequence:
/// any two ArtifactByKind values built from the same Catalog have equal
/// key-sets by construction.
type ArtifactByKind<'element> = private ArtifactByKind of Map<SsKey, 'element>

[<RequireQualifiedAccess>]
module ArtifactByKind =
    let create (catalog: Catalog) (slices: Map<SsKey, 'a>)
        : Result<ArtifactByKind<'a>, EmitError> =
        let required =
            Catalog.allKinds catalog
            |> List.map (fun k -> k.SsKey)
            |> Set.ofList
        let provided = slices |> Map.toSeq |> Seq.map fst |> Set.ofSeq
        let missing  = Set.difference required provided
        let extra    = Set.difference provided required
        match Set.toList missing, Set.toList extra with
        | [], []        -> Ok (ArtifactByKind slices)
        | (k :: _), _   -> Error (KindNotProduced k)
        | [], (k :: _)  -> Error (UnexpectedKind k)

    let toMap (ArtifactByKind m) = m
    let tryFind (k: SsKey) (a: ArtifactByKind<'a>) =
        Map.tryFind k (toMap a)

type Emitter<'element> = Catalog -> Result<ArtifactByKind<'element>, EmitError>
```

**Edge case — superset vs strict equal.** Appendix H §H.4 sketches "every Catalog kind is in the keyset" but does not resolve whether *extra* keys are tolerated. **Decision: strict equality is the right invariant.** An emitter producing a key not in `Catalog.allKinds` is producing output for an identity the catalog does not contain — the only ways this can happen are (a) the emitter has a bug (it manufactured an SsKey from a stale fixture or copy-and-paste); (b) the emitter is consuming derived values whose keys haven't been registered on the catalog (a layering violation — derived keys come from passes which already update the catalog). Both are bugs; neither is benign. Strict equal makes both compile-time-detectable at the smart-constructor seam. The cost is one extra error variant (`UnexpectedKind`) and zero call-site changes. This goes beyond Appendix H's sketch but earns its place — the Appendix's intent ("the type proves the keyset") is sharper under strict equality.

**Composition layer per target shape:**

```fsharp
[<RequireQualifiedAccess>]
module Render =
    /// SSDT raw-text concatenation. Order is supplied externally; emitters
    /// don't know dependency order (it's a Catalog-level fact, not a
    /// per-kind one).
    let concatSql (order: SsKey list) (a: ArtifactByKind<string>) : string =
        let m = ArtifactByKind.toMap a
        order
        |> List.choose (fun k -> Map.tryFind k m)
        |> String.concat "\n\n"

    let toJsonDocument (a: ArtifactByKind<JsonElement>) : JsonDocument = ...
    let toDacpac (order: SsKey list) (a: ArtifactByKind<TSqlObjectScript>)
        : DacPackage = ...
    let toSsdtDirectory (a: ArtifactByKind<SsdtFile>)
        : Map<RelativePath, SsdtFile> = ...
```

**`Map<SsKey, _>` lookup behavior for absent keys.** Per A18 amended, Π is total over the catalog: every `Catalog.allKinds` entry has an output. The smart constructor enforces totality at construction. *After* construction, `Map.tryFind` on a key absent from the source catalog returns `None` legitimately — the only callers asking are diff/comparator code (e.g., `ArtifactByKind.compareWith eq deployed target` per Appendix H §H.8), which expect partial overlap and route the `None` to a `DriftKind` variant. There is no scenario inside Core where an `ArtifactByKind` is asked for a key that *should* be present but isn't; that scenario is the smart constructor's job, and the `Result<_, EmitError>` carries the failure.

**Topological order is *not* an emitter input.** Order depends on cross-kind references (FK target appears before FK source); it is a `TopologicalOrder` value produced by `Projection.Core/Passes/TopologicalOrderPass.fs` (already shipped, line 187 cited in the prompt is the SCC-ordering implementation). The emitter knows kinds; the renderer knows the order. `Render.concatSql` accepts the order as a parameter; the chapter-3 pipeline composes them.

---

## §3. Type design — `SsKey` four-variant DU

```fsharp
namespace Projection.Core

/// Stable identity for catalog nodes (A1, A2, A4). Four variants
/// stratify the JSON-projection-lossiness bound documented at
/// AXIOMS.md:47-72 (session 23 amendment). The variant is not
/// presentation; it is structural, and downstream code pattern-
/// matches on it to decide whether A1 holds unconditionally
/// (OssysOriginal, DerivedFrom rooted in OssysOriginal, V1Mapped)
/// or is bounded (Synthesized).
type SsKey =
    /// SSKey GUID supplied directly by an OSSYS rowsets adapter.
    /// A1 holds unconditionally: this value survives source rename.
    | OssysOriginal of System.Guid

    /// JSON-path-synthesized identity. The source field (e.g.,
    /// "OS_KIND") names the synthesis convention; the basis is the
    /// concatenated name fields the adapter hashed. A1 is BOUNDED
    /// for this variant — a source-side rename produces a different
    /// SsKey. Property tests assert the bound, not preservation.
    | Synthesized of source: string * basis: string

    /// Pass-introduced identity (e.g., the symmetric-closure pass
    /// adding inverse references). Equality semantics: structural,
    /// so `DerivedFrom (a, "inverse") = DerivedFrom (b, "inverse")`
    /// iff `a = b` recursively. A1's preservation inherits from the
    /// root: if `rootOf k = OssysOriginal _`, A1 holds; if
    /// `rootOf k = Synthesized _`, A1 is bounded.
    | DerivedFrom of parent: SsKey * reason: string

    /// Cross-version threading: a V1 entity's SSKey GUID mapped
    /// deterministically into V2's identity space via UUIDv5.
    /// A1 holds within V2 (UUIDv5 is total); the v2Namespace tag
    /// makes the cross-version origin pattern-matchable.
    | V1Mapped of v1Sskey: System.Guid * v2Namespace: System.Guid
```

**Smart constructors per variant:**

```fsharp
[<RequireQualifiedAccess>]
module SsKey =

    /// Total: a System.Guid is well-formed by construction; no Result.
    let ossysOriginal (g: System.Guid) : SsKey = OssysOriginal g

    /// Validated: source and basis must be non-blank.
    let synthesized (source: string) (basis: string) : Result<SsKey> =
        if String.IsNullOrWhiteSpace source then
            Result.failureOf (ValidationError.create "sskey.synth.source.empty"
                "Synthesis source cannot be blank.")
        elif String.IsNullOrWhiteSpace basis then
            Result.failureOf (ValidationError.create "sskey.synth.basis.empty"
                "Synthesis basis cannot be blank.")
        else Result.success (Synthesized (source, basis))

    /// Validated: reason must be non-blank (mirrors current
    /// SsKey.derived at Identity.fs:38-40).
    let derivedFrom (parent: SsKey) (reason: string) : Result<SsKey> =
        if String.IsNullOrWhiteSpace reason then
            Result.failureOf (ValidationError.create "sskey.derived.reason.empty"
                "A derivation reason cannot be blank.")
        else Result.success (DerivedFrom (parent, reason))

    /// V1→V2 UUIDv5 mapping. v2Namespace is the V2-stable namespace
    /// GUID (see "UUIDv5 namespace" below). Total: GUID arithmetic is.
    let fromV1 (v1: System.Guid) (v2Namespace: System.Guid) : SsKey =
        V1Mapped (v1, v2Namespace)
```

**Equality semantics — `OssysOriginal g1 =? V1Mapped (g1, _)`.** *No.* These are different variants of a closed DU; F# structural equality compares the variant tag first. A V1-origin identity that has been deterministically mapped to V2's space is *not the same value* as an OSSYS-rowset GUID that happens to numerically collide — the variant tag preserves the provenance. The pattern-matching consumer (e.g., the `RefactorLogEmitter` audit trail) needs to know which space the identity originated in; equality must respect provenance to keep that information intact. **Equality is per-variant; cross-variant equality is always false.**

**`DerivedFrom` parent semantics — recursive equality.** Structural per F# DU defaults: `DerivedFrom (p1, r1) = DerivedFrom (p2, r2)` iff `p1 = p2 && r1 = r2`, recursively. This is what current `SsKey.Derived` already does at `Identity.fs:38-40`; the new variant inherits the behavior unchanged.

**Pattern-match obligations and exhaustiveness.** Existing `match SsKey with` sites that gain new arms:

- `src/Projection.Core/Identity.fs:44-47, 50-53, 57-60` — `rootOriginal`, `isDerived`, `derivationReasons`. All three need extending: `rootOriginal` walks `DerivedFrom` parents (no semantic change), surfaces `Guid.ToString "N"` for `OssysOriginal` and `V1Mapped`, surfaces `basis` for `Synthesized`. `isDerived` returns true only for `DerivedFrom`; `OssysOriginal`, `Synthesized`, `V1Mapped` return false. `derivationReasons` returns `[]` for all three non-derived leaves.
- `src/Projection.Core/Passes/SymmetricClosure.fs:39-42` — guards on `Derived (_, reason) when reason = inverseReason`. Replaces `Derived` with `DerivedFrom`; semantics unchanged.
- `tests/Projection.Tests/SymmetricClosureTests.fs:45-46` — `Derived (_, reason) -> Assert.Equal("inverse", reason)`. Replaces `Derived` with `DerivedFrom`; semantics unchanged.

These are the *only* match sites — verified by the grep over `src/` and `tests/` against `match.*SsKey`, `| Original`, `| Derived`. Per the closed-DU expansion empirical-test discipline (CLAUDE.md), exhaustiveness errors lighting up *only* at these sites is the structural-correctness check on the seam.

**UUIDv5 namespace question — what V2 uses.** RFC 4122 §4.3 requires a stable namespace GUID. **Decision: V2 derives a single deterministic namespace GUID by hashing a stable string (`"projection.core.v2.identity.namespace"`) under UUIDv5's *own* namespace recursion seeded from the DNS namespace (`6ba7b810-9dad-11d1-80b4-00c04fd430c8`).** Concretely:

```fsharp
let v2Namespace : System.Guid =
    UuidV5.create
        (System.Guid.Parse "6ba7b810-9dad-11d1-80b4-00c04fd430c8")    // DNS ns
        "projection.core.v2.identity.namespace"
```

The chosen seed string is documented in `DECISIONS.md` at the time the namespace lands; once chosen, it can never change without invalidating every `V1Mapped` value ever produced. This is named explicitly in §10 (Risks).

---

## §4. Type design — `CatalogDiff`

```fsharp
type RenameRecord = {
    OldName     : Name
    NewName     : Name
    PassVersion : int
}

/// The diff between two Catalogs, partitioned by SsKey. Smart-constructor
/// enforces the exhaustiveness invariant: every SsKey in source ∪ target
/// is in exactly one of Renamed / Added / Removed / Unchanged.
type CatalogDiff = private {
    Source    : Catalog
    Target    : Catalog
    Renamed   : Map<SsKey, RenameRecord>
    Added     : Set<SsKey>
    Removed   : Set<SsKey>
    Unchanged : Set<SsKey>
}

[<RequireQualifiedAccess>]
module CatalogDiff =
    let between (a: Catalog) (b: Catalog) : Result<CatalogDiff, EmitError> =
        let aKeys = Catalog.allKinds a |> List.map (fun k -> k.SsKey) |> Set.ofList
        let bKeys = Catalog.allKinds b |> List.map (fun k -> k.SsKey) |> Set.ofList
        let added   = Set.difference bKeys aKeys
        let removed = Set.difference aKeys bKeys
        let shared  = Set.intersect aKeys bKeys
        let renamed, unchanged =
            shared
            |> Set.partition (fun k ->
                Catalog.tryFindKind k a
                |> Option.bind (fun ka ->
                    Catalog.tryFindKind k b
                    |> Option.map (fun kb -> ka.Name <> kb.Name))
                |> Option.defaultValue false)
        let renames =
            renamed
            |> Set.toList
            |> List.choose (fun k ->
                match Catalog.tryFindKind k a, Catalog.tryFindKind k b with
                | Some ka, Some kb ->
                    Some (k, { OldName = ka.Name; NewName = kb.Name; PassVersion = 1 })
                | _ -> None)
            |> Map.ofList
        Ok { Source = a; Target = b; Renamed = renames
             Added = added; Removed = removed; Unchanged = unchanged }
```

**Exhaustiveness invariant.** "Every SsKey in source ∪ target is in exactly one of Renamed / Added / Removed / Unchanged." The constructor computes set partitions, so the invariant holds by construction (mutual disjointness comes from `Set.difference` / `Set.partition`; coverage comes from `Set.union (Set.union added removed) (Set.union renamed unchanged) = Set.union aKeys bKeys`). Tests assert this directly.

**SsKey-stable rename vs SsKey-changing replace.** A *rename* preserves SsKey; only `Name` differs — this is the case A1 protects. A *replace* (added + removed; same name, different SsKey) is the failure mode A1 was designed to prevent. Through V2's `SnapshotJson` path, source-side renames currently produce *replace* (different `Synthesized` SsKeys) — this is the bound at `AXIOMS.md:47-72`. The diff *correctly classifies these as replace*, surfacing the bound's consequence: a JSON-path consumer that expected a rename gets a remove+add. This is the right behavior; the diff is honest about the bound. Once `SnapshotRowsets` lands and `OssysOriginal` SsKeys are available, the same source rename produces a *rename* in the diff — A1 is honored end-to-end.

---

## §5. Migration plan — six-step sequence

**Slice 5.1 — Land `ArtifactByKind<'a>` and `Emitter<'a>` in `Projection.Core`.**
- *Precondition.* Existing emitter signatures (`Catalog -> string` at `RawTextEmitter.fs:183`, `JsonEmitter.fs:140`, `DistributionsEmitter.fs:201`) unchanged; tests green at session 25 close.
- *Postcondition.* New types compile in `Projection.Core`; no consumer change; one new property test at the type-construction layer (`ArtifactByKind.create rejects missing keys`, `... rejects extra keys`).
- *Files touched.* `src/Projection.Core/ArtifactByKind.fs` (new, ~80 LOC); `src/Projection.Core/Projection.Core.fsproj` (compile order: after `Catalog.fs`, before `Passes/`); `tests/Projection.Tests/ArtifactByKindTests.fs` (new, ~60 LOC).
- *Test additions.* Strict-equal smart-constructor tests (missing → `KindNotProduced`; extra → `UnexpectedKind`; exact → `Ok`).
- *Rollback move.* No consumer touches the new type; deletion is a clean revert.

**Slice 5.2 — Add `RawTextEmitter.emitSlices : Emitter<string>` next to `emit`.**
- *Precondition.* 5.1 landed.
- *Postcondition.* `RawTextEmitter.emit` becomes `Render.concatSql topoOrder (emitSlices catalog |> Result.value)` (where `topoOrder` is supplied by the test fixture for now; see §6 below). Existing `RawTextEmitterTests.fs` unchanged. One new property test: `T11 by type — emitSlices produces a result whose key-set equals Catalog.allKinds`.
- *Files touched.* `src/Projection.Targets.SSDT/RawTextEmitter.fs` (~+60 LOC: extract per-kind rendering into `renderSlice : Catalog -> Kind -> string`; build the map; call `ArtifactByKind.create`); `tests/Projection.Tests/RawTextEmitterTests.fs` (+20 LOC for the new property).
- *Rollback move.* Delete `emitSlices`; restore `emit`'s direct iteration. No external consumers.

**Slice 5.3 — Same for `JsonEmitter`.**
- *Precondition.* 5.2 landed.
- *Postcondition.* `JsonEmitter.emit` reuses `emitSlices : Emitter<JsonElement>` via `Render.toJsonDocument`. The substring property at `JsonEmitterTests.fs:96-105` is annotated `[<Skip "Retired by ArtifactByKind type theorem; see slice 5.6">]` but kept until 5.6 retires it.
- *Files touched.* `src/Projection.Targets.Json/JsonEmitter.fs`; `tests/Projection.Tests/JsonEmitterTests.fs`.
- *Rollback move.* Same shape as 5.2.

**Slice 5.4 — Same for `DistributionsEmitter` (the `Catalog -> Profile` shape).**
- *Precondition.* 5.3 landed.
- *Postcondition.* `DistributionsEmitter.emitSlices : Catalog -> Profile -> Result<ArtifactByKind<DistributionSlice>, EmitError>` lands; `emit` becomes a wrapper. `RichProfilingEndToEndTests.fs:280` annotated `[<Skip "Retired by ArtifactByKind type theorem">]`.
- *Files touched.* `src/Projection.Targets.Distributions/DistributionsEmitter.fs`; `tests/Projection.Tests/RichProfilingEndToEndTests.fs`.
- *Rollback move.* As 5.2.

**Slice 5.5 — `SsKey` four-variant DU split.** Big-bang within `Projection.Core` per Appendix H §H.7 step 5.
- *Precondition.* 5.4 landed; chapter-mid-audit dispatched and CRITICAL findings (if any) closed.
- *Postcondition.* `SsKey` carries four variants; smart constructors per §3 above; the only match-site changes are the four already named in §3 (Identity.fs three functions, SymmetricClosure.fs guard, the test counterpart). Adapters update: `CatalogReader.fs:93,96,101,110,119` switch `SsKey.original (sprintf "OS_..." )` → `SsKey.synthesized "OS_KIND" basis` (etc.); `Static.fs:62-63` switches to `SsKey.synthesized "OS_ROW" suffix`. Per the closed-DU empirical-test discipline, no caller outside the variant's module needs reshaping — verified by re-running the test suite after the `Identity.fs` change.
- *Files touched.* `src/Projection.Core/Identity.fs`; `src/Projection.Adapters.Osm/CatalogReader.fs`; `src/Projection.Adapters.Sql/Static.fs`; `src/Projection.Core/Passes/SymmetricClosure.fs`; `tests/Projection.Tests/SymmetricClosureTests.fs`; all fixture files that build `SsKey.Original "..."` literals (estimate ~12 fixture files based on `grep -rn "Original \"" tests`; verify at slice-open).
- *Test additions.* `tests/Projection.Tests/SsKeyTests.fs` — variant-equality tests (per §3 above), smart-constructor rejection tests, the `OssysOriginal g <> V1Mapped (g, _)` cross-variant assertion. FsCheck `Arb` instances per variant (see §8).
- *Rollback move.* Revert is a single-commit `Identity.fs` restoration plus the per-adapter call-site reverts; no schema changes, no persistence touched.

**Slice 5.6 — Retire substring T11 enforcement.**
- *Precondition.* 5.5 landed; all three emitters carry `emitSlices`; the property `Set.equal (Map.keys (emitSlices c)) (catalogKeys c)` is green for all three.
- *Postcondition.* `JsonEmitterTests.fs:96-105` and `RichProfilingEndToEndTests.fs:280` are deleted (not Skipped — the type now proves what they asserted). The `SsKey-root-substring-occurs` test at `JsonEmitterTests.fs:87-93` (line 87 — labelled "T4") is *kept* as it asserts a different property (the SsKey root *appears in the rendered text*; that's a rendering invariant, not a kind-coverage one).
- *Files touched.* `tests/Projection.Tests/JsonEmitterTests.fs`; `tests/Projection.Tests/RichProfilingEndToEndTests.fs`.
- *Rollback move.* Restore the deleted tests; they remain semantically correct.

**Slice 5.7 — `CatalogDiff` + `RefactorLogEmitter`.** Per Appendix H §H.7 step 6: chapter-3 *tail* slice, after `SnapshotRowsets` (3.2). The diff needs `OssysOriginal` SsKeys to honor A1 honestly.
- *Precondition.* 5.6 landed; `SnapshotRowsets` shipped and producing `OssysOriginal` SsKeys.
- *Postcondition.* `CatalogDiff.between` smart constructor; `RefactorLogEmitter : CatalogDiff -> Result<ArtifactByKind<RefactorLogEntry>, EmitError>`. Property test `T11: refactor-log emission covers every SsKey in CatalogDiff scope`.
- *Files touched.* `src/Projection.Core/CatalogDiff.fs` (new, ~120 LOC); `src/Projection.Targets.SSDT/RefactorLogEmitter.fs` (new, ~150 LOC); `tests/Projection.Tests/CatalogDiffTests.fs` (new, ~120 LOC); `tests/Projection.Tests/RefactorLogEmitterTests.fs` (new, ~100 LOC).
- *Rollback move.* Both new files deletable independently; existing emitters unaffected.

---

## §6. Coexistence with the current `Catalog -> string` shape

During slices 5.2–5.4, both shapes coexist. The pattern is:

```fsharp
// In RawTextEmitter.fs — after 5.2
let emitSlices : Emitter<string> = fun catalog ->
    let slices =
        Catalog.allKinds catalog
        |> List.map (fun k -> k.SsKey, renderSlice catalog k)
        |> Map.ofList
    ArtifactByKind.create catalog slices

let emit (catalog: Catalog) : string =
    let topoOrder = TopologicalOrderPass.run catalog |> Lineage.value
    match emitSlices catalog with
    | Ok artifact ->
        let prelude = "-- Generated by Projection.Targets.SSDT.RawTextEmitter v..."
        prelude + "\n\n" + Render.concatSql topoOrder.Order artifact
    | Error e -> failwithf "RawTextEmitter.emit: %A" e
```

**Topo-order source of truth.** `TopologicalOrderPass.run : Catalog -> Lineage<TopologicalOrder>` (already shipped; `TopologicalOrder.fs:69`, `TopologicalOrderPass.fs:5`). The emitter does *not* compute order; the order is a Catalog-level fact, computed once and threaded by callers. `Render.concatSql` accepts `SsKey list` (the linearization in `TopologicalOrder.Order`); the wrapper `emit` calls `TopologicalOrderPass.run` at its top and routes `topoOrder.Order` into `Render.concatSql`.

**Does `Render.concatSql` need a separate dependency from the emitter?** Yes. The renderer takes `SsKey list` (the ordered linearization) and an `ArtifactByKind<string>` (the rendered slices). The emitter takes `Catalog` and produces `ArtifactByKind<string>`. Composition is the caller's responsibility. The current monolithic `emit` hides this composition; the new shape exposes it for the canary's benefit (per Appendix H §H.8 — drift detection becomes pointwise per-key, which only works with an exposed map, not a composed string).

The legacy `emit` is the migration's bridge: tests that expect the string output keep passing; new tests at the type theorem layer cover what the substring tests asserted; the old substring tests retire at 5.6.

---

## §7. SsKey DU split — existing call sites

Verified by `grep -rn "SsKey\." src/` and `grep -rn "SsKey\." tests/`. Every site:

- **`src/Projection.Adapters.Osm/CatalogReader.fs:93`** — `SsKey.original (sprintf "OS_MOD_%s" moduleName)`. Migrates to `SsKey.synthesized "OS_MOD" moduleName`. Signature unchanged (`Result<SsKey>`).
- **`src/Projection.Adapters.Osm/CatalogReader.fs:96`** — `SsKey.original (sprintf "OS_KIND_%s_%s" moduleName entityName)`. Migrates to `SsKey.synthesized "OS_KIND" (sprintf "%s.%s" moduleName entityName)`.
- **`src/Projection.Adapters.Osm/CatalogReader.fs:101`** — `OS_ATTR_...`. Migrates to `SsKey.synthesized "OS_ATTR" (sprintf "%s.%s.%s" moduleName entityName attrName)`.
- **`src/Projection.Adapters.Osm/CatalogReader.fs:110`** — `OS_REF_...`. Migrates to `SsKey.synthesized "OS_REF" (sprintf "%s.%s.%s" sourceModuleName sourceEntityName viaAttrName)`.
- **`src/Projection.Adapters.Osm/CatalogReader.fs:119`** — `OS_IDX_...`. Migrates to `SsKey.synthesized "OS_IDX" (sprintf "%s.%s.%s" moduleName entityName indexName)`.
- **`src/Projection.Adapters.Sql/Static.fs:62-63`** — `SsKey.original (sprintf "OS_ROW_%s_%s" (SsKey.rootOriginal kindKey) suffix)`. Migrates to `SsKey.synthesized "OS_ROW" (sprintf "%s.%s" (SsKey.rootOriginal kindKey) suffix)`. The `SsKey.rootOriginal` call also changes — for `Synthesized`, it surfaces `basis`; for `OssysOriginal` and `V1Mapped`, it surfaces the GUID's `"N"` form; for `DerivedFrom`, it walks parents (unchanged).
- **`src/Projection.Core/Passes/SymmetricClosure.fs:46-53`** — `SsKey.derived r.SsKey inverseReason`. Migrates to `SsKey.derivedFrom r.SsKey inverseReason` (one-line rename).
- **`src/Projection.Targets.SSDT/RawTextEmitter.fs:64`**, **`src/Projection.Targets.Json/JsonEmitter.fs:27-28`**, **`src/Projection.Targets.Distributions/DistributionsEmitter.fs:52-53`** — all call `SsKey.rootOriginal` and `SsKey.isDerived` for diagnostic strings only. No call-site code changes; the new variant arms inside `rootOriginal`/`isDerived` already handle them.
- **Fixture files in `tests/Projection.Tests/`** — `grep -rn "Original \"" tests` to enumerate at slice-open. Each `Original "OS_..."` literal becomes `Synthesized ("OS_...", "...")` or, for tests intentionally exercising A1 unconditionally, `OssysOriginal (Guid.Parse "...")`.

**New exhaustiveness obligations** (the four match sites identified in §3): each gains three new arms (`OssysOriginal`, `Synthesized`, `V1Mapped`) on top of the renamed `DerivedFrom`. Per the closed-DU empirical-test discipline (CLAUDE.md), the F# compiler lights up *only these four sites* with exhaustiveness errors at slice 5.5 — if any other site needs reshaping, the seam was wrong and the slice halts.

---

## §8. Test additions

**T11 trivializes:**
```fsharp
[<Property>]
let ``T11: emitSlices key-set equals Catalog.allKinds`` (c: Catalog) =
    let expected = Catalog.allKinds c |> List.map (fun k -> k.SsKey) |> Set.ofList
    match RawTextEmitter.emitSlices c with
    | Ok a  -> Set.equal expected (a |> ArtifactByKind.toMap |> Map.toSeq |> Seq.map fst |> Set.ofSeq)
    | Error _ -> false
```
One line per emitter; one property covers what `JsonEmitterTests.fs:96-105` and `RichProfilingEndToEndTests.fs:280` asserted by substring discipline.

**A1 stratifies:**
```fsharp
[<Property>]
let ``A1: rename preserves OssysOriginal SsKey`` (k: Kind) (m: NamingMorphism.Morphism) =
    match k.SsKey with
    | OssysOriginal _    -> (NamingMorphism.run m { Modules = [...] }).SsKey = k.SsKey
    | DerivedFrom (p, _) -> rootIsOssys p ==> (rename(k).SsKey = k.SsKey)
    | V1Mapped _         -> (NamingMorphism.run m { Modules = [...] }).SsKey = k.SsKey
    | Synthesized _      -> true   // bound documented; not asserted
```

**FsCheck `Arb` instances per variant:**
```fsharp
[<RequireQualifiedAccess>]
module SsKeyArb =
    let ossysOriginal : Arbitrary<SsKey> =
        Arb.fromGen (Gen.map SsKey.ossysOriginal (Gen.constant (System.Guid.NewGuid())))

    let synthesized : Arbitrary<SsKey> =
        Arb.fromGen (
            gen {
                let! source = Gen.elements ["OS_MOD"; "OS_KIND"; "OS_ATTR"; "OS_REF"; "OS_IDX"]
                let! basis = Gen.nonEmptyListOf Gen.alphaNumeric |> Gen.map (System.String.Concat)
                return SsKey.synthesized source basis |> Result.value
            })

    let v1Mapped : Arbitrary<SsKey> = ...
    let derivedFrom (parent: Gen<SsKey>) : Arbitrary<SsKey> = ...

    /// Default arbitrary picks variant uniformly. Tests that need stratified
    /// generation override per variant.
    let any : Arbitrary<SsKey> = ...
```

**Backtick-quoted test names** per maintainer convention:
- `` ``A1: rename preserves OssysOriginal SsKey`` ``
- `` ``A1: rename preserves DerivedFrom SsKey rooted in OssysOriginal`` ``
- `` ``A1 (bound): rename produces different Synthesized SsKey through SnapshotJson path`` ``
- `` ``T11: emitSlices key-set equals Catalog.allKinds for RawTextEmitter`` ``
- `` ``T11: emitSlices key-set equals Catalog.allKinds for JsonEmitter`` ``
- `` ``T11: emitSlices key-set equals Catalog.allKinds for DistributionsEmitter`` ``
- `` ``ArtifactByKind.create rejects missing keys with KindNotProduced`` ``
- `` ``ArtifactByKind.create rejects extra keys with UnexpectedKind`` ``
- `` ``CatalogDiff.between partitions exhaustively into Renamed/Added/Removed/Unchanged`` ``
- `` ``SsKey: OssysOriginal g and V1Mapped (g, _) are not equal across variants`` ``

---

## §9. AXIOMS.md amendments

Two new amendments and two named error variants:

**T11 amended (chapter-3 close):**
> "T11 is a structural type theorem, not a runtime discipline. Encoded as `Emitter<'element> = Catalog -> Result<ArtifactByKind<'element>, EmitError>` where `ArtifactByKind` is a private DU whose smart constructor enforces 'every Catalog kind is in the keyset, and no key outside the catalog is in the keyset.' Any two `ArtifactByKind` values built from the same Catalog have equal key-sets by construction; the substring `Assert.Contains` tests at `JsonEmitterTests.fs:96-105` and `RichProfilingEndToEndTests.fs:280` are retired."

**A1 amended (chapter-3 close):**
> "A1's bound through the `SnapshotJson` input path (session 23 amendment) is type-encoded. `SsKey` carries four variants — `OssysOriginal`, `Synthesized`, `DerivedFrom`, `V1Mapped`. Property tests claiming `rename(n).key = n.key` accept only `OssysOriginal` (and `DerivedFrom` rooted in one) and `V1Mapped`; on `Synthesized`, the same property documents the bound. The variant tag carries provenance; cross-variant equality is always false."

**`EmitError` named in AXIOMS.md "Primitive notions"** with:
> "**EmitError.** A closed DU (`KindNotProduced of SsKey | UnexpectedKind of SsKey | RenderFailed of SsKey * reason: string`) carrying the failure-mode of an `Emitter<'element>`. `ArtifactByKind.create`'s smart constructor produces these; emitters cannot fail silently."

---

## §10. Risks

- **Performance.** A 300-table catalog produces ~300 `Map<SsKey, _>` entries per emit, plus the `Catalog.allKinds` traversal in the smart constructor. Allocation is bounded; F# `Map` is a balanced tree (O(log n) per insertion, ~3KB for n=300). *Measure before optimizing.* If a hot path surfaces — e.g., the canary's per-PR emit cycle running 30 cases per property × 4 properties × 3 emitters = 360 emit cycles — switch to `IReadOnlyDictionary<SsKey, _>` (O(1) lookup, lower allocation pressure). Don't pre-optimize.
- **Discoverability.** `private DU + smart constructor` is a stronger barrier to entry than `type SsKey = Original of string | Derived of ...`. New contributors will not know they must call `SsKey.synthesized` instead of constructing `Synthesized (...)` directly. *Documentation answer:* the F# compiler enforces the privacy — there is no path where a contributor *successfully* bypasses the smart constructor (the type is `private`); the compiler error surfaces the documentation pointer (`SsKey.synthesized`'s docstring per the existing convention at `Identity.fs:30-31`). Plus a CLAUDE.md "Programming style" entry under "Smart constructors return `Result<'a>`" that names the four-variant DU's per-variant constructors as a worked example.
- **UUIDv5 namespace stability.** Once chosen, the V2 namespace GUID can never change without invalidating every `V1Mapped` value ever produced. *Mitigation:* the namespace is derived at slice 5.5 from a documented seed string, and the seed string is recorded in `DECISIONS.md` with explicit commitment language. `V1Mapped` values stored in any persistent surface (refactor.log, drift-detection results, audit trails) carry the namespace tag in the variant — if the namespace ever needs changing, the migration is "produce a `V1MappedV2 of v1Sskey * oldNs * newNs`" variant per the closed-DU empirical-test discipline; existing values keep their tag.

---

## §11. Files inventory

| File | Status | LOC est. | Description |
|---|---|---|---|
| `src/Projection.Core/ArtifactByKind.fs` | NEW | ~80 | `ArtifactByKind<'a>`, `EmitError`, `Emitter<'a>`, smart constructor with strict-equal enforcement. |
| `src/Projection.Core/Render.fs` | NEW | ~80 | `Render.concatSql`, `Render.toJsonDocument`, `Render.toDacpac`, `Render.toSsdtDirectory`. |
| `src/Projection.Core/CatalogDiff.fs` | NEW (5.7) | ~120 | `CatalogDiff` private record + `CatalogDiff.between` smart constructor. |
| `src/Projection.Core/Identity.fs` | MODIFY (5.5) | 60 → ~110 | Four-variant SsKey; per-variant smart constructors; updated `rootOriginal`/`isDerived`/`derivationReasons`. |
| `src/Projection.Core/Projection.Core.fsproj` | MODIFY | +2 | Compile order for new files. |
| `src/Projection.Targets.SSDT/RawTextEmitter.fs` | MODIFY (5.2) | 191 → ~250 | Add `emitSlices : Emitter<string>`; `emit` becomes wrapper. |
| `src/Projection.Targets.SSDT/RefactorLogEmitter.fs` | NEW (5.7) | ~150 | `RefactorLogEmitter : CatalogDiff -> Result<ArtifactByKind<RefactorLogEntry>, EmitError>`. |
| `src/Projection.Targets.Json/JsonEmitter.fs` | MODIFY (5.3) | 153 → ~210 | Add `emitSlices : Emitter<JsonElement>`; `emit` becomes wrapper. |
| `src/Projection.Targets.Distributions/DistributionsEmitter.fs` | MODIFY (5.4) | 228 → ~290 | Add `emitSlices : Catalog -> Profile -> Result<ArtifactByKind<DistributionSlice>, EmitError>`. |
| `src/Projection.Adapters.Osm/CatalogReader.fs` | MODIFY (5.5) | line 93,96,101,110,119 — five call-site updates from `SsKey.original` to `SsKey.synthesized`. |
| `src/Projection.Adapters.Sql/Static.fs` | MODIFY (5.5) | line 62-63 — one call-site update. |
| `src/Projection.Core/Passes/SymmetricClosure.fs` | MODIFY (5.5) | line 39-46 — `Derived` → `DerivedFrom`. |
| `tests/Projection.Tests/ArtifactByKindTests.fs` | NEW | ~60 | Smart-constructor tests. |
| `tests/Projection.Tests/SsKeyTests.fs` | NEW (5.5) | ~120 | Per-variant equality, smart-constructor rejection, FsCheck Arb. |
| `tests/Projection.Tests/CatalogDiffTests.fs` | NEW (5.7) | ~120 | Exhaustiveness invariant; rename-vs-replace classification. |
| `tests/Projection.Tests/RefactorLogEmitterTests.fs` | NEW (5.7) | ~100 | T11 over CatalogDiff scope. |
| `tests/Projection.Tests/JsonEmitterTests.fs` | MODIFY (5.6) | line 96-105 deleted; new T11-by-type property. |
| `tests/Projection.Tests/RichProfilingEndToEndTests.fs` | MODIFY (5.6) | line 280 deleted; new T11-by-type property covering all three emitters. |
| `tests/Projection.Tests/RawTextEmitterTests.fs` | MODIFY (5.2) | +20 | T11-by-type property. |
| `tests/Projection.Tests/SymmetricClosureTests.fs` | MODIFY (5.5) | line 45-46 — `Derived` → `DerivedFrom`. |
| `tests/Projection.Tests/CatalogTests.fs` | MODIFY (5.5) | line 104 may be expanded with FsCheck variant-stratified rename property. |
| `AXIOMS.md` | MODIFY | T11 amendment, A1 amendment, EmitError named. |
| `DECISIONS.md` | APPEND | UUIDv5 namespace commitment; strict-equal smart-constructor decision; `ArtifactByKind` private-DU discoverability mitigation. |

Total new code estimate: ~700 LOC source + ~520 LOC test + ~300 LOC documentation amendment. Total touched LOC: ~1,800. Slices ship independently; rollback is bounded per slice.

### Critical Files for Implementation

- /home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Core/Identity.fs
- /home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Core/Catalog.fs
- /home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Targets.SSDT/RawTextEmitter.fs
- /home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Targets.Json/JsonEmitter.fs
- /home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Adapters.Osm/CatalogReader.fs
