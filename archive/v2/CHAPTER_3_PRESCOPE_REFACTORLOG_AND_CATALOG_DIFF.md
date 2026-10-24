# Chapter 3.5 pre-scope — RefactorLogEmitter + CatalogDiff

*Pre-scope subagent dispatched on the runway to chapter 3.5. The implementation-grade design that follows is what the chapter-open agent should treat as the chapter's first draft, refined under empirical pressure once slices begin.*

---

## §1. Scope

Chapter 3.5 delivers two structural artifacts:

1. **`CatalogDiff`** — a Catalog-typed value computed by smart-constructor from a `(source, target)` Catalog pair. The diff is exhaustive over `source ∪ target` keys; every SsKey in either Catalog is in exactly one of `Renamed`, `Added`, `Removed`, `Unchanged`.
2. **`RefactorLogEmitter`** — a sibling Π whose evidence is a `CatalogDiff`. Signature `CatalogDiff -> Result<ArtifactByKind<RefactorLogEntry>, EmitError>`. Composed via `Render.toRefactorLogXml` into the SSDT-native `.refactorlog` XML document that DacFx and SSDT consume to emit `sp_rename` rather than `DROP COLUMN` + `ADD COLUMN` on incremental deploys.

This slice is what closes A1 in operational terms. Until 3.5 lands, A1 is a *typed* claim (the four-variant `SsKey` DU witnesses it) but not a *structurally exercised* claim — the `renameSurvives` predicate scoped in chapter 3.4 has nothing to assert against because no emitter actually carries the rename forward to the deployment surface. Chapter 3.5's RefactorLogEmitter is the asserter; without it, `renameSurvives` fails trivially the moment FsCheck generates a rename, because the DACPAC redeploy's diff issues `DROP/ADD` and the column's data is lost.

Pre-conditions (must be in place before chapter 3.5 lands its first slice):

- **`ArtifactByKind<'element>` + `Emitter<'element>` types** (Appendix H §H.4 refactor) — so the RefactorLogEmitter's signature is uniform with the other sibling Π's. Without this, the emitter has no shape to inhabit.
- **Four-variant `SsKey` DU** (Appendix H §H.5) — so `OssysOriginal` and `V1Mapped` are distinguished in the diff. Without this, the diff conflates V1-supplied identity with V2-synthesized-from-name identity, and the very renames the emitter must catch are silently invisible.
- **`SnapshotRowsets`** (chapter 3.2 pre-scope) — so V1 SSKey Guids actually flow as `OssysOriginal` SsKeys. Until 3.2 lands, A1's bound applies and chapter 3.5's coverage is itself bounded: renames over `Synthesized` SsKeys cannot be detected (the synthesis basis is the name; a rename produces a different basis and therefore a different SsKey, which the diff classifies as `Removed + Added`, not `Renamed`). The bound is documented; the emitter ships honestly bounded.

---

## §2. `CatalogDiff` design

### Type definition

```fsharp
namespace Projection.Core

/// Per-rename evidence carried inside the diff. The `PassVersion` mirrors
/// the pass-version field other lineage events carry (NamingMorphism.fs:23
/// pattern); it documents which V2 pass produced this rename evidence,
/// enabling cross-version triage when refactor-log entries from older
/// pass versions need re-evaluation.
type RenameRecord = {
    OldName     : Name
    NewName     : Name
    PassVersion : int
}

/// Total decomposition of source ∪ target keys. Smart constructor
/// (`CatalogDiff.between`) enforces exhaustiveness; the type cannot
/// inhabit an inconsistent state.
type CatalogDiff = private CatalogDiff of CatalogDiffData
and CatalogDiffData = {
    Source    : Catalog
    Target    : Catalog
    Renamed   : Map<SsKey, RenameRecord>
    Added     : Set<SsKey>
    Removed   : Set<SsKey>
    Unchanged : Set<SsKey>
}
```

The wrapping `private CatalogDiff of CatalogDiffData` mirrors the `ArtifactByKind` pattern (H.4): the type inhabits an exhaustiveness invariant the smart constructor enforces; consumers go through accessors.

### Smart constructor

