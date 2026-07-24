namespace Projection.Core

/// The **pure delta kernel** of dynamic bridge-row staging — the companion that
/// lets a bridge retarget derive its bridge-row supply from the LIVE estate
/// instead of a pre-authored supplement. Given three observed inputs — the
/// referenced key values (every distinct non-null value the in-scope retargeting
/// FKs hold), a snapshot of the SOURCE rows for those keys, and a snapshot of
/// the BRIDGE rows for those keys — it computes, per referenced key, a closed
/// verdict: already complete, safely stageable (an insert supplement or a
/// fill-only identity update), or a NAMED block. From the verdicts it assembles
/// the staged changes and the planned post-staging evidence the retarget
/// decision is taken over.
///
/// **Zero I/O, zero estate.** Like `BridgeRetarget`, this module is total over
/// SUPPLIED observations: an acquisition boundary gathers the snapshots (which
/// tables, which columns, which connection — all config); the kernel never asks
/// where they came from. Which tables participate, which attribute is the key,
/// which is the identity, how a staged insert's payload maps from source cells —
/// all enter via `BridgeRowStagingSpec`, so nothing estate-specific lives here.
///
/// **The safety rule (why the staged-change vocabulary is closed).** A staged
/// change may only be an INSERT of a row that does not exist, or a FILL of an
/// identity cell that is NULL. An existing non-null bridge cell is NEVER
/// overwritten: a bridge identity that disagrees with the source is an
/// `IdentityConflict` BLOCK, not an update — and the update lane's WHERE guard
/// re-asserts the NULL at apply time. This is deliberately NOT the full-row
/// MERGE the migration-dependency lane uses: a sparse row through that lane
/// would NULL omitted cells on matched rows, exactly the overwrite this kernel
/// exists to make unrepresentable.
///
/// **Fail-closed evidence.** `plannedEvidence` yields the derived
/// `BridgeRetargetEvidence` ONLY when every referenced key's verdict is clean —
/// any block withholds the evidence entirely, so the retarget's profile stays
/// `unproven` and the retarget cannot land. A blocked plan still carries its
/// full verdict ledger (the audit artifact renders it), so the refusal is
/// named, never silent.

/// One observed SOURCE row for a referenced key: the key cell, the identity
/// cell, and the payload cells a staged insert maps from — ONLY the mapped
/// attributes (data minimization: the acquisition selects, and the artifacts
/// persist, no more than the staging spec names).
type BridgeSourceSnapshotRow =
    { Key      : string
      Identity : string option
      Cells    : Map<Name, string option> }

/// One observed BRIDGE row for a referenced key: the key cell and the identity
/// cell. (Keys are non-null by construction — the snapshot is keyed on the
/// referenced values; bridge-wide key nullness/duplication arrive separately as
/// `BridgeWideStats`.)
type BridgeSnapshotRow =
    { Key      : string
      Identity : string option }

/// Bridge-TABLE-wide facts the keyed snapshot cannot see (it only reads rows
/// matching the referenced keys): how many bridge rows carry a NULL key and how
/// many key values occur on more than one row, across the WHOLE bridge table.
/// These flow into the planned evidence's `BridgeKeyNulls` /
/// `BridgeKeyDuplicates` so a retarget never clears against an unsound bridge
/// key just because the unsoundness sat outside the referenced subset.
type BridgeWideStats =
    { KeyNullCount      : int64
      KeyDuplicateCount : int64 }

/// One insert-payload mapping: the staged bridge cell takes the source row's
/// cell at `SourceAttribute`.
type BridgeStagingMapping =
    { BridgeAttribute : Name
      SourceAttribute : Name }

/// One insert-payload constant: the staged bridge cell takes the operator's
/// declared raw value (a mandatory bridge column not derivable from the source
/// — a lookup FK, a discriminator — is supplied here). `None` is SQL NULL.
type BridgeStagingConstant =
    { BridgeAttribute : Name
      Value           : string option }

