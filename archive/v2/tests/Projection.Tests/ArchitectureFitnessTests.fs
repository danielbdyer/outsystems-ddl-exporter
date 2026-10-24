module Projection.Tests.ArchitectureFitnessTests

// ---------------------------------------------------------------------------
// MOVE M15 (THE VECTOR, Wave 0 fitness) — architectural guardrails as
// in-assembly Facts.
//
// The house convention->fitness pattern (CapabilitySurvey.reconcileArchetype /
// CapabilityProfile.of) closes a law as: closed convention -> one total
// expansion site -> derived projection -> round-trip / reconciliation finding.
// Here the "convention" is the hexagonal layer DAG and the Emitter port; the
// "fitness" is reflection over the in-assembly truth (the loaded assemblies +
// the live registry), reconciled against the law derived from the actual
// `.fsproj` ProjectReference graph.
//
// These Facts REPLACE the off-CI grep guardrails:
//   - `scripts/lint-discipline.sh` Rules 20/21/22 (hexagonal-coupling, grep over
//     `open Projection.X`) become the reflected layer-dependency DAG (Fact 1) —
//     a STRONGER law: a grep sees only `open` statements, reflection sees the
//     actual IL assembly references, so a coupling introduced via a
//     fully-qualified type name (no `open`) — invisible to the grep — is caught.
//   - the emitter-port contract (Fact 2) and the migrate-leg registry coverage
//     (Fact 3) are reconciled against the live `RegisteredAllTransforms.all`.
//
// The test project references every Projection.* project transitively
// (Core / Adapters.* / Targets.* / Pipeline / Cli) and is reflection-exempt
// (`open System.Reflection` is banned in `src/` by lint Rule 17, not in
// `tests/`), so it is the one place these in-assembly Facts can live.
//
// HONEST CAP (the prompt's optional Fact 4 — an analyzer-walker unit test —
// is CUT, not silently dropped): `Projection.Analyzers` targets net8.0 and
// pins FSharp.Core 9.0.201 + FSharp.Compiler.Service 43.9.201 +
// FSharp.Analyzers.SDK 0.30.0 (see its `.fsproj` NU1608 note). The test
// project targets net9.0 and rides the SDK's implicit FSharp.Core (9.0.30x).
// Referencing the analyzer here would re-introduce exactly the FSharp.Core
// version conflict that `.fsproj` note exists to avoid, and the SDK's
// `walk`/`Context` surface is a net8 FCS type. The analyzer is instead
// exercised end-to-end by `scripts/run-analyzers.sh`, which this move
// PROMOTES to CI (`analyzers-projection.yml`). A net9-safe in-process walker
// test is left for a future move that unifies the analyzer's TFM/FSharp.Core
// with the test project (no cheap path today).
// ---------------------------------------------------------------------------

open System
open System.Reflection
open Xunit
open Projection.Core
open Projection.Pipeline

// ---------------------------------------------------------------------------
// Shared reflection helpers.
// ---------------------------------------------------------------------------

/// The LOGICAL project name (the graph identity, matching the `.fsproj` file
/// name) paired with the REAL IL assembly simple name to `Assembly.Load`.
/// These coincide for every project EXCEPT `Projection.Cli`, whose `.fsproj`
/// overrides `<AssemblyName>projection</AssemblyName>` (the shipped CLI binary
/// is `projection.dll`). Reflection surfaces exactly this kind of build-detail
/// drift — a grep over `open Projection.Cli` would never see it. Keeping the
/// two identities distinct is what lets the law speak in logical project names
/// while loading and normalizing against the real assembly names.
let private projectionAssemblies : (string * string) list =
    [ "Projection.Core",                           "Projection.Core"
      "Projection.Adapters.Osm",                   "Projection.Adapters.Osm"
      "Projection.Adapters.OssysSql",              "Projection.Adapters.OssysSql"
      "Projection.Adapters.Sql",                   "Projection.Adapters.Sql"
      "Projection.Targets.SSDT",                   "Projection.Targets.SSDT"
      "Projection.Targets.Data",                   "Projection.Targets.Data"
      "Projection.Targets.Json",                   "Projection.Targets.Json"
      "Projection.Targets.Distributions",          "Projection.Targets.Distributions"
      "Projection.Targets.OperationalDiagnostics", "Projection.Targets.OperationalDiagnostics"
      "Projection.Pipeline",                       "Projection.Pipeline"
      // The CLI's IL assembly simple name is `projection`, not `Projection.Cli`.
      "Projection.Cli",                            "projection" ]

