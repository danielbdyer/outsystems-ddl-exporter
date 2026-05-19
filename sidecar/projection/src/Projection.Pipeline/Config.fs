namespace Projection.Pipeline

open System
open System.Text.Json
open Projection.Core

/// Unified config surface for the V2 cutover (`V2_PRODUCTION_CUTOVER.md` §5.1).
/// Operator hand-writes one JSON document; this module parses it into a typed
/// `Config` record with a `Result<Config>` return so structured errors flow
/// through the standard `pipeline.config.*` code namespace.
///
/// **D9 (secret-free by construction):** the type system carries no field
/// named or typed to hold a connection string, password, or access token.
/// The `parse` function additionally scans the JSON for property names
/// matching credential tokens and rejects them before structural parsing —
/// a defensive belt against operators who paste a connection string into
/// an unrelated section. Connection sources live outside this config
/// (env var or separate file referenced via CLI flag).
///
/// **D12 (canonical ordering):** consumers that depend on order (rename
/// application, migration-dependency PK assignment) MUST sort by a canonical
/// key before consumption. This module preserves declaration order from the
/// JSON; canonical sorts happen in the consuming pass.
///
/// Scope (this slice; cf. CLAUDE.md "IR grows under evidence"): the full
/// schema sketch lands as a typed record so the operator can hand-write a
/// complete config. Per-section semantic enrichment (mapping `policy.selection`
/// string → `SelectionPolicy` DU, etc.) defers until a downstream consumer
/// in `Compose.runWithConfig` wires it up. The Pipeline today threads
/// `Policy.empty`; this module's output flows into that surface in Phase A.1.
[<RequireQualifiedAccess>]
module Config =

    // -----------------------------------------------------------------------
    // Types — record-of-records mirroring V2_PRODUCTION_CUTOVER.md §5.1
    // -----------------------------------------------------------------------

    /// One module entry inside `model.modules`. A bare string selects the
    /// whole module; an object form (`{ "name": "M", "entities": [...] }`)
    /// restricts to a named entity subset within the module.
    type ModuleSelector =
        | Whole of name: string
        | WithEntities of name: string * entities: string list

    type ValidationOverrides = {
        AllowMissingPrimaryKey : string list
        AllowMissingSchema     : string list
    }

    type ModelSection = {
        Path                   : string
        Modules                : ModuleSelector list
        IncludeSystemModules   : bool
        IncludeInactiveModules : bool
        OnlyActiveAttributes   : bool
        ValidationOverrides    : ValidationOverrides
    }

    type ProfileSection = {
        Path : string option
    }

    type CacheSection = {
        Root       : string
        Refresh    : bool
        TtlSeconds : int
    }

    type ProfilerSection = {
        Provider   : string
        MockFolder : string option
    }

    type TypeMappingSection = {
        Path      : string option
        Default   : string option
        Overrides : Map<string, string>
    }

    type LogicalName = {
        Module : string
        Entity : string
    }

    type PhysicalName = {
        Schema : string
        Table  : string
    }

    /// `overrides.tableRenames[].from` accepts either a logical pair
    /// (`Module::Entity` via `{ module, entity }`) or a physical pair
    /// (`schema.table` via `{ schema, table }`). Both forms map to the
    /// same downstream rename pass.
    type RenameSource =
        | LogicalSource of LogicalName
        | PhysicalSource of PhysicalName

    type TableRename = {
        From : RenameSource
        To   : PhysicalName
    }

    type CircularDependencyEntry = {
        TableName : string
        Position  : int
    }

    type CircularDependencyCycle = {
        TableOrdering : CircularDependencyEntry list
    }

    type CircularDependenciesSection = {
        AllowedCycles : CircularDependencyCycle list
        StrictMode    : bool
    }

    type FilePathOverride = {
        Path : string
    }

    type OverridesSection = {
        TableRenames           : TableRename list
        MigrationDependencies  : FilePathOverride option
        StaticData             : FilePathOverride option
        CircularDependencies   : CircularDependenciesSection option
    }

    type EmissionSection = {
        Ssdt                  : bool
        Dacpac                : bool
        Json                  : bool
        Distributions         : bool
        StaticSeeds           : bool
        MigrationDependencies : bool
        Bootstrap             : bool
        DecisionLog           : bool
        Opportunities         : bool
        Validations           : bool
    }

    type UserMatchingSection = {
        Strategy : string
        Fallback : string
    }

    type PolicySection = {
        Selection    : string
        Insertion    : string
        UserMatching : UserMatchingSection
    }

    type OutputSection = {
        Dir : string
    }

    type Config = {
        Model        : ModelSection
        Profile      : ProfileSection
        Cache        : CacheSection
        Profiler     : ProfilerSection
        TypeMapping  : TypeMappingSection
        Overrides    : OverridesSection
        Emission     : EmissionSection
        Policy       : PolicySection
        Output       : OutputSection
    }

    // -----------------------------------------------------------------------
    // Defaults — applied when a section is absent from the JSON.
    // -----------------------------------------------------------------------

    let private defaultValidationOverrides : ValidationOverrides = {
        AllowMissingPrimaryKey = []
        AllowMissingSchema     = []
    }

    let private defaultProfile : ProfileSection = {
        Path = None
    }

    let private defaultCache : CacheSection = {
        Root       = ".artifacts/cache"
        Refresh    = false
        TtlSeconds = 7200
    }

    let private defaultProfiler : ProfilerSection = {
        Provider   = "fixture"
        MockFolder = None
    }

    let private defaultTypeMapping : TypeMappingSection = {
        Path      = None
        Default   = None
        Overrides = Map.empty
    }

    let private defaultOverrides : OverridesSection = {
        TableRenames          = []
        MigrationDependencies = None
        StaticData            = None
        CircularDependencies  = None
    }

    let private defaultEmission : EmissionSection = {
        Ssdt                  = true
        Dacpac                = true
        Json                  = true
        Distributions         = true
        StaticSeeds           = true
        MigrationDependencies = true
        Bootstrap             = true
        DecisionLog           = true
        Opportunities         = true
        Validations           = true
    }

    let private defaultUserMatching : UserMatchingSection = {
        Strategy = "ByEmail"
        Fallback = "NoFallback"
    }

    let private defaultPolicy : PolicySection = {
        Selection    = "IncludeAll"
        Insertion    = "SchemaOnly"
        UserMatching = defaultUserMatching
    }

    let private defaultOutput : OutputSection = {
        Dir = "out/"
    }

    // -----------------------------------------------------------------------
    // Error helpers — `pipeline.config.<problem>` dot-namespace.
    // -----------------------------------------------------------------------

    let private configError (code: string) (message: string) : ValidationError =
        ValidationError.create (sprintf "pipeline.config.%s" code) message

    // -----------------------------------------------------------------------
    // D9 guardrail — secret-free by construction.
    //
    // The type system enforces D9 structurally (no field accepts a
    // connection string or token). This pre-parse scan adds defense in
    // depth: a JSON document containing a property named like a credential
    // is rejected with a structured error pointing the operator at D9 /
    // §3.4.
    //
    // Matching is word-boundary aware so common English roots like
    // "secretary" or "passenger" don't false-positive. The property name
    // is tokenized into camelCase / snake_case / kebab-case words; a
    // credential signature is a non-empty word list that must appear as a
    // contiguous subsequence of those words. Single-word signatures
    // (`password`, `secret`) match only on full-word boundaries; compound
    // signatures (`connection` + `string`, `access` + `token`, `api` +
    // `key`) match contiguous pairs. The pre-joined forms
    // (`connectionstring`, `accesstoken`, `apikey`) handle the case where
    // an operator writes them as a single lowercase token.
    // -----------------------------------------------------------------------

    let private splitIdentifierWords (name: string) : string list =
        let sb = System.Text.StringBuilder()
        let words = ResizeArray<string>()
        let flush () =
            if sb.Length > 0 then
                words.Add(sb.ToString().ToLowerInvariant())
                sb.Clear() |> ignore
        let mutable prevLower = false
        for ch in name do
            if Char.IsLetterOrDigit(ch) then
                if prevLower && Char.IsUpper(ch) then
                    flush ()
                sb.Append(ch) |> ignore
                prevLower <- Char.IsLower(ch)
            else
                flush ()
                prevLower <- false
        flush ()
        List.ofSeq words

    let private credentialSignatures : (string list) list =
        [ [ "password" ]
          [ "passwd" ]
          [ "secret" ]
          [ "connection"; "string" ]
          [ "access"; "token" ]
          [ "api"; "key" ]
          [ "private"; "key" ]
          [ "client"; "secret" ]
          [ "connectionstring" ]
          [ "accesstoken" ]
          [ "apikey" ] ]

    let rec private startsWithWords (signature: string list) (words: string list) : bool =
        match signature, words with
        | [], _ -> true
        | _, [] -> false
        | s :: sRest, w :: wRest -> s = w && startsWithWords sRest wRest

    let rec private containsSignature (signature: string list) (words: string list) : bool =
        match words with
        | [] -> List.isEmpty signature
        | _  -> startsWithWords signature words || containsSignature signature (List.tail words)

    let private looksLikeCredentialName (name: string) : bool =
        let words = splitIdentifierWords name
        credentialSignatures |> List.exists (fun sg -> containsSignature sg words)

    let rec private scanForCredentials (path: string) (element: JsonElement) : ValidationError list =
        match element.ValueKind with
        | JsonValueKind.Object ->
            element.EnumerateObject()
            |> Seq.collect (fun prop ->
                let here =
                    if looksLikeCredentialName prop.Name then
                        let where = if path = "" then prop.Name else sprintf "%s.%s" path prop.Name
                        [ configError
                            "credentialPropertyForbidden"
                            (sprintf
                                "Property '%s' looks like a credential; the unified config is secret-free by construction (D9). Source credentials from environment variables or a separate non-checked-in file."
                                where) ]
                    else []
                let childPath = if path = "" then prop.Name else sprintf "%s.%s" path prop.Name
                here @ scanForCredentials childPath prop.Value)
            |> Seq.toList
        | JsonValueKind.Array ->
            element.EnumerateArray()
            |> Seq.mapi (fun i e -> scanForCredentials (sprintf "%s[%d]" path i) e)
            |> Seq.collect id
            |> Seq.toList
        | _ -> []

    // -----------------------------------------------------------------------
    // JSON helpers — light wrappers over System.Text.Json.JsonElement.
    // Mirror `CatalogReader`'s private helpers; kept module-private here.
    // -----------------------------------------------------------------------

    let private getProperty (element: JsonElement) (name: string) : Result<JsonElement> =
        match element.TryGetProperty(name) with
        | true, v -> Result.success v
        | _ ->
            Result.failureOf (
                configError "missingProperty" (sprintf "Required property '%s' not found." name))

    let private tryGetProperty (element: JsonElement) (name: string) : JsonElement option =
        match element.TryGetProperty(name) with
        | true, v ->
            match v.ValueKind with
            | JsonValueKind.Null | JsonValueKind.Undefined -> None
            | _ -> Some v
        | _ -> None

    let private getString (element: JsonElement) (name: string) : Result<string> =
        match getProperty element name with
        | Error es -> Error es
        | Ok v ->
            if v.ValueKind = JsonValueKind.String then
                match v.GetString() with
                | null ->
                    Result.failureOf (
                        configError "nullProperty" (sprintf "Property '%s' is null; expected a string." name))
                | s -> Result.success s
            else
                Result.failureOf (
                    configError "typeMismatch" (sprintf "Property '%s' is not a string." name))

    let private getOptionalString (element: JsonElement) (name: string) : Result<string option> =
        match element.TryGetProperty(name) with
        | false, _ -> Result.success None
        | true, v ->
            match v.ValueKind with
            | JsonValueKind.Null | JsonValueKind.Undefined -> Result.success None
            | JsonValueKind.String ->
                match v.GetString() with
                | null -> Result.success None
                | s -> Result.success (Some s)
            | _ ->
                Result.failureOf (
                    configError "typeMismatch" (sprintf "Property '%s' is not a string when present." name))

    let private getBoolOr (element: JsonElement) (name: string) (defaultValue: bool) : Result<bool> =
        match element.TryGetProperty(name) with
        | false, _ -> Result.success defaultValue
        | true, v ->
            match v.ValueKind with
            | JsonValueKind.True -> Result.success true
            | JsonValueKind.False -> Result.success false
            | JsonValueKind.Null | JsonValueKind.Undefined -> Result.success defaultValue
            | _ ->
                Result.failureOf (
                    configError "typeMismatch" (sprintf "Property '%s' is not a boolean." name))

    let private getIntOr (element: JsonElement) (name: string) (defaultValue: int) : Result<int> =
        match element.TryGetProperty(name) with
        | false, _ -> Result.success defaultValue
        | true, v ->
            match v.ValueKind with
            | JsonValueKind.Number ->
                match v.TryGetInt32() with
                | true, n -> Result.success n
                | _ ->
                    Result.failureOf (
                        configError "typeMismatch" (sprintf "Property '%s' is not an int32." name))
            | JsonValueKind.Null | JsonValueKind.Undefined -> Result.success defaultValue
            | _ ->
                Result.failureOf (
                    configError "typeMismatch" (sprintf "Property '%s' is not a number." name))

    let private getStringListOrEmpty (element: JsonElement) (name: string) : Result<string list> =
        match element.TryGetProperty(name) with
        | false, _ -> Result.success []
        | true, v ->
            match v.ValueKind with
            | JsonValueKind.Null | JsonValueKind.Undefined -> Result.success []
            | JsonValueKind.Array ->
                v.EnumerateArray()
                |> Seq.toList
                |> List.map (fun e ->
                    if e.ValueKind = JsonValueKind.String then
                        match e.GetString() with
                        | null ->
                            Result.failureOf (
                                configError "nullArrayElement" (sprintf "Array '%s' contains a null element." name))
                        | s -> Result.success s
                    else
                        Result.failureOf (
                            configError "typeMismatch" (sprintf "Array '%s' contains a non-string element." name)))
                |> Result.aggregate
            | _ ->
                Result.failureOf (
                    configError "typeMismatch" (sprintf "Property '%s' is not an array." name))

    // -----------------------------------------------------------------------
    // Per-section parsers
    // -----------------------------------------------------------------------

    let private parseModuleSelector (element: JsonElement) : Result<ModuleSelector> =
        match element.ValueKind with
        | JsonValueKind.String ->
            match element.GetString() with
            | null ->
                Result.failureOf (
                    configError "nullProperty" "model.modules entry is null.")
            | s -> Result.success (Whole s)
        | JsonValueKind.Object ->
            match getString element "name" with
            | Error es -> Error es
            | Ok name ->
                match getStringListOrEmpty element "entities" with
                | Error es -> Error es
                | Ok entities -> Result.success (WithEntities (name, entities))
        | _ ->
            Result.failureOf (
                configError "typeMismatch" "model.modules entry must be a string or an object.")

    let private parseModulesList (element: JsonElement) : Result<ModuleSelector list> =
        match element.TryGetProperty("modules") with
        | false, _ -> Result.success []
        | true, v ->
            match v.ValueKind with
            | JsonValueKind.Null | JsonValueKind.Undefined -> Result.success []
            | JsonValueKind.Array ->
                v.EnumerateArray()
                |> Seq.toList
                |> List.map parseModuleSelector
                |> Result.aggregate
            | _ ->
                Result.failureOf (
                    configError "typeMismatch" "model.modules must be an array.")

    let private parseValidationOverrides (element: JsonElement) : Result<ValidationOverrides> =
        match element.TryGetProperty("validationOverrides") with
        | false, _ -> Result.success defaultValidationOverrides
        | true, v ->
            match v.ValueKind with
            | JsonValueKind.Null | JsonValueKind.Undefined ->
                Result.success defaultValidationOverrides
            | JsonValueKind.Object ->
                match getStringListOrEmpty v "allowMissingPrimaryKey" with
                | Error es -> Error es
                | Ok pks ->
                    match getStringListOrEmpty v "allowMissingSchema" with
                    | Error es -> Error es
                    | Ok schemas ->
                        Result.success { AllowMissingPrimaryKey = pks; AllowMissingSchema = schemas }
            | _ ->
                Result.failureOf (
                    configError "typeMismatch" "model.validationOverrides must be an object.")

    let private parseModel (root: JsonElement) : Result<ModelSection> =
        match getProperty root "model" with
        | Error es -> Error es
        | Ok element ->
            match getString element "path" with
            | Error es -> Error es
            | Ok path ->
                match parseModulesList element with
                | Error es -> Error es
                | Ok modules ->
                    match getBoolOr element "includeSystemModules" false with
                    | Error es -> Error es
                    | Ok inclSys ->
                        match getBoolOr element "includeInactiveModules" false with
                        | Error es -> Error es
                        | Ok inclInactive ->
                            match getBoolOr element "onlyActiveAttributes" true with
                            | Error es -> Error es
                            | Ok onlyActive ->
                                match parseValidationOverrides element with
                                | Error es -> Error es
                                | Ok vo ->
                                    Result.success {
                                        Path                   = path
                                        Modules                = modules
                                        IncludeSystemModules   = inclSys
                                        IncludeInactiveModules = inclInactive
                                        OnlyActiveAttributes   = onlyActive
                                        ValidationOverrides    = vo
                                    }

    let private parseProfile (root: JsonElement) : Result<ProfileSection> =
        match tryGetProperty root "profile" with
        | None -> Result.success defaultProfile
        | Some element ->
            match getOptionalString element "path" with
            | Error es -> Error es
            | Ok path -> Result.success { Path = path }

    let private parseCache (root: JsonElement) : Result<CacheSection> =
        match tryGetProperty root "cache" with
        | None -> Result.success defaultCache
        | Some element ->
            let rootR =
                match getOptionalString element "root" with
                | Error es -> Error es
                | Ok None -> Result.success defaultCache.Root
                | Ok (Some s) -> Result.success s
            match rootR with
            | Error es -> Error es
            | Ok r ->
                match getBoolOr element "refresh" defaultCache.Refresh with
                | Error es -> Error es
                | Ok refresh ->
                    match getIntOr element "ttlSeconds" defaultCache.TtlSeconds with
                    | Error es -> Error es
                    | Ok ttl ->
                        Result.success { Root = r; Refresh = refresh; TtlSeconds = ttl }

    let private parseProfiler (root: JsonElement) : Result<ProfilerSection> =
        match tryGetProperty root "profiler" with
        | None -> Result.success defaultProfiler
        | Some element ->
            let providerR =
                match getOptionalString element "provider" with
                | Error es -> Error es
                | Ok None -> Result.success defaultProfiler.Provider
                | Ok (Some s) -> Result.success s
            match providerR with
            | Error es -> Error es
            | Ok provider ->
                match getOptionalString element "mockFolder" with
                | Error es -> Error es
                | Ok mockFolder ->
                    Result.success { Provider = provider; MockFolder = mockFolder }

    let private parseTypeMapping (root: JsonElement) : Result<TypeMappingSection> =
        match tryGetProperty root "typeMapping" with
        | None -> Result.success defaultTypeMapping
        | Some element ->
            match getOptionalString element "path" with
            | Error es -> Error es
            | Ok path ->
                match getOptionalString element "default" with
                | Error es -> Error es
                | Ok defaultRule ->
                    let overrides =
                        match element.TryGetProperty("overrides") with
                        | false, _ -> Map.empty
                        | true, v when v.ValueKind = JsonValueKind.Object ->
                            v.EnumerateObject()
                            |> Seq.choose (fun prop ->
                                if prop.Value.ValueKind = JsonValueKind.String then
                                    match prop.Value.GetString() with
                                    | null -> None
                                    | s -> Some (prop.Name, s)
                                else None)
                            |> Map.ofSeq
                        | _ -> Map.empty
                    Result.success { Path = path; Default = defaultRule; Overrides = overrides }

    let private parsePhysicalName (element: JsonElement) : Result<PhysicalName> =
        match getString element "schema" with
        | Error es -> Error es
        | Ok schema ->
            match getString element "table" with
            | Error es -> Error es
            | Ok table -> Result.success { Schema = schema; Table = table }

    let private parseLogicalName (element: JsonElement) : Result<LogicalName> =
        match getString element "module" with
        | Error es -> Error es
        | Ok m ->
            match getString element "entity" with
            | Error es -> Error es
            | Ok e -> Result.success { Module = m; Entity = e }

    let private parseRenameSource (element: JsonElement) : Result<RenameSource> =
        let hasModule = element.TryGetProperty("module") |> fst
        let hasSchema = element.TryGetProperty("schema") |> fst
        if hasModule && hasSchema then
            Result.failureOf (
                configError
                    "renameSourceAmbiguous"
                    "tableRenames[].from carries both 'module' and 'schema'; pick exactly one form.")
        elif hasModule then
            parseLogicalName element |> Result.map LogicalSource
        elif hasSchema then
            parsePhysicalName element |> Result.map PhysicalSource
        else
            Result.failureOf (
                configError
                    "renameSourceMissing"
                    "tableRenames[].from must carry either { module, entity } or { schema, table }.")

    let private parseTableRename (element: JsonElement) : Result<TableRename> =
        match getProperty element "from" with
        | Error es -> Error es
        | Ok fromElement ->
            match parseRenameSource fromElement with
            | Error es -> Error es
            | Ok source ->
                match getProperty element "to" with
                | Error es -> Error es
                | Ok toElement ->
                    match parsePhysicalName toElement with
                    | Error es -> Error es
                    | Ok target -> Result.success { From = source; To = target }

    let private parseTableRenames (element: JsonElement) : Result<TableRename list> =
        match element.TryGetProperty("tableRenames") with
        | false, _ -> Result.success []
        | true, v ->
            match v.ValueKind with
            | JsonValueKind.Null | JsonValueKind.Undefined -> Result.success []
            | JsonValueKind.Array ->
                v.EnumerateArray()
                |> Seq.toList
                |> List.map parseTableRename
                |> Result.aggregate
            | _ ->
                Result.failureOf (
                    configError "typeMismatch" "overrides.tableRenames must be an array.")

    let private parseFilePathOverride (element: JsonElement) : Result<FilePathOverride> =
        match getString element "path" with
        | Error es -> Error es
        | Ok p -> Result.success { Path = p }

    let private parseOptionalFilePathOverride (root: JsonElement) (key: string) : Result<FilePathOverride option> =
        match tryGetProperty root key with
        | None -> Result.success None
        | Some element ->
            parseFilePathOverride element |> Result.map Some

    let private parseCircularDependencyEntry (element: JsonElement) : Result<CircularDependencyEntry> =
        match getString element "tableName" with
        | Error es -> Error es
        | Ok t ->
            match getIntOr element "position" 0 with
            | Error es -> Error es
            | Ok p -> Result.success { TableName = t; Position = p }

    let private parseCircularDependencyCycle (element: JsonElement) : Result<CircularDependencyCycle> =
        match element.TryGetProperty("tableOrdering") with
        | false, _ -> Result.success { TableOrdering = [] }
        | true, v when v.ValueKind = JsonValueKind.Array ->
            v.EnumerateArray()
            |> Seq.toList
            |> List.map parseCircularDependencyEntry
            |> Result.aggregate
            |> Result.map (fun entries -> { TableOrdering = entries })
        | _ ->
            Result.failureOf (
                configError "typeMismatch" "circularDependencies.allowedCycles[].tableOrdering must be an array.")

    let private parseCircularDependencies (root: JsonElement) : Result<CircularDependenciesSection option> =
        match tryGetProperty root "circularDependencies" with
        | None -> Result.success None
        | Some element ->
            let cyclesR =
                match element.TryGetProperty("allowedCycles") with
                | false, _ -> Result.success []
                | true, v when v.ValueKind = JsonValueKind.Array ->
                    v.EnumerateArray()
                    |> Seq.toList
                    |> List.map parseCircularDependencyCycle
                    |> Result.aggregate
                | _ ->
                    Result.failureOf (
                        configError "typeMismatch" "circularDependencies.allowedCycles must be an array.")
            match cyclesR with
            | Error es -> Error es
            | Ok cycles ->
                match getBoolOr element "strictMode" false with
                | Error es -> Error es
                | Ok strict ->
                    Result.success (Some { AllowedCycles = cycles; StrictMode = strict })

    let private parseOverrides (root: JsonElement) : Result<OverridesSection> =
        match tryGetProperty root "overrides" with
        | None -> Result.success defaultOverrides
        | Some element ->
            match parseTableRenames element with
            | Error es -> Error es
            | Ok renames ->
                match parseOptionalFilePathOverride element "migrationDependencies" with
                | Error es -> Error es
                | Ok migDeps ->
                    match parseOptionalFilePathOverride element "staticData" with
                    | Error es -> Error es
                    | Ok staticData ->
                        match parseCircularDependencies element with
                        | Error es -> Error es
                        | Ok cycles ->
                            Result.success {
                                TableRenames          = renames
                                MigrationDependencies = migDeps
                                StaticData            = staticData
                                CircularDependencies  = cycles
                            }

    let private parseEmission (root: JsonElement) : Result<EmissionSection> =
        match tryGetProperty root "emission" with
        | None -> Result.success defaultEmission
        | Some element ->
            let read name (defaultValue: bool) = getBoolOr element name defaultValue
            match read "ssdt" defaultEmission.Ssdt with
            | Error es -> Error es
            | Ok ssdt ->
                match read "dacpac" defaultEmission.Dacpac with
                | Error es -> Error es
                | Ok dacpac ->
                    match read "json" defaultEmission.Json with
                    | Error es -> Error es
                    | Ok json ->
                        match read "distributions" defaultEmission.Distributions with
                        | Error es -> Error es
                        | Ok dist ->
                            match read "staticSeeds" defaultEmission.StaticSeeds with
                            | Error es -> Error es
                            | Ok seeds ->
                                match read "migrationDependencies" defaultEmission.MigrationDependencies with
                                | Error es -> Error es
                                | Ok migDeps ->
                                    match read "bootstrap" defaultEmission.Bootstrap with
                                    | Error es -> Error es
                                    | Ok boot ->
                                        match read "decisionLog" defaultEmission.DecisionLog with
                                        | Error es -> Error es
                                        | Ok dlog ->
                                            match read "opportunities" defaultEmission.Opportunities with
                                            | Error es -> Error es
                                            | Ok opps ->
                                                match read "validations" defaultEmission.Validations with
                                                | Error es -> Error es
                                                | Ok vals ->
                                                    Result.success {
                                                        Ssdt = ssdt
                                                        Dacpac = dacpac
                                                        Json = json
                                                        Distributions = dist
                                                        StaticSeeds = seeds
                                                        MigrationDependencies = migDeps
                                                        Bootstrap = boot
                                                        DecisionLog = dlog
                                                        Opportunities = opps
                                                        Validations = vals
                                                    }

    let private parseUserMatching (element: JsonElement) : Result<UserMatchingSection> =
        match element.TryGetProperty("userMatching") with
        | false, _ -> Result.success defaultUserMatching
        | true, v when v.ValueKind = JsonValueKind.Object ->
            let strategyR =
                match getOptionalString v "strategy" with
                | Error es -> Error es
                | Ok None -> Result.success defaultUserMatching.Strategy
                | Ok (Some s) -> Result.success s
            match strategyR with
            | Error es -> Error es
            | Ok strategy ->
                let fallbackR =
                    match getOptionalString v "fallback" with
                    | Error es -> Error es
                    | Ok None -> Result.success defaultUserMatching.Fallback
                    | Ok (Some s) -> Result.success s
                match fallbackR with
                | Error es -> Error es
                | Ok fallback ->
                    Result.success { Strategy = strategy; Fallback = fallback }
        | _ ->
            Result.failureOf (
                configError "typeMismatch" "policy.userMatching must be an object.")

    let private parsePolicy (root: JsonElement) : Result<PolicySection> =
        match tryGetProperty root "policy" with
        | None -> Result.success defaultPolicy
        | Some element ->
            let selectionR =
                match getOptionalString element "selection" with
                | Error es -> Error es
                | Ok None -> Result.success defaultPolicy.Selection
                | Ok (Some s) -> Result.success s
            match selectionR with
            | Error es -> Error es
            | Ok selection ->
                let insertionR =
                    match getOptionalString element "insertion" with
                    | Error es -> Error es
                    | Ok None -> Result.success defaultPolicy.Insertion
                    | Ok (Some s) -> Result.success s
                match insertionR with
                | Error es -> Error es
                | Ok insertion ->
                    match parseUserMatching element with
                    | Error es -> Error es
                    | Ok userMatching ->
                        Result.success {
                            Selection    = selection
                            Insertion    = insertion
                            UserMatching = userMatching
                        }

    let private parseOutput (root: JsonElement) : Result<OutputSection> =
        match tryGetProperty root "output" with
        | None -> Result.success defaultOutput
        | Some element ->
            match getOptionalString element "dir" with
            | Error es -> Error es
            | Ok None -> Result.success defaultOutput
            | Ok (Some d) -> Result.success { Dir = d }

    // -----------------------------------------------------------------------
    // Top-level parser
    // -----------------------------------------------------------------------

    let private parseRoot (root: JsonElement) : Result<Config> =
        if root.ValueKind <> JsonValueKind.Object then
            Result.failureOf (
                configError "typeMismatch" "Config root must be a JSON object.")
        else
            match parseModel root with
            | Error es -> Error es
            | Ok model ->
                match parseProfile root with
                | Error es -> Error es
                | Ok profile ->
                    match parseCache root with
                    | Error es -> Error es
                    | Ok cache ->
                        match parseProfiler root with
                        | Error es -> Error es
                        | Ok profiler ->
                            match parseTypeMapping root with
                            | Error es -> Error es
                            | Ok typeMapping ->
                                match parseOverrides root with
                                | Error es -> Error es
                                | Ok overrides ->
                                    match parseEmission root with
                                    | Error es -> Error es
                                    | Ok emission ->
                                        match parsePolicy root with
                                        | Error es -> Error es
                                        | Ok policy ->
                                            match parseOutput root with
                                            | Error es -> Error es
                                            | Ok output ->
                                                Result.success {
                                                    Model       = model
                                                    Profile     = profile
                                                    Cache       = cache
                                                    Profiler    = profiler
                                                    TypeMapping = typeMapping
                                                    Overrides   = overrides
                                                    Emission    = emission
                                                    Policy      = policy
                                                    Output      = output
                                                }

    /// Parse a JSON string into a typed `Config`. Order of operations:
    ///   1. Parse the JSON syntactically. Malformed JSON returns
    ///      `pipeline.config.jsonInvalid`.
    ///   2. Scan the entire document for property names that look like
    ///      credentials (D9 guardrail). Any hit returns
    ///      `pipeline.config.credentialPropertyForbidden`; the structural
    ///      parser is bypassed.
    ///   3. Parse each top-level section. Missing sections fall back to
    ///      typed defaults; missing required fields (`model.path`) error.
    ///      Type mismatches return `pipeline.config.typeMismatch`.
    ///
    /// Returns all accumulated errors (per `Result.aggregate` semantics
    /// in list-parsing helpers) — the operator sees every malformed entry
    /// in one pass, not just the first.
    let parse (json: string) : Result<Config> =
        try
            use document = JsonDocument.Parse(json)
            let root = document.RootElement
            match scanForCredentials "" root with
            | [] -> parseRoot root
            | errors -> Error errors
        with
        | :? JsonException as ex ->
            Result.failureOf (
                configError "jsonInvalid" (sprintf "Failed to parse JSON: %s" ex.Message))

    /// Read and parse a config file from disk. Layer thin on top of `parse`:
    /// surfaces `pipeline.config.fileNotFound` when the path is missing and
    /// `pipeline.config.fileReadError` when the file exists but cannot be
    /// read. Successful read flows into `parse` which produces all
    /// structural / D9 errors as `Result<Config>`.
    let fromFile (path: string) : Result<Config> =
        if not (System.IO.File.Exists path) then
            Result.failureOf (
                configError "fileNotFound" (sprintf "Config file not found: %s" path))
        else
            let readResult =
                try Ok (System.IO.File.ReadAllText path)
                with ex ->
                    Error [
                        configError
                            "fileReadError"
                            (sprintf "Failed to read config file '%s': %s" path ex.Message)
                    ]
            match readResult with
            | Error es -> Error es
            | Ok json -> parse json
