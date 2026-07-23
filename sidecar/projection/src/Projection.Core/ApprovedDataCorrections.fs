namespace Projection.Core

/// The **config-authored + pure engine** half of approved inline data
/// corrections. The durable value vocabulary (derivation / guard / receipt) sits
/// earlier (`DataCorrectionReceipt.fs`, ahead of `Episode.fs`); this module sits
/// after `SliceSpec.fs` because a correction gates row membership on a NULL-aware
/// `Predicate`, and derives values by resolving `AttributeCoordinate`s against
/// the catalog.
///
/// The engine is PURE: `apply` takes the acquired rows (`Map<SsKey, StaticRow
/// list>`), the catalog (for subject/relationship resolution and reference key
/// sets), and the approved corrections, and returns the corrected rows plus
/// count-bearing receipts — or a NAMED refusal. It builds no SQL. The Pipeline
/// injects the corrected rows before the data composers / load plan see them
/// (`emitted_or_loaded_rows = apply(receipts, acquired_source_rows)`), and threads
/// the receipts onto the episode + row-fidelity proof.
///
/// Every correction is FAIL-CLOSED: an unresolved subject/source/parent/sentinel,
/// a finding-count mismatch, an incomplete coverage, an extra reference match —
/// each is a refusal that names itself, never a silent skip. A default config
/// with no corrections leaves the rows byte-identical.

/// A configured derivation with the inputs it needs to transform rows. The bare
/// `DataCorrectionDerivation` (the classification axis on receipts) is projected
/// from this by `toBare`.
[<RequireQualifiedAccess>]
type DataCorrectionDerivationSpec =
    /// Copy the source attribute's cell into the subject cell (same row).
    | SameRowAttribute of source: AttributeCoordinate
    /// Copy a parent row's attribute into the subject cell, joined through the
    /// named relationship on the subject's kind (the one cross-kind derivation).
    | ParentAttribute of relationship: string * parentSource: AttributeCoordinate
    /// Set the subject cell to a typed configured literal (sentinel user id,
    /// migration instant, any constant).
    | ConstantLiteral of value: string
    /// Remove matching rows from the emitted/loaded row set.
    | ExcludeRows

[<RequireQualifiedAccess>]
module DataCorrectionDerivationSpec =

    let toBare (spec: DataCorrectionDerivationSpec) : DataCorrectionDerivation =
        match spec with
        | DataCorrectionDerivationSpec.SameRowAttribute _ -> DataCorrectionDerivation.SameRowAttribute
        | DataCorrectionDerivationSpec.ParentAttribute _  -> DataCorrectionDerivation.ParentAttribute
        | DataCorrectionDerivationSpec.ConstantLiteral _  -> DataCorrectionDerivation.ConstantLiteral
        | DataCorrectionDerivationSpec.ExcludeRows        -> DataCorrectionDerivation.ExcludeRows

    /// Whether the derivation needs another kind's rows — the pipelined-path
    /// two-phase fallback trigger. Only `ParentAttribute` is cross-kind.
    let isCrossKind (spec: DataCorrectionDerivationSpec) : bool =
        DataCorrectionDerivation.isCrossKind (toBare spec)

/// A configured extra reference probe for `NoConfiguredReferenceMatches`: a
/// logical attribute (in some entity) that REFERENCES the subject's primary key
/// but is NOT a formal FK the catalog carries. The exclusion is UNSAFE if any
/// RETAINED row points — via this attribute — at a subject row about to be
/// excluded. Correlated to the exact excluded PK values (the same dynamic check
/// the formal inbound-FK guard performs), NOT a blanket "any row matches the
/// entity": a probe that only asks "does the entity have rows" would refuse
/// every exclusion whenever the referencing table is non-empty.
type ConfiguredReferenceProbe =
    { ReferencingAttribute : AttributeCoordinate }