```fsharp
[<RequireQualifiedAccess>]
module CatalogDiff =

    let private kindKey (k: Kind) = k.SsKey
    let private allKeys (c: Catalog) =
        Catalog.allKinds c |> Seq.map kindKey |> Set.ofSeq

    /// Total: every SsKey in source ∪ target is in exactly one of
    /// Renamed / Added / Removed / Unchanged. Smart-constructor enforces.
    let between (source: Catalog) (target: Catalog) : Result<CatalogDiff, EmitError> =
        let srcKeys = allKeys source
        let tgtKeys = allKeys target
        let added   = Set.difference tgtKeys srcKeys
        let removed = Set.difference srcKeys tgtKeys
        let intersect = Set.intersect srcKeys tgtKeys

        let nameOf c k =
            Catalog.tryFindKind k c
            |> Option.map (fun kd -> kd.Name)

        let renamed, unchanged =
            intersect
            |> Set.fold (fun (rn, un) k ->
                match nameOf source k, nameOf target k with
                | Some sn, Some tn when sn <> tn ->
                    Map.add k { OldName = sn; NewName = tn
                                PassVersion = NamingMorphism.version } rn,
                    un
                | _ ->
                    rn, Set.add k un) (Map.empty, Set.empty)

        // Exhaustiveness invariant: |srcKeys ∪ tgtKeys|
        //   = |Removed| + |Added| + |Renamed| + |Unchanged|
        Ok (CatalogDiff {
            Source = source; Target = target
            Renamed = renamed; Added = added
            Removed = removed; Unchanged = unchanged })

    let source    (CatalogDiff d) = d.Source
    let target    (CatalogDiff d) = d.Target
    let renamed   (CatalogDiff d) = d.Renamed
    let added     (CatalogDiff d) = d.Added
    let removed   (CatalogDiff d) = d.Removed
    let unchanged (CatalogDiff d) = d.Unchanged
```

### Edge case 1: cross-version `OssysOriginal` ↔ `V1Mapped`

When source is V1's last-deployed schema (read through the read-side adapter, which produces `V1Mapped` SsKeys keyed on V1's original SSKey Guid) and target is V2's emission (which produces `OssysOriginal` SsKeys from `SnapshotRowsets`), the same logical entity has *two distinct* `SsKey` values.

**Recommendation: treat them as the same identity.** Both `V1Mapped {v1Sskey = g; v2Namespace = ns}` and `OssysOriginal g` resolve to a stable Guid; we provide a derivation `SsKey.identityKey : SsKey -> Guid` that:

- `OssysOriginal g` → `g`
- `V1Mapped {v1Sskey = g; v2Namespace = ns}` → UUIDv5(ns, g.ToByteArray()) — but this collides with the *source-version* `OssysOriginal` only if `V1Mapped` carries the *same* derived Guid. So the convention is sharper:
- `V1Mapped` is constructed by `SsKey.fromV1 v1Guid v2Namespace`; its `v1Sskey` field is the V1-supplied Guid; its `identityKey` is the deterministic UUIDv5 derivative.
- The cross-version equivalence is enforced at the diff layer, not at structural equality. `CatalogDiff.between` keys both sides through `SsKey.identityKey` *only* when one side carries `V1Mapped` and the other `OssysOriginal`. By default, the diff keys on `SsKey` itself (preserves T11's per-key composability).

This keeps `Catalog`-level equality (A4) honest at the structural level and lets the diff carry the cross-version concern.

### Edge case 2: nested renames

A kind exists in source as `Customer{Attributes=[Name; Email]}` and in target as `Customer{Attributes=[Name; ContactEmail]}`. Two renames are present: zero at the kind level, one at the attribute level (`Email` → `ContactEmail`). The diff must surface both axes.

**Specification.** `CatalogDiff` carries a flat `Renamed: Map<SsKey, RenameRecord>` keyed by `SsKey`, *agnostic to whether the SsKey points at a kind, attribute, reference, or module*. The smart constructor does not climb into a kind's attributes; it walks `Catalog.allKinds source` and computes a *flat* sequence of every `SsKey` in the catalog (kinds + their attributes + their references + their indexes). A new helper:

```fsharp
let private allSsKeys (c: Catalog) : Set<SsKey> =
    seq {
        for m in c.Modules do
            yield m.SsKey
            for k in m.Kinds do
                yield k.SsKey
                for a in k.Attributes do yield a.SsKey
                for r in k.References do yield r.SsKey
                for i in k.Indexes    do yield i.SsKey
    } |> Set.ofSeq
```

The diff computes `Renamed` by name-comparison at the *level of each SsKey's owner record* — for an attribute SsKey, compare `Attribute.Name` in source vs target; for a kind SsKey, compare `Kind.Name`. This means a renamed attribute on an unrenamed kind produces one entry in `Renamed` (the attribute's), the kind itself is in `Unchanged`, and the refactor-log emitter renders one `<Operation>` for the column rename — which is exactly what DacFx needs.

A simultaneous kind-level *and* attribute-level rename produces two entries; the emitter renders two `<Operation>` elements. This fans out without ceremony because the diff's keying already accommodates both.

---

## §3. `RefactorLogEntry` shape

