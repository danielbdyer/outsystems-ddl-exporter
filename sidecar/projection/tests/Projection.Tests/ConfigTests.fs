module Projection.Tests.ConfigTests

open System
open Xunit
open Projection.Core
open Projection.Pipeline

// -----------------------------------------------------------------------
// Tests for `Projection.Pipeline.Config` — the unified config parser.
//
// Cite V2_PRODUCTION_CUTOVER.md decisions where they govern behavior:
//   D9  — secret-free by construction (no connection strings in config)
//   D12 — canonical ordering applied by consumers, not by parser
//   Q1  — auto-PK observes its own dataset; profile MAX(Id) optional
// -----------------------------------------------------------------------

let private mustOk (r: Result<'a>) : 'a =
    match r with
    | Ok v -> v
    | Error es ->
        let codes = es |> List.map (fun e -> e.Code) |> String.concat ", "
        failwithf "Expected Ok, got Error(s): %s" codes

let private mustFail (r: Result<'a>) : ValidationError list =
    match r with
    | Ok _ -> failwith "Expected Error, got Ok."
    | Error es -> es

let private hasCode (code: string) (errors: ValidationError list) : bool =
    errors |> List.exists (fun e -> e.Code = code)

// -----------------------------------------------------------------------
// Wave-3 slice 3.4 — `emission.tolerance`: the per-run accepted-divergence
// set (the R6 equivalence-up-to-quotient), now WIRED. FAIL-CLOSED parse: an
// unrecognized token is a named error, never a silent widening of the
// accepted set (which would corrupt the canary's quotient semantics).
// -----------------------------------------------------------------------

let private withTolerance (tokensJson: string) : string =
    sprintf """{ "model": { "path": "model.json" }, "emission": { "tolerance": %s } }""" tokensJson

[<Fact>]
let ``Config.parse: emission.tolerance parses known divergence tokens into the configured set`` () =
    let cfg = Config.parse (withTolerance """["EmptyTextNormalizedToNull", "DecimalScaleTolerated"]""") |> mustOk
    match cfg.Emission.Tolerance with
    | None -> failwith "expected Some tolerance"
    | Some t ->
        Assert.True(Tolerance.tolerates ToleratedDivergence.EmptyTextNormalizedToNull t)
        Assert.True(Tolerance.tolerates ToleratedDivergence.DecimalScaleTolerated t)
        Assert.False(Tolerance.tolerates ToleratedDivergence.HeaderCommentsOmitted t)

[<Fact>]
let ``Config.parse: emission.tolerance with an unknown token fails FAIL-CLOSED (never silently widens)`` () =
    let errors = Config.parse (withTolerance """["NotARealDivergence"]""") |> mustFail
    Assert.True(
        errors |> List.exists (fun e -> e.Message.Contains "NotARealDivergence" || e.Code.Contains "invalidValue"),
        sprintf "expected a fail-closed unknown-token error; got %A" (errors |> List.map (fun e -> e.Code)))

[<Fact>]
let ``Config.parse: emission.tolerance absent is None (permissive dual-track default applies downstream)`` () =
    let cfg = Config.parse """{ "model": { "path": "model.json" } }""" |> mustOk
    Assert.True(Option.isNone cfg.Emission.Tolerance)

[<Fact>]
let ``Config.parse: emission.tolerance empty array is strict (accept nothing)`` () =
    let cfg = Config.parse (withTolerance "[]") |> mustOk
    match cfg.Emission.Tolerance with
    | Some t -> Assert.True(Tolerance.isStrict t)
    | None -> failwith "expected Some strict tolerance for []"

[<Fact>]
let ``Config.parse: emission.tolerance non-array fails with a typeMismatch`` () =
    let errors = Config.parse (withTolerance "\"EmptyTextNormalizedToNull\"") |> mustFail
    Assert.True(
        errors |> List.exists (fun e -> e.Code.Contains "typeMismatch" || e.Message.Contains "must be an array"),
        sprintf "expected a typeMismatch for a non-array tolerance; got %A" (errors |> List.map (fun e -> e.Code)))

[<Fact>]
let ``Config.parse: accumulates errors across multiple malformed sections (not just the first)`` () =
    // `emission` (tolerance non-array) AND `policy` (transformGroups non-array)
    // are both malformed. `emission` is parsed before `policy`, so the prior
    // hand-threaded `match | Error es -> Error es` ladder short-circuited at
    // `emission` and never reached `policy`. The applicative `validation` parse
    // surfaces BOTH — fulfilling `parse`'s "every malformed entry in one pass"
    // docstring promise. The transformGroups error's presence is the proof that
    // parsing continued past the failing `emission` section.
    let json = """{ "model": { "path": "m" }, "emission": { "tolerance": "x" }, "policy": { "transformGroups": "y" } }"""
    let errors = Config.parse json |> mustFail
    Assert.True(
        errors.Length >= 2,
        sprintf "expected >=2 accumulated errors across sections; got %A" (errors |> List.map (fun e -> e.Message)))
    Assert.True(
        errors |> List.exists (fun e -> e.Message.Contains "transformGroups"),
        sprintf "expected the policy.transformGroups error (proves parsing continued past the failed emission section); got %A"
            (errors |> List.map (fun e -> e.Message)))

// -----------------------------------------------------------------------
// 2026-06-25 — `emission.dataStaging`: the large-kind staging posture
// (`auto`/`inline`/`tempTable` + threshold). Absent ⇒ `auto` > 1000 (the
// byte-identical default); FAIL-CLOSED on an unknown mode / threshold < 1.
// -----------------------------------------------------------------------

let private withDataStaging (objJson: string) : string =
    sprintf """{ "model": { "path": "model.json" }, "emission": { "dataStaging": %s } }""" objJson

[<Fact>]
let ``Config.parse: emission.dataStaging absent is the auto default (threshold 1000)`` () =
    let cfg = Config.parse """{ "model": { "path": "model.json" } }""" |> mustOk
    Assert.Equal(DataStagingMode.Auto, cfg.Emission.DataStaging.Mode)
    Assert.Equal(1000, cfg.Emission.DataStaging.Threshold)
    Assert.Equal(100000, cfg.Emission.DataStaging.IndexThreshold)   // the measured default index floor

[<Fact>]
let ``Config.parse: emission.dataStaging parses indexThreshold`` () =
    let cfg = Config.parse (withDataStaging """{ "indexThreshold": 250000 }""") |> mustOk
    Assert.Equal(250000, cfg.Emission.DataStaging.IndexThreshold)
    Assert.Equal(1000, cfg.Emission.DataStaging.Threshold)          // unset → default

[<Fact>]
let ``Config.parse: emission.dataStaging indexThreshold below 1 fails`` () =
    let errors = Config.parse (withDataStaging """{ "indexThreshold": 0 }""") |> mustFail
    Assert.True(
        errors |> List.exists (fun e -> e.Code.Contains "invalidValue" || e.Message.Contains ">= 1"),
        sprintf "expected an invalidValue for indexThreshold < 1; got %A" (errors |> List.map (fun e -> e.Code)))

[<Fact>]
let ``Config.parse: emission.dataStaging parses mode + threshold`` () =
    let cfg = Config.parse (withDataStaging """{ "mode": "inline", "threshold": 5000 }""") |> mustOk
    Assert.Equal(DataStagingMode.Inline, cfg.Emission.DataStaging.Mode)
    Assert.Equal(5000, cfg.Emission.DataStaging.Threshold)

[<Fact>]
let ``Config.parse: emission.dataStaging tempTable mode parses (threshold defaults to 1000)`` () =
    let cfg = Config.parse (withDataStaging """{ "mode": "tempTable" }""") |> mustOk
    Assert.Equal(DataStagingMode.TempTable, cfg.Emission.DataStaging.Mode)
    Assert.Equal(1000, cfg.Emission.DataStaging.Threshold)

[<Fact>]
let ``Config.parse: emission.dataStaging unknown mode fails FAIL-CLOSED`` () =
    let errors = Config.parse (withDataStaging """{ "mode": "bulkCopy" }""") |> mustFail
    Assert.True(
        errors |> List.exists (fun e -> e.Code.Contains "invalidValue" || e.Message.Contains "bulkCopy"),
        sprintf "expected an invalidValue for an unknown mode; got %A" (errors |> List.map (fun e -> e.Code)))

[<Fact>]
let ``Config.parse: emission.dataStaging threshold below 1 fails`` () =
    let errors = Config.parse (withDataStaging """{ "mode": "auto", "threshold": 0 }""") |> mustFail
    Assert.True(
        errors |> List.exists (fun e -> e.Code.Contains "invalidValue" || e.Message.Contains ">= 1"),
        sprintf "expected an invalidValue for threshold < 1; got %A" (errors |> List.map (fun e -> e.Code)))

[<Fact>]
let ``Config.parse: emission.dataStaging non-object fails with a typeMismatch`` () =
    let errors = Config.parse (withDataStaging "\"auto\"") |> mustFail
    Assert.True(
        errors |> List.exists (fun e -> e.Code.Contains "typeMismatch" || e.Message.Contains "must be an object"),
        sprintf "expected a typeMismatch for a non-object dataStaging; got %A" (errors |> List.map (fun e -> e.Code)))

// -----------------------------------------------------------------------
// Minimal valid config: only model.path is structurally required.
// -----------------------------------------------------------------------

[<Fact>]
let ``Config.parse: model.ossys is read as the live-OSSYS primary; absent is None`` () =
    let withOssys = Config.parse """{ "model": { "path": "model.json", "ossys": "env:ONPREM_OSSYS_CONN" } }""" |> mustOk
    Assert.Equal(Some "env:ONPREM_OSSYS_CONN", withOssys.Model.Ossys)
    let withoutOssys = Config.parse """{ "model": { "path": "model.json" } }""" |> mustOk
    Assert.Equal(None, withoutOssys.Model.Ossys)

[<Fact>]
let ``Config.parse: minimal config with only model.path succeeds`` () =
    let json = """{ "model": { "path": "model.json" } }"""
    let cfg = Config.parse json |> mustOk
    Assert.Equal(Some "model.json", cfg.Model.Path)
    Assert.Empty(cfg.Model.Modules)
    Assert.False(cfg.Model.IncludeSystemModules)
    Assert.False(cfg.Model.IncludeInactiveModules)
    Assert.True(cfg.Model.OnlyActiveAttributes)
    Assert.Equal("out/", cfg.Output.Dir)
    // Emission defaults: every gate open EXCEPT dacpac — the compiled
    // package is operator opt-in (`emission.dacpac: true`), so the default
    // bundle stays byte-identical to the pre-wire output.
    Assert.True(cfg.Emission.Ssdt)
    Assert.False(cfg.Emission.Dacpac)
    Assert.True(cfg.Emission.Validations)
    // Profile path is None when section absent
    Assert.True(cfg.Profile.Path.IsNone)

[<Fact>]
let ``Config.parse: model with only ossys (no path) succeeds — path is optional`` () =
    let cfg = Config.parse """{ "model": { "ossys": "env:ONPREM_OSSYS_CONN" } }""" |> mustOk
    Assert.Equal(None, cfg.Model.Path)
    Assert.Equal(Some "env:ONPREM_OSSYS_CONN", cfg.Model.Ossys)

[<Fact>]
let ``Config.parse: model with neither path nor ossys fails with structured error`` () =
    let json = """{ "model": {} }"""
    let errors = Config.parse json |> mustFail
    Assert.True(hasCode "pipeline.config.modelNoSource" errors)

[<Fact>]
let ``Config.parse: missing model section fails with structured error`` () =
    let json = """{ "output": { "dir": "out/" } }"""
    let errors = Config.parse json |> mustFail
    Assert.True(hasCode "pipeline.config.missingProperty" errors)

[<Fact>]
let ``Config.parse: malformed JSON fails with jsonInvalid code`` () =
    let json = """{ "model": { "path":"""
    let errors = Config.parse json |> mustFail
    Assert.True(hasCode "pipeline.config.jsonInvalid" errors)

// -----------------------------------------------------------------------
// D9: secret-free by construction. Any property name that looks like a
// credential (connectionString / password / accessToken / secret / apikey)
// is rejected at parse time, regardless of where it appears in the
// document.
// -----------------------------------------------------------------------

[<Fact>]
let ``D9: connectionString anywhere in the JSON is rejected`` () =
    let json = """{
        "model": { "path": "model.json" },
        "sql": { "connectionString": "Server=evil;User Id=root;Password=hunter2" }
    }"""
    let errors = Config.parse json |> mustFail
    Assert.True(hasCode "pipeline.config.credentialPropertyForbidden" errors)

[<Fact>]
let ``D9: password property is rejected`` () =
    let json = """{
        "model": { "path": "model.json" },
        "auth": { "password": "hunter2" }
    }"""
    let errors = Config.parse json |> mustFail
    Assert.True(hasCode "pipeline.config.credentialPropertyForbidden" errors)

[<Fact>]
let ``D9: accessToken property is rejected (case-insensitive)`` () =
    let json = """{
        "model": { "path": "model.json" },
        "secrets": { "AccessToken": "ya29.xxx" }
    }"""
    let errors = Config.parse json |> mustFail
    Assert.True(hasCode "pipeline.config.credentialPropertyForbidden" errors)

[<Fact>]
let ``D9: credential property nested in an array element is rejected`` () =
    let json = """{
        "model": { "path": "model.json" },
        "secrets": [ { "apiKey": "..." } ]
    }"""
    let errors = Config.parse json |> mustFail
    Assert.True(hasCode "pipeline.config.credentialPropertyForbidden" errors)

[<Fact>]
let ``D9: similar-but-not-credential property names are not rejected`` () =
    // "password" → reject; "passenger" / "secretary" / "tokenize" → allow.
    // Normalization strips non-alphanumerics and lowercases; substring
    // match against the credential token list. "passenger" lowercased
    // contains "pass" but NOT "password", so should pass.
    let json = """{
        "model": { "path": "model.json" },
        "metadata": {
            "passenger": "data",
            "secretary": "data",
            "tokenize": "data"
        }
    }"""
    let cfg = Config.parse json |> mustOk
    Assert.Equal(Some "model.json", cfg.Model.Path)

// -----------------------------------------------------------------------
// Full schema sketch round-trip.
// -----------------------------------------------------------------------

let private fullConfigJson = """{
    "model": {
        "path": "extracted/osm_model.json",
        "modules": [
            "AppCore",
            { "name": "ServiceCenter", "entities": [ "User", "Organization" ] }
        ],
        "includeSystemModules": false,
        "includeInactiveModules": false,
        "onlyActiveAttributes": true
    },
    "profile": { "path": "extracted/profile.json" },
    "profiler": { "provider": "fixture" },
    "overrides": {
        "tableRenames": [
            { "from": { "module": "Old", "entity": "OldE" }, "to": { "schema": "dbo", "table": "NEW_T" } },
            { "from": { "schema": "dbo", "table": "OSUSR_X_Y" }, "to": { "schema": "dbo", "table": "RENAMED" } }
        ],
        "migrationDependencies": { "path": "overrides/mig.json" },
        "staticData":            { "path": "overrides/static.json" },
        "circularDependencies": {
            "allowedCycles": [
                { "order": [
                    { "module": "AppCore", "entity": "Organization", "position": 100 },
                    { "module": "AppCore", "entity": "User",         "position": 200 }
                ] }
            ],
            "strictMode": false
        },
        "allowMissingPrimaryKey": [
            { "module": "AppCore", "entity": "LegacyAuditLog" }
        ],
        "emissionFolders": [
            { "ref": { "module": "AppCore", "entity": "User" }, "folder": "Static/Reference" },
            { "ref": { "module": "AppCore", "entity": "Organization" }, "folder": "Static/Tenant" }
        ]
    },
    "dynamicData": {
        "insertMode": "PerEntity",
        "staticSeedParentMode": "Include",
        "deferJunctionTables": false
    },
    "emission": {
        "ssdt": true, "dacpac": true, "json": true,
        "distributions": true, "staticSeeds": true,
        "migrationDependencies": true, "bootstrap": true,
        "decisionLog": true, "opportunities": true, "validations": true
    },
    "policy": {
        "insertion": "SchemaOnly"
    },
    "output": { "dir": "out/" }
}"""

[<Fact>]
let ``Config.parse: full schema sketch parses without errors`` () =
    let cfg = Config.parse fullConfigJson |> mustOk
    Assert.Equal(Some "extracted/osm_model.json", cfg.Model.Path)
    Assert.Equal(2, cfg.Model.Modules.Length)
    match cfg.Model.Modules.[0] with
    | Config.Whole name -> Assert.Equal("AppCore", name)
    | Config.WithEntities _ -> failwith "first module should be whole"
    match cfg.Model.Modules.[1] with
    | Config.WithEntities (name, entities) ->
        Assert.Equal("ServiceCenter", name)
        Assert.Equal<string list>([ "User"; "Organization" ], entities)
    | Config.Whole _ -> failwith "second module should be entity-restricted"

[<Fact>]
let ``Config.parse: tableRenames accepts both logical and physical source forms`` () =
    let cfg = Config.parse fullConfigJson |> mustOk
    let renames = cfg.Overrides.TableRenames
    Assert.Equal(2, renames.Length)
    match renames.[0].From with
    | Config.LogicalSource ln ->
        Assert.Equal("Old",  ln.Module)
        Assert.Equal("OldE", ln.Entity)
    | Config.PhysicalSource _ -> failwith "first rename should be logical"
    match renames.[1].From with
    | Config.PhysicalSource pn ->
        Assert.Equal("dbo",        pn.Schema)
        Assert.Equal("OSUSR_X_Y",  pn.Table)
    | Config.LogicalSource _ -> failwith "second rename should be physical"
    Assert.Equal("NEW_T",   renames.[0].To.Table)
    Assert.Equal("RENAMED", renames.[1].To.Table)

[<Fact>]
let ``Config.parse: tableRenames rejects ambiguous source (module + schema)`` () =
    let json = """{
        "model": { "path": "model.json" },
        "overrides": {
            "tableRenames": [
                { "from": { "module": "M", "entity": "E", "schema": "dbo", "table": "T" },
                  "to":   { "schema": "dbo", "table": "U" } }
            ]
        }
    }"""
    let errors = Config.parse json |> mustFail
    Assert.True(hasCode "pipeline.config.renameSourceAmbiguous" errors)

[<Fact>]
let ``Config.parse: tableRenames rejects empty source (neither module nor schema)`` () =
    let json = """{
        "model": { "path": "model.json" },
        "overrides": {
            "tableRenames": [
                { "from": { }, "to": { "schema": "dbo", "table": "U" } }
            ]
        }
    }"""
    let errors = Config.parse json |> mustFail
    Assert.True(hasCode "pipeline.config.renameSourceMissing" errors)

[<Fact>]
let ``Config.parse: overrides.migrationDependencies and staticData carry distinct paths`` () =
    let cfg = Config.parse fullConfigJson |> mustOk
    Assert.True(cfg.Overrides.MigrationDependencies.IsSome)
    Assert.True(cfg.Overrides.StaticData.IsSome)
    Assert.Equal("overrides/mig.json",    cfg.Overrides.MigrationDependencies.Value.Path)
    Assert.Equal("overrides/static.json", cfg.Overrides.StaticData.Value.Path)

[<Fact>]
let ``Config.parse: overrides.emissionFolders round-trips ref + folder fields`` () =
    let cfg = Config.parse fullConfigJson |> mustOk
    let folders = cfg.Overrides.EmissionFolders
    Assert.Equal(2, folders.Length)
    Assert.Equal("AppCore",          folders.[0].Ref.Module)
    Assert.Equal("User",             folders.[0].Ref.Entity)
    Assert.Equal("Static/Reference", folders.[0].Folder)
    Assert.Equal("AppCore",          folders.[1].Ref.Module)
    Assert.Equal("Organization",     folders.[1].Ref.Entity)
    Assert.Equal("Static/Tenant",    folders.[1].Folder)

[<Fact>]
let ``Config.parse: emissionFolders entry missing ref surfaces structured error`` () =
    let json = """{
        "model": { "path": "m.json" },
        "overrides": {
            "emissionFolders": [ { "folder": "Static" } ]
        }
    }"""
    let errors = Config.parse json |> mustFail
    Assert.True(hasCode "pipeline.config.missingProperty" errors)

[<Fact>]
let ``Config.parse: emissionFolders entry missing folder surfaces structured error`` () =
    let json = """{
        "model": { "path": "m.json" },
        "overrides": {
            "emissionFolders": [ { "ref": { "module": "M", "entity": "E" } } ]
        }
    }"""
    let errors = Config.parse json |> mustFail
    Assert.True(hasCode "pipeline.config.missingProperty" errors)

[<Fact>]
let ``Config.parse: emissionFolders non-array shape surfaces structured error`` () =
    let json = """{
        "model": { "path": "m.json" },
        "overrides": {
            "emissionFolders": "not-an-array"
        }
    }"""
    let errors = Config.parse json |> mustFail
    Assert.True(hasCode "pipeline.config.typeMismatch" errors)

[<Fact>]
let ``Config.parse: emissionFolders absent yields empty list`` () =
    let json = """{ "model": { "path": "m.json" } }"""
    let cfg = Config.parse json |> mustOk
    Assert.Empty(cfg.Overrides.EmissionFolders)

[<Fact>]
let ``Config.parse: emission gates round-trip as booleans`` () =
    let json = """{
        "model": { "path": "m.json" },
        "emission": {
            "ssdt": false, "dacpac": false, "json": true,
            "staticSeeds": false, "migrationDependencies": true,
            "decisionLog": false, "validations": true
        }
    }"""
    let cfg = Config.parse json |> mustOk
    Assert.False(cfg.Emission.Ssdt)
    Assert.False(cfg.Emission.Dacpac)
    Assert.True(cfg.Emission.Json)
    Assert.False(cfg.Emission.StaticSeeds)
    Assert.True(cfg.Emission.MigrationDependencies)
    Assert.False(cfg.Emission.DecisionLog)
    Assert.True(cfg.Emission.Validations)
    // Unspecified emission keys keep their defaults (all-true)
    Assert.True(cfg.Emission.Distributions)
    Assert.True(cfg.Emission.Bootstrap)
    Assert.True(cfg.Emission.Opportunities)

[<Fact>]
let ``NM-70: Config.parse: emission.identityAnnotations defaults true (emit) when absent`` () =
    let cfg = Config.parse """{ "model": { "path": "m.json" } }""" |> mustOk
    Assert.True(cfg.Emission.EmitIdentityAnnotations)

[<Fact>]
let ``NM-70: Config.parse: emission.identityAnnotations false parses (the named downgrade)`` () =
    let json = """{ "model": { "path": "m.json" }, "emission": { "identityAnnotations": false } }"""
    let cfg = Config.parse json |> mustOk
    Assert.False(cfg.Emission.EmitIdentityAnnotations)

[<Fact>]
let ``NM-73: Config.parse: emission.dataVerification defaults Standard when absent`` () =
    let cfg = Config.parse """{ "model": { "path": "m.json" } }""" |> mustOk
    Assert.Equal(DataVerification.Standard, cfg.Emission.DataVerification)

[<Fact>]
let ``NM-73: Config.parse: emission.dataVerification "validateBeforeApply" parses (the conservative override)`` () =
    let json = """{ "model": { "path": "m.json" }, "emission": { "dataVerification": "validateBeforeApply" } }"""
    let cfg = Config.parse json |> mustOk
    Assert.Equal(DataVerification.ValidateBeforeApply, cfg.Emission.DataVerification)

[<Fact>]
let ``NM-73: Config.parse: an unrecognised emission.dataVerification is a loud error, not a silent fallback`` () =
    let json = """{ "model": { "path": "m.json" }, "emission": { "dataVerification": "yolo" } }"""
    match Config.parse json with
    | Ok _ -> Assert.Fail("expected a config error for an invalid dataVerification value")
    | Error _ -> ()

// -----------------------------------------------------------------------
// AC-D7 / AC-G4 — emission.deleteScope (the convergent-delete gate).
// -----------------------------------------------------------------------

[<Fact>]
let ``Config.parse: emission.deleteScope terms parse (string + number values)`` () =
    let json = """{
        "model": { "path": "m.json" },
        "emission": { "deleteScope": { "terms": [
            { "column": "TENANT_ID", "value": 42 },
            { "column": "REGION", "value": "emea" }
        ] } }
    }"""
    let cfg = Config.parse json |> mustOk
    match cfg.Emission.DeleteScope with
    | Some scope ->
        Assert.Equal<DeleteScopeTerm list>(
            [ { Column = (ColumnName.create "TENANT_ID" |> Result.value); Value = "42" }; { Column = (ColumnName.create "REGION" |> Result.value); Value = "emea" } ],
            scope.Terms)
    | None -> Assert.Fail "expected the parsed delete scope"

[<Fact>]
let ``Config.parse: emission.deleteScope absent is None (upsert-only default)`` () =
    let cfg = Config.parse """{ "model": { "path": "m.json" } }""" |> mustOk
    Assert.Equal(None, cfg.Emission.DeleteScope)

[<Fact>]
let ``Config.parse: emission.deleteScope with empty terms is refused, named`` () =
    let json = """{ "model": { "path": "m.json" }, "emission": { "deleteScope": { "terms": [] } } }"""
    let errors = Config.parse json |> mustFail
    Assert.True(hasCode "config.emission.deleteScope.empty" errors)

[<Fact>]
let ``Config.parse: emission.deleteScope term without a column is refused, named`` () =
    let json = """{ "model": { "path": "m.json" }, "emission": { "deleteScope": { "terms": [ { "value": 7 } ] } } }"""
    let errors = Config.parse json |> mustFail
    Assert.True(hasCode "config.emission.deleteScope.termShape" errors)

// -----------------------------------------------------------------------
// Type-mismatch surfacing.
// -----------------------------------------------------------------------

[<Fact>]
let ``Config.parse: model.path of wrong type fails typeMismatch`` () =
    let json = """{ "model": { "path": 42 } }"""
    let errors = Config.parse json |> mustFail
    Assert.True(hasCode "pipeline.config.typeMismatch" errors)

[<Fact>]
let ``Config.parse: emission.ssdt of wrong type fails typeMismatch`` () =
    let json = """{ "model": { "path": "m.json" }, "emission": { "ssdt": "yes" } }"""
    let errors = Config.parse json |> mustFail
    Assert.True(hasCode "pipeline.config.typeMismatch" errors)

// -----------------------------------------------------------------------
// Forward compatibility: unknown top-level properties are ignored.
// -----------------------------------------------------------------------

[<Fact>]
let ``Config.parse: unknown top-level property is tolerated`` () =
    let json = """{
        "model": { "path": "m.json" },
        "futureUnknownSection": { "flag": true }
    }"""
    let cfg = Config.parse json |> mustOk
    Assert.Equal(Some "m.json", cfg.Model.Path)

// -----------------------------------------------------------------------
// Defaults: sections absent from the JSON receive typed defaults.
// -----------------------------------------------------------------------

[<Fact>]
let ``Config.parse: defaults applied when sections are absent`` () =
    let json = """{ "model": { "path": "m.json" } }"""
    let cfg = Config.parse json |> mustOk
    Assert.Equal(Config.ProfilerProvider.Fixture, cfg.Profiler.Provider)
    Assert.Equal("SchemaOnly", cfg.Policy.Insertion)

// -----------------------------------------------------------------------
// profiler.provider — the closed ProfilerProvider DU. "live"/"fixture"
// parse to their variants; an unrecognized token is a NAMED refusal, not a
// silent downgrade to fixture (a typo'd provider must not silently disable
// live profiling — the named-refusal discipline).
// -----------------------------------------------------------------------

[<Fact>]
let ``Config.parse: profiler.provider "live" parses to ProfilerProvider.Live`` () =
    let json = """{ "model": { "path": "m.json" }, "profiler": { "provider": "live" } }"""
    let cfg = Config.parse json |> mustOk
    Assert.Equal(Config.ProfilerProvider.Live, cfg.Profiler.Provider)

[<Fact>]
let ``Config.parse: profiler.provider unknown token is a named refusal (never silently fixture)`` () =
    let json = """{ "model": { "path": "m.json" }, "profiler": { "provider": "fixtuer" } }"""
    let errors = Config.parse json |> mustFail
    Assert.True(
        errors |> List.exists (fun e -> e.Code.Contains "profiler.provider.unknown"),
        sprintf "expected a profiler.provider.unknown refusal; got %A" (errors |> List.map (fun e -> e.Code)))

// -----------------------------------------------------------------------
// Config.fromFile — file I/O wrapper. A.1 CLI bridge ingests config via
// this entry point; thin testable layer over `parse`.
// -----------------------------------------------------------------------

let private withTempFile (contents: string) (action: string -> 'a) : 'a =
    let path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName() + ".json")
    System.IO.File.WriteAllText(path, contents)
    try action path
    finally
        if System.IO.File.Exists path then System.IO.File.Delete path

[<Fact>]
let ``Config.fromFile: valid file produces Ok with parsed Config`` () =
    let json = """{ "model": { "path": "model.json" }, "output": { "dir": "elsewhere/" } }"""
    let cfg =
        withTempFile json (fun path -> Config.fromFile path |> mustOk)
    Assert.Equal(Some "model.json", cfg.Model.Path)
    Assert.Equal("elsewhere/", cfg.Output.Dir)

[<Fact>]
let ``Config.fromFile: missing file produces fileNotFound`` () =
    let bogus = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "does-not-exist-" + System.IO.Path.GetRandomFileName())
    let errors = Config.fromFile bogus |> mustFail
    Assert.True(hasCode "pipeline.config.fileNotFound" errors)

[<Fact>]
let ``Config.fromFile: malformed JSON in file surfaces jsonInvalid`` () =
    let errors =
        withTempFile """{ "model": { "path":""" (fun path ->
            Config.fromFile path |> mustFail)
    Assert.True(hasCode "pipeline.config.jsonInvalid" errors)

[<Fact>]
let ``Config.fromFile: D9 violation in file surfaces credentialPropertyForbidden`` () =
    let json = """{ "model": { "path": "m.json" }, "sql": { "connectionString": "..." } }"""
    let errors =
        withTempFile json (fun path -> Config.fromFile path |> mustFail)
    Assert.True(hasCode "pipeline.config.credentialPropertyForbidden" errors)
