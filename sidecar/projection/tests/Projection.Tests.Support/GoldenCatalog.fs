module Projection.Tests.GoldenCatalog

// ---------------------------------------------------------------------
// THE PLATONIC CATALOG (THE_GOLDEN_EMISSION.md §4) — one contrived
// catalog deliberately containing every emission-relevant variance the
// engine can express today. Authored through the production smart
// constructors so it carries the same invariants as the forward path.
// It is not a realistic estate; it is the COMPLETE estate.
//
// Consolidation discipline (operator blessing #1, DECISIONS
// 2026-06-13): MANY ATTRIBUTES, FEW TABLES — every variety enumerated
// on master tables rather than spread across a suite:
//   Forms     — ScalarGallery (every scalar × its DEFAULT literal,
//               named/unnamed DEFAULTs, checks, trigger, the full
//               index gallery) + Heap (PK-less)
//   Relations — Engagement (every reference variance on one table,
//               including the self-referencing FK) + the pure targets
//               User / Customer + ChangeLog (cross-schema)
//   Statics   — the data-lane variances (rows, deferred-FK cycle,
//               delete-scope column)
//
// Growth rule: a new emission capability lands with its variance HERE
// and its inventory row in THE_GOLDEN_EMISSION.md, in the same commit.
// ---------------------------------------------------------------------

open Projection.Core

let private nm (s: string) : Name =
    match Name.create s with | Ok n -> n | Error e -> failwithf "name %s: %A" s e

let private key (kind: string) (s: string) : SsKey =
    match SsKey.synthesized kind s with | Ok k -> k | Error e -> failwithf "key %s: %A" s e

let private kkey  = key "GOLD_KIND"
let private akey  = key "GOLD_ATTR"
let private refk  = key "GOLD_REF"
let private ikey  = key "GOLD_IDX"
let private ckey  = key "GOLD_CHK"
let private tkey  = key "GOLD_TRG"
let private rowk  = key "GOLD_ROW"
let private mkey  = key "GOLD_MOD"

let private col (name: string) (nullable: bool) : ColumnRealization =
    match ColumnRealization.create name nullable with
    | Ok c -> c | Error e -> failwithf "col %s: %A" name e

let private table (schema: string) (t: string) : TableId =
    match TableId.create schema t with
    | Ok x -> x | Error e -> failwithf "table %s: %A" t e

/// Base attribute: logical name, physical column, type, nullability.
let private attr (k: SsKey) (logical: string) (physical: string) (ptype: PrimitiveType) (nullable: bool) : Attribute =
    { Attribute.create k (nm logical) ptype with
        Column      = col physical nullable
        IsMandatory = not nullable }

let private pkAttr (k: SsKey) (logical: string) (physical: string) (identity: bool) : Attribute =
    { attr k logical physical Integer false with
        IsPrimaryKey = true
        IsIdentity   = identity }

// ---------------------------------------------------------------------
// Forms module — ScalarGallery: the master scalar/DEFAULT/check/
// trigger/index table. Physical column names are OSSYS-flavored
// (UPPER_SNAKE) so the logical substitution — and its v2 follow-through
// into CHECK definitions and index FILTER predicates — is visible.
// ---------------------------------------------------------------------

