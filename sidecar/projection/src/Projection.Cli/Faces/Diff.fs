module Projection.Cli.Faces.Diff

// The read-only catalog-diff / compare / check-shape faces, extracted from the
// RunFaces wall (recon #3 — the per-verb file split). Self-contained: they
// resolve refs through `Ref`, render through `View`/`TtyRenderer`, and compute
// via `Comparison`/`Readiness` — never a RunFaces-internal helper.

open System
open Projection.Core
open Projection.Pipeline
open Projection.Cli
open Projection.Cli.OperatorConsole

/// `diff <refA> <refB>` — change, rendered essence-first (INSTRUMENT slice 1,
/// the first surface of the instrument). Resolves both refs through `Ref`
/// (file / `@runId` / `json:` / `live:`) and renders the catalog change: the
/// plain verdict that leads, then the per-channel dig beneath. `--format json`
/// emits the same `View` as structure. `--module <name>` scopes the COMPUTATION
/// to one module (a smaller, reviewable diff); `--only <channel>` scopes the
/// DISPLAY to one channel (columns / relationships / indexes / sequences / tables).
let runDiff (refAText: string) (refBText: string) (asJson: bool) (depth: int) (channel: string option) (onlyModule: string option) : int =
    let refA, refB = Ref.parse refAText, Ref.parse refBText
    // Espace posture (CROSS_ENVIRONMENT_READINESS.md): two `live:` (physical)
    // OutSystems reads do not share identity — `ReadSide` synthesizes SsKeys from
    // the physical name, so the same entity in two environments will not align.
    // Name it (never a silent, wrong diff); steer to the espace-safe operands.
    if Ref.bothLive refA refB then
        Console.Error.WriteLine "projection diff: comparing two `live:` reads by PHYSICAL identity is espace-unsafe — SsKeys are synthesized from physical names and will not align across OutSystems environments. Use `ossys:<conn>` operands (native GUID identity) for a cross-environment diff, or `projection check shape` for the readiness gate."
    // Both OSSYS-sourced ⇒ the operator wants the espace-safe LOGICAL shape:
    // normalize away the realization-name artifacts `CatalogDiff` compares.
    let norm (c: Catalog) : Catalog = if Ref.bothOssys refA refB then Readiness.toLogicalShape c else c
    let resolve (s: string) = (Ref.resolveCatalog (Ref.parse s)).GetAwaiter().GetResult()
    // `--module <name>` keeps only the named module's kinds before diffing —
    // sequences are catalog-level, so a module scope drops them. Case-insensitive
    // name match; a name that matches nothing yields an empty scope (the diff reads
    // "no differences") — the operator's signal to correct the flag. The raw record
    // update is safe here: the diff only OBSERVES the catalogs (no re-validation /
    // FK closure needed — `CatalogDiff.between` compares by SsKey).
    let scopeModule (cat: Catalog) : Catalog =
        match onlyModule with
        | None -> cat
        | Some name ->
            { cat with
                Modules   = cat.Modules |> List.filter (fun m -> System.String.Equals(Name.value m.Name, name, System.StringComparison.OrdinalIgnoreCase))
                Sequences = [] }
    match resolve refAText with
    | Error errs ->
        Console.Error.WriteLine "projection diff: could not resolve the first reference:"
        printErrors Console.Error errs
        2
    | Ok a ->
        match resolve refBText with
        | Error errs ->
            Console.Error.WriteLine "projection diff: could not resolve the second reference:"
            printErrors Console.Error errs
            2
        | Ok b ->
            match Comparison.catalog.Between (scopeModule (norm a)) (scopeModule (norm b)) with
            | Error e ->
                Console.Error.WriteLine(sprintf "projection diff: %s" e)
                2
            | Ok d ->
                // L2 — the changeset becomes a CONTROL surface: dig the move-lanes live on
                // a terminal, the same document one-shot when piped / --json / --query.
                Navigator.present asJson depth (Comparison.renderCatalogChangeScoped channel d)

