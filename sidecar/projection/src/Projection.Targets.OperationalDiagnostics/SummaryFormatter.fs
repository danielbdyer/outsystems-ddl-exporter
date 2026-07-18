namespace Projection.Targets.OperationalDiagnostics

// LINT-ALLOW-FILE: terminal operator-facing summary text; segments are typed and the formatted
//   value is immutable. `String.concat` is the BCL primitive at this terminal
//   text boundary.

open System.Text
open Projection.Core

/// `SummaryFormatter` — chapter 5+ slice `5.13.summary-formatter`
/// (per `V1_PARITY_MATRIX` row 81). Walks the three tightening
/// `DecisionSet` outputs and emits per-bucket prose mirroring V1's
/// `PolicyDecisionSummaryFormatter` (439 LOC of imperative
/// classification + console formatting). V2 collapses the V1 surface
/// into typed `Outcome` DU pattern-matching across six buckets:
///
///   - **PrimaryKey** — `NullabilityOutcome.EnforceNotNull (PrimaryKey)`
///   - **Physical** — `NullabilityOutcome.EnforceNotNull (PhysicallyNotNull)`
///   - **Mandatory** — `NullabilityOutcome.EnforceNotNull
///     (LogicalMandatoryNoProfile | LogicalMandatoryNoNulls |
///     LogicalMandatoryWithinBudget)`
///   - **ForeignKey** — `ForeignKeyOutcome.EnforceConstraint _`
///   - **Unique** — `UniqueIndexOutcome.EnforceUnique _`
///   - **Remediation** — operator-attention findings:
///     `NullabilityOutcome.RequireOperatorApproval`,
///     `NullabilityOutcome.KeepNullable (RelaxedUnderEvidence _)`,
///     `ForeignKeyOutcome.DoNotEnforce (DataHasOrphans _)`,
///     `UniqueIndexOutcome.DoNotEnforce (DataHasDuplicates)`
///
/// **V2 collapses V1's `NullabilityMode` enum.** V1's mode-aware
/// narration ("running in Cautious mode" / "Aggressive mode")
/// reflected V1's three-mode tightening surface. V2 collapsed to a
/// single tightening behavior at the V2 level (`Policy.fs:105-111`
/// comment: "V1 only ever uses Cautious in production; if a real
/// second mode lands later, it arrives as a new field"). The
/// summary's prose therefore mentions intervention IDs (operator-
/// chosen names) rather than mode-aware narration.
///
/// **Pillar 9 — `DataIntent`.** The summary projects evidence from
/// the DecisionSets; no operator opinion enters the projection.
[<RequireQualifiedAccess>]
module SummaryFormatter =

    [<Literal>]
    let version : int = 1

    [<RequireQualifiedAccess>]
    type private Bucket =
        | PrimaryKey
        | Physical
        | Mandatory
        | ForeignKey
        | Unique
        | Remediation

    /// One bucket's accumulated entries. `Count` is the rollup; the
    /// first three entries' SsKeys surface in the prose as sample
    /// rows (matches the §11 LogSink rollup's "first three samples"
    /// shape; pillar 9 codification of operator-readable rollups).
    type private BucketRollup = {
        Bucket    : Bucket
        Count     : int
        FirstKeys : SsKey list
    }

    let private emptyRollup (bucket: Bucket) : BucketRollup = {
        Bucket    = bucket
        Count     = 0
        FirstKeys = []
    }

    let private accumulate (rollup: BucketRollup) (key: SsKey) : BucketRollup =
        let nextKeys =
            if List.length rollup.FirstKeys < 3
            then rollup.FirstKeys @ [ key ]
            else rollup.FirstKeys
        { rollup with Count = rollup.Count + 1; FirstKeys = nextKeys }

    let private classifyNullability
        (decision: NullabilityDecision)
        : Bucket option =
        match decision.Outcome with
        | NullabilityOutcome.EnforceNotNull NullabilityEvidence.PrimaryKey        -> Some Bucket.PrimaryKey
        | NullabilityOutcome.EnforceNotNull NullabilityEvidence.PhysicallyNotNull -> Some Bucket.Physical
        | NullabilityOutcome.EnforceNotNull NullabilityEvidence.LogicalMandatoryNoProfile
        | NullabilityOutcome.EnforceNotNull (NullabilityEvidence.LogicalMandatoryNoNulls _)
        | NullabilityOutcome.EnforceNotNull (NullabilityEvidence.LogicalMandatoryWithinBudget _) ->
            Some Bucket.Mandatory
        | NullabilityOutcome.KeepNullable (RelaxedUnderEvidence _)                -> Some Bucket.Remediation
        | NullabilityOutcome.KeepNullable _                                       -> None
        | NullabilityOutcome.RequireOperatorApproval _                            -> Some Bucket.Remediation

    let private classifyForeignKey
        (decision: ForeignKeyDecision)
        : Bucket option =
        match decision.Outcome with
        | ForeignKeyOutcome.EnforceConstraint _              -> Some Bucket.ForeignKey
        | ForeignKeyOutcome.DoNotEnforce (DataHasOrphans _)  -> Some Bucket.Remediation
        | ForeignKeyOutcome.DoNotEnforce _                   -> None

    let private classifyUniqueIndex
        (decision: UniqueIndexDecision)
        : Bucket option =
        match decision.Outcome with
        | UniqueIndexOutcome.EnforceUnique _                  -> Some Bucket.Unique
        | UniqueIndexOutcome.DoNotEnforce DataHasDuplicates   -> Some Bucket.Remediation
        | UniqueIndexOutcome.DoNotEnforce _                   -> None

    let private bucketLabel (bucket: Bucket) : string =
        match bucket with
        | Bucket.PrimaryKey   -> "PrimaryKey"
        | Bucket.Physical     -> "Physical"
        | Bucket.Mandatory    -> "Mandatory"
        | Bucket.ForeignKey   -> "ForeignKey"
        | Bucket.Unique       -> "Unique"
        | Bucket.Remediation  -> "Remediation"

    let private bucketNarration (bucket: Bucket) : string =
        match bucket with
        | Bucket.PrimaryKey ->
            "primary-key columns tightened to NOT NULL — always tightens regardless of mode/budget/profile."
        | Bucket.Physical ->
            "physically NOT NULL columns tightened — data already conforms in the source schema."
        | Bucket.Mandatory ->
            "model-mandatory columns tightened — logical declaration matches profile evidence."
        | Bucket.ForeignKey ->
            "FK constraints elected for creation — passed evidence + cross-schema gates."
        | Bucket.Unique ->
            // The count is the ENFORCED total (carried + promoted); the split
            // beneath separates source fidelity from profile-driven tightening.
            "unique-index enforcements — the split beneath separates carried-from-source from promoted."
        | Bucket.Remediation ->
            "operator-attention findings — model/data conflict needs reconciliation before tightening."

    let private writeBucket
        (sb: StringBuilder)
        (rollup: BucketRollup)
        : unit =
        sb.AppendLine(
            sprintf "  [%-12s] %4d decision(s) — %s"
                (bucketLabel rollup.Bucket)
                rollup.Count
                (bucketNarration rollup.Bucket))
        |> ignore
        for key in rollup.FirstKeys do
            sb.AppendLine(sprintf "                   sample: %s" (SsKey.rootOriginal key)) |> ignore

    // -- The unique-index decision split (2026-07-18) -----------------------
    //
    // The `[Unique]` bucket count is the ENFORCED total, which conflated two
    // very different outcomes: an index the dev team DECLARED unique (source
    // fidelity — never a tightening) and an index PROMOTED from profile
    // evidence (a tightening BEYOND what the source declared). This split
    // reports the two separately, plus the three withheld reasons the prior
    // single line dropped entirely (duplicates / missing evidence / policy
    // disabled). **The dev team's declared indexes are authoritative**: a
    // promotion is only ever APPLIED when an operator explicitly registers a
    // `uniqueIndex` tightening intervention (`UniqueIndexPass` is a no-op
    // otherwise — a default publish promotes nothing and this whole section
    // reads zero). The count exists so a configured promotion is never silent.

    /// The per-outcome tally of the unique-index decision set — carried vs
    /// promoted (the two enforced arms) and the three withheld reasons.
    type private UniqueSplit = {
        Carried                 : int
        Promoted                : int
        AdvisedNotApplied       : int
        WithheldDuplicates      : int
        WithheldMissingEvidence : int
        WithheldPolicyDisabled  : int
    }

    let private emptyUniqueSplit : UniqueSplit = {
        Carried = 0; Promoted = 0; AdvisedNotApplied = 0
        WithheldDuplicates = 0; WithheldMissingEvidence = 0; WithheldPolicyDisabled = 0
    }

    let private uniqueSplitOf (uniqueIndex: UniqueIndexDecisionSet) : UniqueSplit =
        uniqueIndex.Decisions
        |> List.fold (fun acc d ->
            match d.Outcome with
            | UniqueIndexOutcome.EnforceUnique UniqueIndexEvidence.AlreadyUnique ->
                { acc with Carried = acc.Carried + 1 }
            | UniqueIndexOutcome.EnforceUnique (UniqueIndexEvidence.SingleColumnNoDuplicates _)
            | UniqueIndexOutcome.EnforceUnique UniqueIndexEvidence.CompositeNoDuplicates ->
                { acc with Promoted = acc.Promoted + 1 }
            | UniqueIndexOutcome.DoNotEnforce UniqueIndexKeepReason.PromotionAdvisedNotApplied ->
                { acc with AdvisedNotApplied = acc.AdvisedNotApplied + 1 }
            | UniqueIndexOutcome.DoNotEnforce UniqueIndexKeepReason.DataHasDuplicates ->
                { acc with WithheldDuplicates = acc.WithheldDuplicates + 1 }
            | UniqueIndexOutcome.DoNotEnforce UniqueIndexKeepReason.EvidenceMissing
            | UniqueIndexOutcome.DoNotEnforce UniqueIndexKeepReason.NoCandidateProfiled ->
                { acc with WithheldMissingEvidence = acc.WithheldMissingEvidence + 1 }
            | UniqueIndexOutcome.DoNotEnforce UniqueIndexKeepReason.PolicyDisabled ->
                { acc with WithheldPolicyDisabled = acc.WithheldPolicyDisabled + 1 }) emptyUniqueSplit

    /// Render the split as indented sub-lines beneath the `[Unique]` bucket.
    /// Every line is emitted (including zeros) for diff-friendliness and so an
    /// operator reads the full disposition of the unique axis at a glance.
    let private writeUniqueSplit (sb: StringBuilder) (split: UniqueSplit) : unit =
        let line (label: string) (n: int) (gloss: string) =
            sb.AppendLine(sprintf "        %-30s %4d — %s" label n gloss) |> ignore
        line "carried from source" split.Carried
            "declared UNIQUE in the model; emitted faithfully (source fidelity, not a tightening)."
        line "advised — could be promoted" split.AdvisedNotApplied
            "not declared UNIQUE, profile shows no duplicates; a promotion the operator COULD apply. Advisory only — emission stays faithful to the model unless applyUniquePromotions is set."
        line "promoted from profile evidence" split.Promoted
            "a promotion the operator APPLIED (applyUniquePromotions set) — a tightening BEYOND the source's declaration."
        line "withheld — duplicates" split.WithheldDuplicates
            "profile shows duplicate values; enforcing would break existing rows (also counted under Remediation)."
        line "withheld — missing evidence" split.WithheldMissingEvidence
            "no reliable probe or no profiled candidate; evidence absent, so no tightening."
        line "withheld — policy disabled" split.WithheldPolicyDisabled
            "the intervention's toggle for this index's column-count category is off."

    /// Build the per-bucket rollup `Map` from the three DecisionSets.
    /// Each decision contributes to at most one bucket per
    /// classifier; rollups initialize empty (zero count) for every
    /// bucket so the prose carries explicit "0 decision(s)" lines
    /// (V1 parity: V1 emits the bucket header even when count = 0;
    /// V2 preserves this for operator-readability + diff-friendliness).
    let private buildRollups
        (nullability: NullabilityDecisionSet)
        (uniqueIndex: UniqueIndexDecisionSet)
        (foreignKey: ForeignKeyDecisionSet)
        : Map<Bucket, BucketRollup> =
        let allBuckets =
            [ Bucket.PrimaryKey
              Bucket.Physical
              Bucket.Mandatory
              Bucket.ForeignKey
              Bucket.Unique
              Bucket.Remediation ]
        let initial =
            allBuckets
            |> List.map (fun b -> b, emptyRollup b)
            |> Map.ofList
        let folded =
            initial
            |> (fun acc ->
                nullability.Decisions
                |> List.fold (fun acc d ->
                    match classifyNullability d with
                    | Some bucket ->
                        let prior = Map.find bucket acc
                        Map.add bucket (accumulate prior d.AttributeKey) acc
                    | None -> acc) acc)
            |> (fun acc ->
                foreignKey.Decisions
                |> List.fold (fun acc d ->
                    match classifyForeignKey d with
                    | Some bucket ->
                        let prior = Map.find bucket acc
                        Map.add bucket (accumulate prior d.ReferenceKey) acc
                    | None -> acc) acc)
            |> (fun acc ->
                uniqueIndex.Decisions
                |> List.fold (fun acc d ->
                    match classifyUniqueIndex d with
                    | Some bucket ->
                        let prior = Map.find bucket acc
                        Map.add bucket (accumulate prior d.IndexKey) acc
                    | None -> acc) acc)
        folded

    /// Format the per-bucket summary as operator-readable prose.
    /// Returns `string list` (one line per output row) so callers can
    /// pipe through any line-oriented sink (console, file, structured
    /// log). The order across buckets is fixed: PrimaryKey → Physical
    /// → Mandatory → ForeignKey → Unique → Remediation (V1 parity).
    let format
        (nullability: NullabilityDecisionSet)
        (uniqueIndex: UniqueIndexDecisionSet)
        (foreignKey: ForeignKeyDecisionSet)
        : string list =
        use _ = Bench.scope "summary.format"
        let rollups = buildRollups nullability uniqueIndex foreignKey
        let sb = StringBuilder()
        sb.AppendLine("Tightening decision summary") |> ignore
        sb.AppendLine("---------------------------") |> ignore
        let bucketOrder =
            [ Bucket.PrimaryKey
              Bucket.Physical
              Bucket.Mandatory
              Bucket.ForeignKey
              Bucket.Unique
              Bucket.Remediation ]
        let uniqueSplit = uniqueSplitOf uniqueIndex
        for bucket in bucketOrder do
            writeBucket sb (Map.find bucket rollups)
            // The unique axis carries its carried-vs-promoted-vs-withheld split
            // immediately beneath its bucket line (the fidelity-vs-tightening
            // precision, 2026-07-18).
            if bucket = Bucket.Unique then writeUniqueSplit sb uniqueSplit
        sb.AppendLine() |> ignore
        let totalActioned =
            rollups
            |> Map.toSeq
            |> Seq.filter (fun (b, _) ->
                match b with
                | Bucket.PrimaryKey | Bucket.Physical | Bucket.Mandatory
                | Bucket.ForeignKey | Bucket.Unique -> true
                | Bucket.Remediation -> false)
            |> Seq.sumBy (fun (_, r) -> r.Count)
        let remediationCount = (Map.find Bucket.Remediation rollups).Count
        sb.AppendLine(
            sprintf "Totals: %d structural tightening(s); %d remediation finding(s)."
                totalActioned remediationCount)
        |> ignore
        let text = sb.ToString()
        // Split on newlines + drop the trailing empty string from the
        // last AppendLine. This makes the output operator-pipeable.
        text.Split([| '\n' |], System.StringSplitOptions.None)
        |> Array.toList
        |> List.map (fun line ->
            // Strip trailing \r if present (cross-platform AppendLine).
            if line.EndsWith "\r" then line.Substring(0, line.Length - 1)
            else line)
        |> List.filter (fun line -> not (System.String.IsNullOrEmpty line))

    /// Convenience: join the formatted lines into a single text block
    /// for the `manifest.summary.txt` artifact-emission boundary.
    let formatText
        (nullability: NullabilityDecisionSet)
        (uniqueIndex: UniqueIndexDecisionSet)
        (foreignKey: ForeignKeyDecisionSet)
        : string =
        format nullability uniqueIndex foreignKey
        // LF, not Environment.NewLine: `manifest.summary.txt` is an
        // emitted artifact and must be byte-identical across platforms
        // (T1). A host-dependent newline would yield CRLF on Windows.
        |> String.concat "\n"

    /// `RegisteredTransform` metadata view per the pillar 9 +
    /// L3-CC-Transform-Totality discipline. Classifies as
    /// `DataIntent` — the summary projects evidence (DecisionSet
    /// outcomes) into bucket-prose; no operator opinion enters.
    let registeredMetadata : RegisteredTransformMetadata =
        RegisteredTransformMetadata.emitter "summaryFormatter" Diagnostics
            [ TransformSite.dataIntent "perBucketRollup"
                "Project Nullability/ForeignKey/UniqueIndex DecisionSet outcomes into 6-bucket rollup (PrimaryKey / Physical / Mandatory / ForeignKey / Unique / Remediation) mirroring V1 PolicyDecisionSummaryFormatter; per-bucket count + first-3-sample SsKeys + bucket narration. Operator-readable prose; deterministic ordering across buckets."
              TransformSite.dataIntent "totalRollup"
                "Aggregate structural-tightening count + remediation count across the three axes. Operator surface for 'how much did tightening do this run.'" ]