let private scalarGallery : Kind =
    let idA      = akey "ScalarGallery.Id"
    let codeA    = akey "ScalarGallery.Code"
    let notesA   = akey "ScalarGallery.Notes"
    let tallyA   = akey "ScalarGallery.Tally"
    let amountA  = akey "ScalarGallery.Amount"
    let ix (k: SsKey) (n: string) (cols: SsKey list) = Index.ofKeyColumns k (nm n) cols
    { Kind.create (kkey "ScalarGallery") (nm "ScalarGallery")
        (table "dbo" "GOLD_SCALAR_GALLERY")
        [ pkAttr idA "Id" "ID" true
          // Text + NAMED DEFAULT.
          { attr codeA "Code" "CODE" Text false with
              Length = Some 20
              DefaultValue = Some (SqlLiteral.TextLit "Pending")
              DefaultName  = Some (nm "DF_ScalarGallery_Code")
              Description  = Some "Workflow code; defaults to Pending." }
          // Text + EMPTY-STRING DEFAULT — the platform shape for optional
          // Text (`DEFAULT ('')`); renders `DEFAULT N''` (the constraint
          // plane always carried `TextLit ""` faithfully — WP-3/F11 fixed
          // the DATA plane to match).
          { attr notesA "Notes" "NOTES" Text true with
              Length = Some 2000
              DefaultValue = Some (SqlLiteral.TextLit "") }
          // Integer + unnamed DEFAULT.
          { attr tallyA "Tally" "TALLY" Integer true with
              DefaultValue = Some (SqlLiteral.IntegerLit "42") }
          // Decimal p,s + unnamed DEFAULT.
          { attr amountA "Amount" "AMOUNT" Decimal true with
              Precision = Some 18
              Scale = Some 4
              DefaultValue = Some (SqlLiteral.DecimalLit "3.1400") }
          // Boolean + NAMED DEFAULT.
          { attr (akey "ScalarGallery.IsActive") "IsActive" "IS_ACTIVE" Boolean false with
              DefaultValue = Some (SqlLiteral.BooleanLit true)
              DefaultName  = Some (nm "DF_ScalarGallery_IsActive") }
          // DateTime / Date / Time — each with its temporal DEFAULT
          // (WP-17(d): the category-bearing variants; rendered as V1's
          // explicit CAST forms).
          { attr (akey "ScalarGallery.OccurredOn") "OccurredOn" "OCCURRED_ON" DateTime true with
              DefaultValue = Some (SqlLiteral.DateTimeLit "2020-01-01 00:00:00") }
          { attr (akey "ScalarGallery.DueDate") "DueDate" "DUE_DATE" Date true with
              DefaultValue = Some (SqlLiteral.DateLit "2020-01-01") }
          { attr (akey "ScalarGallery.AlarmAt") "AlarmAt" "ALARM_AT" Time true with
              DefaultValue = Some (SqlLiteral.TimeLit "08:30:00") }
          // Guid + DEFAULT.
          { attr (akey "ScalarGallery.ExternalKey") "ExternalKey" "EXTERNAL_KEY" Guid true with
              DefaultValue = Some (SqlLiteral.GuidLit "00000000-0000-0000-0000-000000000000") }
          // Binary + DEFAULT.
          { attr (akey "ScalarGallery.Payload") "Payload" "PAYLOAD" Binary true with
              Length = Some 512
              DefaultValue = Some (SqlLiteral.BinaryLit "0x00") }
          // Contrast: no DEFAULT at all.
          { attr (akey "ScalarGallery.FreeText") "FreeText" "FREE_TEXT" Text true with
              Length = Some 50
              Description = Some "No default; the contrast column." } ]
      with
        Description = Some "The scalar gallery: every primitive realization and every DEFAULT-able literal."
        ColumnChecks =
            // Definitions authored with PHYSICAL column references —
            // LogicalColumnEmission v2 rewrites them to the logical
            // names ([TALLY] → [Tally], [AMOUNT] → [Amount]).
            [ { SsKey = ckey "ScalarGallery.TallyNonNegative"
                Name = Some (nm "CK_ScalarGallery_Tally")
                Definition = "([TALLY]>=(0))"
                IsNotTrusted = false }
              { SsKey = ckey "ScalarGallery.AmountCeiling"
                Name = None
                Definition = "([AMOUNT]<=(1000000.0000))"
                IsNotTrusted = true }
              // Multi-column CHECK — references two columns, so it
              // stays at the TABLE level (the faithful placement; the
              // single-column siblings attach beneath their attribute).
              { SsKey = ckey "ScalarGallery.TallyWithinAmount"
                Name = Some (nm "CK_ScalarGallery_TallyWithinAmount")
                Definition = "([TALLY]<=[AMOUNT])"
                IsNotTrusted = false } ]
        Triggers =
            [ { SsKey = tkey "ScalarGallery.Audit"
                Name = nm "TRG_ScalarGallery_Audit"
                IsDisabled = false
                Definition = "CREATE TRIGGER [dbo].[TRG_ScalarGallery_Audit] ON [dbo].[GOLD_SCALAR_GALLERY] AFTER INSERT AS BEGIN SET NOCOUNT ON; END" } ]
        Indexes =
            [ ix (ikey "SG.Plain") "IX_ScalarGallery_Code" [ codeA ]
              { ix (ikey "SG.Unique") "UIX_ScalarGallery_Code" [ codeA ] with
                  Uniqueness = Unique }
              // Platform-auto (present in `default`; pruned in the
              // `pruned-platform-auto` scenario).
              { ix (ikey "SG.PlatformAuto") "OSIDX_GOLD_SCALAR_GALLERY_TALLY" [ tallyA ] with
                  IsPlatformAuto = true }
              // FILTER authored with the PHYSICAL column reference —
              // exercises the v2 filter rewrite ([TALLY] → [Tally]).
              { ix (ikey "SG.Filtered") "IX_ScalarGallery_Tally_Filtered" [ tallyA ] with
                  Filter = Some "([TALLY] IS NOT NULL)" }
              { ix (ikey "SG.Covering") "IX_ScalarGallery_Code_Covering" [ codeA ] with
                  IncludedColumns = [ amountA ] }
              { Index.create (ikey "SG.Desc") (nm "IX_ScalarGallery_Tally_Desc")
                  [ IndexColumn.create tallyA Descending ] with
                  ExtendedProperties =
                      [ match ExtendedProperty.create "MS_Description" (Some "Descending scan support.") with
                        | Ok p -> p | Error e -> failwithf "extprop: %A" e ] }
              { ix (ikey "SG.Tuned") "UIX_ScalarGallery_Amount_Tuned" [ amountA ] with
                  Uniqueness = Unique
                  FillFactor = Some 80
                  IsPadded = true
                  IgnoreDuplicateKey = true
                  DataCompression = Some DataCompressionLevel.Page }
              { ix (ikey "SG.Disabled") "IX_ScalarGallery_Code_Disabled" [ codeA ] with
                  IsDisabled = true } ] }

