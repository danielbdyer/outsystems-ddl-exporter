module Projection.Tests.GoldenCatalog

// ---------------------------------------------------------------------
// THE PLATONIC CATALOG (THE_GOLDEN_EMISSION.md §4) — one contrived
// catalog deliberately containing every emission-relevant variance the
// engine can express today. Authored through the production smart
// constructors so it carries the same invariants as the forward path.
// It is not a realistic estate; it is the COMPLETE estate.
//
// Organized as themed modules so the golden files group by theme:
//   Forms     — types, defaults, keys, checks, triggers, indexes
//   Relations — every reference variance (the CreatedBy/UpdatedBy →
//               User corporate shape included)
//   Statics   — data-lane variances (rows, deferred-FK cycle,
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
// Forms module
// ---------------------------------------------------------------------

/// Every primitive-type realization + length/precision/scale carriage +
/// the three DEFAULT shapes (unnamed, named, empty-string-Text — the
/// `EmptyTextNormalizedToNull` tolerance) + table/column descriptions.
let private typeGallery : Kind =
    { Kind.create (kkey "TypeGallery") (nm "TypeGallery")
        (table "dbo" "GOLD_TYPE_GALLERY")
        [ pkAttr (akey "TypeGallery.Id") "Id" "ID" true
          { attr (akey "TypeGallery.Label") "Label" "LABEL" Text false with
              Length = Some 100
              Description = Some "A bounded text column." }
          { attr (akey "TypeGallery.Notes") "Notes" "NOTES" Text true with
              Length = Some 2000
              // Unnamed empty-string Text default — the named
              // EmptyTextNormalizedToNull tolerance (renders DEFAULT NULL).
              DefaultValue = Some (SqlLiteral.TextLit "") }
          { attr (akey "TypeGallery.Amount") "Amount" "AMOUNT" Decimal true with
              Precision = Some 18
              Scale = Some 4
              // Unnamed inline default.
              DefaultValue = Some (SqlLiteral.DecimalLit "0.0") }
          { attr (akey "TypeGallery.IsActive") "IsActive" "IS_ACTIVE" Boolean false with
              // Named DEFAULT constraint.
              DefaultValue = Some (SqlLiteral.BooleanLit true)
              DefaultName  = Some (nm "DF_TypeGallery_IsActive") }
          attr (akey "TypeGallery.OccurredOn") "OccurredOn" "OCCURRED_ON" DateTime true
          attr (akey "TypeGallery.DueDate") "DueDate" "DUE_DATE" Date true
          attr (akey "TypeGallery.AlarmAt") "AlarmAt" "ALARM_AT" Time true
          { attr (akey "TypeGallery.Payload") "Payload" "PAYLOAD" Binary true with
              Length = Some 512 }
          attr (akey "TypeGallery.ExternalKey") "ExternalKey" "EXTERNAL_KEY" Guid true ]
      with
        Description = Some "The type gallery: every primitive realization." }

/// PK-less heap — `allowMissingPrimaryKey` shape.
let private heap : Kind =
    Kind.create (kkey "Heap") (nm "Heap")
        (table "dbo" "GOLD_HEAP")
        [ attr (akey "Heap.LoggedAt") "LoggedAt" "LOGGED_AT" DateTime false
          { attr (akey "Heap.Message") "Message" "MESSAGE" Text true with Length = Some 500 } ]

/// CHECK constraints (named + unnamed) and a trigger.
let private guarded : Kind =
    { Kind.create (kkey "Guarded") (nm "Guarded")
        (table "dbo" "GOLD_GUARDED")
        [ pkAttr (akey "Guarded.Id") "Id" "ID" true
          attr (akey "Guarded.Qty") "Qty" "QTY" Integer false ]
      with
        ColumnChecks =
            [ { SsKey = ckey "Guarded.QtyNonNegative"
                Name = Some (nm "CK_Guarded_Qty")
                Definition = "([QTY]>=(0))"
                IsNotTrusted = false }
              { SsKey = ckey "Guarded.QtyCeiling"
                Name = None
                Definition = "([QTY]<=(1000000))"
                IsNotTrusted = true } ]
        Triggers =
            [ { SsKey = tkey "Guarded.Audit"
                Name = nm "TRG_Guarded_Audit"
                IsDisabled = false
                Definition = "CREATE TRIGGER [dbo].[TRG_Guarded_Audit] ON [dbo].[GOLD_GUARDED] AFTER INSERT AS BEGIN SET NOCOUNT ON; END" } ] }