/// The staging declaration's pure shape — what the delta kernel needs to know
/// about ONE source→bridge pairing, fully config-supplied (the binder resolves
/// operator text to these typed names fail-closed). `StagingId` echoes onto the
/// plan for correlation.
type BridgeRowStagingSpec =
    { StagingId               : string
      BridgeKeyAttribute      : Name
      BridgeIdentityAttribute : Name
      Mappings                : BridgeStagingMapping list
      Constants               : BridgeStagingConstant list }

/// The closed per-key verdict vocabulary. Stageable outcomes and blocks are
/// distinct cases, never severity flags — the compiler forces every consumer
/// (staging emission, evidence derivation, the audit artifact) to decide each
/// one.
[<RequireQualifiedAccess>]
type BridgeRowVerdict =
    /// A bridge row exists and its identity cell is populated. `identityVerified`
    /// is TRUE when the source identity is present and equal (the bridge value is
    /// corroborated); FALSE when the source identity is absent (the bridge value
    /// stands but cannot be corroborated — provenance downgrades to Ambiguous,
    /// a warning, never a block).
    | AlreadyComplete of identityVerified: bool
    /// No bridge row exists and the source row supplies everything a complete
    /// row needs — stage an INSERT supplement.
    | StageInsert
    /// A bridge row exists, its identity cell is NULL, and the source has the
    /// identity — stage a FILL-ONLY update (`SET identity WHERE key AND identity
    /// IS NULL`). The only update the vocabulary can express.
    | StageIdentityUpdate
    /// BLOCK: a referenced key has no source row — the FK value does not resolve
    /// to the original parent, so there is nothing to derive a bridge row from.
    | SourceRowMissing
    /// BLOCK: the source row exists but its identity cell is NULL — a complete
    /// bridge row cannot be derived from the estate alone (an external identity
    /// authority would have to supply it). `bridgeRowExists` distinguishes
    /// "no row could be staged" from "an existing row could not be completed".
    | SourceIdentityMissing of bridgeRowExists: bool
    /// BLOCK: the bridge identity is populated and DISAGREES with the source
    /// identity. Never auto-resolved — overwriting either side silently is the
    /// exact failure mode the safety rule forbids.
    | IdentityConflict of bridgeValue: string * sourceValue: string
    /// BLOCK: more than one bridge row carries this key — resolution through the
    /// bridge would be ambiguous.
    | AmbiguousBridgeRows of bridgeRowCount: int
    /// BLOCK: more than one source row carries this key — the source key is not
    /// unique over the observed rows, so derivation has no single row to stand on.
    | AmbiguousSourceRows of sourceRowCount: int

/// One staged INSERT supplement: the new bridge row's full cell map (key +
/// identity + mapped payload + constants), raw-string cells per the codebase's
/// row convention (`None` = SQL NULL).
type BridgeStagedInsert =
    { Key   : string
      Cells : Map<Name, string option> }

/// One staged FILL-ONLY identity update: set `Identity` on the bridge row at
/// `Key` — the apply-time WHERE re-asserts the identity cell is still NULL.
type BridgeStagedIdentityUpdate =
    { Key      : string
      Identity : string }

/// The computed plan for one staging declaration: the full per-key verdict
/// ledger (key-sorted, deterministic — the audit artifact renders it "no more,
/// no less"), the staged changes, and the observed within-scope counts the
/// evidence derives from.
type BridgeRowStagingPlan =
    { StagingId       : string
      Verdicts        : (string * BridgeRowVerdict) list
      Inserts         : BridgeStagedInsert list
      IdentityUpdates : BridgeStagedIdentityUpdate list }

