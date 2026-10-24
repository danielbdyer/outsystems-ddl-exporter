namespace Projection.Core

/// Where a transformation fires in the pipeline. Five stage seams per
/// `DECISIONS 2026-05-15 (late)`'s canonical strongly-typed registry
/// shape; each enumerates a class of transformation site:
///
///   - `Adapter` — raw rowset / JSON → Catalog-fragment translations
///     (e.g., OSSYS adapter rules).
///   - `Pass` — `Catalog -> Lineage<Diagnostics<Catalog>>` (the existing
///     12 passes under `Projection.Core/Passes/`).
///   - `OrderingPolicy` — ordering-policy parameter sites (e.g.,
///     `SelfLoopPolicy` on `TopologicalOrderPass`).
///   - `Emitter` — `Catalog -> ArtifactByKind<'element>` (sibling Π
///     emitters).
///   - `Pipeline` — `Compose`-level transformations (e.g.,
///     `TableRename.applyRenames`).
///
/// **Naming note:** `StageBinding.OrderingPolicy` (the stage-seam
/// vocabulary) is distinct from `OverlayAxis.Ordering` (the
/// operator-intent vocabulary, in `Classification.fs`). The two are
/// related — a transformation registered at `StageBinding =
/// OrderingPolicy` is typically classified `OperatorIntent Ordering` —
/// but the StageBinding names where the transformation fires and the
/// OverlayAxis names whose intent it expresses.
type StageBinding =
    | Adapter
    | Pass
    | OrderingPolicy
    | Emitter
    | Pipeline

/// The codified concerns of V2's structural-evidence layer per
/// `PRODUCT_AXIOMS.md`. Each transformation site advertises which
/// concern it operates on; the registry's totality property test
/// (slice θ) asserts every registered transformation names a Domain.
///
/// **Cross-cutting** is the catch-all for transformations that
/// operate orthogonally to schema / data / identity / diagnostics /
/// cutover-safety (e.g., lineage emission itself, bench probes,
/// the registry traversal). Use a concrete Domain when one fits;
/// `CrossCutting` is reserved for genuinely orthogonal seams.
type Domain =
    | Schema
    | Data
    | Identity
    | Diagnostics
    | CutoverSafety
    | CrossCutting

/// One classified sub-site within a registered transformation.
/// Captures intra-pass classification fidelity per `DECISIONS
/// 2026-05-15 (late)` Q11: a single pass may have multiple sites,
/// each classified independently. `TopologicalOrderPass`'s
/// `[SortKahn; SelfLoopHandling]` pair is the canonical worked
/// example (the SortKahn site is `DataIntent`; the SelfLoopHandling
/// site is `OperatorIntent Ordering` per the chapter A.4.7 open's
/// Q9-trigger-fires worked example).
///
/// The `Rationale` field is the harvest-discipline analysis prose
/// (pillar 9's 4-step workflow output). `TransformRegistry.create`
/// rejects empty rationales — the discipline requires the analysis
/// to be done, not assumed. The harvest-classification coverage
/// property test (slice θ) cross-references this prose against
/// `Tolerance.fs` entries for v1↔v2 divergence audit.
type TransformSite = {
    SiteName : string
    Classification : Classification
    Rationale : string
}

/// Whether a registered transformation has a v2 implementation.
/// Per the harvest workflow's triple-deliverable: v1 transformations
/// without a v2 equivalent ship as `NotImplementedInV2 of rationale`
/// registry entries (paired with a `Tolerance.fs` entry naming the
/// divergence and a `[<Fact(Skip = "...")>]` test stub citing the
/// classification rationale). The three surfaces redundantly catch
/// silent harvest inclusion (pillar 9 named failure mode).
///
/// `TransformRegistry.create` rejects `NotImplementedInV2 ""` —
/// the rationale must name the harvest decision substantively
/// (matches the LINT-ALLOW substantive-rationale discipline pattern
/// per `DECISIONS 2026-05-10`).
type TransformStatus =
    | Active
    | NotImplementedInV2 of rationale: string