/// Composite primary key — the TABLE-level 2-line PK shape (single-
/// column PKs attach inline beneath their attribute; composite PKs
/// keep V1's table-level placement).
let private assignment : Kind =
    Kind.create (kkey "Assignment") (nm "Assignment")
        (table "dbo" "GOLD_ASSIGNMENT")
        [ pkAttr (akey "Assignment.ProjectId") "ProjectId" "PROJECT_ID" false
          pkAttr (akey "Assignment.ResourceId") "ResourceId" "RESOURCE_ID" false
          { attr (akey "Assignment.Role") "Role" "ROLE" Text true with Length = Some 40 } ]

/// PK-less heap — `allowMissingPrimaryKey` shape.
let private heap : Kind =
    Kind.create (kkey "Heap") (nm "Heap")
        (table "dbo" "GOLD_HEAP")
        [ attr (akey "Heap.LoggedAt") "LoggedAt" "LOGGED_AT" DateTime false
          { attr (akey "Heap.Message") "Message" "MESSAGE" Text true with Length = Some 500 } ]

// ---------------------------------------------------------------------
// Relations module — Engagement: the master reference table.
// ---------------------------------------------------------------------

let private userKindKey = kkey "User"
let private customerKindKey = kkey "Customer"
let private engagementKey = kkey "Engagement"