```fsharp
namespace Projection.Targets.SSDT

open Projection.Core

/// SSDT-native operation type. The .refactorlog XML format supports
/// several operation kinds; rename is the load-bearing one for V2's
/// cutover demand. `MoveSchema` is admissible if a future schema-
/// rename pass demands it (out of chapter 3.5 scope; deferred).
type RefactorOperationKind =
    | RenameRefactor

/// Element-type discriminator for the SSDT element being refactored.
/// SSDT enumerates these as strings (SqlSimpleColumn, SqlTable,
/// SqlIndex, etc.); V2 keeps a closed DU and renders to string at
/// emission time. Add variants under the closed-DU empirical-test
/// discipline; first slice carries Column only.
type RefactorElementType =
    | SqlSimpleColumn
    | SqlTable

/// One record's worth of refactor evidence — exactly what becomes one
/// <Operation> element in the .refactorlog XML.
type RefactorLogEntry = {
    /// Deterministic UUIDv5 derived from the SsKey's identityKey + a
    /// stable namespace. This is the SSDT operation `Key`. Two emit
    /// runs against the same diff produce the same operation Key —
    /// load-bearing for T1.
    OperationKey      : System.Guid
    /// SSDT operation kind (the XML's `Name` attribute).
    OperationKind     : RefactorOperationKind
    /// Element reference in SSDT-bracketed form: [schema].[table].[col].
    ElementName       : string
    /// SSDT element-type tag.
    ElementType       : RefactorElementType
    /// Owner of the element (the table for a column rename).
    ParentElementName : string
    ParentElementType : RefactorElementType
    /// The new name (SSDT does not store the old name; the .sql file's
    /// current state is the new name. `OldName` lives in the SsKey's
    /// pre-diff Catalog and in the source-side Name).
    NewName           : string
    /// Pass version that produced the rename (carried from RenameRecord).
    PassVersion       : int
}
```

The deterministic `OperationKey` matters. SSDT's GUI generates a fresh GUID per rename; V2 must derive it deterministically from the diff so that `Catalog × Catalog → CatalogDiff → RefactorLogEntry` is pure. UUIDv5 does this:

```
OperationKey = uuidv5(namespace = V2_REFACTOR_NS,
                     name      = "rename:" + identityKey(SsKey) + ":" + oldName + ":" + newName)
```

The triple of (identityKey, oldName, newName) is in the diff's `Renamed` map; the namespace is a fixed V2 constant (see §5).

**Operation kinds beyond rename.** SSDT's refactor.log supports `RenameRefactor`, `MoveSchemaRefactor`, `WildcardExpansionRefactor`, `ChangeColumnTypeRefactor`. V2 emits **`RenameRefactor` only** in chapter 3.5. Move-schema and column-type changes are admissible later via DU expansion under the closed-DU empirical-test discipline. The `Added` and `Removed` keys in the diff produce **no** refactor-log entry — they're genuine creates/drops, not renames; DacFx's incremental deploy plan handles them with its native CREATE/DROP path.

---

## §4. `RefactorLogEmitter` as a sibling Π

```fsharp
namespace Projection.Targets.SSDT

[<RequireQualifiedAccess>]
module RefactorLogEmitter =

    [<Literal>]
    let version : int = 1

    /// Sibling Π. T11 obligation: every SsKey in the diff's scope
    /// appears in the output's keyset.
    let emit : CatalogDiff -> Result<ArtifactByKind<RefactorLogEntry list>, EmitError>
```

The element type is `RefactorLogEntry list`, not `RefactorLogEntry`, because:

- An `Unchanged` SsKey produces an empty list (T11 satisfied: every key in scope is in the output, with empty evidence).
- A `Renamed` kind-level SsKey produces a one-element list.
- An `Added` or `Removed` SsKey produces an empty list (no rename evidence; the diff classified it as create/drop).

This is the cleanest answer to the question "what does T11 mean for `Unchanged` keys" raised in the brief: *T11 says every catalog key is in the output's keyset, not that every output value is non-empty*. An emitter producing empty per-key payload still satisfies T11 by the ArtifactByKind smart-constructor's keyset check; the consumer (the renderer) filters keys with empty lists out at composition time.

### Implementation skeleton

```fsharp
let emit (diff: CatalogDiff) : Result<ArtifactByKind<RefactorLogEntry list>, EmitError> =
    let target = CatalogDiff.target diff
    let renamed = CatalogDiff.renamed diff

    // Required keyset: every SsKey present in the target (T11 obligation
    // is to the *target* Catalog — refactor-log entries describe the
    // target state's history, not the source state's).
    let slices =
        Catalog.allKinds target
        |> Seq.collect (fun k ->
            seq {
                yield k.SsKey, kindRefactorEntries diff k
                for a in k.Attributes do
                    yield a.SsKey, attributeRefactorEntries diff k a
                // (References + indexes deferred to slice 4 if demand surfaces.)
            })
        |> Map.ofSeq

    ArtifactByKind.create target slices
```