/// `projection compare <A> <B>` — NM-71/WP9: the read-only multi-environment
/// readiness check. Resolves both operands to catalogs (the `Ref` machinery,
/// like `diff`), runs the schema-delta + data-dealbreaker compare, prints the
/// roll-up (or `--format json`), and writes `compare.json`. Advisory — exits 0
/// (the report carries the readiness verdict); a malformed operand exits 2.
/// The SOURCE operand resolves to a `Source` so a live env can be PROFILED —
/// the data-dealbreaker section reads A's data against B's declared model. A
/// static source (file / `@runId` / json) carries no profile, so the section
/// stays honestly advisory-silent; a live env supplies it. A profiling failure
/// degrades to advisory-silent (never aborts — the schema delta still leads).
let runCompare (refAText: string) (refBText: string) (asJson: bool) : int =
    let refA, refB = Ref.parse refAText, Ref.parse refBText
    // Espace posture — see `runDiff`. Two `live:` reads of OutSystems environments
    // do not share identity; name the hazard rather than emit a silently-wrong compare.
    if Ref.bothLive refA refB then
        Console.Error.WriteLine "projection compare: comparing two `live:` reads by PHYSICAL identity is espace-unsafe — SsKeys are synthesized from physical names and will not align across OutSystems environments. Use `ossys:<conn>` operands (native GUID identity) for a cross-environment comparison, or `projection check shape` for the readiness gate."
    let norm (c: Catalog) : Catalog = if Ref.bothOssys refA refB then Readiness.toLogicalShape c else c
    let resolve (s: string) = (Ref.resolveCatalog (Ref.parse s)).GetAwaiter().GetResult()
    let resolveSrc (s: string) = (Ref.resolveSource (Ref.parse s)).GetAwaiter().GetResult()
    match resolveSrc refAText with
    | Error errs ->
        Console.Error.WriteLine "projection compare: could not resolve the first reference:"
        printErrors Console.Error errs
        2
    | Ok srcA ->
        match (Source.read srcA).GetAwaiter().GetResult() with
        | Error errs ->
            Console.Error.WriteLine "projection compare: could not read the first reference's catalog:"
            printErrors Console.Error errs
            2
        | Ok a ->
            match resolve refBText with
            | Error errs ->
                Console.Error.WriteLine "projection compare: could not resolve the second reference:"
                printErrors Console.Error errs
                2
            | Ok b ->
                // Live-profile the source when it can (a live env). The acquire
                // is the reified capability (`Source.profile` = `Some f` iff
                // profilable); a failure → advisory-silent, not a hard error.
                let profileA =
                    match Source.profile srcA with
                    | None -> None
                    | Some acquire ->
                        match (acquire a).GetAwaiter().GetResult() with
                        | Ok p -> Some p
                        | Error _ -> None
                let source : Compare.Operand = { Label = refAText; Catalog = norm a; Profile = profileA }
                let target : Compare.Operand = { Label = refBText; Catalog = norm b; Profile = None }
                let report = Compare.compute source target
                if asJson then printfn "%s" (Compare.toJsonString report)
                else Compare.render report |> List.iter (fun line -> printfn "%s" line)
                System.IO.File.WriteAllText("compare.json", Compare.toJsonString report)
                0

/// `projection check shape` — the espace-safe cross-environment readiness gate
/// (CROSS_ENVIRONMENT_READINESS.md §4/§5). Reads the agreed shape + every
/// `confirm` environment via OSSYS (`Source.ofOssys` → native GUID identity, so
/// the comparison is espace-safe), profiles each env's data, rolls a
/// `Readiness.ReadinessReport`, prints the roll-up (or `--format json`), writes
/// `readiness.json`, and exits 0 (estate ready) / 5 (not ready — a real schema
/// divergence or a data dealbreaker) / 6 (an env could not be read). Read-only.
let runCheckShape (agreedLabel: string) (agreedRef: string) (confirm: (string * string) list) (asJson: bool) : int =
    let readCatalog (refStr: string) = (Source.read (Source.ofOssys refStr)).GetAwaiter().GetResult()
    match readCatalog agreedRef with
    | Error errs ->
        Console.Error.WriteLine(sprintf "projection check shape: could not read the agreed shape '%s':" agreedLabel)
        printErrors Console.Error errs
        6
    | Ok agreedCatalog ->
        let agreedOperand : Compare.Operand = { Label = agreedLabel; Catalog = agreedCatalog; Profile = None }
        // Each confirm env: its OSSYS catalog (schema) + a profile of its live
        // data (the dealbreaker evidence). A profile failure degrades to
        // advisory-silent (the schema verdict still leads), never aborts.
        let resolveEnv (label: string, refStr: string) : Result<string * Compare.Operand> =
            let src = Source.ofOssys refStr
            match (Source.read src).GetAwaiter().GetResult() with
            | Error errs -> Result.failure errs
            | Ok catalog ->
                let profile =
                    match Source.profile src with
                    | None -> None
                    | Some acquire ->
                        match (acquire catalog).GetAwaiter().GetResult() with
                        | Ok p    -> Some p
                        | Error _ -> None
                Result.success (label, ({ Label = label; Catalog = catalog; Profile = profile } : Compare.Operand))
        let resolved = confirm |> List.map resolveEnv
        let readErrors = resolved |> List.collect (function Error es -> es | Ok _ -> [])
        match readErrors with
        | _ :: _ ->
            Console.Error.WriteLine "projection check shape: could not read one or more confirm environments:"
            printErrors Console.Error readErrors
            6
        | [] ->
            let envs = resolved |> List.choose (function Ok v -> Some v | Error _ -> None)
            let report = Readiness.compute agreedLabel agreedOperand envs
            if asJson then printfn "%s" (Readiness.toJsonString report)
            else Readiness.render report |> List.iter (fun line -> printfn "%s" line)
            System.IO.File.WriteAllText("readiness.json", Readiness.toJsonString report)
            if Readiness.isReady report then 0 else 5