/// One approved, publish-time inline data correction. `Subject` is the target
/// attribute (reusing the operator-authorable `AttributeCoordinate`);
/// `Predicate` selects the malformed rows; `Derivation` produces the corrected
/// value or the exclusion; `Guards` are the fail-closed safety conditions.
/// `Enabled = false` skips the correction entirely (no receipt).
type ApprovedDataCorrection =
    { Id                  : string
      SourceRemediationId : string option
      Enabled             : bool
      Subject             : AttributeCoordinate
      Predicate           : Predicate option
      Derivation          : DataCorrectionDerivationSpec
      Guards              : DataCorrectionGuard list
      EvidenceColumns     : AttributeCoordinate list
      /// The operator's expected match count — the `ExpectedFindingCount` guard
      /// compares `RowsMatched` against it (evidence-drift protection).
      ExpectedCount       : int64 option
      /// The referenced target entity whose key set the `SentinelExists` /
      /// `SourceReferencesExistingTarget` guards probe.
      ReferencedEntity    : EntityCoordinate option
      /// Extra reference probes for `NoConfiguredReferenceMatches`.
      ConfiguredProbes    : ConfiguredReferenceProbe list
      ApprovedBy          : string option
      ApprovedAt          : string option }

/// The engine's output: the corrected row map and the count-bearing receipts.
type CorrectionOutcome =
    { CorrectedRows : Map<SsKey, StaticRow list>
      Receipts      : DataCorrectionReceipt list }