Where `kindRefactorEntries` and `attributeRefactorEntries` consult `renamed` and produce `RefactorLogEntry list` (zero or one element).

### `Render.toRefactorLogXml`

```fsharp
[<RequireQualifiedAccess>]
module Render =

    /// Compose the per-key entries into one .refactorlog XML document.
    /// Determinism: <Operation> elements are sorted by OperationKey
    /// (stable UUIDv5 derivation; T1 byte-deterministic).
    let toRefactorLogXml (artifact: ArtifactByKind<RefactorLogEntry list>) : string =
        let entries =
            artifact
            |> ArtifactByKind.toMap
            |> Map.toSeq
            |> Seq.collect snd
            |> Seq.sortBy (fun e -> e.OperationKey)
            |> Seq.toList
        renderXml entries
```

`renderXml` writes the XML with a deterministic UTF-8 encoder (the `Utf8JsonWriter` discipline does not apply — XML — so use `XmlWriter` with `XmlWriterSettings { Encoding = UTF8NoBom; Indent = true; NewLineChars = "\n"; OmitXmlDeclaration = false }`). The whole document is byte-deterministic because (a) SsKey ordering is stable in `ArtifactByKind`, (b) operation Keys are UUIDv5-derived (stable), (c) the XML writer settings pin every formatting axis. T1 holds.

---

## §5. V1↔V2 UUIDv5 identity threading

### `SsKey.fromV1`

```fsharp
namespace Projection.Core

[<RequireQualifiedAccess>]
module SsKey =

    /// Stable namespace for V1↔V2 identity threading. The literal is
    /// derived once (RFC 4122 §4.3 from the string
    /// "projection.v2.v1mapped") and recorded as a constant. Consumers
    /// that re-derive get the same value; the namespace is part of V2's
    /// stable wire identity.
    [<Literal>]
    let private v1MappedNamespaceString =
        "c7c7c7c7-1f9c-5e7d-a1d2-4f2b8e6c9a3f"
    let v1MappedNamespace : System.Guid =
        System.Guid v1MappedNamespaceString

    /// Map a V1 SSKey Guid to a V2 SsKey via deterministic UUIDv5.
    /// Total: Guid construction never fails; no Result.
    let fromV1 (v1Sskey: System.Guid) : SsKey =
        V1Mapped (v1Sskey = v1Sskey, v2Namespace = v1MappedNamespace)

    /// The identity key extracted for diff-time cross-version comparison.
    let identityKey (k: SsKey) : System.Guid =
        match k with
        | OssysOriginal g          -> g
        | V1Mapped (v1, _)         -> v1
        | Synthesized (_, basis)   -> uuidv5 v1MappedNamespace basis
        | DerivedFrom (parent, _)  -> identityKey parent
```

UUIDv5 is the standard RFC 4122 §4.3 SHA-1 derivation; F# implementation lives in `Projection.Core/Identity.fs` as a small private helper (~30 LOC).

### Mapping seam

The `V1Mapped` constructor is called in **exactly two places**:

1. **`Projection.Adapters.Sql.ReadSide`** (chapter 3.1) — when reading V1's last-deployed schema back from the live SQL Server, the read-side adapter has access to the `EntitySSKey` Guids stored in V1's SSDT extended properties (V1's emission writes them as table-level extended properties; chapter 3.1's read-side adapter parses them via `sys.extended_properties`). Each read-back kind gets `SsKey.fromV1 epGuid`.

2. **`Projection.Adapters.Osm.RowsetBundle.parseRowsetBundle`** (chapter 3.2) — when the rowset bundle's `EntityRow.EntitySsKey: Guid option` is `Some g`, the adapter calls `SsKey.fromV1 g` instead of the synthesized `OS_KIND_<modName>_<entName>` path. *Wait — sharper:* `SsKey.fromV1` is for cross-version threading specifically (V1 deployed → V2 emission). For the in-bundle case, the rowset's `EntitySsKey` *is* OutSystems' canonical identity, so the right constructor is `SsKey.ossysOriginal g` (a new total smart constructor — Guid construction never fails). `V1Mapped` is reserved for the *read-back* path where the Guid was stored by V1's previous emission and retrieved from the deployed schema.

The split:

- **`OssysOriginal`** = "this Guid came from the OutSystems platform's rowsets" (chapter 3.2).
- **`V1Mapped`** = "this Guid was stamped onto a deployed schema by V1 and we read it back" (chapter 3.1).

Both are honest about A1; the discriminator carries provenance for diagnostics.

### Bound

