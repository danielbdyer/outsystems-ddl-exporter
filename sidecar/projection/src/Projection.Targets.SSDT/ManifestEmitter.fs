namespace Projection.Targets.SSDT

// LINT-ALLOW-FILE-MUTATION: BCL System.Text.Json.Nodes APIs require
//   mutation on JsonObject / JsonArray for typed JSON tree
//   construction (`obj.Add(key, value)`, `arr.Add(value)`). This is
//   the canonical BCL primitive (per pillar 4: OOP where BCL forces
//   it; pillar 7: gold-standard library precedence). The mutation is
//   reified at the file level; the typed JsonNode tree IS the
//   structural carrier through to the Utf8JsonWriter terminal.

open System.IO
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open Projection.Core

/// Π_Manifest — chapter 4.1.A slice 9 substantive deliverable. The
/// production-deployment SSDT manifest emitter (sibling Π) producing
/// `<outDir>/manifest.json` per V1 `SsdtManifest` schema (V1 source
/// `src/Osm.Emission/SsdtManifest.cs:6-14`; mirrored here per V1↔V2
/// ubiquitous-language commitment per pillar 8 + cherry-pick
/// discipline per `DECISIONS 2026-05-06`).
///
/// **V2-driver KPI Phase 2 (per `V2_DRIVER.md`).** The manifest is
/// part of the SSDT directory bundle V2 emits under V2-driver mode;
/// V1's existing pipeline expects it. Without it, V2's bundle isn't
/// SSDT-compatible.
///
/// **Per chapter pre-scope §8 slice 9 + V2-driver KPI smart-product-
/// choices:** v0 manifest carries the structural fields V2 currently
/// has evidence for (Tables array per kind; Emission stamp). Fields
/// that chapter 4.4 fills (Coverage / PredicateCoverage /
/// PreRemediation / Unsupported) emit explicitly as `null` / empty
/// arrays so the schema shape is V1-compatible while the semantic
/// payload defers to its rightful chapter. Per pillar 1: the typed
/// `Manifest` record IS the canonical form; the JSON text emerges
/// only at the absolute terminal `Utf8JsonWriter` boundary.
/// Slice α (chapter 4.4) — per-axis emit-vs-total breakdown.
/// Mirrors V1's `Osm.Emission.CoverageBreakdown`
/// (`SsdtManifest.cs:68-90`) including the percentage-rounding
/// contract (`Math.Round(value, 2, MidpointRounding.AwayFromZero)`).
///
/// Pillar-9 classification: DataIntent. Reachable from `Catalog`
/// alone without operator opinion; lands in the skeleton.
///
/// Pillar 8 four-question naming analysis. Concept-shaped: a
/// `CoverageBreakdown` IS the axis's emit-vs-total summary, not
/// a verb. Sibling vocabulary with V1's `CoverageBreakdown`.
type CoverageBreakdown = {
    /// Count of units the emitter emitted. Always ≤ `Total`
    /// (smart-constructor invariant).
    Emitted    : int
    /// Count of units in the catalog. Always ≥ 0.
    Total      : int
    /// `Emitted` as a percentage of `Total`, rounded to 2 decimal
    /// places (AwayFromZero, mirroring V1). Edge cases: `Total = 0`
    /// → 100m (vacuous full coverage); `Emitted = 0` with
    /// `Total > 0` → 0m.
    Percentage : decimal
}

[<RequireQualifiedAccess>]
module CoverageBreakdown =

    let private emittedNegative =
        ValidationError.create
            "coverage.emittedNegative"
            "Coverage Emitted count cannot be negative."
    let private totalNegative =
        ValidationError.create
            "coverage.totalNegative"
            "Coverage Total count cannot be negative."
    let private emittedExceedsTotal =
        ValidationError.create
            "coverage.emittedExceedsTotal"
            "Coverage Emitted count cannot exceed Total."

    let private computePercentage (emitted: int) (total: int) : decimal =
        if total <= 0 then 100m
        elif emitted <= 0 then 0m
        else
            let value = (decimal emitted) / (decimal total) * 100m
            System.Math.Round(value, 2, System.MidpointRounding.AwayFromZero)

    /// Smart constructor (A39). Enforces non-negative counts +
    /// `Emitted ≤ Total`. Percentage computed via V1's contract.
    let create (emitted: int) (total: int) : Result<CoverageBreakdown> =
        if emitted < 0 then Result.failureOf emittedNegative
        elif total < 0 then Result.failureOf totalNegative
        elif emitted > total then Result.failureOf emittedExceedsTotal
        else
            Result.success
                { Emitted    = emitted
                  Total      = total
                  Percentage = computePercentage emitted total }


