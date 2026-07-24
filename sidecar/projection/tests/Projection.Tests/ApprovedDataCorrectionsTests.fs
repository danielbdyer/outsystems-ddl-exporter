module Projection.Tests.ApprovedDataCorrectionsTests

open Xunit
open Projection.Core

// The pure approved-data-correction engine (ApprovedDataCorrections.fs). The
// laws exercised here:
//   * default (no corrections) is the identity — byte-identical rows, no receipts;
//   * each derivation (same-row / constant / parent / exclude) transforms the
//     matched, guard-passing subset and emits ONE count-bearing receipt;
//   * guards are fail-closed — a finding-count mismatch, an absent sentinel, or a
//     retained inbound reference REFUSES the correction by name;
//   * NULL is distinguished from '' (the IsNull predicate + TargetIsNull guard).

let private nm (s: string) : Name = Name.create s |> Result.value
let private key (parts: string list) : SsKey = SsKey.synthesizedComposite "DC" parts |> Result.value

let private attribute (keyParts: string list) (name: string) (isPk: bool) (isMandatory: bool) : Attribute =
    { Attribute.create (key keyParts) (nm name) PrimitiveType.Text with
        Column = ColumnRealization.create name false |> Result.value
        IsPrimaryKey = isPk
        IsMandatory = isMandatory }

let private mkKind (ssKey: SsKey) (name: string) (attrs: Attribute list) (refs: Reference list) : Kind =
    { SsKey = ssKey
      Name = nm name
      Origin = Native
      Modality = []
      Physical = TableId.create "dbo" name |> Result.value
      Attributes = attrs
      References = refs
      Indexes = []
      Description = None
      IsActive = true
      Triggers = []
      ColumnChecks = []
      ExtendedProperties = [] }

let private row (keyParts: string list) (cells: (string * string option) list) : StaticRow =
    { Identifier = key keyParts
      Values = cells |> List.map (fun (n, v) -> nm n, v) |> Map.ofList }

let private customerKey = key [ "Customer" ]
let private accountKey = key [ "Account" ]

let private catalog : Catalog =
    let customer =
        mkKind customerKey "Customer"
            [ attribute [ "Customer"; "Id" ] "Id" true true
              attribute [ "Customer"; "Name" ] "Name" false false ]
            []
    let custRef =
        Reference.create (key [ "Account"; "CustRef" ]) (nm "CustRef") (key [ "Account"; "CustomerId" ]) customerKey
    let account =
        mkKind accountKey "Account"
            [ attribute [ "Account"; "Id" ] "Id" true true
              attribute [ "Account"; "CustomerId" ] "CustomerId" false false
              attribute [ "Account"; "LegacyCustomerId" ] "LegacyCustomerId" false false
              attribute [ "Account"; "OwnerName" ] "OwnerName" false false ]
            [ custRef ]
    Catalog.create
        [ { SsKey = key [ "Mod" ]; Name = nm "Sales"; Kinds = [ customer; account ]; IsActive = true; ExtendedProperties = [] } ]
        []
    |> Result.value

let private customerRows =
    [ row [ "Customer"; "R"; "C1" ] [ "Id", Some "C1"; "Name", Some "Acme" ]
      row [ "Customer"; "R"; "C2" ] [ "Id", Some "C2"; "Name", Some "Beta" ] ]

// a1: needs backfill (CustomerId null, legacy C1 exists)
// a2: CustomerId already C1 (references C1) — skipped by a null predicate
// a3: both null — the malformed exclusion candidate
// a4: legacy C9 does NOT exist in the customer key set
let private accountRows =
    [ row [ "Account"; "R"; "a1" ] [ "Id", Some "a1"; "CustomerId", None;      "LegacyCustomerId", Some "C1"; "OwnerName", None ]
      row [ "Account"; "R"; "a2" ] [ "Id", Some "a2"; "CustomerId", Some "C1"; "LegacyCustomerId", Some "C1"; "OwnerName", None ]
      row [ "Account"; "R"; "a3" ] [ "Id", Some "a3"; "CustomerId", None;      "LegacyCustomerId", None;      "OwnerName", None ]
      row [ "Account"; "R"; "a4" ] [ "Id", Some "a4"; "CustomerId", None;      "LegacyCustomerId", Some "C9"; "OwnerName", None ] ]