[<RequireQualifiedAccess>]
module BridgeRowVerdict =

    /// Whether the verdict blocks staging (and therefore withholds the derived
    /// evidence). Total — a new case fails to compile until classified.
    let isBlocking (verdict: BridgeRowVerdict) : bool =
        match verdict with
        | BridgeRowVerdict.AlreadyComplete _
        | BridgeRowVerdict.StageInsert
        | BridgeRowVerdict.StageIdentityUpdate     -> false
        | BridgeRowVerdict.SourceRowMissing
        | BridgeRowVerdict.SourceIdentityMissing _
        | BridgeRowVerdict.IdentityConflict _
        | BridgeRowVerdict.AmbiguousBridgeRows _
        | BridgeRowVerdict.AmbiguousSourceRows _   -> true

    /// The stable diagnostic code for a verdict — the camelCase name the audit
    /// artifact and lineage narration carry.
    let name (verdict: BridgeRowVerdict) : string =
        match verdict with
        | BridgeRowVerdict.AlreadyComplete true    -> "alreadyComplete"
        | BridgeRowVerdict.AlreadyComplete false   -> "alreadyCompleteUnverified"
        | BridgeRowVerdict.StageInsert             -> "stageInsert"
        | BridgeRowVerdict.StageIdentityUpdate     -> "stageIdentityUpdate"
        | BridgeRowVerdict.SourceRowMissing        -> "sourceRowMissing"
        | BridgeRowVerdict.SourceIdentityMissing _ -> "sourceIdentityMissing"
        | BridgeRowVerdict.IdentityConflict _      -> "identityConflict"
        | BridgeRowVerdict.AmbiguousBridgeRows _   -> "ambiguousBridgeRows"
        | BridgeRowVerdict.AmbiguousSourceRows _   -> "ambiguousSourceRows"

