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
            "unique-index candidates tightened — declared unique or profile shows no duplicates."
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
        for bucket in bucketOrder do
            writeBucket sb (Map.find bucket rollups)
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
        |> String.concat System.Environment.NewLine

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