let private rowsMap = Map.ofList [ customerKey, customerRows; accountKey, accountRows ]

let private baseCorrection : ApprovedDataCorrection =
    { Id = "c"
      SourceRemediationId = None
      Enabled = true
      Subject = AttributeCoordinate.create "Sales" "Account" "CustomerId"
      Predicate = None
      Derivation = DataCorrectionDerivationSpec.ConstantLiteral ""
      Guards = []
      EvidenceColumns = []
      ExpectedCount = None
      ReferencedEntity = None
      ConfiguredProbes = []
      ApprovedBy = Some "operator"
      ApprovedAt = Some "2026-07-23" }

let private applyOk (corrections: ApprovedDataCorrection list) : CorrectionOutcome =
    match ApprovedDataCorrections.apply catalog corrections rowsMap with
    | Ok o -> o
    | Error es -> failwithf "expected Ok; got %A" (es |> List.map (fun e -> e.Code))

let private applyErr (corrections: ApprovedDataCorrection list) : string =
    match ApprovedDataCorrections.apply catalog corrections rowsMap with
    | Ok _ -> failwith "expected fail-closed refusal; got Ok"
    | Error es -> (List.head es).Code

let private cellOf (o: CorrectionOutcome) (kindKey: SsKey) (rowParts: string list) (col: string) : string option =
    o.CorrectedRows.[kindKey] |> List.find (fun r -> r.Identifier = key rowParts) |> StaticRow.value (nm col)

let private countOf (o: CorrectionOutcome) (kindKey: SsKey) : int =
    o.CorrectedRows.[kindKey] |> List.length

[<Fact>]
let ``no corrections is the identity: rows unchanged, no receipts`` () =
    let o = applyOk []
    Assert.Equal<Map<SsKey, StaticRow list>>(rowsMap, o.CorrectedRows)
    Assert.Empty o.Receipts

[<Fact>]
let ``disabled correction is skipped: no receipt, rows unchanged`` () =
    let c = { baseCorrection with Enabled = false
                                  Derivation = DataCorrectionDerivationSpec.ConstantLiteral "X"
                                  Predicate = Some (Predicate.IsNull (nm "CustomerId")) }
    let o = applyOk [ c ]
    Assert.Equal<Map<SsKey, StaticRow list>>(rowsMap, o.CorrectedRows)
    Assert.Empty o.Receipts

[<Fact>]
let ``same-row backfill copies source into null target, source-not-null narrows the set`` () =
    let c =
        { baseCorrection with
            Predicate = Some (Predicate.IsNull (nm "CustomerId"))
            Derivation = DataCorrectionDerivationSpec.SameRowAttribute (AttributeCoordinate.create "Sales" "Account" "LegacyCustomerId")
            Guards = [ DataCorrectionGuard.SourceIsNotNull ] }
    let o = applyOk [ c ]
    // matched a1,a3,a4 (CustomerId null); changeable a1,a4 (legacy not null); a3 stays null
    Assert.Equal(Some "C1", cellOf o accountKey [ "Account"; "R"; "a1" ] "CustomerId")
    Assert.Equal(Some "C9", cellOf o accountKey [ "Account"; "R"; "a4" ] "CustomerId")
    Assert.Equal(None, cellOf o accountKey [ "Account"; "R"; "a3" ] "CustomerId")
    let r = List.exactlyOne o.Receipts
    Assert.Equal(3L, r.RowsMatched)
    Assert.Equal(2L, r.RowsChanged)
    Assert.Equal(0L, r.RowsExcluded)
    Assert.Equal(DataCorrectionDerivation.SameRowAttribute, r.Derivation)