let private projectionAssemblyNames : string list =
    projectionAssemblies |> List.map fst

/// Real-IL-simple-name -> logical-project-name (the inverse of the override
/// above), so reflected references and loaded assemblies both speak the graph's
/// logical vocabulary. The identity for all but the CLI; `projection` maps back
/// to `Projection.Cli`.
let private toLogicalName (ilSimpleName: string) : string =
    projectionAssemblies
    |> List.tryFind (fun (_, il) -> il = ilSimpleName)
    |> Option.map fst
    |> Option.defaultValue ilSimpleName

let private loadProjectionAssembly (logicalName: string) : Assembly =
    let ilName =
        projectionAssemblies
        |> List.tryFind (fun (logical, _) -> logical = logicalName)
        |> Option.map snd
        |> Option.defaultValue logicalName
    Assembly.Load(AssemblyName(ilName))

/// Is an IL assembly simple name one of OUR projection assemblies (in either
/// the `Projection.*` spelling or the CLI's `projection` override)?
let private isProjectionIlName (ilSimpleName: string) : bool =
    (ilSimpleName.StartsWith("Projection.", StringComparison.Ordinal))
    || (projectionAssemblies |> List.exists (fun (_, il) -> il = ilSimpleName))

/// The Projection.* assemblies a given assembly DIRECTLY references in IL,
/// reported in LOGICAL project names (`projection` normalized to
/// `Projection.Cli`). `GetReferencedAssemblies` is the linker's view — direct
/// references only; the compiler may elide an unused transitive reference,
/// which is why the law below is "reflected ⊆ declared transitive closure",
/// not equality.
let private referencedProjectionNames (asm: Assembly) : Set<string> =
    asm.GetReferencedAssemblies()
    |> Array.choose (fun an ->
        match an.Name with
        | null -> None
        | n when isProjectionIlName n -> Some (toLogicalName n)
        | _ -> None)
    |> Set.ofArray

// ---------------------------------------------------------------------------
// Fact 1 — the layer-dependency DAG (replaces lint Rules 20/21/22).
// ---------------------------------------------------------------------------

/// The DECLARED direct-dependency edges — read straight off each project's
/// `.fsproj` ProjectReference list (the single source of truth for the layer
/// graph). This map IS the law: derive the allowed edges from what the build
/// graph actually declares, so the Fact is precise rather than a hand-guessed
/// approximation. `Core -> {}` is the keystone: Core depends on nothing
/// outward (the pure-core spine).
let private declaredDirectEdges : Map<string, Set<string>> =
    [ "Projection.Core",                          Set.empty
      "Projection.Adapters.Osm",                  set [ "Projection.Core" ]
      "Projection.Adapters.OssysSql",             set [ "Projection.Core"; "Projection.Adapters.Osm" ]
      "Projection.Adapters.Sql",                  set [ "Projection.Core" ]
      "Projection.Targets.SSDT",                  set [ "Projection.Core" ]
      "Projection.Targets.Data",                  set [ "Projection.Core"; "Projection.Targets.SSDT" ]
      "Projection.Targets.Json",                  set [ "Projection.Core" ]
      "Projection.Targets.Distributions",         set [ "Projection.Core" ]
      "Projection.Targets.OperationalDiagnostics", set [ "Projection.Core" ]
      "Projection.Pipeline",
        set [ "Projection.Core"
              "Projection.Adapters.Osm"; "Projection.Adapters.OssysSql"; "Projection.Adapters.Sql"
              "Projection.Targets.SSDT"; "Projection.Targets.Json"; "Projection.Targets.Distributions"
              "Projection.Targets.Data"; "Projection.Targets.OperationalDiagnostics" ]
      "Projection.Cli",
        set [ "Projection.Core"; "Projection.Adapters.Sql"; "Projection.Pipeline"; "Projection.Targets.SSDT" ] ]
    |> Map.ofList