/// Pure FK target — the corporate User shape. The inverse-exclusion
/// contract is visible here: NO golden file may carry an FK owned by
/// this kind (negative invariant 3).
let private user : Kind =
    { Kind.create userKindKey (nm "User")
        (table "dbo" "GOLD_USER")
        [ pkAttr (akey "User.Id") "Id" "ID" true
          { attr (akey "User.Email") "Email" "EMAIL" Text false with Length = Some 250 } ]
      with
        Description = Some "The platform user kind (pure reference target)." }

let private customer : Kind =
    Kind.create customerKindKey (nm "Customer")
        (table "dbo" "GOLD_CUSTOMER")
        [ pkAttr (akey "Customer.Id") "Id" "ID" true
          { attr (akey "Customer.Name") "Name" "NAME" Text false with Length = Some 120 } ]

/// Every reference variance on ONE table:
///   CreatedBy  → User       source-backed, trusted, ON UPDATE CASCADE
///   UpdatedBy  → User       source-backed, UNTRUSTED (NOCHECK two-step)
///   CustomerId → Customer   logical-only, ON DELETE CASCADE
///   AltCustomerId → Customer logical-only, ON DELETE SET NULL
///   ParentId   → Engagement SELF-REFERENCING, source-backed, trusted
let private engagement : Kind =
    let createdBy = akey "Engagement.CreatedBy"
    let updatedBy = akey "Engagement.UpdatedBy"
    let custFk    = akey "Engagement.CustomerId"
    let altFk     = akey "Engagement.AltCustomerId"
    let parentFk  = akey "Engagement.ParentId"
    { Kind.create engagementKey (nm "Engagement")
        (table "dbo" "GOLD_ENGAGEMENT")
        [ pkAttr (akey "Engagement.Id") "Id" "ID" true
          { attr (akey "Engagement.Subject") "Subject" "SUBJECT" Text false with Length = Some 200 }
          attr createdBy "CreatedBy" "CREATED_BY" Integer false
          attr updatedBy "UpdatedBy" "UPDATED_BY" Integer true
          attr custFk "CustomerId" "CUSTOMER_ID" Integer false
          // DEFAULT + FK on one column — the constraint STACK (slice 3b).
          { attr altFk "AltCustomerId" "ALT_CUSTOMER_ID" Integer true with
              DefaultValue = Some (SqlLiteral.IntegerLit "0") }
          attr parentFk "ParentId" "PARENT_ID" Integer true ]
      with
        References =
            [ { (Reference.create (refk "Engagement.CreatedBy") (nm "User") createdBy userKindKey
                 |> Reference.withConstraintState true true)
                with OnUpdate = Some Cascade }
              (Reference.create (refk "Engagement.UpdatedBy") (nm "User") updatedBy userKindKey
               |> Reference.withConstraintState true false)
              { Reference.create (refk "Engagement.Customer") (nm "Customer") custFk customerKindKey
                  with OnDelete = Cascade }
              { Reference.create (refk "Engagement.AltCustomer") (nm "Customer") altFk customerKindKey
                  with OnDelete = SetNull }
              (Reference.create (refk "Engagement.Parent") (nm "Engagement") parentFk engagementKey
               |> Reference.withConstraintState true true) ]
        Indexes =
            // Slice 3b — composite indexes: a multi-attribute UNIQUE
            // index and a mixed-direction composite index.
            [ { Index.ofKeyColumns (ikey "EN.CompositeUix") (nm "UIX_Engagement_CustomerId_Subject")
                  [ custFk; akey "Engagement.Subject" ] with
                  Uniqueness = Unique }
              Index.create (ikey "EN.CompositeMixed") (nm "IX_Engagement_CreatedBy_UpdatedByDesc")
                  [ IndexColumn.create createdBy Ascending
                    IndexColumn.create updatedBy Descending ] ] }

let private snapshotKey = kkey "EcrmSnapshot"