[<Fact>]
let ``the receipt enumerates the EXACT changed rows — identity + before -> after, length = RowsChanged (no more, no less)`` () =
    let c =
        { baseCorrection with
            Predicate = Some (Predicate.IsNull (nm "CustomerId"))
            Derivation = DataCorrectionDerivationSpec.SameRowAttribute (AttributeCoordinate.create "Sales" "Account" "LegacyCustomerId")
            Guards = [ DataCorrectionGuard.SourceIsNotNull ] }
    let r = List.exactlyOne (applyOk [ c ]).Receipts
    // The enumeration is provably complete: exactly the changed rows, no more, no less.
    Assert.Equal(r.RowsChanged, int64 (List.length r.ChangedRows))
    Assert.Empty r.ExcludedRows
    // Exactly a1, a4 — subject NULL before, the copied legacy value after (C1, C9).
    Assert.All(r.ChangedRows, fun rc -> Assert.Equal(None, rc.Before))
    Assert.Equal<string option list>([ Some "C1"; Some "C9" ], r.ChangedRows |> List.map (fun rc -> rc.After) |> List.sort)
    Assert.Equal(2, r.ChangedRows |> List.map (fun rc -> rc.RowIdentity) |> List.distinct |> List.length)

[<Fact>]
let ``the receipt enumerates the EXACT excluded rows — length = RowsExcluded, no value carried after`` () =
    let c =
        { baseCorrection with
            Predicate = Some (Predicate.IsNull (nm "CustomerId"))
            Derivation = DataCorrectionDerivationSpec.ExcludeRows }
    let r = List.exactlyOne (applyOk [ c ]).Receipts
    Assert.True(r.RowsExcluded > 0L)
    Assert.Equal(r.RowsExcluded, int64 (List.length r.ExcludedRows))
    Assert.Empty r.ChangedRows
    Assert.All(r.ExcludedRows, fun rc -> Assert.Equal(None, rc.After))
    // Distinct identities — one entry per excluded row.
    Assert.Equal(List.length r.ExcludedRows, r.ExcludedRows |> List.map (fun rc -> rc.RowIdentity) |> List.distinct |> List.length)

[<Fact>]
let ``source-references-existing-target excludes rows whose copied value is absent from the key set`` () =
    let c =
        { baseCorrection with
            Predicate = Some (Predicate.IsNull (nm "CustomerId"))
            Derivation = DataCorrectionDerivationSpec.SameRowAttribute (AttributeCoordinate.create "Sales" "Account" "LegacyCustomerId")
            Guards = [ DataCorrectionGuard.SourceIsNotNull; DataCorrectionGuard.SourceReferencesExistingTarget ]
            ReferencedEntity = Some (EntityCoordinate.create "Sales" "Customer") }
    let o = applyOk [ c ]
    // C9 is absent from the customer key set → a4 is NOT changed
    Assert.Equal(Some "C1", cellOf o accountKey [ "Account"; "R"; "a1" ] "CustomerId")
    Assert.Equal(None, cellOf o accountKey [ "Account"; "R"; "a4" ] "CustomerId")
    Assert.Equal(1L, (List.exactlyOne o.Receipts).RowsChanged)

[<Fact>]
let ``expected-finding-count mismatch refuses by name`` () =
    let c =
        { baseCorrection with
            Predicate = Some (Predicate.IsNull (nm "CustomerId"))
            Derivation = DataCorrectionDerivationSpec.SameRowAttribute (AttributeCoordinate.create "Sales" "Account" "LegacyCustomerId")
            Guards = [ DataCorrectionGuard.ExpectedFindingCount ]
            ExpectedCount = Some 5L }
    Assert.Equal("dataCorrection.expectedCount.mismatch", applyErr [ c ])