/// The index gallery — every index variance on one kind.
let private indexGallery : Kind =
    let idA = akey "IndexGallery.Id"
    let aA  = akey "IndexGallery.Alpha"
    let bA  = akey "IndexGallery.Beta"
    let cA  = akey "IndexGallery.Gamma"
    let ix (k: SsKey) (n: string) (cols: SsKey list) = Index.ofKeyColumns k (nm n) cols
    { Kind.create (kkey "IndexGallery") (nm "IndexGallery")
        (table "dbo" "GOLD_INDEX_GALLERY")
        [ pkAttr idA "Id" "ID" true
          { attr aA "Alpha" "ALPHA" Text true with Length = Some 50 }
          attr bA "Beta" "BETA" Integer true
          attr cA "Gamma" "GAMMA" Integer true ]
      with
        Indexes =
            [ ix (ikey "IG.Plain") "IX_IndexGallery_Alpha" [ aA ]
              { ix (ikey "IG.Unique") "UIX_IndexGallery_Beta" [ bA ] with
                  Uniqueness = Unique }
              // Platform-auto (present in `default`; pruned in the
              // `pruned-platform-auto` scenario).
              { ix (ikey "IG.PlatformAuto") "OSIDX_GOLD_INDEX_GALLERY_GAMMA" [ cA ] with
                  IsPlatformAuto = true }
              { ix (ikey "IG.Filtered") "IX_IndexGallery_Beta_Filtered" [ bA ] with
                  Filter = Some "([BETA] IS NOT NULL)" }
              { ix (ikey "IG.Covering") "IX_IndexGallery_Alpha_Covering" [ aA ] with
                  IncludedColumns = [ cA ] }
              { Index.create (ikey "IG.Desc") (nm "IX_IndexGallery_Beta_Desc")
                  [ IndexColumn.create bA Descending ] with
                  ExtendedProperties =
                      [ match ExtendedProperty.create "MS_Description" (Some "Descending scan support.") with
                        | Ok p -> p | Error e -> failwithf "extprop: %A" e ] }
              { ix (ikey "IG.Tuned") "UIX_IndexGallery_Gamma_Tuned" [ cA ] with
                  Uniqueness = Unique
                  FillFactor = Some 80
                  IsPadded = true
                  IgnoreDuplicateKey = true
                  DataCompression = Some DataCompressionLevel.Page }
              { ix (ikey "IG.Disabled") "IX_IndexGallery_Alpha_Disabled" [ aA ] with
                  IsDisabled = true } ] }

// ---------------------------------------------------------------------
// Relations module
// ---------------------------------------------------------------------

let private userKindKey = kkey "User"

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

/// The corporate shape: two source-backed forward references to one
/// target. CreatedBy: trusted, ON DELETE NoAction, explicit ON UPDATE
/// Cascade (the V2 superset over V1). UpdatedBy: UNTRUSTED — drives the
/// NOCHECK two-step ALTER pair.
let private task : Kind =
    let createdBy = akey "Task.CreatedBy"
    let updatedBy = akey "Task.UpdatedBy"
    { Kind.create (kkey "Task") (nm "Task")
        (table "dbo" "GOLD_TASK")
        [ pkAttr (akey "Task.Id") "Id" "ID" true
          { attr (akey "Task.Title") "Title" "TITLE" Text false with Length = Some 200 }
          attr createdBy "CreatedBy" "CREATED_BY" Integer false
          attr updatedBy "UpdatedBy" "UPDATED_BY" Integer true ]
      with
        References =
            [ { (Reference.create (refk "Task.CreatedBy") (nm "User") createdBy userKindKey
                 |> Reference.withConstraintState true true)
                with OnUpdate = Some Cascade }
              (Reference.create (refk "Task.UpdatedBy") (nm "User") updatedBy userKindKey
               |> Reference.withConstraintState true false) ] }

let private customerKindKey = kkey "Customer"

let private customer : Kind =
    Kind.create customerKindKey (nm "Customer")
        (table "dbo" "GOLD_CUSTOMER")
        [ pkAttr (akey "Customer.Id") "Id" "ID" true
          { attr (akey "Customer.Name") "Name" "NAME" Text false with Length = Some 120 } ]

/// Logical-only reference (HasDbConstraint=false — the OutSystems
/// model edge with no storage constraint) with ON DELETE Cascade; and
/// a SetNull sibling.
let private order : Kind =
    let custFk = akey "Order.CustomerId"
    let altFk  = akey "Order.AltCustomerId"
    { Kind.create (kkey "Order") (nm "Order")
        (table "dbo" "GOLD_ORDER")
        [ pkAttr (akey "Order.Id") "Id" "ID" true
          attr custFk "CustomerId" "CUSTOMER_ID" Integer false
          attr altFk "AltCustomerId" "ALT_CUSTOMER_ID" Integer true ]
      with
        References =
            [ { Reference.create (refk "Order.Customer") (nm "Customer") custFk customerKindKey
                  with OnDelete = Cascade }
              { Reference.create (refk "Order.AltCustomer") (nm "Customer") altFk customerKindKey
                  with OnDelete = SetNull } ] }

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
        Values = Map.ofList [ nm "Id", "1"; nm "Code", "US"; nm "Label", "United States" ] }
      { Identifier = rowk "Country.CA"
        Values = Map.ofList [ nm "Id", "2"; nm "Code", "CA"; nm "Label", "Canada" ] }
      { Identifier = rowk "Country.MX"
        Values = Map.ofList [ nm "Id", "3"; nm "Code", "MX"; nm "Label", "Mexico" ] } ]

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
                    Values = Map.ofList [ nm "Id", "1"; nm "Name", "North"; nm "PartnerId", "1" ] } ] ] }

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
                    Values = Map.ofList [ nm "Id", "1"; nm "Name", "South"; nm "PartnerId", "1" ] } ] ] }

/// Static kind carrying the delete-scope gate column (TENANT_ID) —
/// the `delete-scope` scenario adds the `WHEN NOT MATCHED BY SOURCE …
/// DELETE` arm here and ONLY here.
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
                    Values = Map.ofList [ nm "Id", "1"; nm "TenantId", "42"; nm "Value", "Alpha" ] }
                  { Identifier = rowk "ScopedLookup.B"
                    Values = Map.ofList [ nm "Id", "2"; nm "TenantId", "42"; nm "Value", "Beta" ] } ] ] }

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
            [ mkModule (mkey "Forms")     "Forms"     [ typeGallery; heap; guarded; indexGallery ]
              mkModule (mkey "Relations") "Relations" [ user; task; customer; order; changeLog ]
              mkModule (mkey "Statics")   "Statics"   [ country; regionA; regionB; scopedLookup ] ]
            []
    with
    | Ok c -> c
    | Error e -> failwithf "GoldenCatalog.catalog: %A" e