/// The canonical strongly-typed surface for a transformation site.
/// Each pass module's primary public surface (slice γ onward) becomes
/// `<PassName>.registered : RegisteredTransform<'In, 'Out>`; the
/// `let run` function becomes private; consumers invoke
/// `registered.Run`. Single definition site; no parallel enumeration
/// (per `DECISIONS 2026-05-15 (late)` Q3 answer).
///
/// The `Run` field carries the typed transformation function itself.
/// Per `DECISIONS 2026-05-13 — Pass return-type codification`, the
/// canonical pass shape is `Lineage<Diagnostics<'Out>>` — the dual
/// writer captures both decisions (Lineage) and observer-relevant
/// findings (Diagnostics). Passes that emit only decisions (no
/// diagnostics) wrap their output via `Diagnostics.ofValue` at
/// `.registered` registration time (slice γ adapts).
///
/// Type erasure happens at the `RegisteredTransformMetadata` boundary
/// (the type-stripped view used for the flat `TransformRegistry.all`
/// enumeration list, the manifest emission, and the totality
/// coverage property test). Per-Run invocation uses the pass
/// module's typed export directly — no `obj`-boxing at consumer
/// call sites.
type RegisteredTransform<'In, 'Out when 'Out : equality> = {
    Name : string
    Domain : Domain
    StageBinding : StageBinding
    Sites : TransformSite list
    Run : 'In -> Lineage<Diagnostics<'Out>>
    Status : TransformStatus
}

/// Type-erased projection of `RegisteredTransform<'In, 'Out>` —
/// drops the `Run` field so the registry's flat collection can hold
/// per-stage heterogeneous transformations without unsafe `obj`-
/// boxing. The metadata view is what `TransformRegistry.create`
/// validates, what the manifest emitter serializes (slice η), and
/// what the totality coverage property test (slice θ) enumerates.
/// Pass-module consumers invoke `<PassName>.registered.Run`
/// directly on the typed export.
type RegisteredTransformMetadata = {
    Name : string
    Domain : Domain
    StageBinding : StageBinding
    Sites : TransformSite list
    Status : TransformStatus
}

[<RequireQualifiedAccess>]
module RegisteredTransform =

    /// Project a typed `RegisteredTransform<'In, 'Out>` to its
    /// type-erased metadata for registry enumeration. The `Run`
    /// field is dropped; consumers that need to invoke `Run` use
    /// the pass module's `.registered` export directly.
    let toMetadata (rt: RegisteredTransform<'In, 'Out>) : RegisteredTransformMetadata =
        { Name = rt.Name
          Domain = rt.Domain
          StageBinding = rt.StageBinding
          Sites = rt.Sites
          Status = rt.Status }

/// Smart-constructors for the typed `TransformSite` value. Per pillar 9
/// the classification (DataIntent vs OperatorIntent OverlayAxis) is the
/// load-bearing axis of every site; lifting these helpers into the
/// function name puts the classification at the head of every site
/// declaration rather than nested inside a record literal field.
///
/// Two-consumer threshold cleared at slice 5.13.sibling-emitter-registry-
/// helper-extraction (2026-05-18): 9 emitter + adapter registrations
/// (SsdtDdl / Json / Distributions / StaticPopulation / DataEmissionComposer
/// / StaticSeeds / MigrationDependencies / Bootstrap / CatalogReader)
/// shared the literal record syntax; the helpers absorb the boilerplate.
[<RequireQualifiedAccess>]
module TransformSite =

    /// A site that projects evidence without operator opinion. Per
    /// pillar 9's 4-step harvest workflow, this is the DataIntent arm:
    /// the transformation is reachable from
    /// `Project(catalog, Policy.empty, profile)` without operator-supplied
    /// overlay. The rationale prose names what the projection does and
    /// why no Policy enters.
    let dataIntent (name: string) (rationale: string) : TransformSite =
        { SiteName = name
          Classification = DataIntent
          Rationale = rationale }

    /// A site that expresses operator-supplied intent through the named
    /// `OverlayAxis`. Per pillar 9, this is the OperatorIntent arm: the
    /// transformation lands as a registered overlay carrying classified
    /// `LineageEvent`s. The axis IS the operator-intent vocabulary
    /// (Selection / Emission / Insertion / Tightening / Ordering); the
    /// rationale prose names the source of operator intent (e.g.,
    /// "operator-supplied UserRemapContext" / "Policy.Emission.DataComposition").
    let operatorIntent (name: string) (axis: OverlayAxis) (rationale: string) : TransformSite =
        { SiteName = name
          Classification = OperatorIntent axis
          Rationale = rationale }