/// Transitive closure of the declared edges — the set of every project a given
/// project may LEGITIMATELY reference (directly or via a chain). A reflected IL
/// reference outside this set is a real layer violation (a regression).
let private allowedTransitive (root: string) : Set<string> =
    let rec walk (seen: Set<string>) (frontier: string list) : Set<string> =
        match frontier with
        | [] -> seen
        | x :: rest when Set.contains x seen -> walk seen rest
        | x :: rest ->
            let direct = Map.tryFind x declaredDirectEdges |> Option.defaultValue Set.empty
            walk (Set.add x seen) (rest @ (Set.toList direct))
    // Closure of the DEPENDENCIES of `root` (root itself excluded — a project
    // does not "reference itself").
    let directOfRoot = Map.tryFind root declaredDirectEdges |> Option.defaultValue Set.empty
    walk Set.empty (Set.toList directOfRoot)

[<Fact>]
let ``M15: the layer-dependency DAG holds (Core depends on nothing outward)`` () =
    // Each loaded assembly's actual IL Projection.* references must be a SUBSET
    // of the closure the declared `.fsproj` graph permits. This is the
    // reflected, stronger form of lint Rules 20/21/22: the grep sees only
    // `open` statements; `GetReferencedAssemblies` sees every real coupling,
    // including ones introduced by a fully-qualified name with no `open`.
    let violations =
        projectionAssemblyNames
        |> List.collect (fun name ->
            let asm = loadProjectionAssembly name
            let allowed = allowedTransitive name
            let actual = referencedProjectionNames asm
            Set.difference actual allowed
            |> Set.toList
            |> List.map (fun bad -> sprintf "%s -> %s" name bad))

    Assert.True(
        List.isEmpty violations,
        sprintf
            "Layer-DAG violation(s) — an assembly references a Projection.* assembly the declared .fsproj graph forbids: %s"
            (String.concat "; " violations))

[<Fact>]
let ``M15: Core references no outward Projection assembly (the pure-core keystone)`` () =
    // The keystone stated as its own Fact (the most load-bearing edge of the
    // DAG, and the strongest of lint Rule 20). Core is the sink of the
    // dependency graph: nothing it ships may reach an adapter / target /
    // pipeline / cli assembly.
    let core = loadProjectionAssembly "Projection.Core"
    let outward = referencedProjectionNames core
    Assert.True(
        Set.isEmpty outward,
        sprintf
            "Projection.Core must reference NO other Projection.* assembly (pure-core spine); found: %s"
            (outward |> Set.toList |> String.concat ", "))

[<Fact>]
let ``M15: no Targets assembly references Pipeline or Cli (horizontal siblings, lint Rule 21)`` () =
    // Targets are horizontal siblings under Core; none may reach UP into
    // Pipeline or Cli. (Targets.Data -> Targets.SSDT is a DECLARED sibling
    // edge — permitted by the .fsproj graph — so this Fact bans only the
    // upward Pipeline/Cli reach, exactly as lint Rule 21 does.)
    let forbidden = set [ "Projection.Pipeline"; "Projection.Cli" ]
    let violations =
        projectionAssemblyNames
        |> List.filter (fun n -> n.StartsWith("Projection.Targets.", StringComparison.Ordinal))
        |> List.collect (fun name ->
            let actual = referencedProjectionNames (loadProjectionAssembly name)
            Set.intersect actual forbidden
            |> Set.toList
            |> List.map (fun bad -> sprintf "%s -> %s" name bad))
    Assert.True(
        List.isEmpty violations,
        sprintf "Targets must not reference Pipeline/Cli; found: %s" (String.concat "; " violations))