Through the `SnapshotJson` path (chapter 2's adapter), no V1 SSKey Guid is ever available — the JSON projection strips the SSKey columns at V1's `SnapshotJsonBuilder.cs:114-126` (per chapter 3.2 pre-scope §1). All SsKeys produced from `SnapshotJson` are `Synthesized {source = "osm.json"; basis = "OS_KIND_<mod>_<ent>"}`. On `Synthesized`, the `renameSurvives` property is the *negation* form (Appendix H §H.5): the property documents the bound rather than asserting preservation. When `SnapshotRowsets` lands (chapter 3.2), the bound resolves for every kind whose rowset bundle carries `EntitySsKey`. The bound becomes type-visible and FsCheck generators stratify naturally.

---

## §6. The SSDT `.refactorlog` XML format

The `.refactorlog` is an XML file living alongside the `.sqlproj` (or inside the SSDT project's folder hierarchy when referenced from the project file). DacFx and SSDT consume it during incremental deploy planning to convert what would be a `DROP COLUMN` + `ADD COLUMN` pair into an `sp_rename` call.

### Element schema

```
<Operations Version="1.0" xmlns="http://schemas.microsoft.com/sqlserver/dac/Serialization/2012/02">
  <Operation Name="<refactor-kind>" Key="<guid>" ChangeDateTime="<ISO-8601>">
    <Property Name="ElementName"        Value="[schema].[table].[col]" />
    <Property Name="ElementType"        Value="<SqlSimpleColumn|SqlTable|...>" />
    <Property Name="ParentElementName"  Value="[schema].[table]" />
    <Property Name="ParentElementType"  Value="<SqlTable|SqlSchema|...>" />
    <Property Name="NewName"            Value="<new-leaf-name>" />
  </Operation>
  ...
</Operations>
```

Confirmed via the in-repo SSDT playbook (`/home/user/outsystems-ddl-exporter/ssdt-playbook/Foundations/Anatomy-of-an-SSDT-Project.md` and `/home/user/outsystems-ddl-exporter/handbook/09-The-Refactorlog-and-Rename-Discipline.md`). The XML namespace is `http://schemas.microsoft.com/sqlserver/dac/Serialization/2012/02` (load-bearing — DacFx rejects documents with the wrong namespace silently by treating them as empty).

### Sample document (one rename)

```xml
<?xml version="1.0" encoding="utf-8"?>
<Operations Version="1.0" xmlns="http://schemas.microsoft.com/sqlserver/dac/Serialization/2012/02">
  <Operation Name="Rename Refactor"
             Key="3e8a1c92-7d4f-5b6a-9e3c-2d1f4a8b6c7e"
             ChangeDateTime="2026-05-08T00:00:00Z">
    <Property Name="ElementName"        Value="[dbo].[Customer].[FirstName]" />
    <Property Name="ElementType"        Value="SqlSimpleColumn" />
    <Property Name="ParentElementName"  Value="[dbo].[Customer]" />
    <Property Name="ParentElementType"  Value="SqlTable" />
    <Property Name="NewName"            Value="GivenName" />
  </Operation>
</Operations>
```

### Determinism

`ChangeDateTime` is the one non-deterministic field SSDT writes by default. V2 cannot use wall-clock time (T1 prohibition; CLAUDE.md "no `DateTime.Now` in Core"). Two options:

1. **Pin a deterministic constant.** Use `2000-01-01T00:00:00Z` for every operation. DacFx ignores `ChangeDateTime` for refactor application; it's audit metadata, not load-bearing. T1 holds; the document is byte-identical.
2. **Derive from the diff.** `ChangeDateTime` = the source Catalog's snapshot timestamp, threaded through the Lifecycle dimension. Costs Lifecycle-input plumbing.

**Recommendation: option 1** for chapter 3.5. The Lifecycle thread re-opens if a real consumer needs the timestamp — but DacFx ignores it. Pinning a constant is the cheap honest move.

`Render.toRefactorLogXml` produces a deterministic document by:

- Sorting `<Operation>` elements by `OperationKey` (lexicographic Guid string).
- Writing UTF-8 with no BOM, `\n` line endings, two-space indent.
- Pinning `ChangeDateTime` to the constant.
- Pinning the namespace string and the `Version="1.0"` attribute.

---

## §7. Slice-by-slice breakdown

| # | Slice | Files created/touched | LOC | Acceptance criterion |
|---|---|---|---|---|
| 1 | `CatalogDiff` type + smart constructor + exhaustiveness property test | `src/Projection.Core/CatalogDiff.fs` (new); `tests/Projection.Tests/CatalogDiffTests.fs` (new) | ~120 + ~100 | FsCheck property: for any `(s, t)`, `|allSsKeys s ∪ allSsKeys t| = |Renamed| + |Added| + |Removed| + |Unchanged|`. Worked example: rename one attribute → exactly one entry in `Renamed`, kind in `Unchanged`. |
| 2 | `SsKey.fromV1` + UUIDv5 derivation + property test | `src/Projection.Core/Identity.fs` (extend); `tests/Projection.Tests/IdentityTests.fs` (new — extracted) | ~50 + ~80 | FsCheck property: `fromV1 g = fromV1 g` (determinism); `identityKey (fromV1 g) = g`; `identityKey (Synthesized x y) = identityKey (Synthesized x y)`. |
| 3 | `RefactorLogEntry` + `RefactorLogEmitter.emit` over a synthesized `CatalogDiff` | `src/Projection.Targets.SSDT/RefactorLogEmitter.fs` (new); `tests/Projection.Tests/RefactorLogEmitterTests.fs` (new) | ~150 + ~120 | T11 property: `Set.equal (allSsKeys (CatalogDiff.target d)) (Map.keys (ArtifactByKind.toMap result))`. Worked example: one-rename diff produces one-entry artifact for the renamed key, empty lists elsewhere. |
| 4 | `Render.toRefactorLogXml` + golden-file test | `src/Projection.Targets.SSDT/RefactorLogRender.fs` (new); `tests/Projection.Tests/RefactorLogRenderTests.fs` (new) + `tests/.../golden/single-column-rename.refactorlog.xml` | ~100 + ~80 + ~12 | One rename produces specific XML bytes (golden file); n renames produce n `<Operation>` elements sorted by Key (FsCheck property). |
| 5 | Wire-up to SnapshotRowsets — `V1Mapped`/`OssysOriginal` SsKey path | `src/Projection.Adapters.Osm/CatalogReader.fs` (modify `parseRowsetBundle`) — gated on chapter 3.2 landing | ~30 (extension) | Rowset path produces `OssysOriginal` SsKeys when `EntitySsKey: Some g`; falls through to synthesized when `None`. Differential test: same fixture through both paths produces structurally-equivalent Catalog modulo SsKey shape. |
| 6 | `renameSurvives` predicate green | `tests/Projection.Tests/CanaryPredicatesTests.fs` (depends on chapter 3.4) — extends predicate with refactor-log path | ~80 | Property: for any `(s, t)` where `t` is `s` with one rename, `RefactorLogEmitter.emit (CatalogDiff.between s t)` produces a `RenameRefactor` entry that DacFx applies as `sp_rename`. Tier-2 ephemeral-DB integration test confirms zero `DROP COLUMN` in the redeploy plan. |

Sequencing:
- Slices 1–4 ship in chapter 3.5's first session-arc; they form a self-contained landable unit (the type, the emitter, the renderer, the golden) that does not yet light A1 up at the rowset level.
- Slice 5 is gated on chapter 3.2 (`SnapshotRowsets`); ships in chapter 3.5's second session if 3.2 lands first, or trails as a follow-on.
- Slice 6 is gated on chapter 3.4 (`renameSurvives` predicate library) AND chapter 3.3 (DacpacEmitter for the integration test). It's the chapter-tail close.

---

## §8. Test strategy

**Property-test dominant** (per CLAUDE.md). Three property bands:

1. **Diff exhaustiveness.** FsCheck-generated `(Catalog, Catalog)` pairs; assert `|src ∪ tgt| = |Renamed| + |Added| + |Removed| + |Unchanged|` and the four sets are pairwise disjoint. ~30 cases × shrinking covers the combinatorial space.
2. **UUIDv5 determinism.** `fromV1 g = fromV1 g`; `OperationKey(diff) = OperationKey(diff)`. The emitter is a pure function; this is the T1 lemma at the type level.
3. **T11 sibling-agreement on diff.** `RefactorLogEmitter` consumes `CatalogDiff`; the emitter's output keyset = the target Catalog's keyset. Since `CatalogDiff` is itself Catalog-typed (carries source + target as fields), the same `ArtifactByKind` smart constructor enforces T11; the property test trivializes to `Set.equal (Catalog.allKinds (CatalogDiff.target d) |> Seq.map _.SsKey |> Set.ofSeq) (artifact |> ArtifactByKind.toMap |> Map.toSeq |> Seq.map fst |> Set.ofSeq)`.

**Golden-file tests.** One rename → one `.refactorlog` document with specific bytes (committed at `tests/Projection.Tests/golden/single-column-rename.refactorlog.xml`). N renames sorted by Key; spot-check via FsCheck for "every operation in output has a Key matching some entry in `diff.Renamed`."

**Integration test (slice 6).** Ephemeral SQL Server (`SqlServerFixture` reuse from `tests/Osm.TestSupport/`); deploy DACPAC v1 → rename one column in source → deploy DACPAC v2 with `.refactorlog` → assert the deploy plan's T-SQL contains `EXEC sp_rename` and not `DROP COLUMN`. This is where chapter 3.4's `renameSurvives` predicate cashes out against this emitter.

Test fixtures: build a `CatalogDiff` literal from inline source + target Catalog values; same pattern as `OsmCatalogReaderDifferentialTests.fs`'s string-literal fixtures.

---

## §9. V1 hooks

**V1 does not emit a `.refactorlog`.** Confirmed by exhaustive search of `/home/user/outsystems-ddl-exporter/src/`: no string `RefactorLog`, `.refactorlog`, or `Operations xmlns` in any `.cs` file under `src/`. V1's `SmoRenameLens` (`src/Osm.Smo/SmoRenameLens.cs`) computes per-table rename mappings for *header annotations* (`PerTableHeaderItem.Create("RenamedFrom", ...)` at `src/Osm.Emission/TableHeaderFactory.cs:39`), not for `.refactorlog` emission. V1's rename support is a *comment in the generated `.sql`*, not an SSDT-native refactor record.

This has two consequences:

1. **V2 is the sole emitter** of refactor.log artifacts. The cutover scenario's "RefactorLog records that need to survive across schema versions" demand is entirely on V2.
2. **The chapter 3.1 comparator does not need a tolerance for refactor.log divergences** — V1 produces nothing, V2 produces something; the divergence is "V2 added capability," not "V2 disagrees with V1." The triangulation comparator (`C_v1` vs `C_round` vs `C_ossys`) classifies any refactor-log entry as a V2-only signal, not as an attribution candidate.

V1's `SmoRenameMapping` could optionally feed V2's `RenameRecord` carrier as a *seed* for already-known historical renames — but this is a chapter-4 concern (replay V1's history through V2's substrate). Out of chapter 3.5 scope.

---

## §10. AXIOMS.md amendments

Three new amendments, all append-only:

**A1 amended again (2026-05-08, chapter-3.5 session-N).** The four-variant `SsKey` DU (added in the H.4 refactor) now has a fourth case `V1Mapped of v1Sskey: Guid * v2Namespace: Guid` whose role is **cross-version identity preservation**. A `V1Mapped` SsKey carries the V1-supplied Guid (read back from the deployed schema's extended properties or from the rowset bundle) plus a V2 namespace constant; UUIDv5 derivation makes the value deterministic. A1's identity-survives-rename guarantee extends to the cross-version case: a V1-deployed kind with SSKey `g`, retrieved by the read-side adapter, produces `V1Mapped (g, ns)`; the same kind in V2's emission via SnapshotRowsets produces `OssysOriginal g`; the diff's `identityKey` projection identifies them; the rename property holds across the version boundary. The bound for `Synthesized` SsKeys (session-23 amendment) is unchanged.

**T11 amended (2026-05-08, chapter-3.5 session-N).** The sibling-Π commutativity theorem extends to **diff-typed inputs** by the same proof: a `CatalogDiff` carries `Source: Catalog` and `Target: Catalog` as fields, both Catalog-typed; `ArtifactByKind`'s smart constructor enforces the keyset against `target`; T11's "every Catalog kind is in the keyset" trivializes for Π's that consume diffs. RefactorLogEmitter is the worked example.

**A35 (new, 2026-05-08) — Renames are first-class.** A renamed identity is preserved across schema versions via the SSDT-native refactor.log surface. *Enforcement.* The four-variant `SsKey` DU + `RefactorLogEmitter` over `CatalogDiff` + UUIDv5 determinism. *Property test.* `renameSurvives`: for any source/target Catalog pair related by one rename, the deployed redeploy plan contains `sp_rename` and no `DROP COLUMN`. *Bound.* Through the `SnapshotJson` path, A35 inherits A1's bound.

---

## §11. Risks

**R1: SSDT format drift across SQL Server versions (2019 vs 2022).** The XML namespace `http://schemas.microsoft.com/sqlserver/dac/Serialization/2012/02` is stable across SSDT versions through SQL 2022 (the namespace year `2012` is the schema's revision marker, not the server's version). Verified via the SSDT playbook's anatomy doc (which is the team's own current reference for SQL 2019/2022 deployments). **Mitigation:** pin the namespace to a single string constant; if a future SQL Server version bumps it, the change lights up at the constant's call sites only. **Re-open trigger:** DacFx integration test on SQL 2025 (or whichever version ships next) reports the `.refactorlog` as ignored.

**R2: DacFx ignores the refactor.log for column-type-change-with-rename.** SSDT's `.refactorlog` carries `RenameRefactor` only; if a column is *both* renamed *and* type-changed in the same diff, DacFx applies the rename via `sp_rename` and then issues an `ALTER COLUMN` for the type — but the *data* during the type change may be lost if the type is incompatible. **Mitigation (V2):** the diff already classifies type changes orthogonally (the `Renamed` map carries `OldName/NewName` only; type changes ride through SSDT's normal `ALTER COLUMN` plan). Document the joint-rename-and-retype edge case as a chapter-3.5 known-limitation; the canary's `idempotentRedeploy` predicate catches it because the redeploy plan is non-empty in this case.

**R3: V1 SSKey collisions across modules.** V1's SSKey is unique per OutSystems platform but not necessarily across deployments (two separate OutSystems instances may produce overlapping Guids). **Mitigation:** the `v2Namespace` field in `V1Mapped` is the disambiguator; UUIDv5 derivation includes it. Cross-instance threading is out of chapter 3.5 scope; the namespace is a single constant.

**R4: Refactor-log entries accumulate forever.** SSDT convention is "never delete refactorlog entries" (per the handbook). V2's diff is per-version (source = previous deployment; target = current emission); each emit produces only the *new* renames. The composition into a *cumulative* refactor.log across versions (the document SSDT wants) is a chapter-3.5-tail concern: the renderer can either (a) emit a per-version delta document and let SSDT/DacFx merge with a baseline, or (b) accumulate against a baseline file. **Recommendation:** (a) for chapter 3.5 (delta-only); (b) deferred until a real operator demand surfaces (likely chapter 4.1's promoted-lane integration test will force the question).