/// Smart-constructors for `RegisteredTransformMetadata` at the two
/// most common StageBinding values (Emitter + Adapter). The Pass /
/// OrderingPolicy / Pipeline bindings continue to flow through the
/// typed `RegisteredTransform<'In, 'Out>` shell + `RegisteredTransform.
/// toMetadata` projection (those bindings carry the `Run` field).
/// Emitter + Adapter signatures don't fit the typed shell (heterogeneous
/// `'In` / `'Out` with `ArtifactByKind<_>` / `Result<_, EmitError>` /
/// `Task<_>` envelopes); metadata-only registration is the principled
/// form for both — `CatalogReader.registeredMetadata` is the adapter
/// precedent, `SsdtDdlEmitter.registeredMetadata` is the emitter
/// precedent.
///
/// Both helpers fix `Status = Active`. Consumers that need a
/// `NotImplementedInV2` registration use the record-literal form
/// directly — the per-rationale parameter would clutter the common
/// constructor for a rare case.
[<RequireQualifiedAccess>]
module RegisteredTransformMetadata =

    /// Construct emitter-stage metadata. The `StageBinding = Emitter`
    /// binding is fixed; callers supply Name + Domain + Sites.
    let emitter
        (name: string)
        (domain: Domain)
        (sites: TransformSite list)
        : RegisteredTransformMetadata =
        { Name = name
          Domain = domain
          StageBinding = Emitter
          Sites = sites
          Status = Active }

    /// Construct adapter-stage metadata. The `StageBinding = Adapter`
    /// binding is fixed; sibling to `emitter`. The CatalogReader
    /// precedent ships at the cherry-pick boundary (slice δ); future
    /// adapter `registeredMetadata` lifts use this constructor.
    let adapter
        (name: string)
        (domain: Domain)
        (sites: TransformSite list)
        : RegisteredTransformMetadata =
        { Name = name
          Domain = domain
          StageBinding = Adapter
          Sites = sites
          Status = Active }

