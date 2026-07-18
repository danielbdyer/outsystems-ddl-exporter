namespace Projection.Pipeline

// LINT-ALLOW-FILE: the estate board's rolled-up text renderer + the environments.json
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
/// resolved operands. One substrate: the rendered board and `environments.json` are
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
    /// and `environments.json` cannot drift.
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

    /// The row-fidelity clause's state on this run (RT-10, wave A4β). The
    /// estate config's `fidelityFlow` decides whether the clause is part of
    /// the verdict at all: unconfigured, it is named and excluded (a never-run
    /// proof never holds the verdict hostage); configured, a green proof rides
    /// the masthead and the three non-green states each mint a DECIDE finding.
    /// The face computes it (reading `fidelity.rows.json` + its mtime); the
    /// engine folds it (`withFidelity`).
    [<RequireQualifiedAccess>]
    type FidelityClause =
        /// No `readiness.estate.fidelityFlow` — the clause is out of the verdict.
        | NotConfigured
        /// A proof exists, agrees, and is no older than this run's evidence.
        | Green of flow: string * ageDays: int
        /// The config names a flow but no proof artifact was found.
        | Missing of flow: string
        /// The proof predates this run's freshest evidence — the world moved.
        | Stale of flow: string * ageDays: int
        /// The proof reports differing rows — the load is not byte-faithful.
        | Diverged of flow: string * differingRows: int64

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
            /// entry count when `environments.overlay.json` + `environments.probes.sql`
            /// exist (wave A6; the face stamps them, `compute` stays
            /// file-blind). `None` = no proposed relaxations this run.
            OverlayEntries : int option
            /// The SSDT emission-fidelity findings (Phase 1, the #669 audit) —
            /// properties of the TARGET shape being promoted (would it deploy,
            /// does it model reality), not cross-environment divergences, so
            /// they carry their own list and their own board section.
            EmissionFindings : Finding list
            /// The movement since a recorded baseline (the burndown, wave A7)
            /// — the face stamps it from the evidence store's history;
            /// `compute` stays store-blind. `None` = no baseline (a first
            /// recorded reading, or no store).
            Burndown : Burndown option
            /// Consecutive UNIFIED runs, this one included (0 while the
            /// estate diverges) — the gate streak. Stamped with the burndown.
            Streak : int
            /// The row-fidelity clause's state (RT-10, wave A4β) — the face
            /// stamps it from `fidelity.rows.json` + the configured flow;
            /// `compute` stays proof-blind (I/O lives at the boundary).
            /// `NotConfigured` until stamped.
            Fidelity : FidelityClause
            /// Whether the static-content probe (D10/D11, wave A4β) ran this
            /// run — true when per-env static rows were threaded (the face's
            /// live probe), false on `compute` / `--offline`. Drives the
            /// coverage-honesty line (a clean verdict covers static content
            /// only when it was inspected).
            StaticInspected : bool
        }

    /// The movement between a recorded baseline reading and this run —
    /// findings keyed by `FindingKey`, so the burndown, the block ids, and
    /// the overlay entries all say one name (wave A7).
    and Burndown =
        {
            /// The baseline reading's run identity (`--since @runId`, or the
            /// latest recorded reading by default).
            SinceRunId   : string
            /// The baseline reading's age at this run, whole days.
            SinceAgeDays : int
            /// Findings present at the baseline and absent now — closed.
            Closed       : int
            /// Findings absent at the baseline and present now — opened.
            Opened       : int
            /// Findings present in both readings — remaining.
            Remaining    : int
            /// The oldest open finding's age in whole days (first-seen
            /// carried across readings); `None` when nothing is open.
            OldestDays   : int option
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
            /// The default repair band — the fix-vs-relax threshold for any
            /// entity without its own band.
            RepairBand        : int64
            /// Per-entity repair bands (`readiness.estate.repairBandByEntity`):
            /// 100,000 orphans means one thing in a 200-row lookup and another
            /// in a billion-row fact table, so the threshold is set per entity
            /// (by logical entity name), falling back to `RepairBand`.
            RepairBandByEntity : Map<string, int64>
            /// The decision floor (`readiness.estate.decisionFloor`) — the
            /// minimum observation an estate-grade conclusion needs. Defaults
            /// to `decisionFloor`; A44-tunable per estate (DECISIONS 2026-07-18).
            DecisionFloor : int64
            /// The rowcount-asymmetry factor (`readiness.estate.asymmetryFactor`)
            /// — the ratio past which the small side's evidence is advisory.
            /// Defaults to `asymmetryFactor`.
            AsymmetryFactor : int64
            /// The operator's declared promotion lattice
            /// (`readiness.estate.promotionOrder`), most-upstream first —
            /// enables the deployed↔deployed regime. Empty (the default) ⇒ the
            /// tool makes no promotion-order assumption and the regime is silent.
            PromotionOrder : string list
            /// Reference keys the loaded posture keeps untracked
            /// (`referenceOverrides` + `keepUntracked`).
            RelaxedReferences : Set<SsKey>
            /// Attribute keys the loaded posture keeps nullable
            /// (budget-less nullability `overrides` + `keepNullable`).
            RelaxedAttributes : Set<SsKey>
        }

    [<RequireQualifiedAccess>]
    module Posture =
        /// No active posture, the default band and thresholds — `compute`'s basis.
        let defaults : Posture =
            { RepairBand        = repairBandDefault
              RepairBandByEntity = Map.empty
              DecisionFloor     = decisionFloor
              AsymmetryFactor   = asymmetryFactor
              PromotionOrder    = []
              RelaxedReferences = Set.empty
              RelaxedAttributes = Set.empty }

    /// The per-run STATIC ROW content the D10/D11 detectors read (wave A4β).
    /// The estate carries only statistical `Profile` in its operands, and the
    /// OSSYS read marks a static entity `Modality.Static` WITHOUT its row
    /// content (the records ride a skipped metamodel result set) — so the face
    /// reads the actual rows (bounded; static tables are small reference data)
    /// and threads them here. `Seed` is the reference basis (the target's
    /// declared static content); `ByEnv` is each confirm environment's actual
    /// rows, per static kind (keyed by SsKey — espace-stable). `empty` is
    /// `compute`'s basis and the offline / no-static-kind case (no D10/D11
    /// findings; the coverage line stays honest).
    type StaticContent =
        {
            Seed  : Map<SsKey, StaticRow list>
            ByEnv : Map<string, Map<SsKey, StaticRow list>>
        }

    [<RequireQualifiedAccess>]
    module StaticContent =
        /// No static-row evidence — `compute`'s basis; the D10/D11 detectors
        /// produce nothing.
        let empty : StaticContent = { Seed = Map.empty; ByEnv = Map.empty }

    /// The logical entity a finding's subject names (the part before the
    /// first `.` in `Entity.Column`, or the whole token for an entity).
    let private entityOfSubject (subject: string) : string =
        match subject.IndexOf '.' with
        | i when i > 0 -> subject.Substring(0, i)
        | _ -> subject

    /// The repair band that governs one subject — the entity's own band when
    /// set, else the posture default (per-entity fix-vs-relax thresholds).
    let bandFor (posture: Posture) (subject: string) : int64 =
        posture.RepairBandByEntity
        |> Map.tryFind (entityOfSubject subject)
        |> Option.defaultValue posture.RepairBand

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
                    (sprintf "%s exists in %s but is missing from the target shape — no promotion added it" name env) 1L)
        let presenceInTarget =
            CatalogDiff.removed diff
            |> Set.toList
            |> List.map (fun key ->
                let name = kindNameIn targetCatalog key
                contribution EstateFindingKind.SchemaLag name None
                    (sprintf "the target shape declares %s and %s has not received it — the ordinary publish promotes it" name env) 1L)
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
        // One finding per differing facet — triggers, check constraints,
        // modality (static vs regular), and active state are named
        // distinctly, never lumped (the operator rules each on its own).
        let facets =
            CatalogDiff.kindFacetDiffs diff
            |> Map.toList
            |> List.collect (fun (key, fs) ->
                let name = kindNameIn targetCatalog key
                fs
                |> Set.toList
                |> List.map (fun facet ->
                    let kind, statement =
                        match facet with
                        | KindFacet.Triggers ->
                            EstateFindingKind.SchemaTrigger,
                            sprintf "%s's trigger set differs from the target shape in %s" name env
                        | KindFacet.ColumnChecks ->
                            EstateFindingKind.SchemaCheck,
                            sprintf "%s's check constraints differ from the target shape in %s" name env
                        | KindFacet.Modality ->
                            EstateFindingKind.SchemaModality,
                            sprintf "%s's entity kind (static vs regular) differs from the target shape in %s" name env
                        | KindFacet.IsActive ->
                            EstateFindingKind.SchemaActivity,
                            sprintf "%s's active state differs from the target shape in %s" name env
                    contribution kind name (Some (sprintf "%A" facet)) statement 1L))
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
    /// fabricated). `attrKeyFor` / `refKeyFor` resolve the coordinate to its logical
    /// keys (wave A6): a violation whose coordinate the ACTIVE posture
    /// already relaxes returns `None` — the posture's own line owns that
    /// fact (its meter), and one fact never renders twice. A violation
    /// past the repair band lands on the RELAX lane's proposed kind
    /// instead of the repair queue (D3′ / the D1 relax arm).
    let private dataFindingOf
        (env: string)
        (posture: Posture)
        (sentinelZeroOf: ModelFidelity.EntityColumn -> int64 option)
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
        | ModelFidelity.NotNullButNullsPresent n when n > bandFor posture subject ->
            contribution EstateFindingKind.DataNotNullPastBand
                (sprintf "%s is required (NOT NULL); %s NULL row(s) in %s exceed the repair band — leave the column nullable until they are backfilled"
                    subject (humane64 n) env) n
        | ModelFidelity.NotNullButNullsPresent n ->
            // Post-WP-3 (DECISIONS 2026-07-16): the empty string survives
            // distinct from NULL on every write path — it no longer folds into
            // the NULL count at ingestion, nor normalizes to NULL on publish.
            // The count is genuine NULLs only; the pre-WP-3 "includes empty
            // text" clause is retired with the erasure it described.
            let count = if n > 0L then sprintf "%s NULL row(s)" (humane64 n) else "NULL rows"
            contribution EstateFindingKind.DataNotNull
                (sprintf "%s is required (NOT NULL); %s holds %s" subject env count) (max n 1L)
        | ModelFidelity.UniqueButDuplicatesPresent ->
            contribution EstateFindingKind.DataUnique
                (sprintf "%s must be unique; %s holds duplicate values" subject env) 1L
        | ModelFidelity.ForeignKeyOrphans _ when relaxedRef () -> None
        | ModelFidelity.ForeignKeyOrphans n ->
            let zeros =
                match sentinelZeroOf v.Reference with
                | Some z when z > 0L -> min z n
                | _ -> 0L
            // The TRUE orphan count — past the sentinel-zero split (the
            // unset references clear to NULL; they never need the band).
            let trueOrphans = n - zeros
            if trueOrphans > bandFor posture subject then
                contribution EstateFindingKind.DataOrphansPastBand
                    (sprintf "%s has %s reference(s) to missing rows in %s, past the repair band — leave the relationship unenforced until they clear"
                        subject (humane64 trueOrphans) env) trueOrphans
            else
                let sentinelClause =
                    if zeros > 0L then sprintf ", of which %s reference the unset value 0" (humane64 zeros)
                    else ""
                contribution EstateFindingKind.DataOrphans
                    (sprintf "%s has %s reference(s) to rows that do not exist in %s%s"
                        subject (humane64 n) env sentinelClause) (max n 1L)
        | ModelFidelity.LengthOrTypeOverflow (observed, declared) ->
            contribution EstateFindingKind.DataOverflow
                (sprintf "%s holds values that exceed its column length setting — %s against a setting of %s — in %s"
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
        (decisionFloor: int64)
        (asymmetryFactor: int64)
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
                          sprintf "%s holds %s row(s) in %s — findings drawn from the smaller sample are advisory"
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
        (decisionFloor: int64)
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
                            sprintf "%s has no duplicate in %s — %s of %s row(s) are distinct, so it could serve as a business key for matching"
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
                                        sprintf "%s has reached %s of %s in %s — %d%% of the limit is used"
                                            subject (d.Max.ToString("N0", Globalization.CultureInfo.InvariantCulture)) capText env percent
                                      Weight = int64 percent
                                      Signature = None }
                            else None))))

    /// D8 (wave A4): a date column's empty-of-meaning sentinels — a value that
    /// satisfies a NOT-NULL reading but carries no real date. The census
    /// covers the OutSystems platform's stand-in `1900-01-01` AND the SQL
    /// Server `datetime` floor `1753-01-01` (a column pinned at its type's
    /// minimum reads the same way). Witnessed by categorical evidence, or
    /// silent — never guessed. The `9999-12-31` "far future" is deliberately
    /// NOT a sentinel here: it usually carries real intent ("never expires").
    let private dateSentinels : string list = [ "1900-01-01"; "1753-01-01" ]

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
                        // Sum every row sitting on a sentinel, and name the
                        // sentinel that carries the most (the one the operator
                        // sees first) so the statement stays concrete.
                        let hits =
                            dateSentinels
                            |> List.choose (fun s ->
                                let n =
                                    c.Frequencies
                                    |> List.filter (fun (value, _) -> value.StartsWith s)
                                    |> List.sumBy snd
                                if n > 0L then Some (s, n) else None)
                        let sentinelCount = hits |> List.sumBy snd
                        if sentinelCount > 0L then
                            let leadSentinel = hits |> List.maxBy snd |> fst
                            let subject = sprintf "%s.%s" (Name.value kind.Name) (Name.value a.Name)
                            Some
                                { Kind = EstateFindingKind.DataDateSentinel
                                  Subject = subject
                                  Reference = None
                                  Env = env
                                  Fragment =
                                    sprintf "%s holds %s row(s) set to %s in %s — a stand-in for an empty date; a required-column reading is satisfied, but the dates carry no real value"
                                        subject (humane64 sentinelCount) leadSentinel env
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
                        sprintf "%s kind(s) in %s number their rows differently than the target — the key is generated by the database in one and a fixed value in the other, so renames stay unstable until the identity is anchored"
                            (humane mismatched) env
                      Weight = int64 mismatched
                      Signature = None }
            else None)

    /// The fourth comparison regime (DECISIONS 2026-07-18): deployed↔deployed
    /// across the promotion lattice. The estate compares each environment to
    /// the TARGET; this compares each environment to its UPSTREAM promotion
    /// SOURCE — the adjacent pair in the `readiness.confirm` order, read
    /// most-upstream first (Dev → QA → UAT → PROD). A kind a DOWNSTREAM
    /// environment carries that its upstream source LACKS reached the
    /// downstream without passing through its source — a change that bypassed
    /// the promotion path (a hotfix applied straight to the downstream).
    /// Advisory (a bypass may be a sanctioned emergency change); the operator
    /// confirms the order is intended. This is drift the target-anchored
    /// per-environment diff cannot express — it names each environment's delta
    /// from the target, never the promotion CHAIN's own monotonicity.
    /// The promotion order is the operator's declared lattice
    /// (`readiness.estate.promotionOrder`), NOT the environment-list order —
    /// the tool never guesses which environment is upstream. Absent ⇒ no
    /// assumption, and the regime is silent (an empty chain).
    let private promotionOrderContributions
        (promotionOrder: string list)
        (logicalEnvByEnv: Map<string, Catalog>)
        : EnvContribution list =
        let kindNames (c: Catalog) =
            Catalog.allKinds c |> List.map (fun k -> Name.value k.Name) |> Set.ofList
        // The declared chain, restricted to the environments actually read.
        promotionOrder
        |> List.choose (fun env -> Map.tryFind env logicalEnvByEnv |> Option.map (fun c -> env, c))
        |> List.pairwise
        |> List.collect (fun ((upstream, uCat), (downstream, dCat)) ->
            Set.difference (kindNames dCat) (kindNames uCat)
            |> Set.toList
            |> List.sort
            |> List.map (fun kindName ->
                { Kind = EstateFindingKind.SchemaPromotionOrder
                  Subject = kindName
                  Reference = None
                  Env = downstream
                  Fragment =
                    sprintf "%s exists in %s but not in %s, its upstream promotion source — a change reached %s without passing through %s"
                        kindName downstream upstream downstream upstream
                  Weight = 1L
                  Signature = None }))

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
                            sprintf "Change tracking is on for %s in %s and off in %s — a cutover write feeds live consumers in %s alone"
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
                            sprintf "%s → %s is left unenforced for now; %s reference(s) still point to missing rows in %s"
                                subject targetName count env)
                        (fun env ->
                            sprintf "%s → %s is left unenforced for now; the count is unobserved in %s"
                                subject targetName env)
                        (fun env ->
                            sprintf "%s has zero references to missing rows in %s now — the relationship can be enforced again"
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
                            sprintf "%s is left nullable for now; %s row(s) are still NULL in %s"
                                subject count env)
                        (fun env ->
                            sprintf "%s is left nullable for now; the count is unobserved in %s"
                                subject env)
                        (fun env ->
                            sprintf "%s has zero NULL row(s) in %s now — the column can be required (NOT NULL) again"
                                subject env)
                        (fun p -> Profile.tryFindColumn attrKey p |> Option.map (fun c -> c.NullCount)))
        referenceLines @ attributeLines

    // -- Grouping + the report ----------------------------------------------

    // -- The emission-audit detectors (Phase 1, the #669 audit) — properties
    //    of the TARGET shape (would it deploy, does it model reality), not
    //    cross-environment divergences. ------------------------------------

    /// One emission finding — a target-shape property with no per-environment
    /// evidence; DECIDE-lane, its ruling as the lever.
    let private emissionFinding (kind: EstateFindingKind) (subject: string) (statement: string) : Finding =
        { Key       = FindingKey.create kind subject
          Kind      = kind
          Lane      = EstateFindingKind.laneOf kind
          Plane     = EstateFindingKind.planeOf kind
          Envs      = []
          Statement = statement
          Lever     =
            match EstateFindingKind.leverFormOf kind with
            | EstateLeverForm.Ruling imperative -> Some imperative
            | _ -> None
          Fork      = false }

    /// WP-12: a relationship whose target entity has a COMPOSITE primary key.
    /// The emitter renders a single-column foreign key that references only
    /// the target's first key column — SQL Server rejects it at deploy.
    let private emissionCompositePkFkFindings (target: Catalog) : Finding list =
        Catalog.allKinds target
        |> List.collect (fun k ->
            k.References
            |> List.choose (fun r ->
                match Catalog.tryFindKind r.TargetKind target with
                | Some targetKind when List.length (Kind.primaryKey targetKind) > 1 ->
                    let sourceCol =
                        k.Attributes
                        |> List.tryFind (fun a -> a.SsKey = r.SourceAttribute)
                        |> Option.map (fun a -> Name.value a.Name)
                        |> Option.defaultValue "?"
                    let subject = sprintf "%s.%s → %s" (Name.value k.Name) sourceCol (Name.value targetKind.Name)
                    Some (emissionFinding EstateFindingKind.EmissionCompositePkFk subject
                            (sprintf "%s targets a composite primary key (%s columns) — the emitted foreign key would reference only its first column, which SQL Server rejects at deploy."
                                subject (humane (List.length (Kind.primaryKey targetKind)))))
                | _ -> None))

    /// WP-16: two entities whose logical names collide across modules — one
    /// published schema cannot hold two tables of the same name.
    let private emissionDuplicateNameFindings (target: Catalog) : Finding list =
        target.Modules
        |> List.collect (fun m -> m.Kinds |> List.map (fun k -> Name.value k.Name, Name.value m.Name))
        |> List.groupBy fst
        |> List.choose (fun (entity, pairs) ->
            if List.length pairs > 1 then
                let modules = pairs |> List.map snd |> List.distinct
                Some (emissionFinding EstateFindingKind.EmissionDuplicateName entity
                        (sprintf "%s entities are named '%s' (in %s) — they would collide as one emitted table."
                            (humane (List.length pairs)) entity (envListText modules)))
            else None)

    /// WP-11: an identifier longer than SQL Server's 128-character limit —
    /// the deploy rejects it. The authored entity and column names are checked
    /// (synthesized FK/index names ride their own budget, a later slice).
    let private emissionLongNameFindings (target: Catalog) : Finding list =
        let limit = 128
        Catalog.allKinds target
        |> List.collect (fun k ->
            let kindName = Name.value k.Name
            let kindHit =
                if String.length kindName > limit then
                    [ emissionFinding EstateFindingKind.EmissionLongName kindName
                        (sprintf "The entity name '%s' is %s characters — SQL Server rejects identifiers over 128 characters at deploy."
                            kindName (humane (String.length kindName))) ]
                else []
            let attrHits =
                k.Attributes
                |> List.choose (fun a ->
                    let n = Name.value a.Name
                    if String.length n > limit then
                        Some (emissionFinding EstateFindingKind.EmissionLongName (sprintf "%s.%s" kindName n)
                                (sprintf "The column name '%s' on %s is %s characters — SQL Server rejects identifiers over 128 characters at deploy."
                                    n kindName (humane (String.length n))))
                    else None)
            kindHit @ attrHits)

    /// #669 §9 heap audit: an entity with no primary key emits as a heap
    /// (no clustered key) — advisory; the operator confirms it is intended.
    let private emissionNoPrimaryKeyFindings (target: Catalog) : Finding list =
        Catalog.allKinds target
        |> List.filter (fun k -> List.isEmpty (Kind.primaryKey k))
        |> List.map (fun k ->
            emissionFinding EstateFindingKind.EmissionNoPrimaryKey (Name.value k.Name)
                (sprintf "%s has no primary key — it would emit as a heap, with no clustered key for replication or lookups."
                    (Name.value k.Name)))

    /// #669 WP-17 inventory: columns whose concrete type the data-transfer
    /// plane carries through a coarser form (float / real / datetimeoffset /
    /// xml) — surfaced now to scope the exposure; the faithful carriage is
    /// WP-17 (a separate emission-context slice). WATCH, by design.
    let private emissionLossyScalarFindings (target: Catalog) : Finding list =
        Catalog.allKinds target
        |> List.collect (fun k ->
            k.Attributes
            |> List.choose (fun a ->
                let lossy =
                    match a.SqlStorage with
                    | Some SqlStorageType.Float              -> Some ("float", "precision beyond 15 significant digits, and overflow above roughly 7.9E28")
                    | Some SqlStorageType.Real               -> Some ("real", "IEEE-754 single precision, carried as a decimal string")
                    | Some (SqlStorageType.DateTimeOffset _) -> Some ("datetimeoffset", "the time-zone offset")
                    | Some SqlStorageType.Xml                -> Some ("xml", "whitespace and attribute order, since the document is re-serialized")
                    | _                                      -> None
                lossy
                |> Option.map (fun (typeName, what) ->
                    let subject = sprintf "%s.%s" (Name.value k.Name) (Name.value a.Name)
                    emissionFinding EstateFindingKind.EmissionLossyScalar subject
                        (sprintf "%s is %s — the data-transfer plane can lose %s (WP-17); this scopes how carefully the transfer must handle it."
                            subject typeName what))))

    /// #669 §9 audit: a relationship carrying a non-default ON UPDATE action
    /// (cascade / set-null / restrict) — unusual; confirm it is intended.
    let private emissionNonDefaultOnUpdateFindings (target: Catalog) : Finding list =
        Catalog.allKinds target
        |> List.collect (fun k ->
            k.References
            |> List.choose (fun r ->
                match r.OnUpdate with
                | Some action when action <> ReferenceAction.NoAction ->
                    let targetName =
                        Catalog.tryFindKind r.TargetKind target
                        |> Option.map (fun t -> Name.value t.Name) |> Option.defaultValue "?"
                    let sourceCol =
                        k.Attributes |> List.tryFind (fun a -> a.SsKey = r.SourceAttribute)
                        |> Option.map (fun a -> Name.value a.Name) |> Option.defaultValue "?"
                    let actionName =
                        match action with
                        | ReferenceAction.Cascade  -> "CASCADE"
                        | ReferenceAction.SetNull  -> "SET NULL"
                        | ReferenceAction.Restrict -> "NO ACTION (RESTRICT)"
                        | ReferenceAction.NoAction -> "NO ACTION"
                    let subject = sprintf "%s.%s → %s" (Name.value k.Name) sourceCol targetName
                    Some (emissionFinding EstateFindingKind.EmissionNonDefaultOnUpdate subject
                            (sprintf "%s carries ON UPDATE %s — an unusual rule; confirm it is intended, since most references leave ON UPDATE at the default."
                                subject actionName))
                | _ -> None))

    /// #669 M-1 residue (DECISIONS 2026-07-18): an authored default whose
    /// raw text does not parse as a value of the column's type. The
    /// classification lift (`SqlLiteral.ofAuthoredDefault`) carries niladic
    /// calls and quoted text faithfully; what remains in a value literal
    /// must actually BE a value — a raw that is not deploys as a DEFAULT
    /// and then fails at the first insert that relies on it. Validation is
    /// SQL-shaped (invariant TryParse), not canonical-format-strict, so a
    /// legitimate authored `2024-01-01` date-time passes.
    let private emissionAuthoredDefaultFindings (target: Catalog) : Finding list =
        Catalog.allKinds target
        |> List.collect (fun k ->
            k.Attributes
            |> List.choose (fun a ->
                a.DefaultValue
                |> Option.bind SqlLiteral.unparsableValueReason
                |> Option.map (fun problem ->
                    let subject = sprintf "%s.%s" (Name.value k.Name) (Name.value a.Name)
                    emissionFinding EstateFindingKind.EmissionAuthoredDefault subject
                        (sprintf "%s carries an authored default that %s — the DEFAULT deploys, and the first insert that relies on it fails."
                            subject problem))))

    /// #669 B-1 (DECISIONS 2026-07-18): entities that reference each other
    /// in a cycle that nullable-column deferral cannot break. The v6
    /// ordering keeps every other kind in true dependency position; the
    /// cycle's members are the load-order residue — the live transfer
    /// refuses, and the cycle's own rows cannot load in one pass while its
    /// relationships are enforced. Advisory, one finding per cycle, the
    /// members named.
    let private emissionDataLaneOrderFindings (target: Catalog) : Finding list =
        let topo = (Projection.Core.Passes.TopologicalOrderPass.runWith TreatAsCycle target).Value
        topo.Cycles
        |> List.filter (CycleDiagnostic.isResolved >> not)
        |> List.map (fun c ->
            let names =
                CycleDiagnostic.members c
                |> List.map (fun key ->
                    Catalog.tryFindKind key target
                    |> Option.map (fun kind -> Name.value kind.Name)
                    |> Option.defaultValue (SsKey.rootOriginal key))
            let subject = String.concat "+" names
            // v7 slice 8 — the certificate narration (one Voice copy with
            // the gate and the go board).
            let narration =
                CycleNarration.certificateText target c
                |> Option.map (sprintf " %s")
                |> Option.defaultValue ""
            emissionFinding EstateFindingKind.EmissionDataLaneOrder subject
                (sprintf "%s reference each other in a cycle with no deferrable relationship — every other table keeps its dependency position, and the cycle's own rows cannot load in one pass while its relationships are enforced.%s"
                    (envListText names) narration))

    /// #669 M-8 / EF-19 (DECISIONS 2026-07-18): a computed column whose
    /// expression references an identifier resolving to NO column of the
    /// entity — physical or logical. The emitter rewrites physical
    /// identifiers to logical; a token matching neither cannot be
    /// rewritten, and a case-sensitive target rejects the emitted
    /// expression at deploy. Bracketed identifiers only (`sys`
    /// definitions arrive bracket-normalized).
    let private emissionComputedExprFindings (target: Catalog) : Finding list =
        Catalog.allKinds target
        |> List.collect (fun k ->
            k.Attributes
            |> List.choose (fun a ->
                match Kind.unresolvedComputedIdentifiers k a with
                | [] -> None
                | tokens ->
                    let subject = sprintf "%s.%s" (Name.value k.Name) (Name.value a.Name)
                    Some (emissionFinding EstateFindingKind.EmissionComputedExprIdentifiers subject
                            (sprintf "%s is computed from [%s], which resolves to no column of %s — the expression cannot be rewritten to logical names, and a case-sensitive database rejects it at deploy."
                                subject (String.concat "], [" tokens) (Name.value k.Name)))))

    /// EF-23 (DECISIONS 2026-07-18): a system-versioned kind. The emission
    /// cannot yet deploy its period columns (GENERATED ALWAYS is the named
    /// backlog item), so the publish refuses (`EmitError.TemporalKindRefused`,
    /// the same predicate) — the board names the fact and its ruling.
    let private emissionTemporalFindings (target: Catalog) : Finding list =
        Catalog.allKinds target
        |> List.choose (fun k ->
            k.Modality
            |> List.tryPick (function ModalityMark.Temporal tc -> Some tc | _ -> None)
            |> Option.map (fun tc ->
                let history =
                    match tc.HistorySchema, tc.HistoryTable with
                    | Some hs, Some ht -> sprintf " (history table %s.%s)" hs ht
                    | _ -> ""
                emissionFinding EstateFindingKind.EmissionTemporalDropped (Name.value k.Name)
                    (sprintf "%s is system-versioned%s — the emission cannot yet carry SYSTEM_VERSIONING, so the publish refuses rather than dropping it silently."
                        (Name.value k.Name) history)))

    /// EF-20 / family 4e (DECISIONS 2026-07-18): a trigger body that does
    /// not parse, or that still carries an OutSystems physical identifier
    /// after the logical-emission passes. The publish refuses the same two
    /// predicates (`EmitError.TriggerUnrewrittenRefused`) — one shared
    /// parse-check (`ScriptDomGenerate.tryParseTriggerDefinition`) keeps
    /// the board and the gate the same fact.
    let private emissionTriggerFindings (target: Catalog) : Finding list =
        Catalog.allKinds target
        |> List.collect (fun k ->
            k.Triggers
            |> List.choose (fun t ->
                let subject = sprintf "%s.%s" (Name.value k.Name) (Name.value t.Name)
                match Projection.Targets.SSDT.ScriptDomGenerate.tryParseTriggerDefinition t.Definition with
                | Error reason ->
                    Some (emissionFinding EstateFindingKind.EmissionTriggerUnrewritten subject
                            (sprintf "%s's body does not parse (%s) — its identifiers cannot be rewritten to the published names, so the publish refuses." subject reason))
                | Ok () ->
                    match Projection.Targets.SSDT.ScriptDomGenerate.firstPhysicalResidue t.Definition with
                    | Some token ->
                        Some (emissionFinding EstateFindingKind.EmissionTriggerUnrewritten subject
                                (sprintf "%s's body still references the physical identifier '%s' after logical-name emission — the published body would target an object that does not exist, so the publish refuses." subject token))
                    | None -> None))

    /// Every emission-audit finding over the target shape (Phase 1) — the
    /// SSDT-fidelity dimension of the readiness report.
    let emissionFindingsFor (target: Catalog) : Finding list =
        [ emissionCompositePkFkFindings target
          emissionDuplicateNameFindings target
          emissionLongNameFindings target
          emissionNoPrimaryKeyFindings target
          emissionLossyScalarFindings target
          emissionNonDefaultOnUpdateFindings target
          emissionAuthoredDefaultFindings target
          emissionDataLaneOrderFindings target
          emissionComputedExprFindings target
          emissionTemporalFindings target
          emissionTriggerFindings target ]
        |> List.concat
        |> List.sortBy (fun f -> FindingKey.text f.Key)

    /// EF-18 / M-3 (operator decision 2; DECISIONS 2026-07-18): a column the
    /// AGREED shape would emit nullable that a deployed environment enforces
    /// NOT NULL — publishing the model's shape drops that environment's
    /// constraint. The governing principle is `deployed-schema > model >
    /// data-evidence` (CUTOVER_BOARD_POPULATION_PLAN.md §3, decision 2): the
    /// engine fix consults the physical `is_nullable` at emission; until it
    /// ships, the board surfaces the silent constraint drop.
    ///
    /// Unlike the Phase-1 emission findings (properties of the target shape,
    /// `Envs = []`), this one carries PER-ENVIRONMENT evidence — the deployed
    /// nullability from each live profile's `AttributeReality.IsNullableInDatabase`.
    /// It stays on the Emission plane / DECIDE lane so the cutover ladder
    /// counts it (the ladder reads `EmissionFindings`), and folds into the
    /// EMISSION section beside the target-shape findings.
    ///
    /// The predicate fires only when the agreed shape would ACTUALLY emit the
    /// column nullable — logical-optional (`not IsMandatory`, decision 2's
    /// `Is_Mandatory=0`), not a primary key, and physically nullable in the
    /// model (`Column.IsNullable`). The physical-nullable guard is what keeps
    /// it from flooding on the OutSystems platform's optional-but-physically-
    /// NOT-NULL columns: where the model already carries NOT NULL, the emitter
    /// preserves it and there is nothing to loosen.
    let deployedNotNullFindings (target: Catalog) (profilesByEnv: (string * Profile) list) : Finding list =
        let kind = EstateFindingKind.EmissionDeployedNotNullLoosened
        Catalog.allKinds target
        |> List.collect (fun k ->
            k.Attributes
            |> List.choose (fun a ->
                if a.IsMandatory || a.IsPrimaryKey || not a.Column.IsNullable then None
                else
                    let deployedNotNullEnvs =
                        profilesByEnv
                        |> List.choose (fun (env, profile) ->
                            profile.AttributeRealities
                            |> List.tryFind (fun r -> r.AttributeKey = a.SsKey)
                            |> Option.filter (fun r -> not r.IsNullableInDatabase)
                            |> Option.map (fun _ -> env))
                    match deployedNotNullEnvs with
                    | [] -> None
                    | envs ->
                        let subject = sprintf "%s.%s" (Name.value k.Name) (Name.value a.Name)
                        Some
                            { Key       = FindingKey.create kind subject
                              Kind      = kind
                              Lane      = EstateFindingKind.laneOf kind
                              Plane     = EstateFindingKind.planeOf kind
                              Envs      = envs |> List.map (fun e -> e, 1L)
                              Statement =
                                sprintf "%s is NOT NULL in the deployed database(s) %s and nullable in the model — publishing the model's shape drops the constraint there."
                                    subject (envListText envs)
                              Lever     =
                                match EstateFindingKind.leverFormOf kind with
                                | EstateLeverForm.Ruling imperative -> Some imperative
                                | _ -> None
                              Fork      = false }))
        |> List.sortBy (fun f -> FindingKey.text f.Key)

    // -- D10 / D11: static-entity content + identity (wave A4β) ---------------

    /// The business key of a static entity — the label convention: the first
    /// mandatory, non-key, TEXT attribute (a Country's Name, a Status's
    /// Label). `None` when the kind has none; the detectors then skip the kind
    /// by name (coverage honesty), never guessing a key.
    /// Exposed (not `private`) so `EstateRemediation`'s D10 block resolves the
    /// SAME business key the detector keyed on — one definition, no drift
    /// between the finding and its alignment MERGE's ON clause.
    let staticBusinessKey (k: Kind) : Attribute option =
        k.Attributes
        |> List.tryFind (fun a -> a.IsMandatory && not a.IsPrimaryKey && a.Type = Text)

    /// The static entity's primary-key attribute (the surrogate whose minting
    /// D11 watches). Exposed alongside `staticBusinessKey` so the remediation
    /// excludes exactly the surrogate the detector ignored.
    let staticPk (k: Kind) : Attribute option =
        k.Attributes |> List.tryFind (fun a -> a.IsPrimaryKey)

    /// D10 (`DataStaticContent`): each environment's static rows compared
    /// against the SEED (the target's declared static content) by business
    /// key — missing rows, extra rows, or column drift, via
    /// `Reconciliation.staticLookupIdentity`. A REPAIR-lane finding (the
    /// alignment MERGE is the prepared repair). The surrogate PK is EXCLUDED
    /// from the compare (the environments mint their own keys; that is D11's
    /// concern). A kind absent from the seed, or with no business key,
    /// contributes nothing.
    let private staticContentContributions
        (logicalTarget: Catalog)
        (content: StaticContent)
        : EnvContribution list =
        Catalog.allKinds logicalTarget
        |> List.collect (fun kind ->
            match Map.tryFind kind.SsKey content.Seed, staticBusinessKey kind, staticPk kind with
            | Some seedRows, Some bk, Some pk when not (List.isEmpty seedRows) ->
                content.ByEnv
                |> Map.toList
                |> List.choose (fun (env, byKind) ->
                    match Map.tryFind kind.SsKey byKind with
                    | None -> None
                    | Some envRows ->
                        let div =
                            Reconciliation.staticLookupIdentity Set.empty kind.SsKey bk.Name pk.Name seedRows envRows
                        if div.IsClean then None
                        else
                            let missing = List.length div.MissingOnTarget
                            let extra = List.length div.ExtraOnTarget
                            let drift = div.ColumnDrifts |> List.sumBy (fun d -> d.DifferingPairs)
                            let name = Name.value kind.Name
                            Some
                                { Kind = EstateFindingKind.DataStaticContent
                                  Subject = name
                                  Reference = None
                                  Env = env
                                  Fragment =
                                    sprintf "%s in %s differs from the seed — %s row(s) missing, %s extra, %s value difference(s)"
                                        name env (humane missing) (humane extra) (humane drift)
                                  Weight = int64 (missing + extra + drift)
                                  Signature = None })
            | _ -> [])

    /// D11 (`DataStaticIdentity`): an AUTONUMBER static entity (its PK an
    /// IDENTITY — `SurrogateRemap.IdentityDisposition.ofKind = AssignedBySink`)
    /// that numbers the SAME business rows differently across environments —
    /// every inbound reference means something different per environment. A
    /// DECIDE-lane fork (rule the seed; pin explicit keys). The comparison is
    /// the business-key→surrogate map ACROSS environments — the surrogate the
    /// content compare (D10) deliberately excludes. Emits only when a shared
    /// business key carries at least two distinct surrogate values.
    let private staticIdentityContributions
        (logicalTarget: Catalog)
        (content: StaticContent)
        : EnvContribution list =
        Catalog.allKinds logicalTarget
        |> List.collect (fun kind ->
            match staticBusinessKey kind, staticPk kind with
            | Some bk, Some pk when pk.IsIdentity ->
                let mapByEnv =
                    content.ByEnv
                    |> Map.toList
                    |> List.choose (fun (env, byKind) ->
                        Map.tryFind kind.SsKey byKind
                        |> Option.map (fun rows ->
                            let m =
                                rows
                                |> List.choose (fun r ->
                                    match StaticRow.value bk.Name r, StaticRow.value pk.Name r with
                                    | Some bkv, Some pkv when bkv <> "" -> Some (bkv, pkv)
                                    | _ -> None)
                                |> List.distinctBy fst
                                |> Map.ofList
                            env, m))
                if List.length mapByEnv < 2 then []
                else
                    // A business key present in >= 2 envs with >= 2 distinct
                    // surrogate values is the divergence.
                    let sharedKeys =
                        mapByEnv
                        |> List.map (fun (_, m) -> m |> Map.toList |> List.map fst |> Set.ofList)
                        |> List.reduce Set.intersect
                    let diverging =
                        sharedKeys
                        |> Set.filter (fun bkv ->
                            mapByEnv
                            |> List.choose (fun (_, m) -> Map.tryFind bkv m)
                            |> List.distinct
                            |> List.length >= 2)
                    if Set.isEmpty diverging then []
                    else
                        let name = Name.value kind.Name
                        let example = diverging |> Set.toList |> List.sort |> List.head
                        mapByEnv
                        |> List.map (fun (env, m) ->
                            let ev = Map.tryFind example m |> Option.defaultValue "?"
                            { Kind = EstateFindingKind.DataStaticIdentity
                              Subject = name
                              Reference = None
                              Env = env
                              Fragment = sprintf "%s numbers '%s' as %s in %s" name example ev env
                              Weight = int64 (Set.count diverging)
                              // The surrogate map is the fork signature: two envs
                              // with different maps numbered the same rows
                              // differently — no single adoption resolves it.
                              Signature = Some (m |> Map.toList |> List.map (fun (a, b) -> a + "=" + b) |> String.concat ",") })
            | _ -> [])

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
        (staticContent: StaticContent)
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
                // categorical evidence.
                let sentinelZeroFor (ec: ModelFidelity.EntityColumn) : int64 option =
                    operand.Profile
                    |> Option.bind (fun p ->
                        Map.tryFind (ec.Entity, ec.Column) attributeKeyOf
                        |> Option.bind (fun key -> Profile.tryFindCategorical key p)
                        |> Option.bind (fun c ->
                            c.Frequencies
                            |> List.tryFind (fun (value, _) -> value = "0")
                            |> Option.map snd))
                let attrKeyFor (ec: ModelFidelity.EntityColumn) : SsKey option =
                    Map.tryFind (ec.Entity, ec.Column) attributeKeyOf
                let refKeyFor (ec: ModelFidelity.EntityColumn) : SsKey option =
                    Map.tryFind (ec.Entity, ec.Column) referenceKeyOf
                let data =
                    compare.DataDealbreakers
                    |> List.choose (dataFindingOf env posture sentinelZeroFor attrKeyFor refKeyFor)
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
                                if c.RowCount < posture.DecisionFloor then
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
            asymmetryContributions posture.DecisionFloor posture.AsymmetryFactor logicalTarget profilesByEnv
            @ uniquenessCandidateContributions posture.DecisionFloor logicalTarget profilesByEnv
            @ headroomContributions logicalTarget profilesByEnv
            @ dateSentinelContributions logicalTarget profilesByEnv
            @ collationCollisionContributions logicalTarget profilesByEnv
            @ synthesizedIdentityContributions logicalTarget logicalEnvs
            @ promotionOrderContributions posture.PromotionOrder (Map.ofList logicalEnvs)
            @ cdcParityContributions logicalTarget profilesByEnv
            @ postureContributions logicalTarget posture (envs |> List.map fst) profilesByEnv
            // D10 / D11 (wave A4β): the static-entity content + identity
            // detectors over the per-env static rows the face read (empty on
            // `compute` / offline — no findings, the coverage line honest).
            @ staticContentContributions logicalTarget staticContent
            @ staticIdentityContributions logicalTarget staticContent
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
                    // A schema Decide finding forks when the environments
                    // carry distinct divergence signatures; D11 (identity
                    // plane) forks the same way over the surrogate maps — the
                    // same rows numbered differently, no single adoption
                    // resolving it (wave A4β).
                    (plane = EstatePlane.Schema && EstateFindingKind.laneOf kind = EstateLane.Decide
                     || kind = EstateFindingKind.DataStaticIdentity)
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
                        Some (sprintf "Review the block for %s in environments.remediation.%s.sql." (FindingKey.readableLabel key) primary.Env)
                    | EstateLeverForm.MergeOverlayEntry ->
                        Some (sprintf "Merge the config edit for %s in environments.overlay.json." (FindingKey.readableLabel key))
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
          OverlayEntries = None
          EmissionFindings =
            emissionFindingsFor logicalTarget
            @ deployedNotNullFindings logicalTarget (Map.toList profileByEnv)
            |> List.sortBy (fun f -> FindingKey.text f.Key)
          Burndown = None
          Streak = 0
          Fidelity = FidelityClause.NotConfigured
          StaticInspected = not (Map.isEmpty staticContent.ByEnv) }

    /// `computeWith` under no active posture and the default repair band —
    /// the zero-flag basis, and every pre-A6 call site verbatim.
    let compute
        (target: TargetOperand)
        (targetCatalog: Catalog)
        (envs: (string * Compare.Operand) list)
        : EstateReport =
        computeWith Posture.defaults StaticContent.empty target targetCatalog envs

    /// The cutover ladder (`CUTOVER_BOARD_POPULATION_PLAN.md` §0's green
    /// definition; DECISIONS 2026-07-18). Green — ready to cut an
    /// environment over — holds when:
    ///   1. no Emission-plane RULING remains open (the deploy-blocking or
    ///      intent-dropping emission facts; WATCH advisories never block),
    ///      and
    ///   2. no data dealbreaker remains on the REPAIR lane (orphan
    ///      references, NULLs under NOT NULL, duplicates, overflow,
    ///      collation collisions) — each is either CLEARED, or past-band
    ///      and RELAXED with its reopen probe (the RELAX lane never
    ///      blocks; a RETIRABLE posture is the healthy endpoint and never
    ///      blocks), and
    ///   3. the per-environment data readiness (`check shape` —
    ///      `Readiness.isReady`) holds; that verdict rides its own run
    ///      surface and composes with this one at the face.
    /// The ladder names ONE outstanding item (THE_VOICE §8 — one lever,
    /// never a list of ten): the first emission ruling, else the first
    /// data dealbreaker.
    type CutoverLadder =
        { EmissionRulings : Finding list
          DataBlockers    : Finding list
          Green           : bool
          OutstandingItem : Finding option }

    let cutoverLadder (report: EstateReport) : CutoverLadder =
        let emissionRulings =
            report.EmissionFindings
            |> List.filter (fun f -> f.Lane = EstateLane.Decide)
        let dataBlockers =
            report.Findings
            |> List.filter (fun f ->
                f.Plane = EstatePlane.Data
                && f.Lane = EstateLane.Repair
                && f.Kind <> EstateFindingKind.PostureRetirable)
        let outstanding =
            match emissionRulings, dataBlockers with
            | f :: _, _ -> Some f
            | [], f :: _ -> Some f
            | [], [] -> None
        { EmissionRulings = emissionRulings
          DataBlockers    = dataBlockers
          Green           = Option.isNone outstanding
          OutstandingItem = outstanding }

    /// The ladder's surface lines (THE_VOICE §8): the verdict, then —
    /// when an item stands in the way — the one item and its lever.
    let cutoverLadderLines (ladder: CutoverLadder) : string list =
        if ladder.Green then
            [ "Ready to cut over on this board: no emission ruling remains open, and every data dealbreaker is cleared or relaxed with its reopen probe."
              "The per-environment data readiness (check shape) completes the gate." ]
        else
            let total = List.length ladder.EmissionRulings + List.length ladder.DataBlockers
            let head =
                if total = 1 then "One item remains before cutover."
                else sprintf "%d items remain before cutover. The one in the way:" total
            match ladder.OutstandingItem with
            | Some f ->
                [ head; f.Statement ]
                @ (match f.Lever with Some l -> [ l ] | None -> [])
            | None -> [ head ]

    /// The face's remediation stamp: the artifacts it wrote this run —
    /// (file, block count) per environment (`compute` stays file-blind;
    /// the ARTIFACTS index and the JSON read the stamped list).
    let withRemediation (artifacts: (string * int) list) (report: EstateReport) : EstateReport =
        { report with Remediation = artifacts }

    /// The face's posture stamp (wave A6): the overlay's entry count once
    /// `environments.overlay.json` + `environments.probes.sql` are written — the
    /// ARTIFACTS index and the JSON read it; `compute` stays file-blind.
    let withOverlay (entries: int) (report: EstateReport) : EstateReport =
        { report with OverlayEntries = Some entries }

    /// The face's history stamp (wave A7): the movement against the recorded
    /// baseline and the unified-run streak, read from the evidence store's
    /// history — `compute` stays store-blind; the boundary owns clocks and
    /// directories.
    let withHistory (burndown: Burndown option) (streak: int) (report: EstateReport) : EstateReport =
        { report with Burndown = burndown; Streak = streak }

    /// The face's fidelity stamp (RT-10, wave A4β): fold the row-fidelity
    /// clause into the report. A configured-but-non-green proof mints its
    /// DECIDE finding (ProofMissing / ProofStale / ProofDiverged, keyed on the
    /// flow) and RE-COMPUTES the verdict over the widened finding set — so a
    /// missing or stale proof turns Unified to Converging (the verdict formula
    /// includes the configured proof, RT-10's whole point). `NotConfigured`
    /// and `Green` add no finding: the clause rides the masthead only, and the
    /// verdict stands on the schema/data findings alone. Apply BEFORE
    /// `withHistory` so the proof finding participates in the burndown and the
    /// streak reset (a run whose proof is missing is not a unified run).
    let withFidelity (clause: FidelityClause) (report: EstateReport) : EstateReport =
        let proofFinding (kind: EstateFindingKind) (flow: string) (statement: string) : Finding =
            { Key = FindingKey.create kind flow
              Kind = kind
              Lane = EstateFindingKind.laneOf kind
              Plane = EstateFindingKind.planeOf kind
              // Estate-wide (a property of the proof artifact, not a
              // per-environment divergence) — no per-env weight rows.
              Envs = []
              Statement = statement
              // The one lever names the flow to run — the §3 contract's row
              // (the registry's generic Ruling is the form; the flow rides here).
              Lever = Some (sprintf "Run: projection check fidelity %s." flow)
              Fork = false }
        let extra =
            match clause with
            | FidelityClause.NotConfigured
            | FidelityClause.Green _ -> []
            | FidelityClause.Missing flow ->
                [ proofFinding EstateFindingKind.ProofMissing flow
                    (sprintf "The fidelity proof for flow '%s' has not run against the current estate." flow) ]
            | FidelityClause.Stale (flow, ageDays) ->
                [ proofFinding EstateFindingKind.ProofStale flow
                    (sprintf "The fidelity proof for flow '%s' is %s day(s) old and the estate's evidence has moved since — the proof predates what this run can see." flow (humane ageDays)) ]
            | FidelityClause.Diverged (flow, diffs) ->
                [ proofFinding EstateFindingKind.ProofDiverged flow
                    (sprintf "The fidelity proof for flow '%s' reports %s differing row(s) — the load is not yet byte-faithful." flow (humane64 diffs)) ]
        let findings = report.Findings @ extra
        let verdict =
            if List.isEmpty findings then Verdict.Unified
            elif findings |> List.exists (fun f -> f.Fork) then Verdict.Forked
            else Verdict.Converging
        { report with Findings = findings; Verdict = verdict; Fidelity = clause }

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

    // The lane/plane/provenance formatters are exposed (not `private`) so the
    // rich board lens (`EstateBoardView.ofReport`, the CLI's live board) presents
    // the SAME load-bearing copy the plain lens does — one report value, two
    // lenses, no drift on the words the operator reads.
    let laneTitle (lane: EstateLane) : string =
        match lane with
        | EstateLane.Decide -> "DECIDE — the ruling queue"
        | EstateLane.Repair -> "REPAIR — prepared repairs"
        | EstateLane.Relax  -> "RELAX — interim changes to carry through cutover"
        | EstateLane.Watch  -> "WATCH — advisories"

    let laneEmptyLine (lane: EstateLane) : string =
        match lane with
        | EstateLane.Decide -> "  Nothing awaits a ruling."
        | EstateLane.Repair -> "  Nothing carries a repair."
        | EstateLane.Relax  -> "  No interim changes are needed."
        | EstateLane.Watch  -> "  Nothing is under watch."

    let planeToken (p: EstatePlane) : string =
        match p with
        | EstatePlane.Schema -> "schema"
        | EstatePlane.Data -> "data"
        | EstatePlane.Identity -> "identity"
        | EstatePlane.Operational -> "operational"
        | EstatePlane.Emission -> "emission"

    /// The per-lane cap — the top consequences are named; the remainder is
    /// counted (THE_VOICE §12: cap the breadth, name the remainder; the full
    /// list is `environments.json`'s, searchable, never scrollable).
    let laneCap : int = 8

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
    let provenanceText (basis: EnvBasis) : string =
        match basis.Provenance with
        | EvidenceProvenance.Live ->
            "live data evidence, profiled this run"
        | EvidenceProvenance.Cached (_, age, kinds) ->
            sprintf "evidence captured %s; fingerprints (row count, max key, and content hash) clean across %s kind(s) — the cache is content-verified fresh" (ageText age) (humane kinds)
        | EvidenceProvenance.Refreshed moved ->
            sprintf "%s kind(s) moved since capture (%s) — re-profiled this run"
                (humane (List.length moved)) (movedKindsText moved)
        | EvidenceProvenance.Offline (_, age) ->
            sprintf "offline evidence, captured %s and unprobed — every verdict standing on it is advisory" (ageText age)
        | EvidenceProvenance.Absent ->
            "no data evidence this run — the data plane is advisory-silent"

    /// The rolled-up evidence-confidence footing (DECISIONS 2026-07-18): how
    /// much of the verdict stands on FIRM evidence (live, re-profiled, or a
    /// content-verified cache) versus ADVISORY (offline / absent). The per-env
    /// provenance lines say WHICH environment is on what; this says HOW MUCH of
    /// the estate the verdict firmly rests on. Exposed so the text board and
    /// the rich board render one line from one source.
    let evidenceConfidenceLine (report: EstateReport) : string =
        let firm, advisory =
            report.Bases
            |> List.partition (fun b ->
                match b.Provenance with
                | EvidenceProvenance.Live
                | EvidenceProvenance.Cached _
                | EvidenceProvenance.Refreshed _ -> true
                | EvidenceProvenance.Offline _
                | EvidenceProvenance.Absent -> false)
        match advisory with
        | [] ->
            sprintf "Evidence confidence: all %s environment(s) stand on firm evidence (live, re-profiled, or content-verified cache)."
                (humane (List.length firm))
        | _ ->
            sprintf "Evidence confidence: %s on firm evidence, %s advisory (%s) — verdicts leaning on the advisory environment(s) are advisory too."
                (humane (List.length firm)) (humane (List.length advisory))
                (advisory |> List.map (fun b -> b.Env) |> String.concat ", ")

    /// The coverage-honesty line (THE_VOICE §14): the classes this run does not
    /// yet check, so "one shape" never reads as "everything is clean". The
    /// row-fidelity proof joins the covered set exactly when it is configured;
    /// the static-content probe (D10/D11) drops from the not-inspected list
    /// exactly when it ran. Exposed (not inlined in `render`) so the rich board
    /// lens presents the SAME coverage sentence — one source, no drift.
    let coverageLine (report: EstateReport) : string =
        let coveredTail =
            match report.Fidelity with
            | FidelityClause.NotConfigured -> "A clean verdict covers schema and data convergence only."
            | _ -> "A clean verdict covers schema convergence, data convergence, and the row-fidelity proof."
        let notInspected =
            [ if not report.StaticInspected then "static-entity content"
              "user references"; "grants"; "computed columns"
              "emission fidelity (clustering, temporal tables, sequences)" ]
        sprintf "Not inspected this run: %s. %s" (String.concat ", " notInspected) coveredTail

    /// Render the board regions BELOW the verdict (the masthead through the
    /// action), from the one report value. The face renders the verdict Hero
    /// through the Voice catalog first, then these lines.
    let render (report: EstateReport) : string list =
        [ // MASTHEAD — the estate and its basis.
          yield sprintf "ENVIRONMENTS — %s environment(s) against %s"
                    (humane (List.length report.Bases)) (TargetOperand.basisText report.Target)
          for basis in report.Bases do
              yield sprintf "  %-14s %s" basis.Env (provenanceText basis)
          yield sprintf "  %s" (evidenceConfidenceLine report)
          yield
              (match report.Evidence with
               | EvidenceStoreBasis.Enabled dir ->
                   sprintf "  Evidence store: %s." dir
               | EvidenceStoreBasis.Disabled ->
                   "  Evidence reads live this run — no store is configured (PROJECTION_ESTATE_DIR, or the ledger directory's estate child, enables pay-once evidence).")
          // The fidelity clause, named (RT-10, wave A4β): the estate config's
          // `fidelityFlow` decides whether the row-fidelity proof is part of
          // the verdict. Unconfigured, it is named and excluded (a never-run
          // proof never holds the verdict hostage); configured, a green proof
          // rides here and each non-green state is a DECIDE finding below.
          yield
              (match report.Fidelity with
               | FidelityClause.NotConfigured ->
                   "  The fidelity clause is not configured; the verdict stands on the schema and data evidence."
               | FidelityClause.Green (flow, ageDays) ->
                   let age = if ageDays <= 0 then "captured today" else sprintf "%s day(s) old" (humane ageDays)
                   sprintf "  The fidelity proof for flow '%s' is green — every row byte-identical (%s)." flow age
               | FidelityClause.Missing flow ->
                   sprintf "  The fidelity proof for flow '%s' has not run — it stands as a ruling below." flow
               | FidelityClause.Stale (flow, ageDays) ->
                   sprintf "  The fidelity proof for flow '%s' is %s day(s) old and predates this run's evidence — it stands as a ruling below." flow (humane ageDays)
               | FidelityClause.Diverged (flow, diffs) ->
                   sprintf "  The fidelity proof for flow '%s' reports %s differing row(s) — it stands as a ruling below." flow (humane64 diffs))
          // Coverage honesty (THE_VOICE §14 — a clean verdict never overstates
          // what it inspected), the one source `coverageLine` (the rich board
          // lens reads the same sentence).
          yield sprintf "  %s" (coverageLine report)
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
                      yield sprintf "  … and %s more — environments.json carries every finding." (humane remainder)
              yield ""

          // EMISSION — the SSDT-fidelity audit over the target shape (Phase 1,
          // the #669 audit): would the schema deploy, does it model reality.
          yield "EMISSION — the schema this estate would publish, audited against database reality"
          for f in report.EmissionFindings |> List.truncate laneCap do
              yield sprintf "  %s" f.Statement
              match f.Lever with
              | Some lever -> yield sprintf "      → %s" lever
              | None -> ()
          let emissionExtra = List.length report.EmissionFindings - laneCap
          if emissionExtra > 0 then
              yield sprintf "  … and %s more — environments.json carries every finding." (humane emissionExtra)
          if List.isEmpty report.EmissionFindings then
              yield "  No emission hazards in the checks that run today."
          // The coverage line is DERIVED from the detector set (the
          // `DetectionStatus` classifier), never restated — the predecessor
          // was hand-maintained and drifted (it promised temporal tables and
          // sequences as "coming" after both shipped). One source, no drift.
          let emissionPhrasesBy status =
              EstateFindingKind.all
              |> List.filter (fun k ->
                  EstateFindingKind.planeOf k = EstatePlane.Emission
                  && EstateFindingKind.detectionStatus k = status)
              |> List.map EstateFindingKind.phrase
          match emissionPhrasesBy DetectionStatus.Active with
          | [] -> ()
          | ps -> yield sprintf "  Runs today, each catching one hazard: %s." (String.concat "; " ps)
          match emissionPhrasesBy DetectionStatus.NotYetDetected with
          | [] -> ()
          | ps -> yield sprintf "  Named follow-ons, not yet checked: %s." (String.concat "; " ps)
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

          // BURNDOWN — the movement since the recorded baseline (wave A7).
          // Three honest states: movement against a named baseline; a first
          // recorded reading; no store, no memory — said, never implied.
          match report.Burndown, report.Evidence with
          | Some b, _ ->
              let sinceClause =
                  if b.SinceAgeDays <= 0 then "earlier today"
                  else sprintf "%s day(s) ago" (humane b.SinceAgeDays)
              let oldestClause =
                  match b.OldestDays with
                  | Some days when b.Remaining + b.Opened > 0 ->
                      sprintf " — the oldest open finding is %s day(s) old" (humane days)
                  | _ -> ""
              yield sprintf "BURNDOWN — since run %s (%s): %s closed, %s opened, %s remain%s."
                        b.SinceRunId sinceClause (humane b.Closed) (humane b.Opened) (humane b.Remaining) oldestClause
          | None, EvidenceStoreBasis.Enabled _ ->
              yield "BURNDOWN — this run is the estate's first recorded reading; movement renders from the next run."
          | None, EvidenceStoreBasis.Disabled ->
              yield "BURNDOWN — the estate keeps no memory without a store; PROJECTION_ESTATE_DIR (or the ledger directory's estate child) enables the burndown."
          if report.Streak > 0 then
              yield sprintf "  The estate has read unified for %s consecutive run(s)." (humane report.Streak)
          yield ""

          // ARTIFACTS — the index: one line per artifact naming its role.
          yield "ARTIFACTS"
          yield "  environments.json — the full findings record: every board element, machine-readable."
          for file, blocks in report.Remediation do
              yield sprintf "  %s — %s prepared repair block(s); the locating SELECT is active, every repair is commented." file (humane blocks)
          match report.OverlayEntries with
          | Some entries when entries > 0 ->
              yield sprintf "  environments.overlay.json — %s interim change(s) as config edits; each carries the probe that clears it. The merge is an operator edit; the engine never applies it." (humane entries)
              yield "  environments.probes.sql — every reopen probe, runnable as one batch; the posture's retirement meter."
          | _ -> ()
          yield ""

          // RUNBOOK — the cutover procedure (source estate → target database)
          // and the manual gates it still leaves to the operator (#669 §11).
          // Static context, so nothing about the hand-off is a surprise.
          yield "RUNBOOK — source estate → target database"
          yield "  1. Confirm readiness (this check) · 2. Publish the schema bundle · 3. Deploy via sqlpackage"
          yield "  4. Load the bulk data · 5. Re-trust foreign keys and enable CDC · 6. Verify (drift · rows · CDC-silence)"
          yield "  Manual gates to own before cutover: the bulk-load step (Data/Bootstrap.sql) is not auto-run;"
          yield "  enabling CDC on the target has no verb; the streaming and synthetic load legs need a manual"
          yield "  foreign-key re-trust sweep; the refactorlog is not yet placed in the bundle; a publish rollback"
          yield "  is not yet proven."
          yield ""

          // ACTION — the one next move; a holding estate names its streak.
          let action =
              match laneFindings EstateLane.Decide report with
              | f :: _ -> sprintf "Next: rule the first DECIDE finding — %s" (FindingKey.readableLabel f.Key)
              | [] ->
                  match laneFindings EstateLane.Repair report with
                  | f :: _ -> sprintf "Next: review the first REPAIR finding — %s" (FindingKey.readableLabel f.Key)
                  | [] when report.Streak > 1 ->
                      sprintf "Next: the estate holds — %s consecutive unified run(s); re-run on the publish cadence." (humane report.Streak)
                  | [] -> "Next: the estate holds; re-run on the publish cadence."
          yield action ]

    // ----------------------------------------------------------------------
    // The environments.json codec — the structured sibling of the board (one
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

    /// One environment's provenance, projected for `environments.json` (one
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
        root.["staticContentInspected"] <- JsonValue.Create report.StaticInspected
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
             o.["file"] <- JsonValue.Create "environments.overlay.json"
             o.["probes"] <- JsonValue.Create "environments.probes.sql"
             o.["entries"] <- JsonValue.Create entries
             root.["overlay"] <- o
         | None -> ())
        // The fidelity clause (RT-10) — the state token plus its coordinates,
        // so a CI reader branches on the proof without parsing the board.
        (let fc = JsonObject()
         (match report.Fidelity with
          | FidelityClause.NotConfigured ->
              fc.["state"] <- JsonValue.Create "notConfigured"
          | FidelityClause.Green (flow, ageDays) ->
              fc.["state"] <- JsonValue.Create "green"
              fc.["flow"] <- JsonValue.Create flow
              fc.["ageDays"] <- JsonValue.Create ageDays
          | FidelityClause.Missing flow ->
              fc.["state"] <- JsonValue.Create "missing"
              fc.["flow"] <- JsonValue.Create flow
          | FidelityClause.Stale (flow, ageDays) ->
              fc.["state"] <- JsonValue.Create "stale"
              fc.["flow"] <- JsonValue.Create flow
              fc.["ageDays"] <- JsonValue.Create ageDays
          | FidelityClause.Diverged (flow, diffs) ->
              fc.["state"] <- JsonValue.Create "diverged"
              fc.["flow"] <- JsonValue.Create flow
              fc.["differingRows"] <- JsonValue.Create diffs)
         root.["fidelityClause"] <- fc)
        (match report.Burndown with
         | Some b ->
             let o = JsonObject()
             o.["sinceRunId"] <- JsonValue.Create b.SinceRunId
             o.["sinceAgeDays"] <- JsonValue.Create b.SinceAgeDays
             o.["closed"] <- JsonValue.Create b.Closed
             o.["opened"] <- JsonValue.Create b.Opened
             o.["remaining"] <- JsonValue.Create b.Remaining
             (match b.OldestDays with
              | Some days -> o.["oldestDays"] <- JsonValue.Create days
              | None -> ())
             root.["burndown"] <- o
         | None -> ())
        root.["streak"] <- JsonValue.Create report.Streak
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
        // The emission-audit dimension (Phase 1): target-shape fidelity, its
        // own array beside the convergence findings.
        let emission = JsonArray()
        for f in report.EmissionFindings do
            let o = JsonObject()
            o.["key"] <- JsonValue.Create(FindingKey.text f.Key)
            o.["kind"] <- JsonValue.Create(EstateFindingKind.token f.Kind)
            o.["lane"] <- JsonValue.Create(laneToken f.Lane)
            o.["plane"] <- JsonValue.Create(planeToken f.Plane)
            o.["statement"] <- JsonValue.Create f.Statement
            (match f.Lever with
             | Some lever -> o.["lever"] <- JsonValue.Create lever
             | None -> ())
            emission.Add o
        root.["emission"] <- emission
        root

    /// Serialize to a pretty-printed JSON string (the artifact body).
    let toJsonString (report: EstateReport) : string =
        let opts = System.Text.Json.JsonSerializerOptions(WriteIndented = true)
        (toJson report).ToJsonString(opts)
