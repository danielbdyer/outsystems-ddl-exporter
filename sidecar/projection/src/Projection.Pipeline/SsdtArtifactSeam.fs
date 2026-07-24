namespace Projection.Pipeline

open Projection.Core
open Projection.Targets.SSDT

/// The post-emit **SSDT-ARTIFACT SEAM** — the `ArtifactByKind<SsdtFile>` analog of
/// `EmissionSeam` (post-chain `Catalog → Catalog`) and `DataCorrectionSeam`
/// (the row plane). Every operator-intent rewrite of the emitted SSDT bundle
/// lives here as ONE bound, registered entry, so `registered ⇔ executed` holds
/// for the seam by construction — the same discipline E1 / E5 established for the
/// emit / post-chain stages.
///
/// **Why this seam exists (the blind spot it closes).** Emission-folder targeting
/// (`overrides.emissionFolders` — relocate a kind's emitted `.sql` from its
/// default `Modules/<Module>/` folder to an operator-named folder) was EXECUTED by
/// a bare `applyEmissionFolderOverrides` call inside the SSDT emit step, with no
/// registry binding — the exact F2/F3-shaped "operator-intent mutation runs
/// outside every bound source" pattern `EmissionSeam` was created to retire.
/// Routing it through this one seam fixes it structurally: `apply` folds exactly
/// the registered `rewrites`, and `metadata` / `executedNames` project from the
/// SAME list — so an SSDT-artifact rewrite added here is BOTH executed (by
/// `apply`) and registered (in `metadata`, wired into `RegisteredAllTransforms.all`)
/// by construction, never one without the other.
///
/// The seam is operator-config-driven (the folder targets are operator emission
/// policy, not source evidence — `OperatorIntent Emission`), which is why it is a
/// post-emit ARTIFACT seam rather than a `Catalog → Catalog` pass or the
/// `EmissionSeam`: it rewrites the emitted per-kind `SsdtFile` paths, downstream of
/// Π (pillar 9: emitters are `DataIntent`; operator opinion enters at the
/// Pipeline-layer realization boundary).
[<RequireQualifiedAccess>]
module SsdtArtifactSeam =

    /// One post-emit SSDT-artifact rewrite: its registry metadata paired with the
    /// pure transform it executes over the emitted bundle. The pairing is the
    /// load-bearing invariant — the metadata and the transform travel together, so
    /// neither can drift from the other.
    type private Rewrite =
        { Metadata  : RegisteredTransformMetadata
          Transform : EmissionFolders
                          -> Catalog
                          -> ArtifactByKind<SsdtDdlEmitter.SsdtFile>
                          -> ArtifactByKind<SsdtDdlEmitter.SsdtFile> }

    /// Emission-folder targeting — relocate a kind's emitted SSDT `.sql` from its
    /// default `Modules/<Module>/` folder to the operator-named folder
    /// (`overrides.emissionFolders`), preserving the cross-platform-deterministic
    /// `<Schema>.<Table>.sql` basename and replacing only the directory prefix.
    /// Key-preserving (`ArtifactByKind.mapValues`), so the strict-equality keyset
    /// invariant (T11) carries over with no re-validation. `EmissionFolders.empty`
    /// ⇒ identity (byte-identical). Body lifted verbatim from the former hand-wired
    /// `applyEmissionFolderOverrides`.
    let private emissionFolderTargeting : Rewrite =
        { Metadata =
            RegisteredTransformMetadata.emitter "emissionFolderTargeting" Schema
                [ TransformSite.operatorIntent "emissionFolderTargeting" Emission
                    "Relocate a kind's emitted SSDT `.sql` from its default Modules/<Module>/ folder to the operator-named folder (`overrides.emissionFolders`), preserving the `<Schema>.<Table>.sql` basename; only the directory prefix is rewritten. OperatorIntent Emission: the target folder is operator-supplied emission policy, not source evidence. Empty ⇒ identity (byte-identical)." ]
          Transform =
            fun folders _catalog files ->
                if EmissionFolders.isEmpty folders then files
                else
                    use _ = Bench.scope "compose.applyEmissionFolderOverrides"
                    // PL-4 (S56) — key-preserving rewrite: the proven keyset carries
                    // over via `mapValues`; no re-validation, no unreachable arm.
                    files
                    |> ArtifactByKind.mapValues (fun key file ->
                        match Map.tryFind key folders.ByKind with
                        | None        -> file
                        | Some folder ->
                            let segments = file.RelativePath.Split('/')
                            let basename = segments.[segments.Length - 1]
                            { file with
                                RelativePath = System.String.Concat(folder, "/", basename) }) }

    /// The registered post-emit SSDT-artifact rewrites, in application order. The
    /// SINGLE source `apply` / `metadata` / `executedNames` all project from. A
    /// future SSDT-bundle rewrite is added here (covered by the bidirectional
    /// totality test), not as a bare call inside the emit step.
    let private rewrites : Rewrite list = [ emissionFolderTargeting ]

    /// Apply every registered SSDT-artifact rewrite, in order — the ONE seam the
    /// SSDT emit step routes its post-emit bundle rewrites through.
    let apply
        (folders: EmissionFolders)
        (catalog: Catalog)
        (files: ArtifactByKind<SsdtDdlEmitter.SsdtFile>)
        : ArtifactByKind<SsdtDdlEmitter.SsdtFile> =
        rewrites |> List.fold (fun f r -> r.Transform folders catalog f) files

    /// The seam's registry metadata — projected from the SAME `rewrites` that
    /// `apply` executes, so `registered ⇔ executed` holds for the seam by
    /// construction (the E1 discipline). Spliced into `RegisteredAllTransforms.all`.
    let metadata : RegisteredTransformMetadata list =
        rewrites |> List.map (fun r -> r.Metadata)

    /// The executed rewrite names — the bidirectional test pairs these against
    /// `metadata` (the seam's own closure) and against `RegisteredAllTransforms.all`
    /// (the wiring), the two halves of the seam's totality guarantee.
    let executedNames : string list =
        rewrites |> List.map (fun r -> r.Metadata.Name)