/// Long-name pair — the identifier-length budget made VISIBLE: the
/// generated FK name (FK_<Owner>_<Target>_<SourceColumn>) overflows
/// 128 chars and lands as the 115-char head + `_` + 12-hex-hash form.
let private ecrmSnapshot : Kind =
    Kind.create snapshotKey (nm "EnterpriseCustomerRelationshipManagementProfileSnapshot")
        (table "dbo" "GOLD_ECRM_PROFILE_SNAPSHOT")
        [ pkAttr (akey "EcrmSnapshot.Id") "Id" "ID" true ]

let private ledger : Kind =
    let managerFk = akey "Ledger.ManagerId"
    { Kind.create (kkey "Ledger") (nm "InterdepartmentalResourceAllocationAuthorizationLedger")
        (table "dbo" "GOLD_IRAA_LEDGER")
        [ pkAttr (akey "Ledger.Id") "Id" "ID" true
          attr managerFk "PrimaryResponsibleEnterpriseCustomerRelationshipManagerId" "PRIMARY_RESPONSIBLE_ECRM_MANAGER_ID" Integer false ]
      with
        References =
            [ Reference.create (refk "Ledger.Manager") (nm "EnterpriseCustomerRelationshipManagementProfileSnapshot") managerFk snapshotKey
              |> Reference.withConstraintState true true ] }

/// Cross-schema FK: audit.ChangeLog → dbo.GOLD_USER.
let private changeLog : Kind =
    let byUser = akey "ChangeLog.UserId"
    { Kind.create (kkey "ChangeLog") (nm "ChangeLog")
        (table "audit" "GOLD_CHANGE_LOG")
        [ pkAttr (akey "ChangeLog.Id") "Id" "ID" true
          attr byUser "UserId" "USER_ID" Integer false
          attr (akey "ChangeLog.At") "At" "AT" DateTime false ]
      with
        References =
            [ Reference.create (refk "ChangeLog.User") (nm "User") byUser userKindKey
              |> Reference.withConstraintState true true ] }

// ---------------------------------------------------------------------
// Statics module — the data lanes
// ---------------------------------------------------------------------

let private countryKindKey = kkey "Country"

let private countryRows : StaticRow list =
    [ { Identifier = rowk "Country.US"
        Values = StaticRow.presentValues [ nm "Id", "1"; nm "Code", "US"; nm "Label", "United States" ] }
      { Identifier = rowk "Country.CA"
        Values = StaticRow.presentValues [ nm "Id", "2"; nm "Code", "CA"; nm "Label", "Canada" ] }
      { Identifier = rowk "Country.MX"
        Values = StaticRow.presentValues [ nm "Id", "3"; nm "Code", "MX"; nm "Label", "Mexico" ] } ]

/// Static lookup with a non-identity PK and authored rows — the
/// idempotent MERGE seed.
let private country : Kind =
    { Kind.create countryKindKey (nm "Country")
        (table "dbo" "GOLD_COUNTRY")
        [ pkAttr (akey "Country.Id") "Id" "ID" false
          { attr (akey "Country.Code") "Code" "CODE" Text false with Length = Some 2 }
          { attr (akey "Country.Label") "Label" "LABEL" Text false with Length = Some 100 } ]
      with
        Modality = [ Static countryRows ] }

/// WP-3 (F11) — the text-fidelity witness: a static kind whose rows
/// demonstrate the three distinct Text states end-to-end in the emitted
/// seeds: a genuine empty string (`N''`), an explicit NULL, the
/// OutSystems single-space sentinel (`" "` on a nullable Text attribute
/// → NULL, the deliberate V1-parity rule), and an ordinary value.
let private textFidelityKey = kkey "TextFidelity"

let private textFidelityRows : StaticRow list =
    [ { Identifier = rowk "TextFidelity.Empty"
        Values = Map.ofList [ nm "Id", Some "1"; nm "Body", Some "" ] }
      { Identifier = rowk "TextFidelity.Null"
        Values = Map.ofList [ nm "Id", Some "2"; nm "Body", None ] }
      { Identifier = rowk "TextFidelity.Space"
        Values = Map.ofList [ nm "Id", Some "3"; nm "Body", Some " " ] }
      { Identifier = rowk "TextFidelity.Word"
        Values = Map.ofList [ nm "Id", Some "4"; nm "Body", Some "hello" ] } ]