[<Fact>]
let ``expected-finding-count match passes`` () =
    let c =
        { baseCorrection with
            Predicate = Some (Predicate.IsNull (nm "CustomerId"))
            Derivation = DataCorrectionDerivationSpec.SameRowAttribute (AttributeCoordinate.create "Sales" "Account" "LegacyCustomerId")
            Guards = [ DataCorrectionGuard.ExpectedFindingCount ]
            ExpectedCount = Some 3L }
    let o = applyOk [ c ]
    Assert.Equal(3L, (List.exactlyOne o.Receipts).RowsMatched)

[<Fact>]
let ``constant literal fills every null target`` () =
    let c =
        { baseCorrection with
            Subject = AttributeCoordinate.create "Sales" "Account" "OwnerName"
            Predicate = Some (Predicate.IsNull (nm "OwnerName"))
            Derivation = DataCorrectionDerivationSpec.ConstantLiteral "MIGRATED"
            Guards = [ DataCorrectionGuard.TargetIsNull ] }
    let o = applyOk [ c ]
    Assert.Equal(Some "MIGRATED", cellOf o accountKey [ "Account"; "R"; "a1" ] "OwnerName")
    Assert.Equal(4L, (List.exactlyOne o.Receipts).RowsChanged)

[<Fact>]
let ``sentinel-exists passes when the constant is in the target key set, refuses when absent`` () =
    let present =
        { baseCorrection with
            Predicate = Some (Predicate.IsNull (nm "CustomerId"))
            Derivation = DataCorrectionDerivationSpec.ConstantLiteral "C1"
            Guards = [ DataCorrectionGuard.TargetIsNull; DataCorrectionGuard.SentinelExists ]
            ReferencedEntity = Some (EntityCoordinate.create "Sales" "Customer") }
    Assert.Equal(3L, (List.exactlyOne (applyOk [ present ]).Receipts).RowsChanged)
    let absent = { present with Derivation = DataCorrectionDerivationSpec.ConstantLiteral "C9" }
    Assert.Equal("dataCorrection.sentinel.absent", applyErr [ absent ])

[<Fact>]
let ``parent-derived recovery copies the parent attribute through the relationship`` () =
    let c =
        { baseCorrection with
            Subject = AttributeCoordinate.create "Sales" "Account" "OwnerName"
            Predicate = Some (Predicate.IsNull (nm "OwnerName"))
            Derivation = DataCorrectionDerivationSpec.ParentAttribute ("CustRef", AttributeCoordinate.create "Sales" "Customer" "Name")
            Guards = [ DataCorrectionGuard.ParentExists; DataCorrectionGuard.ParentSourceIsNotNull ] }
    let o = applyOk [ c ]
    // only a2 has a resolvable parent (CustomerId = C1 → Acme)
    Assert.Equal(Some "Acme", cellOf o accountKey [ "Account"; "R"; "a2" ] "OwnerName")
    Assert.Equal(None, cellOf o accountKey [ "Account"; "R"; "a1" ] "OwnerName")
    Assert.Equal(1L, (List.exactlyOne o.Receipts).RowsChanged)

[<Fact>]
let ``exclude-rows removes the malformed rows and emits an exclusion receipt`` () =
    let c =
        { baseCorrection with
            Predicate = Some (Predicate.And [ Predicate.IsNull (nm "CustomerId"); Predicate.IsNull (nm "LegacyCustomerId") ])
            Derivation = DataCorrectionDerivationSpec.ExcludeRows
            Guards = [ DataCorrectionGuard.NoFormalInboundReferences ] }
    let o = applyOk [ c ]
    Assert.Equal(3, countOf o accountKey) // a3 removed
    let r = List.exactlyOne o.Receipts
    Assert.Equal(1L, r.RowsExcluded)
    Assert.Equal(0L, r.RowsChanged)