**R5: `Synthesized` SsKey rename invisibility.** Through `SnapshotJson`, a renamed kind produces a *different* synthesized SsKey (because the synthesis basis is the name). The diff classifies this as `Removed (oldKey) + Added (newKey)`, not `Renamed`. RefactorLogEmitter produces no operation; DacFx does `DROP/ADD`; data loss occurs. **Mitigation:** SnapshotRowsets resolves this for every kind with `EntitySsKey: Some _`. Until 3.2 lands, A1's bound applies and chapter 3.5 ships honestly bounded; the property test on `Synthesized` documents the bound rather than asserting the property.

---

## §12. Files inventory

**New:**

- `/home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Core/CatalogDiff.fs`
- `/home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Targets.SSDT/RefactorLogEmitter.fs`
- `/home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Targets.SSDT/RefactorLogRender.fs`
- `/home/user/outsystems-ddl-exporter/sidecar/projection/tests/Projection.Tests/CatalogDiffTests.fs`
- `/home/user/outsystems-ddl-exporter/sidecar/projection/tests/Projection.Tests/IdentityTests.fs` *(extracted from `CatalogTests.fs:127-149`)*
- `/home/user/outsystems-ddl-exporter/sidecar/projection/tests/Projection.Tests/RefactorLogEmitterTests.fs`
- `/home/user/outsystems-ddl-exporter/sidecar/projection/tests/Projection.Tests/RefactorLogRenderTests.fs`
- `/home/user/outsystems-ddl-exporter/sidecar/projection/tests/Projection.Tests/golden/single-column-rename.refactorlog.xml`