let private textFidelity : Kind =
    { Kind.create textFidelityKey (nm "TextFidelity")
        (table "dbo" "GOLD_TEXT_FIDELITY")
        [ pkAttr (akey "TextFidelity.Id") "Id" "ID" false
          { attr (akey "TextFidelity.Body") "Body" "BODY" Text true with Length = Some 50 } ]
      with
        Modality = [ Static textFidelityRows ] }

let private regionAKey = kkey "RegionA"
let private regionBKey = kkey "RegionB"

/// RegionA ⇄ RegionB: a NULLABLE FK cycle between two static kinds —
/// drives the two-phase realization (Phase-1 MERGE without the deferred
/// FK columns; Phase-2 UPDATE re-points them).
let private regionA : Kind =
    let partner = akey "RegionA.PartnerId"
    { Kind.create regionAKey (nm "RegionA")
        (table "dbo" "GOLD_REGION_A")
        [ pkAttr (akey "RegionA.Id") "Id" "ID" false
          { attr (akey "RegionA.Name") "Name" "NAME" Text false with Length = Some 60 }
          attr partner "PartnerId" "PARTNER_ID" Integer true ]
      with
        References = [ Reference.create (refk "RegionA.Partner") (nm "RegionB") partner regionBKey ]
        Modality =
            [ Static
                [ { Identifier = rowk "RegionA.North"
                    Values = StaticRow.presentValues [ nm "Id", "1"; nm "Name", "North"; nm "PartnerId", "1" ] } ] ] }

let private regionB : Kind =
    let partner = akey "RegionB.PartnerId"
    { Kind.create regionBKey (nm "RegionB")
        (table "dbo" "GOLD_REGION_B")
        [ pkAttr (akey "RegionB.Id") "Id" "ID" false
          { attr (akey "RegionB.Name") "Name" "NAME" Text false with Length = Some 60 }
          attr partner "PartnerId" "PARTNER_ID" Integer true ]
      with
        References = [ Reference.create (refk "RegionB.Partner") (nm "RegionA") partner regionAKey ]
        Modality =
            [ Static
                [ { Identifier = rowk "RegionB.South"
                    Values = StaticRow.presentValues [ nm "Id", "1"; nm "Name", "South"; nm "PartnerId", "1" ] } ] ] }

/// Static kind carrying the delete-scope gate column (logical name
/// `TenantId` post-substitution) — the `delete-scope` scenario adds the
/// `WHEN NOT MATCHED BY SOURCE … DELETE` arm here and ONLY here.
let private scopedLookup : Kind =
    { Kind.create (kkey "ScopedLookup") (nm "ScopedLookup")
        (table "dbo" "GOLD_SCOPED_LOOKUP")
        [ pkAttr (akey "ScopedLookup.Id") "Id" "ID" false
          attr (akey "ScopedLookup.TenantId") "TenantId" "TENANT_ID" Integer false
          { attr (akey "ScopedLookup.Value") "Value" "VALUE" Text false with Length = Some 80 } ]
      with
        Modality =
            [ Static
                [ { Identifier = rowk "ScopedLookup.A"
                    Values = StaticRow.presentValues [ nm "Id", "1"; nm "TenantId", "42"; nm "Value", "Alpha" ] }
                  { Identifier = rowk "ScopedLookup.B"
                    Values = StaticRow.presentValues [ nm "Id", "2"; nm "TenantId", "42"; nm "Value", "Beta" ] } ] ] }

let private tierKindKey = kkey "Tier"

