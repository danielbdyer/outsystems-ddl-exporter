namespace Projection.Pipeline

// LINT-ALLOW-FILE: the estate board's rolled-up text renderer + the estate.json
//   codec compose operator-facing statements (THE_VOICE twelve-rule register;
//   the presentation contract, CHAPTER_ESTATE_OPEN.md Appendix A) and structured
//   JSON at a terminal reporting boundary. The aggregation core (findings from
//   CatalogDiff channels + the ModelFidelity dealbreakers; grouping; the verdict
//   formula) is pure and carries no I/O — operand resolution (the OSSYS reads +
//   profiling) lives one layer up (the CLI run face), exactly as `Compare.fs` /
//   `Readiness.fs` split.

open System
open System.Text.Json.Nodes
open Projection.Core

/// `projection check estate` — the estate-convergence instrument
/// (`CHAPTER_ESTATE_OPEN.md`; `DECISIONS 2026-07-15 — The estate chapter
/// opens`). Given the unification TARGET (the agreed environment's shape, or
/// the authored model under `--against model`) and N confirm environments, it
/// presents every divergence as a finding on a disposition lane
/// (DECIDE → REPAIR → RELAX → WATCH) with a stable cross-artifact key:
///
///   - **Schema plane** — each environment's logical shape against the
///     target's (`CatalogDiff` over `Readiness.toLogicalShape`, so the
///     comparison is espace-safe; A45): presence / rename / attribute /
///     reference / index / facet divergences, grouped across environments by
///     `FindingKey`.
///   - **Data plane** — each environment's profiled data against the target's
///     declared constraints (the `ModelFidelity` engine via `Compare`): NULLs
///     under NOT NULL, duplicates under UNIQUE/PK, relationship orphans,
///     length/type overflow — per-environment evidence on one keyed finding.
///
/// Read-only / advisory — a convergence instrument, not a move. Pure over
/// resolved operands. One substrate: the rendered board and `estate.json` are
/// projections of one `EstateReport` value.
[<RequireQualifiedAccess>]
module Estate =

    /// The unification target the run compared against — named on the
    /// masthead, so the basis of every verdict is explicit (DECISIONS
    /// 2026-07-15: the run states which operand it used).
    [<RequireQualifiedAccess>]
    type TargetOperand =
        /// The agreed environment's OSSYS shape (`readiness.schema` — the
        /// default basis).
        | AgreedEnv of label: string
        /// The authored model (`--against model` — the cutover's declared
        /// destination).
        | AuthoredModel of label: string

    [<RequireQualifiedAccess>]
    module TargetOperand =
        let label (t: TargetOperand) : string =
            match t with
            | TargetOperand.AgreedEnv l -> l
            | TargetOperand.AuthoredModel l -> l

        /// The masthead's basis phrase.
        let basisText (t: TargetOperand) : string =
            match t with
            | TargetOperand.AgreedEnv l -> sprintf "the agreed shape (%s)" l
            | TargetOperand.AuthoredModel l -> sprintf "the authored model (%s)" l

    /// One finding — a keyed divergence with its per-environment evidence.
    /// `Envs` carries `(environment, weight)` pairs: the weight is the
    /// count-evidence (NULL rows, orphan rows, channel-change count) that
    /// ranks the finding and fills its statement. The `Statement` and `Lever`
    /// are minted ONCE here (the presentation contract's row), so the board
    /// and `estate.json` cannot drift.
    type Finding =
        {
            Key       : FindingKey
            Kind      : EstateFindingKind
            Lane      : EstateLane
            Plane     : EstatePlane
            Envs      : (string * int64) list
            Statement : string
            /// The one next move, when its artifact exists. `None` renders no
            /// dangling pointer (the remediation artifacts join the board at
            /// their own wave; a lever is never promised before it exists).
            Lever     : string option
            /// The fork witness (wave A6): at least two environments diverge
            /// from the target DIFFERENTLY on this subject — both changed,
            /// and no promotion order explains it. Any forked finding turns
            /// the estate verdict to `Forked`.
            Fork      : bool
        }

    /// The impact rank — the largest consequence anywhere in the estate.
    let weightOf (f: Finding) : int64 =
        f.Envs |> List.map snd |> List.fold max 0L

    /// How one environment's data evidence reached this run — the masthead's
    /// honesty line (RT-7: a decision line never hides the age of what it
    /// stands on). `compute` defaults to the live pair (`Live` when a profile
    /// rode the operand, `Absent` when none did); the face overrides from the
    /// evidence store's actual path via `withEvidence`.
    [<RequireQualifiedAccess>]
    type EvidenceProvenance =
        /// Profiled during this run (no store, a first capture, or `--refresh`).
        | Live
        /// The store's evidence, reused under clean fingerprints.
        | Cached of capturedAtUtc: DateTimeOffset * ageDays: int * kindCount: int
        /// Re-profiled this run — fingerprints moved; the moved kinds named.
        | Refreshed of movedKinds: string list
        /// The store's evidence reused unprobed (`--offline`) — every verdict
        /// standing on it is advisory.
        | Offline of capturedAtUtc: DateTimeOffset * ageDays: int
        /// No data evidence reached this run — the data plane is
        /// advisory-silent for this environment.
        | Absent

    /// Whether a durable evidence store backed this run — the masthead states
    /// it either way (a disabled store is a live-only run, said, never
    /// silent). `compute` is store-blind and defaults to `Disabled`; the face
    /// stamps the resolved basis via `withEvidence`.
    [<RequireQualifiedAccess>]
    type EvidenceStoreBasis =
        | Enabled of dir: string
        | Disabled

    /// One environment's masthead line: whether live data evidence backed its
    /// data-plane verdicts, and how that evidence reached the run.
    type EnvBasis =
        {
            Env                   : string
            DataEvidenceAvailable : bool
            Provenance            : EvidenceProvenance
        }

    /// The estate verdict — the ONLY verdict vocabulary on the surface
    /// (Appendix A.4). Unified: no findings. Converging: findings exist and
    /// every one carries a lawful disposition. Forked: at least one finding
    /// where the environments disagree AMONG THEMSELVES — both changed,
    /// differently; no promotion order explains it, and no single adoption
    /// resolves it (wave A6).
    [<RequireQualifiedAccess>]
    type Verdict =
        | Unified
        | Converging
        | Forked

    /// The assembled estate report — the one value the board, the JSON, and
    /// the exit code project.
    type EstateReport =
        {
            Target   : TargetOperand
            Bases    : EnvBasis list
            Findings : Finding list
            Verdict  : Verdict
            Evidence : EvidenceStoreBasis
            /// The remediation artifacts this run wrote — (file, block
            /// count) per environment (wave A5; the face stamps them after
            /// writing, `compute` stays file-blind).
            Remediation : (string * int) list
            /// The interim-posture artifacts this run wrote — the overlay's
            /// entry count when `estate.overlay.json` + `estate.probes.sql`
            /// exist (wave A6; the face stamps them, `compute` stays
            /// file-blind). `None` = no proposed relaxations this run.
            OverlayEntries : int option
        }

    // ----------------------------------------------------------------------
    // The aggregation — pure over resolved operands.
    // ----------------------------------------------------------------------

    let private humane (n: int) : string =
        (int64 n).ToString("N0", System.Globalization.CultureInfo.InvariantCulture)

    let private humane64 (n: int64) : string =
        n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)

    /// The operator-facing name of a kind key, resolved against the catalog
    /// that carries it; the identity's display root when the catalog does not
    /// (an impossible state on the constructing side, kept total).
    let private kindNameIn (catalog: Catalog) (key: SsKey) : string =
        match Catalog.tryFindKind key catalog with
        | Some k -> Name.value k.Name
        | None   -> SsKey.rootOriginal key

    /// The comma-joined environment list of a grouped finding — "dev and uat"
    /// / "dev, qa, and uat" (the finding names its environments; THE_VOICE
    /// rule 10, the exact referent).
    let private envListText (envs: string list) : string =
        match envs with
        | [] -> "no environment"
        | [ one ] -> one
        | [ a; b ] -> sprintf "%s and %s" a b
        | many ->
            let front = many |> List.take (List.length many - 1) |> String.concat ", "
            sprintf "%s, and %s" front (List.last many)

    // -- The estate consensus (wave A2) --------------------------------------

    /// The decision floor: a clean per-environment verdict observed over
    /// fewer rows than this renders ADVISORY on the board — the sample is
    /// too small to license an estate-grade conclusion. A named constant
    /// until the estate config knobs land with their consumer wave (A44 —
    /// never an expressible-but-inert key before its consumer).
    let decisionFloor : int64 = 100L

    /// The rowcount-asymmetry factor (D12, wave A3): when one environment
    /// holds at least this many times the rows of another for one kind,
    /// verdicts drawn on the small side's evidence are advisory. A named
    /// constant, same A44 discipline as the decision floor.
    let asymmetryFactor : int64 = 100L

    /// The repair band (D3′/D1-relax, wave A6): the contradicting-row count
    /// past which a prepared repair defers to the interim relaxation — a
    /// 113,000-orphan UPDATE/DELETE is not an operator's afternoon; the
    /// posture (untrack / keep nullable) with its reopen probe is the
    /// honest interim. The default; `readiness.estate.repairBand` overrides
    /// it (consumed in this wave — A44, never an inert key).
    let repairBandDefault : int64 = 100_000L

    /// The operator-controlled inputs `computeWith` reads beside the
    /// resolved operands (wave A6): the repair band, and the loaded
    /// config's ACTIVE interim posture — the relaxation keys the bound
    /// tightening policy carries. OSSYS identities are espace-stable, so
    /// keys bound against the target catalog match every environment's
    /// profile evidence.
    type Posture =
        {
            RepairBand        : int64
            /// Reference keys the loaded posture keeps untracked
            /// (`referenceOverrides` + `keepUntracked`).
            RelaxedReferences : Set<SsKey>
            /// Attribute keys the loaded posture keeps nullable
            /// (budget-less nullability `overrides` + `keepNullable`).
            RelaxedAttributes : Set<SsKey>
        }

    [<RequireQualifiedAccess>]
    module Posture =
        /// No active posture, the default band — `compute`'s basis.
        let defaults : Posture =
            { RepairBand        = repairBandDefault
              RelaxedReferences = Set.empty
              RelaxedAttributes = Set.empty }

    /// Decide once on the evidence JOIN: fold every environment's profile
    /// through `Profile.merge` (commutative, associative, `Profile.empty`
    /// identity — worst case per axis) and run the fidelity engine against
    /// the target's declared model. Deciding on the join IS the unanimity
    /// consensus — a violation appears here exactly when at least one
    /// environment's evidence carries it, so the board's per-environment
    /// grouping and the single estate-grade decision cannot disagree (the
    /// union law, property-tested).
    let decideOnJoin (targetCatalog: Catalog) (profiles: Profile list) : ModelFidelity.DataViolation list =
        let joined = profiles |> List.fold Profile.merge Profile.empty
        (ModelFidelity.compose "the estate" targetCatalog joined { Decisions = [] } []).DataViolations

    /// One environment's contribution to a finding, pre-grouping. `Reference`
    /// carries the data-plane coordinate (the clean-environment attribution
    /// resolves evidence through it); schema-plane contributions carry `None`.
    /// `Signature` is the fork witness (wave A6): a deterministic rendering
    /// of HOW this environment diverges from the target — two environments
    /// on one subject with DIFFERENT signatures have both changed,
    /// differently, and no promotion order explains that. Compared, never
    /// displayed; carried only by the schema channels whose divergence
    /// content is comparable.
    type private EnvContribution =
        {
            Kind      : EstateFindingKind
            Subject   : string
            Reference : ModelFidelity.EntityColumn option
            Env       : string
            Fragment  : string
            Weight    : int64
            Signature : string option
        }

    // -- Schema-plane findings (one env against the target) -----------------

    /// The per-kind channel divergences of one environment against the target
    /// shape. `diff = CatalogDiff.between targetCatalog envCatalog`, so
    /// `added` = kinds the ENVIRONMENT carries that the target does not, and
    /// `removed` = kinds the target declares that the environment does not.
    let private schemaFindingsOf
        (env: string)
        (targetCatalog: Catalog)
        (envCatalog: Catalog)
        (diff: CatalogDiff)
        : EnvContribution list =
        let contribution (kind: EstateFindingKind) (subject: string) (signature: string option) (fragment: string) (weight: int64) : EnvContribution =
            { Kind = kind; Subject = subject; Reference = None; Env = env; Fragment = fragment; Weight = weight; Signature = signature }
        // The direction classifier (T1, wave A3): a kind an environment
        // carries BEYOND the target is deployed-ahead drift (a ruling);
        // a kind the target declares that an environment has not received
        // is promotion lag (watchable — the ordinary publish resolves it).
        let presenceInEnv =
            CatalogDiff.added diff
            |> Set.toList
            |> List.map (fun key ->
                let name = kindNameIn envCatalog key
                contribution EstateFindingKind.SchemaPresence name None
                    (sprintf "%s exists in %s and is absent from the target shape — deployed-ahead drift; no promotion explains it" name env) 1L)
        let presenceInTarget =
            CatalogDiff.removed diff
            |> Set.toList
            |> List.map (fun key ->
                let name = kindNameIn targetCatalog key
                contribution EstateFindingKind.SchemaLag name None
                    (sprintf "the target shape declares %s and %s does not carry it — promotion lag; the ordinary publish resolves it" name env) 1L)
        let renames =
            CatalogDiff.renamed diff
            |> Map.toList
            |> List.map (fun (key, _) ->
                let targetName = kindNameIn targetCatalog key
                let envName = kindNameIn envCatalog key
                contribution EstateFindingKind.SchemaRename targetName (Some envName)
                    (sprintf "%s is named %s in %s" targetName envName env) 1L)
        // The fork witness (wave A6): F#'s structural `%A` rendering of the
        // channel diff is deterministic for equal values — an EQUALITY
        // token, never operator-facing text.
        let channel
            (kind: EstateFindingKind)
            (noun: string)
            (diffs: Map<SsKey, ChannelDiff<'change>>)
            : EnvContribution list =
            diffs
            |> Map.toList
            |> List.map (fun (key, d) ->
                let name = kindNameIn targetCatalog key
                let count =
                    Set.count d.Added + Set.count d.Removed
                    + Map.count d.Renamed + List.length d.Reshaped
                contribution kind name (Some (sprintf "%A" d))
                    (sprintf "%s's %s differ from the target shape in %s (%s difference(s))"
                        name noun env (humane count))
                    (int64 count))
        let facets =
            CatalogDiff.kindFacetDiffs diff
            |> Map.toList
            |> List.map (fun (key, fs) ->
                let name = kindNameIn targetCatalog key
                contribution EstateFindingKind.SchemaFacets name (Some (sprintf "%A" fs))
                    (sprintf "%s's own facets differ from the target shape in %s (%s facet(s))"
                        name env (humane (Set.count fs)))
                    (int64 (Set.count fs)))
        presenceInEnv
        @ presenceInTarget
        @ renames
        @ channel EstateFindingKind.SchemaAttributes "columns" (CatalogDiff.attributeDiffs diff)
        @ channel EstateFindingKind.SchemaReferences "relationships" (CatalogDiff.referenceDiffs diff)
        @ channel EstateFindingKind.SchemaIndexes "indexes" (CatalogDiff.indexDiffs diff)
        @ facets

    // -- Data-plane findings (one env's data against the target's model) ----

    /// `sentinelZeroOf` answers how many of a coordinate's observed values
    /// are the unset reference `0` (the categorical distribution's witness,
    /// D3a — `None` when the evidence does not carry it; the split is never
    /// fabricated). `isTextTyped` answers whether the coordinate is a Text
    /// column (D1×D5 — empty text folds into the NULL count at ingestion,
    /// NM-18, and normalizes to NULL on publish; the statement says so).
    /// `attrKeyFor` / `refKeyFor` resolve the coordinate to its logical
    /// keys (wave A6): a violation whose coordinate the ACTIVE posture
    /// already relaxes returns `None` — the posture's own line owns that
    /// fact (its meter), and one fact never renders twice. A violation
    /// past the repair band lands on the RELAX lane's proposed kind
    /// instead of the repair queue (D3′ / the D1 relax arm).
    let private dataFindingOf
        (env: string)
        (posture: Posture)
        (sentinelZeroOf: ModelFidelity.EntityColumn -> int64 option)
        (isTextTyped: ModelFidelity.EntityColumn -> bool)
        (attrKeyFor: ModelFidelity.EntityColumn -> SsKey option)
        (refKeyFor: ModelFidelity.EntityColumn -> SsKey option)
        (v: ModelFidelity.DataViolation)
        : EnvContribution option =
        let subject = ModelFidelity.entityColumnText v.Reference
        let contribution (kind: EstateFindingKind) (fragment: string) (weight: int64) : EnvContribution option =
            Some
                { Kind = kind; Subject = subject; Reference = Some v.Reference
                  Env = env; Fragment = fragment; Weight = weight; Signature = None }
        let relaxedAttr () =
            match attrKeyFor v.Reference with
            | Some key -> Set.contains key posture.RelaxedAttributes
            | None -> false
        let relaxedRef () =
            match refKeyFor v.Reference with
            | Some key -> Set.contains key posture.RelaxedReferences
            | None -> false
        match v.Kind with
        | ModelFidelity.NotNullButNullsPresent _ when relaxedAttr () -> None
        | ModelFidelity.NotNullButNullsPresent n when n > posture.RepairBand ->
            contribution EstateFindingKind.DataNotNullPastBand
                (sprintf "%s declares NOT NULL; %s contradicting row(s) in %s exceed the repair band — the interim relaxation keeps the column nullable, and the reopen probe retires it at zero"
                    subject (humane64 n) env) n
        | ModelFidelity.NotNullButNullsPresent n ->
            let count = if n > 0L then sprintf "%s NULL row(s)" (humane64 n) else "NULL rows"
            let emptyTextClause =
                if n > 0L && isTextTyped v.Reference
                then " (the count includes empty text, which normalizes to NULL on publish)"
                else ""
            contribution EstateFindingKind.DataNotNull
                (sprintf "%s declares NOT NULL; %s holds %s%s" subject env count emptyTextClause) (max n 1L)
        | ModelFidelity.UniqueButDuplicatesPresent ->
            contribution EstateFindingKind.DataUnique
                (sprintf "%s declares unique; %s holds duplicate values" subject env) 1L
        | ModelFidelity.ForeignKeyOrphans _ when relaxedRef () -> None
        | ModelFidelity.ForeignKeyOrphans n ->
            let zeros =
                match sentinelZeroOf v.Reference with
                | Some z when z > 0L -> min z n
                | _ -> 0L
            // The TRUE orphan count — past the sentinel-zero split (the
            // unset references clear to NULL; they never need the band).
            let trueOrphans = n - zeros
            if trueOrphans > posture.RepairBand then
                contribution EstateFindingKind.DataOrphansPastBand
                    (sprintf "%s: %s true orphan row(s) in %s exceed the repair band — the interim relaxation keeps the relationship untracked, and the reopen probe retires it at zero"
                        subject (humane64 trueOrphans) env) trueOrphans
            else
                let sentinelClause =
                    if zeros > 0L then sprintf ", of which %s reference the unset value 0" (humane64 zeros)
                    else ""
                contribution EstateFindingKind.DataOrphans
                    (sprintf "%s: %s row(s) in %s reference a record that does not exist%s"
                        subject (humane64 n) env sentinelClause) (max n 1L)
        | ModelFidelity.LengthOrTypeOverflow (observed, declared) ->
            contribution EstateFindingKind.DataOverflow
                (sprintf "%s holds values to %s against a declared %s in %s"
                    subject observed declared env) 1L

    // -- The A3 detectors: trust census, rowcount asymmetry, candidacy ------

    /// A reference's operator-facing display, resolved against the catalog
    /// that carries it: (kind name, source attribute name, target name).
    let private referenceDisplay (catalog: Catalog) (refKey: SsKey) : (string * string * string) option =
        Catalog.allKinds catalog
        |> List.tryPick (fun k ->
            k.References
            |> List.tryFind (fun r -> r.SsKey = refKey)
            |> Option.map (fun r ->
                let attrName =
                    Kind.tryFindAttribute r.SourceAttribute k
                    |> Option.map (fun a -> Name.value a.Name)
                    |> Option.defaultValue (SsKey.rootOriginal r.SourceAttribute)
                Name.value k.Name, attrName, Name.value r.Name))

    /// The kind that carries a reference, resolved by the reference's key.
    let private kindCarrying (catalog: Catalog) (refKey: SsKey) : Kind option =
        Catalog.allKinds catalog
        |> List.tryFind (fun k -> k.References |> List.exists (fun r -> r.SsKey = refKey))

    /// A kind's representative observed row count in one profile — the
    /// maximum `ColumnProfile.RowCount` across the kind's attributes
    /// (Profile carries no per-kind axis; the maximum is the honest
    /// representative under sampling).
    let private kindRowCountIn (profile: Profile) (kind: Kind) : int64 option =
        match
            kind.Attributes
            |> List.choose (fun a -> Profile.tryFindColumn a.SsKey profile |> Option.map (fun c -> c.RowCount))
          with
        | [] -> None
        | counts -> Some (List.max counts)

    /// The untrusted-constraint census (S7/O3, wave A3): every relationship
    /// an environment enforces WITH NOCHECK is a preparable repair — the
    /// re-trust cost rides the statement when the rowcount evidence exists.
    let private trustFindingsOf
        (env: string)
        (envCatalog: Catalog)
        (profile: Profile)
        : EnvContribution list =
        profile.ForeignKeys
        |> List.filter (fun fk -> fk.IsNoCheck)
        |> List.choose (fun fk ->
            referenceDisplay envCatalog fk.ReferenceKey
            |> Option.map (fun (kindName, attrName, targetName) ->
                let subject = sprintf "%s.%s" kindName attrName
                let rows =
                    kindCarrying envCatalog fk.ReferenceKey
                    |> Option.bind (kindRowCountIn profile)
                let costClause =
                    match rows with
                    | Some n when n > 0L -> sprintf " — re-trusting scans %s row(s)" (humane64 n)
                    | _ -> ""
                { Kind = EstateFindingKind.SchemaTrust
                  Subject = subject
                  Reference = None
                  Env = env
                  Fragment =
                    sprintf "the relationship %s → %s is enforced WITH NOCHECK in %s (untrusted)%s"
                        subject targetName env costClause
                  Weight = rows |> Option.defaultValue 1L
                  Signature = None }))

    /// The rowcount-asymmetry advisories (D12, wave A3): a kind whose
    /// environments' observed row counts diverge past the asymmetry factor
    /// carries a WATCH finding naming both ends — verdicts drawn on the
    /// small side's evidence are advisory. No lever, by design.
    let private asymmetryContributions
        (logicalTarget: Catalog)
        (profilesByEnv: (string * Profile) list)
        : EnvContribution list =
        Catalog.allKinds logicalTarget
        |> List.collect (fun kind ->
            let counts =
                profilesByEnv
                |> List.choose (fun (env, p) -> kindRowCountIn p kind |> Option.map (fun c -> env, c))
            if List.length counts < 2 then []
            else
                let maxEnv, maxCount = counts |> List.maxBy snd
                let minEnv, minCount = counts |> List.minBy snd
                if maxCount >= decisionFloor && maxCount >= asymmetryFactor * max 1L minCount then
                    let name = Name.value kind.Name
                    [ { Kind = EstateFindingKind.DataAsymmetry
                        Subject = name
                        Reference = None
                        Env = maxEnv
                        Fragment = sprintf "%s holds %s row(s) in %s" name (humane64 maxCount) maxEnv
                        Weight = maxCount
                        Signature = None }
                      { Kind = EstateFindingKind.DataAsymmetry
                        Subject = name
                        Reference = None
                        Env = minEnv
                        Fragment =
                          sprintf "%s holds %s row(s) in %s — verdicts drawn on this evidence are advisory at the asymmetry"
                              name (humane64 minCount) minEnv
                        Weight = minCount
                        Signature = None } ]
                else [])

    /// The natural-key candidacies (D15, wave A3): a non-key column whose
    /// categorical evidence is distinct-in-every-observed-row in EVERY
    /// evidenced environment (per-environment unanimity — never the join:
    /// merged frequencies would wrongly kill candidates on values every
    /// environment legitimately shares), over at least the decision floor
    /// of summed observations. Advisory; WATCH.
    let private uniquenessCandidateContributions
        (logicalTarget: Catalog)
        (profilesByEnv: (string * Profile) list)
        : EnvContribution list =
        let pkAttrs =
            Catalog.allKinds logicalTarget
            |> List.collect (fun k ->
                k.Attributes |> List.filter (fun a -> a.IsPrimaryKey) |> List.map (fun a -> a.SsKey))
            |> Set.ofList
        Catalog.allKinds logicalTarget
        |> List.collect (fun kind ->
            kind.Attributes
            |> List.filter (fun a -> not (Set.contains a.SsKey pkAttrs))
            |> List.collect (fun a ->
                let evidenced =
                    profilesByEnv
                    |> List.choose (fun (env, p) ->
                        Profile.tryFindCategorical a.SsKey p |> Option.map (fun c -> env, c))
                let unanimous =
                    not (List.isEmpty evidenced)
                    && evidenced
                       |> List.forall (fun (_, c) ->
                           not c.IsTruncated
                           && c.DistinctCount = CategoricalDistribution.totalObservations c)
                let totalAcross =
                    evidenced |> List.sumBy (fun (_, c) -> CategoricalDistribution.totalObservations c)
                if unanimous && totalAcross >= decisionFloor then
                    let subject = sprintf "%s.%s" (Name.value kind.Name) (Name.value a.Name)
                    evidenced
                    |> List.map (fun (env, c) ->
                        let total = CategoricalDistribution.totalObservations c
                        { Kind = EstateFindingKind.DataUniquenessCandidate
                          Subject = subject
                          Reference = None
                          Env = env
                          Fragment =
                            sprintf "%s is distinct in every observed row of %s (%s of %s row(s))"
                                subject env (humane64 c.DistinctCount) (humane64 total)
                          Weight = total
                          Signature = None })
                else []))

    /// The headroom floor (D13, wave A4): a primary key consuming at least
    /// this percentage of its declared storage ceiling carries the advisory.
    /// A named constant, same A44 discipline as the decision floor.
    let headroomFloorPercent : int = 50

    /// D13 (wave A4): a primary key's observed maximum against its DECLARED
    /// storage ceiling — evidence-gated twice (the storage must be known,
    /// the numeric distribution must exist); never a guessed ceiling.
    let private headroomContributions
        (logicalTarget: Catalog)
        (profilesByEnv: (string * Profile) list)
        : EnvContribution list =
        Catalog.allKinds logicalTarget
        |> List.collect (fun kind ->
            kind.Attributes
            |> List.filter (fun a -> a.IsPrimaryKey)
            |> List.collect (fun a ->
                let ceiling =
                    match a.SqlStorage with
                    | Some SqlStorageType.Int -> Some (decimal Int32.MaxValue, "int's 2,147,483,647")
                    | Some SqlStorageType.BigInt -> Some (decimal Int64.MaxValue, "bigint's 9,223,372,036,854,775,807")
                    | _ -> None
                match ceiling with
                | None -> []
                | Some (cap, capText) ->
                    profilesByEnv
                    |> List.choose (fun (env, p) ->
                        Profile.tryFindNumeric a.SsKey p
                        |> Option.bind (fun d ->
                            let percent = int (d.Max / cap * 100M)
                            if percent >= headroomFloorPercent then
                                let subject = sprintf "%s.%s" (Name.value kind.Name) (Name.value a.Name)
                                Some
                                    { Kind = EstateFindingKind.DataHeadroom
                                      Subject = subject
                                      Reference = None
                                      Env = env
                                      Fragment =
                                        sprintf "%s stands at %s of %s in %s — %d%% of the ceiling is consumed"
                                            subject (d.Max.ToString("N0", Globalization.CultureInfo.InvariantCulture)) capText env percent
                                      Weight = int64 percent
                                      Signature = None }
                            else None))))

    /// D8 (wave A4): the platform's empty-date convention — a date column's
    /// categorical evidence carrying 1900-01-01 values reads as satisfied
    /// NOT NULL, empty of meaning. Witnessed or silent, never guessed.
    let private dateSentinelContributions
        (logicalTarget: Catalog)
        (profilesByEnv: (string * Profile) list)
        : EnvContribution list =
        Catalog.allKinds logicalTarget
        |> List.collect (fun kind ->
            kind.Attributes
            |> List.filter (fun a -> match a.Type with | Date | DateTime -> true | _ -> false)
            |> List.collect (fun a ->
                profilesByEnv
                |> List.choose (fun (env, p) ->
                    Profile.tryFindCategorical a.SsKey p
                    |> Option.bind (fun c ->
                        let sentinelCount =
                            c.Frequencies
                            |> List.filter (fun (value, _) -> value.StartsWith "1900-01-01")
                            |> List.sumBy snd
                        if sentinelCount > 0L then
                            let subject = sprintf "%s.%s" (Name.value kind.Name) (Name.value a.Name)
                            Some
                                { Kind = EstateFindingKind.DataDateSentinel
                                  Subject = subject
                                  Reference = None
                                  Env = env
                                  Fragment =
                                    sprintf "%s holds %s row(s) at 1900-01-01 in %s — the platform's empty-date convention; a NOT NULL reading of the column is satisfied and empty of meaning"
                                        subject (humane64 sentinelCount) env
                                  Weight = sentinelCount
                                  Signature = None }
                        else None))))

    /// D6 (wave A4): values a single-column unique declaration keeps
    /// distinct that a case-insensitive collation collapses — the unique
    /// index fails on unification. Witnessed by categorical evidence on the
    /// declared column.
    let private collationCollisionContributions
        (logicalTarget: Catalog)
        (profilesByEnv: (string * Profile) list)
        : EnvContribution list =
        let uniqueTextAttrs =
            Catalog.allKinds logicalTarget
            |> List.collect (fun kind ->
                kind.Indexes
                |> List.choose (fun ix ->
                    match ix.Uniqueness, ix.Columns with
                    | IndexUniqueness.Unique, [ column ] ->
                        kind.Attributes
                        |> List.tryFind (fun a -> a.SsKey = column.Attribute && a.Type = Text)
                        |> Option.map (fun a -> kind, a)
                    | _ -> None))
        uniqueTextAttrs
        |> List.collect (fun (kind, a) ->
            profilesByEnv
            |> List.choose (fun (env, p) ->
                Profile.tryFindCategorical a.SsKey p
                |> Option.bind (fun c ->
                    let collapsedPairs =
                        c.Frequencies
                        |> List.groupBy (fun (value, _) -> value.ToLowerInvariant())
                        |> List.sumBy (fun (_, values) -> max 0 (List.length values - 1))
                    if collapsedPairs > 0 then
                        let subject = sprintf "%s.%s" (Name.value kind.Name) (Name.value a.Name)
                        Some
                            { Kind = EstateFindingKind.DataCollationCollision
                              Subject = subject
                              Reference = None
                              Env = env
                              Fragment =
                                sprintf "under a case-insensitive collation, %s collapses %s case-distinct value(s) into duplicates in %s — the unique declaration fails on unification"
                                    subject (humane collapsedPairs) env
                              Weight = int64 collapsedPairs
                              Signature = None }
                    else None)))

    /// I3 (wave A4): identity provenance across the estate — kinds whose
    /// root differs from the target's for the SAME logical name (synthesized
    /// against native). Renames across such a pair cannot track by identity
    /// until it anchors. A uniformly-synthesized estate (fixtures; file
    /// models compared to file models) stays silent — the hazard is the MIX.
    let private synthesizedIdentityContributions
        (logicalTarget: Catalog)
        (logicalEnvs: (string * Catalog) list)
        : EnvContribution list =
        let isNative (key: SsKey) : bool =
            match SsKey.rootKey key with
            | OssysOriginal _ -> true
            | _ -> false
        let targetProvenance : Map<string, bool> =
            Catalog.allKinds logicalTarget
            |> List.map (fun k -> Name.value k.Name, isNative k.SsKey)
            |> Map.ofList
        logicalEnvs
        |> List.choose (fun (env, catalog) ->
            let mismatched =
                Catalog.allKinds catalog
                |> List.filter (fun k ->
                    match Map.tryFind (Name.value k.Name) targetProvenance with
                    | Some targetNative -> isNative k.SsKey <> targetNative
                    | None -> false)
                |> List.length
            if mismatched > 0 then
                Some
                    { Kind = EstateFindingKind.IdentitySynthesized
                      Subject = env
                      Reference = None
                      Env = env
                      Fragment =
                        sprintf "%s kind(s) in %s carry a different identity provenance than the target (synthesized against native) — renames across this pair are unstable until the identity anchors"
                            (humane mismatched) env
                      Weight = int64 mismatched
                      Signature = None }
            else None)

    /// O1 (wave A4): CDC parity — a kind tracked in some environments and
    /// not in other evidenced ones; a cutover write feeds live consumers
    /// unevenly. Needs at least two evidenced environments (parity is a
    /// comparison).
    let private cdcParityContributions
        (logicalTarget: Catalog)
        (profilesByEnv: (string * Profile) list)
        : EnvContribution list =
        if List.length profilesByEnv < 2 then []
        else
            Catalog.allKinds logicalTarget
            |> List.collect (fun kind ->
                let tracking =
                    profilesByEnv
                    |> List.filter (fun (_, p) -> CdcAwareness.isEnabled kind.SsKey p.CdcAwareness)
                    |> List.map fst
                let silent =
                    profilesByEnv
                    |> List.map fst
                    |> List.filter (fun env -> not (List.contains env tracking))
                if List.isEmpty tracking || List.isEmpty silent then []
                else
                    let name = Name.value kind.Name
                    tracking
                    |> List.map (fun env ->
                        { Kind = EstateFindingKind.OperationalCdc
                          Subject = name
                          Reference = None
                          Env = env
                          Fragment =
                            sprintf "CDC tracks %s in %s and not in %s — a cutover write to this kind feeds live consumers in %s alone"
                                name env (envListText silent) env
                          Weight = 1L
                          Signature = None }))

    /// The ACTIVE posture's lines (wave A6): every relaxation the loaded
    /// config carries renders — merged posture is config-fact, shown with
    /// its probe status per environment. Retirable ⇔ every EVIDENCED
    /// environment reads zero on the reopen probe's quantity AND at least
    /// one environment is evidenced (an estate-grade conclusion — the
    /// relaxation is one config for every environment, so one dirty
    /// environment keeps it active everywhere). An unevidenced environment
    /// says so — the meter is honest about what it has not observed.
    let private postureContributions
        (logicalTarget: Catalog)
        (posture: Posture)
        (envNames: string list)
        (profilesByEnv: (string * Profile) list)
        : EnvContribution list =
        let meterLines
            (kind: EstateFindingKind)
            (subject: string)
            (activeFragment: string -> string -> string)   // env -> countText -> fragment
            (unobservedFragment: string -> string)         // env -> fragment
            (retirableFragment: string -> string)          // env -> fragment
            (countIn: Profile -> int64 option)
            : EnvContribution list =
            let observed =
                profilesByEnv |> List.choose (fun (env, p) -> countIn p |> Option.map (fun n -> env, n))
            let retirable =
                not (List.isEmpty observed) && observed |> List.forall (fun (_, n) -> n = 0L)
            if retirable then
                observed
                |> List.map (fun (env, _) ->
                    { Kind = EstateFindingKind.PostureRetirable
                      Subject = subject
                      Reference = None
                      Env = env
                      Fragment = retirableFragment env
                      Weight = 1L
                      Signature = None })
            else
                let observedEnvs = observed |> List.map fst
                let unobserved = envNames |> List.filter (fun e -> not (List.contains e observedEnvs))
                (observed
                 |> List.map (fun (env, n) ->
                     { Kind = kind
                       Subject = subject
                       Reference = None
                       Env = env
                       Fragment = activeFragment env (humane64 n)
                       Weight = max n 1L
                       Signature = None }))
                @ (unobserved
                   |> List.map (fun env ->
                       { Kind = kind
                         Subject = subject
                         Reference = None
                         Env = env
                         Fragment = unobservedFragment env
                         Weight = 1L
                         Signature = None }))
        let referenceLines =
            posture.RelaxedReferences
            |> Set.toList
            |> List.collect (fun refKey ->
                match referenceDisplay logicalTarget refKey with
                | None -> []
                | Some (kindName, attrName, targetName) ->
                    let subject = sprintf "%s.%s" kindName attrName
                    meterLines EstateFindingKind.PostureActive subject
                        (fun env count ->
                            sprintf "the relationship %s → %s is untracked by the interim posture; the reopen probe stands at %s in %s"
                                subject targetName count env)
                        (fun env ->
                            sprintf "the relationship %s → %s is untracked by the interim posture; the reopen probe is unobserved in %s"
                                subject targetName env)
                        (fun env ->
                            sprintf "%s holds zero orphan row(s) in %s under the active relaxation — the relaxation is retirable; the relationship can track WITH CHECK"
                                subject env)
                        (fun p -> Profile.tryFindForeignKey refKey p |> Option.map (fun fk -> fk.OrphanCount)))
        let attributeLines =
            posture.RelaxedAttributes
            |> Set.toList
            |> List.collect (fun attrKey ->
                let display =
                    Catalog.allKinds logicalTarget
                    |> List.tryPick (fun k ->
                        k.Attributes
                        |> List.tryFind (fun a -> a.SsKey = attrKey)
                        |> Option.map (fun a -> sprintf "%s.%s" (Name.value k.Name) (Name.value a.Name)))
                match display with
                | None -> []
                | Some subject ->
                    meterLines EstateFindingKind.PostureActive subject
                        (fun env count ->
                            sprintf "%s is kept nullable by the interim posture; the reopen probe stands at %s in %s"
                                subject count env)
                        (fun env ->
                            sprintf "%s is kept nullable by the interim posture; the reopen probe is unobserved in %s"
                                subject env)
                        (fun env ->
                            sprintf "%s holds zero NULL row(s) in %s under the active relaxation — the relaxation is retirable; the column can tighten to its declared NOT NULL"
                                subject env)
                        (fun p -> Profile.tryFindColumn attrKey p |> Option.map (fun c -> c.NullCount)))
        referenceLines @ attributeLines

    // -- Grouping + the report ----------------------------------------------

    /// Compute the estate report from the resolved target and confirm
    /// operands, under the operator's posture (the repair band + the loaded
    /// config's active relaxations — wave A6). Every catalog is normalized
    /// to its espace-invariant logical shape first (A45), so a divergence
    /// is a REAL estate fact. Findings with one identity across
    /// environments group onto one key — the per-environment evidence
    /// rides the finding, and a strict majority of diverging environments
    /// turns the statement's closing clause around (the target, not the
    /// environments, may be the one behind).
    let computeWith
        (posture: Posture)
        (target: TargetOperand)
        (targetCatalog: Catalog)
        (envs: (string * Compare.Operand) list)
        : EstateReport =
        let logicalTarget = Readiness.toLogicalShape targetCatalog
        // The clean-environment attribution's evidence paths (wave A2): each
        // environment's profile, and the target's logical coordinates resolved
        // to attribute / reference identities once.
        let profileByEnv : Map<string, Profile> =
            envs
            |> List.choose (fun (env, operand) -> operand.Profile |> Option.map (fun p -> env, p))
            |> Map.ofList
        let attributeKeyOf : Map<string * string, SsKey> =
            logicalTarget
            |> Catalog.allKinds
            |> List.collect (fun k ->
                k.Attributes |> List.map (fun a -> (Name.value k.Name, Name.value a.Name), a.SsKey))
            |> Map.ofList
        let referenceKeyOf : Map<string * string, SsKey> =
            logicalTarget
            |> Catalog.allKinds
            |> List.collect (fun k ->
                k.References
                |> List.choose (fun r ->
                    Kind.tryFindAttribute r.SourceAttribute k
                    |> Option.map (fun a -> (Name.value k.Name, Name.value a.Name), r.SsKey)))
            |> Map.ofList
        let typeOf : Map<SsKey, PrimitiveType> =
            logicalTarget
            |> Catalog.allKinds
            |> List.collect (fun k -> k.Attributes |> List.map (fun a -> a.SsKey, a.Type))
            |> Map.ofList
        let perEnv =
            envs
            |> List.map (fun (env, operand) ->
                let logicalEnv = Readiness.toLogicalShape operand.Catalog
                let compare =
                    Compare.compute
                        { operand with Label = env; Catalog = logicalEnv }
                        { Label = TargetOperand.label target; Catalog = logicalTarget; Profile = None }
                let schema =
                    match compare.SchemaDelta with
                    | Some diff -> schemaFindingsOf env logicalTarget logicalEnv diff
                    | None -> []
                // The coordinate lookups the data-plane refinements read
                // (wave A3): the zero-sentinel witness from this env's
                // categorical evidence, and the Text-typing of the target's
                // declared column.
                let sentinelZeroFor (ec: ModelFidelity.EntityColumn) : int64 option =
                    operand.Profile
                    |> Option.bind (fun p ->
                        Map.tryFind (ec.Entity, ec.Column) attributeKeyOf
                        |> Option.bind (fun key -> Profile.tryFindCategorical key p)
                        |> Option.bind (fun c ->
                            c.Frequencies
                            |> List.tryFind (fun (value, _) -> value = "0")
                            |> Option.map snd))
                let isTextTyped (ec: ModelFidelity.EntityColumn) : bool =
                    Map.tryFind (ec.Entity, ec.Column) attributeKeyOf
                    |> Option.bind (fun key -> Map.tryFind key typeOf)
                    |> Option.map (fun t -> t = Text)
                    |> Option.defaultValue false
                let attrKeyFor (ec: ModelFidelity.EntityColumn) : SsKey option =
                    Map.tryFind (ec.Entity, ec.Column) attributeKeyOf
                let refKeyFor (ec: ModelFidelity.EntityColumn) : SsKey option =
                    Map.tryFind (ec.Entity, ec.Column) referenceKeyOf
                let data =
                    compare.DataDealbreakers
                    |> List.choose (dataFindingOf env posture sentinelZeroFor isTextTyped attrKeyFor refKeyFor)
                let trust =
                    match operand.Profile with
                    | Some p -> trustFindingsOf env logicalEnv p
                    | None -> []
                env, compare.DataEvidenceAvailable, schema @ data @ trust)
        let bases =
            perEnv
            |> List.map (fun (env, evidence, _) ->
                { Env = env
                  DataEvidenceAvailable = evidence
                  Provenance = if evidence then EvidenceProvenance.Live else EvidenceProvenance.Absent })
        let envCount = List.length perEnv
        // The clean-environment clause (wave A2; RT-6): the environments that
        // carry evidence for the finding's coordinate and DO NOT carry the
        // finding are named beside the divergence with their observation
        // basis — advisory beneath the decision floor. An environment with no
        // evidence for the coordinate stays silent here; the masthead already
        // names evidence-less environments estate-wide.
        let cleanClause
            (reference: ModelFidelity.EntityColumn option)
            (kind: EstateFindingKind)
            (dirtyEnvs: string list)
            : string =
            match reference with
            | None -> ""
            | Some ec ->
                let coordinate = ec.Entity, ec.Column
                let basisOf (env: string) : string option =
                    match Map.tryFind env profileByEnv with
                    | None -> None
                    | Some profile ->
                        match kind with
                        | EstateFindingKind.DataOrphans ->
                            Map.tryFind coordinate referenceKeyOf
                            |> Option.bind (fun key -> Profile.tryFindForeignKey key profile)
                            |> Option.filter (fun fk -> not fk.HasOrphan)
                            |> Option.map (fun _ -> sprintf "clean in %s" env)
                        | _ ->
                            Map.tryFind coordinate attributeKeyOf
                            |> Option.bind (fun key -> Profile.tryFindColumn key profile)
                            |> Option.map (fun c ->
                                if c.RowCount < decisionFloor then
                                    sprintf "clean in %s (%s row(s) observed — advisory; the sample is below the decision floor)"
                                        env (humane64 c.RowCount)
                                else
                                    sprintf "clean in %s (%s row(s) observed)" env (humane64 c.RowCount))
                let clauses =
                    perEnv
                    |> List.choose (fun (env, _, _) ->
                        if List.contains env dirtyEnvs then None else basisOf env)
                match clauses with
                | [] -> ""
                | cs -> sprintf "; %s" (String.concat "; " cs)
        // The cross-environment detectors (waves A3 + A4) read every profile
        // (and, for the identity plane, every normalized catalog) at once;
        // their contributions join the grouping input beside the per-env
        // streams.
        let crossEnv =
            let profilesByEnv = Map.toList profileByEnv
            let logicalEnvs =
                envs |> List.map (fun (env, operand) -> env, Readiness.toLogicalShape operand.Catalog)
            asymmetryContributions logicalTarget profilesByEnv
            @ uniquenessCandidateContributions logicalTarget profilesByEnv
            @ headroomContributions logicalTarget profilesByEnv
            @ dateSentinelContributions logicalTarget profilesByEnv
            @ collationCollisionContributions logicalTarget profilesByEnv
            @ synthesizedIdentityContributions logicalTarget logicalEnvs
            @ cdcParityContributions logicalTarget profilesByEnv
            @ postureContributions logicalTarget posture (envs |> List.map fst) profilesByEnv
        let findings =
            (perEnv |> List.collect (fun (_, _, contributions) -> contributions)) @ crossEnv
            |> List.groupBy (fun c -> c.Kind, c.Subject)
            |> List.map (fun ((kind, subject), rows) ->
                let plane = EstateFindingKind.planeOf kind
                let perEnvRows = rows |> List.sortBy (fun c -> c.Env)
                let envNames = perEnvRows |> List.map (fun c -> c.Env)
                let body = perEnvRows |> List.map (fun c -> c.Fragment) |> String.concat "; "
                let clean =
                    cleanClause (perEnvRows |> List.tryPick (fun c -> c.Reference)) kind envNames
                // The majority clause is a SHAPE conclusion — data findings
                // speak through their per-environment evidence and the clean
                // clause instead; a LAG majority is the normal pre-publish
                // state, never target-behind evidence, so lag never carries
                // it either (T1, wave A3).
                let majorityNote =
                    if plane = EstatePlane.Schema
                       && kind <> EstateFindingKind.SchemaLag
                       && envCount > 1
                       && List.length envNames * 2 > envCount then
                        sprintf
                            " Most environments differ from the target shape here (%s) — the target may be the one behind."
                            (envListText envNames)
                    else ""
                let key = FindingKey.create kind subject
                // The fork witness (wave A6): two environments on one
                // subject with DIFFERENT divergence signatures have both
                // changed, differently — no promotion order explains it,
                // and no single adoption resolves it.
                let fork =
                    plane = EstatePlane.Schema
                    && EstateFindingKind.laneOf kind = EstateLane.Decide
                    && (perEnvRows |> List.choose (fun c -> c.Signature) |> List.distinct |> List.length) >= 2
                let forkNote =
                    if fork then
                        " The environments disagree among themselves here — a fork; no single adoption resolves it."
                    else ""
                // The one lever per line (waves A5 + A6), minted from the
                // presentation contract's per-kind form: a ruling carries
                // its own imperative; a block review names the primary
                // (heaviest-evidence) environment's artifact — the file the
                // face writes in the same run; an overlay merge names the
                // entry by the finding's key (π-coherence); the watchable
                // kinds and the active posture carry none, by design.
                let lever =
                    match EstateFindingKind.leverFormOf kind with
                    | EstateLeverForm.Ruling imperative -> Some imperative
                    | EstateLeverForm.ReviewBlock ->
                        let primary = perEnvRows |> List.maxBy (fun c -> c.Weight)
                        Some (sprintf "Review block %s of estate.remediation.%s.sql." (FindingKey.text key) primary.Env)
                    | EstateLeverForm.MergeOverlayEntry ->
                        Some (sprintf "Merge overlay entry %s of estate.overlay.json." (FindingKey.text key))
                    | EstateLeverForm.NoLever -> None
                { Key = key
                  Kind = kind
                  Lane = EstateFindingKind.laneOf kind
                  Plane = plane
                  Envs = perEnvRows |> List.map (fun c -> c.Env, c.Weight)
                  Statement = (sprintf "%s%s.%s%s" body clean majorityNote forkNote).TrimEnd()
                  Lever = lever
                  Fork = fork })
            |> List.sortByDescending weightOf
        // The verdict formula (Appendix A.4): unified ⇔ nothing diverges
        // (an active posture keeps its own RELAX/REPAIR lines, so a
        // non-empty relaxation set can never read unified); forked ⇔ any
        // fork witness; converging otherwise — every finding carries a
        // lawful disposition.
        let verdict =
            if List.isEmpty findings then Verdict.Unified
            elif findings |> List.exists (fun f -> f.Fork) then Verdict.Forked
            else Verdict.Converging
        { Target = target
          Bases = bases
          Findings = findings
          Verdict = verdict
          Evidence = EvidenceStoreBasis.Disabled
          Remediation = []
          OverlayEntries = None }

    /// `computeWith` under no active posture and the default repair band —
    /// the zero-flag basis, and every pre-A6 call site verbatim.
    let compute
        (target: TargetOperand)
        (targetCatalog: Catalog)
        (envs: (string * Compare.Operand) list)
        : EstateReport =
        computeWith Posture.defaults target targetCatalog envs

    /// The face's remediation stamp: the artifacts it wrote this run —
    /// (file, block count) per environment (`compute` stays file-blind;
    /// the ARTIFACTS index and the JSON read the stamped list).
    let withRemediation (artifacts: (string * int) list) (report: EstateReport) : EstateReport =
        { report with Remediation = artifacts }

    /// The face's posture stamp (wave A6): the overlay's entry count once
    /// `estate.overlay.json` + `estate.probes.sql` are written — the
    /// ARTIFACTS index and the JSON read it; `compute` stays file-blind.
    let withOverlay (entries: int) (report: EstateReport) : EstateReport =
        { report with OverlayEntries = Some entries }

    /// The face's evidence stamp: the resolved store basis and each
    /// environment's actual acquisition path, applied onto a computed report
    /// (`compute` stays store-blind and pure; the boundary owns clocks and
    /// directories). An environment absent from the map keeps compute's
    /// default pair.
    let withEvidence
        (store: EvidenceStoreBasis)
        (provenance: Map<string, EvidenceProvenance>)
        (report: EstateReport)
        : EstateReport =
        { report with
            Evidence = store
            Bases =
                report.Bases
                |> List.map (fun b ->
                    match Map.tryFind b.Env provenance with
                    | Some p -> { b with Provenance = p }
                    | None -> b) }

    /// The estate is unified — the exit-0 predicate.
    let isUnified (report: EstateReport) : bool =
        report.Verdict = Verdict.Unified

    /// The lane's findings, board-ordered (impact-ranked within the lane).
    let laneFindings (lane: EstateLane) (report: EstateReport) : Finding list =
        report.Findings |> List.filter (fun f -> f.Lane = lane)

    /// The per-lane counts the verdict copy and the JSON both read.
    let laneCounts (report: EstateReport) : (EstateLane * int) list =
        [ EstateLane.Decide; EstateLane.Repair; EstateLane.Relax; EstateLane.Watch ]
        |> List.map (fun lane -> lane, List.length (laneFindings lane report))

    // ----------------------------------------------------------------------
    // The board — the rolled-up text projection (ten regions, fixed order;
    // CHAPTER_ESTATE_OPEN Appendix A.1). The VERDICT region renders through
    // the Voice catalog at the face (`estate.unified` / `estate.diverged`);
    // every other region renders here, from the same report value the JSON
    // projects (one substrate).
    // ----------------------------------------------------------------------

    let private laneTitle (lane: EstateLane) : string =
        match lane with
        | EstateLane.Decide -> "DECIDE — the ruling queue"
        | EstateLane.Repair -> "REPAIR — prepared repairs"
        | EstateLane.Relax  -> "RELAX — the interim posture"
        | EstateLane.Watch  -> "WATCH — advisories"

    let private laneEmptyLine (lane: EstateLane) : string =
        match lane with
        | EstateLane.Decide -> "  Nothing awaits a ruling."
        | EstateLane.Repair -> "  Nothing carries a repair."
        | EstateLane.Relax  -> "  The interim posture is empty."
        | EstateLane.Watch  -> "  Nothing is under watch."

    let private planeToken (p: EstatePlane) : string =
        match p with
        | EstatePlane.Schema -> "schema"
        | EstatePlane.Data -> "data"
        | EstatePlane.Identity -> "identity"
        | EstatePlane.Operational -> "operational"

    /// The per-lane cap — the top consequences are named; the remainder is
    /// counted (THE_VOICE §12: cap the breadth, name the remainder; the full
    /// list is `estate.json`'s, searchable, never scrollable).
    let private laneCap : int = 8

    /// The humane capture-age clause ("today" / "N day(s) ago").
    let private ageText (ageDays: int) : string =
        if ageDays <= 0 then "today" else sprintf "%s day(s) ago" (humane ageDays)

    /// The capped moved-kind enumeration — the first three named, the
    /// remainder counted (§12: cap the breadth, name the remainder).
    let private movedKindsText (moved: string list) : string =
        let shown = moved |> List.truncate 3
        let remainder = List.length moved - List.length shown
        if remainder > 0 then
            sprintf "%s, and %s more" (String.concat ", " shown) (humane remainder)
        else String.concat ", " shown

    /// One environment's masthead evidence clause — the provenance made
    /// legible (RT-7: capture age and fingerprint status ride the masthead).
    let private provenanceText (basis: EnvBasis) : string =
        match basis.Provenance with
        | EvidenceProvenance.Live ->
            "live data evidence, profiled this run"
        | EvidenceProvenance.Cached (_, age, kinds) ->
            sprintf "evidence captured %s; fingerprints clean across %s kind(s)" (ageText age) (humane kinds)
        | EvidenceProvenance.Refreshed moved ->
            sprintf "%s kind(s) moved since capture (%s) — re-profiled this run"
                (humane (List.length moved)) (movedKindsText moved)
        | EvidenceProvenance.Offline (_, age) ->
            sprintf "offline evidence, captured %s and unprobed — every verdict standing on it is advisory" (ageText age)
        | EvidenceProvenance.Absent ->
            "no data evidence this run — the data plane is advisory-silent"

    /// Render the board regions BELOW the verdict (the masthead through the
    /// action), from the one report value. The face renders the verdict Hero
    /// through the Voice catalog first, then these lines.
    let render (report: EstateReport) : string list =
        [ // MASTHEAD — the estate and its basis.
          yield sprintf "ESTATE — %s environment(s) against %s"
                    (humane (List.length report.Bases)) (TargetOperand.basisText report.Target)
          for basis in report.Bases do
              yield sprintf "  %-14s %s" basis.Env (provenanceText basis)
          yield
              (match report.Evidence with
               | EvidenceStoreBasis.Enabled dir ->
                   sprintf "  Evidence store: %s." dir
               | EvidenceStoreBasis.Disabled ->
                   "  Evidence reads live this run — no store is configured (PROJECTION_ESTATE_DIR, or the ledger directory's estate child, enables pay-once evidence).")
          // The fidelity clause, named (RT-10): the estate config cannot
          // name a fidelity flow yet (the container-proof wave brings it),
          // so the clause is not configured — said, never silent, and the
          // verdict formula excludes it.
          yield "  The fidelity clause is not configured; the verdict stands on the schema and data evidence."
          yield ""

          // The lanes — DECIDE → REPAIR → RELAX → WATCH, impact-ranked, capped.
          for lane in [ EstateLane.Decide; EstateLane.Repair; EstateLane.Relax; EstateLane.Watch ] do
              let findings = laneFindings lane report
              yield laneTitle lane
              match findings with
              | [] -> yield laneEmptyLine lane
              | fs ->
                  let shown = fs |> List.truncate laneCap
                  for f in shown do
                      yield sprintf "  %s" f.Statement
                      match f.Lever with
                      | Some lever -> yield sprintf "      → %s" lever
                      | None -> ()
                  let remainder = List.length fs - List.length shown
                  if remainder > 0 then
                      yield sprintf "  … and %s more — estate.json carries every finding." (humane remainder)
              yield ""

          // MATRIX — environment × plane counts (the drill-down door).
          yield "MATRIX — findings by environment and plane"
          if List.isEmpty report.Findings then
              yield "  No findings; the matrix is empty."
          else
              for basis in report.Bases do
                  let cells =
                      [ EstatePlane.Schema; EstatePlane.Data; EstatePlane.Identity; EstatePlane.Operational ]
                      |> List.map (fun plane ->
                          let count =
                              report.Findings
                              |> List.filter (fun f ->
                                  f.Plane = plane && f.Envs |> List.exists (fun (e, _) -> e = basis.Env))
                              |> List.length
                          sprintf "%s %s" (planeToken plane) (humane count))
                      |> String.concat " · "
                  yield sprintf "  %-14s %s" basis.Env cells
          yield ""

          // BURNDOWN — joins at the history wave; the first run says so.
          yield "BURNDOWN — this run is the estate's first recorded reading; movement renders from the next run."
          yield ""

          // ARTIFACTS — the index: one line per artifact naming its role.
          yield "ARTIFACTS"
          yield "  estate.json — the full findings record: every board element, machine-readable."
          for file, blocks in report.Remediation do
              yield sprintf "  %s — %s prepared repair block(s); the locating SELECT is active, every repair is commented." file (humane blocks)
          match report.OverlayEntries with
          | Some entries when entries > 0 ->
              yield sprintf "  estate.overlay.json — %s interim relaxation(s) as config edits; each carries the probe that retires it. The merge is an operator edit; the engine never applies it." (humane entries)
              yield "  estate.probes.sql — every reopen probe, runnable as one batch; the posture's retirement meter."
          | _ -> ()
          yield ""

          // ACTION — the one next move.
          let action =
              match laneFindings EstateLane.Decide report with
              | f :: _ -> sprintf "Next: rule the first DECIDE finding — %s" (FindingKey.text f.Key)
              | [] ->
                  match laneFindings EstateLane.Repair report with
                  | f :: _ -> sprintf "Next: review the first REPAIR finding — %s" (FindingKey.text f.Key)
                  | [] -> "Next: the estate holds; re-run on the publish cadence."
          yield action ]

    // ----------------------------------------------------------------------
    // The estate.json codec — the structured sibling of the board (one
    // substrate: both project the one report value).
    // ----------------------------------------------------------------------

    let private verdictToken (v: Verdict) : string =
        match v with
        | Verdict.Unified -> "unified"
        | Verdict.Converging -> "converging"
        | Verdict.Forked -> "forked"

    let private laneToken (lane: EstateLane) : string =
        match lane with
        | EstateLane.Decide -> "decide"
        | EstateLane.Repair -> "repair"
        | EstateLane.Relax -> "relax"
        | EstateLane.Watch -> "watch"

    /// One environment's provenance, projected for `estate.json` (one
    /// substrate: the same facts the masthead line renders).
    let private provenanceJson (p: EvidenceProvenance) : JsonObject =
        let o = JsonObject()
        match p with
        | EvidenceProvenance.Live ->
            o.["basis"] <- JsonValue.Create "live"
        | EvidenceProvenance.Cached (captured, age, kinds) ->
            o.["basis"] <- JsonValue.Create "cached"
            o.["capturedAtUtc"] <- JsonValue.Create(captured.ToString "O")
            o.["ageDays"] <- JsonValue.Create age
            o.["kindCount"] <- JsonValue.Create kinds
        | EvidenceProvenance.Refreshed moved ->
            o.["basis"] <- JsonValue.Create "refreshed"
            let arr = JsonArray()
            for kind in moved do arr.Add(JsonValue.Create kind)
            o.["movedKinds"] <- arr
        | EvidenceProvenance.Offline (captured, age) ->
            o.["basis"] <- JsonValue.Create "offline"
            o.["capturedAtUtc"] <- JsonValue.Create(captured.ToString "O")
            o.["ageDays"] <- JsonValue.Create age
        | EvidenceProvenance.Absent ->
            o.["basis"] <- JsonValue.Create "absent"
        o

    let toJson (report: EstateReport) : JsonObject =
        let root = JsonObject()
        root.["verdict"] <- JsonValue.Create(verdictToken report.Verdict)
        root.["target"] <- JsonValue.Create(TargetOperand.basisText report.Target)
        root.["evidenceStore"] <-
            JsonValue.Create(
                match report.Evidence with
                | EvidenceStoreBasis.Enabled dir -> dir
                | EvidenceStoreBasis.Disabled -> "disabled")
        let envs = JsonArray()
        for basis in report.Bases do
            let o = JsonObject()
            o.["env"] <- JsonValue.Create basis.Env
            o.["dataEvidenceAvailable"] <- JsonValue.Create basis.DataEvidenceAvailable
            o.["evidence"] <- provenanceJson basis.Provenance
            envs.Add o
        root.["environments"] <- envs
        let lanes = JsonObject()
        for lane, count in laneCounts report do
            lanes.[laneToken lane] <- JsonValue.Create count
        root.["lanes"] <- lanes
        let remediation = JsonArray()
        for file, blocks in report.Remediation do
            let o = JsonObject()
            o.["file"] <- JsonValue.Create file
            o.["blocks"] <- JsonValue.Create blocks
            remediation.Add o
        root.["remediation"] <- remediation
        (match report.OverlayEntries with
         | Some entries ->
             let o = JsonObject()
             o.["file"] <- JsonValue.Create "estate.overlay.json"
             o.["probes"] <- JsonValue.Create "estate.probes.sql"
             o.["entries"] <- JsonValue.Create entries
             root.["overlay"] <- o
         | None -> ())
        root.["fidelityClause"] <- JsonValue.Create "notConfigured"
        let findings = JsonArray()
        for f in report.Findings do
            let o = JsonObject()
            o.["key"] <- JsonValue.Create(FindingKey.text f.Key)
            o.["kind"] <- JsonValue.Create(EstateFindingKind.token f.Kind)
            o.["lane"] <- JsonValue.Create(laneToken f.Lane)
            o.["plane"] <- JsonValue.Create(planeToken f.Plane)
            o.["statement"] <- JsonValue.Create f.Statement
            (match f.Lever with
             | Some lever -> o.["lever"] <- JsonValue.Create lever
             | None -> ())
            if f.Fork then o.["fork"] <- JsonValue.Create true
            let perEnv = JsonArray()
            for env, weight in f.Envs do
                let e = JsonObject()
                e.["env"] <- JsonValue.Create env
                e.["weight"] <- JsonValue.Create weight
                perEnv.Add e
            o.["environments"] <- perEnv
            findings.Add o
        root.["findings"] <- findings
        root

    /// Serialize to a pretty-printed JSON string (the artifact body).
    let toJsonString (report: EstateReport) : string =
        let opts = System.Text.Json.JsonSerializerOptions(WriteIndented = true)
        (toJson report).ToJsonString(opts)