[<Fact>]
let ``M15: no Adapters assembly references any Targets or Pipeline or Cli (lint Rule 22)`` () =
    // Adapters sit beside Targets under Core (raw rowset/JSON -> Catalog); none
    // may reach a Targets emitter, Pipeline, or Cli. (Adapters.OssysSql ->
    // Adapters.Osm is a DECLARED adapter edge — permitted — so this Fact bans
    // only the cross-plane reach into Targets/Pipeline/Cli, as lint Rule 22.)
    let isForbidden (n: string) =
        n.StartsWith("Projection.Targets.", StringComparison.Ordinal)
        || n = "Projection.Pipeline"
        || n = "Projection.Cli"
    let violations =
        projectionAssemblyNames
        |> List.filter (fun n -> n.StartsWith("Projection.Adapters.", StringComparison.Ordinal))
        |> List.collect (fun name ->
            referencedProjectionNames (loadProjectionAssembly name)
            |> Set.filter isForbidden
            |> Set.toList
            |> List.map (fun bad -> sprintf "%s -> %s" name bad))
    Assert.True(
        List.isEmpty violations,
        sprintf "Adapters must not reference Targets/Pipeline/Cli; found: %s" (String.concat "; " violations))

// ---------------------------------------------------------------------------
// Fact 2 — every shipped emitter realizes the Emitter port.
// ---------------------------------------------------------------------------
//
// `Emitter<'element>` (Projection.Core/Types.fs) is the port:
//   `Catalog -> Result<ArtifactByKind<'element>, EmitError>`.
// "Realizes the port" has two halves, both checked here:
//   (a) STATIC — the shipped emitter values are bound through the `Emitter<_>`
//       type abbreviation below. If any drifts off the port shape (a new
//       Profile/Diff parameter, a non-`Result` codomain), THIS FILE STOPS
//       COMPILING — a build-green failure of the witness, the strongest signal.
//   (b) RUNTIME — each port value, applied to the empty Catalog, yields the
//       port codomain `Result<ArtifactByKind<_>, EmitError>` (a real value,
//       Ok or Error, never a throw). A regression that made an emitter partial
//       on the empty Catalog (an unhandled exn) fails the assertion.

/// The shipped emitters that expose the canonical `Emitter<'element>` port
/// (Catalog-only, `Result<ArtifactByKind<_>, EmitError>` codomain). The
/// sibling-Π emitters (`DistributionsEmitter` = `EmitterWithProfile`,
/// `RefactorLogEmitter` = `EmitterOverDiff`, the Data-axis emitters whose
/// wider `Profile`/options arguments don't fit the bare port) are the
/// A18-amended variants — covered by Fact 3's registry-coverage law, not by
/// this bare-port realization. The static `: Emitter<_>` annotations are the
/// compile-time port-conformance proof.
let private ssdtPort : Emitter<Projection.Targets.SSDT.SsdtDdlEmitter.SsdtFile> =
    Projection.Targets.SSDT.SsdtDdlEmitter.emitSlices

let private jsonPort : Emitter<System.Text.Json.Nodes.JsonNode> =
    Projection.Targets.Json.JsonEmitter.emitSlices