[<RequireQualifiedAccess>]
module ApprovedDataCorrections =

    let private ciEq (a: string) (b: string) : bool =
        System.String.Equals(a, b, System.StringComparison.OrdinalIgnoreCase)

    let private err (code: string) (message: string) : Result<'a> =
        Result.failureOf (ValidationError.create code message)

    /// The rows of a kind: the correction map first (bootstrap-acquired rows),
    /// else the catalog's static populations. Read-only lookup for guards; write
    /// targets always land back in the output map.
    let private rowsOfKind (catalog: Catalog) (rows: Map<SsKey, StaticRow list>) (kindKey: SsKey) : StaticRow list =
        match Map.tryFind kindKey rows with
        | Some rs -> rs
        | None ->
            match Catalog.tryFindKind kindKey catalog with
            | Some k -> Kind.staticPopulations k
            | None -> []

    /// The single primary-key attribute's `Name` for a kind (the FK-target key).
    let private pkNameOf (catalog: Catalog) (kindKey: SsKey) : Name option =
        Catalog.tryFindKind kindKey catalog
        |> Option.bind (fun k -> Kind.primaryKey k |> List.tryHead |> Option.map (fun a -> a.Name))

    /// The set of PK cell values across a kind's rows — the target key set the
    /// sentinel / reference guards probe.
    let private keySetOf (catalog: Catalog) (rows: Map<SsKey, StaticRow list>) (kindKey: SsKey) : Set<string> =
        match pkNameOf catalog kindKey with
        | None -> Set.empty
        | Some pk ->
            rowsOfKind catalog rows kindKey
            |> List.choose (fun r -> StaticRow.value pk r)
            |> Set.ofList

    /// Resolve an entity coordinate to its kind `SsKey` (case-insensitive; named
    /// not-found / ambiguity refusals), mirroring `AttributeCoordinate.resolveFull`.
    let private resolveEntity (catalog: Catalog) (coord: EntityCoordinate) : Result<SsKey> =
        let matches =
            Catalog.allModulesKinds catalog
            |> List.filter (fun (m, k) ->
                (coord.Module = "" || ciEq (Name.value m.Name) coord.Module) && ciEq (Name.value k.Name) coord.Entity)
            |> List.map (fun (_, k) -> k.SsKey)
            |> List.distinct
        match matches with
        | [ one ] -> Result.success one
        | []      -> err "dataCorrection.entity.notFound" (String.concat "" [ "referenced entity '"; coord.Module; "/"; coord.Entity; "' is not in the model" ])
        | _       -> err "dataCorrection.entity.ambiguous" (String.concat "" [ "referenced entity '"; coord.Module; "/"; coord.Entity; "' is ambiguous across the resolved scope" ])

    /// A deterministic SHA-256 hex digest over a set of rows' subject cells
    /// (sorted by row identity) — the receipt's before/after content anchor the
    /// row-fidelity replay bounds against. SHA-256-in-Core is the house
    /// precedent (`RowDigester.hashRowBytes`, `ActConsent`).
    let private digestOf (subject: Name) (rows: StaticRow list) : string =
        let canonical =
            rows
            |> List.map (fun r ->
                let idText = SsKey.serialize r.Identifier
                let cellText = match StaticRow.value subject r with Some v -> String.concat "" [ "S:"; v ] | None -> "N"
                String.concat "" [ idText; cellText ])
            |> List.sort
            |> String.concat ""
        let bytes = System.Text.Encoding.UTF8.GetBytes canonical
        let hash = System.Security.Cryptography.SHA256.HashData(System.ReadOnlySpan<byte>(bytes))
        (System.Convert.ToHexString hash).ToLowerInvariant()

    /// A deterministic SHA-256 hex digest over a set of rows' EVIDENCE cells (the
    /// supporting metadata columns), sorted by row identity — the audit anchor a
    /// reviewer replays to confirm the derivation stood on the evidence the
    /// receipt names.
    let private evidenceDigestOf (evidenceNames: Name list) (rows: StaticRow list) : string =
        let canonical =
            rows
            |> List.map (fun r ->
                let idText = SsKey.serialize r.Identifier
                let cells =
                    evidenceNames
                    |> List.map (fun n ->
                        match StaticRow.value n r with
                        | Some v -> String.concat "" [ Name.value n; "=S:"; v ]
                        | None   -> String.concat "" [ Name.value n; "=N" ])
                    |> String.concat ""
                String.concat "" [ idText; "|"; cells ])
            |> List.sort
            |> String.concat ""
        let bytes = System.Text.Encoding.UTF8.GetBytes canonical
        let hash = System.Security.Cryptography.SHA256.HashData(System.ReadOnlySpan<byte>(bytes))
        (System.Convert.ToHexString hash).ToLowerInvariant()

    let private identitySet (rows: StaticRow list) : Set<SsKey> =
        rows |> List.map (fun r -> r.Identifier) |> Set.ofList

    /// Apply one enabled correction, threading the row map. Returns the updated
    /// map + the receipt, or a named fail-closed refusal.
    let private applyOne
        (catalog: Catalog)
        (rows: Map<SsKey, StaticRow list>)
        (c: ApprovedDataCorrection)
        : Result<Map<SsKey, StaticRow list> * DataCorrectionReceipt> =
        match AttributeCoordinate.resolveFull catalog c.Subject with
        | Error _ ->
            err "dataCorrection.subject.unresolved"
                (String.concat "" [ "correction '"; c.Id; "': subject "; c.Subject.Module; "/"; c.Subject.Entity; "/"; c.Subject.Attribute; " is not in the model" ])
        | Ok (kindKey, subjectName, _) ->
            let kindRows = rowsOfKind catalog rows kindKey
            let predicate = c.Predicate |> Option.defaultValue Predicate.All
            let matched = kindRows |> List.filter (fun r -> Predicate.eval r predicate)
            let hasGuard g = List.contains g c.Guards

            // Per-derivation: compute the changeable subset (selector guards), the
            // per-row transform, an optional whole-set assertion, and the bare
            // derivation for the receipt.
            let plan
                : Result<StaticRow list * (StaticRow -> StaticRow) * (unit -> Result<unit>)> =
                match c.Derivation with
                | DataCorrectionDerivationSpec.SameRowAttribute source ->
                    match AttributeCoordinate.resolveFull catalog source with
                    | Error _ -> err "dataCorrection.source.unresolved" (String.concat "" [ "correction '"; c.Id; "': source attribute is not in the model" ])
                    | Ok (srcKindKey, srcName, _) when srcKindKey <> kindKey ->
                        err "dataCorrection.source.differentKind" (String.concat "" [ "correction '"; c.Id; "': same-row source attribute is on a different kind than the subject" ])
                    | Ok (_, srcName, _) ->
                        let refKeySet =
                            if hasGuard DataCorrectionGuard.SourceReferencesExistingTarget then
                                match c.ReferencedEntity with
                                | Some ent -> resolveEntity catalog ent |> Result.map (keySetOf catalog rows)
                                | None -> err "dataCorrection.referencedEntity.missing" (String.concat "" [ "correction '"; c.Id; "': sourceReferencesExistingTarget guard needs a referencedEntity" ])
                            else Result.success Set.empty
                        match refKeySet with
                        | Error e -> Error e
                        | Ok keySet ->
                            let selectorOk (r: StaticRow) =
                                (not (hasGuard DataCorrectionGuard.TargetIsNull) || Option.isNone (StaticRow.value subjectName r))
                                && (not (hasGuard DataCorrectionGuard.SourceIsNotNull) || Option.isSome (StaticRow.value srcName r))
                                && (not (hasGuard DataCorrectionGuard.SourceReferencesExistingTarget)
                                    || (match StaticRow.value srcName r with Some v -> Set.contains v keySet | None -> false))
                            let changeable = matched |> List.filter selectorOk
                            let transform (r: StaticRow) = { r with Values = Map.add subjectName (StaticRow.value srcName r) r.Values }
                            Result.success (changeable, transform, (fun () -> Result.success ()))

                | DataCorrectionDerivationSpec.ParentAttribute (relationship, parentSource) ->
                    match Catalog.tryFindKind kindKey catalog with
                    | None -> err "dataCorrection.subject.kindMissing" (String.concat "" [ "correction '"; c.Id; "': subject kind missing from the catalog" ])
                    | Some subjectKind ->
                        match subjectKind.References |> List.tryFind (fun r -> ciEq (Name.value r.Name) relationship) with
                        | None -> err "dataCorrection.relationship.unresolved" (String.concat "" [ "correction '"; c.Id; "': relationship '"; relationship; "' is not a reference on the subject kind" ])
                        | Some reference ->
                            let fkName =
                                subjectKind.Attributes
                                |> List.tryFind (fun a -> a.SsKey = reference.SourceAttribute)
                                |> Option.map (fun a -> a.Name)
                            match fkName with
                            | None -> err "dataCorrection.relationship.fkMissing" (String.concat "" [ "correction '"; c.Id; "': relationship's source attribute is missing" ])
                            | Some fk ->
                                let parentKindKey = reference.TargetKind
                                match AttributeCoordinate.resolveFull catalog parentSource with
                                | Error _ -> err "dataCorrection.parentSource.unresolved" (String.concat "" [ "correction '"; c.Id; "': parent source attribute is not in the model" ])
                                | Ok (pKindKey, _, _) when pKindKey <> parentKindKey ->
                                    err "dataCorrection.parentSource.wrongKind" (String.concat "" [ "correction '"; c.Id; "': parent source attribute is not on the relationship's target kind" ])
                                | Ok (_, pSrcName, _) ->
                                    match pkNameOf catalog parentKindKey with
                                    | None -> err "dataCorrection.parent.noPk" (String.concat "" [ "correction '"; c.Id; "': parent kind has no primary key to join on" ])
                                    | Some parentPk ->
                                        let parentMap =
                                            rowsOfKind catalog rows parentKindKey
                                            |> List.choose (fun r -> StaticRow.value parentPk r |> Option.map (fun k -> k, r))
                                            |> Map.ofList
                                        let parentOf (r: StaticRow) = StaticRow.value fk r |> Option.bind (fun k -> Map.tryFind k parentMap)
                                        let selectorOk (r: StaticRow) =
                                            (not (hasGuard DataCorrectionGuard.TargetIsNull) || Option.isNone (StaticRow.value subjectName r))
                                            && (not (hasGuard DataCorrectionGuard.ParentExists) || Option.isSome (parentOf r))
                                            && (not (hasGuard DataCorrectionGuard.ParentSourceIsNotNull)
                                                || (match parentOf r with Some p -> Option.isSome (StaticRow.value pSrcName p) | None -> false))
                                        let changeable = matched |> List.filter selectorOk
                                        let transform (r: StaticRow) =
                                            match parentOf r with
                                            | Some p -> { r with Values = Map.add subjectName (StaticRow.value pSrcName p) r.Values }
                                            | None -> r
                                        Result.success (changeable, transform, (fun () -> Result.success ()))

                | DataCorrectionDerivationSpec.ConstantLiteral value ->
                    let sentinelAssertion () =
                        if hasGuard DataCorrectionGuard.SentinelExists then
                            match c.ReferencedEntity with
                            | None -> err "dataCorrection.referencedEntity.missing" (String.concat "" [ "correction '"; c.Id; "': sentinelExists guard needs a referencedEntity" ])
                            | Some ent ->
                                match resolveEntity catalog ent with
                                | Error e -> Error e
                                | Ok k ->
                                    if Set.contains value (keySetOf catalog rows k) then Result.success ()
                                    else err "dataCorrection.sentinel.absent" (String.concat "" [ "correction '"; c.Id; "': sentinel value '"; value; "' does not exist in the referenced target key set" ])
                        else Result.success ()
                    let selectorOk (r: StaticRow) =
                        (not (hasGuard DataCorrectionGuard.TargetIsNull) || Option.isNone (StaticRow.value subjectName r))
                    let changeable = matched |> List.filter selectorOk
                    let transform (r: StaticRow) = { r with Values = Map.add subjectName (Some value) r.Values }
                    Result.success (changeable, transform, sentinelAssertion)

                | DataCorrectionDerivationSpec.ExcludeRows ->
                    let changeable = matched
                    let excludedIds = identitySet changeable
                    let excludedPkValues =
                        match pkNameOf catalog kindKey with
                        | Some pk -> changeable |> List.choose (fun r -> StaticRow.value pk r) |> Set.ofList
                        | None -> Set.empty
                    let inboundAssertion () =
                        if hasGuard DataCorrectionGuard.NoFormalInboundReferences then
                            let offending =
                                Catalog.allModulesKinds catalog
                                |> List.exists (fun (_, j) ->
                                    j.References
                                    |> List.filter (fun r -> r.TargetKind = kindKey)
                                    |> List.exists (fun r ->
                                        match j.Attributes |> List.tryFind (fun a -> a.SsKey = r.SourceAttribute) |> Option.map (fun a -> a.Name) with
                                        | None -> false
                                        | Some fk ->
                                            let jRows = rowsOfKind catalog rows j.SsKey
                                            let jRetained =
                                                if j.SsKey = kindKey then jRows |> List.filter (fun r -> not (Set.contains r.Identifier excludedIds))
                                                else jRows
                                            jRetained |> List.exists (fun r ->
                                                match StaticRow.value fk r with Some v -> Set.contains v excludedPkValues | None -> false)))
                            if offending then err "dataCorrection.exclude.inboundReference" (String.concat "" [ "correction '"; c.Id; "': a retained row formally references a row about to be excluded" ])
                            else Result.success ()
                        else Result.success ()
                    let probesAssertion () =
                        if hasGuard DataCorrectionGuard.NoConfiguredReferenceMatches then
                            let rec loop probes =
                                match probes with
                                | [] -> Result.success ()
                                | (probe: ConfiguredReferenceProbe) :: rest ->
                                    match AttributeCoordinate.resolveFull catalog probe.ReferencingAttribute with
                                    | Error _ ->
                                        err "dataCorrection.probe.unresolved" (String.concat "" [ "correction '"; c.Id; "': configured reference probe attribute "; probe.ReferencingAttribute.Module; "/"; probe.ReferencingAttribute.Entity; "/"; probe.ReferencingAttribute.Attribute; " is not in the model" ])
                                    | Ok (probeKindKey, refAttrName, _) ->
                                        // Retained rows of the probe's kind (exclude the subject's
                                        // own about-to-be-excluded rows when the probe points at
                                        // the same kind). Offending iff any retained row references
                                        // — via this attribute — an excluded subject PK value.
                                        let retained =
                                            let pRows = rowsOfKind catalog rows probeKindKey
                                            if probeKindKey = kindKey then pRows |> List.filter (fun r -> not (Set.contains r.Identifier excludedIds))
                                            else pRows
                                        let offending =
                                            retained |> List.exists (fun r ->
                                                match StaticRow.value refAttrName r with Some v -> Set.contains v excludedPkValues | None -> false)
                                        if offending then
                                            err "dataCorrection.exclude.configuredReferenceMatch" (String.concat "" [ "correction '"; c.Id; "': a retained row references (via '"; Name.value refAttrName; "') a row about to be excluded" ])
                                        else loop rest
                            loop c.ConfiguredProbes
                        else Result.success ()
                    let assertion () =
                        match inboundAssertion () with
                        | Error e -> Error e
                        | Ok () -> probesAssertion ()
                    Result.success (changeable, id, assertion)

            match plan with
            | Error e -> Error e
            | Ok (changeable, transform, assertion) ->
                let matchedCount = int64 (List.length matched)
                let changeableCount = int64 (List.length changeable)
                // Count / coverage guards (fail-closed).
                let countCheck () =
                    if hasGuard DataCorrectionGuard.ExpectedFindingCount then
                        match c.ExpectedCount with
                        | None -> err "dataCorrection.expectedCount.missing" (String.concat "" [ "correction '"; c.Id; "': expectedFindingCount guard needs an expectedCount" ])
                        | Some expected when expected <> matchedCount ->
                            err "dataCorrection.expectedCount.mismatch" (String.concat "" [ "correction '"; c.Id; "': expected "; string expected; " matched rows, found "; string matchedCount ])
                        | Some _ -> Result.success ()
                    else Result.success ()
                let coverageCheck () =
                    if hasGuard DataCorrectionGuard.ExpectedCoverage && changeableCount <> matchedCount then
                        err "dataCorrection.coverage.incomplete" (String.concat "" [ "correction '"; c.Id; "': coverage incomplete — "; string changeableCount; " of "; string matchedCount; " matched rows carry the required evidence" ])
                    else Result.success ()
                match countCheck () with
                | Error e -> Error e
                | Ok () ->
                    match coverageCheck () with
                    | Error e -> Error e
                    | Ok () ->
                        match assertion () with
                        | Error e -> Error e
                        | Ok () ->
                            let isExclude = (match c.Derivation with DataCorrectionDerivationSpec.ExcludeRows -> true | _ -> false)
                            let changeableIds = identitySet changeable
                            let newKindRows =
                                if isExclude then kindRows |> List.filter (fun r -> not (Set.contains r.Identifier changeableIds))
                                else kindRows |> List.map (fun r -> if Set.contains r.Identifier changeableIds then transform r else r)
                            let beforeDigest = Some (digestOf subjectName changeable)
                            let afterDigest =
                                if isExclude then None
                                else Some (digestOf subjectName (changeable |> List.map transform))
                            let guardResults =
                                c.Guards
                                |> List.map (fun g ->
                                    let observed =
                                        match g with
                                        | DataCorrectionGuard.ExpectedFindingCount -> Some matchedCount
                                        | DataCorrectionGuard.ExpectedCoverage     -> Some changeableCount
                                        | _ -> None
                                    DataCorrectionGuardResult.passed g observed)
                            // Resolve the configured evidence columns FAIL-CLOSED (a
                            // named-but-absent evidence column is a config error), and
                            // digest their cells on the changed rows so the receipt
                            // preserves the evidence the derivation was accepted on.
                            let evidenceNamesR =
                                let rec loop acc cols =
                                    match cols with
                                    | [] -> Result.success (List.rev acc)
                                    | (ec: AttributeCoordinate) :: rest ->
                                        match AttributeCoordinate.resolveFull catalog ec with
                                        | Ok (_, n, _) -> loop (n :: acc) rest
                                        | Error _ -> err "dataCorrection.evidence.unresolved" (String.concat "" [ "correction '"; c.Id; "': evidence column "; ec.Module; "/"; ec.Entity; "/"; ec.Attribute; " is not in the model" ])
                                loop [] c.EvidenceColumns
                            match evidenceNamesR with
                            | Error e -> Error e
                            | Ok evidenceNames ->
                                let evidenceDigest =
                                    if List.isEmpty evidenceNames then None
                                    else Some (evidenceDigestOf evidenceNames changeable)
                                let receipt =
                                    { CorrectionId        = c.Id
                                      SourceRemediationId = c.SourceRemediationId
                                      Subject             = c.Subject
                                      Derivation          = DataCorrectionDerivationSpec.toBare c.Derivation
                                      GuardResults        = guardResults
                                      RowsMatched         = matchedCount
                                      RowsChanged         = (if isExclude then 0L else changeableCount)
                                      RowsExcluded        = (if isExclude then changeableCount else 0L)
                                      BeforeDigest        = beforeDigest
                                      AfterDigest         = afterDigest
                                      EvidenceColumns     = c.EvidenceColumns
                                      EvidenceDigest      = evidenceDigest
                                      ApprovedBy          = c.ApprovedBy
                                      ApprovedAt          = c.ApprovedAt }
                                Result.success (Map.add kindKey newKindRows rows, receipt)

    /// Apply approved corrections to a row map, fail-closed. Disabled corrections
    /// are skipped (no receipt). Returns the corrected rows + receipts, or the
    /// first named refusal. A default (empty) correction list is the identity:
    /// `apply catalog [] rows = { CorrectedRows = rows; Receipts = [] }`.
    let apply
        (catalog: Catalog)
        (corrections: ApprovedDataCorrection list)
        (rows: Map<SsKey, StaticRow list>)
        : Result<CorrectionOutcome> =
        let enabled = corrections |> List.filter (fun c -> c.Enabled)
        let rec loop accRows accReceipts cs =
            match cs with
            | [] -> Result.success { CorrectedRows = accRows; Receipts = DataCorrectionReceipt.sorted (List.rev accReceipts) }
            | c :: rest ->
                match applyOne catalog accRows c with
                | Error e -> Error e
                | Ok (newRows, receipt) -> loop newRows (receipt :: accReceipts) rest
        loop rows [] enabled

    /// Whether any enabled correction is cross-kind (parent-derived) — the
    /// pipelined publish path names a deliberate two-phase fallback when this
    /// holds, because it never materializes a cross-kind row map.
    let requiresTwoPhase (corrections: ApprovedDataCorrection list) : bool =
        corrections |> List.exists (fun c -> c.Enabled && DataCorrectionDerivationSpec.isCrossKind c.Derivation)

    /// The row-fidelity **reconciliation law** (the "receipt-bounded" half of
    /// byte-identity-with-noted-exceptions): the receipts a proof REPLAYS over the
    /// source must match the receipts the publish RECORDED, count-for-count. A
    /// recorded receipt whose replay changes a different number of rows fails the
    /// proof BY NAME — a receipt claiming 105 rows changed but replaying 104 is a
    /// red proof, not a silent pass. Compared by `(CorrectionId, RowsChanged,
    /// RowsExcluded)`; a receipt recorded-but-not-replayed (or vice versa) is also
    /// a named failure. `reconcile [] [] = Ok` (the no-correction proof).
    let reconcile (recorded: DataCorrectionReceipt list) (replayed: DataCorrectionReceipt list) : Result<unit> =
        let toMap (rs: DataCorrectionReceipt list) = rs |> List.map (fun r -> r.CorrectionId, r) |> Map.ofList
        let recMap = toMap recorded
        let repMap = toMap replayed
        let ids =
            Set.union
                (recorded |> List.map (fun r -> r.CorrectionId) |> Set.ofList)
                (replayed |> List.map (fun r -> r.CorrectionId) |> Set.ofList)
        let mismatch =
            ids
            |> Set.toList
            |> List.sort
            |> List.tryPick (fun id ->
                match Map.tryFind id recMap, Map.tryFind id repMap with
                | Some a, Some b when a.RowsChanged = b.RowsChanged && a.RowsExcluded = b.RowsExcluded -> None
                | Some a, Some b ->
                    Some (String.concat "" [ "correction '"; id; "': recorded (changed="; string a.RowsChanged; ", excluded="; string a.RowsExcluded; ") ≠ replayed (changed="; string b.RowsChanged; ", excluded="; string b.RowsExcluded; ")" ])
                | Some _, None -> Some (String.concat "" [ "correction '"; id; "': recorded by the publish but not reproduced by the fidelity replay" ])
                | None, Some _ -> Some (String.concat "" [ "correction '"; id; "': reproduced by the fidelity replay but never recorded by the publish" ])
                | None, None -> None)
        match mismatch with
        | None -> Result.success ()
        | Some detail -> err "dataCorrection.fidelity.receiptMismatch" detail

    /// Pillar-9 registry metadata (A41) — the approved-correction transform is a
    /// registered, first-class metadata surface, NOT an implementation detail
    /// hidden inside the emitters. Domain.Data (it changes row values / row
    /// membership); StageBinding.Pipeline (it fires between acquisition and the
    /// data composers). The `OperatorIntent Insertion` site is the value/membership
    /// change (the operator's approved corrections drive it); the two `DataIntent`
    /// sites are the guard evaluation over acquired evidence and the correction
    /// receipts / coverage diagnostics. Appended to `RegisteredAllTransforms.all`.
    let registeredMetadata : RegisteredTransformMetadata =
        { Name         = "approvedDataCorrections"
          Domain       = Data
          StageBinding = Pipeline
          Sites =
            [ TransformSite.operatorIntent "rowCorrection" Insertion
                "Approved row-correction derivations (same-row / parent / constant / exclude) change the data VALUES and row MEMBERSHIP that emission and load receive — driven entirely by the operator-approved `emission.dataCorrections`. Empty ⇒ identity over rows (skeleton-purity preserved)."
              TransformSite.dataIntent "guardEvaluation"
                "Fail-closed guard evaluation over acquired row evidence (null / coverage / reference / finding-count probes). Pure over the row map + catalog; no operator opinion beyond which guards the correction declares."
              TransformSite.dataIntent "correctionReceipts"
                "Emit count-bearing correction receipts (rows matched / changed / excluded, guard results, before/after digest) threaded onto the episode + row-fidelity proof — the intervention ledger that bounds the byte-identity-with-noted-exceptions claim." ]
          Status = Active }
