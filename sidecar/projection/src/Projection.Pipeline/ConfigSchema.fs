namespace Projection.Pipeline

// LINT-ALLOW-FILE-MUTATION: BCL System.Text.Json.Nodes DOM construction — the
//   sanctioned typed-JSON path for schema emission (the artifact-builder
//   precedent); no string-assembled JSON anywhere in this file.

open System.Text.Json
open System.Text.Json.Nodes
open Projection.Core

/// The GENERATED JSON Schema for `projection.json`'s shaping namespaces — the
/// editor-validation projection of `Config.parse` (T-IV: the more of the
/// operator surface that is generated, the less of it can lie).
///
/// **Why generated, and why the enums derive from code.** The config shape was
/// stated in more places than were checked — a signoff-mode refusal hint went
/// stale across five hand-maintained statements of one list, and no test parsed
/// the shipped samples — so this schema is emitted from code, its closed
/// vocabularies pulled from the SAME enumerations the parser accepts
/// (`WriteSignoff.allModes` × `modeLabel`, `TransformGroup.all` ×
/// `configName`), and a drift test regenerates it byte-for-byte against the
/// committed `projection.schema.json`. A vocabulary the schema advertises and
/// the parser refuses (or vice versa) is therefore unrepresentable for the
/// derived enums and a failing test for the rest.
///
/// **Scope.** The strict shaping namespaces this repo's `CONFIG_REFERENCE.md`
/// documents (`model` / `overrides` / `emission` / `policy` / `profiler` /
/// `output`), at the granularity the parser enforces: closed vocabularies as
/// `enum`s, required keys as `required`, everything the parser ignores left
/// open (`additionalProperties: true` — unknown keys are ignored by contract,
/// and the movement namespaces ride the same file). Operators reference it as
/// `"$schema": "./projection.schema.json"` for as-you-type validation.
[<RequireQualifiedAccess>]
module ConfigSchema =

    // -- tiny typed-DOM helpers (schema-shaped, not general JSON) ----------

    let private str (desc: string) : JsonObject =
        let o = JsonObject()
        o["type"] <- JsonValue.Create "string"
        if desc <> "" then o["description"] <- JsonValue.Create desc
        o

    let private boolean (desc: string) : JsonObject =
        let o = JsonObject()
        o["type"] <- JsonValue.Create "boolean"
        if desc <> "" then o["description"] <- JsonValue.Create desc
        o

    let private integer (desc: string) : JsonObject =
        let o = JsonObject()
        o["type"] <- JsonValue.Create "integer"
        if desc <> "" then o["description"] <- JsonValue.Create desc
        o

    let private number (desc: string) : JsonObject =
        let o = JsonObject()
        o["type"] <- JsonValue.Create "number"
        if desc <> "" then o["description"] <- JsonValue.Create desc
        o

    let private enumOf (desc: string) (values: string list) : JsonObject =
        let o = JsonObject()
        o["type"] <- JsonValue.Create "string"
        let e = JsonArray()
        for v in values do e.Add(JsonValue.Create v)
        o["enum"] <- e
        if desc <> "" then o["description"] <- JsonValue.Create desc
        o

    let private arrayOf (desc: string) (items: JsonObject) : JsonObject =
        let o = JsonObject()
        o["type"] <- JsonValue.Create "array"
        o["items"] <- items
        if desc <> "" then o["description"] <- JsonValue.Create desc
        o

    let private objectOf (desc: string) (required: string list) (props: (string * JsonObject) list) : JsonObject =
        let o = JsonObject()
        o["type"] <- JsonValue.Create "object"
        if desc <> "" then o["description"] <- JsonValue.Create desc
        let p = JsonObject()
        for (name, node) in props do p[name] <- node
        o["properties"] <- p
        if not (List.isEmpty required) then
            let r = JsonArray()
            for name in required do r.Add(JsonValue.Create name)
            o["required"] <- r
        o["additionalProperties"] <- JsonValue.Create true
        o

    let private oneOf (desc: string) (variants: JsonObject list) : JsonObject =
        let o = JsonObject()
        let arr = JsonArray()
        for v in variants do arr.Add(v)
        o["oneOf"] <- arr
        if desc <> "" then o["description"] <- JsonValue.Create desc
        o

    // -- the coordinate shapes -------------------------------------------

    let private logicalEntity (desc: string) =
        objectOf desc [ "module"; "entity" ]
            [ "module", str "logical module name (espace-safe)"
              "entity", str "logical entity name" ]

    let private stagingSide (desc: string) =
        objectOf desc [ "key"; "identity" ]
            [ "module", str "logical module (with `entity`; espace-safe — preferred)"
              "entity", str "logical entity (with `module`)"
              "schema", str "physical schema (with `table`; the alternative form)"
              "table", str "physical table (with `schema`)"
              "key", str "the key attribute (logical name)"
              "identity", str "the identity attribute (logical name)" ]

    // -- the document ------------------------------------------------------

    /// The whole schema as a typed DOM. Enums that have a code owner are
    /// DERIVED, never restated.
    let private document () : JsonObject =
        let signoffModes = WriteSignoff.allModes |> List.map WriteSignoff.modeLabel
        let groupNames = TransformGroup.all |> List.map TransformGroup.configName
        let root = JsonObject()
        root["$schema"] <- JsonValue.Create "http://json-schema.org/draft-07/schema#"
        root["title"] <- JsonValue.Create "projection.json — shaping namespaces"
        root["description"] <- JsonValue.Create
            "GENERATED by ConfigSchema.generate (do not edit): the editor-validation projection of Config.parse. Closed vocabularies derive from the same code enumerations the parser accepts; a drift test byte-compares this file against regeneration. Unknown keys are ignored by the parser, so additionalProperties stays true throughout (the movement namespaces — environments/flows/readiness — ride the same file)."
        root["type"] <- JsonValue.Create "object"
        let props = JsonObject()
        props["model"] <-
            objectOf "the model source + scope (one of env/ossys/path)" []
                [ "env", str "primary-environment reference (unified config)"
                  "ossys", str "live OSSYS connection reference (env:<VAR> / file:<path>) — never a literal"
                  "path", str "exported osm_model.json path (the fallback source)"
                  "modules", arrayOf "in-scope selector — bare module name or { name, entities } (bounds bridge-retarget DISCOVERY)"
                                (oneOf "" [ str ""; objectOf "" [ "name" ] [ "name", str ""; "entities", arrayOf "" (str "") ] ])
                  "includeSystemModules", boolean ""
                  "includeInactiveModules", boolean ""
                  "onlyActiveAttributes", boolean "" ]
        props["overrides"] <-
            objectOf "naming + structural directives (all optional; empty = byte-identical)" []
                [ "tableRenames", arrayOf "" (objectOf "" [ "from"; "to" ] [ "from", oneOf "logical OR physical (not both)" [ logicalEntity ""; objectOf "" [ "schema"; "table" ] [ "schema", str ""; "table", str "" ] ]
                                                                             "to", objectOf "" [ "schema"; "table" ] [ "schema", str ""; "table", str "" ] ])
                  "emissionFolders", arrayOf "" (objectOf "" [ "ref"; "folder" ] [ "ref", logicalEntity ""; "folder", str "forward-slash relative folder" ])
                  "allowMissingPrimaryKey", arrayOf "" (logicalEntity "")
                  "circularDependencies", objectOf "" [] [ "allowedCycles", arrayOf "" (objectOf "" [] [ "order", arrayOf "" (objectOf "" [ "module"; "entity"; "position" ] [ "module", str ""; "entity", str ""; "position", integer "" ]) ]) ]
                  "migrationDependencies", objectOf "the MigrationData lane's operator-curated row inventory" [ "path" ] [ "path", str "" ]
                  "staticData", objectOf "reserved — parsed but not consumed" [ "path" ] [ "path", str "" ]
                  "bridgeRetargets",
                      arrayOf "declared FK bridge retargets (opt-in + signoff-gated; fail-closed until evidence clears)"
                          (objectOf "" [ "id"; "reference"; "bridge" ]
                              [ "id", str "stable retarget id (evidence + artifacts key on it)"
                                "reference",
                                    oneOf "one explicit FK, or DISCOVERY of every reference targeting an entity"
                                        [ objectOf "explicit" [ "module"; "entity"; "relationship" ]
                                            [ "module", str ""; "entity", str ""; "relationship", str "the reference name" ]
                                          objectOf "discovery" [ "allReferencesTo" ]
                                            [ "allReferencesTo", logicalEntity "every reference in the resolved model scope targeting this entity" ] ]
                                "bridge", objectOf "" [ "module"; "entity"; "attribute" ]
                                            [ "module", str ""; "entity", str ""; "attribute", str "the bridge business key (needs a single-column UNIQUE index)" ] ])
                  "bridgeRetargetEvidence", objectOf "operator-authored DATA-half evidence file (the alternative to bridgeRowStaging)" [ "path" ] [ "path", str "" ]
                  "bridgeRowStaging",
                      arrayOf "dynamic bridge-row staging companions (derive supply + evidence from the LIVE estate)"
                          (objectOf "" [ "id"; "source"; "bridge" ]
                              [ "id", str "stable staging id (path-safe; artifacts + cache key on it)"
                                "scope", enumOf "referenced (default, minimal) | allSourceRows (every source row)" [ "referenced"; "allSourceRows" ]
                                "cache", enumOf "off (default) | auto (fingerprint-validated reuse) | pinned (reuse without probing)" [ "off"; "auto"; "pinned" ]
                                "source", stagingSide "the source table + its key/identity attributes"
                                "bridge", stagingSide "the bridge table + its key/identity attributes"
                                "insertMappings", arrayOf "bridge column <- source column, for INSERTED rows only" (objectOf "" [ "bridge"; "from" ] [ "bridge", str ""; "from", str "" ])
                                "insertConstants", arrayOf "mandatory bridge columns the source cannot supply (null = SQL NULL)" (objectOf "" [ "bridge" ] [ "bridge", str "" ]) ]) ]
        props["emission"] <-
            objectOf "what the bundle emits and how the data lanes write" []
                [ "ssdt", boolean ""; "dacpac", boolean ""; "sqlproj", boolean ""; "json", boolean ""; "distributions", boolean ""
                  "staticSeeds", boolean ""; "migrationDependencies", boolean ""; "bootstrap", boolean ""; "bootstrapAllData", boolean ""
                  "decisionLog", boolean ""; "opportunities", boolean ""; "validations", boolean ""; "includePlatformAutoIndexes", boolean ""
                  "renderConstraintsElegant", boolean ""; "renderDataElegant", boolean ""; "identityAnnotations", boolean ""
                  "dataVerification", enumOf "" [ "standard"; "validateBeforeApply" ]
                  "signoff",
                      arrayOf "standing greenlights for destructive/authorized modes"
                          (objectOf "" []
                              [ "mode", enumOf "the write mode being greenlit (DERIVED from WriteSignoff.allModes)" signoffModes
                                "tables", arrayOf "" (str "")
                                "acknowledgedImpact", str ""
                                "approvedBy", str ""
                                "date", str "" ]) ]
        // align-II.2 — the ruling attribution an override row may carry.
        // Optional everywhere; `approvedBy` anchors the attribution (the
        // binder refuses attribution fields without an approver, a
        // malformed `approvedAt`, and an unknown `finding` key — each by
        // name).
        // (A function, not a shared list — JsonNode values are single-parent,
        // so each attaching object mints fresh nodes.)
        let provenanceFields () =
            [ "approvedBy", str "who approved this override (the attribution anchor)"
              "approvedAt", str "when (ISO-8601 round-trip form; parsed fail-closed)"
              "rationale", str "why — carried onto the estate posture un-severed"
              "finding", str "the finding key this override answers (<kind-token>:<subject>)" ]
        props["policy"] <-
            objectOf "tightening + transform-group toggles" []
                [ "insertion", str "e.g. SchemaOnly"
                  "tightening",
                      objectOf "operator tightening interventions (align-II.2: override rows carry optional ruling attribution)" []
                          [ "interventions",
                                arrayOf "one entry per intervention kind"
                                    (objectOf "" [ "kind"; "id" ]
                                        ([ "kind", enumOf "" [ "nullability"; "uniqueIndex"; "foreignKey"; "categoricalUniqueness" ]
                                           "id", str "stable intervention id"
                                           "nullBudget", number "nullability: permitted null fraction [0,1]"
                                           "allowMandatoryRelaxation", boolean "nullability"
                                           "overrides",
                                               arrayOf "nullability per-attribute overrides"
                                                   (objectOf "" [ "attributeRef"; "action" ]
                                                       ([ "attributeRef", str "Module.Entity.Attribute or Schema.Table.Column"
                                                          "action", enumOf "" [ "keepNullable" ] ] @ provenanceFields ()))
                                           "enforceSingleColumnUnique", boolean "uniqueIndex"
                                           "enforceMultiColumnUnique", boolean "uniqueIndex"
                                           "applyUniquePromotions", boolean "uniqueIndex: apply (true) vs advise-only (default)"
                                           "indexOverrides",
                                               arrayOf "uniqueIndex per-index promotion rulings (consulted before the blanket flag; align-II.3)"
                                                   (objectOf "" [ "indexRef"; "action" ]
                                                       ([ "indexRef", str "Module.Entity.IndexName"
                                                          "action", enumOf "" [ "adoptPromotion"; "refusePromotion" ] ] @ provenanceFields ()))
                                           "enableCreation", boolean "foreignKey"
                                           "allowCrossSchema", boolean "foreignKey"
                                           "allowNoCheckCreation", boolean "foreignKey"
                                           "referenceOverrides",
                                               arrayOf "foreignKey per-reference overrides"
                                                   (objectOf "" [ "referenceRef"; "action" ]
                                                       ([ "referenceRef", str "the anchoring attribute (logical or physical form)"
                                                          "action", enumOf "" [ "keepUntracked" ] ] @ provenanceFields ()))
                                           "minDistinctCountForUniqueness", integer "categoricalUniqueness" ])) ]
                  "transformGroups",
                      arrayOf "opt-in/out of registered pass groups"
                          (objectOf "" [ "name"; "enabled" ]
                              [ "name", enumOf "DERIVED from TransformGroup.all" groupNames
                                "enabled", boolean "" ]) ]
        props["profiler"] <-
            objectOf "" []
                [ "provider", enumOf "" [ "live"; "fixture" ]
                  "maxConcurrency", integer "" ]
        // The sink freshness section (the data-sink chapter, S8). The policy
        // enum DERIVES from `Config.SinkPolicy.all` — the same list the
        // parser accepts — so schema and parser cannot drift (A44).
        let sinkPolicies = Config.SinkPolicy.all |> List.map Config.SinkPolicy.label
        props["sink"] <-
            let o =
                objectOf "the sink freshness posture (reuse axis only — witnessing is gated by store presence, never by policy)" []
                    [ "policy", enumOf "off (default: every model read pays the wire) | auto (reuse the witnessed state while the ossys fingerprints match) | pinned (reuse without probing; --refresh overrides)" sinkPolicies ]
            let perEnv = JsonObject()
            perEnv["type"] <- JsonValue.Create "object"
            perEnv["description"] <- JsonValue.Create "per-environment policy refinements, keyed by the label `projection sync <env>` stamped"
            perEnv["additionalProperties"] <- enumOf "" sinkPolicies
            (match o["properties"] with
             | :? JsonObject as p -> p["perEnvironment"] <- perEnv
             | _ -> ())
            o
        props["output"] <- objectOf "" [ "dir" ] [ "dir", str "where emitted artifacts land" ]
        root["properties"] <- props
        root["additionalProperties"] <- JsonValue.Create true
        root

    let private jsonOpts = JsonSerializerOptions(WriteIndented = true)

    /// The schema text — regenerate-and-compare is the drift test's contract,
    /// so the rendering is deterministic (fixed property order, indented).
    let generate () : string =
        (document ()).ToJsonString(jsonOpts)