[<Fact>]
let ``exclude-rows refuses when a retained row formally references the excluded row`` () =
    // a2 references C1; excluding Customer C1 with the formal inbound guard refuses
    let excludeReferenced =
        { baseCorrection with
            Subject = AttributeCoordinate.create "Sales" "Customer" "Id"
            Predicate = Some (Predicate.Equals (nm "Id", "C1"))
            Derivation = DataCorrectionDerivationSpec.ExcludeRows
            Guards = [ DataCorrectionGuard.NoFormalInboundReferences ] }
    Assert.Equal("dataCorrection.exclude.inboundReference", applyErr [ excludeReferenced ])
    // C2 is referenced by nobody → excluding it passes
    let excludeUnreferenced = { excludeReferenced with Predicate = Some (Predicate.Equals (nm "Id", "C2")) }
    Assert.Equal(1L, (List.exactlyOne (applyOk [ excludeUnreferenced ]).Receipts).RowsExcluded)

[<Fact>]
let ``unknown subject refuses by name`` () =
    let c = { baseCorrection with Subject = AttributeCoordinate.create "Sales" "Account" "NoSuchColumn"
                                  Derivation = DataCorrectionDerivationSpec.ConstantLiteral "X" }
    Assert.Equal("dataCorrection.subject.unresolved", applyErr [ c ])

[<Fact>]
let ``requiresTwoPhase is true only when an enabled parent-derived correction is present`` () =
    let parent = { baseCorrection with Derivation = DataCorrectionDerivationSpec.ParentAttribute ("CustRef", AttributeCoordinate.create "Sales" "Customer" "Name") }
    let sameRow = { baseCorrection with Derivation = DataCorrectionDerivationSpec.SameRowAttribute (AttributeCoordinate.create "Sales" "Account" "LegacyCustomerId") }
    Assert.True(ApprovedDataCorrections.requiresTwoPhase [ parent ])
    Assert.False(ApprovedDataCorrections.requiresTwoPhase [ sameRow ])
    Assert.False(ApprovedDataCorrections.requiresTwoPhase [ { parent with Enabled = false } ])

// -- the row-fidelity reconciliation law (receipt-bounded replay) ------------

let private mkReceipt (id: string) (changed: int64) (excluded: int64) : DataCorrectionReceipt =
    { CorrectionId = id
      SourceRemediationId = None
      Subject = AttributeCoordinate.create "M" "E" "A"
      Derivation = DataCorrectionDerivation.ConstantLiteral
      GuardResults = []
      RowsMatched = changed + excluded
      RowsChanged = changed
      RowsExcluded = excluded
      ChangedRows = []
      ExcludedRows = []
      BeforeDigest = None
      AfterDigest = None
      EvidenceColumns = []
      EvidenceDigest = None
      ApprovedBy = None
      ApprovedAt = None }

[<Fact>]
let ``reconcile of no receipts is the no-correction proof (Ok)`` () =
    match ApprovedDataCorrections.reconcile [] [] with
    | Ok () -> ()
    | Error es -> Assert.Fail(sprintf "expected Ok; got %A" es)

[<Fact>]
let ``reconcile passes when recorded and replayed counts agree (order-insensitive)`` () =
    let recorded = [ mkReceipt "c1" 105L 0L; mkReceipt "c2" 0L 12L ]
    let replayed = [ mkReceipt "c2" 0L 12L; mkReceipt "c1" 105L 0L ]
    match ApprovedDataCorrections.reconcile recorded replayed with
    | Ok () -> ()
    | Error es -> Assert.Fail(sprintf "expected Ok; got %A" es)

[<Fact>]
let ``reconcile fails by name on a rows-changed count mismatch (105 vs 104)`` () =
    match ApprovedDataCorrections.reconcile [ mkReceipt "c1" 105L 0L ] [ mkReceipt "c1" 104L 0L ] with
    | Ok () -> Assert.Fail "expected mismatch"
    | Error es -> Assert.Equal("dataCorrection.fidelity.receiptMismatch", (List.head es).Code)