/// Slice α (chapter 4.4) — per-axis coverage summary. Mirrors V1's
/// `Osm.Emission.SsdtCoverageSummary` (`SsdtManifest.cs:54-66`).
/// Three axes: Tables / Columns / Constraints. The constraint
/// breakdown counts the union (PKs + non-PK unique indexes + FKs +
/// CHECK constraints) of every structural constraint the emitter
/// renders for a kind.
type CoverageSummary = {
    Tables      : CoverageBreakdown
    Columns     : CoverageBreakdown
    Constraints : CoverageBreakdown
}

[<RequireQualifiedAccess>]
module CoverageSummary =

    /// Build a summary where every axis reports `Emitted = Total`
    /// (V2's emit-everything default; T11 keyset coverage holds
    /// structurally). Mirrors V1's
    /// `SsdtCoverageSummary.CreateComplete`.
    let createComplete (tables: int) (columns: int) (constraints: int)
        : Result<CoverageSummary> =
        match CoverageBreakdown.create tables tables,
              CoverageBreakdown.create columns columns,
              CoverageBreakdown.create constraints constraints with
        | Ok t, Ok c, Ok r ->
            Result.success { Tables = t; Columns = c; Constraints = r }
        | tablesR, columnsR, constraintsR ->
            // Aggregate errors across all three axes for diagnostic
            // completeness.
            [tablesR; columnsR; constraintsR]
            |> List.collect Result.errors
            |> Result.failure


/// Slice α (chapter 4.4) — `Catalog -> CoverageSummary` computation.
/// V2 emits every kind from the catalog (T11 keyset coverage); the
/// computation reduces to `CreateComplete` over per-axis catalog
/// counts. A18 amended preserved: Catalog only.
///
/// Forward signal: if a future `EmissionPolicy.Selection` axis
/// filters kinds, the emitted-vs-total split widens — `Coverage
/// .compute` then takes the emit-set as a second argument.
[<RequireQualifiedAccess>]
module Coverage =

    /// Count constraints attached to a kind for the manifest
    /// coverage axis. PK counts as 1 if the kind has any PK
    /// attributes (a kind has at most one primary key). Non-PK
    /// unique indexes + FK references + CHECK constraints each
    /// contribute their list length.
    let private constraintsOf (k: Kind) : int =
        let pkCount =
            if k.Attributes |> List.exists (fun a -> a.IsPrimaryKey) then 1
            else 0
        let uniqueIndexCount =
            k.Indexes
            |> List.filter (fun i -> not i.IsPrimaryKey && i.IsUnique)
            |> List.length
        let fkCount = List.length k.References
        let checkCount = List.length k.ColumnChecks
        pkCount + uniqueIndexCount + fkCount + checkCount

    /// Compute the coverage summary for a Catalog. Per A18 amended:
    /// Catalog only; no Policy, no Profile. Per T1 byte-determinism:
    /// pure function of the input; same input → same output.
    let compute (catalog: Catalog) : CoverageSummary =
        use _ = Bench.scope "emit.manifest.coverage"
        let allKinds =
            catalog.Modules |> List.collect (fun m -> m.Kinds)
        let tableCount = List.length allKinds
        let columnCount =
            allKinds |> List.sumBy (fun k -> List.length k.Attributes)
        let constraintCount =
            allKinds |> List.sumBy constraintsOf
        // Result.value safe here: counts are catalog-derived and
        // therefore non-negative; emitted = total by construction.
        CoverageSummary.createComplete tableCount columnCount constraintCount
        |> Result.value


