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
        }

    /// Build the manifest from a Catalog. Per A18 amended: Catalog
    /// only, no Profile, no Policy. The Tables array is built by
    /// walking the catalog's Modules in declared order, then each
    /// module's Kinds in declared order; per A33 (deterministic-
    /// ordered schema emission), the input Catalog already arrived
    /// in canonical order via `CanonicalizeIdentity.run`.
    let build (catalog: Catalog) : Manifest =
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
        }

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
        // Chapter 4.4 territory — null until the operational diagnostics
        // chapter ships. Per V2-driver KPI smart-product-choices: emit
        // the field shape so the V1-compatible schema is preserved; the
        // semantic payload defers to its rightful chapter.
        doc.Add("coverage", null)
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