/// Static lookup with an IDENTITY primary key + authored rows — the
/// `IdentityDisposition.AssignedBySink` case (WP6 step 1). Seeding
/// explicit PK values into an IDENTITY column requires the MERGE be
/// bracketed by `SET IDENTITY_INSERT … ON/OFF` (one GO batch). The
/// other statics carry non-identity PKs (`pkAttr … false`), so this is
/// the first static whose seed shows the bracket in the goldens.
let private tier : Kind =
    { Kind.create tierKindKey (nm "Tier")
        (table "dbo" "GOLD_TIER")
        [ pkAttr (akey "Tier.Id") "Id" "ID" true
          { attr (akey "Tier.Name") "Name" "NAME" Text false with Length = Some 40 } ]
      with
        Description = Some "Static lookup with an IDENTITY PK — the IDENTITY_INSERT bracket case."
        Modality =
            [ Static
                [ { Identifier = rowk "Tier.Bronze"
                    Values = StaticRow.presentValues [ nm "Id", "1"; nm "Name", "Bronze" ] }
                  { Identifier = rowk "Tier.Silver"
                    Values = StaticRow.presentValues [ nm "Id", "2"; nm "Name", "Silver" ] }
                  { Identifier = rowk "Tier.Gold"
                    Values = StaticRow.presentValues [ nm "Id", "3"; nm "Name", "Gold" ] } ] ] }

// ---------------------------------------------------------------------
// The catalog
// ---------------------------------------------------------------------

let private mkModule (k: SsKey) (n: string) (kinds: Kind list) : Module =
    { SsKey = k; Name = nm n; Kinds = kinds; IsActive = true; ExtendedProperties = [] }

/// THE Platonic catalog. Deterministic by construction (synthesized
/// SsKeys; no clocks; authored rows).
let catalog : Catalog =
    match
        Catalog.create
            [ mkModule (mkey "Forms")     "Forms"     [ scalarGallery; assignment; heap ]
              mkModule (mkey "Relations") "Relations" [ user; customer; engagement; ecrmSnapshot; ledger; changeLog ]
              mkModule (mkey "Statics")   "Statics"   [ country; textFidelity; regionA; regionB; scopedLookup; tier ] ]
            []
    with
    | Ok c -> c
    | Error e -> failwithf "GoldenCatalog.catalog: %A" e

/// Isolated one-off catalog for the platform-auto-index prune axis
/// (`emission.includePlatformAutoIndexes = false`). That flag is global —
/// all-or-nothing per run — so it cannot fold into the master Platonic
/// catalog; this tiny purpose-built catalog is its STANDALONE one-off
/// emission (DECISIONS 2026-06-13 — maximal master + standalone one-offs).
/// One kind with a platform-auto index (dropped under prune) beside a
/// normal index (kept), so the one-off shows exactly the prune's effect and
/// nothing else rather than re-emitting the whole estate.
let prunePlatformAutoCatalog : Catalog =
    let codeA = akey "PruneProbe.Code"
    let probe : Kind =
        { Kind.create (kkey "PruneProbe") (nm "PruneProbe")
            (table "dbo" "GOLD_PRUNE_PROBE")
            [ pkAttr (akey "PruneProbe.Id") "Id" "ID" true
              { attr codeA "Code" "CODE" Text false with Length = Some 20 } ]
          with
            Description = Some "Prune one-off probe: a platform-auto index beside a normal one."
            Indexes =
                [ Index.ofKeyColumns (ikey "PruneProbe.Code") (nm "IX_PruneProbe_Code") [ codeA ]
                  { Index.ofKeyColumns (ikey "PruneProbe.AutoCode") (nm "OSIDX_GOLD_PRUNE_PROBE_CODE") [ codeA ] with
                      IsPlatformAuto = true } ] }
    match Catalog.create [ mkModule (mkey "PruneForms") "PruneForms" [ probe ] ] [] with
    | Ok c -> c
    | Error e -> failwithf "GoldenCatalog.prunePlatformAutoCatalog: %A" e