[<Fact>]
let ``M15: every shipped emitter realizes the Emitter port`` () =
    let emptyCatalog : Catalog = { Modules = []; Sequences = [] }

    // The port values, erased to a uniform probe shape: each takes the empty
    // Catalog and must produce a `Result<ArtifactByKind<_>, EmitError>` (Ok or
    // Error). The codomain is asserted by matching the Result; an unhandled
    // throw escapes and fails the Fact.
    let probeOk (name: string) (run: Catalog -> bool) =
        // `run` collapses the port result to a bool only AFTER confirming the
        // codomain is a real `Result` value (the match is total over Result).
        Assert.True(run emptyCatalog, sprintf "emitter '%s' did not return the Emitter-port codomain" name)

    probeOk "ssdtDdlEmitter" (fun c ->
        match ssdtPort c with
        | Ok _ | Error _ -> true)

    probeOk "jsonEmitter" (fun c ->
        match jsonPort c with
        | Ok _ | Error _ -> true)

    // The registry's view must AGREE that these realize-the-port emitters are
    // present at the Emitter stage — the static port binding and the live
    // registry cannot disagree on what an emitter is.
    let registeredEmitterNames =
        RegisteredAllTransforms.all
        |> List.filter (fun rt -> rt.StageBinding = Emitter)
        |> List.map (fun rt -> rt.Name)
        |> Set.ofList
    for portName in [ "ssdtDdlEmitter"; "jsonEmitter" ] do
        Assert.True(
            Set.contains portName registeredEmitterNames,
            sprintf "port-realizing emitter '%s' is not registered at the Emitter stage" portName)

// ---------------------------------------------------------------------------
// Fact 3 — every migrate-leg emitter is registered.
// ---------------------------------------------------------------------------
//
// The migrate leg's emit phase is the registry-driven `Compose.emitSteps`
// fold (Pipeline) surfaced into `RegisteredAllTransforms.all`. `registered ⇔
// executed` holds for that stage BY CONSTRUCTION (each EmitStep projects its
// own `Metadata`); this Fact is the standing witness that the canonical
// migrate-leg emitter set has not silently dropped a member. Each name here
// is a transform the migrate leg actually executes (SsdtDdlEmitter +
// JsonEmitter + DistributionsEmitter via Compose.emitSteps; DacpacEmitter +
// StaticPopulationEmitter as the conditional schema/data realizers; the
// operator-UX projections Remediation / Summary / SuggestConfig).

[<Fact>]
let ``M15: every migrate-leg emitter is registered`` () =
    let registeredEmitterNames =
        RegisteredAllTransforms.all
        |> List.filter (fun rt -> rt.StageBinding = Emitter)
        |> List.map (fun rt -> rt.Name)
        |> Set.ofList

    // The canonical migrate-leg emitter roster (the names the emit phase
    // actually fires; see `Compose.emitSteps` + the conditional
    // schema/data/dacpac emitters in `RegisteredAllTransforms.all`). A drop or
    // rename of any one — the exact regression lint could never see, since a
    // grep over `open` says nothing about registry membership — fails here.
    let expectedMigrateLegEmitters =
        set [ "ssdtDdlEmitter"
              "jsonEmitter"
              "distributionsEmitter"
              "remediationEmitter"
              "summaryFormatter"
              "suggestConfigEmitter"
              "dacpacEmitter"
              "staticPopulationEmitter" ]

    let missing = Set.difference expectedMigrateLegEmitters registeredEmitterNames
    Assert.True(
        Set.isEmpty missing,
        sprintf
            "migrate-leg emitter(s) not registered at the Emitter stage: %s (registered: %s)"
            (missing |> Set.toList |> String.concat ", ")
            (registeredEmitterNames |> Set.toList |> String.concat ", "))

    // And the dual direction: the registry must validate (the registered set is
    // a real, uniquely-named, rationale-complete registry — not a bag of
    // strings), so the coverage claim above is anchored on a sound registry.
    match TransformRegistry.create RegisteredAllTransforms.all with
    | Ok _ -> ()
    | Error es ->
        let codes = es |> List.map (fun e -> e.Code) |> String.concat ", "
        Assert.Fail(sprintf "RegisteredAllTransforms.all does not validate; codes: %s" codes)