[<Fact>]
let ``reconcile fails when a recorded receipt is not reproduced by the replay`` () =
    match ApprovedDataCorrections.reconcile [ mkReceipt "c1" 5L 0L ] [] with
    | Ok () -> Assert.Fail "expected mismatch"
    | Error es -> Assert.Equal("dataCorrection.fidelity.receiptMismatch", (List.head es).Code)

[<Fact>]
let ``a value correction records before/after digests for the changed rows`` () =
    let c =
        { baseCorrection with
            Predicate = Some (Predicate.IsNull (nm "CustomerId"))
            Derivation = DataCorrectionDerivationSpec.ConstantLiteral "SENTINEL"
            Guards = [ DataCorrectionGuard.TargetIsNull ] }
    let r = List.exactlyOne (applyOk [ c ]).Receipts
    Assert.True(Option.isSome r.BeforeDigest)
    Assert.True(Option.isSome r.AfterDigest)
    Assert.NotEqual<string option>(r.BeforeDigest, r.AfterDigest)

// -- item 1: configured reference probes correlate to the excluded PKs --------

[<Fact>]
let ``configured reference probe refuses exclusion only when a retained row references an excluded PK`` () =
    let excludeC1 =
        { baseCorrection with
            Subject = AttributeCoordinate.create "Sales" "Customer" "Id"
            Predicate = Some (Predicate.Equals (nm "Id", "C1"))
            Derivation = DataCorrectionDerivationSpec.ExcludeRows
            Guards = [ DataCorrectionGuard.NoConfiguredReferenceMatches ]
            ConfiguredProbes = [ { ReferencingAttribute = AttributeCoordinate.create "Sales" "Account" "CustomerId" } ] }
    // a2.CustomerId = C1 references the excluded row → refuse by name
    Assert.Equal("dataCorrection.exclude.configuredReferenceMatch", applyErr [ excludeC1 ])
    // C2 is referenced by no retained row → excluding it passes (the probe does
    // not fire merely because the referencing table is non-empty)
    let excludeC2 = { excludeC1 with Predicate = Some (Predicate.Equals (nm "Id", "C2")) }
    Assert.Equal(1L, (List.exactlyOne (applyOk [ excludeC2 ]).Receipts).RowsExcluded)

// -- item 4: evidence columns are non-inert (recorded + digested, fail-closed) --

[<Fact>]
let ``evidence columns are recorded on the receipt with a digest`` () =
    let c =
        { baseCorrection with
            Subject = AttributeCoordinate.create "Sales" "Account" "OwnerName"
            Predicate = Some (Predicate.IsNull (nm "OwnerName"))
            Derivation = DataCorrectionDerivationSpec.ConstantLiteral "X"
            Guards = [ DataCorrectionGuard.TargetIsNull ]
            EvidenceColumns = [ AttributeCoordinate.create "Sales" "Account" "LegacyCustomerId" ] }
    let r = List.exactlyOne (applyOk [ c ]).Receipts
    Assert.Equal<AttributeCoordinate list>([ AttributeCoordinate.create "Sales" "Account" "LegacyCustomerId" ], r.EvidenceColumns)
    Assert.True(Option.isSome r.EvidenceDigest)

[<Fact>]
let ``an unresolved evidence column refuses fail-closed`` () =
    let c =
        { baseCorrection with
            Predicate = Some (Predicate.IsNull (nm "CustomerId"))
            Derivation = DataCorrectionDerivationSpec.ConstantLiteral "X"
            Guards = [ DataCorrectionGuard.TargetIsNull ]
            EvidenceColumns = [ AttributeCoordinate.create "Sales" "Account" "NoSuchEvidence" ] }
    Assert.Equal("dataCorrection.evidence.unresolved", applyErr [ c ])