[<RequireQualifiedAccess>]
module BridgeRowDelta =

    /// Assemble the staged insert's full cell map from the spec + the source
    /// row. Precedence (later wins): constants, then mappings, then the key and
    /// identity cells — so a declaration can never displace the two cells the
    /// invariant stands on (the binder additionally refuses declarations that
    /// name them). A mapping whose source cell is absent from the snapshot map
    /// yields SQL NULL — the binder's coverage guard has already proven every
    /// MANDATORY bridge attribute is covered, so an absent optional cell is a
    /// legitimate NULL, not a silent hole.
    let private stagedCells
        (spec: BridgeRowStagingSpec)
        (source: BridgeSourceSnapshotRow)
        : Map<Name, string option> =
        let constants = spec.Constants |> List.map (fun c -> c.BridgeAttribute, c.Value)
        let mapped =
            spec.Mappings
            |> List.map (fun m -> m.BridgeAttribute, (Map.tryFind m.SourceAttribute source.Cells |> Option.flatten))
        [ yield! constants
          yield! mapped
          yield spec.BridgeKeyAttribute, Some source.Key
          yield spec.BridgeIdentityAttribute, source.Identity ]
        |> Map.ofList

    /// The per-key decision — total over the observed rows for one key.
    let private verdictFor
        (sources: BridgeSourceSnapshotRow list)
        (bridges: BridgeSnapshotRow list)
        : BridgeRowVerdict =
        match sources, bridges with
        | _, (_ :: _ :: _ as many) -> BridgeRowVerdict.AmbiguousBridgeRows (List.length many)
        | (_ :: _ :: _ as many), _ -> BridgeRowVerdict.AmbiguousSourceRows (List.length many)
        | [], _ -> BridgeRowVerdict.SourceRowMissing
        | [ source ], [] ->
            match source.Identity with
            | Some _ -> BridgeRowVerdict.StageInsert
            | None   -> BridgeRowVerdict.SourceIdentityMissing false
        | [ source ], [ bridge ] ->
            match bridge.Identity, source.Identity with
            | Some b, Some s when b = s -> BridgeRowVerdict.AlreadyComplete true
            | Some b, Some s            -> BridgeRowVerdict.IdentityConflict (b, s)
            | Some _, None              -> BridgeRowVerdict.AlreadyComplete false
            | None,   Some _            -> BridgeRowVerdict.StageIdentityUpdate
            | None,   None              -> BridgeRowVerdict.SourceIdentityMissing true

    /// Compute the staging plan for one declaration. Deterministic: referenced
    /// keys are de-duplicated and ordinal-sorted; every key gets exactly one
    /// verdict; the staged changes are projected from the SAME verdict ledger
    /// the audit artifact renders, so staged ⇔ decided by construction.
    let compute
        (spec: BridgeRowStagingSpec)
        (referencedKeys: string list)
        (sourceRows: BridgeSourceSnapshotRow list)
        (bridgeRows: BridgeSnapshotRow list)
        : BridgeRowStagingPlan =
        let keys =
            referencedKeys
            |> List.distinct
            |> List.sortWith (fun a b -> System.String.CompareOrdinal(a, b))
        let sourcesByKey = sourceRows |> List.groupBy (fun r -> r.Key) |> Map.ofList
        let bridgesByKey = bridgeRows |> List.groupBy (fun r -> r.Key) |> Map.ofList
        let rowsFor key m = Map.tryFind key m |> Option.defaultValue []
        let verdicts =
            keys
            |> List.map (fun key ->
                key, verdictFor (rowsFor key sourcesByKey) (rowsFor key bridgesByKey))
        let inserts =
            verdicts
            |> List.choose (fun (key, verdict) ->
                match verdict with
                | BridgeRowVerdict.StageInsert ->
                    rowsFor key sourcesByKey
                    |> List.tryExactlyOne
                    |> Option.map (fun source -> { Key = key; Cells = stagedCells spec source })
                | _ -> None)
        let identityUpdates =
            verdicts
            |> List.choose (fun (key, verdict) ->
                match verdict with
                | BridgeRowVerdict.StageIdentityUpdate ->
                    rowsFor key sourcesByKey
                    |> List.tryExactlyOne
                    |> Option.bind (fun source -> source.Identity)
                    |> Option.map (fun identity -> { Key = key; Identity = identity })
                | _ -> None)
        { StagingId       = spec.StagingId
          Verdicts        = verdicts
          Inserts         = inserts
          IdentityUpdates = identityUpdates }

    /// The blocking subset of the plan's ledger — what the audit artifact and
    /// the withheld-evidence refusal enumerate.
    let blockingVerdicts (plan: BridgeRowStagingPlan) : (string * BridgeRowVerdict) list =
        plan.Verdicts |> List.filter (fun (_, v) -> BridgeRowVerdict.isBlocking v)

    /// Derive the retarget evidence from the PLANNED post-staging state —
    /// current bridge rows plus the staged inserts/updates — FAIL-CLOSED:
    ///
    ///   * ANY blocking verdict withholds the evidence entirely (`None`), so the
    ///     retarget's profile stays `unproven` and the retarget cannot land;
    ///   * a clean plan yields the honest counts: coverage and parent resolution
    ///     are total by construction (every key resolved to exactly one planned
    ///     bridge row and one source row — that is what "no blocks" means), the
    ///     bridge-WIDE key nullness/duplication pass through from `wide` (an
    ///     unsound bridge key still blocks the retarget even when the unsoundness
    ///     sits outside the referenced subset), and identity provenance is
    ///     `Present` only when every completed row's identity is corroborated or
    ///     freshly staged from the source — an uncorroborated existing value
    ///     downgrades to `Ambiguous` (a warning on the bridge-rows verdict,
    ///     never a block on the retarget).
    let plannedEvidence (wide: BridgeWideStats) (plan: BridgeRowStagingPlan) : BridgeRetargetEvidence option =
        if not (List.isEmpty (blockingVerdicts plan)) then None
        else
            let anyUnverified =
                plan.Verdicts
                |> List.exists (fun (_, v) ->
                    match v with
                    | BridgeRowVerdict.AlreadyComplete false -> true
                    | _ -> false)
            Some
                { UnresolvedThroughBridge = 0L
                  BrokenOriginalParent    = 0L
                  OrphanedBridgeRows      = 0L
                  PayloadConflicts        = 0L
                  BridgeKeyDuplicates     = wide.KeyDuplicateCount
                  BridgeKeyNulls          = wide.KeyNullCount
                  IdentityEvidence        =
                    if anyUnverified then BridgeIdentityEvidence.Ambiguous
                    else BridgeIdentityEvidence.Present }