[<RequireQualifiedAccess>]
module ManifestEmitter =

    [<Literal>]
    let version : int = 1

    /// Per-table manifest entry. Concept-shaped per pillar 8: the
    /// entry IS the table's per-kind summary in the manifest.
    /// Mirrors V1's `TableManifestEntry` (V1 source
    /// `SsdtManifest.cs:16-26`); fields V2 has evidence for ship
    /// directly; fields that depend on chapter 4.4 enrichment emit
    /// as defaults under V2-driver KPI smart-product-choices.
    type TableManifestEntry =
        {
            /// Owning module name (V1 convention: directory in
            /// `<outDir>/Modules/<Module>/`).
            Module : string
            /// SQL Server schema (typically `dbo`).
            Schema : string
            /// SQL Server table name (V1 physical name).
            Table : string
            /// Relative path of the per-table .sql file from
            /// `<outDir>` root. Forward-slash separators per V1
            /// convention (cross-platform deterministic per
            /// chapter pre-scope §6 + V2-driver KPI).
            TableFile : string
            /// Count of non-PK indexes emitted for this table.
            IndexCount : int
            /// Count of FK constraints emitted (inline) for this
            /// table.
            ForeignKeyCount : int
        }

    /// V2 manifest shape. Mirrors V1's `SsdtManifest`
    /// (`src/Osm.Emission/SsdtManifest.cs:6-14`) at the structural
    /// level; per chapter pre-scope §8 slice 9 + V2-driver KPI:
    /// Coverage / PredicateCoverage / PreRemediation / Unsupported
    /// emit as defaults until chapter 4.4 surfaces real evidence.
    type Manifest =
        {
            /// One entry per kind in the catalog (sorted by Module
            /// then by Schema/Table within the module). Determinism:
            /// the order is a function of the input Catalog.
            Tables : TableManifestEntry list
            /// Emitter-version stamp. Used by downstream consumers
            /// to detect manifest-schema changes; bump
            /// `ManifestEmitter.version` when the shape changes.
            EmitterVersion : int
            /// **Chapter A.4.7' slice ζ** — deterministic SHA256 digest
            /// of the registered-transforms metadata. Stable across
            /// emit calls when the registry is unchanged; changes when
            /// any Name / Domain / StageBinding / Sites / Status field
            /// changes. The 5th bidirectional property test
            /// (registry-digest round-trip) asserts both halves of the
            /// stability + perturbation contract.
            RegistryDigest : string
            /// **Chapter 4.4 slice α** — per-axis emit-vs-total
            /// summary (Tables / Columns / Constraints). Retires the
            /// `chapter 4.4 fills` deferral for the Coverage axis.
            /// V2 emits every kind in the catalog (T11 keyset
            /// coverage); the value is `CreateComplete` over the
            /// catalog's counts.
            Coverage : CoverageSummary
        }

    /// Build the manifest from a Catalog + explicit registry-metadata
    /// list. The `registry` parameter feeds the slice-ζ
    /// `RegistryDigest` field; production callers use
    /// `RegisteredTransforms.all`; the 5th bidirectional property test
    /// supplies a perturbed list to exercise digest sensitivity.
    /// Per A18 amended: Catalog only, no Profile, no Policy.
    let buildWith (registry: RegisteredTransformMetadata list) (catalog: Catalog) : Manifest =
        use _ = Bench.scope "emit.manifest.build"
        let entries =
            catalog.Modules
            |> List.collect (fun m ->
                m.Kinds
                |> List.map (fun k ->
                    let nonPkIndexCount =
                        k.Indexes
                        |> List.filter (fun idx -> not idx.IsPrimaryKey)
                        |> List.length
                    {
                        Module = Name.value m.Name
                        Schema = k.Physical.Schema
                        Table = k.Physical.Table
                        // Same shape as SsdtDdlEmitter.relativePath (V1
                        // convention; forward slashes for cross-platform
                        // determinism per V2-driver KPI).
                        TableFile =
                            System.String.Concat(  // LINT-ALLOW: cross-platform-deterministic relative path; Path.Combine considered + rejected (platform-specific separators violate T1 byte-determinism); segments are typed (Name.value m.Name + k.Physical.Schema + k.Physical.Table from Coordinates.TableId)
                                "Modules/",
                                Name.value m.Name,
                                "/",
                                k.Physical.Schema,
                                ".",
                                k.Physical.Table,
                                ".sql")
                        IndexCount = nonPkIndexCount
                        ForeignKeyCount = List.length k.References
                    }))
        {
            Tables = entries
            EmitterVersion = version
            RegistryDigest = TransformRegistry.digest registry
            Coverage = Coverage.compute catalog
        }

    /// Build with the canonical production registry
    /// (`RegisteredTransforms.all`). Preserves the existing emit
    /// signature; production callers continue to use this form.
    let build (catalog: Catalog) : Manifest =
        buildWith RegisteredTransforms.all catalog

    let private requireValue (label: string) (v: JsonValue | null) : JsonNode =
        match Option.ofObj v with
        | Some node -> node :> JsonNode
        | None ->
            invalidOp (sprintf "ManifestEmitter.%s: JsonValue.Create returned null (unreachable for non-null input)" label)

    /// Render the manifest to a typed `JsonNode`. Per pillar 1: the
    /// typed JsonNode IS the canonical intermediate form; the JSON
    /// text emerges only at `toJson`. Per pillar 7: `JsonObject` /
    /// `JsonArray` / `JsonValue.Create` are the BCL gold-standard
    /// primitives for typed JSON tree construction.
    let toNode (manifest: Manifest) : JsonNode =
        let doc = JsonObject()
        doc.Add("emitter", requireValue "emitter" (JsonValue.Create("Projection.Targets.SSDT.ManifestEmitter")))
        doc.Add("version", requireValue "version" (JsonValue.Create(manifest.EmitterVersion)))
        // Chapter A.4.7' slice ζ — load-bearing per A41 (registry as
        // execution-totality surface). Stable across emit calls when
        // the registry is unchanged; perturbation surfaces here.
        let registryObj = JsonObject()
        registryObj.Add("digest", requireValue "registry.digest" (JsonValue.Create(manifest.RegistryDigest)))
        doc.Add("registry", registryObj :> JsonNode)
        let tablesArr = JsonArray()
        for entry in manifest.Tables do
            let entryObj = JsonObject()
            entryObj.Add("module", requireValue "module" (JsonValue.Create(entry.Module)))
            entryObj.Add("schema", requireValue "schema" (JsonValue.Create(entry.Schema)))
            entryObj.Add("table", requireValue "table" (JsonValue.Create(entry.Table)))
            entryObj.Add("tableFile", requireValue "tableFile" (JsonValue.Create(entry.TableFile)))
            entryObj.Add("indexCount", requireValue "indexCount" (JsonValue.Create(entry.IndexCount)))
            entryObj.Add("foreignKeyCount", requireValue "foreignKeyCount" (JsonValue.Create(entry.ForeignKeyCount)))
            tablesArr.Add(entryObj)
        doc.Add("tables", tablesArr)
        // Chapter 4.4 slice α — per-axis Coverage breakdown.
        // Retires the prior `null` default for this field. Mirrors V1's
        // `SsdtCoverageSummary` shape (Tables / Columns / Constraints
        // each with Emitted / Total / Percentage).
        let buildBreakdown (b: CoverageBreakdown) : JsonNode =
            let obj = JsonObject()
            obj.Add("emitted", requireValue "emitted" (JsonValue.Create(b.Emitted)))
            obj.Add("total", requireValue "total" (JsonValue.Create(b.Total)))
            obj.Add("percentage", requireValue "percentage" (JsonValue.Create(b.Percentage)))
            obj :> JsonNode
        let coverageObj = JsonObject()
        coverageObj.Add("tables", buildBreakdown manifest.Coverage.Tables)
        coverageObj.Add("columns", buildBreakdown manifest.Coverage.Columns)
        coverageObj.Add("constraints", buildBreakdown manifest.Coverage.Constraints)
        doc.Add("coverage", coverageObj :> JsonNode)
        // Chapter 4.4 slice β/γ territory — null/empty pending slices.
        // PreRemediation stays empty-array per V2_DRIVER §154
        // (RemediationEmitter deferred to chapter 5+).
        doc.Add("predicateCoverage", null)
        doc.Add("preRemediation", JsonArray() :> JsonNode)
        doc.Add("unsupported", JsonArray() :> JsonNode)
        doc :> JsonNode

    /// Render the manifest to JSON text. The terminal serialization
    /// flows through `Utf8JsonWriter` with `JsonOptions.indented()`
    /// (the chapter-3.6 reified option-builder + chapter-3.7 slice ε
    /// JsonEmitter precedent). T1 byte-determinism: pinned options
    /// guarantee byte-identical output across repeat invocations.
    let toJson (manifest: Manifest) : string =
        use _ = Bench.scope "emit.manifest.toJson"
        let doc = toNode manifest
        use stream = new MemoryStream()
        do
            use writer = new Utf8JsonWriter(stream, (JsonOptions.indented ()))
            doc.WriteTo(writer)
            writer.Flush()
        Encoding.UTF8.GetString(stream.ToArray())

    /// Π port realization (chapter 4.1.A slice 9). Per A18 amended:
    /// Catalog only, no Profile, no Policy. Returns the typed
    /// `Manifest` value; downstream realization (`Render
    /// .toSsdtDirectory` per slice 10) emits the manifest.json file
    /// alongside the per-table .sql files.
    let emit : Catalog -> Manifest = build