**Modified:**

- `/home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Core/Identity.fs` *(four-variant `SsKey` DU; `fromV1` constructor; `identityKey` projection — Appendix H §H.5 refactor; arrives via the H.5 chapter that precedes 3.5)*
- `/home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Core/Projection.Core.fsproj` *(register `CatalogDiff.fs`)*
- `/home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Targets.SSDT/Projection.Targets.SSDT.fsproj` *(register `RefactorLogEmitter.fs`, `RefactorLogRender.fs`)*
- `/home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Adapters.Osm/CatalogReader.fs` *(slice 5 — call `SsKey.ossysOriginal` from `parseRowsetBundle`)*
- `/home/user/outsystems-ddl-exporter/sidecar/projection/AXIOMS.md` *(A1 fourth amendment; T11 amendment for diff inputs; A35 new)*
- `/home/user/outsystems-ddl-exporter/sidecar/projection/DECISIONS.md` *(append entry: "RefactorLogEmitter as Π over CatalogDiff — chapter 3.5 close")*
- `/home/user/outsystems-ddl-exporter/sidecar/projection/HANDOFF.md` *(chapter-3 close handoff content)*
- `/home/user/outsystems-ddl-exporter/sidecar/projection/ADMIRE.md` *(no V1 component admired; entry "RefactorLogEmitter — V2-growth (V1 has no refactor.log)" )*

---

### Critical Files for Implementation

- /home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Core/CatalogDiff.fs (new)
- /home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Core/Identity.fs (extend)
- /home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Targets.SSDT/RefactorLogEmitter.fs (new)
- /home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Targets.SSDT/RefactorLogRender.fs (new)
- /home/user/outsystems-ddl-exporter/sidecar/projection/src/Projection.Adapters.Osm/CatalogReader.fs (extend for slice 5)