[<RequireQualifiedAccess>]
module TransformRegistry =

    let private registryError (code: string) (message: string) : ValidationError =
        ValidationError.create (System.String.Concat ("registry.", code)) message  // LINT-ALLOW: terminal error-code prefix at the ValidationError boundary; segments are typed (literal prefix + code identifier); BCL `String.Concat` is the use-case-specific library for two-segment terminal-text composition

    let private validateName (entry: RegisteredTransformMetadata) : Result<unit> =
        if System.String.IsNullOrWhiteSpace entry.Name then
            Result.failureOf
                (registryError "nameEmpty" "Registered transformation Name must be non-empty.")
        else
            Result.success ()

    let private validateSite (entry: RegisteredTransformMetadata) (site: TransformSite) : Result<unit> =
        if System.String.IsNullOrWhiteSpace site.Rationale then
            Result.failureOf
                (registryError
                    "siteRationaleEmpty"
                    (System.String.Concat (  // LINT-ALLOW: terminal error-message composition at the ValidationError boundary; segments are typed (pass identifier + site identifier from the registry record); BCL `String.Concat` is the use-case-specific library for the multi-segment audit-narration message
                        "Registered transformation '", entry.Name,
                        "' site '", site.SiteName,
                        "' has empty Rationale; pillar 9 harvest-discipline analysis required.")))
        else
            Result.success ()

    let private validateStatus (entry: RegisteredTransformMetadata) : Result<unit> =
        match entry.Status with
        | Active -> Result.success ()
        | NotImplementedInV2 rationale ->
            if System.String.IsNullOrWhiteSpace rationale then
                Result.failureOf
                    (registryError
                        "notImplementedRationaleEmpty"
                        (System.String.Concat (  // LINT-ALLOW: terminal error-message composition at the ValidationError boundary; segments are typed (pass identifier from the registry record + literal narration); BCL `String.Concat` is the use-case-specific library for the multi-segment audit-narration message
                            "Registered transformation '", entry.Name,
                            "' has Status = NotImplementedInV2 with empty rationale; ",
                            "harvest-discipline triple deliverable requires substantive rationale.")))
            else
                Result.success ()

    let private validateUniqueNames (entries: RegisteredTransformMetadata list) : Result<unit> =
        let duplicateNames =
            entries
            |> List.groupBy (fun e -> e.Name)
            |> List.filter (fun (_, xs) -> List.length xs > 1)
            |> List.map fst
        if List.isEmpty duplicateNames then
            Result.success ()
        else
            duplicateNames
            |> List.map (fun name ->
                registryError
                    "duplicatePassName"
                    (System.String.Concat (  // LINT-ALLOW: terminal error-message composition at the ValidationError boundary; segments are typed (literal narration + duplicated pass identifier); BCL `String.Concat` is the use-case-specific library for the multi-segment audit-narration message
                        "Registered transformation Name '", name,
                        "' appears multiple times; Names must be unique within the registry.")))
            |> Result.failure

    /// Smart constructor enforcing the registry's totality invariants.
    /// Per the chapter A.4.7 open success criteria + DECISIONS
    /// 2026-05-15 (late):
    ///   - Every `Name` unique within the registry.
    ///   - Every `Site.Rationale` non-empty (pillar 9 harvest-
    ///     discipline analysis required at every classified site).
    ///   - `Status = NotImplementedInV2 r` requires substantive `r`
    ///     (triple deliverable harvest-workflow audit).
    ///
    /// Slice β ships the constructor against an empty `all` list;
    /// slice γ / δ / ε populate the list as each pass module exposes
    /// `.registered`. The validation runs once at construction; the
    /// resulting metadata list is trusted thereafter.
    let create (entries: RegisteredTransformMetadata list) : Result<RegisteredTransformMetadata list> =
        use _ = Bench.scope "ir.registry.create"
        let entryErrors =
            entries
            |> List.collect (fun entry ->
                let nameResult = validateName entry
                let statusResult = validateStatus entry
                let siteResults = entry.Sites |> List.map (validateSite entry)
                let allResults = nameResult :: statusResult :: siteResults
                allResults
                |> List.collect (fun r ->
                    match r with
                    | Ok () -> []
                    | Error es -> es))
        let uniquenessErrors =
            match validateUniqueNames entries with
            | Ok () -> []
            | Error es -> es
        let allErrors = entryErrors @ uniquenessErrors
        if List.isEmpty allErrors then
            Result.success entries
        else
            Error allErrors

    /// The empty registry. Slice β shipped this as a structural
    /// placeholder; the populate-trigger (slices γ–ε) ultimately fired
    /// **elsewhere** — the live, populated registry is
    /// `RegisteredTransforms.all` (and the assembly-wide
    /// `RegisteredAllTransforms.all`), NOT this binding. This one stays
    /// empty by design (pinned by `TransformRegistryTests` "ships
    /// empty"); do not read it as the registry — reach for
    /// `RegisteredTransforms.all` (NM-39).
    let all : RegisteredTransformMetadata list = []

    /// Filter to a single stage. Used by `Compose.run` traversal
    /// (slice ζ) to iterate per-stage in stage order.
    let inStage (stage: StageBinding) (entries: RegisteredTransformMetadata list) : RegisteredTransformMetadata list =
        entries |> List.filter (fun rt -> rt.StageBinding = stage)

    /// Stage-ordering function. Used by `Compose.run` (slice ζ) to
    /// traverse the registry in execution order: Adapter →
    /// (OrderingPolicy + Pass) → Emitter → Pipeline.
    let stageOrdinal (stage: StageBinding) : int =
        match stage with
        | Adapter        -> 0
        | OrderingPolicy -> 1
        | Pass           -> 2
        | Emitter        -> 3
        | Pipeline       -> 4

    /// All entries sorted in stage-ordinal order. Slice ζ's
    /// `Compose.run` traversal consumes this as its execution loop.
    let allInStageOrder (entries: RegisteredTransformMetadata list) : RegisteredTransformMetadata list =
        entries |> List.sortBy (fun rt -> stageOrdinal rt.StageBinding)

    /// **Skeleton view** — entries whose every Site classifies as
    /// `DataIntent`. Reachable from `Project(catalog, Policy.empty,
    /// profile)` without operator opinion; lands in the skeleton.
    ///
    /// Chapter A.4.7 slice ζ (minimum). The full `Compose.runWithSkeleton`
    /// traversal-with-filter (per `V2_PRODUCTION_CUTOVER.md` §6.4.7 task 4)
    /// is deferred-with-trigger pending pass-chaining adapter design for
    /// heterogeneous output types; the filter helper ships now so slice
    /// θ's skeleton-purity property test can verify the structural
    /// classification correctness independently of runtime traversal.
    ///
    /// **Failure mode caught.** If a pass classified `DataIntent` actually
    /// expresses operator intent (e.g., a filter that re-applies inactive-
    /// records-drop policy classified as DataIntent rather than
    /// OperatorIntent Selection), its Site list contains an `OperatorIntent`
    /// entry, and the pass is excluded from the skeleton view — the
    /// property test asserts every event emitted during a skeleton
    /// traversal carries `Classification = DataIntent`; misclassification
    /// surfaces as a leak.
    let skeletonView (entries: RegisteredTransformMetadata list) : RegisteredTransformMetadata list =
        use _ = Bench.scope "ir.registry.skeletonView"
        entries
        |> List.filter (fun rt ->
            rt.Sites
            |> List.forall (fun site ->
                match site.Classification with
                | DataIntent -> true
                | OperatorIntent _ -> false))

    /// **Overlay view** — entries whose Sites contain at least one
    /// `OperatorIntent` classification. These are the registered
    /// overlays that the skeleton excludes; the overlay-exercise
    /// property test (slice θ) asserts each fires in at least one
    /// canary scenario.
    ///
    /// Note: a multi-site pass with mixed classifications (e.g.,
    /// `TopologicalOrderPass`'s sortKahn DataIntent + selfLoopHandling
    /// OperatorIntent Ordering) appears in `overlayView` but NOT in
    /// `skeletonView` — the pass-level classification follows the
    /// strictest site. Slice θ's tests witness this asymmetry.
    let overlayView (entries: RegisteredTransformMetadata list) : RegisteredTransformMetadata list =
        use _ = Bench.scope "ir.registry.overlayView"
        entries
        |> List.filter (fun rt ->
            rt.Sites
            |> List.exists (fun site ->
                match site.Classification with
                | OperatorIntent _ -> true
                | DataIntent -> false))

    /// **Overlay axes** present across the registry — every distinct
    /// `OverlayAxis` value that appears on at least one `OperatorIntent`
    /// site. Used by slice θ's overlay-exercise property test to
    /// enumerate the operator-intent surface that the canary must
    /// cover.
    let overlayAxes (entries: RegisteredTransformMetadata list) : Set<OverlayAxis> =
        entries
        |> List.collect (fun rt -> rt.Sites)
        |> List.choose (fun site ->
            match site.Classification with
            | OperatorIntent axis -> Some axis
            | DataIntent -> None)
        |> Set.ofList

    /// **By-domain filter** — entries belonging to the named `Domain`.
    /// Per pillar 9's six-concern domain taxonomy
    /// (Schema / Data / Identity / Diagnostics / CutoverSafety /
    /// CrossCutting): the per-axis confidence map in
    /// `CUTOVER_READINESS_BRIEF.md` is the operator-facing
    /// projection of this view. Slice 5.13.identity-axis-closure
    /// codifies the filter as the two-consumer threshold:
    ///   (1) Data axis aggregation lives at `RegisteredDataTransforms`
    ///       (slice 5.13.data-emission-registry)
    ///   (2) Identity axis aggregation reads cross-project metadata
    ///       (UserFkReflowPass in Core + Migration / Bootstrap
    ///       User-FK Sites in Data) via this filter rather than a
    ///       parallel aggregator.
    let byDomain (domain: Domain) (entries: RegisteredTransformMetadata list) : RegisteredTransformMetadata list =
        entries |> List.filter (fun rt -> rt.Domain = domain)

    /// **By-overlay-axis filter** — entries whose Sites contain at
    /// least one `OperatorIntent <axis>` classification. Cross-cuts
    /// `Domain` (a single overlay axis can fire across multiple
    /// domains; e.g., `Insertion` fires on both Identity-axis
    /// MigrationDeps emitter sites AND Data-axis static-seed
    /// emission). Slice 5.13.identity-axis-closure uses this to
    /// surface every transformation site that touches the
    /// operator-supplied User-FK reflow surface.
    let byOverlayAxis (axis: OverlayAxis) (entries: RegisteredTransformMetadata list) : RegisteredTransformMetadata list =
        entries
        |> List.filter (fun rt ->
            rt.Sites
            |> List.exists (fun site ->
                match site.Classification with
                | OperatorIntent a -> a = axis
                | DataIntent -> false))

    // Domain / StageBinding / Classification / OverlayAxis projection
    // helpers — Sites' Rationale strings appear as-is. Per pillar 1:
    // typed DU → stable string at the digest boundary; explicit
    // projection is the discipline (`sprintf "%A"` forbidden — its
    // shape depends on F# compiler version).
    let private domainName (d: Domain) : string =
        match d with
        | Schema -> "Schema"
        | Data -> "Data"
        | Identity -> "Identity"
        | Diagnostics -> "Diagnostics"
        | CutoverSafety -> "CutoverSafety"
        | CrossCutting -> "CrossCutting"

    let private stageName (s: StageBinding) : string =
        match s with
        | Adapter -> "Adapter"
        | Pass -> "Pass"
        | OrderingPolicy -> "OrderingPolicy"
        | Emitter -> "Emitter"
        | Pipeline -> "Pipeline"

    let private overlayAxisName (a: OverlayAxis) : string =
        match a with
        | Selection -> "Selection"
        | Emission -> "Emission"
        | Insertion -> "Insertion"
        | Tightening -> "Tightening"
        | Ordering -> "Ordering"

    let private classificationName (c: Classification) : string =
        match c with
        | DataIntent -> "DataIntent"
        | OperatorIntent axis ->
            System.String.Concat ("OperatorIntent.", overlayAxisName axis)  // LINT-ALLOW: terminal digest-projection at the SHA256 boundary; segments are typed (literal prefix + closed-DU case name); BCL `String.Concat` is the use-case-specific library for two-segment qualified-case-name composition

    /// NM-60 — append a variable-length (free-text) field LENGTH-PREFIXED:
    /// `<utf8-byte-count>:<value>`. A length prefix makes the encoding
    /// injective: no crafted `Name` / `SiteName` / `Rationale` containing the
    /// structural delimiters (`|`, `=`, `{`, `}`, `;`, `[`, `]`, `:`) can forge
    /// a different field structure that serializes to the same buffer (the
    /// delimiter-injection collision the unescaped append allowed). The byte
    /// count — not the char count — is used so a multibyte UTF-8 field cannot be
    /// confused with a shorter one. Mirrors the explicit-projection discipline
    /// the closed-DU fields (`domainName` / `stageName` / ...) already enjoy by
    /// drawing from a fixed token set.
    let private appendLenPrefixed (sb: System.Text.StringBuilder) (value: string) : unit =
        let byteCount = System.Text.Encoding.UTF8.GetByteCount(value)
        sb.Append(byteCount) |> ignore
        sb.Append(':') |> ignore
        sb.Append(value) |> ignore

    let private statusName (s: TransformStatus) : string =
        match s with
        | Active -> "Active"
        | NotImplementedInV2 r ->
            System.String.Concat ("NotImplementedInV2:", r)  // LINT-ALLOW: terminal digest-projection at the SHA256 boundary; segments are typed (literal prefix + rationale string from the registry record); BCL `String.Concat` is the use-case-specific library for two-segment case-name + payload composition

    /// Chapter A.4.7' slice ζ — deterministic SHA256 digest of the
    /// registry's metadata content. Sorted by Name; serialized field-
    /// by-field via the explicit DU-projection helpers above; hashed
    /// via `System.Security.Cryptography.SHA256.HashData`. Returned as
    /// lowercase hex string suitable for the manifest's
    /// `registry.digest` field.
    ///
    /// Property: any change to Name / Domain / StageBinding / Sites
    /// (SiteName, Classification, Rationale) / Status changes the
    /// digest; reorderings do not (the sort by Name normalizes input
    /// order). The 5th bidirectional property test asserts the round-
    /// trip stability + perturbation sensitivity per A41.
    ///
    /// **NM-60 — tamper-evidence: the variable-length free-text fields are
    /// LENGTH-PREFIXED, not appended raw.** `Name`, `SiteName`, `Rationale`, and
    /// the `NotImplementedInV2` status rationale previously went into the buffer
    /// between unescaped `|` / `=` / `{` / `}` / `;` delimiters. A crafted
    /// rationale (e.g. one containing `}` or `|domain=`) could re-parse the field
    /// structure so two DISTINCT registries serialized to the SAME buffer — a
    /// delimiter-injection collision that silently downgraded the manifest's
    /// tamper-evidence claim. Every free-text field now carries its UTF-8 byte
    /// count as a `<len>:` prefix (`appendLenPrefixed`), making the encoding
    /// injective: the structural delimiters become unforgeable. The closed-DU
    /// fields (`domainName` / `stageName` / `classificationName` / the status
    /// TAG) draw from a fixed finite token set and need no prefix. **This changes
    /// the digest VALUE** — the round-trip baseline + any pinned digest move.
    let digest (entries: RegisteredTransformMetadata list) : string =
        use _ = Bench.scope "ir.registry.digest"
        let sorted = entries |> List.sortBy (fun e -> e.Name)
        let buffer = System.Text.StringBuilder()  // LINT-ALLOW-FILE-MUTATION not needed — instance-local mutation only; sealed at this function's exit via the SHA256.HashData call. Per slice ζ: deterministic concatenation of typed DU projections at the SHA256 boundary; no consumer reads the StringBuilder, only the resulting bytes.
        for entry in sorted do
            buffer.Append('|') |> ignore
            buffer.Append("name=") |> ignore
            appendLenPrefixed buffer entry.Name
            buffer.Append("|domain=") |> ignore
            buffer.Append(domainName entry.Domain) |> ignore
            buffer.Append("|stage=") |> ignore
            buffer.Append(stageName entry.StageBinding) |> ignore
            buffer.Append("|status=") |> ignore
            appendLenPrefixed buffer (statusName entry.Status)
            buffer.Append("|sites=[") |> ignore
            for site in entry.Sites do
                buffer.Append('{') |> ignore
                buffer.Append("siteName=") |> ignore
                appendLenPrefixed buffer site.SiteName
                buffer.Append(";classification=") |> ignore
                buffer.Append(classificationName site.Classification) |> ignore
                buffer.Append(";rationale=") |> ignore
                appendLenPrefixed buffer site.Rationale
                buffer.Append('}') |> ignore
            buffer.Append(']') |> ignore
        let bytes = System.Text.Encoding.UTF8.GetBytes(buffer.ToString())
        let hash = System.Security.Cryptography.SHA256.HashData(bytes)
        System.Convert.ToHexString(hash).ToLowerInvariant()
